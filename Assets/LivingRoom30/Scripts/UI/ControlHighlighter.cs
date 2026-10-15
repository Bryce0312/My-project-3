using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30
{
    public enum HighlightTarget
    {
        Left,
        Right,
        Both,
    }

    /// <summary>
    /// Lights up a named part of a controller model while the tutorial asks the reviewer to use it,
    /// and can float a beacon above the hand for controls the reviewer cannot see.
    ///
    /// The shape of this class follows from three facts:
    /// the controllers only instantiate their model on their first <c>Update</c>, so the lookup is
    /// retried rather than resolved once; the model's material is an FBX sub-asset shared by every
    /// part of that hand, so the highlight swaps in its own material and restores the original
    /// rather than editing anything in place; and the trigger faces away from the reviewer, so a
    /// colour change alone is easy to miss and needs motion to pull the gaze down to the hand.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ControlHighlighter : MonoBehaviour
    {
        private const float ResolveTimeout = 5f;
        private const float EmissionStrength = 0.6f;
        private const float BeaconHeight = 0.10f;
        private const float BeaconBobHeight = 0.012f;
        private const float BeaconBobSpeed = 3.5f;

        [SerializeField] private ActionBasedController leftController;
        [SerializeField] private ActionBasedController rightController;
        [SerializeField] private Transform leftBeacon;
        [SerializeField] private Transform rightBeacon;
        [SerializeField] private Color highlightColor = new Color(0.35f, 0.85f, 1f);
        [SerializeField] private float pulseSpeed = 4f;

        private readonly List<Renderer> glowing = new List<Renderer>();
        private readonly List<Material> originalMaterials = new List<Material>();
        private readonly List<ActionBasedController> targets = new List<ActionBasedController>(2);

        private Material highlightMaterial;
        private string pendingPartName;
        private HighlightTarget pendingTarget;
        private float pendingDeadline;
        private bool beaconActive;

        public ActionBasedController LeftController => leftController;
        public ActionBasedController RightController => rightController;
        public Transform LeftBeacon => leftBeacon;
        public Transform RightBeacon => rightBeacon;

        public void Configure(ActionBasedController left, ActionBasedController right, Transform leftMarker, Transform rightMarker)
        {
            leftController = left;
            rightController = right;
            leftBeacon = leftMarker;
            rightBeacon = rightMarker;
        }

        public void Highlight(string partName, HighlightTarget target, bool showBeacon)
        {
            Clear();
            pendingPartName = partName;
            pendingTarget = target;
            pendingDeadline = Time.unscaledTime + ResolveTimeout;
            beaconActive = showBeacon;
            TryApply();
        }

        public void Clear()
        {
            for (int i = 0; i < glowing.Count; i++)
            {
                if (glowing[i] != null)
                {
                    glowing[i].sharedMaterial = originalMaterials[i];
                }
            }

            glowing.Clear();
            originalMaterials.Clear();
            pendingPartName = null;
            beaconActive = false;
            SetBeaconActive(leftBeacon, false);
            SetBeaconActive(rightBeacon, false);
        }

        private void Update()
        {
            if (pendingPartName != null && glowing.Count == 0)
            {
                TryApply();
            }

            if (glowing.Count > 0 || beaconActive)
            {
                Pulse();
            }

            UpdateBeacons();
        }

        private void OnDisable()
        {
            // The tutorial panel is deactivated when it finishes, so without this the models would
            // stay lit and the beacons would hang in the air for the rest of the session.
            Clear();
        }

        private void TryApply()
        {
            CollectTargets();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i] != null && targets[i].model == null)
                {
                    if (Time.unscaledTime > pendingDeadline)
                    {
                        Debug.LogWarning("ControlHighlighter never found a controller model to highlight '" + pendingPartName + "' on.", this);
                        pendingPartName = null;
                    }

                    return;
                }
            }

            Material material = EnsureMaterial();
            int applied = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                Transform part = FindPart(targets[i].model, pendingPartName);
                if (part == null)
                {
                    continue;
                }

                foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
                {
                    originalMaterials.Add(renderer.sharedMaterial);
                    glowing.Add(renderer);
                    renderer.sharedMaterial = material;
                    applied++;
                }
            }

            if (applied == 0)
            {
                Debug.LogWarning("ControlHighlighter found no controller part named '" + pendingPartName + "'.", this);
            }

            pendingPartName = null;
        }

        private void CollectTargets()
        {
            targets.Clear();
            if (pendingTarget == HighlightTarget.Left || pendingTarget == HighlightTarget.Both)
            {
                targets.Add(leftController);
            }

            if (pendingTarget == HighlightTarget.Right || pendingTarget == HighlightTarget.Both)
            {
                targets.Add(rightController);
            }
        }

        private void UpdateBeacons()
        {
            if (!beaconActive)
            {
                return;
            }

            CollectTargets();
            float bob = Mathf.Sin(Time.unscaledTime * BeaconBobSpeed) * BeaconBobHeight;

            for (int i = 0; i < targets.Count; i++)
            {
                Transform beacon = BeaconFor(targets[i]);
                if (targets[i] == null || beacon == null)
                {
                    continue;
                }

                // Positioned in world space above the hand so the marker stays readable whatever
                // way the controller is tilted.
                beacon.position = targets[i].transform.position + Vector3.up * (BeaconHeight + bob);
                SetBeaconActive(beacon, true);
            }
        }

        // Mapped by controller, not by list position: a right-hand-only step has that controller
        // at index 0 and must still light the right beacon.
        private Transform BeaconFor(ActionBasedController controller)
        {
            if (controller == null)
            {
                return null;
            }

            return controller == leftController ? leftBeacon : rightBeacon;
        }

        private void SetBeaconActive(Transform beacon, bool active)
        {
            if (beacon != null && beacon.gameObject.activeSelf != active)
            {
                if (active)
                {
                    Renderer renderer = beacon.GetComponent<Renderer>();
                    if (renderer != null)
                    {
                        renderer.sharedMaterial = EnsureMaterial();
                    }
                }

                beacon.gameObject.SetActive(active);
            }
        }

        private Material EnsureMaterial()
        {
            if (highlightMaterial != null)
            {
                return highlightMaterial;
            }

            Shader shader = Shader.Find("Standard") ?? Shader.Find("Legacy Shaders/Diffuse");
            highlightMaterial = new Material(shader) { name = "ControllerHighlight" };
            highlightMaterial.color = highlightColor;
            if (highlightMaterial.HasProperty("_Glossiness"))
            {
                highlightMaterial.SetFloat("_Glossiness", 0.2f);
            }

            // Best effort: should the emission variant be stripped from a player build, the
            // material still renders as a brightly lit colour and the highlight stays visible.
            highlightMaterial.EnableKeyword("_EMISSION");
            return highlightMaterial;
        }

        private void Pulse()
        {
            if (highlightMaterial == null)
            {
                return;
            }

            float pulse = (Mathf.Sin(Time.unscaledTime * pulseSpeed) + 1f) * 0.5f;
            Color color = Color.Lerp(highlightColor * 0.45f, highlightColor, pulse);
            color.a = 1f;

            highlightMaterial.color = color;
            if (highlightMaterial.HasProperty("_EmissionColor"))
            {
                highlightMaterial.SetColor("_EmissionColor", color * EmissionStrength);
            }
        }

        private static Transform FindPart(Transform model, string partName)
        {
            if (model == null)
            {
                return null;
            }

            // Exact match on purpose: "ThumbStick" is a prefix of "ThumbStick_Base", so anything
            // looser would light up the wrong part of the stick.
            foreach (Transform candidate in model.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == partName)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
