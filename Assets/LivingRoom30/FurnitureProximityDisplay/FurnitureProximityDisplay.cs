using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LivingRoom30
{
    /// <summary>
    /// Additive, read-only furniture dimension display with a camera/character proximity trigger.
    ///
    /// Behaviour:
    ///  - Lazily uses <see cref="Camera.main"/> (or an optional explicit reference point) purely to
    ///    measure "how close is the viewer" and to billboard the labels, so it works for both the XR
    ///    headset camera and the desktop review camera.
    ///  - Caches every <see cref="FurnitureInfo"/> in the scene and derives dimensions purely from the
    ///    object's Renderer/Collider world-space bounds (it never reads or writes FurnitureInfo fields,
    ///    and never touches selection / measurement / teleport / feedback systems).
    ///  - TRIGGER (proximity distance from the viewer): for each piece we measure the distance from the
    ///    viewer (camera / character reference) to the piece's world AABB and show the border + dimension
    ///    label only while that distance is within <see cref="m_TriggerDistance"/>. Moving away hides it
    ///    again. A separate, slightly larger <see cref="m_ReleaseDistance"/> provides hysteresis so the
    ///    border does not flicker on/off right at the threshold.
    ///  - Distance can be measured in the horizontal plane (XZ, ignoring height -- the most intuitive
    ///    "walk over to it" metric) or as the true 3D distance to the nearest point of the AABB.
    ///  - RENDER (contour-following outline): the border is an inverted-hull outline (back-face normal
    ///    extrusion, see Shaders/OutlineInvertedHull.shader) generated from the furniture's REAL mesh
    ///    geometry. Curved surfaces yield curved outlines, polygons yield polygonal outlines -- it hugs
    ///    the modeling contour tightly, with NO gap and NO rectangular / AABB approximation.
    ///  - MULTIPLE pieces can be visible at once (e.g. the sofa + table you just walked up to); each
    ///    gets its own billboarded label (name + W x H x D in metres) and its own edge-hugging outline.
    ///
    /// The trigger and the render share the same per-furniture world bounds (the trigger uses the
    /// viewer-to-bounds distance; the label uses bounds size + top placement), while the contour traces
    /// the actual mesh -- so dimensions appear exactly when you are near, and the border always sits
    /// flush against the furniture edge.
    ///
    /// Text height formula (metres) = characterSize x fontSize x 0.1, matching the convention
    /// documented in Measurement/BillboardLabel.cs.
    ///
    /// This component is fully self-contained: label and outline GameObjects are created at runtime and
    /// parented under the furniture's own renderers (outline) or live at the scene root (labels), so no
    /// prefab or scene file is edited. It also auto-installs itself on scene load. Old component
    /// instances saved with outdated values are auto-migrated in <see cref="OnEnable"/> via
    /// <see cref="m_ConfigVersion"/>.
    /// </summary>
    [AddComponentMenu("LivingRoom30/Furniture Proximity Display")]
    [DisallowMultipleComponent]
    public sealed class FurnitureProximityDisplay : MonoBehaviour
    {
        // Bump when the tuned defaults below change; older saved instances (whose serialized
        // values override script defaults) are then re-normalized on enable.
        private const int k_ConfigVersion = 7;

        /// <summary>How the viewer-to-furniture distance is measured.</summary>
        public enum DistanceMode
        {
            /// <summary>Horizontal (XZ) distance, ignoring height -- the most intuitive "walk up to it" metric.</summary>
            PlanarXZ,

            /// <summary>True 3D distance from the viewer to the nearest point of the furniture's AABB.</summary>
            Full3D,
        }

        [Header("Toggle")]
        [SerializeField] private bool m_EnableDisplay = true;

        [Header("Trigger (camera / character proximity)")]
        [Tooltip("进入范围距离（米，1 unit = 1 m）。相机/角色与家具包围盒最近点的距离 ≤ 此值时显示边框与尺寸标注。")]
        [SerializeField] private float m_TriggerDistance = 1.5f;

        [Tooltip("离开范围距离（米）。已显示的家具需大于此距离才隐藏；比进入距离略大即可形成滞回，避免在阈值边界反复闪烁。建议 ≥ 进入距离。")]
        [SerializeField] private float m_ReleaseDistance = 1.8f;

        [Tooltip("距离计算方式：水平(XZ) 忽略高度差，行走靠近最直观；三维 为相机到包围盒最近点的真实距离。")]
        [SerializeField] private DistanceMode m_DistanceMode = DistanceMode.PlanarXZ;

        [Tooltip("距离参考点（角色/相机）。留空则自动使用每帧的 Camera.main（编辑器预览相机或 XR 头部相机）。")]
        [SerializeField] private Transform m_DistanceOrigin;

        [Tooltip("检测频率（秒）。越低越灵敏、CPU 略高。")]
        [SerializeField] private float m_MeasureInterval = 0.1f;

        [Header("Label Layout")]
        [Tooltip("标签位于家具顶面之上的小高度（米），使其贴合轮廓上沿而非高高漂浮。")]
        [SerializeField] private float m_LabelVerticalOffset = 0.03f;

        // Text world height (m) = m_CharacterSize * m_FontSize * 0.1  ->  0.02 * 48 * 0.1 = 0.096 m.
        [SerializeField] private Color m_TextColor = new Color(1f, 0.92f, 0.16f);
        [SerializeField] private int m_FontSize = 48;
        [SerializeField] private float m_CharacterSize = 0.02f;

        [Header("Border Style (Inverted-Hull, contour-following)")]
        [Tooltip("显示贴合家具真实轮廓的描边（取代旧版 AABB 线框）。")]
        [SerializeField] private bool m_ShowOutline = true;

        // Reuses the previous box colour slot (cyan) as the outline colour so any saved value survives.
        [FormerlySerializedAs("m_BoxColor")]
        [Tooltip("边框描边颜色。")]
        [SerializeField] private Color m_OutlineColor = new Color(0.2f, 0.9f, 1f, 1f);

        [Tooltip("描边外扩厚度（世界米，1 unit = 1 m）。恒定不随缩放变化，数值小则紧密贴合轮廓。0.002 - 0.05 m。")]
        [SerializeField] private float m_OutlineWidth = 0.012f;

        [Header("Maintenance")]
        [Tooltip("自动重新扫描场景 FurnitureInfo 的间隔秒数（0 = 不自动）。覆盖运行时新增家具。")]
        [SerializeField] private float m_AutoRefreshInterval = 2f;

        [SerializeField] private int m_ConfigVersion;

        private sealed class Entry
        {
            public FurnitureInfo Info;
            public Renderer[] Renderers;
            public Collider[] Colliders;
        }

        // One outline child GameObject per Renderer of a focused furniture (so multi-mesh and
        // multi-sub-mesh furniture are each framed). Keyed by the cached Entry reference.
        private sealed class OutlineSet
        {
            public readonly List<GameObject> Outlines = new List<GameObject>();
        }

        private readonly List<Entry> m_Entries = new List<Entry>();
        private readonly Dictionary<Entry, OutlineSet> m_OutlineSets = new Dictionary<Entry, OutlineSet>();
        // 当前帧处于“范围内”状态的家具集合，用于与上一帧做差量并实现滞回（避免每帧重复开关）。
        private readonly HashSet<Entry> m_Active = new HashSet<Entry>();
        // 每件活动家具一个标签（名称 + 尺寸），支持同时显示多件。
        private readonly Dictionary<Entry, GameObject> m_Labels = new Dictionary<Entry, GameObject>();

        private Camera m_Camera;
        private float m_MeasureTimer;
        private float m_RefreshTimer;
        private Material m_LabelMaterial;
        private Material m_OutlineMaterial;

        // Once-only warnings so the Console is not spammed every throttle tick.
        private static bool s_SkinnedOutlineWarned;
        private static bool s_ShaderMissingWarned;

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
            m_ReleaseDistance = 1.8f;
            m_DistanceMode = DistanceMode.PlanarXZ;
            m_MeasureInterval = 0.1f;
            m_LabelVerticalOffset = 0.03f;
            m_TextColor = new Color(1f, 0.92f, 0.16f);
            m_FontSize = 48;
            m_CharacterSize = 0.02f;
            m_ShowOutline = true;
            m_OutlineColor = new Color(0.2f, 0.9f, 1f, 1f);
            m_OutlineWidth = 0.012f;
            m_AutoRefreshInterval = 2f;
            m_ConfigVersion = k_ConfigVersion;
        }

        private void ApplyLabelSettings()
        {
            // Push the current outline colour / width into the (runtime) outline material.
            ApplyOutlineSettings();
        }

        private void ApplyOutlineSettings()
        {
            if (m_OutlineMaterial != null)
            {
                m_OutlineMaterial.SetColor("_OutlineColor", m_OutlineColor);
                m_OutlineMaterial.SetFloat("_OutlineWidth", m_OutlineWidth);
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
            HideAll();
            m_MeasureTimer = 0f;
            m_RefreshTimer = 0f;
        }

        private void OnDisable()
        {
            HideAll();
        }

        private void OnDestroy()
        {
            // Outline children + materials are runtime-created; dispose them.
            ClearOutlineSets();
            ClearLabels();
            if (m_LabelMaterial != null)
            {
                Destroy(m_LabelMaterial);
                m_LabelMaterial = null;
            }

            if (m_OutlineMaterial != null)
            {
                Destroy(m_OutlineMaterial);
                m_OutlineMaterial = null;
            }
        }

        private void OnValidate()
        {
            m_TriggerDistance = Mathf.Max(0f, m_TriggerDistance);
            // Keep the release distance at least as large as the trigger distance so the hysteresis
            // is always valid (release >= trigger).
            m_ReleaseDistance = Mathf.Max(m_TriggerDistance, m_ReleaseDistance);
            m_MeasureInterval = Mathf.Clamp(m_MeasureInterval, 0.02f, 1f);
            m_LabelVerticalOffset = Mathf.Max(0f, m_LabelVerticalOffset);
            m_AutoRefreshInterval = Mathf.Max(0f, m_AutoRefreshInterval);
            m_OutlineWidth = Mathf.Clamp(m_OutlineWidth, 0.002f, 0.05f);
            ApplyLabelSettings();
        }

        private void Update()
        {
            if (!m_EnableDisplay)
            {
                HideAll();
                return;
            }

            // Re-resolve every frame: Camera.main returns whichever camera is currently enabled
            // (desktop S02 preview vs. XR rig head camera), so labels always billboard correctly.
            m_Camera = Camera.main;
            if (m_Camera == null)
            {
                HideAll();
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

            // Viewer reference: an explicit character/transform if assigned, otherwise the active camera.
            // (In this rig the head camera is the only reliably moved part of the player, so it is the
            // correct proxy for "where the character is".)
            Vector3 viewerPos = m_DistanceOrigin != null
                ? m_DistanceOrigin.position
                : m_Camera.transform.position;

            m_MeasureTimer += Time.deltaTime;
            if (m_MeasureTimer >= m_MeasureInterval)
            {
                m_MeasureTimer = 0f;
                MeasureAndUpdate(viewerPos);
            }

            // Billboard every visible label toward the camera.
            foreach (var kvp in m_Labels)
            {
                if (kvp.Value != null && kvp.Value.activeSelf)
                {
                    kvp.Value.transform.rotation = m_Camera.transform.rotation;
                }
            }
        }

        /// <summary>
        /// Re-scan the scene for <see cref="FurnitureInfo"/> components and cache their
        /// Renderer/Collider references. Safe to call repeatedly (e.g. after furniture is (re)built).
        /// </summary>
        [ContextMenu("Refresh Furniture")]
        public void RefreshFurniture()
        {
            // Outline children are parented to the (now stale) furniture renderers, so drop them
            // before we forget the old entries.
            ClearOutlineSets();
            ClearLabels();

            m_Entries.Clear();
            m_Active.Clear();

            // Non-generic overload avoids any version-specific generic ambiguity.
            Object[] found = Object.FindObjectsOfType(typeof(FurnitureInfo));
            for (int i = 0; i < found.Length; i++)
            {
                FurnitureInfo info = found[i] as FurnitureInfo;
                if (info == null)
                {
                    continue;
                }

                Entry entry = new Entry
                {
                    Info = info,
                    Renderers = info.GetComponentsInChildren<Renderer>(),
                    Colliders = info.GetComponentsInChildren<Collider>(),
                };
                m_Entries.Add(entry);
            }
        }

        /// <summary>
        /// PROXIMITY trigger: show a piece's border + dimensions only while the viewer is close enough.
        /// For every piece we measure the distance from <paramref name="viewerPos"/> to the piece's world
        /// AABB; a piece that is currently hidden appears once within <see cref="m_TriggerDistance"/>, and
        /// a piece that is currently shown stays visible until it exceeds the (larger)
        /// <see cref="m_ReleaseDistance"/> (hysteresis). Supports multiple simultaneous visible pieces.
        /// </summary>
        private void MeasureAndUpdate(Vector3 viewerPos)
        {
            int n = m_Entries.Count;
            Bounds?[] bounds = new Bounds?[n];
            for (int i = 0; i < n; i++)
            {
                bounds[i] = ComputeWorldBounds(m_Entries[i]);
            }

            bool use3D = m_DistanceMode == DistanceMode.Full3D;
            HashSet<Entry> next = new HashSet<Entry>();

            for (int i = 0; i < n; i++)
            {
                if (bounds[i] == null)
                {
                    continue;
                }

                Entry entry = m_Entries[i];
                float distance = DistanceToBounds(viewerPos, bounds[i].Value, use3D);

                // Hysteresis: an already-shown piece must exceed the larger release distance to hide,
                // while a hidden piece shows as soon as it enters the trigger distance.
                bool wasActive = m_Active.Contains(entry);
                float threshold = wasActive
                    ? Mathf.Max(m_ReleaseDistance, m_TriggerDistance)
                    : m_TriggerDistance;

                if (distance <= threshold)
                {
                    next.Add(entry);
                }
            }

            // Diff against the previous frame: disable entries that left the range.
            foreach (var e in m_Active)
            {
                if (!next.Contains(e))
                {
                    DisableEntry(e);
                }
            }

            // Enable / refresh the currently-in-range entries.
            for (int i = 0; i < n; i++)
            {
                Entry e = m_Entries[i];
                if (next.Contains(e) && bounds[i] != null)
                {
                    ShowEntry(e, bounds[i].Value);
                }
            }

            m_Active.Clear();
            foreach (var e in next)
            {
                m_Active.Add(e);
            }
        }

        /// <summary>
        /// Distance (metres) from a world point to the nearest point of a world AABB.
        /// When <paramref name="use3D"/> is false the height (Y) difference is ignored (horizontal
        /// distance only), which is the most intuitive metric for "walking up to" furniture.
        /// </summary>
        private static float DistanceToBounds(Vector3 point, Bounds b, bool use3D)
        {
            float dx = Mathf.Max(b.min.x - point.x, 0f, point.x - b.max.x);
            float dz = Mathf.Max(b.min.z - point.z, 0f, point.z - b.max.z);
            if (!use3D)
            {
                return Mathf.Sqrt(dx * dx + dz * dz);
            }

            float dy = Mathf.Max(b.min.y - point.y, 0f, point.y - b.max.y);
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void ShowEntry(Entry entry, Bounds bounds)
        {
            // Contour-following border: inverted-hull, flush against the real mesh edge.
            if (m_ShowOutline)
            {
                OutlineSet set = GetOrBuildOutlineSet(entry);
                if (set != null)
                {
                    EnableOutlineSet(set);
                }
            }
            else
            {
                DisableOutlineSet(entry);
            }

            // Dimension label (name + W x H x D).
            GameObject label = GetOrCreateLabel(entry);
            if (label != null)
            {
                Vector3 top = bounds.center + Vector3.up * (bounds.extents.y + m_LabelVerticalOffset);
                label.transform.position = top;
                if (m_Camera != null)
                {
                    label.transform.rotation = m_Camera.transform.rotation;
                }

                TextMesh tm = label.GetComponent<TextMesh>();
                if (tm != null)
                {
                    string name = entry.Info.EnglishName;
                    Vector3 size = bounds.size;
                    string line2 = $"W {size.x:F2} × H {size.y:F2} × D {size.z:F2} m";
                    tm.text = string.IsNullOrEmpty(name) ? line2 : (name + "\n" + line2);
                    tm.color = m_TextColor;
                }

                label.SetActive(true);
            }
        }

        private void DisableEntry(Entry entry)
        {
            DisableOutlineSet(entry);
            if (m_Labels.TryGetValue(entry, out GameObject label) && label != null)
            {
                label.SetActive(false);
            }
        }

        private void HideAll()
        {
            foreach (var e in m_Active)
            {
                DisableEntry(e);
            }

            m_Active.Clear();
        }

        private GameObject GetOrCreateLabel(Entry entry)
        {
            if (m_Labels.TryGetValue(entry, out GameObject existing))
            {
                return existing;
            }

            GameObject go = new GameObject("FurnitureDimLabel_" + entry.Info.EnglishName);
            go.transform.SetParent(null);

            TextMesh tm = go.AddComponent<TextMesh>();

            // Same built-in font setup as Measurement/MeasurementVisualizer.cs: Unity 2022.2+ removed
            // Arial.ttf, and the TextMesh MeshRenderer must use the font material to render correctly.
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                tm.font = font;

                MeshRenderer labelRenderer = go.GetComponent<MeshRenderer>();
                if (labelRenderer != null)
                {
                    // Disable depth test so the label is never hidden inside the furniture.
                    if (m_LabelMaterial == null)
                    {
                        m_LabelMaterial = new Material(font.material);
                        m_LabelMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                        m_LabelMaterial.SetInt("_ZWrite", 0);
                        m_LabelMaterial.renderQueue = 4000;
                    }

                    labelRenderer.sharedMaterial = m_LabelMaterial;
                    labelRenderer.sortingOrder = 10;
                }
            }

            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Normal;
            tm.characterSize = m_CharacterSize;
            tm.fontSize = m_FontSize;
            tm.color = m_TextColor;

            go.SetActive(false);
            m_Labels[entry] = go;
            return go;
        }

        private void ClearLabels()
        {
            foreach (var kvp in m_Labels)
            {
                if (kvp.Value != null)
                {
                    Destroy(kvp.Value);
                }
            }

            m_Labels.Clear();
        }

        // ---- Outline (inverted-hull) management --------------------------------------------

        /// <summary>
        /// Ensures the shared outline material exists, creating it from the custom inverted-hull
        /// shader. Logs a clear error (once) if the shader cannot be found so the failure is
        /// obvious rather than a silent null material.
        /// </summary>
        private void EnsureOutlineMaterial()
        {
            if (m_OutlineMaterial != null)
            {
                return;
            }

            Shader shader = Shader.Find("LivingRoom30/OutlineInvertedHull");
            if (shader == null)
            {
                if (!s_ShaderMissingWarned)
                {
                    Debug.LogError("[FurnitureProximityDisplay] Outline shader 'LivingRoom30/OutlineInvertedHull' " +
                        "not found. The contour outline is disabled. Ensure the shader asset exists in the project " +
                        "(and, for player builds, is listed under GraphicsSettings > Always Included Shaders).");
                    s_ShaderMissingWarned = true;
                }

                return;
            }

            m_OutlineMaterial = new Material(shader);
            m_OutlineMaterial.name = "FurnitureOutlineMaterial";
            ApplyOutlineSettings();
        }

        /// <summary>
        /// Returns the cached outline set for an entry, building it on first use. One child
        /// GameObject (MeshFilter + MeshRenderer using the outline material) is parented under
        /// EACH renderer of the furniture, with an identity local transform so it shares the
        /// renderer's exact world placement -- the outline auto-follows the furniture, and because
        /// it reuses the same sharedMesh it traces the real contour (curved OR polygonal).
        /// </summary>
        private OutlineSet GetOrBuildOutlineSet(Entry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (m_OutlineSets.TryGetValue(entry, out OutlineSet set))
            {
                return set;
            }

            EnsureOutlineMaterial();
            if (m_OutlineMaterial == null)
            {
                // Shader missing (already warned once) -- skip building children so we never assign
                // a null material (which would render as the default error material).
                return null;
            }

            set = new OutlineSet();

            if (entry.Renderers != null)
            {
                for (int i = 0; i < entry.Renderers.Length; i++)
                {
                    Renderer renderer = entry.Renderers[i];
                    if (renderer == null || !renderer.enabled)
                    {
                        continue;
                    }

                    Mesh mesh = GetRendererMesh(renderer);
                    if (mesh == null)
                    {
                        continue;
                    }

                    GameObject outline = new GameObject("FurnitureOutline_" + renderer.name);
                    // Identity local transform relative to the renderer -> coincides with the
                    // furniture mesh exactly and follows it automatically (no per-frame reposition).
                    outline.transform.SetParent(renderer.transform, false);

                    MeshFilter mf = outline.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh; // shared with the furniture; we never Destroy it.

                    MeshRenderer mr = outline.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = m_OutlineMaterial;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;

                    outline.SetActive(false); // hidden until this furniture is active
                    set.Outlines.Add(outline);
                }
            }

            m_OutlineSets[entry] = set;
            return set;
        }

        /// <summary>
        /// Resolves the mesh a renderer draws, so the outline uses the same geometry.
        /// MeshRenderer -> its MeshFilter.sharedMesh. SkinnedMeshRenderer -> sharedMesh (bind pose;
        /// the static-furniture assumption is documented via a once-only warning).
        /// </summary>
        private static Mesh GetRendererMesh(Renderer renderer)
        {
            if (renderer is MeshRenderer mr)
            {
                MeshFilter mf = mr.GetComponent<MeshFilter>();
                return mf != null ? mf.sharedMesh : null;
            }

            if (renderer is SkinnedMeshRenderer smr)
            {
                // Static-furniture assumption: outline the bind-pose mesh. For animated/skinned
                // furniture this would not follow the live pose, so warn once instead of silently
                // producing a wrong outline.
                if (!s_SkinnedOutlineWarned)
                {
                    Debug.LogWarning("[FurnitureProximityDisplay] A SkinnedMeshRenderer was found; its outline " +
                        "uses the bind-pose mesh (static assumption). Animated poses are not followed. " +
                        "(Shader: LivingRoom30/OutlineInvertedHull)");
                    s_SkinnedOutlineWarned = true;
                }

                return smr.sharedMesh;
            }

            return null;
        }

        private static void EnableOutlineSet(OutlineSet set)
        {
            if (set == null)
            {
                return;
            }

            for (int i = 0; i < set.Outlines.Count; i++)
            {
                if (set.Outlines[i] != null)
                {
                    set.Outlines[i].SetActive(true);
                }
            }
        }

        private void DisableOutlineSet(Entry entry)
        {
            if (entry == null)
            {
                return;
            }

            if (m_OutlineSets.TryGetValue(entry, out OutlineSet set))
            {
                for (int i = 0; i < set.Outlines.Count; i++)
                {
                    if (set.Outlines[i] != null)
                    {
                        set.Outlines[i].SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// Destroys every cached outline child GameObject (not the shared meshes -- those belong to
        /// the furniture). Called on re-scan and on destroy.
        /// </summary>
        private void ClearOutlineSets()
        {
            foreach (var kvp in m_OutlineSets)
            {
                OutlineSet set = kvp.Value;
                if (set == null)
                {
                    continue;
                }

                for (int i = 0; i < set.Outlines.Count; i++)
                {
                    if (set.Outlines[i] != null)
                    {
                        Destroy(set.Outlines[i]);
                    }
                }
            }

            m_OutlineSets.Clear();
        }

        /// <summary>
        /// Builds a world-space AABB by merging child Renderer bounds, falling back to Collider
        /// bounds when no Renderer is present. Returns null when neither yields a valid bounds.
        /// Used for BOTH the proximity trigger (viewer-to-bounds distance) AND the label (size + top
        /// placement) -- but never for the contour outline (that traces the real mesh).
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
