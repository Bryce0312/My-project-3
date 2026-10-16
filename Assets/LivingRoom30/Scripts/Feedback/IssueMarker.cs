using UnityEngine;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 单个空间问题标注（对应需求 UN-04「提交后的问题标注应持续保留在空间中」）。
    ///
    /// 视觉构成（全部运行时生成，不依赖任何预制体）：
    /// - Stem：从用户指向的世界坐标竖直向上的细杆，表达「标注锚点在地面/家具表面」。
    /// - Head：球体图钉，使用分类配色，是标注的主体视觉。
    /// - Label：「ISSUE #01 / Circulation」Billboard 标签，始终朝向相机并保持恒定屏幕尺寸。
    ///
    /// 设计约束：
    /// - **不附加任何 Collider**：C（测量）与 D（选择）都通过 Physics.Raycast 拾取世界，
    ///   若 Marker 带碰撞体将被误拾取。V1 的点选定位由「问题汇总列表 → 高亮」承担。
    /// - 所有材质运行时按 Unlit 创建，避免依赖导入的材质资源。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IssueMarker : MonoBehaviour
    {
        private const float StemHeight = 0.55f;
        private const float HeadHeight = 0.62f;
        private const float LabelHeight = 0.86f;

        private Transform head;
        private TextMesh label;
        private BillboardFacing facing;

        private Material headMaterial;
        private Material labelMaterial;
        private Color baseColor;
        private bool highlighted;
        private float highlightTimer;

        /// <summary>对应的记录 ID（与 IssueRecord.id 一致）。</summary>
        public string IssueId { get; private set; }

        /// <summary>标注锚定的世界坐标。</summary>
        public Vector3 Anchor { get; private set; }

        /// <summary>运行时创建：构建完整视觉层级并绑定相机。</summary>
        public static IssueMarker Create(Transform parent, string issueId, Vector3 worldPoint,
            IssueCategory category, string labelText, Camera facingCamera)
        {
            var root = new GameObject("IssueMarker_" + issueId);
            root.transform.SetParent(parent, false);
            root.transform.position = worldPoint;

            var marker = root.AddComponent<IssueMarker>();
            marker.Build(issueId, worldPoint, category, labelText, facingCamera);
            return marker;
        }

        private void Build(string issueId, Vector3 worldPoint, IssueCategory category,
            string labelText, Camera facingCamera)
        {
            IssueId = issueId;
            Anchor = worldPoint;
            baseColor = IssueCatalog.CategoryColor(category);

            // 图钉颜色材质（Unlit：不受室内光照影响，暗处也清晰可辨）
            headMaterial = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            headMaterial.color = baseColor;

            // 竖直细杆
            GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            StripCollider(stem);
            stem.transform.SetParent(transform, false);
            stem.transform.localPosition = new Vector3(0f, StemHeight * 0.5f, 0f);
            stem.transform.localScale = new Vector3(0.012f, StemHeight * 0.5f, 0.012f);
            stem.GetComponent<MeshRenderer>().sharedMaterial = headMaterial;

            // 球形图钉（编号用文字表达，避免在球面上生成贴图）
            GameObject headGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headGo.name = "Head";
            StripCollider(headGo);
            headGo.transform.SetParent(transform, false);
            headGo.transform.localPosition = new Vector3(0f, HeadHeight, 0f);
            headGo.transform.localScale = Vector3.one * 0.15f;
            headGo.GetComponent<MeshRenderer>().sharedMaterial = headMaterial;
            head = headGo.transform;

            // Billboard 标签
            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(transform, false);
            labelGo.transform.localPosition = new Vector3(0f, LabelHeight, 0f);
            label = labelGo.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) label.font = font;
            label.text = labelText;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.05f;
            label.fontSize = 64;
            label.fontStyle = FontStyle.Bold;
            label.color = baseColor;

            var labelRenderer = labelGo.GetComponent<MeshRenderer>();
            if (labelRenderer != null)
            {
                if (font != null && font.material != null)
                {
                    labelMaterial = new Material(font.material);
                    labelMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); // 不被家具遮挡
                    labelMaterial.SetInt("_ZWrite", 0);
                    labelMaterial.renderQueue = 4000;
                    labelRenderer.sharedMaterial = labelMaterial;
                }
                labelRenderer.sortingOrder = 20;
            }

            facing = labelGo.AddComponent<BillboardFacing>();
            facing.SetCamera(facingCamera != null ? facingCamera.transform : null);
        }

        /// <summary>更新标签文本（例如分类被修改后）。</summary>
        public void SetLabelText(string text)
        {
            if (label != null) label.text = text;
        }

        /// <summary>切换分类配色（记录被修改时不会发生，保留接口以便后续 V1+ 支持改分类）。</summary>
        public void SetCategory(IssueCategory category)
        {
            baseColor = IssueCatalog.CategoryColor(category);
            if (headMaterial != null) headMaterial.color = baseColor;
            if (label != null) label.color = baseColor;
        }

        /// <summary>由列表面板点击时调用：短暂脉冲高亮，帮助用户在场景中定位该问题。</summary>
        public void Highlight(float seconds = 2.5f)
        {
            highlighted = true;
            highlightTimer = Mathf.Max(0.2f, seconds);
        }

        /// <summary>相机切换（XR ↔ 桌面）时由 Manager 统一注入。</summary>
        public void SetFacingCamera(Camera cam)
        {
            if (facing != null) facing.SetCamera(cam != null ? cam.transform : null);
        }

        private void Update()
        {
            if (!highlighted) return;

            highlightTimer -= Time.deltaTime;
            if (highlightTimer <= 0f)
            {
                highlighted = false;
                if (head != null) head.localScale = Vector3.one * 0.15f;
                if (headMaterial != null) headMaterial.color = baseColor;
                return;
            }

            // 呼吸式脉冲：图钉尺寸 1.0~1.6 倍，颜色向白色偏移
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            float scale = Mathf.Lerp(0.15f, 0.24f, t);
            if (head != null) head.localScale = Vector3.one * scale;
            if (headMaterial != null) headMaterial.color = Color.Lerp(baseColor, Color.white, t * 0.6f);
        }

        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private void OnDestroy()
        {
            if (headMaterial != null) Destroy(headMaterial);
            if (labelMaterial != null) Destroy(labelMaterial);
        }

        /// <summary>
        /// 轻量 Billboard：始终正对相机，并按距离补偿缩放以维持「恒定屏幕尺寸」。
        /// 与测量模块的 BillboardLabel 同源思路，但刻意独立实现，避免 E 与 C 之间产生编译期耦合。
        /// </summary>
        public sealed class BillboardFacing : MonoBehaviour
        {
            [SerializeField] private Transform cameraTransform;
            [SerializeField] private bool constantScreenSize = true;
            [SerializeField] private float screenFraction = 0.016f;
            [SerializeField] private float minScale = 0.35f;
            [SerializeField] private float maxScale = 4.0f;
            [SerializeField] private float baseWorldHeight = 0.32f;

            private Camera cam;
            private float lastDistance = -1f;

            public void SetCamera(Transform t)
            {
                cameraTransform = t;
                cam = t != null ? t.GetComponent<Camera>() : null;
                lastDistance = -1f;
            }

            private void LateUpdate()
            {
                Transform camT = cameraTransform;
                if (camT == null)
                {
                    Camera main = Camera.main;
                    if (main == null) return;
                    cam = main;
                    camT = main.transform;
                }

                // 正对相机（保持文字水平）：只在水平方向朝向相机，避免文字上下颠倒
                Vector3 dir = camT.position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-6f)
                    transform.rotation = Quaternion.LookRotation(-dir.normalized, Vector3.up);

                if (!constantScreenSize) return;

                float dist = Vector3.Distance(camT.position, transform.position);
                if (lastDistance > 0f && Mathf.Abs(dist - lastDistance) < lastDistance * 0.01f) return;
                lastDistance = dist;

                float halfFov = (cam != null ? cam.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
                float k = screenFraction * 2f * Mathf.Tan(halfFov);
                float s = Mathf.Clamp(k * dist / Mathf.Max(0.001f, baseWorldHeight), minScale, maxScale);
                transform.localScale = Vector3.one * s;
            }
        }
    }
}
