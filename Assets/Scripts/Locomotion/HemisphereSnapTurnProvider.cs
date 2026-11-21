using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30.Locomotion
{
    /// <summary>
    /// Maps every right/left thumbstick direction to one fixed 30-degree snap, including rear quadrants.
    /// The stick must return to neutral before another turn; holding it never repeats or refines the turn.
    /// </summary>
    [DefaultExecutionOrder(-210)]
    [AddComponentMenu("XR/Locomotion/Hemisphere Snap Turn Provider")]
    public class HemisphereSnapTurnProvider : ActionBasedSnapTurnProvider
    {
        private const float SnapAngle = 30f;
        private const float ReleaseMagnitude = .10f;
        private const float ActivationMagnitude = .25f;
        private const float HorizontalNoiseThreshold = .005f;

        private InputAction rawRightStick;
        private bool centered;
        private bool gestureActive;
        private bool gestureTurnApplied;
        private float gestureSign;
        private float gestureStartedAt;
        private float nextGestureAllowedAt;

        protected override void Awake()
        {
            base.Awake();
            // The Starter Assets Snap Turn binding uses Sector interactions, which discard
            // forward/back diagonal values before a provider can map them. Read the full stick.
            rawRightStick = new InputAction("Mapped Right Snap Turn", InputActionType.Value,
                "<XRController>{RightHand}/Primary2DAxis", processors: "stickDeadzone(min=0.15,max=1)",
                expectedControlType: "Vector2");
            rightHandSnapTurnAction = new InputActionProperty(rawRightStick);
        }

        protected new void OnEnable()
        {
            base.OnEnable();
            ResetGesture(false);
            nextGestureAllowedAt = 0f;
            locomotionPhase = LocomotionPhase.Idle;
        }

        protected new void OnDisable()
        {
            base.OnDisable();
            ResetGesture(false);
            locomotionPhase = LocomotionPhase.Idle;
        }

        private void OnDestroy()
        {
            rawRightStick?.Dispose();
        }

        protected override Vector2 ReadInput()
        {
            return rightHandSnapTurnAction.action?.ReadValue<Vector2>() ?? Vector2.zero;
        }

        /// <summary>
        /// Every deliberate left/right deflection maps to the same fixed snap, regardless of
        /// its bearing or strength. Pure forward/backward and hardware noise do not turn the rig.
        /// </summary>
        public static float MapTurnDegrees(Vector2 input)
        {
            if (input.magnitude < ActivationMagnitude || Mathf.Abs(input.x) <= HorizontalNoiseThreshold)
                return 0f;

            return Mathf.Sign(input.x) * SnapAngle;
        }

        protected override float GetTurnAmount(Vector2 input)
        {
            return enableTurnLeftRight ? MapTurnDegrees(input) : 0f;
        }

        // The package Update repeats a held stick after debounceTime and quantizes to cardinals.
        // Keep its locomotion-system ownership/events, but snap exactly once per gesture.
        protected new void Update()
        {
            if (locomotionPhase == LocomotionPhase.Done)
                locomotionPhase = LocomotionPhase.Idle;

            if (!enableTurnLeftRight)
            {
                ResetGesture(false);
                return;
            }

            Vector2 input = ReadInput();
            if (input.magnitude <= ReleaseMagnitude)
            {
                ResetGesture(true);
                return;
            }

            // A provider resumed after sitting, desktop mode, or a scene reload must see neutral
            // before a held stick is allowed to turn the newly restored rig.
            if (!centered) return;

            if (!gestureActive)
            {
                float target = GetTurnAmount(input);
                if (Mathf.Approximately(target, 0f) || Time.unscaledTime < nextGestureAllowedAt)
                    return;
                gestureActive = true;
                gestureSign = Mathf.Sign(target);
                gestureStartedAt = Time.unscaledTime;
            }

            // Changing direction or pushing harder while held never produces another turn.
            if (gestureTurnApplied || Time.unscaledTime - gestureStartedAt < delayTime || !CanBeginLocomotion())
                return;

            var origin = system != null ? system.xrOrigin : null;
            if (origin == null) return;

            locomotionPhase = LocomotionPhase.Started;
            if (!BeginLocomotion())
            {
                locomotionPhase = LocomotionPhase.Idle;
                return;
            }

            try
            {
                locomotionPhase = LocomotionPhase.Moving;
                origin.RotateAroundCameraUsingOriginUp(gestureSign * SnapAngle);
                gestureTurnApplied = true;
                nextGestureAllowedAt = Time.unscaledTime + Mathf.Max(0f, debounceTime);
            }
            finally
            {
                EndLocomotion();
                locomotionPhase = LocomotionPhase.Done;
            }
        }

        private void ResetGesture(bool isCentered)
        {
            centered = isCentered;
            gestureActive = false;
            gestureTurnApplied = false;
            gestureSign = 0f;
        }
    }
}
