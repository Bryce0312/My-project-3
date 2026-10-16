using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 反馈 UI 的纯代码控件工厂与共用样式。
    ///
    /// ===== 为什么全部世界空间（World Space）=====
    /// 本项目场景 <c>S06_XR_UIEventSystem</c> 使用 <c>XRUIInputModule</c>，且刻意
    /// <c>enableMouseInput = false</c>（避免鼠标与 XR Device Simulator 抢输入）。因此：
    /// - ScreenSpace-Overlay / ScreenSpace-Camera 画布在头显与模拟器下**没有任何指针可以点到**
    ///   （XRI 的 <c>TrackedDeviceGraphicRaycaster</c> 只支持 World Space 与 Camera 模式）；
    /// - 只有世界空间画布 + <c>TrackedDeviceGraphicRaycaster</c> 才能被手柄射线点击。
    /// 所以 E 的所有交互界面都构建为世界空间画布：XR 真机与「无头显 + XR Device Simulator」两种
    /// 运行方式下行为完全一致。
    ///
    /// ===== 为什么全部代码构建 =====
    /// 场景 <c>SceneNew.unity</c> 已改为**手工维护**（<c>SceneNewBuilder</c> 不再自动重建，只做缺失告警），
    /// 且其中已包含 D 手工摆放的 23 个标注对象。往场景里写预制体引用会造成合并冲突，
    /// 因此 E 的一切对象都在运行时生成，不在场景中留下任何序列化残留。
    ///
    /// ===== 字体 =====
    /// 使用内置 <c>LegacyRuntime.ttf</c>（与项目既有的 <c>S05_ReviewHud</c>、<c>MeasureUIPanel</c> 一致）。
    /// 刻意不用 TextMeshPro 构建 E 自己的界面：TMP 默认字体资源（LiberationSans SDF）不含中文字形，
    /// 而 E 的列表要显示用户输入的中文描述。仅在**改写 D 的 TMP 标注面板**时才使用 TMP（见 AnnotationBinding）。
    /// </summary>
    public static class FeedbackUi
    {
        /// <summary>UI 层（项目图层表中层 5 为 UI；与 S05_ReviewHud 保持一致）。</summary>
        public const string UiLayerName = "UI";

        public static readonly Color PanelBg = new Color(0.08f, 0.09f, 0.12f, 0.97f);
        public static readonly Color RowBg = new Color(0.15f, 0.16f, 0.20f, 1f);
        public static readonly Color RowBgAlt = new Color(0.12f, 0.13f, 0.17f, 1f);
        public static readonly Color ButtonNormal = new Color(0.24f, 0.25f, 0.31f, 1f);
        public static readonly Color ButtonActive = new Color(0.20f, 0.50f, 0.90f, 1f);
        public static readonly Color PrimaryButton = new Color(0.16f, 0.56f, 0.40f, 1f);
        public static readonly Color DangerButton = new Color(0.56f, 0.25f, 0.25f, 1f);
        public static readonly Color AccentText = new Color(1f, 0.92f, 0.55f);
        public static readonly Color DimText = new Color(0.70f, 0.72f, 0.78f, 1f);
        public static readonly Color TextPrimary = new Color(0.96f, 0.97f, 0.98f, 1f);
        public static readonly Color MeasurementText = new Color(0.55f, 0.85f, 0.95f);

        private static Font cachedFont;
        private static bool fontChecked;

        /// <summary>内置字体（Unity 2022.2+ 已移除 Arial.ttf，改用 LegacyRuntime.ttf）。</summary>
        public static Font DefaultFont
        {
            get
            {
                if (cachedFont == null)
                    cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

                if (!fontChecked)
                {
                    fontChecked = true;
                    if (cachedFont == null)
                        Debug.LogError("[反馈] 内置字体 LegacyRuntime.ttf 不可用，E 自建的界面将无法显示文字。" +
                                       "请检查 Unity 版本（需 2022.2+）与项目资源完整性。");
                }
                return cachedFont;
            }
        }

        // ------------------------------------------------------------------
        // 画布
        // ------------------------------------------------------------------

        /// <summary>
        /// 创建一个世界空间画布。返回其 RectTransform（画布根）。
        /// 挂载 <see cref="TrackedDeviceGraphicRaycaster"/> 是**必需**的：缺了它手柄射线点不到任何控件。
        /// </summary>
        public static RectTransform CreateWorldSpaceCanvas(string name, Transform parent,
            Vector2 sizeDelta, float scale, Camera worldCamera)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = sizeDelta;
            rect.localScale = Vector3.one * scale;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = worldCamera;
            canvas.sortingOrder = 0;

            // 与 S05_ReviewHud 一致：世界空间画布用固定像素密度，避免文字被过度细分。
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 1f;

            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            SetLayerRecursively(go, UiLayerName);
            return rect;
        }

        /// <summary>递归设置层（与 SceneNewBuilder 的做法一致，保证手柄射线与渲染口径统一）。</summary>
        public static void SetLayerRecursively(GameObject root, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0) return;

            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursively(root.transform.GetChild(i).gameObject, layerName);
        }

        // ------------------------------------------------------------------
        // 布局辅助（手工定位，不依赖 LayoutGroup，排版完全确定）
        // ------------------------------------------------------------------

        /// <summary>铺满父级。</summary>
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>创建一个铺满父级的节点。</summary>
        public static RectTransform CreateStretch(string name, Transform parent)
        {
            RectTransform rect = NewRect(name, parent);
            Stretch(rect);
            return rect;
        }

        /// <summary>以父级中心为锚点创建固定尺寸节点。</summary>
        public static RectTransform CreateCentered(string name, Transform parent, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        /// <summary>以父级左上角为原点定位（x 向右、y 向下）。</summary>
        public static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }

        /// <summary>顶部对齐、横向拉伸（宽度 = 父宽 - 左右边距）。</summary>
        public static void PlaceStretchX(RectTransform rect, float top, float height,
            float leftMargin, float rightMargin)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(-(leftMargin + rightMargin), height);
            rect.anchoredPosition = new Vector2((leftMargin - rightMargin) * 0.5f, -top);
        }

        public static RectTransform NewChildRect(string name, Transform parent)
        {
            return NewRect(name, parent);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        // ------------------------------------------------------------------
        // 文本
        // ------------------------------------------------------------------

        public static Text AddText(Transform parent, string content, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text", typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = DefaultFont;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = color;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Text AddPlacedText(Transform parent, string content, int size, TextAnchor anchor,
            Color color, float x, float y, float width, float height)
        {
            Text text = AddText(parent, content, size, anchor, color);
            Place(text.rectTransform, x, y, width, height);
            return text;
        }

        // ------------------------------------------------------------------
        // 按钮
        // ------------------------------------------------------------------

        public static Button AddButton(Transform parent, string label, Color background,
            Action onClick, int fontSize = 15)
        {
            var go = new GameObject("Button", typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = background;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            button.colors = colors;

            Text text = AddText(go.transform, label, fontSize, TextAnchor.MiddleCenter, TextPrimary);
            Stretch(text.rectTransform);

            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            return button;
        }

        public static Button AddPlacedButton(Transform parent, string label, Color background,
            Action onClick, int fontSize, float x, float y, float width, float height)
        {
            Button button = AddButton(parent, label, background, onClick, fontSize);
            Place(button.GetComponent<RectTransform>(), x, y, width, height);
            return button;
        }

        public static void SetButtonColor(Button button, Color color)
        {
            if (button == null) return;
            var image = button.targetGraphic as Image;
            if (image != null) image.color = color;
        }

        public static void SetButtonLabel(Button button, string label)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>();
            if (text != null) text.text = label;
        }

        /// <summary>创建一个纯色面板底板。</summary>
        public static RectTransform CreatePanel(string name, Transform parent, Color background)
        {
            var go = new GameObject(name, typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = background;
            image.raycastTarget = true;
            return rect;
        }

        // ------------------------------------------------------------------
        // rig 解析
        // ------------------------------------------------------------------

        /// <summary>
        /// 按名字前缀查找手柄控制器变换（与 <c>SceneNewBuilder.FindController</c> 同口径）。
        /// rig 预制体的子物体名为 <c>LeftHand Controller</c> / <c>RightHand Controller</c>。
        /// </summary>
        public static Transform FindControllerTransform(string namePrefix)
        {
            var controllers = UnityEngine.Object.FindObjectsOfType<XRBaseController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                var controller = controllers[i];
                if (controller == null) continue;
                if (controller.gameObject.name.StartsWith(namePrefix, StringComparison.Ordinal))
                    return controller.transform;
            }
            return null;
        }

        /// <summary>
        /// 解析头显相机：优先 XROrigin.Camera（与 SceneNewBuilder.ResolveHeadCamera 同口径），
        /// 回退到 <c>Camera.main</c>，再回退到任意启用相机。
        /// </summary>
        public static Camera ResolveHeadCamera()
        {
            var origin = UnityEngine.Object.FindObjectOfType<XROrigin>(true);
            if (origin != null && origin.Camera != null && origin.Camera.isActiveAndEnabled)
                return origin.Camera;

            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) return main;

            var cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
            for (int i = 0; i < cameras.Length; i++)
                if (cameras[i] != null && cameras[i].isActiveAndEnabled) return cameras[i];

            if (origin != null && origin.Camera != null) return origin.Camera;
            return main;
        }

        /// <summary>
        /// 确保存在可用的 EventSystem。本项目的 <c>S06_XR_UIEventSystem</c> 已提供
        /// <c>XRUIInputModule</c>，因此此处通常什么都不做；仅在场景被替换时才补建（幂等）。
        /// </summary>
        public static void EnsureEventSystem()
        {
            var existing = UnityEngine.Object.FindObjectOfType<EventSystem>();
            if (existing == null)
            {
                var go = new GameObject("FeedbackEventSystem");
                go.AddComponent<EventSystem>();
                go.AddComponent<XRUIInputModule>();
                return;
            }

            if (existing.GetComponent<BaseInputModule>() == null)
                existing.gameObject.AddComponent<XRUIInputModule>();
        }
    }
}
