using System.Globalization;
using UnityEngine;
using LivingRoom30.Measurement;

namespace LivingRoom30.Review
{
    /// <summary>
    /// 坐姿视野中的「沙发 ↔ 茶几净距」提示（Phase 2 补充需求）。
    ///
    /// ===== 需求 =====
    /// 用户坐进沙发后（<c>SeatViewController.IsSeated</c>），在视野内补充沙发至茶几的净距读数；
    /// **同时保留**项目已有的两类线索，本组件对它们只读、不替换：
    /// 1. 实尺度线索 —— <c>FurnitureProximityDisplay</c> 的家具尺寸标注与轮廓描边（独立运行，不受影响）；
    /// 2. 电视视线线索 —— <c>ReviewJourneyController</c> 的 <c>Stage.SeatedLook</c> 观察目标
    ///    "TV at seated eye height"（本组件不触碰旅程状态机与 HUD 文本）。
    ///
    /// ===== 表现 =====
    /// - 数字标签：`SOFA - TABLE CLEARANCE` + `0.42 m`，世界空间 TextMesh + <c>BillboardLabel</c>
    ///   （与测量标签同一套恒定屏幕尺寸逻辑，近大远小补偿、距离死区防抖）。
    /// - 间隙引线：一条细线连接两件家具包围盒的最近点，明确「净距量的是哪一段」。
    /// - 只在坐姿（且不在坐下/站起过渡中）显示；站立后立即隐藏，避免与自由行走时的实尺度标注混淆。
    ///
    /// ===== 净距的定义 =====
    /// 取两件家具**全部渲染器世界包围盒**的水平（XZ）最近距离：
    /// dx = max(0, A.minX − B.maxX, B.minX − A.maxX)，dz 同理，净距 = √(dx² + dz²)。
    /// 这是「最窄处」的口径，与项目内邻近测距（表面间隙）的语义一致；
    /// 1 Unity unit = 1 m，因此数值直接以米显示，两位小数。
    ///
    /// ===== 工程约束 =====
    /// - 运行期自举，不往场景写序列化内容（与 <c>FurnitureProximityDisplay</c>、E 部分同一约定）；
    ///   场景中如手动放置同名实例则复用该实例，便于 Inspector 调参。
    /// - 只读家具名与 <c>FurnitureInfo</c>，不修改任何既有脚本、预制体或场景；
    ///   若场景缺家具则静默禁用并只告警一次，不影响评审流程。
    /// - 标签不带碰撞体，不会被选择/测量射线拾取；渲染关闭阴影，避免破坏室内光照。
    /// - 3D 文本只用 ASCII：内置字体与 TMP 默认字体都没有中文字形，3D 标签必须保持英文，
    ///   中文仅出现在 UGUI 面板（与项目既有规范一致）。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SeatedClearanceCue : MonoBehaviour
    {
        // ---- 家具定位（与 ReviewJourneyController.Start 的校验口径一致，另加 FurnitureInfo 兜底）----
        private const string SofaObjectName = "F01_Sofa_ThreeSeat";
        private const string TableObjectName = "F04_CoffeeTable";
        private const string SofaFurnitureId = "F01";
        private const string TableFurnitureId = "F04";

        // ---- 时序 ----
        private const float RefreshInterval = 0.2f;     // 家具可能被脚本移动，0.2 s 重算足够
        private const float SeatPollInterval = 1f;
        private const float FurniturePollInterval = 2f;
        private const float CameraPollInterval = 2f;

        // ---- 版式（米）----
        private const float LabelLift = 0.12f;          // 标签高于茶几台面
        private const float LineLift = 0.015f;          // 引线略高于台面，避免与桌面 z-fighting
        private const float LineWidth = 0.006f;
        private const float TextWorldHeight = 0.32f;    // characterSize 0.05 × fontSize 64 × 0.1
        private const float ScreenFraction = 0.016f;   // 文字行高占屏幕高度比例（与测量标签同量级）

        [Header("显示")]
        [Tooltip("仅在坐姿时显示净距线索；关闭后任何姿态都显示（调试用）。")]
        [SerializeField] private bool showOnlyWhenSeated = true;

        [Tooltip("标签与引线颜色。默认暖白，避免与实尺度标注的黄色、轮廓的青色混淆。")]
        [SerializeField] private Color cueColor = new Color(1f, 0.95f, 0.82f, 1f);

        [Tooltip("是否绘制连接两件家具最近点的细引线。")]
        [SerializeField] private bool showGapLine = true;

        private SeatViewController seat;
        private SpatialMeasureManager measureManager;

        private GameObject sofa;
        private GameObject table;
        private Renderer[] sofaRenderers;
        private Renderer[] tableRenderers;

        private Transform labelTransform;
        private TextMesh labelText;
        private BillboardLabel billboard;
        private LineRenderer gapLine;
        private Material lineMaterial;

        private Camera headCamera;
        private float refreshTimer;
        private float seatPollTimer;
        private float furniturePollTimer;
        private float cameraPollTimer;

        private bool visible;
        private bool warnedMissingFurniture;
        private float lastGap = -1f;

        /// <summary>当前显示的净距（米）。未显示时为 -1。</summary>
        public float CurrentGap { get { return visible ? Mathf.Max(0f, lastGap) : -1f; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<SeatedClearanceCue>() != null) return;
            var go = new GameObject("SeatedClearanceCue");
            go.AddComponent<SeatedClearanceCue>();
        }

        private void Awake()
        {
            BuildVisuals();
            ResolveFurniture();
            seatPollTimer = 0f;
            SetVisible(false);
        }

        private void Update()
        {
            if (seat == null)
            {
                seatPollTimer -= Time.deltaTime;
                if (seatPollTimer <= 0f)
                {
                    seatPollTimer = SeatPollInterval;
                    seat = FindObjectOfType<SeatViewController>();
                }
            }

            if (sofa == null || table == null)
            {
                furniturePollTimer -= Time.deltaTime;
                if (furniturePollTimer <= 0f)
                {
                    furniturePollTimer = FurniturePollInterval;
                    ResolveFurniture();
                }
            }

            bool shouldShow = labelText != null && sofa != null && table != null
                && (!showOnlyWhenSeated || (seat != null && seat.IsSeated && !seat.IsTransitioning));

            if (!shouldShow)
            {
                if (visible) SetVisible(false);
                return;
            }

            refreshTimer -= Time.deltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = RefreshInterval;

            RefreshCue();
            if (!visible) SetVisible(true);
        }

        // ------------------------------------------------------------------
        // 构建
        // ------------------------------------------------------------------

        private void BuildVisuals()
        {
            var labelObject = new GameObject("ClearanceLabel");
            labelObject.transform.SetParent(transform, false);
            labelTransform = labelObject.transform;

            labelText = labelObject.AddComponent<TextMesh>();
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                labelText.font = font;
                var labelRenderer = labelObject.GetComponent<MeshRenderer>();
                if (labelRenderer != null && font.material != null)
                {
                    // 直接复用字体材质（深度测试开启）：标签放在真实间隙处，随家具正确遮挡，
                    // 与“穿墙可见”的 IssueMarker 标签刻意区分。
                    labelRenderer.sharedMaterial = font.material;
                    labelRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    labelRenderer.receiveShadows = false;
                }
            }
            else
            {
                Debug.LogWarning("[净距线索] 内置字体不可用，坐姿净距标签无法显示。");
            }

            labelText.anchor = TextAnchor.MiddleCenter;
            labelText.alignment = TextAlignment.Center;
            labelText.characterSize = 0.05f;
            labelText.fontSize = 64;
            labelText.fontStyle = FontStyle.Bold;
            labelText.color = cueColor;
            labelText.text = "SOFA - TABLE CLEARANCE\n-- m";

            billboard = labelObject.AddComponent<BillboardLabel>();
            // 与测量标签同口径：行高约占屏幕 1.6%（其默认 2% 的同一量级），
            // 下限 0.05 仅约束极近距离（<0.9 m），避免贴脸时文字占满视野。
            billboard.SetScaling(ScreenFraction, 0.05f, 3.0f, TextWorldHeight);

            var lineObject = new GameObject("ClearanceGapLine");
            lineObject.transform.SetParent(transform, false);
            gapLine = lineObject.AddComponent<LineRenderer>();
            lineMaterial = new Material(Shader.Find("Sprites/Default"));
            gapLine.sharedMaterial = lineMaterial;
            gapLine.useWorldSpace = true;
            gapLine.positionCount = 2;
            gapLine.startWidth = LineWidth;
            gapLine.endWidth = LineWidth;
            gapLine.numCapVertices = 4;
            gapLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gapLine.receiveShadows = false;
            gapLine.startColor = new Color(cueColor.r, cueColor.g, cueColor.b, 0.9f);
            gapLine.endColor = new Color(cueColor.r, cueColor.g, cueColor.b, 0.9f);
        }

        private void ResolveFurniture()
        {
            sofa = FindFurniture(SofaObjectName, SofaFurnitureId);
            table = FindFurniture(TableObjectName, TableFurnitureId);

            sofaRenderers = sofa != null ? sofa.GetComponentsInChildren<Renderer>(true) : null;
            tableRenderers = table != null ? table.GetComponentsInChildren<Renderer>(true) : null;

            if ((sofa == null || table == null) && !warnedMissingFurniture)
            {
                warnedMissingFurniture = true;
                Debug.LogWarning("[净距线索] 未找到 " + SofaObjectName + " 或 " + TableObjectName
                    + "，坐姿净距提示不可用。评审流程的其它功能不受影响。");
            }
        }

        /// <summary>先按对象名找（与旅程同口径），再按 FurnitureInfo 的 ID 兜底（场景改名时不至于失效）。</summary>
        private static GameObject FindFurniture(string preferredName, string furnitureId)
        {
            GameObject byName = GameObject.Find(preferredName);
            if (byName != null) return byName;

            var infos = FindObjectsOfType<FurnitureInfo>(true);
            for (int i = 0; i < infos.Length; i++)
            {
                if (infos[i] != null && infos[i].FurnitureId == furnitureId)
                    return infos[i].gameObject;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 每 0.2 s 刷新：位置、朝向相机、读数
        // ------------------------------------------------------------------

        private void RefreshCue()
        {
            if (sofa == null || table == null || labelText == null) return;

            Bounds a, b;
            if (!TryAggregateBounds(sofaRenderers, out a) || !TryAggregateBounds(tableRenderers, out b))
                return;

            // 水平 XZ 最近距离（最窄处口径）
            float gapX = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
            float gapZ = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
            float gap = Mathf.Sqrt(gapX * gapX + gapZ * gapZ);

            float lineY = b.max.y + LineLift;
            Vector3 pa, pb;
            if (gap > 0.001f)
            {
                // 最近点：只在分离的轴上取包围盒边缘；重叠的轴取重叠区中点，
                // 保证引线沿真实分离方向绘制（不会因两端取样点不同而画成斜线）。
                float overlapMidX = (Mathf.Max(a.min.x, b.min.x) + Mathf.Min(a.max.x, b.max.x)) * 0.5f;
                float overlapMidZ = (Mathf.Max(a.min.z, b.min.z) + Mathf.Min(a.max.z, b.max.z)) * 0.5f;
                bool separatedX = gapX > 0f;
                bool separatedZ = gapZ > 0f;

                float ax = separatedX ? (b.center.x >= a.center.x ? a.max.x : a.min.x) : overlapMidX;
                float bx = separatedX ? (b.center.x >= a.center.x ? b.min.x : b.max.x) : overlapMidX;
                float az = separatedZ ? (b.center.z >= a.center.z ? a.max.z : a.min.z) : overlapMidZ;
                float bz = separatedZ ? (b.center.z >= a.center.z ? b.min.z : b.max.z) : overlapMidZ;

                pa = new Vector3(ax, lineY, az);
                pb = new Vector3(bx, lineY, bz);
            }
            else
            {
                // 已接触/重叠：引线无意义，仅保留读数
                float cx = (a.center.x + b.center.x) * 0.5f;
                float cz = (a.center.z + b.center.z) * 0.5f;
                pa = new Vector3(cx, lineY, cz);
                pb = pa;
            }

            Vector3 mid = (pa + pb) * 0.5f;
            // 间隙正常时标签落在茶几台面上方；若两件家具重叠（净距 0），
            // 中点会埋进几何体内，此时抬到两者最高点之上，保证读数仍可见。
            mid.y = gap > 0.001f
                ? b.max.y + LabelLift
                : Mathf.Max(a.max.y, b.max.y) + LabelLift;
            labelTransform.position = mid;

            if (lastGap < 0f || Mathf.Abs(gap - lastGap) > 0.005f)
            {
                lastGap = gap;
                labelText.text = "SOFA - TABLE CLEARANCE\n"
                    + gap.ToString("F2", CultureInfo.InvariantCulture) + " m";
            }

            if (gapLine != null)
            {
                bool lineOn = showGapLine && gap > 0.001f;
                if (gapLine.gameObject.activeSelf != lineOn) gapLine.gameObject.SetActive(lineOn);
                if (lineOn)
                {
                    gapLine.SetPosition(0, pa);
                    gapLine.SetPosition(1, pb);
                }
            }

            RefreshCamera();
        }

        /// <summary>相机口径与项目一致：优先 <c>SpatialMeasureManager.ActiveCamera</c>，回退 <c>Camera.main</c>。</summary>
        private void RefreshCamera()
        {
            cameraPollTimer -= Time.deltaTime;
            if (cameraPollTimer > 0f && headCamera != null) return;
            cameraPollTimer = CameraPollInterval;

            if (measureManager == null) measureManager = FindObjectOfType<SpatialMeasureManager>();
            Camera camera = measureManager != null && measureManager.ActiveCamera != null
                ? measureManager.ActiveCamera : Camera.main;
            if (camera == null) return;

            if (camera != headCamera)
            {
                headCamera = camera;
                if (billboard != null) billboard.SetCamera(headCamera.transform);
            }
        }

        private static bool TryAggregateBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default(Bounds);
            if (renderers == null || renderers.Length == 0) return false;

            bool hasAny = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!hasAny) { bounds = renderer.bounds; hasAny = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return hasAny;
        }

        private void SetVisible(bool value)
        {
            visible = value;
            if (labelTransform != null) labelTransform.gameObject.SetActive(value);
            if (gapLine != null) gapLine.gameObject.SetActive(value && showGapLine);
            if (!value) lastGap = -1f;
        }

        private void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }
    }
}
