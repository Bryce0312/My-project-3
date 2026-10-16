using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// E 部分（反馈与问题管理）的总控与对外集成入口。
    ///
    /// ===== 需求覆盖 =====
    /// UN-03 关联到具体目标 · UN-04 标注持续可见 · UN-05 快速分类 · UN-06 补充描述 ·
    /// UN-07 汇总查看所有问题 · G4 结构化反馈 · G5 持续标注与问题汇总，
    /// 以及 PROJECT_BRIEF 的「问题类型、文字描述和严重程度输入」与「一条反馈至少应包含
    /// 空间位置、问题类型、用户描述和创建时间」。
    ///
    /// ===== 数据流 =====
    /// <code>
    /// 用户用手柄射线指向物体 + 按下 grip
    ///   → D 的 HoverDwellHighlighter / XRSimpleInteractable.selectEntered
    ///   → D 的 AnnotationSpawner 弹出世界空间标注面板（AnnotationUI）
    ///   → E 的 AnnotationBridge 用同一个 selectEntered 事件捕获目标对象与空间锚点
    ///   → E 的 AnnotationBinding 接管面板：5 类问题分类 + 3 档严重程度 + 描述 + Submit
    ///   → FeedbackService.SubmitAnnotation
    ///        ├─ IssueStore        结构化记录（含世界坐标、家具 ID、测量、时间）→ JSON/CSV
    ///        ├─ IssueMarkerManager 在锚点生成编号空间标注（提交后不消失）
    ///        └─ IssueListHud      刷新右腕 ISSUES(n) 入口与汇总面板
    /// </code>
    ///
    /// ===== 运行时自举 =====
    /// 场景 <c>SceneNew.unity</c> 现在由人工维护（<c>SceneNewBuilder</c> 只做缺失告警、不再自动重建），
    /// 其中已包含 D 手工摆放的标注对象。E 仍然坚持运行时自举、不往场景写任何序列化内容：
    /// 这样既不会与 D 的场景改动产生冲突，也便于单独开关 E（注释掉 Bootstrap 即可）。
    ///
    /// ===== 对外契约 =====
    /// - D（或任何模块）可直接调用：
    ///   <c>FeedbackService.Instance.SubmitAnnotation(target, worldPoint, category, severity, description, origin)</c>
    /// - C（测量）把结果送进来（二选一）：
    ///   <c>OfferMeasurement(new MeasurementSnapshot(label, meters, a, b))</c> /
    ///   <c>OfferMeasurementText("表面间隙：0.72 m")</c>
    ///   若在标注前测量，结果会被暂存并在下一次提交时自动附加（消费一次）。
    ///   此外 E 每 2 秒软读取一次测量面板读数，一旦读数变化即暂存，因此不改 C 的代码也能自动带上测量。
    /// - 未来 Designer Dashboard：订阅 <c>IssueCreated</c> / <c>IssueUpdated</c>，或读取 <c>Store.All</c>。
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public sealed class FeedbackService : MonoBehaviour
    {
        /// <summary>轮询测量模块读数的间隔（秒）。</summary>
        private const float MeasurementPollInterval = 2f;

        public static FeedbackService Instance { get; private set; }

        [Header("标注面板")]
        [Tooltip("由 E 接管 D 面板的分类区（5 类问题）与严重程度区（3 档）。关闭后改为映射 D 原有的三个分类按钮。")]
        public bool manageCategoryRow = true;

        [Header("诊断")]
        [Tooltip("打印桥接细节（捕获到的目标、绑定的面板），排查集成问题时打开。")]
        public bool logBridgeActivity = true;

        [Header("测量")]
        [Tooltip("周期性软读取测量模块的当前读数，读数变化时暂存并在下次提交时附加。")]
        public bool pullMeasurementFromModule = true;

        public event Action<IssueRecord> IssueCreated;
        public event Action<IssueRecord> IssueUpdated;

        private AnnotationBridge annotations;
        private IssueListHud hud;

        private bool hasPendingMeasurement;
        private MeasurementSnapshot pendingMeasurement;

        private float measurementPollTimer;
        private string lastMeasurementReadout = "";

        public IssueStore Store { get; private set; }
        public IssueMarkerManager Markers { get; private set; }
        public AnnotationBridge Annotations { get { return annotations; } }
        public IssueListHud Hud { get { return hud; } }
        public bool ManageCategoryRow { get { return manageCategoryRow; } }
        public bool LogBridgeActivity { get { return logBridgeActivity; } }
        public bool IsIssueListOpen { get { return hud != null && hud.IsOpen; } }
        public bool HasPendingMeasurement { get { return hasPendingMeasurement; } }

        // ------------------------------------------------------------------
        // 自举
        // ------------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindObjectOfType<FeedbackService>() != null) return;
            var go = new GameObject("FeedbackSystem");
            go.AddComponent<FeedbackService>();
        }

        private void Awake()
        {
            Instance = this;

            Store = new IssueStore();
            Store.SceneName = SceneManager.GetActiveScene().name;

            Markers = new IssueMarkerManager(transform);

            annotations = new AnnotationBridge(this);
            annotations.Initialize();

            hud = IssueListHud.Create(transform, this);
            hud.Refresh();

            Debug.Log("[反馈] FeedbackSystem 已启动。会话 " + Store.SessionId
                + "；已订阅标注交互对象 " + annotations.SubscribedInteractableCount
                + " 个；导出目录 " + IssueStore.DefaultExportDirectory);
        }

        private void Update()
        {
            if (annotations != null) annotations.Tick();

            if (pullMeasurementFromModule)
            {
                measurementPollTimer -= Time.deltaTime;
                if (measurementPollTimer <= 0f)
                {
                    measurementPollTimer = MeasurementPollInterval;
                    PollMeasurementModule();
                }
            }
        }

        private void OnDestroy()
        {
            if (annotations != null) annotations.Shutdown();
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            TrySaveSession("退出时保存");
        }

        // ------------------------------------------------------------------
        // 对外 API：提交标注（D → E）
        // ------------------------------------------------------------------

        /// <summary>
        /// 把一次标注固化为结构化反馈：写入数据层、在空间锚点生成编号标注、落盘并刷新汇总入口。
        /// D 的标注面板（经 <see cref="AnnotationBinding"/>）即通过本方法入库；
        /// 其它模块若自建标注入口，也可直接调用本方法。
        /// </summary>
        public IssueRecord SubmitAnnotation(GameObject target, Vector3 worldPoint,
            IssueCategory category, IssueSeverity severity, string description, string origin)
        {
            if (Store == null) return null;

            string furnitureId, displayName, objectName;
            IssueTargetResolver.Describe(target, out furnitureId, out displayName, out objectName);

            var draft = new IssueDraft
            {
                index = Store.PeekNextIndex,
                worldPoint = worldPoint,
                targetRoot = target,
                targetFurnitureId = furnitureId,
                targetName = displayName,
                targetObjectName = objectName,
                origin = string.IsNullOrEmpty(origin) ? "annotation" : origin,
                category = category,
                severity = severity,
                description = description ?? "",
                createdAt = DateTime.Now
            };

            // 若此前量过尺寸（且尚未被消费），自动附加到这条反馈上
            if (hasPendingMeasurement)
            {
                draft.measurement = pendingMeasurement;
                draft.hasMeasurement = true;
                hasPendingMeasurement = false;
            }

            IssueRecord record;
            try
            {
                record = Store.AddFromDraft(draft);
            }
            catch (Exception exception)
            {
                Debug.LogError("[反馈] 创建问题记录失败：" + exception);
                ShowStatus("Save failed", 6f);
                return null;
            }

            // 空间反馈：新建标注立刻脉冲一次，用户不用去腕部确认也能看到"标记已落下"
            IssueMarker marker = Markers != null ? Markers.Spawn(record) : null;
            if (marker != null) marker.Highlight(2.5f);

            TrySaveSession("提交后自动保存");
            if (hud != null)
            {
                hud.Refresh();
                hud.ShowStatus("Saved #" + record.id, 5f);
            }

            Debug.Log("[反馈] 已记录 ISSUE #" + record.id
                + " | " + record.category + " / " + record.severity
                + " | 目标 " + record.targetName
                + (string.IsNullOrEmpty(record.targetFurnitureId) ? "" : "(" + record.targetFurnitureId + ")")
                + " | 位置 " + record.position.ToString()
                + " | 描述 " + (string.IsNullOrEmpty(record.description) ? "（无）" : record.description)
                + (record.hasMeasurement ? " | 测量 " + record.measurementText : ""));

            var handler = IssueCreated;
            if (handler != null) handler(record);

            return record;
        }

        // ------------------------------------------------------------------
        // 对外 API：测量接入（C → E）
        // ------------------------------------------------------------------

        /// <summary>接收测量模块结果：有草稿时立即附加，否则暂存待下次提交自动附加。</summary>
        public void OfferMeasurement(MeasurementSnapshot snapshot)
        {
            if (!snapshot.IsValid) return;
            pendingMeasurement = snapshot;
            hasPendingMeasurement = true;
        }

        /// <summary>轻量契约：直接传入测量读数字符串（如 "表面间隙：0.72 m"）。</summary>
        public void OfferMeasurementText(string readoutText)
        {
            if (string.IsNullOrEmpty(readoutText)) return;
            OfferMeasurement(MeasurementBridge.Parse(readoutText));
        }

        public void ClearPendingMeasurement()
        {
            hasPendingMeasurement = false;
            pendingMeasurement = default(MeasurementSnapshot);
        }

        /// <summary>
        /// 软读取测量模块读数：C 的 <c>MeasureUIPanel.readout</c> 一旦变化即暂存为待附加测量。
        /// 反射失败（C 未合并 / 字段改名）时静默降级，不影响任何其它功能。
        /// </summary>
        private void PollMeasurementModule()
        {
            MeasurementSnapshot snapshot;
            string raw;
            if (!MeasurementBridge.TryRead(out snapshot, out raw)) return;
            if (raw == lastMeasurementReadout) return;

            lastMeasurementReadout = raw;
            if (!snapshot.IsValid) return;

            pendingMeasurement = snapshot;
            hasPendingMeasurement = true;

            if (logBridgeActivity)
                Debug.Log("[反馈] 已暂存测量结果供反馈附加：" + snapshot.ToDisplayText());
        }

        // ------------------------------------------------------------------
        // 对外 API：问题汇总与导出
        // ------------------------------------------------------------------

        public void OpenIssueList()
        {
            if (hud != null) hud.OpenPanel();
        }

        public void CloseIssueList()
        {
            if (hud != null) hud.ClosePanel();
        }

        public void ToggleIssueList()
        {
            if (hud != null) hud.TogglePanel();
        }

        public void ShowStatus(string message, float seconds = 5f)
        {
            if (hud != null) hud.ShowStatus(message, seconds);
        }

        /// <summary>导出会话 JSON，返回文件路径；失败返回 null。</summary>
        public string ExportJson()
        {
            try
            {
                string path = Store.ExportJson();
                Debug.Log("[反馈] 已导出 JSON：" + path);
                return path;
            }
            catch (Exception exception)
            {
                Debug.LogError("[反馈] 导出 JSON 失败：" + exception);
                return null;
            }
        }

        /// <summary>导出问题 CSV，返回文件路径；失败返回 null。</summary>
        public string ExportCsv()
        {
            try
            {
                string path = Store.ExportCsv();
                Debug.Log("[反馈] 已导出 CSV：" + path);
                return path;
            }
            catch (Exception exception)
            {
                Debug.LogError("[反馈] 导出 CSV 失败：" + exception);
                return null;
            }
        }

        /// <summary>清空本次会话的全部问题记录与空间标注。</summary>
        public void ClearAllIssues()
        {
            int count = Store != null ? Store.Count : 0;
            if (count == 0)
            {
                ShowStatus("Nothing to clear", 4f);
                return;
            }

            Store.Clear();
            if (Markers != null) Markers.Clear();
            if (hud != null) hud.Refresh();

            Debug.Log("[反馈] 已清空 " + count + " 条问题记录与其空间标注。");
            ShowStatus("Cleared " + count, 4f);
        }

        private void TrySaveSession(string reason)
        {
            try
            {
                if (Store == null || Store.Count == 0) return;
                string path = Store.SaveSession();
                Debug.Log("[反馈] " + reason + " → " + path + "（共 " + Store.Count + " 条）");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[反馈] 保存会话失败：" + exception.Message);
            }
        }

        // ------------------------------------------------------------------
        // 数据变更
        // ------------------------------------------------------------------

        /// <summary>外部修改了某条记录后调用：刷新标注配色/标签与汇总面板，并触发事件。</summary>
        public void NotifyRecordChanged(IssueRecord record)
        {
            if (record == null) return;

            IssueMarker marker = Markers != null ? Markers.Get(record.id) : null;
            if (marker != null)
            {
                marker.SetCategory(record.CategoryEnum);
                marker.SetLabelText("ISSUE #" + record.id + "\n" + IssueCatalog.CategoryEnglish(record.CategoryEnum));
            }

            if (Store != null) Store.NotifyChanged();
            if (hud != null) hud.Refresh();

            var handler = IssueUpdated;
            if (handler != null) handler(record);
        }
    }
}
