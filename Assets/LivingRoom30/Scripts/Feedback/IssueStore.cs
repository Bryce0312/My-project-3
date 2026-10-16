using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 反馈数据的唯一记录中心（对应需求 UN-07 / 目标 G5 的「数据记录」）。
    ///
    /// 职责边界：
    /// - 只负责「问题记录的增删改查 + 序列化」，不涉及 UI、不涉及场景可视化、不涉及输入。
    /// - 会话内数据保存在内存；通过 <see cref="SaveSession"/> / <see cref="ExportCsv"/>
    ///   落盘到 <c>Application.persistentDataPath/xr-review/</c>，为未来的 Designer Dashboard 预留数据基础。
    ///
    /// 线程模型：仅主线程访问（Unity 生命周期内调用），无锁。
    /// </summary>
    public sealed class IssueStore
    {
        private readonly List<IssueRecord> issues = new List<IssueRecord>();

        /// <summary>本次会话标识（时间戳派生，稳定且可读）。</summary>
        public string SessionId { get; private set; }

        /// <summary>会话开始时间。</summary>
        public DateTime StartedAt { get; private set; }

        /// <summary>场景名（写入导出文件，便于区分来源）。</summary>
        public string SceneName { get; set; }

        /// <summary>新增、修改、清空时触发，供 UI 与外部模块（未来 Dashboard）订阅。</summary>
        public event Action Changed;

        public IssueStore()
        {
            StartedAt = DateTime.Now;
            SessionId = StartedAt.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            SceneName = "";
        }

        public int Count { get { return issues.Count; } }

        /// <summary>只读视图（不要直接修改返回的元素，改后须调用 <see cref="NotifyChanged"/>）。</summary>
        public IList<IssueRecord> All { get { return issues.AsReadOnly(); } }

        /// <summary>下一条记录的展示序号（1 起）。</summary>
        public int PeekNextIndex { get { return issues.Count + 1; } }

        public IssueRecord GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].id == id) return issues[i];
            return null;
        }

        /// <summary>当前会话中同分类的记录数（列表分组统计用）。</summary>
        public int CountByCategory(IssueCategory category)
        {
            int n = 0;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].CategoryEnum == category) n++;
            return n;
        }

        /// <summary>按严重程度统计（用于问题汇总头部摘要）。</summary>
        public int CountBySeverity(IssueSeverity severity)
        {
            int n = 0;
            for (int i = 0; i < issues.Count; i++)
                if (issues[i].SeverityEnum == severity) n++;
            return n;
        }

        /// <summary>
        /// 以草稿为蓝本创建并登记一条问题记录。由 FeedbackService 在 Submit 时调用。
        /// 序号与 ID 在此处统一分配，保证唯一与连续。
        /// </summary>
        public IssueRecord AddFromDraft(IssueDraft draft)
        {
            if (draft == null) throw new ArgumentNullException("draft");

            int index = issues.Count + 1;
            var record = new IssueRecord
            {
                id = index.ToString("00"),
                index = index,
                sessionId = SessionId,
                origin = draft.origin ?? "",
                category = draft.category.ToString(),
                severity = draft.severity.ToString(),
                description = draft.description ?? "",
                position = new Vec3(draft.worldPoint),
                targetFurnitureId = draft.targetFurnitureId ?? "",
                targetName = draft.targetName ?? "",
                targetObjectName = draft.targetObjectName ?? "",
                hasMeasurement = draft.hasMeasurement,
                measurementMeters = draft.hasMeasurement ? draft.measurement.meters : 0f,
                measurementText = draft.hasMeasurement ? draft.measurement.ToDisplayText() : "",
                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };

            issues.Add(record);
            RaiseChanged();
            return record;
        }

        /// <summary>外部模块（或加载流程）直接追加一条记录时使用；序号与 ID 会自动补齐。</summary>
        public IssueRecord Add(IssueRecord record)
        {
            if (record == null) throw new ArgumentNullException("record");

            if (record.index <= 0) record.index = issues.Count + 1;
            if (string.IsNullOrEmpty(record.id)) record.id = record.index.ToString("00");
            if (string.IsNullOrEmpty(record.sessionId)) record.sessionId = SessionId;

            issues.Add(record);
            RaiseChanged();
            return record;
        }

        public bool Remove(string id)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].id != id) continue;
                issues.RemoveAt(i);
                Renumber();
                RaiseChanged();
                return true;
            }
            return false;
        }

        public void Clear()
        {
            if (issues.Count == 0) return;
            issues.Clear();
            RaiseChanged();
        }

        /// <summary>外部就地修改了某条记录后调用，用于触发 UI 刷新（例如 List 面板内改状态）。</summary>
        public void NotifyChanged() { RaiseChanged(); }

        private void RaiseChanged()
        {
            var handler = Changed;
            if (handler != null) handler();
        }

        /// <summary>删除后重排序号，保证 #01..#NN 连续（ID 与 index 同步更新）。</summary>
        private void Renumber()
        {
            for (int i = 0; i < issues.Count; i++)
            {
                issues[i].index = i + 1;
                issues[i].id = (i + 1).ToString("00");
            }
        }

        // ------------------------------------------------------------------
        // 序列化 / 导出
        // ------------------------------------------------------------------

        /// <summary>组装为可序列化的会话对象。</summary>
        public IssueSession BuildSession()
        {
            var session = new IssueSession
            {
                sceneName = SceneName ?? "",
                sessionId = SessionId,
                startedAt = StartedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };
            session.issues.AddRange(issues);
            return session;
        }

        /// <summary>会话 JSON 文本（缩进，便于人工阅读与后续工具消费）。</summary>
        public string ToJson()
        {
            return JsonUtility.ToJson(BuildSession(), true);
        }

        /// <summary>导出目录：Application.persistentDataPath/xr-review（跨平台可写）。</summary>
        public static string DefaultExportDirectory
        {
            get { return Path.Combine(Application.persistentDataPath, "xr-review"); }
        }

        /// <summary>
        /// 保存本次会话 JSON。返回写入的文件绝对路径。
        /// 使用「先写临时文件再替换」的原子写法，避免中途失败留下半个文件。
        /// </summary>
        public string SaveSession(string directory = null)
        {
            string dir = string.IsNullOrEmpty(directory) ? DefaultExportDirectory : directory;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "session_" + SessionId + ".json");
            WriteTextAtomic(path, ToJson());
            return path;
        }

        /// <summary>导出 JSON 到指定目录（文件名带会话时间戳）。</summary>
        public string ExportJson(string directory = null)
        {
            return SaveSession(directory);
        }

        /// <summary>导出 CSV 到指定目录：一行一条问题，字段与 IssueRecord 对齐，供表格软件/设计师查阅。</summary>
        public string ExportCsv(string directory = null)
        {
            string dir = string.IsNullOrEmpty(directory) ? DefaultExportDirectory : directory;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "issues_" + SessionId + ".csv");

            var sb = new StringBuilder();
            sb.AppendLine("index,id,category,severity,description," +
                          "posX,posY,posZ,targetFurnitureId,targetName,measurementMeters,measurementText," +
                          "createdAt,sessionId,origin");

            for (int i = 0; i < issues.Count; i++)
            {
                var r = issues[i];
                sb.Append(r.index).Append(',');
                sb.Append(Csv(r.id)).Append(',');
                sb.Append(Csv(r.category)).Append(',');
                sb.Append(Csv(r.severity)).Append(',');
                sb.Append(Csv(r.description)).Append(',');
                sb.Append(r.position.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.position.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(r.position.z.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(Csv(r.targetFurnitureId)).Append(',');
                sb.Append(Csv(r.targetName)).Append(',');
                sb.Append(r.measurementMeters.ToString("F3", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(Csv(r.measurementText)).Append(',');
                sb.Append(Csv(r.createdAt)).Append(',');
                sb.Append(Csv(r.sessionId)).Append(',');
                sb.Append(Csv(r.origin));
                sb.AppendLine();
            }

            WriteTextAtomic(path, sb.ToString());
            return path;
        }

        /// <summary>从 JSON 文本载入（覆盖当前内存列表）；用于回看历史会话。</summary>
        public void LoadFromJson(string json)
        {
            var session = JsonUtility.FromJson<IssueSession>(json);
            if (session == null || session.issues == null) return;

            issues.Clear();
            issues.AddRange(session.issues);
            if (!string.IsNullOrEmpty(session.sessionId)) SessionId = session.sessionId;
            SceneName = session.sceneName ?? SceneName;
            Renumber();
            RaiseChanged();
        }

        // ------------------------------------------------------------------
        // 内部工具
        // ------------------------------------------------------------------

        private static void WriteTextAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(true)); // 带 BOM，Excel 打开中文不乱码
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>CSV 字段转义：含逗号/引号/换行时整体加引号，内部引号翻倍。</summary>
        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            bool needQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 ||
                             value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            if (!needQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
