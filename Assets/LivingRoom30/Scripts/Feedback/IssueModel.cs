using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 问题类型（对应需求 UN-05「快速分类问题」）。
    /// 顺序即 UI 呈现顺序，追加新类型请只追加在末尾，避免影响已持久化记录的枚举名映射
    /// （记录中保存的是枚举名字符串，不是序号）。
    /// </summary>
    public enum IssueCategory
    {
        Space = 0,         // 空间 / Scale & Proportion
        Layout = 1,        // 布局 / Layout
        Circulation = 2,   // 动线 / Circulation
        Accessibility = 3, // 可达性 / Accessibility
        Dimension = 4      // 尺寸 / Dimension
    }

    /// <summary>问题严重程度（对应 PROJECT_BRIEF「核心设计评审能力 4：问题类型、文字描述和严重程度输入」）。</summary>
    public enum IssueSeverity
    {
        Low = 0,
        Medium = 1,
        High = 2
    }

    /// <summary>
    /// 分类 / 严重程度的展示元数据集中处：中英文标签与配色。
    /// 3D 空间 Marker 使用分类配色，UI 列表使用严重程度配色。
    /// </summary>
    public static class IssueCatalog
    {
        public static readonly IssueCategory[] Categories =
        {
            IssueCategory.Space, IssueCategory.Layout, IssueCategory.Circulation,
            IssueCategory.Accessibility, IssueCategory.Dimension
        };

        public static readonly IssueSeverity[] Severities =
        {
            IssueSeverity.Low, IssueSeverity.Medium, IssueSeverity.High
        };

        /// <summary>分类中文短标签（UI 按钮用，尽量 ≤ 5 字）。</summary>
        public static string CategoryLabel(IssueCategory c)
        {
            switch (c)
            {
                case IssueCategory.Space: return "空间";
                case IssueCategory.Layout: return "布局";
                case IssueCategory.Circulation: return "动线";
                case IssueCategory.Accessibility: return "可达性";
                case IssueCategory.Dimension: return "尺寸";
                default: return c.ToString();
            }
        }

        /// <summary>分类英文标签（用于 3D TextMesh 标签；ASCII 以避免缺字）。</summary>
        public static string CategoryEnglish(IssueCategory c)
        {
            switch (c)
            {
                case IssueCategory.Space: return "Space";
                case IssueCategory.Layout: return "Layout";
                case IssueCategory.Circulation: return "Circulation";
                case IssueCategory.Accessibility: return "Accessibility";
                case IssueCategory.Dimension: return "Dimension";
                default: return c.ToString();
            }
        }

        /// <summary>分类配色：用于空间 Marker 图钉与标签。</summary>
        public static Color CategoryColor(IssueCategory c)
        {
            switch (c)
            {
                case IssueCategory.Space: return new Color(0.96f, 0.72f, 0.20f);       // 琥珀
                case IssueCategory.Layout: return new Color(0.55f, 0.45f, 0.95f);      // 紫
                case IssueCategory.Circulation: return new Color(0.22f, 0.72f, 0.95f); // 青蓝
                case IssueCategory.Accessibility: return new Color(0.30f, 0.80f, 0.55f);// 绿
                case IssueCategory.Dimension: return new Color(0.95f, 0.45f, 0.42f);   // 珊瑚红
                default: return Color.white;
            }
        }

        public static string SeverityLabel(IssueSeverity s)
        {
            switch (s)
            {
                case IssueSeverity.Low: return "轻微";
                case IssueSeverity.Medium: return "中等";
                case IssueSeverity.High: return "严重";
                default: return s.ToString();
            }
        }

        public static string SeverityEnglish(IssueSeverity s)
        {
            switch (s)
            {
                case IssueSeverity.Low: return "Low";
                case IssueSeverity.Medium: return "Medium";
                case IssueSeverity.High: return "High";
                default: return s.ToString();
            }
        }

        public static Color SeverityColor(IssueSeverity s)
        {
            switch (s)
            {
                case IssueSeverity.Low: return new Color(0.19f, 0.64f, 0.42f);   // 绿
                case IssueSeverity.Medium: return new Color(0.96f, 0.65f, 0.14f);// 橙
                case IssueSeverity.High: return new Color(0.90f, 0.28f, 0.30f);  // 红
                default: return Color.white;
            }
        }

        /// <summary>安全解析：未知字符串回退到默认值，避免旧记录/手工编辑导致崩溃。</summary>
        public static IssueCategory ParseCategory(string name, IssueCategory fallback = IssueCategory.Space)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            try { return (IssueCategory)Enum.Parse(typeof(IssueCategory), name, true); }
            catch { return fallback; }
        }

        public static IssueSeverity ParseSeverity(string name, IssueSeverity fallback = IssueSeverity.Medium)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            try { return (IssueSeverity)Enum.Parse(typeof(IssueSeverity), name, true); }
            catch { return fallback; }
        }

        /// <summary>
        /// 分类短代码：用于空间受限的按钮（D 的标注面板 Submit 键只有约 91×29 单位）。
        /// 保持全大写 ASCII，避免在 TMP 默认字体（不含中文字形）下出现缺字。
        /// </summary>
        public static string CategoryShortCode(IssueCategory c)
        {
            switch (c)
            {
                case IssueCategory.Space: return "SPACE";
                case IssueCategory.Layout: return "LAYOUT";
                case IssueCategory.Circulation: return "CIRCUL";
                case IssueCategory.Accessibility: return "ACCESS";
                case IssueCategory.Dimension: return "SIZE";
                default: return "ISSUE";
            }
        }

        /// <summary>严重程度短代码（3 字母，供紧凑按钮与小标签使用）。</summary>
        public static string SeverityShort(IssueSeverity s)
        {
            switch (s)
            {
                case IssueSeverity.Low: return "LOW";
                case IssueSeverity.Medium: return "MED";
                case IssueSeverity.High: return "HIGH";
                default: return "MED";
            }
        }

        /// <summary>
        /// 把任意分类文案（中/英文，来自按钮标签或外部输入）映射到需求枚举。
        ///
        /// 关键词语序很重要：先匹配更具体的分类，再落到默认值。
        /// 例如 "too cramped"（间距/通行局促）优先归入 Circulation（动线），而不是 Space。
        /// 这张表是唯一的映射入口，团队若调整口径只需改这里。
        /// </summary>
        public static IssueCategory CategoryFromLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return IssueCategory.Space;
            string text = label.ToLowerInvariant();

            if (ContainsAny(text, "size", "dimension", "length", "height", "width", "尺寸", "大小", "长短", "高低"))
                return IssueCategory.Dimension;
            if (ContainsAny(text, "circul", "cramped", "clearance", "corridor", "walkway", "passage",
                                   "动线", "通行", "通道", "局促", "拥挤", "净空", "间距"))
                return IssueCategory.Circulation;
            if (ContainsAny(text, "access", "reach", "reachab", "approach", "可达", "够不到", "难以接近", "进出"))
                return IssueCategory.Accessibility;
            if (ContainsAny(text, "layout", "arrange", "arrangement", "place", "position", "布局", "布置", "摆放"))
                return IssueCategory.Layout;
            if (ContainsAny(text, "space", "proportion", "scale", "space", "空间", "比例", "尺度"))
                return IssueCategory.Space;

            return IssueCategory.Space;
        }

        private static bool ContainsAny(string text, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
                if (text.Contains(keys[i])) return true;
            return false;
        }
    }

    /// <summary>JsonUtility 可序列化的 Vector3（避免平台差异，同时便于 CSV 展平与人工阅读）。</summary>
    [Serializable]
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vec3(Vector3 v) { x = v.x; y = v.y; z = v.z; }

        public Vector3 ToVector3() { return new Vector3(x, y, z); }

        public override string ToString() { return x.ToString("F2") + ", " + y.ToString("F2") + ", " + z.ToString("F2"); }
    }

    /// <summary>
    /// 测量快照：由 C（测量模块）产生、E 附加到某条反馈上。
    /// 设计为纯数据契约，使 E 不依赖 C 的任何类型——C 只需构造本结构调用
    /// <c>FeedbackService.Instance.OfferMeasurement(...)</c> 即可完成融合。
    /// </summary>
    [Serializable]
    public struct MeasurementSnapshot
    {
        /// <summary>人类可读的测量描述，如 "Sofa ↔ Coffee Table 表面间隙"。可为空。</summary>
        public string label;
        /// <summary>主数值（米）。&lt;= 0 时视为无主数值。</summary>
        public float meters;
        /// <summary>测量起点（世界坐标）。</summary>
        public Vec3 from;
        /// <summary>测量终点（世界坐标）。</summary>
        public Vec3 to;

        public bool IsValid { get { return !string.IsNullOrEmpty(label) || meters > 0f; } }

        public MeasurementSnapshot(string label, float meters, Vector3 from, Vector3 to)
        {
            this.label = label;
            this.meters = meters;
            this.from = new Vec3(from);
            this.to = new Vec3(to);
        }

        /// <summary>展平为一行文本，供 UI 显示与 CSV 导出。</summary>
        public string ToDisplayText()
        {
            string head = string.IsNullOrEmpty(label) ? "测量" : label;
            if (meters > 0f) return head + "：" + meters.ToString("F2") + " m";
            return head;
        }
    }

    /// <summary>
    /// 一条已提交的问题反馈（对应 PROJECT_BRIEF「一条反馈至少应包含空间位置、问题类型、
    /// 用户描述和创建时间」，并在能关联家具时保存目标对象标识）。
    /// 字段全部为 public 且基础类型，保证 JsonUtility 可完整往返。
    /// </summary>
    [Serializable]
    public class IssueRecord
    {
        // ---- 标识 ----
        public string id = "";          // 稳定 ID："01"、"02" …
        public int index;               // 展示序号（1 起）
        public string sessionId = "";   // 所属会话
        public string origin = "";      // 触发来源：annotation / measurement / probe

        // ---- 内容（结构化反馈）----
        public string category = "";    // IssueCategory 枚举名
        public string severity = "";    // IssueSeverity 枚举名
        public string description = ""; // 用户补充描述，可空

        // ---- 空间定位 ----
        public Vec3 position;           // 用户指向的世界坐标
        public string targetFurnitureId = ""; // FurnitureInfo.FurnitureId（如 F01）
        public string targetName = "";        // FurnitureInfo.EnglishName 或物体名
        public string targetObjectName = "";  // GameObject 名（保留层级线索）

        // ---- 附加测量（可选）----
        public bool hasMeasurement;
        public float measurementMeters;
        public string measurementText = "";

        // ---- 时间 ----
        public string createdAt = "";   // ISO 8601（本地时区）

        public IssueCategory CategoryEnum { get { return IssueCatalog.ParseCategory(category); } }
        public IssueSeverity SeverityEnum { get { return IssueCatalog.ParseSeverity(severity); } }

        /// <summary>单行摘要（列表第二行）。</summary>
        public string SummaryLine()
        {
            string target = string.IsNullOrEmpty(targetName) ? "空间位置" : targetName;
            return target + "  ·  " + (string.IsNullOrEmpty(description) ? "（无补充描述）" : description);
        }
    }

    /// <summary>
    /// 一次评审会话的完整记录（持久化根对象）。
    /// 版本号用于未来的字段迁移。
    /// </summary>
    [Serializable]
    public class IssueSession
    {
        public int schemaVersion = 1;
        public string project = "XR Spatial Review";
        public string sceneName = "";
        public string sessionId = "";
        public string startedAt = "";
        public string exportedAt = "";
        public List<IssueRecord> issues = new List<IssueRecord>();
    }

    /// <summary>
    /// 运行时草稿：D（选择与标注）指向某处后创建，经 UI 补充分类/严重程度/描述，
    /// Submit 后固化为 <see cref="IssueRecord"/>。
    /// 草稿本身不持久化。
    /// </summary>
    public class IssueDraft
    {
        public int index;                 // 预留序号（提交时可能因并发清空而微调）
        public Vector3 worldPoint;
        public GameObject targetRoot;     // 命中的家具根（可为 null）
        public string targetFurnitureId = "";
        public string targetName = "";
        public string targetObjectName = "";
        public string origin = "";
        public DateTime createdAt;

        public IssueCategory category = IssueCategory.Space;
        public IssueSeverity severity = IssueSeverity.Medium;
        public string description = "";
        public MeasurementSnapshot measurement;
        public bool hasMeasurement;

        public string DisplayId { get { return index.ToString("00"); } }
    }
}
