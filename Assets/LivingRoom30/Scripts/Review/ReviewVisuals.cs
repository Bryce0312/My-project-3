using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace LivingRoom30.Review
{
    internal static class ReviewVisuals
    {
        public static readonly Color Ink = new Color(0.94f, 0.94f, 0.90f, 1f);
        public static readonly Color Back = new Color(0.07f, 0.08f, 0.09f, 0.94f);

        public static Canvas Canvas(Transform parent, string name, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            go.GetComponent<RectTransform>().sizeDelta = size;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            return canvas;
        }

        public static Image Image(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Text(Transform parent, string name, Vector2 position,
            Vector2 size, string value, int fontSize, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = Ink;
            text.alignment = alignment;
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        public static Button Button(Transform parent, string name, Vector2 position,
            Vector2 size, string title, UnityEngine.Events.UnityAction action)
        {
            var image = Image(parent, name, position, size, new Color(0.23f, 0.25f, 0.27f, 1f));
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
            button.colors = colors;
            Text(image.transform, "Label", Vector2.zero, size - new Vector2(8f, 4f), title, 19,
                TextAnchor.MiddleCenter);
            button.onClick.AddListener(action);
            return button;
        }

        public static ReviewIconGraphic Icon(Transform parent, Vector2 position, float size,
            ReviewIconGraphic.Kind kind)
        {
            var go = new GameObject("Icon_" + kind, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = Vector2.one * size;
            var icon = go.AddComponent<ReviewIconGraphic>();
            icon.color = Ink;
            icon.Symbol = kind;
            icon.raycastTarget = false;
            return icon;
        }

        public static LineRenderer Line(Transform parent, string name, Material material,
            Vector3[] points, float width, Color color, bool loop = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.loop = loop;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }
    }

    // Shape carries the meaning; none of these icons changes colour to indicate task state.
    public sealed class ReviewIconGraphic : MaskableGraphic
    {
        public enum Kind { Walk, Eye, Chair, Hand, Return, Check }
        public Kind Symbol;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            switch (Symbol)
            {
                case Kind.Walk:
                    Stroke(vh, -.24f, -.38f, -.24f, .1f);
                    Stroke(vh, .24f, -.05f, .24f, .38f);
                    Stroke(vh, -.36f, .15f, -.12f, .15f);
                    Stroke(vh, .12f, .43f, .36f, .43f);
                    break;
                case Kind.Eye:
                    Stroke(vh, -.47f, 0, -.22f, .24f); Stroke(vh, -.22f, .24f, .22f, .24f);
                    Stroke(vh, .22f, .24f, .47f, 0); Stroke(vh, .47f, 0, .22f, -.24f);
                    Stroke(vh, .22f, -.24f, -.22f, -.24f); Stroke(vh, -.22f, -.24f, -.47f, 0);
                    Stroke(vh, 0, -.11f, 0, .11f);
                    break;
                case Kind.Chair:
                    Stroke(vh, -.30f, .40f, -.30f, -.10f); Stroke(vh, -.30f, -.10f, .32f, -.10f);
                    Stroke(vh, -.22f, -.10f, -.22f, -.42f); Stroke(vh, .26f, -.10f, .26f, -.42f);
                    Stroke(vh, -.30f, .40f, .18f, .40f); Stroke(vh, .18f, .40f, .18f, -.10f);
                    break;
                case Kind.Hand:
                    Stroke(vh, -.28f, -.35f, -.4f, -.03f); Stroke(vh, -.4f, -.03f, -.25f, .1f);
                    Stroke(vh, -.25f, .1f, -.12f, -.02f); Stroke(vh, -.12f, -.02f, -.12f, .40f);
                    Stroke(vh, -.12f, .40f, .01f, .40f); Stroke(vh, .01f, .40f, .01f, .05f);
                    Stroke(vh, .01f, .05f, .28f, .05f); Stroke(vh, .28f, .05f, .28f, -.35f);
                    Stroke(vh, .28f, -.35f, -.28f, -.35f);
                    break;
                case Kind.Return:
                    Stroke(vh, -.4f, .14f, .28f, .14f); Stroke(vh, .28f, .14f, .28f, -.30f);
                    Stroke(vh, .28f, -.30f, -.14f, -.30f);
                    Stroke(vh, -.4f, .14f, -.15f, .38f); Stroke(vh, -.4f, .14f, -.15f, -.1f);
                    break;
                default:
                    Stroke(vh, -.4f, .02f, -.1f, -.28f); Stroke(vh, -.1f, -.28f, .4f, .35f);
                    break;
            }
        }

        private void Stroke(VertexHelper vh, float ax, float ay, float bx, float by)
        {
            var rect = rectTransform.rect;
            Vector2 a = rect.center + new Vector2(ax * rect.width, ay * rect.height);
            Vector2 b = rect.center + new Vector2(bx * rect.width, by * rect.height);
            Vector2 normal = new Vector2(-(b-a).y, (b-a).x).normalized * rect.width * .032f;
            int start = vh.currentVertCount;
            vh.AddVert(a-normal, color, Vector2.zero); vh.AddVert(a+normal, color, Vector2.zero);
            vh.AddVert(b+normal, color, Vector2.zero); vh.AddVert(b-normal, color, Vector2.zero);
            vh.AddTriangle(start, start+1, start+2); vh.AddTriangle(start, start+2, start+3);
        }
    }
}
