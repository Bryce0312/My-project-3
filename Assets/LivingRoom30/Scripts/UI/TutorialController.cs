using System.Collections.Generic;
using LivingRoom30.Locomotion;
using LivingRoom30.Measurement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30
{
    /// <summary>
    /// Walks a first-time reviewer through the controls one step at a time. Each step only advances
    /// once the reviewer has performed the action. Final wrist-menu pages advance explicitly.
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
            Teleport,
            CancelTeleport,
            ClickPractice,
            ApproachFurniture,
            Informational,
            EnableGaps,
            ToggleGaps,
            Annotate,
            WristMenu,
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
            new Step { Instruction = "Push the RIGHT stick left/right to snap 30 degrees", Goal = StepGoal.Turn, PartName = "ThumbStick", Hand = HighlightTarget.Right },
            new Step { Instruction = "Press X to aim, then X to teleport", Goal = StepGoal.Teleport, PartName = "Button_B", Hand = HighlightTarget.Left, ShowBeacon = true },
            new Step { Instruction = "Press X to aim, then press Y to cancel", Goal = StepGoal.CancelTeleport, PartName = "Button_A", Hand = HighlightTarget.Left, ShowBeacon = true },
            // The trigger faces away from the eyes, so it needs a visible marker to pull the gaze
            // down to the hand before the glow on the trigger itself means anything.
            new Step { Instruction = "Look at your controller and press the TRIGGER", Goal = StepGoal.ClickPractice, PartName = "Trigger", Hand = HighlightTarget.Both, ShowBeacon = true },
            new Step { Instruction = "Walk close to the furniture", Goal = StepGoal.ApproachFurniture },
            new Step { Instruction = "GAPS starts OFF\nTap LEFT wrist GAPS to show it", Goal = StepGoal.EnableGaps },
            new Step { Instruction = "Green numbers are the clearances", Goal = StepGoal.Informational },
            new Step { Instruction = "Press GAPS on your wrist to hide them", Goal = StepGoal.ToggleGaps },
            // The grip sits under the handle, out of sight in the same way the trigger is, so it gets
            // the same treatment: the part itself glows and a beacon floats above the hand.
            // "Bumper" is the model's name for it — the mesh sits lowest and furthest back, exactly
            // where a middle finger closes on the handle.
            new Step { Instruction = "Point at a decoration and press the GRIP to add a note", Goal = StepGoal.Annotate, PartName = "Bumper", Hand = HighlightTarget.Both, ShowBeacon = true },
            new Step { Instruction = "GAPS: green distance bars\nOFF by default; tap to toggle", Goal = StepGoal.WristMenu },
            new Step { Instruction = "PAUSE / RESUME: timer only\nHELP: replay this tutorial", Goal = StepGoal.WristMenu },
            new Step { Instruction = "GUIDE: task directions\nEND REVIEW / RESULTS: issue list", Goal = StepGoal.WristMenu },
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
        private bool teleportPerformed;
        private bool teleportCancelled;
        private bool annotationOpened;

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
            LocomotionComfortManager.Teleported += OnTeleported;
            LocomotionComfortManager.TeleportCancelled += OnTeleportCancelled;
        }

        private void OnDisable()
        {
            LocomotionComfortManager.Teleported -= OnTeleported;
            LocomotionComfortManager.TeleportCancelled -= OnTeleportCancelled;
        }

        private void OnTeleported()
        {
            teleportPerformed = true;
        }

        private void OnTeleportCancelled()
        {
            teleportCancelled = true;
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
                    // Hemisphere snap turns accept rear/front diagonals whose horizontal axis
                    // can be below the old tutorial threshold. The provider runs before this
                    // Update, so Done confirms that a turn actually occurred.
                    bool mappedTurnCompleted = turnProvider is HemisphereSnapTurnProvider
                        && turnProvider.locomotionPhase == LocomotionPhase.Done;
                    if (mappedTurnCompleted || Mathf.Abs(ReadTurn().x) > InputDeadzone)
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.Teleport:
                    if (teleportPerformed)
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.CancelTeleport:
                    if (teleportCancelled)
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
                case StepGoal.EnableGaps:
                    if (ClearancesMatch(true))
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.ToggleGaps:
                    if (ClearancesMatch(false))
                    {
                        AdvanceStep();
                    }
                    break;
                case StepGoal.Annotate:
                    if (!annotationOpened && AnnotationSpawner.currentActiveUI != null)
                    {
                        annotationOpened = true;
                        if (stepLabel != null)
                        {
                            stepLabel.text = (stepIndex + 1) + " / " + Steps.Length +
                                "\nSave or close the note to continue";
                        }
                        if (highlighter != null) highlighter.Clear();
                    }
                    else if (annotationOpened && AnnotationSpawner.currentActiveUI == null)
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

        // Lets the scene-authored review journey offer this controls tutorial on demand.
        public void Dismiss() => Hide();

        private void Hide()
        {
            if (highlighter != null)
            {
                highlighter.Clear();
            }

            gameObject.SetActive(false);
        }

        /// <summary>
        /// The action button advances click practice and the final reading pages only.
        /// </summary>
        private void OnPracticePressed()
        {
            if (stepIndex < Steps.Length &&
                (Steps[stepIndex].Goal == StepGoal.ClickPractice || Steps[stepIndex].Goal == StepGoal.WristMenu))
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
            teleportPerformed = false;
            teleportCancelled = false;
            annotationOpened = false;

            if (stepLabel != null)
            {
                stepLabel.text = (stepIndex + 1) + " / " + Steps.Length +
                    (step.Goal == StepGoal.WristMenu ? " - LEFT WRIST MENU" : "") + "\n" + step.Instruction;
            }

            if (practiceButton != null)
            {
                bool menuPage = step.Goal == StepGoal.WristMenu;
                practiceButton.gameObject.SetActive(step.Goal == StepGoal.ClickPractice || menuPage);
                Text buttonLabel = practiceButton.GetComponentInChildren<Text>(true);
                if (buttonLabel != null)
                {
                    buttonLabel.text = menuPage
                        ? (stepIndex == Steps.Length - 1 ? "FINISH" : "NEXT")
                        : "PRESS ME";
                }
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

        private static bool ClearancesMatch(bool enabled)
        {
            SpatialMeasureManager manager = FindObjectOfType<SpatialMeasureManager>();
            return manager != null && manager.ProximityEnabled == enabled;
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
