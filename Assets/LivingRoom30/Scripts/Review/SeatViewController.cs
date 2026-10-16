using System;
using System.Collections;
using LivingRoom30.Measurement;
using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30.Review
{
    /// <summary>
    /// Offers a fixed seated viewpoint while leaving the headset and hands tracked.
    /// Scene references are supplied explicitly; this component does not install itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeatViewController : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float fadeSeconds = 0.12f;

        [SerializeField] private SpatialMeasureManager manager;
        [SerializeField] private Transform seatEye;
        [SerializeField] private Transform exitFloor;
        [SerializeField] private Transform lookAt;

        private Transform sessionOrigin;
        private Camera sessionCamera;
        private Vector3 standingOriginPosition;
        private Quaternion standingOriginRotation;
        private float standingEyeHeight;
        private bool hasStandingSnapshot;
        private bool seatedPoseApplied;
        private bool ownsLocomotionBlock;
        private Coroutine transitionRoutine;

        private Canvas fadeCanvas;
        private CanvasGroup fadeGroup;

        public bool IsSeated { get; private set; }
        public bool IsTransitioning { get; private set; }
        public string LastMessage { get; private set; } = string.Empty;
        public Func<bool> CanTransition { get; set; }
        public event Action<bool> SeatedChanged;

        public void Configure(SpatialMeasureManager manager, Transform seatEye,
            Transform exitFloor, Transform lookAt)
        {
            ResetSession(string.Empty);
            this.manager = manager;
            this.seatEye = seatEye;
            this.exitFloor = exitFloor;
            this.lookAt = lookAt;
        }

        public bool TrySit()
        {
            if (!CanBeginTransition()) return false;
            if (IsSeated)
            {
                LastMessage = "You are already seated.";
                return false;
            }

            if (manager == null || !manager.IsXrMode)
            {
                LastMessage = "Use XR or the XR simulator to try the seated view.";
                return false;
            }

            Camera camera = manager.ActiveCamera;
            Transform origin = manager.XrOriginTransform;
            if (camera == null || origin == null || seatEye == null || exitFloor == null)
            {
                LastMessage = "The seat or XR rig has not been configured.";
                return false;
            }

            if (!camera.transform.IsChildOf(origin) || seatEye.IsChildOf(origin) || exitFloor.IsChildOf(origin))
            {
                LastMessage = "The seat anchors must be outside the tracked XR rig.";
                return false;
            }

            // This living room has a level floor. The authored exit anchor supplies that floor
            // height; preserve the reviewer's actual eye height rather than assuming 1.6 metres.
            float eyeHeight = camera.transform.position.y - exitFloor.position.y;
            if (float.IsNaN(eyeHeight) || float.IsInfinity(eyeHeight) || eyeHeight < 0.25f)
            {
                LastMessage = "Wait for the XR view to finish initializing.";
                return false;
            }

            sessionOrigin = origin;
            sessionCamera = camera;
            standingOriginPosition = origin.position;
            standingOriginRotation = origin.rotation;
            standingEyeHeight = eyeHeight;
            hasStandingSnapshot = true;
            seatedPoseApplied = false;
            BeginTransition(true);
            return true;
        }

        public bool TryStand()
        {
            if (!CanBeginTransition()) return false;
            if (!IsSeated)
            {
                LastMessage = "You are already standing.";
                return false;
            }

            if (!SessionIsValid())
            {
                ResetSession("The seated view ended because XR mode changed.");
                return true;
            }

            BeginTransition(false);
            return true;
        }

        private bool CanBeginTransition()
        {
            if (!Application.isPlaying || !isActiveAndEnabled)
            {
                LastMessage = "The seated view is available while the scene is running.";
                return false;
            }

            if (IsTransitioning)
            {
                LastMessage = "Please wait for the view to finish changing.";
                return false;
            }

            bool allowed = true;
            try
            {
                if (CanTransition != null) allowed = CanTransition();
            }
            catch (Exception exception)
            {
                LastMessage = "The seat is not ready to change views.";
                Debug.LogException(exception, this);
                return false;
            }

            if (!allowed)
            {
                LastMessage = "Put down the held object before sitting or standing.";
                return false;
            }

            return true;
        }

        private void BeginTransition(bool sit)
        {
            IsTransitioning = true;
            LastMessage = sit ? "Sitting down..." : "Standing up...";
            SetLocomotionBlocked(true);
            EnsureFadeCanvas(sessionCamera);
            transitionRoutine = StartCoroutine(ChangeView(sit));
        }

        private IEnumerator ChangeView(bool sit)
        {
            yield return FadeTo(1f);
            if (!SessionIsValid() || (sit && seatEye == null))
            {
                // This coroutine is already ending; do not try to stop it from ResetSession.
                transitionRoutine = null;
                ResetSession("The seated view ended because the XR rig changed.");
                yield break;
            }

            if (sit)
            {
                Vector3 forward = lookAt != null
                    ? lookAt.position - seatEye.position
                    : seatEye.forward;
                AlignHead(sessionOrigin, sessionCamera.transform, seatEye.position, forward);
                seatedPoseApplied = true;
                SetSeated(true);
            }
            else
            {
                RestoreStandingPose();
                SetSeated(false);
            }

            yield return FadeTo(0f);
            IsTransitioning = false;
            transitionRoutine = null;
            HideFade();
            SetLocomotionBlocked(IsSeated);
            LastMessage = IsSeated ? "Seated view. Look around naturally." : "Standing view restored.";
            if (!IsSeated) ClearSnapshot();
        }

        private void Update()
        {
            if ((IsSeated || IsTransitioning) && !SessionIsValid())
                ResetSession("The seated view ended because XR mode changed.");
        }

        private bool SessionIsValid()
        {
            return manager != null && manager.IsXrMode && sessionOrigin != null && sessionCamera != null
                && manager.XrOriginTransform == sessionOrigin && manager.ActiveCamera == sessionCamera;
        }

        private static void AlignHead(Transform origin, Transform head, Vector3 eyePosition, Vector3 forward)
        {
            Vector3 from = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            Vector3 to = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (from.sqrMagnitude > 0.0001f && to.sqrMagnitude > 0.0001f)
            {
                float yaw = Vector3.SignedAngle(from, to, Vector3.up);
                origin.RotateAround(head.position, Vector3.up, yaw);
            }

            // Move the Origin, not the tracked camera. Future physical head movement is preserved.
            origin.position += eyePosition - head.position;
        }

        private void RestoreStandingPose()
        {
            if (!hasStandingSnapshot || !seatedPoseApplied || sessionOrigin == null) return;
            if (sessionCamera != null && exitFloor != null)
            {
                Vector3 eyePosition = exitFloor.position + Vector3.up * standingEyeHeight;
                AlignHead(sessionOrigin, sessionCamera.transform, eyePosition, exitFloor.forward);
            }
            else
            {
                sessionOrigin.SetPositionAndRotation(standingOriginPosition, standingOriginRotation);
            }

            seatedPoseApplied = false;
        }

        private void SetLocomotionBlocked(bool blocked)
        {
            if (manager != null && (blocked || ownsLocomotionBlock))
                manager.SetReviewLocomotionBlocked(blocked);
            ownsLocomotionBlock = blocked;
        }

        private void SetSeated(bool seated)
        {
            if (IsSeated == seated) return;
            IsSeated = seated;
            // A listener must not strand the user behind the fade with locomotion blocked.
            if (SeatedChanged == null) return;
            foreach (Action<bool> listener in SeatedChanged.GetInvocationList())
            {
                try
                {
                    listener(seated);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }
        }

        private void EnsureFadeCanvas(Camera camera)
        {
            if (fadeCanvas == null)
            {
                var canvasObject = new GameObject("SeatViewFade", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
                canvasObject.transform.SetParent(transform, false);
                canvasObject.layer = 2; // Visual only; no collider or GraphicRaycaster.
                fadeCanvas = canvasObject.GetComponent<Canvas>();
                fadeCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                fadeCanvas.overrideSorting = true;
                fadeCanvas.sortingOrder = 32760;
                fadeGroup = canvasObject.GetComponent<CanvasGroup>();
                fadeGroup.alpha = 0f;
                fadeGroup.interactable = false;
                fadeGroup.blocksRaycasts = false;

                var imageObject = new GameObject("Black", typeof(RectTransform), typeof(Image));
                imageObject.transform.SetParent(canvasObject.transform, false);
                imageObject.layer = 2;
                var image = imageObject.GetComponent<Image>();
                image.color = Color.black;
                image.raycastTarget = false;
                RectTransform rect = imageObject.GetComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            fadeCanvas.worldCamera = camera;
            fadeCanvas.planeDistance = Mathf.Max(camera.nearClipPlane + 0.01f, 0.05f);
            fadeCanvas.enabled = true;
        }

        private IEnumerator FadeTo(float alpha)
        {
            if (fadeGroup == null) yield break;
            float start = fadeGroup.alpha;
            float duration = Mathf.Max(0f, fadeSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                fadeGroup.alpha = Mathf.Lerp(start, alpha, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            fadeGroup.alpha = alpha;
        }

        private void HideFade()
        {
            if (fadeGroup != null) fadeGroup.alpha = 0f;
            if (fadeCanvas != null) fadeCanvas.enabled = false;
        }

        private void ResetSession(string message)
        {
            if (transitionRoutine != null)
            {
                StopCoroutine(transitionRoutine);
                transitionRoutine = null;
            }

            RestoreStandingPose();
            IsTransitioning = false;
            HideFade();
            SetLocomotionBlocked(false);
            ClearSnapshot();
            LastMessage = message;
            SetSeated(false);
        }

        private void ClearSnapshot()
        {
            hasStandingSnapshot = false;
            seatedPoseApplied = false;
            sessionOrigin = null;
            sessionCamera = null;
        }

        private void OnDisable()
        {
            ResetSession(string.Empty);
        }

        private void OnDestroy()
        {
            ResetSession(string.Empty);
            if (fadeCanvas != null)
            {
                if (Application.isPlaying) Destroy(fadeCanvas.gameObject);
                else DestroyImmediate(fadeCanvas.gameObject);
            }
        }
    }
}
