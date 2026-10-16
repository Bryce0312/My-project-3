using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 把 D（选择与标注）的 <c>AnnotationUI</c> 标注面板接入 E 的反馈管线。
    ///
    /// ===== 为什么需要这一层 =====
    /// 实测 D 的 <c>Assets/XR interaction/Canvas.prefab</c>：面板只有「关闭」按钮绑定了
    /// <c>AnnotationUI.OnCancel</c>；**问题分类按钮与 Submit 按钮都没有绑定任何 onClick**，
    /// 且 <c>AnnotationUI.OnSubmit</c> 只做 <c>Debug.Log</c>。也就是说 D 交付的是一条
    /// 「能弹出、能关闭、但既不能选分类也不能提交」的流程。E 的职责正是
    /// 「问题分类 / 文字输入 / Submit / 数据记录 / 最终 Issue List」，
    /// 因此由 E 在**运行期**完成这块接线，同时补齐 PROJECT_BRIEF 要求的「严重程度」，
    /// 并把结果交给 <c>FeedbackService</c> 落库与生成空间标注。
    ///
    /// ===== 不改 D 的任何文件、不动预制体 =====
    /// 全部通过运行期补装完成：
    /// - 目标与锚点：由 <c>AnnotationBridge</c> 订阅 <c>XRSimpleInteractable.selectEntered</c> 捕获；
    /// - 描述文本：读取 D 的**公开字段** <c>AnnotationUI.descriptionInput.text</c>；
    /// - 提交按钮：由公开字段 <c>AnnotationUI.submitButtonText</c> 反查其父 Button；
    /// - 分类/严重程度：隐藏 D 那三个未接线且覆盖不全的分类按钮，在腾出的空白区建立 E 自己的两行分类 + 一行严重程度；
    /// - 关闭与高亮复位：调用 D 的公开方法 <c>AnnotationUI.CloseUI()</c>，不做重复实现。
    ///
    /// ===== 布局依据（取自 Canvas.prefab 实测坐标；画布 400×300，子物体以中心为锚）=====
    /// 已占用：标题 y 86..136、关闭 x 152..183 y 104..135、分类按钮 y 45..76 与 y -4..26、
    /// 描述 y -98..-18、Submit y -137..-107。
    /// 隐藏 D 的分类按钮后，可安全使用 y -11..75 的整幅宽度（x -188..188），按要求分三行排布：
    /// 行1 三个分类（SPACE / LAYOUT / SIZE）、行2 两个分类（CIRCULATION / ACCESSIBILITY）、
    /// 行3 三档严重程度（LOW / MEDIUM / HIGH）。
    /// 若日后 D 调整面板版式，只需修改本类顶部 <c>Row*</c> 常量（"距画布顶边的距离"）。
    /// </summary>
    public sealed class AnnotationBinding
    {
        // ---- 版式常量：含义为「距离画布顶边的距离」，画布高 300 ----
        private const float CanvasHeight = 300f;
        private const float RowMargin = 12f;
        private const float RowWidth = 400f - RowMargin * 2f;   // 376

        private const float Row1Top = 75f;    // 局部 y 49..75
        private const float Row1Height = 26f;
        private const float Row2Top = 105f;   // 局部 y 19..45
        private const float Row2Height = 26f;
        private const float Row3Top = 137f;   // 局部 y -11..13
        private const float Row3Height = 24f;

        private const int CategoryFontSize = 13;
        private const int WideCategoryFontSize = 14;
        private const int SeverityFontSize = 13;
        private const int SubmitFontSize = 14;

        /// <summary>E 注入的行容器名前缀（用于在收集面板按钮时排除自己）。</summary>
        private const string RowContainerPrefix = "FeedbackRow_";

        /// <summary>等待 D 的 Start() 建立描述输入框的宽限帧数。</summary>
        private const int DescriptionGraceFrames = 12;

        private readonly AnnotationUI ui;
        private readonly FeedbackService service;
        private readonly GameObject target;
        private readonly Vector3 anchorPoint;

        private readonly List<Button> categoryButtons = new List<Button>();
        private readonly List<IssueCategory> categoryValues = new List<IssueCategory>();
        private readonly List<Button> severityButtons = new List<Button>();
        private readonly List<IssueSeverity> severityValues = new List<IssueSeverity>();

        private Button submitButton;
        private int waitedFrames;
        private bool built;
        private bool submitted;
        private bool disposed;

        private IssueCategory category = IssueCategory.Space;
        private IssueSeverity severity = IssueSeverity.Medium;

        public IssueCategory Category { get { return category; } }
        public IssueSeverity Severity { get { return severity; } }
        public GameObject Target { get { return target; } }
        public Vector3 AnchorPoint { get { return anchorPoint; } }

        public AnnotationBinding(AnnotationUI annotationUi, FeedbackService feedbackService,
            GameObject targetObject, Vector3 worldAnchor)
        {
            ui = annotationUi;
            service = feedbackService;
            target = targetObject;
            anchorPoint = worldAnchor;
        }

        public bool IsAlive
        {
            get { return !disposed && ui != null; }
        }

        /// <summary>
        /// 分帧装配：D 的描述输入框在它自己的 <c>Start()</c> 里才创建，而 E 的 Update 可能先于该 Start
        /// 执行，因此按帧重试；超过宽限帧数后即按「无描述输入」装配，避免无限等待。
        /// </summary>
        public bool TryBuild()
        {
            if (built || disposed) return true;
            if (ui == null) { disposed = true; return true; }

            if (ui.submitButtonText == null)
            {
                if (++waitedFrames > DescriptionGraceFrames) { disposed = true; return true; }
                return false;
            }

            if (ui.descriptionInput == null && waitedFrames < DescriptionGraceFrames)
            {
                waitedFrames++;
                return false;
            }

            Build();
            built = true;
            return true;
        }

        /// <summary>面板被销毁时回收 E 添加的监听。</summary>
        public void Dispose()
        {
            disposed = true;
            if (submitButton != null) submitButton.onClick.RemoveListener(OnSubmitClicked);
        }

        // ------------------------------------------------------------------
        // 装配
        // ------------------------------------------------------------------

        private void Build()
        {
            GameObject canvasRoot = ui.gameObject;
            var canvasRect = canvasRoot.GetComponent<RectTransform>();
            if (canvasRect == null)
            {
                Debug.LogWarning("[反馈] AnnotationUI 根节点不是 RectTransform，无法注入反馈控件。");
                return;
            }

            Button closeButton = FindCloseButton(canvasRect);
            submitButton = FindSubmitButton(canvasRect);
            List<Button> externalCategoryButtons = CollectExternalButtons(canvasRect, submitButton, closeButton);

            // 分类：默认由 E 接管（D 的三个分类按钮既未接线、也覆盖不全需求枚举的 5 个分类）
            if (service == null || service.ManageCategoryRow)
            {
                for (int i = 0; i < externalCategoryButtons.Count; i++)
                    externalCategoryButtons[i].gameObject.SetActive(false);
                BuildCategoryRows(canvasRoot);
            }
            else
            {
                for (int i = 0; i < externalCategoryButtons.Count; i++)
                {
                    Button captured = externalCategoryButtons[i];
                    string label = ReadLabel(captured);
                    captured.onClick.AddListener(() => OnExternalCategoryClicked(label));
                }
            }

            // 严重程度：D 的面板没有该维度（PROJECT_BRIEF 核心评审能力 4 要求），由 E 补齐
            BuildSeverityRow(canvasRoot);

            if (submitButton != null)
            {
                submitButton.onClick.AddListener(OnSubmitClicked);
            }
            else
            {
                Debug.LogWarning("[反馈] 未能在标注面板中找到 Submit 按钮；提交链路不可用。");
            }

            RefreshSelectionVisuals();
        }

        /// <summary>
        /// 收集标注面板**自身的**按钮。
        ///
        /// 关键：只取画布**直接子级**上的 Button，不能用 <c>GetComponentsInChildren</c>。
        /// 原因：D 在 `AnnotationUI.Start()` 里会构建自带虚拟键盘
        /// （`IssueMarkerVirtualKeyboard`，约 40 个按键，初始 inactive），它同样挂在画布之下。
        /// 若用递归收集，那些键会被误判成"分类按钮"并连同键盘一起被隐藏，直接废掉 XR 内的文字输入。
        /// 实测版本中面板的自身按钮都是画布的直接子级（`size` / `cramped` / `layout` / submit / Btn_Close）。
        /// </summary>
        private static List<Button> CollectPanelButtons(RectTransform canvasRect)
        {
            var result = new List<Button>();
            for (int i = 0; i < canvasRect.childCount; i++)
            {
                Transform child = canvasRect.GetChild(i);
                // 跳过 E 自己注入的行容器（防御性：正常情况下绑定只发生一次）
                if (child.name.StartsWith(RowContainerPrefix, StringComparison.Ordinal)) continue;

                var button = child.GetComponent<Button>();
                if (button != null) result.Add(button);
            }
            return result;
        }

        /// <summary>Submit 按钮：用 D 的公开字段反查其父 Button，不依赖名字与标签文本。</summary>
        private Button FindSubmitButton(RectTransform canvasRect)
        {
            if (ui.submitButtonText != null)
            {
                Button fromText = ui.submitButtonText.GetComponentInParent<Button>();
                if (fromText != null) return fromText;
            }

            // 兜底：在面板自身按钮里按标签文本查找
            List<Button> buttons = CollectPanelButtons(canvasRect);
            for (int i = 0; i < buttons.Count; i++)
            {
                if (ReadLabel(buttons[i]).ToLowerInvariant().Contains("submit")) return buttons[i];
            }
            return null;
        }

        /// <summary>关闭按钮：预制体中名为 Btn_Close、标签为 "X"，已由 D 自己接线，E 不接管。</summary>
        private static Button FindCloseButton(RectTransform canvasRect)
        {
            List<Button> buttons = CollectPanelButtons(canvasRect);
            for (int i = 0; i < buttons.Count; i++)
            {
                if (buttons[i].gameObject.name.StartsWith("Btn_Close", StringComparison.Ordinal))
                    return buttons[i];
            }
            for (int i = 0; i < buttons.Count; i++)
            {
                string label = ReadLabel(buttons[i]).Trim();
                if (label.Length == 1 && (label == "X" || label == "x" || label == "×")) return buttons[i];
            }
            return null;
        }

        /// <summary>除 Submit 与 Close 之外的**面板自身按钮**即 D 的分类按钮。</summary>
        private static List<Button> CollectExternalButtons(RectTransform canvasRect, Button submit, Button close)
        {
            var result = new List<Button>();
            List<Button> buttons = CollectPanelButtons(canvasRect);
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                if (button == null || button == submit || button == close) continue;
                result.Add(button);
            }
            return result;
        }

        // ------------------------------------------------------------------
        // 控件构建
        // ------------------------------------------------------------------

        private void BuildCategoryRows(GameObject canvasRoot)
        {
            // 行 1：空间 / 布局 / 尺寸
            RectTransform row1 = AddRow(canvasRoot, "FeedbackRow_Category1", Row1Top, Row1Height);
            AddRowButton(row1, 3, 0, "SPACE", CategoryFontSize, IssueCategory.Space, true);
            AddRowButton(row1, 3, 1, "LAYOUT", CategoryFontSize, IssueCategory.Layout, true);
            AddRowButton(row1, 3, 2, "SIZE", CategoryFontSize, IssueCategory.Dimension, true);

            // 行 2：动线 / 可达性（标签较长，两个按钮均分整行）
            RectTransform row2 = AddRow(canvasRoot, "FeedbackRow_Category2", Row2Top, Row2Height);
            AddRowButton(row2, 2, 0, "CIRCULATION", WideCategoryFontSize, IssueCategory.Circulation, true);
            AddRowButton(row2, 2, 1, "ACCESSIBILITY", WideCategoryFontSize, IssueCategory.Accessibility, true);
        }

        private void BuildSeverityRow(GameObject canvasRoot)
        {
            RectTransform row3 = AddRow(canvasRoot, "FeedbackRow_Severity", Row3Top, Row3Height);
            AddRowSeverityButton(row3, 3, 0, "LOW", IssueSeverity.Low);
            AddRowSeverityButton(row3, 3, 1, "MEDIUM", IssueSeverity.Medium);
            AddRowSeverityButton(row3, 3, 2, "HIGH", IssueSeverity.High);
        }

        /// <summary>按要求「距画布顶边的距离」在画布上创建一个等分行的容器。</summary>
        private static RectTransform AddRow(GameObject canvasRoot, string name, float topFromTop, float height)
        {
            var row = FeedbackUi.NewChildRect(name, canvasRoot.transform);
            row.anchorMin = new Vector2(0.5f, 0.5f);
            row.anchorMax = new Vector2(0.5f, 0.5f);
            row.pivot = new Vector2(0.5f, 0.5f);
            row.sizeDelta = new Vector2(RowWidth, height);
            // 画布中心为局部原点：中心 Y = 半高 - 距顶距离 - 半行高
            row.anchoredPosition = new Vector2(0f, CanvasHeight * 0.5f - topFromTop - height * 0.5f);
            return row;
        }

        private void AddRowButton(RectTransform row, int cellCount, int cellIndex, string label,
            int fontSize, IssueCategory value, bool isCategory)
        {
            Button button = CreateRowCell(row, cellCount, cellIndex, label, fontSize, () => SelectCategory(value));
            if (isCategory)
            {
                categoryButtons.Add(button);
                categoryValues.Add(value);
            }
        }

        private void AddRowSeverityButton(RectTransform row, int cellCount, int cellIndex, string label,
            IssueSeverity value)
        {
            Button button = CreateRowCell(row, cellCount, cellIndex, label, SeverityFontSize,
                () => SelectSeverity(value));
            severityButtons.Add(button);
            severityValues.Add(value);
        }

        private static Button CreateRowCell(RectTransform row, int cellCount, int cellIndex,
            string label, int fontSize, Action onClick)
        {
            const float gap = 4f;
            float cellWidth = (RowWidth - gap * (cellCount - 1)) / cellCount;
            float x = cellIndex * (cellWidth + gap);
            float height = row.sizeDelta.y;

            Button button = FeedbackUi.AddButton(row, label, FeedbackUi.ButtonNormal, onClick, fontSize);
            FeedbackUi.Place(button.GetComponent<RectTransform>(), x, 0f, cellWidth, height);
            // D 的画布与其控件都在 UI 层，E 注入的按钮保持同一口径，避免日后有人给
            // TrackedDeviceGraphicRaycaster 开启 3D 遮挡检查时出现层不一致的问题。
            FeedbackUi.SetLayerRecursively(button.gameObject, FeedbackUi.UiLayerName);
            return button;
        }

        // ------------------------------------------------------------------
        // 选择与提交
        // ------------------------------------------------------------------

        /// <summary>兜底路径：把 D 的英文标签映射到需求的 5 个分类（仅当 ManageCategoryRow = false）。</summary>
        private void OnExternalCategoryClicked(string rawLabel)
        {
            SelectCategory(IssueCatalog.CategoryFromLabel(rawLabel));
        }

        private void SelectCategory(IssueCategory value)
        {
            category = value;
            RefreshSelectionVisuals();
        }

        private void SelectSeverity(IssueSeverity value)
        {
            severity = value;
            RefreshSelectionVisuals();
        }

        private void RefreshSelectionVisuals()
        {
            for (int i = 0; i < categoryButtons.Count; i++)
            {
                bool active = categoryValues[i] == category;
                FeedbackUi.SetButtonColor(categoryButtons[i],
                    active ? IssueCatalog.CategoryColor(category) : FeedbackUi.ButtonNormal);
            }

            for (int i = 0; i < severityButtons.Count; i++)
            {
                bool active = severityValues[i] == severity;
                FeedbackUi.SetButtonColor(severityButtons[i],
                    active ? IssueCatalog.SeverityColor(severity) : FeedbackUi.ButtonNormal);
            }

            // 同步 D 自己的分类状态（其 submitButtonText 随后由 E 覆写），保证两边一致
            if (ui != null) ui.OnSelectCategory(IssueCatalog.CategoryEnglish(category));

            StyleSubmitLabel();
        }

        /// <summary>
        /// 把当前选择压缩为两行写入 Submit 按钮。原按钮仅 91×29 单位，放不下完整标签，
        /// 且 D 原始的 24 号字会溢出，因此这里改小字号并允许换行。
        /// </summary>
        private void StyleSubmitLabel()
        {
            if (ui == null || ui.submitButtonText == null) return;

            ui.submitButtonText.enableWordWrapping = true;
            ui.submitButtonText.fontSize = SubmitFontSize;
            ui.submitButtonText.text = "Submit\n"
                + IssueCatalog.CategoryShortCode(category)
                + " / " + IssueCatalog.SeverityShort(severity);
        }

        private void OnSubmitClicked()
        {
            if (submitted || disposed) return;
            submitted = true;

            string description = ui != null && ui.descriptionInput != null
                ? ui.descriptionInput.text
                : string.Empty;

            if (service != null)
                service.SubmitAnnotation(target, anchorPoint, category, severity, description, "annotation");

            // 关闭与高亮复位交给 D 自己的实现，E 不重复处理
            if (ui != null) ui.CloseUI();
        }

        // ------------------------------------------------------------------
        // 标签读取（TMP 与 legacy Text 都兼容）
        // ------------------------------------------------------------------

        private static string ReadLabel(Button button)
        {
            if (button == null) return string.Empty;

            var tmp = button.GetComponentInChildren<TMP_Text>(true);
            if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text;

            var legacy = button.GetComponentInChildren<Text>(true);
            if (legacy != null && !string.IsNullOrEmpty(legacy.text)) return legacy.text;

            return string.Empty;
        }
    }
}
