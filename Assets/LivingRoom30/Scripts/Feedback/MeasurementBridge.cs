using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 与测量模块（C）的可选软桥接：**不产生编译期依赖**，C 未合并或结构变更时静默降级。
    ///
    /// ===== 两个数据来源（按可靠性排序） =====
    /// 1. <b>可视化标签池</b>——这是当前 main 版本上**真正**承载测量数值的地方。
    ///    实测：`SpatialMeasureManager.Awake` 里执行了 `ui.showSidePanel = false`，而
    ///    `MeasureUIPanel.Build()` 在创建 `readout` 之前就 `return` 了，因此
    ///    <c>MeasureUIPanel.readout</c> **永远是 null**；测量结果实际只以
    ///    `MeasurementVisualizer` 的 3D `TextMesh` 标签呈现（"0.72 m"）。
    ///    因此本桥接扫描 `MeasurementSystem/Visualizer` 的 `labelPool`。
    /// 2. <b>读数面板</b>——若日后有人启用侧边面板（或 C 恢复该字段），优先使用它，
    ///    因为它带有"两点距离 / 表面间隙"这类语义前缀。
    ///
    /// ===== 为什么要"恰好 1 个数值标签"才认 =====
    /// 两点距离与两物体距离只产生 1 个数值标签；而家具尺寸模式会一次性画出 12 条包围盒棱线、
    /// 即 12 个数值标签、以及带"路径"后缀的路线标签。用"恰好 1 个、且不含路径后缀"来区分，
    /// 可以避免把一次"查看家具尺寸"误当成"量了一段距离"附加到反馈上。
    ///
    /// ===== 推荐的长期做法 =====
    /// 在 C 的 <c>ShowPointToPoint</c> / <c>ShowObjectToObject</c> 末尾各加一行
    /// <c>FeedbackService.Instance?.OfferMeasurement(new MeasurementSnapshot(...))</c>，
    /// 或调用 <c>OfferMeasurementText(读数)</c>，即可完全绕开反射。
    /// </summary>
    public static class MeasurementBridge
    {
        private const string MeasurementSystemName = "MeasurementSystem";
        private const string UiPanelTypeName = "MeasureUIPanel";
        private const string VisualizerTypeName = "MeasurementVisualizer";
        private const string PrimaryVisualizerName = "Visualizer";
        private const string ProximityVisualizerName = "ProximityVisualizer";
        private const string ReadoutFieldName = "readout";
        private const string LabelPoolFieldName = "labelPool";

        /// <summary>路径标签的后缀（C 用 <c>PlaceLabel(mid, "1.05 m 路径")</c> 标注路线总长）。</summary>
        private const string PathLabelSuffix = "路径";

        /// <summary>无法从读数里解析出语义前缀时使用的兜底标签。</summary>
        private const string DefaultDistanceLabel = "距离";

        private static readonly Regex MeterNumber =
            new Regex(@"(\d+(?:\.\d+)?)\s*m\b", RegexOptions.Compiled);

        private static readonly Regex ContainsDigit = new Regex(@"\d", RegexOptions.Compiled);

        /// <summary>测量模块是否在线（可视化标签池或读数面板至少有一处可定位）。</summary>
        public static bool IsAvailable
        {
            get
            {
                Text readout = FindReadoutText();
                if (readout != null && !string.IsNullOrEmpty(readout.text)) return true;
                return FindMeasurementVisualizer() != null;
            }
        }

        /// <summary>
        /// 读取测量模块当前的结果。
        /// 返回 false 表示模块不存在 / 尚无有效测量 / 结构已变更（均安全降级，不影响反馈其它功能）。
        /// </summary>
        public static bool TryRead(out MeasurementSnapshot snapshot, out string rawText)
        {
            snapshot = default(MeasurementSnapshot);
            rawText = null;

            // 来源 1：读数面板（当前版本上为 null，保留以兼容 C 的后续改动）
            Text readout = FindReadoutText();
            if (readout != null && !string.IsNullOrEmpty(readout.text))
            {
                MeasurementSnapshot fromReadout = Parse(readout.text);
                if (fromReadout.IsValid)
                {
                    snapshot = fromReadout;
                    rawText = readout.text;
                    return true;
                }
            }

            // 来源 2：可视化标签池（当前版本上真正有效的来源）
            return TryReadVisualizerLabel(out snapshot, out rawText);
        }

        /// <summary>
        /// 把任意「含 <c>数字 m</c> 的文本」解析为测量快照。
        /// 例："两点距离：0.72 m\n可行路线：1.05 m" → 0.72 m，标签「两点距离」；
        /// 例："0.72 m"（3D 标签原文）→ 0.72 m，标签「距离」。
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

            // 无「m」单位时退化为取第一个数字（例如自定义读数）
            Match any = Regex.Match(text, @"(\d+(?:\.\d+)?)");
            if (any.Success)
            {
                float value;
                if (float.TryParse(any.Groups[1].Value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value))
                {
                    snapshot.label = DefaultDistanceLabel;
                    snapshot.meters = value;
                }
            }
            return snapshot;
        }

        // ------------------------------------------------------------------
        // 来源 2：可视化标签池
        // ------------------------------------------------------------------

        private static bool TryReadVisualizerLabel(out MeasurementSnapshot snapshot, out string rawText)
        {
            snapshot = default(MeasurementSnapshot);
            rawText = null;

            MonoBehaviour visualizer = FindMeasurementVisualizer();
            if (visualizer == null) return false;

            FieldInfo poolField = visualizer.GetType().GetField(LabelPoolFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (poolField == null) return false;

            var pool = poolField.GetValue(visualizer) as IEnumerable;
            if (pool == null) return false;

            string single = null;
            int numericLabels = 0;

            foreach (object item in pool)
            {
                var labelObject = item as GameObject;
                if (labelObject == null || !labelObject.activeSelf) continue;

                var mesh = labelObject.GetComponent<TextMesh>();
                if (mesh == null || string.IsNullOrEmpty(mesh.text)) continue;

                string label = mesh.text;
                if (label.Contains(PathLabelSuffix)) continue;   // 路线总长不算直接测量值
                if (!MeterNumber.IsMatch(label)) continue;       // 必须是 "<数字> m"

                numericLabels++;
                if (single == null) single = label;
            }

            // 恰好 1 个数值标签 = 一次两点/两物体距离测量；12 个 = 家具尺寸线框，不作为测量附加
            if (numericLabels != 1 || string.IsNullOrEmpty(single)) return false;

            MeasurementSnapshot parsed = Parse(single);
            if (!parsed.IsValid) return false;

            snapshot = parsed;
            rawText = single;
            return true;
        }

        /// <summary>
        /// 定位承载点击式测量的那个 <c>MeasurementVisualizer</c>：
        /// 优先 <c>MeasurementSystem/Visualizer</c>，跳过邻近测距用的 <c>ProximityVisualizer</c>
        /// （它的标签是间隙读数，语义与"两点距离"不同，不应混用）。
        /// </summary>
        private static MonoBehaviour FindMeasurementVisualizer()
        {
            var system = GameObject.Find(MeasurementSystemName);
            if (system == null) return null;

            var behaviours = system.GetComponentsInChildren<MonoBehaviour>(true);
            MonoBehaviour fallback = null;
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().Name != VisualizerTypeName) continue;

                if (behaviour.gameObject.name == PrimaryVisualizerName) return behaviour;
                if (behaviour.gameObject.name != ProximityVisualizerName && fallback == null)
                    fallback = behaviour;
            }
            return fallback;
        }

        /// <summary>按类型名定位测量 UI 面板的读数 Text（反射读取私有字段，失败即安全返回 null）。</summary>
        private static Text FindReadoutText()
        {
            var systemGo = GameObject.Find(MeasurementSystemName);
            if (systemGo == null) return null;

            var behaviours = systemGo.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) continue;
                if (behaviour.GetType().Name != UiPanelTypeName) continue;

                // 读数 Text 挂在子物体 UIPanel 上，因此需要向下查找
                var readout = FindReadoutField(behaviour);
                if (readout != null) return readout;
            }

            // 兜底：UIPanel 可能不是 MeasurementSystem 的直接组件持有者
            var panels = systemGo.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < panels.Length; i++)
            {
                var behaviour = panels[i];
                if (behaviour == null || behaviour.GetType().Name != UiPanelTypeName) continue;
                var readout = FindReadoutField(behaviour);
                if (readout != null) return readout;
            }
            return null;
        }

        private static Text FindReadoutField(MonoBehaviour uiPanel)
        {
            FieldInfo field = uiPanel.GetType().GetField(ReadoutFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) return null;
            return field.GetValue(uiPanel) as Text;
        }

        /// <summary>从一行读数中剥离数值部分，得到语义标签（"表面间隙：0.72 m" → "表面间隙"）。</summary>
        private static string ExtractLabel(string line, string matchedNumber)
        {
            int cut = line.IndexOf(matchedNumber, System.StringComparison.Ordinal);
            string head = cut > 0 ? line.Substring(0, cut) : line;
            head = head.TrimEnd(' ', '：', ':', '=', '-', '→').Trim();

            // 没有语义前缀（如 3D 标签原文 "0.72 m"）或残留数字时，用兜底标签，
            // 避免出现 "0.72 m：0.72 m" 这种重复显示
            if (string.IsNullOrEmpty(head) || ContainsDigit.IsMatch(head))
                return DefaultDistanceLabel;

            return head;
        }
    }
}
