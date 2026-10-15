using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30
{
    /// <summary>
    /// Walks a first-time reviewer through the controls one step at a time. Each step only advances
    /// once the reviewer has actually performed the action, so the panel cannot be dismissed by
    /// reading past it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialController : MonoBehaviour
    {
        private const float InputDeadzone = 0.2f;

        private struct Step
        {
            public string Instruction;
            public string PartName;
            public HighlightTarget Hand;
            public bool ShowBeacon;
        }

        private static readonly Step[] Steps =
        {
            new Step { Instruction = "Push the LEFT stick to walk", PartName = "ThumbStick", Hand = HighlightTarget.Left },
            new Step { Instruction = "Push the RIGHT stick to turn", PartName = "ThumbStick", Hand = HighlightTarget.Right },
            // The trigger faces away from the eyes, so it needs a visible marker to pull the gaze
            // down to the hand before the glow on the trigger itself means anything.
            new Step { Instruction = "Look at your controller and press the TRIGGER", PartName = "Trigger", Hand = HighlightTarget.Both, ShowBeacon = true },
        };

        [SerializeField] private Text stepLabel;
        [SerializeField] private Button practiceButton;
        [SerializeField] private Button skipButton;
        [SerializeField] private Button helpButton;
        [SerializeField] private ControlHighlighter highlighter;
        [SerializeField] private ActionBasedContinuousMoveProvider moveProvider;
        [SerializeField] private ActionBasedSnapTurnProvider turnProvider;

        private int stepIndex;

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
                practiceButton.onClick.AddListener(AdvanceStep);
            }
        }

        private void OnEnable()
        {
            stepIndex = 0;
            RefreshStep();
        }

        private void Update()
        {
            switch (stepIndex)
            {
                case 0:
                    if (ReadMovement().magnitude > InputDeadzone)
                    {
                        AdvanceStep();
                    }
                    break;
                case 1:
                    if (Mathf.Abs(ReadTurn().x) > InputDeadzone)
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

            if (stepLabel != null)
            {
                stepLabel.text = (stepIndex + 1) + " / " + Steps.Length + "\n" + step.Instruction;
            }

            if (practiceButton != null)
            {
                practiceButton.gameObject.SetActive(stepIndex == Steps.Length - 1);
            }

            if (highlighter != null)
            {
                highlighter.Highlight(step.PartName, step.Hand, step.ShowBeacon);
            }
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
