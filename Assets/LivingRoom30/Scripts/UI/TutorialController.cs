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

        private static readonly string[] StepInstructions =
        {
            "Push the LEFT stick to walk",
            "Push the RIGHT stick to turn",
            "Point at the button and press the TRIGGER",
        };

        [SerializeField] private Text stepLabel;
        [SerializeField] private Button practiceButton;
        [SerializeField] private Button skipButton;
        [SerializeField] private Button helpButton;
        [SerializeField] private ActionBasedContinuousMoveProvider moveProvider;
        [SerializeField] private ActionBasedSnapTurnProvider turnProvider;

        private int stepIndex;

        public Text StepLabel => stepLabel;
        public Button PracticeButton => practiceButton;
        public Button SkipButton => skipButton;
        public Button HelpButton => helpButton;
        public ActionBasedContinuousMoveProvider MoveProvider => moveProvider;
        public ActionBasedSnapTurnProvider TurnProvider => turnProvider;

        public void Configure(Text stepText, Button practice, Button skip, Button help,
            ActionBasedContinuousMoveProvider movement, ActionBasedSnapTurnProvider turning)
        {
            stepLabel = stepText;
            practiceButton = practice;
            skipButton = skip;
            helpButton = help;
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
            gameObject.SetActive(false);
        }

        private void AdvanceStep()
        {
            stepIndex++;
            if (stepIndex >= StepInstructions.Length)
            {
                Hide();
                return;
            }

            RefreshStep();
        }

        private void RefreshStep()
        {
            if (stepLabel != null)
            {
                stepLabel.text = (stepIndex + 1) + " / " + StepInstructions.Length + "\n" + StepInstructions[stepIndex];
            }

            if (practiceButton != null)
            {
                practiceButton.gameObject.SetActive(stepIndex == 2);
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
