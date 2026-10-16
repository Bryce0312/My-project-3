using System.Collections.Generic;
using LivingRoom30.Measurement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30
{
    /// <summary>
    /// Walks a first-time reviewer through the controls one step at a time. Each step only advances
    /// once the reviewer has actually performed the action, so the panel cannot be dismissed by
    /// reading past it.
    ///
    /// Phase one covers the controls themselves; phase two introduces what the tool produces —
    /// the automatic clearance readout and the annotation panel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialController : MonoBehaviour
    {
        private const float InputDeadzone = 0.2f;
        private const float ProximityRadius = 3.5f;   // matches ProximityMeasurer.observerThreshold
        private const float InformationSeconds = 3.5f;

        private enum StepGoal
        {
            Move,
            Turn,
            ClickPractice,
            ApproachFurniture,
            Informational,
            ToggleGaps,
            Annotate,
        }

        private struct Step
        {
            public string Instruction;
            public StepGoal Goal;
            public string PartName;
            public HighlightTarget Hand;
            public bool ShowBeacon;
        }

        private static readonly Step[] Steps =
        {
            new Step { Instruction = "Push the LEFT stick to walk", Goal = StepGoal.Move, PartName = "ThumbStick", Hand = HighlightTarget.Left },
            new Step { Instruction = "Push the RIGHT stick to turn", Goal = StepGoal.Turn, PartName = "ThumbStick", Hand = HighlightTarget.Right },
            // The trigger faces away from the eyes, so it needs a visible marker to pull the gaze
            // down to the hand before the glow on the trigger itself means anything.
            new Step { Instruction = "Look at your controller and press the TRIGGER", Goal = StepGoal.ClickPractice, PartName = "Trigger", Hand = HighlightTarget.Both, ShowBeacon = true },
            new Step { Instruction = "Walk close to the furniture", Goal = StepGoal.ApproachFurniture },
            new Step { Instruction = "Green numbers are the clearances", Goal = StepGoal.Informational },
            new Step { Instruction = "Press GAPS on your wrist to hide them", Goal = StepGoal.ToggleGaps },
            // The grip sits under the handle, out of sight in the same way the trigger is, so it gets
            // the same treatment: the part itself glows and a beacon floats above the hand.
            // "Bumper" is the model's name for it — the mesh sits lowest and furthest back, exactly
            // where a middle finger closes on the handle.
            new Step { Instruction = "Point at a decoration and press the GRIP to add a note", Goal = StepGoal.Annotate, PartName = "Bumper", Hand = HighlightTarget.Both, ShowBeacon = true },
        };

        [SerializeField] private Text stepLabel;
        [SerializeField] private Button practiceButton;
        [SerializeField] private Button skipButton;
        [SerializeField] private Button helpButton;
        [SerializeField] private ControlHighlighter highlighter;
        [SerializeField] private ActionBasedContinuousMoveProvider moveProvider;
        [SerializeField] private ActionBasedSnapTurnProvider turnProvider;

        private readonly List<FurnitureInfo> furniture = new List<FurnitureInfo>();
        private HudLazyFollow panelFollow;
        private int stepIndex;
        private float stepEnteredAt;

        public Text StepLabel => stepLabel;
        public Button PracticeButton => practiceButton;
        public Button SkipButton => skipButton;
        public Button HelpButton => helpButton;
        public ControlHighlighter Highlighter => highlighter;
        public ActionBasedContinuousMoveProvider MoveProvider => moveProvider;
        public ActionBasedSnapTurnProvider TurnProvider => turnProvider;

        public void Configure(Text stepText, Button practice, Button skip, Button help,
            ControlHighlighter controlHighlighter,
            ActionBasedContinuousMoveProvider movement, ActionBasedSnapTurnProvider turning)
        {
            stepLabel = stepText;
            practiceButton = practice;
            skipButton = skip;
            helpButton = help;
            highlighter = controlHighlighter;
            moveProvider = movement;
            turnProvider = turning;
        }

        private void Awake()
        {
            // Subscribed once: this object is only deactivated when the tutorial finishes, so
            // OnEnable runs again on replay but Awake does not, which keeps the listeners unique.
            if (skipButton != null)
            {
                skipButton.onClick.AddListener(Hide);
            }

            if (helpButton != null)
            {
                helpButton.onClick.AddListener(Show);
            }

            if (practiceButton != null)
            {
                practiceButton.onClick.AddListener(OnPracticePressed);
            }
        }

        private void Start()
        {
            furniture.AddRange(FindObjectsOfType<FurnitureInfo>());
            panelFollow = GetComponent<HudLazyFollow>();
        }

        private void OnEnable()
        {
            stepIndex = 0;
            RefreshStep();
        }

        private void Update()
        {
            if (stepIndex >= Steps.Length)
            {
                return;
            }

            switch (Steps[stepIndex].Goal)
            {
                case StepGoal.Move:
                    if (ReadMovement().magnitude > InputDeadzone)
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.Turn:
                    if (Mathf.Abs(ReadTurn().x) > InputDeadzone)
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.ApproachFurniture:
                    if (IsCloseToFurniture())
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.Informational:
                    // Nothing to do — just give the reviewer time to read it.
                    if (Time.unscaledTime - stepEnteredAt >= InformationSeconds)
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.ToggleGaps:
                    if (!ClearancesVisible())
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.Annotate:
                    if (AnnotationSpawner.currentActiveUI != null)
                    {
                        AdvanceStep();
                    }
                    break;
            }
        }

        public void Show()
        {
            gameObject.SetActive(true);
        }

        private void Hide()
        {
            if (highlighter != null)
            {
                highlighter.Clear();
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// The practice button is only shown for the step that asks for it, so pressing it must not
        /// skip the other steps.
        /// </summary>
        private void OnPracticePressed()
        {
            if (stepIndex < Steps.Length && Steps[stepIndex].Goal == StepGoal.ClickPractice)
            {
                AdvanceStep();
            }
        }

        private void AdvanceStep()
        {
            stepIndex++;
            if (stepIndex >= Steps.Length)
            {
                Hide();
                return;
            }

            RefreshStep();
        }

        private void RefreshStep()
        {
            Step step = Steps[stepIndex];
            stepEnteredAt = Time.unscaledTime;

            if (stepLabel != null)
            {
                stepLabel.text = (stepIndex + 1) + " / " + Steps.Length + "\n" + step.Instruction;
            }

            if (practiceButton != null)
            {
                practiceButton.gameObject.SetActive(step.Goal == StepGoal.ClickPractice);
            }

            if (highlighter == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(step.PartName))
            {
                highlighter.Clear();
            }
            else
            {
                highlighter.Highlight(step.PartName, step.Hand, step.ShowBeacon);
            }
        }

        private Vector3 ObserverPosition()
        {
            // The panel follows the head, so its follow target is the head itself. Falling back to
            // the panel's own position would be off by the follow distance.
            return panelFollow != null && panelFollow.target != null
                ? panelFollow.target.position
                : transform.position;
        }

        private bool IsCloseToFurniture()
        {
            Vector3 observer = ObserverPosition();
            for (int i = 0; i < furniture.Count; i++)
            {
                if (furniture[i] == null)
                {
                    continue;
                }

                if (Vector3.Distance(observer, furniture[i].transform.position) < ProximityRadius)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ClearancesVisible()
        {
            SpatialMeasureManager manager = FindObjectOfType<SpatialMeasureManager>();
            return manager == null || manager.ProximityEnabled;
        }

        private Vector2 ReadMovement()
        {
            return moveProvider != null && moveProvider.leftHandMoveAction.action != null
                ? moveProvider.leftHandMoveAction.action.ReadValue<Vector2>()
                : Vector2.zero;
        }

        private Vector2 ReadTurn()
        {
            return turnProvider != null && turnProvider.rightHandSnapTurnAction.action != null
                ? turnProvider.rightHandSnapTurnAction.action.ReadValue<Vector2>()
                : Vector2.zero;
        }
    }
}
