using System.Collections.Generic;
using UnityEngine;

namespace LivingRoom30
{
    /// <summary>
    /// Additive, read-only proximity dimension display for furniture.
    ///
    /// Behaviour:
    ///  - Lazily uses <see cref="Camera.main"/> as the reference point so it works for both the
    ///    XR headset camera and the desktop review camera.
    ///  - Caches every <see cref="FurnitureInfo"/> in the scene and derives dimensions purely from
    ///    the object's Renderer/Collider world-space bounds (it never reads or writes FurnitureInfo
    ///    fields, and never touches selection / measurement / teleport / feedback systems).
    ///  - Throttled (~0.1s) nearest-point test against each furniture AABB; when the camera is
    ///    within <see cref="m_TriggerDistance"/> of the nearest furniture, a billboarded TextMesh
    ///    label (name + size in metres) sits on the furniture top border AND a cyan wireframe
    ///    outline is drawn around the furniture.
    ///
    /// Text height formula (metres) = characterSize x fontSize x 0.1, matching the convention
    /// documented in Measurement/BillboardLabel.cs.
    ///
    /// This component is fully self-contained: the label and outline GameObjects are created at
    /// runtime and live at the scene root, so no prefab or scene file is edited. It also
    /// auto-installs itself on scene load, so no menu click is required. Old component instances
    /// saved with outdated values are auto-migrated in <see cref="OnEnable"/> via
    /// <see cref="m_ConfigVersion"/>.
    /// </summary>
    [AddComponentMenu("LivingRoom30/Furniture Proximity Display")]
    [DisallowMultipleComponent]
    public sealed class FurnitureProximityDisplay : MonoBehaviour
    {
        // Bump when the tuned defaults below change; older saved instances (whose serialized
        // values override script defaults) are then re-normalized on enable.
        private const int k_ConfigVersion = 3;

        [Header("Toggle")]
        [SerializeField] private bool m_EnableDisplay = true;

        [Header("Trigger / Timing")]
        [Tooltip("Distance (metres, 1 unit = 1 m) from the camera to a furniture AABB within which the visuals appear.")]
        [SerializeField] private float m_TriggerDistance = 1.5f;

        [Tooltip("How often (seconds) the nearest-furniture test is performed. Lower = more responsive, higher CPU.")]
        [SerializeField] private float m_MeasureInterval = 0.1f;

        [Header("Label Layout")]
        [Tooltip("Small height (metres) kept just above the furniture top face so the label sits on the outline border instead of floating high above.")]
        [SerializeField] private float m_LabelVerticalOffset = 0.03f;

        // Text world height (m) = m_CharacterSize * m_FontSize * 0.1  ->  0.02 * 48 * 0.1 = 0.096 m.
        [SerializeField] private Color m_TextColor = new Color(1f, 0.92f, 0.16f);
        [SerializeField] private int m_FontSize = 48;
        [SerializeField] private float m_CharacterSize = 0.02f;

        [Header("Outline Box")]
        [SerializeField] private bool m_ShowOutline = true;
        [SerializeField] private Color m_BoxColor = new Color(0.2f, 0.9f, 1f, 1f);

        [Header("Maintenance")]
        [Tooltip("Seconds between automatic re-scans of the scene for FurnitureInfo (0 = never). Covers runtime-added furniture.")]
        [SerializeField] private float m_AutoRefreshInterval = 2f;

        [SerializeField] private int m_ConfigVersion;

        private sealed class Entry
        {
            public FurnitureInfo Info;
            public Renderer[] Renderers;
            public Collider[] Colliders;
        }

        private readonly List<Entry> m_Entries = new List<Entry>();
        private Camera m_Camera;
        private float m_MeasureTimer;
        private float m_RefreshTimer;
        private Entry m_FocusEntry;

        private GameObject m_LabelObject;
        private TextMesh m_LabelText;
        private Material m_LabelMaterial;
        private GameObject m_BoxObject;
        private Mesh m_BoxMesh;
        private Material m_BoxMaterial;

        // Auto-install so the feature works without manually adding the component or using the menu.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (Object.FindObjectOfType(typeof(FurnitureProximityDisplay)) != null)
            {
                return;
            }

            GameObject go = new GameObject("FurnitureDimensionDisplay");
            go.AddComponent<FurnitureProximityDisplay>();
        }

        /// <summary>
        /// Re-normalizes outdated serialized values (instances saved before a tuning change keep
        /// their old serialized values; script defaults alone never update them).
        /// </summary>
        private void ApplyConfigDefaults()
        {
            m_EnableDisplay = true;
            m_TriggerDistance = 1.5f;
            m_MeasureInterval = 0.1f;
            m_LabelVerticalOffset = 0.03f;
            m_TextColor = new Color(1f, 0.92f, 0.16f);
            m_FontSize = 48;
            m_CharacterSize = 0.02f;
            m_ShowOutline = true;
            m_BoxColor = new Color(0.2f, 0.9f, 1f, 1f);
            m_AutoRefreshInterval = 2f;
            m_ConfigVersion = k_ConfigVersion;
        }

        private void ApplyLabelSettings()
        {
            if (m_LabelText != null)
            {
                m_LabelText.fontSize = m_FontSize;
                m_LabelText.characterSize = m_CharacterSize;
                m_LabelText.color = m_TextColor;
            }

            if (m_BoxMaterial != null)
            {
                m_BoxMaterial.color = m_BoxColor;
            }
        }

        private void OnEnable()
        {
            if (m_ConfigVersion != k_ConfigVersion)
            {
                ApplyConfigDefaults();
            }

            ApplyLabelSettings();
            RefreshFurniture();
            HideVisuals();
            m_MeasureTimer = 0f;
            m_RefreshTimer = 0f;
        }

        private void OnDisable()
        {
            HideVisuals();
        }

        private void OnDestroy()
        {
            if (m_LabelObject != null)
            {
                Destroy(m_LabelObject);
                m_LabelObject = null;
                m_LabelText = null;
            }

            if (m_LabelMaterial != null)
            {
                Destroy(m_LabelMaterial);
                m_LabelMaterial = null;
            }

            if (m_BoxObject != null)
            {
                Destroy(m_BoxObject);
                m_BoxObject = null;
            }

            if (m_BoxMesh != null)
            {
                Destroy(m_BoxMesh);
                m_BoxMesh = null;
            }

            if (m_BoxMaterial != null)
            {
                Destroy(m_BoxMaterial);
                m_BoxMaterial = null;
            }
        }

        private void OnValidate()
        {
            m_TriggerDistance = Mathf.Max(0f, m_TriggerDistance);
            m_MeasureInterval = Mathf.Clamp(m_MeasureInterval, 0.02f, 1f);
            m_LabelVerticalOffset = Mathf.Max(0f, m_LabelVerticalOffset);
            m_AutoRefreshInterval = Mathf.Max(0f, m_AutoRefreshInterval);
            ApplyLabelSettings();
        }

        private void Update()
        {
            if (!m_EnableDisplay)
            {
                HideVisuals();
                return;
            }

            if (m_Camera == null)
            {
                m_Camera = Camera.main;
            }

            if (m_Camera == null)
            {
                HideVisuals();
                return;
            }

            if (m_AutoRefreshInterval > 0f)
            {
                m_RefreshTimer += Time.deltaTime;
                if (m_RefreshTimer >= m_AutoRefreshInterval)
                {
                    m_RefreshTimer = 0f;
                    RefreshFurniture();
                }
            }

            m_MeasureTimer += Time.deltaTime;
            if (m_MeasureTimer >= m_MeasureInterval)
            {
                m_MeasureTimer = 0f;
                MeasureAndUpdate();
            }

            // Billboard the label while visible so it always faces the camera.
            if (m_LabelObject != null && m_LabelObject.activeSelf)
            {
                m_LabelObject.transform.rotation = m_Camera.transform.rotation;
            }
        }

        /// <summary>
        /// Re-scan the scene for <see cref="FurnitureInfo"/> components and cache their
        /// Renderer/Collider references. Safe to call repeatedly (e.g. after furniture is (re)built).
        /// </summary>
        [ContextMenu("Refresh Furniture")]
        public void RefreshFurniture()
        {
            m_Entries.Clear();

            // Non-generic overload avoids any version-specific generic ambiguity.
            Object[] found = Object.FindObjectsOfType(typeof(FurnitureInfo));
            for (int i = 0; i < found.Length; i++)
            {
                FurnitureInfo info = found[i] as FurnitureInfo;
                if (info == null)
                {
                    continue;
                }

                m_Entries.Add(new Entry
                {
                    Info = info,
                    Renderers = info.GetComponentsInChildren<Renderer>(),
                    Colliders = info.GetComponentsInChildren<Collider>(),
                });
            }
        }

        private void MeasureAndUpdate()
        {
            Vector3 camPos = m_Camera.transform.position;
            float bestDist = float.MaxValue;
            Entry best = null;
            Bounds bestBounds = default;

            for (int i = 0; i < m_Entries.Count; i++)
            {
                Entry entry = m_Entries[i];
                if (entry == null || entry.Info == null)
                {
                    continue;
                }

                Bounds? bounds = ComputeWorldBounds(entry);
                if (bounds == null)
                {
                    continue;
                }

                // Closest point on the world-space AABB to the camera; distance 0 when inside.
                Vector3 closest = bounds.Value.ClosestPoint(camPos);
                float distance = Vector3.Distance(camPos, closest);
                if (distance < bestDist)
                {
                    bestDist = distance;
                    best = entry;
                    bestBounds = bounds.Value;
                }
            }

            if (best != null && bestDist <= m_TriggerDistance)
            {
                ShowVisuals(best, bestBounds);
            }
            else
            {
                HideVisuals();
            }
        }

        private void ShowVisuals(Entry entry, Bounds bounds)
        {
            EnsureVisuals();

            // Label sits just above the furniture top border, world-anchored so it does not
            // drift with the player.
            if (m_LabelObject != null)
            {
                Vector3 top = bounds.center + Vector3.up * (bounds.extents.y + m_LabelVerticalOffset);
                m_LabelObject.transform.position = top;
                m_LabelObject.transform.rotation = m_Camera.transform.rotation;
                m_LabelObject.SetActive(true);
            }

            // Cyan wireframe outline around the furniture.
            if (m_BoxObject != null)
            {
                m_BoxObject.SetActive(m_ShowOutline);
                if (m_ShowOutline)
                {
                    UpdateBoxMesh(bounds);
                }
            }

            // Dimensions are static per furniture; only rebuild the text when the focus changes.
            if (m_FocusEntry != entry)
            {
                m_FocusEntry = entry;

                if (m_LabelText != null)
                {
                    string name = entry.Info.EnglishName;
                    Vector3 size = bounds.size;
                    string line2 = $"W {size.x:F2} × H {size.y:F2} × D {size.z:F2} m";
                    m_LabelText.text = string.IsNullOrEmpty(name) ? line2 : (name + "\n" + line2);
                    m_LabelText.color = m_TextColor;
                }
            }
        }

        private void HideVisuals()
        {
            if (m_LabelObject != null)
            {
                m_LabelObject.SetActive(false);
            }

            if (m_BoxObject != null)
            {
                m_BoxObject.SetActive(false);
            }

            m_FocusEntry = null;
        }

        private void EnsureVisuals()
        {
            if (m_LabelObject == null)
            {
                m_LabelObject = new GameObject("FurnitureDimensionLabel");
                m_LabelObject.transform.SetParent(null);

                m_LabelText = m_LabelObject.AddComponent<TextMesh>();

                // Same built-in font setup as Measurement/MeasurementVisualizer.cs: Unity 2022.2+
                // removed Arial.ttf, and the TextMesh MeshRenderer must use the font material to
                // render correctly.
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                {
                    m_LabelText.font = font;

                    MeshRenderer labelRenderer = m_LabelObject.GetComponent<MeshRenderer>();
                    if (labelRenderer != null)
                    {
                        // Disable depth test so the label is never hidden inside the furniture.
                        m_LabelMaterial = new Material(font.material);
                        m_LabelMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                        m_LabelMaterial.SetInt("_ZWrite", 0);
                        m_LabelMaterial.renderQueue = 4000;
                        labelRenderer.sharedMaterial = m_LabelMaterial;
                        labelRenderer.sortingOrder = 10;
                    }
                }

                m_LabelText.anchor = TextAnchor.MiddleCenter;
                m_LabelText.alignment = TextAlignment.Center;
                m_LabelText.fontStyle = FontStyle.Normal;
                m_LabelText.characterSize = m_CharacterSize;
                m_LabelText.fontSize = m_FontSize;
                m_LabelText.color = m_TextColor;

                m_LabelObject.SetActive(false);
            }

            if (m_BoxObject == null)
            {
                m_BoxObject = new GameObject("FurnitureDimensionBox");
                m_BoxObject.transform.SetParent(null);

                MeshFilter mf = m_BoxObject.AddComponent<MeshFilter>();
                MeshRenderer mr = m_BoxObject.AddComponent<MeshRenderer>();

                m_BoxMesh = new Mesh();
                m_BoxMesh.name = "FurnitureBounds";
                mf.sharedMesh = m_BoxMesh;

                m_BoxMaterial = new Material(Shader.Find("Unlit/Color"));
                m_BoxMaterial.color = m_BoxColor;
                mr.sharedMaterial = m_BoxMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                m_BoxObject.SetActive(false);
            }
        }

        /// <summary>
        /// Builds a wireframe box (line-list mesh) from the world-space AABB corners.
        /// </summary>
        private void UpdateBoxMesh(Bounds b)
        {
            if (m_BoxMesh == null)
            {
                return;
            }

            Vector3 min = b.min;
            Vector3 max = b.max;

            Vector3[] v = new Vector3[8];
            v[0] = new Vector3(min.x, min.y, min.z);
            v[1] = new Vector3(max.x, min.y, min.z);
            v[2] = new Vector3(max.x, min.y, max.z);
            v[3] = new Vector3(min.x, min.y, max.z);
            v[4] = new Vector3(min.x, max.y, min.z);
            v[5] = new Vector3(max.x, max.y, min.z);
            v[6] = new Vector3(max.x, max.y, max.z);
            v[7] = new Vector3(min.x, max.y, max.z);

            int[] idx = new int[]
            {
                0, 1, 1, 2, 2, 3, 3, 0, // bottom
                4, 5, 5, 6, 6, 7, 7, 4, // top
                0, 4, 1, 5, 2, 6, 3, 7  // verticals
            };

            m_BoxMesh.vertices = v;
            m_BoxMesh.SetIndices(idx, MeshTopology.Lines, 0);
            m_BoxMesh.RecalculateBounds();
        }

        /// <summary>
        /// Builds a world-space AABB by merging child Renderer bounds, falling back to Collider
        /// bounds when no Renderer is present. Returns null when neither yields a valid bounds.
        /// </summary>
        private static Bounds? ComputeWorldBounds(Entry entry)
        {
            bool initialized = false;
            Bounds acc = default;

            if (entry.Renderers != null)
            {
                for (int i = 0; i < entry.Renderers.Length; i++)
                {
                    Renderer renderer = entry.Renderers[i];
                    if (renderer == null || !renderer.enabled)
                    {
                        continue;
                    }

                    if (!initialized)
                    {
                        acc = renderer.bounds;
                        initialized = true;
                    }
                    else
                    {
                        acc.Encapsulate(renderer.bounds);
                    }
                }
            }

            if (!initialized && entry.Colliders != null)
            {
                for (int i = 0; i < entry.Colliders.Length; i++)
                {
                    Collider collider = entry.Colliders[i];
                    if (collider == null || !collider.enabled)
                    {
                        continue;
                    }

                    if (!initialized)
                    {
                        acc = collider.bounds;
                        initialized = true;
                    }
                    else
                    {
                        acc.Encapsulate(collider.bounds);
                    }
                }
            }

            return initialized ? acc : (Bounds?)null;
        }
    }
}
