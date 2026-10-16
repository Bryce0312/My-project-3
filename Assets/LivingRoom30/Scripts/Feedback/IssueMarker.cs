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
    /// - 朝向与缩放逻辑直接写在本类中，**不另开嵌套的 MonoBehaviour**：
    ///   Unity 的脚本注册以「文件名 = 外层类名」为单位，嵌套 MonoBehaviour 无法在 Inspector 中挂载，
    ///   不同版本下 `AddComponent` 也可能报 "script class cannot be found"。
    ///   这里只有十行逻辑，内联进来即可完全规避该风险（本项目其它脚本也都是"一个 MonoBehaviour 一个文件"）。
    ///
    /// 朝向口径：项目既有的 <c>BillboardLabel</c>（测量标签）与 <c>WristPanelFacing</c> / <c>HudLazyFollow</c>
    /// （世界空间画布）都采用「物体 forward 背对观察者」，
    /// 因此这里用 `LookRotation(自身位置 − 相机位置)`；反向会让文字变成镜像。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IssueMarker : MonoBehaviour
    {
        // ---- 几何 ----
        private const float StemHeight = 0.55f;
        private const float HeadHeight = 0.62f;
        private const float LabelHeight = 0.86f;
        private const float HeadScale = 0.15f;
        private const float HeadScaleHighlighted = 0.24f;

        // ---- 标签恒定屏幕尺寸 ----
        /// <summary>文字行高占屏幕高度的比例。</summary>
        private const float LabelScreenFraction = 0.016f;
        private const float LabelMinScale = 0.35f;
        private const float LabelMaxScale = 4.0f;
        /// <summary>localScale = 1 时的文字 em 高（米）≈ characterSize(0.05) × fontSize(64) × 0.1。</summary>
        private const float LabelBaseWorldHeight = 0.32f;
        /// <summary>距离变化小于该比例时跳过重算（死区防抖）。</summary>
        private const float LabelScaleDeadZone = 0.01f;

        private Transform head;
        private TextMesh label;
        private Camera facingCamera;

        private Material headMaterial;
        private Material labelMaterial;
        private Color baseColor;
        private bool highlighted;
        private float highlightTimer;
        private float lastLabelDistance = -1f;

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
            this.facingCamera = facingCamera;

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
            GameObject headObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            headObject.name = "Head";
            StripCollider(headObject);
            headObject.transform.SetParent(transform, false);
            headObject.transform.localPosition = new Vector3(0f, HeadHeight, 0f);
            headObject.transform.localScale = Vector3.one * HeadScale;
            headObject.GetComponent<MeshRenderer>().sharedMaterial = headMaterial;
            head = headObject.transform;

            // Billboard 标签
            var labelObject = new GameObject("Label");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = new Vector3(0f, LabelHeight, 0f);

            label = labelObject.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) label.font = font;
            label.text = labelText;
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.05f;
            label.fontSize = 64;
            label.fontStyle = FontStyle.Bold;
            label.color = baseColor;

            var labelRenderer = labelObject.GetComponent<MeshRenderer>();
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

            RefreshLabelTransform();
        }

        /// <summary>更新标签文本（例如分类被修改后）。</summary>
        public void SetLabelText(string text)
        {
            if (label != null) label.text = text;
        }

        /// <summary>切换分类配色。</summary>
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

        /// <summary>相机切换（XR ↔ 桌面）时由 IssueMarkerManager 统一注入。</summary>
        public void SetFacingCamera(Camera camera)
        {
            if (camera == null) return;
            facingCamera = camera;
            lastLabelDistance = -1f;
            RefreshLabelTransform();
        }

        private void Update()
        {
            if (!highlighted) return;

            highlightTimer -= Time.deltaTime;
            if (highlightTimer <= 0f)
            {
                highlighted = false;
                if (head != null) head.localScale = Vector3.one * HeadScale;
                if (headMaterial != null) headMaterial.color = baseColor;
                return;
            }

            // 呼吸式脉冲：图钉尺寸与颜色同时变化，远处也能注意到
            float t = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            if (head != null) head.localScale = Vector3.one * Mathf.Lerp(HeadScale, HeadScaleHighlighted, t);
            if (headMaterial != null) headMaterial.color = Color.Lerp(baseColor, Color.white, t * 0.6f);
        }

        /// <summary>
        /// 标签朝向与恒定屏幕尺寸。放在 LateUpdate：确保相机本帧位姿已确定后再对齐，
        /// 避免与相机驱动组件（XRHeadPoseFollower / DesktopOrbitCamera）争抢同一帧的先后顺序。
        /// </summary>
        private void LateUpdate()
        {
            RefreshLabelTransform();
        }

        private void RefreshLabelTransform()
        {
            if (label == null) return;

            Camera camera = facingCamera != null ? facingCamera : Camera.main;
            if (camera == null) return;

            Transform cameraTransform = camera.transform;
            Vector3 labelPosition = label.transform.position;

            // 朝向：只在水平面上对齐，避免文字随俯仰倾斜；forward 背对观察者
            Vector3 direction = labelPosition - cameraTransform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 1e-6f)
                label.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);

            // 恒定屏幕尺寸：按距离补偿缩放，保证近处不过大、远处仍可读
            float distance = Vector3.Distance(cameraTransform.position, labelPosition);
            if (lastLabelDistance > 0f &&
                Mathf.Abs(distance - lastLabelDistance) < lastLabelDistance * LabelScaleDeadZone)
                return;
            lastLabelDistance = distance;

            float halfFov = camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float metersPerDistance = LabelScreenFraction * 2f * Mathf.Tan(halfFov);
            float scale = Mathf.Clamp(metersPerDistance * distance / LabelBaseWorldHeight,
                LabelMinScale, LabelMaxScale);
            label.transform.localScale = Vector3.one * scale;
        }

        private static void StripCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
        }

        private void OnDestroy()
        {
            if (headMaterial != null) Destroy(headMaterial);
            if (labelMaterial != null) Destroy(labelMaterial);
        }
    }
}
