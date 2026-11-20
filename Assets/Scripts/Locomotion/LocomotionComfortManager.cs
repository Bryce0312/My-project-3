using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30.Locomotion
{
    /// <summary>
    /// Self-configuring comfort-locomotion coordinator for the living-room review rig.
    ///
    /// It is bootstrapped at runtime (no scene or prefab edits), finds the rig's existing
    /// <see cref="ActionBasedContinuousMoveProvider"/> and <see cref="ActionBasedSnapTurnProvider"/>,
    /// and applies three things:
    ///
    ///   1. Walking: keeps smooth movement but at a gentler speed, with a comfort vignette that
    ///      fades in with movement and out when stationary.
    ///   2. Turning: reduces snap-turn to 30 degrees and disables the 180-degree turn-around that
    ///      was the source of the "sometimes a different angle" confusion.
    ///   3. Teleport: a button-toggled "long jump". Press the left primary button (X on Quest) to
    ///      raise the teleport arc, aim by turning the wrist, press X again to teleport, or press
    ///      the left secondary button (Y on Quest) to cancel.
    ///
    /// The teleport arc is blocked by furniture and walls, and it only lands on an upward-facing
    /// "Environment" surface (the floor) where the reviewer's body also fits without overlapping an
    /// obstacle. Together these guarantees prevent teleporting inside furniture or walls.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocomotionComfortManager : MonoBehaviour
    {
        private const float WalkSpeed = 1.0f;
        private const float TurnAngle = 30f;
        private const float TurnDebounce = 0.35f;

        private const float TeleportArcSpeed = 4.5f;
        private const float TeleportMaxSeconds = 1.2f;
        private const float TeleportSampleStep = 0.04f;
        private const float FloorNormalTolerance = 0.7f;
        private const float ClearanceRadius = 0.25f;   // player body radius (0.22) + comfort buffer
        private const float ClearanceHeight = 1.75f;
        private const float ClearanceMargin = 0.03f;

        private const float VignetteEaseInSeconds = 0.2f;
        private const float VignetteEaseOutSeconds = 0.3f;
        private const float VignetteMaxIntensity = 0.8f;
        private const float TeleportBlinkSeconds = 0.09f;

        private static readonly Color ValidColor = new Color(0.25f, 1f, 0.45f, 0.95f);
        private static readonly Color InvalidColor = new Color(1f, 0.22f, 0.22f, 0.95f);

        private enum TeleportState
        {
            Idle,
            Aiming,
        }

        /// <summary>Raised when the reviewer confirms a teleport. Consumed by the control tutorial.</summary>
        public static event System.Action Teleported;

        /// <summary>Raised when the reviewer cancels a teleport with the secondary button. Consumed by the control tutorial.</summary>
        public static event System.Action TeleportCancelled;

        private ActionBasedContinuousMoveProvider moveProvider;
        private ActionBasedSnapTurnProvider turnProvider;
        private LocomotionSystem locomotionSystem;
        private TeleportationProvider teleportProvider;
        private Transform leftHand;

        private InputAction teleportToggleAction;
        private InputAction teleportCancelAction;

        private ComfortVignette vignette;
        private Material lineMaterial;
        private LineRenderer arcRenderer;
        private LineRenderer reticleRenderer;
        private readonly List<Vector3> arcPoints = new List<Vector3>(64);

        private TeleportState teleportState = TeleportState.Idle;
        private int teleportLayerMask = -1;
        private int clearanceLayerMask = -1;
        private int environmentLayer = -1;
        private bool hasValidTarget;
        private Vector3 validTarget;

        private float aimRestoreMoveSpeed;
        private float vignetteIntensity;
        private float blinkUntil;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<LocomotionComfortManager>() != null)
                return;

            var host = new GameObject("LocomotionComfort");
            host.AddComponent<LocomotionComfortManager>();
        }

        private void Start()
        {
            if (!ResolveProviders())
            {
                Debug.Log("[LocomotionComfort] No XR locomotion providers found; comfort locomotion disabled in this scene.");
                enabled = false;
                return;
            }

            ApplyComfortSettings();

            teleportLayerMask = LayerMask.GetMask("Environment", "Furniture", "Interactable");
            clearanceLayerMask = teleportLayerMask;
            environmentLayer = LayerMask.NameToLayer("Environment");
            lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "LocomotionComfort_Line" };
            CreateInputActions();
            CreateTeleportProvider();
            CreateTeleportVisuals();

            vignette = gameObject.AddComponent<ComfortVignette>();
        }

        private void OnDestroy()
        {
            teleportToggleAction?.Dispose();
            teleportCancelAction?.Dispose();
        }

        private void CreateInputActions()
        {
            // On Quest Touch the left hand's face buttons are X (primary) and Y (secondary).
            teleportToggleAction = new InputAction(
                "TeleportToggle", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
            teleportCancelAction = new InputAction(
                "TeleportCancel", InputActionType.Button, "<XRController>{LeftHand}/secondaryButton");

            teleportToggleAction.Enable();
            teleportCancelAction.Enable();
        }

        private bool ResolveProviders()
        {
            moveProvider = FindObjectOfType<ActionBasedContinuousMoveProvider>();
            turnProvider = FindObjectOfType<ActionBasedSnapTurnProvider>();

            if (moveProvider == null && turnProvider == null)
                return false;

            locomotionSystem = moveProvider != null ? moveProvider.system : turnProvider.system;
            if (locomotionSystem == null)
                locomotionSystem = FindObjectOfType<LocomotionSystem>();

            foreach (var controller in FindObjectsOfType<ActionBasedController>())
            {
                if (controller.gameObject.name.ToLowerInvariant().Contains("left"))
                {
                    leftHand = controller.transform;
                    break;
                }
            }

            return true;
        }

        private void ApplyComfortSettings()
        {
            if (moveProvider != null)
                moveProvider.moveSpeed = WalkSpeed;

            if (turnProvider != null)
            {
                turnProvider.turnAmount = TurnAngle;
                turnProvider.enableTurnAround = false;
                turnProvider.debounceTime = TurnDebounce;
            }
        }

        private void CreateTeleportProvider()
        {
            teleportProvider = gameObject.AddComponent<TeleportationProvider>();
            if (locomotionSystem != null)
                teleportProvider.system = locomotionSystem;
        }

        private void CreateTeleportVisuals()
        {
            var arcObject = new GameObject("TeleportArc");
            arcObject.transform.SetParent(transform, false);
            arcRenderer = arcObject.AddComponent<LineRenderer>();
            arcRenderer.sharedMaterial = lineMaterial;
            arcRenderer.useWorldSpace = true;
            arcRenderer.positionCount = 0;
            arcRenderer.startWidth = 0.02f;
            arcRenderer.endWidth = 0.02f;
            arcRenderer.startColor = ValidColor;
            arcRenderer.endColor = ValidColor;
            arcRenderer.numCapVertices = 4;
            arcRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arcRenderer.receiveShadows = false;

            var reticleObject = new GameObject("TeleportReticle");
            reticleObject.transform.SetParent(transform, false);
            reticleRenderer = reticleObject.AddComponent<LineRenderer>();
            reticleRenderer.sharedMaterial = lineMaterial;
            reticleRenderer.useWorldSpace = false;
            reticleRenderer.loop = true;
            reticleRenderer.startWidth = 0.025f;
            reticleRenderer.endWidth = 0.025f;
            reticleRenderer.startColor = ValidColor;
            reticleRenderer.endColor = ValidColor;
            reticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            reticleRenderer.receiveShadows = false;

            const int segments = 40;
            const float radius = 0.25f;
            reticleRenderer.positionCount = segments;
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                reticleRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            SetTeleportVisualsVisible(false);
        }

        private void Update()
        {
            if (moveProvider == null && turnProvider == null)
                return;

            if (teleportToggleAction != null && teleportToggleAction.WasPressedThisFrame())
                OnTogglePressed();

            if (teleportState == TeleportState.Aiming)
            {
                if (!IsLocomotionAvailable())
                {
                    // The review blocked walking (e.g. seated view); drop the teleport arc too.
                    EndTeleportAim();
                }
                else if (teleportCancelAction != null && teleportCancelAction.WasPressedThisFrame())
                {
                    EndTeleportAim();
                    TeleportCancelled?.Invoke();
                }
                else
                {
                    UpdateTeleportAim();
                }
            }

            UpdateVignette();
        }

        private void OnTogglePressed()
        {
            if (!IsLocomotionAvailable())
            {
                if (teleportState == TeleportState.Aiming)
                    EndTeleportAim();
                return;
            }

            if (teleportState == TeleportState.Idle)
            {
                BeginTeleportAim();
            }
            else if (hasValidTarget)
            {
                ConfirmTeleport();
            }
            // Otherwise the toggle landed on an invalid target: stay in aim so the user can adjust.
        }

        /// <summary>
        /// Teleport is locomotion, so it shares the same gate as walking. The review (and desktop
        /// mode) blocks locomotion by disabling the continuous-move provider; when that happens the
        /// reviewer must also be unable to teleport, otherwise they could move the rig away from the
        /// seated or measured viewpoint and desync the seat or measurement state.
        /// </summary>
        private bool IsLocomotionAvailable()
        {
            return moveProvider != null && moveProvider.isActiveAndEnabled;
        }

        private void BeginTeleportAim()
        {
            if (leftHand == null)
            {
                Debug.LogWarning("[LocomotionComfort] Left hand controller not found; teleport is disabled.");
                return;
            }

            teleportState = TeleportState.Aiming;
            hasValidTarget = false;

            // Suspend walking while the teleport arc is up, so the reviewer does not drift while
            // aiming. Zeroing the speed keeps the provider alive without moving the rig.
            if (moveProvider != null)
            {
                aimRestoreMoveSpeed = moveProvider.moveSpeed;
                moveProvider.moveSpeed = 0f;
            }

            SetTeleportVisualsVisible(true);
        }

        private void EndTeleportAim()
        {
            teleportState = TeleportState.Idle;
            hasValidTarget = false;

            if (moveProvider != null)
                moveProvider.moveSpeed = aimRestoreMoveSpeed;

            SetTeleportVisualsVisible(false);
        }

        private void ConfirmTeleport()
        {
            blinkUntil = Time.unscaledTime + TeleportBlinkSeconds;

            if (teleportProvider != null)
            {
                teleportProvider.QueueTeleportRequest(new TeleportRequest
                {
                    destinationPosition = validTarget,
                    destinationRotation = Quaternion.identity,
                    requestTime = Time.time,
                    matchOrientation = MatchOrientation.None,
                });
            }

            EndTeleportAim();
            Teleported?.Invoke();
        }

        private void UpdateTeleportAim()
        {
            if (leftHand == null)
                return;

            Vector3 origin = leftHand.position;
            Vector3 velocity = leftHand.forward * TeleportArcSpeed;

            hasValidTarget = ComputeArc(origin, velocity, out validTarget);
            DrawArc();
            DrawReticle();
        }

        private bool ComputeArc(Vector3 origin, Vector3 velocity, out Vector3 landing)
        {
            landing = Vector3.zero;
            arcPoints.Clear();
            arcPoints.Add(origin);

            Vector3 gravity = Physics.gravity;
            int steps = Mathf.CeilToInt(TeleportMaxSeconds / TeleportSampleStep);
            Vector3 previous = origin;

            for (int i = 1; i <= steps; i++)
            {
                float t = i * TeleportSampleStep;
                Vector3 current = origin + velocity * t + 0.5f * gravity * t * t;

                Vector3 segment = current - previous;
                float distance = segment.magnitude;
                if (distance > 0.0001f &&
                    Physics.Raycast(previous, segment / distance, out RaycastHit hit, distance, teleportLayerMask, QueryTriggerInteraction.Ignore))
                {
                    arcPoints.Add(hit.point);

                    // A floor landing is only on the Environment layer with an upward normal.
                    // Furniture, interactable objects, walls and the ceiling all stop the arc red.
                    bool isFloor = hit.collider.gameObject.layer == environmentLayer &&
                                   Vector3.Dot(hit.normal, Vector3.up) > FloorNormalTolerance;
                    if (!isFloor)
                        return false;

                    landing = hit.point;
                    return IsLandingClear(hit.point);
                }

                arcPoints.Add(current);
                previous = current;
            }

            return false;
        }

        /// <summary>
        /// Returns true when the reviewer's body (approximated by the rig CharacterController
        /// capsule) fits at the given floor point without overlapping furniture, walls or other
        /// obstacles. The capsule is raised slightly so the floor itself never counts as a blocker.
        /// </summary>
        private bool IsLandingClear(Vector3 floorPoint)
        {
            Vector3 bottom = floorPoint + Vector3.up * (ClearanceRadius + ClearanceMargin);
            Vector3 top = floorPoint + Vector3.up * (ClearanceHeight - ClearanceRadius + ClearanceMargin);
            return !Physics.CheckCapsule(bottom, top, ClearanceRadius, clearanceLayerMask, QueryTriggerInteraction.Ignore);
        }

        private void DrawArc()
        {
            Color color = hasValidTarget ? ValidColor : InvalidColor;
            arcRenderer.startColor = color;
            arcRenderer.endColor = color;

            arcRenderer.positionCount = arcPoints.Count;
            for (int i = 0; i < arcPoints.Count; i++)
                arcRenderer.SetPosition(i, arcPoints[i]);
        }

        private void DrawReticle()
        {
            Vector3 position = hasValidTarget
                ? validTarget
                : (arcPoints.Count > 0 ? arcPoints[arcPoints.Count - 1] : Vector3.zero);

            Color color = hasValidTarget ? ValidColor : InvalidColor;
            reticleRenderer.startColor = color;
            reticleRenderer.endColor = color;

            reticleRenderer.transform.position = position + Vector3.up * 0.015f;
        }

        private void SetTeleportVisualsVisible(bool visible)
        {
            if (arcRenderer != null)
                arcRenderer.enabled = visible;
            if (reticleRenderer != null)
                reticleRenderer.enabled = visible;
        }

        private void UpdateVignette()
        {
            if (vignette == null)
                return;

            float target = 0f;

            if (Time.unscaledTime < blinkUntil)
            {
                target = 1f;
            }
            else if (teleportState == TeleportState.Idle && moveProvider != null && moveProvider.isActiveAndEnabled)
            {
                Vector2 move = ReadMoveInput();
                float magnitude = move.magnitude;
                if (magnitude > 0.05f)
                    target = Mathf.InverseLerp(0.05f, 1f, magnitude) * VignetteMaxIntensity;
            }

            float easeSeconds = target > vignetteIntensity ? VignetteEaseInSeconds : VignetteEaseOutSeconds;
            float blend = easeSeconds <= 0.0001f ? 1f : 1f - Mathf.Exp(-Time.unscaledDeltaTime / easeSeconds);
            vignetteIntensity = Mathf.Lerp(vignetteIntensity, target, blend);
            vignette.SetIntensity(vignetteIntensity);
        }

        private Vector2 ReadMoveInput()
        {
            Vector2 value = Vector2.zero;
            if (moveProvider == null)
                return value;

            if (moveProvider.leftHandMoveAction.action != null)
                value += moveProvider.leftHandMoveAction.action.ReadValue<Vector2>();
            if (moveProvider.rightHandMoveAction.action != null)
                value += moveProvider.rightHandMoveAction.action.ReadValue<Vector2>();
            return value;
        }
    }
}
