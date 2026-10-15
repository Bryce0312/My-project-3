using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 侧边 UI 面板（UGUI/Canvas，ScreenSpace-Overlay）：工具模式切换、行走/轨道切换、输入模式切换、清空、实时数值汇总。
    /// 全部以代码方式构建，避免依赖外部预制体；并在场景缺 EventSystem 时按需补建（否则 UGUI 按钮点不动）。
    /// </summary>
    public class MeasureUIPanel : MonoBehaviour
    {
        public event System.Action<MeasureMode> OnModeSelected;
        public event System.Action OnClear;
        public event System.Action OnToggleInputMode;
        public event System.Action OnToggleProximity;
        public event System.Action OnToggleWalkMode;

        private Text readout;
        private Text proxReadout;
        private Button toggleBtn;
        private Button proxBtn;
        private Button walkBtn;
        private Button[] modeButtons;

        private readonly Color normalCol = new Color(0.22f, 0.22f, 0.22f, 0.95f);
        private readonly Color activeCol = new Color(0.20f, 0.50f, 0.90f, 1f);

        // 是否构建侧边测量面板（false 时只保留 Canvas 基础设施）。
        // 由唯一使用方 SpatialMeasureManager 在 Build 前设置。
        public bool showSidePanel = true;

        private void Awake() { } // 不自动构建：调用方需先设置 showSidePanel，再显式调用 Build()

        public void Build()
        {
            EnsureEventSystem();

            var canvasGO = new GameObject("MeasureCanvas");
            canvasGO.transform.SetParent(transform);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            canvasGO.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGO.AddComponent<GraphicRaycaster>();

            if (!showSidePanel) return; // 用户要求：隐藏侧边面板，仅保留顶部提示横幅能力

            var panel = new GameObject("Panel", typeof(Image)).GetComponent<RectTransform>();
            panel.SetParent(canvas.transform);
            // 靠右放置：左侧留给项目原本的 XR Device Simulator 操作面板，避免互相遮挡
            panel.anchorMin = new Vector2(1, 0);
            panel.anchorMax = new Vector2(1, 1);
            panel.offsetMin = new Vector2(-272, 8);
            panel.offsetMax = new Vector2(-8, -8);
            panel.GetComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 0.78f);

            var layout = new GameObject("Layout", typeof(VerticalLayoutGroup)).GetComponent<VerticalLayoutGroup>();
            layout.transform.SetParent(panel);
            var lrt = layout.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddText(layout.transform, "空间测量工具", 18, TextAnchor.MiddleLeft);
            modeButtons = new Button[3];
            modeButtons[0] = AddButton(layout.transform, "① 家具尺寸", () => OnModeSelected?.Invoke(MeasureMode.Dimensions));
            modeButtons[1] = AddButton(layout.transform, "② 两点距离", () => OnModeSelected?.Invoke(MeasureMode.PointToPoint));
            modeButtons[2] = AddButton(layout.transform, "③ 两物体距离", () => OnModeSelected?.Invoke(MeasureMode.ObjectToObject));
            proxBtn = AddButton(layout.transform, "④ 邻近自动测距", () => OnToggleProximity?.Invoke());
            walkBtn = AddButton(layout.transform, "⑤ 桌面行走(F)", () => OnToggleWalkMode?.Invoke());
            var hint = AddText(layout.transform, "提示：按 F 切换「行走/轨道」\n行走：WASD 移动 · 右键拖拽视角 · Shift 加速", 12, TextAnchor.UpperLeft);
            hint.color = new Color(1f, 0.92f, 0.55f);
            hint.GetComponent<RectTransform>().sizeDelta = new Vector2(248, 40);
            toggleBtn = AddButton(layout.transform, "输入: 自动", () => OnToggleInputMode?.Invoke());
            AddButton(layout.transform, "清空", () => OnClear?.Invoke());

            readout = AddText(layout.transform, "尚未测量", 14, TextAnchor.UpperLeft);
            var rrt = readout.GetComponent<RectTransform>();
            rrt.sizeDelta = new Vector2(248, 110);

            proxReadout = AddText(layout.transform, "邻近自动测距：已开启", 13, TextAnchor.UpperLeft);
            var prt = proxReadout.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(248, 110);
        }

        private Text AddText(Transform parent, string txt, int size, TextAnchor align)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent);
            var t = go.GetComponent<Text>();
            t.text = txt;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(248, size + 8);
            return t;
        }

        private Button AddButton(Transform parent, string label, System.Action onClick)
        {
            var go = new GameObject("Btn", typeof(Image), typeof(Button));
            go.transform.SetParent(parent);
            go.GetComponent<Image>().color = normalCol;
            var btn = go.GetComponent<Button>();
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(248, 34);

            var txt = new GameObject("Label", typeof(Text));
            txt.transform.SetParent(go.transform);
            var t = txt.GetComponent<Text>();
            t.text = label;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.fontSize = 14;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var trt = txt.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public void SetReadout(string s) { if (readout) readout.text = s; }
        public void SetProximityReadout(string s) { if (proxReadout) proxReadout.text = s; }
        public void SetInputModeLabel(string s) { if (toggleBtn) toggleBtn.GetComponentInChildren<Text>().text = s; }
        public void SetProximityActive(bool on) { if (proxBtn) proxBtn.GetComponent<Image>().color = on ? activeCol : normalCol; }
        public void SetWalkModeActive(bool on) { if (walkBtn) walkBtn.GetComponent<Image>().color = on ? activeCol : normalCol; }

        /// <summary>场景缺 EventSystem 时按需补建（本项目场景没有任何 EventSystem，缺它 UGUI 按钮收不到点击）。</summary>
        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("MeasureEventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        public void SetActiveMode(MeasureMode m)
        {
            if (modeButtons == null) return;
            for (int i = 0; i < modeButtons.Length; i++)
                modeButtons[i].GetComponent<Image>().color = (i == (int)m) ? activeCol : normalCol;
        }
    }
}
