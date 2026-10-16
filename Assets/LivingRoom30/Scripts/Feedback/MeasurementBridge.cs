using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 与测量模块（C）的可选软桥接。**不产生编译期依赖**，因此：
    /// - E 的分支可独立编译运行（C 未合并时，本桥接只是返回「未检测到」）；
    /// - C 合并后无需任何改动即可自动生效。
    ///
    /// 工作方式：测量模块的读数会写入 <c>MeasureUIPanel</c> 的私有字段 <c>readout</c>（UGUI Text）。
    /// 本桥接按组件类型名定位该面板，用反射读取其当前读数文本并解析出数值。
    ///
    /// 推荐做法（更稳、长期更优）：在 C 的 <c>ShowPointToPoint</c> / <c>ShowObjectToObject</c> 末尾
    /// 各加一行 <c>FeedbackService.Instance?.OfferMeasurement(new MeasurementSnapshot(...));</c>
    /// 即可绕过反射。两种方式可共存。详见 Docs/E_Feedback_技术路线与集成说明.md。
    /// </summary>
    public static class MeasurementBridge
    {
        private const string MeasurementSystemName = "MeasurementSystem";
        private const string UiPanelTypeName = "MeasureUIPanel";
        private const string ReadoutFieldName = "readout";

        private static readonly Regex MeterNumber =
            new Regex(@"(\d+(?:\.\d+)?)\s*m\b", RegexOptions.Compiled);

        /// <summary>测量模块是否在线（是否存在 MeasurementSystem 与其读数面板）。</summary>
        public static bool IsAvailable
        {
            get { return FindReadoutText() != null; }
        }

        /// <summary>
        /// 读取测量模块当前显示的结果。
        /// 返回 false 表示模块不存在 / 尚无测量结果 / 结构已变更（均安全降级）。
        /// </summary>
        public static bool TryRead(out MeasurementSnapshot snapshot, out string rawText)
        {
            snapshot = default(MeasurementSnapshot);
            rawText = null;

            var readout = FindReadoutText();
            if (readout == null) return false;

            rawText = readout.text;
            if (string.IsNullOrEmpty(rawText)) return false;

            snapshot = Parse(rawText);
            return snapshot.IsValid;
        }

        /// <summary>
        /// 把任意「含 <c>数字 m</c> 的读数文本」解析为测量快照。
        /// 例："两点距离：0.72 m\n可行路线：1.05 m" → 0.72 m，标签「两点距离」。
        /// 亦公开给 C 使用：C 可直接调用 <c>FeedbackService.Instance.OfferMeasurementText(text)</c>。
        /// </summary>
        public static MeasurementSnapshot Parse(string text)
        {
            var snapshot = default(MeasurementSnapshot);
            if (string.IsNullOrEmpty(text)) return snapshot;

            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                Match match = MeterNumber.Match(line);
                if (!match.Success) continue;

                float meters;
                if (!float.TryParse(match.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out meters))
                    continue;

                snapshot.label = ExtractLabel(line, match.Value);
                snapshot.meters = meters;
                return snapshot;
            }

            // 无「m」单位时，退化取第一个数字（例如自定义读数）
            Match any = Regex.Match(text, @"(\d+(?:\.\d+)?)");
            if (any.Success)
            {
                float value;
                if (float.TryParse(any.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value))
                {
                    snapshot.label = text.Split('\n')[0].Trim();
                    snapshot.meters = value;
                }
            }
            return snapshot;
        }

        /// <summary>从一行读数中剥离数值部分，得到语义标签（"表面间隙：0.72 m" → "表面间隙"）。</summary>
        private static string ExtractLabel(string line, string matchedNumber)
        {
            int cut = line.IndexOf(matchedNumber, System.StringComparison.Ordinal);
            string head = cut > 0 ? line.Substring(0, cut) : line;
            head = head.TrimEnd(' ', '：', ':', '=', '-', '→');
            if (string.IsNullOrEmpty(head)) return "测量";
            // 多行读数中的中文全角冒号可能残留，统一清理
            return head.Trim();
        }

        /// <summary>按类型名定位测量 UI 面板的读数 Text（反射读取私有字段，失败即安全返回 null）。</summary>
        private static Text FindReadoutText()
        {
            var systemGo = GameObject.Find(MeasurementSystemName);
            if (systemGo == null) return null;

            var behaviours = systemGo.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null) continue;
                if (behaviour.GetType().Name != UiPanelTypeName) continue;

                FieldInfo field = behaviour.GetType().GetField(ReadoutFieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (field == null) return null;

                return field.GetValue(behaviour) as Text;
            }
            return null;
        }
    }
}
