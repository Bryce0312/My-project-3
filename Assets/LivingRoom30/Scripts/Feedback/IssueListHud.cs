using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using LivingRoom30;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 问题汇总（对应需求 UN-07「体验结束后查看本次评审产生的完整问题列表及对应位置」、目标 G5）。
    ///
    /// 由两部分组成：
    /// 1. <b>右腕常驻入口</b>——小块世界空间画布，挂在右手柄上，显示 <c>ISSUES (n)</c> 与一行状态文字。
    ///    位置与朝向完全沿用项目既有的 <c>S05_ReviewHud</c> 方案（同一腕部偏移、同一缩放、
    ///    同一 <c>WristPanelFacing</c> 朝向修正），因此视觉与交互手感与项目其它 HUD 一致。
    ///    挂在**右**腕而不是左腕，是因为 <c>S05_ReviewHud</c> 已经占用了左腕；两者互不遮挡。
    ///    入口按钮由**左手柄射线**点击（正如项目教程里用一只手去点另一只手腕上的 GAPS 键）。
    /// 2. <b>汇总面板</b>——点击入口后在头显前方生成的大幅世界空间列表：
    ///    标题与严重程度摘要、每页 6 条问题、逐条「FOCUS」定位高亮、分页、
    ///    导出（CSV+JSON）与关闭。生成方式与 D 的标注面板一致（相机前方、射线避让），
    ///    因此交互方式对用户是同一套心智模型。
    ///
    /// 为什么用分页而不是 ScrollRect：世界空间下用射线拖拽滚动需要额外的遮罩与拖拽调校，
    /// 分页用两个按钮即可完成，确定性强、在不同设备上行为一致。
    /// </summary>
    public sealed class IssueListHud : MonoBehaviour
    {
        // ---- 腕部入口版式（单位：画布像素，画布缩放 0.00035 ⇒ 1 像素 ≈ 0.35 mm）----
        private const float WristCanvasWidth = 340f;
        private const float WristCanvasHeight = 132f;
        private const float WristCanvasScale = 0.00035f;
        private static readonly Vector3 WristLocalOffset = new Vector3(0f, 0.06f, -0.11f);

        // ---- 汇总面板版式 ----
        private const float PanelWidth = 760f;
        private const float PanelHeight = 600f;
        private const float PanelScale = 0.0014f;
        private const float PanelSpawnDistance = 1.15f;
        /// <summary>障碍物前留出的余量（米）。</summary>
        private const float PanelObstacleGap = 0.2f;
        /// <summary>面板与眼睛的最小距离（米）：贴墙时防止面板糊在脸上。</summary>
        private const float MinPanelDistance = 0.6f;
        private const float RowHeight = 62f;
        private const float RowSpacing = 4f;
        private const int PageSize = 6;
        /// <summary>头显相机的复检间隔（秒）。</summary>
        private const float CameraPollInterval = 0.5f;

        private FeedbackService service;
        private Camera headCamera;
        private Transform wristAnchor;

        private RectTransform wristCanvas;
        private Button entryButton;
        private Text statusText;
        private float statusTimer;

        private RectTransform listCanvas;
        private Text listTitle;
        private Text listSummary;
        private RectTransform listRowsRoot;
        private Text pageLabel;
        private Button prevButton;
        private Button nextButton;
        private int page;
        private float cameraPollTimer;

        public bool IsOpen { get { return listCanvas != null; } }

        /// <summary>
        /// 创建腕部入口。若找不到右手柄（例如场景被替换），返回 null 并给出告警——
        /// 此时 E 的其余功能（标注入库、空间标注、导出）不受影响。
        /// </summary>
        public static IssueListHud Create(Transform parent, FeedbackService feedbackService)
        {
            var go = new GameObject("IssueListHud");
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<IssueListHud>();
            hud.Build(feedbackService);
            return hud;
        }

        private void Build(FeedbackService feedbackService)
        {
            service = feedbackService;
            headCamera = FeedbackUi.ResolveHeadCamera();
            FeedbackUi.EnsureEventSystem();

            wristAnchor = FeedbackUi.FindControllerTransform("Right");
            if (wristAnchor == null)
            {
                Debug.LogWarning("[反馈] 未找到右手柄控制器，问题汇总入口无法挂载。" +
                                 "标注入库与空间标注不受影响。");
                return;
            }

            wristCanvas = FeedbackUi.CreateWorldSpaceCanvas("Feedback_IssueEntry",
                wristAnchor, new Vector2(WristCanvasWidth, WristCanvasHeight),
                WristCanvasScale, headCamera);
            wristCanvas.localPosition = WristLocalOffset;
            wristCanvas.localRotation = Quaternion.identity;
            wristCanvas.gameObject.AddComponent<WristPanelFacing>().Configure(headCamera != null ? headCamera.transform : null);

            // 底板：不参与射线，避免与入口按钮抢点击
            var background = FeedbackUi.CreatePanel("Entry_Background", wristCanvas,
                new Color(0.06f, 0.07f, 0.09f, 0.55f));
            FeedbackUi.Stretch(background);
            background.GetComponent<Image>().raycastTarget = false;

            entryButton = FeedbackUi.AddPlacedButton(wristCanvas, "ISSUES (0)", FeedbackUi.ButtonActive,
                TogglePanel, 44, 8f, 8f, WristCanvasWidth - 16f, 74f);

            statusText = FeedbackUi.AddPlacedText(wristCanvas, "No issues yet", 26, TextAnchor.MiddleCenter,
                FeedbackUi.DimText, 8f, 88f, WristCanvasWidth - 16f, 36f);

            FeedbackUi.SetLayerRecursively(wristCanvas.gameObject, FeedbackUi.UiLayerName);
        }

        private void OnDestroy()
        {
            ClosePanel();
        }

        private void Update()
        {
            // XR ↔ 桌面切换后头显相机可能变化，这里带节流地跟随刷新
            // （腕部与汇总面板都需要正确的 worldCamera）。
            // 不每帧解析：ResolveHeadCamera 内部会 FindObjectOfType，在几千个物体的场景里没必要每帧做。
            cameraPollTimer -= Time.deltaTime;
            if (cameraPollTimer <= 0f)
            {
                cameraPollTimer = CameraPollInterval;
                Camera resolved = FeedbackUi.ResolveHeadCamera();
                if (resolved != null && resolved != headCamera)
                {
                    headCamera = resolved;
                    if (wristCanvas != null) wristCanvas.GetComponent<Canvas>().worldCamera = headCamera;
                    var facing = wristCanvas != null ? wristCanvas.GetComponent<WristPanelFacing>() : null;
                    if (facing != null) facing.Configure(headCamera.transform);
                    if (listCanvas != null) listCanvas.GetComponent<Canvas>().worldCamera = headCamera;
                }
            }

            if (statusTimer > 0f)
            {
                statusTimer -= Time.deltaTime;
                if (statusTimer <= 0f) ApplyIdleStatus();
            }
        }

        /// <summary>恢复腕部状态行的空闲文案与配色。</summary>
        private void ApplyIdleStatus()
        {
            if (statusText == null) return;
            statusText.text = IdleStatus();
            statusText.color = FeedbackUi.DimText;
        }

        // ------------------------------------------------------------------
        // 对外操作
        // ------------------------------------------------------------------

        /// <summary>刷新计数与列表内容（新增/清空/修改问题后调用）。</summary>
        public void Refresh()
        {
            int count = service != null && service.Store != null ? service.Store.Count : 0;

            if (entryButton != null)
                FeedbackUi.SetButtonLabel(entryButton, "ISSUES (" + count + ")");

            if (statusTimer <= 0f) ApplyIdleStatus();

            if (listCanvas != null)
            {
                ClampPage(count);
                RebuildList();
            }
        }

        /// <summary>在腕部状态行显示一条短消息（提交成功、导出路径等），若干秒后恢复空闲文案。</summary>
        public void ShowStatus(string message, float seconds = 5f)
        {
            if (statusText == null) return;
            statusText.text = message;
            statusText.color = FeedbackUi.AccentText;
            statusTimer = Mathf.Max(0.5f, seconds);
        }

        public void TogglePanel()
        {
            if (listCanvas != null) ClosePanel();
            else OpenPanel();
        }

        public void OpenPanel()
        {
            if (listCanvas != null) return;

            headCamera = FeedbackUi.ResolveHeadCamera();
            if (headCamera == null)
            {
                ShowStatus("No head camera", 4f);
                return;
            }

            Transform head = headCamera.transform;
            Vector3 forward = head.forward;
            float distance = PanelSpawnDistance;

            // 与 D 的 AnnotationSpawner 同款射线避让：前方有障碍时把面板拉到障碍物之前，避免穿模。
            // 额外两点加固：(1) 排除玩家自身层，避免把面板贴到自己的胶囊上；
            // (2) 设下限距离——否则贴墙时障碍物距离很小，面板会糊在脸上无法阅读。
            int mask = BuildOcclusionMask();
            RaycastHit hit;
            if (Physics.Raycast(head.position, forward, out hit, PanelSpawnDistance, mask, QueryTriggerInteraction.Ignore))
            {
                distance = Mathf.Clamp(hit.distance - PanelObstacleGap, MinPanelDistance, PanelSpawnDistance);
            }

            Vector3 spawnPosition = head.position + forward * distance;

            listCanvas = FeedbackUi.CreateWorldSpaceCanvas("Feedback_IssueList", transform,
                new Vector2(PanelWidth, PanelHeight), PanelScale, headCamera);
            listCanvas.position = spawnPosition;

            // 朝向：与 S05_ReviewHud（WristPanelFacing）同口径 —— forward 背对观察者。
            // 注意：**不要**额外 Rotate(0,180,0)。本项目已有的 S05_ReviewHud 与 HudLazyFollow
            // 都用 LookRotation(面板位置 − 观察者位置) 让世界空间画布正面朝向观察者；
            // 再翻转 180° 会把整块面板渲染成镜像文字。
            // 极端情况防护：面板沿视线方向生成，若用户近乎垂直俯视/仰视（视线 ∥ up），
            // LookRotation 会报 "Look rotation viewing vector is zero" 并返回无效旋转，
            // 此时退化为水平朝向，保证面板仍正确可读。
            Vector3 facing = spawnPosition - head.position;
            if (Mathf.Abs(facing.y) > 0.999f * facing.magnitude)
            {
                facing = new Vector3(head.forward.x, 0f, head.forward.z);
                if (facing.sqrMagnitude < 1e-6f) facing = Vector3.back;
            }
            listCanvas.rotation = Quaternion.LookRotation(facing, Vector3.up);

            BuildPanelContent();
            page = 0;
            RebuildList();
        }

        /// <summary>面板避让用的遮挡层：排除玩家自身，避免面板被自己的胶囊顶到脸上。</summary>
        private static int BuildOcclusionMask()
        {
            int playerBody = LayerMask.NameToLayer("PlayerBody");
            return playerBody >= 0 ? ~(1 << playerBody) : ~0;
        }

        public void ClosePanel()
        {
            if (listCanvas == null) return;
            Destroy(listCanvas.gameObject);
            listCanvas = null;
            listTitle = null;
            listSummary = null;
            listRowsRoot = null;
            pageLabel = null;
            prevButton = null;
            nextButton = null;
        }

        // ------------------------------------------------------------------
        // 汇总面板构建
        // ------------------------------------------------------------------

        private void BuildPanelContent()
        {
            var background = FeedbackUi.CreatePanel("Panel_Background", listCanvas, FeedbackUi.PanelBg);
            FeedbackUi.Stretch(background);

            listTitle = FeedbackUi.AddPlacedText(listCanvas, "Issue List", 30, TextAnchor.MiddleLeft,
                FeedbackUi.TextPrimary, 20f, 14f, PanelWidth - 40f, 34f);

            listSummary = FeedbackUi.AddPlacedText(listCanvas, "", 18, TextAnchor.MiddleLeft,
                FeedbackUi.DimText, 20f, 50f, PanelWidth - 40f, 26f);

            listRowsRoot = FeedbackUi.CreateStretch("Rows", listCanvas);
            listRowsRoot.offsetMin = new Vector2(0f, 0f);
            listRowsRoot.offsetMax = new Vector2(0f, 0f);

            // 底部：上一页 / 页码 / 下一页 / 导出 / 关闭
            const float footerY = 512f;
            const float footerHeight = 52f;
            const float gap = 6f;
            float x = 20f;
            prevButton = FeedbackUi.AddPlacedButton(listCanvas, "PREV", FeedbackUi.ButtonNormal, PreviousPage, 20,
                x, footerY, 120f, footerHeight);
            x += 120f + gap;
            pageLabel = FeedbackUi.AddPlacedText(listCanvas, "1 / 1", 20, TextAnchor.MiddleCenter,
                FeedbackUi.TextPrimary, x, footerY, 120f, footerHeight);
            x += 120f + gap;
            nextButton = FeedbackUi.AddPlacedButton(listCanvas, "NEXT", FeedbackUi.ButtonNormal, NextPage, 20,
                x, footerY, 120f, footerHeight);
            x += 120f + gap;
            FeedbackUi.AddPlacedButton(listCanvas, "EXPORT CSV+JSON", FeedbackUi.PrimaryButton, ExportAll, 18,
                x, footerY, 218f, footerHeight);
            x += 218f + gap;
            FeedbackUi.AddPlacedButton(listCanvas, "CLOSE", FeedbackUi.DangerButton, ClosePanel, 20,
                PanelWidth - 20f - 118f, footerY, 118f, footerHeight);

            FeedbackUi.SetLayerRecursively(listCanvas.gameObject, FeedbackUi.UiLayerName);
        }

        private void RebuildList()
        {
            if (listRowsRoot == null) return;

            for (int i = listRowsRoot.childCount - 1; i >= 0; i--)
                Destroy(listRowsRoot.GetChild(i).gameObject);

            int count = service != null && service.Store != null ? service.Store.Count : 0;
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(count / (float)PageSize));
            page = Mathf.Clamp(page, 0, pageCount - 1);

            if (listTitle != null)
                listTitle.text = "Issue List (" + count + ")";

            if (listSummary != null && service != null && service.Store != null)
            {
                IssueStore store = service.Store;
                listSummary.text = "HIGH " + store.CountBySeverity(IssueSeverity.High)
                    + "   MED " + store.CountBySeverity(IssueSeverity.Medium)
                    + "   LOW " + store.CountBySeverity(IssueSeverity.Low)
                    + "        session " + store.SessionId;
            }

            if (pageLabel != null) pageLabel.text = (page + 1) + " / " + pageCount;
            if (prevButton != null) prevButton.interactable = page > 0;
            if (nextButton != null) nextButton.interactable = page < pageCount - 1;

            if (count == 0)
            {
                FeedbackUi.AddPlacedText(listRowsRoot, "No issues recorded yet.\n" +
                    "Point at an object, press the grip, then pick a type and submit.",
                    20, TextAnchor.MiddleCenter, FeedbackUi.DimText, 20f, 100f, PanelWidth - 40f, 200f);
                // 空态同样是逐次新建的节点，需要统一层后再返回
                FeedbackUi.SetLayerRecursively(listCanvas.gameObject, FeedbackUi.UiLayerName);
                return;
            }

            var all = service.Store.All;
            int firstIndex = page * PageSize;
            float top = 84f;
            for (int i = 0; i < PageSize; i++)
            {
                int recordIndex = firstIndex + i;
                if (recordIndex >= all.Count) break;
                BuildRow(all[recordIndex], top, i % 2 == 0);
                top += RowHeight + RowSpacing;
            }

            // 行是逐页新建的，需要重新统一层（与 S05_ReviewHud 的 UI 层口径保持一致）
            FeedbackUi.SetLayerRecursively(listCanvas.gameObject, FeedbackUi.UiLayerName);
        }

        /// <summary>
        /// 构建一行问题。
        /// 行宽 = PanelWidth − 左右各 20 边距 = 720，行内元素坐标必须落在这个范围内：
        /// 编号 8..64 ｜ 分类/严重程度/目标 72..452 ｜ 位置与时间 458..638 ｜ FOCUS 646..710 ｜
        /// 描述 72..646（第二行，与上面各元素靠 y 分离）。
        /// </summary>
        private void BuildRow(IssueRecord record, float top, bool alternate)
        {
            var row = FeedbackUi.CreatePanel("Row_" + record.id, listRowsRoot,
                alternate ? FeedbackUi.RowBg : FeedbackUi.RowBgAlt);
            FeedbackUi.PlaceStretchX(row, top, RowHeight, 20f, 20f);

            // 编号徽标（底色 = 分类色）
            var chip = FeedbackUi.CreatePanel("Chip", row, IssueCatalog.CategoryColor(record.CategoryEnum));
            FeedbackUi.Place(chip, 8f, 15f, 56f, 30f);
            var chipText = FeedbackUi.AddText(chip, "#" + record.id, 20, TextAnchor.MiddleCenter,
                new Color(0.08f, 0.08f, 0.08f, 1f));
            FeedbackUi.Stretch(chipText.rectTransform);

            // 分类 / 严重程度 / 目标
            string meta = IssueCatalog.CategoryLabel(record.CategoryEnum)
                + "  ·  " + IssueCatalog.SeverityLabel(record.SeverityEnum)
                + (string.IsNullOrEmpty(record.targetName) ? "" : "  ·  " + record.targetName);
            if (meta.Length > 28) meta = meta.Substring(0, 28) + "…";
            FeedbackUi.AddPlacedText(row, meta, 20, TextAnchor.MiddleLeft,
                IssueCatalog.SeverityColor(record.SeverityEnum), 72f, 8f, 380f, 26f);

            // 位置与时间
            FeedbackUi.AddPlacedText(row, record.position.ToString() + "   " + record.createdAt,
                16, TextAnchor.MiddleRight, FeedbackUi.DimText, 458f, 8f, 180f, 26f);

            // 描述（换行压平，避免撑破行高）
            string description = string.IsNullOrEmpty(record.description) ? "（无补充描述）" : record.description;
            description = description.Replace('\n', ' ').Replace('\r', ' ');
            if (description.Length > 64) description = description.Substring(0, 64) + "…";
            FeedbackUi.AddPlacedText(row, description, 18, TextAnchor.MiddleLeft,
                FeedbackUi.TextPrimary, 72f, 34f, 574f, 26f);

            // 定位（脉冲高亮该问题在场景中的空间标注）
            string id = record.id;
            FeedbackUi.AddPlacedButton(row, "FOCUS", FeedbackUi.ButtonActive,
                () => OnFocusClicked(id), 18, 646f, 15f, 64f, 32f);
        }

        // ------------------------------------------------------------------
        // 交互
        // ------------------------------------------------------------------

        private void OnFocusClicked(string id)
        {
            if (service == null) return;
            bool found = service.Markers != null && service.Markers.Highlight(id);
            ShowStatus(found ? "Highlighted #" + id : "#" + id + " not in scene", 4f);
        }

        private void PreviousPage()
        {
            page = Mathf.Max(0, page - 1);
            RebuildList();
        }

        private void NextPage()
        {
            page++;
            RebuildList();
        }

        private void ExportAll()
        {
            if (service == null) return;
            string csv = service.ExportCsv();
            string json = service.ExportJson();
            // 腕部状态行很窄，这里只给短提示；完整路径写入 Console（导出成功时两个方法内部已打印）
            if (string.IsNullOrEmpty(csv) && string.IsNullOrEmpty(json))
                ShowStatus("Export failed", 6f);
            else
                ShowStatus("Exported CSV+JSON", 8f);
        }

        private void ClampPage(int count)
        {
            int pageCount = Mathf.Max(1, Mathf.CeilToInt(count / (float)PageSize));
            page = Mathf.Clamp(page, 0, pageCount - 1);
        }

        private string IdleStatus()
        {
            int count = service != null && service.Store != null ? service.Store.Count : 0;
            return count == 0 ? "No issues yet" : "Tap to review issues";
        }
    }
}
