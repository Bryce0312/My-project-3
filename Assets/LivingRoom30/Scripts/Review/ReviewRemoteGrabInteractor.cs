using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30.Review
{
    /// <summary>Remote-only near-hand interaction; never changes the controller's shared Grip binding.</summary>
    public sealed class ReviewRemoteGrabInteractor : XRDirectInteractor
    {
        public const float HoldSeconds = 0.5f;
        public XRGrabInteractable Target { get; set; }
        public Func<bool> IsUiTargeted { get; set; }
        public bool ReadyToGrab { get; private set; }
        public float HoldProgress => Mathf.Clamp01(heldSeconds / HoldSeconds);
        public bool OwnsTriggerPress { get; private set; }
        public bool AwaitRelease { get; private set; }

        private readonly List<IXRInteractable> targets = new List<IXRInteractable>();
        private float heldSeconds;
        private float pressStarted = -1f;
        private bool qualified;

        private bool TriggerPressed => xrController != null && xrController.activateInteractionState.active;
        private bool ControllerTracked => xrController != null && xrController.isActiveAndEnabled &&
            xrController.currentControllerState.isTracked &&
            (xrController.currentControllerState.inputTrackingState &
                (UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation)) ==
                (UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation);

        // base.isSelectActive reads Grip. This interactor alone uses the controller's Trigger state.
        public override bool isSelectActive => allowSelect && ControllerTracked && TriggerPressed &&
            !AwaitRelease && qualified;

        public override void PreprocessInteractor(XRInteractionUpdateOrder.UpdatePhase phase)
        {
            base.PreprocessInteractor(phase);
            if (phase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;

            if (!ControllerTracked || Target == null || !Target.isActiveAndEnabled)
            {
                CancelPress();
                return;
            }
            if (!TriggerPressed)
            {
                AwaitRelease = false;
                OwnsTriggerPress = false;
                heldSeconds = 0f;
                pressStarted = -1f;
                qualified = false;
            }
            if (hasSelection)
            {
                ReadyToGrab = false;
                return; // Keep holding when the hand moves away from the original pickup position.
            }

            // Check UI even when far away: a press that began on UI must finish there.
            if (IsUiTargeted != null && IsUiTargeted())
            {
                if (TriggerPressed) AwaitRelease = true;
                OwnsTriggerPress = false;
                ReadyToGrab = false;
                heldSeconds = 0f;
                pressStarted = -1f;
                qualified = false;
                return;
            }
            targets.Clear();
            GetValidTargets(targets);
            ReadyToGrab = !Target.isSelected && targets.Contains(Target) &&
                interactionManager != null && interactionManager.IsHoverPossible(this, Target);
            if (!ReadyToGrab || AwaitRelease || !TriggerPressed)
            {
                heldSeconds = 0f;
                pressStarted = -1f;
                qualified = false;
                return;
            }

            OwnsTriggerPress = true;
            if (pressStarted < 0f) pressStarted = Time.unscaledTime;
            heldSeconds = Time.unscaledTime - pressStarted;
            qualified = heldSeconds >= HoldSeconds;
        }

        /// <summary>Reset/mode loss cannot pick the object up again until the current press ends.</summary>
        public void CancelPress()
        {
            // Preserve ownership: resetting one hand must not swallow the other hand's UI press.
            OwnsTriggerPress = TriggerPressed && (OwnsTriggerPress || hasSelection);
            AwaitRelease = true; // Only a valid tracked release can clear this latch.
            ReadyToGrab = false;
            qualified = false;
            heldSeconds = 0f;
            pressStarted = -1f;
        }

        protected override void OnDisable()
        {
            CancelPress();
            base.OnDisable();
        }
    }
}
