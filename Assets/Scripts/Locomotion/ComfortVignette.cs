using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30.Locomotion
{
    /// <summary>
    /// Full-screen radial "comfort vignette" that softens the peripheral field of view during
    /// smooth locomotion. It is a plain 2D overlay (Screen Space Overlay) with a procedurally
    /// generated radial-gradient sprite, so it needs no extra shader, material or package.
    ///
    /// The vignette is intentionally headless: the <see cref="LocomotionComfortManager"/> owns the
    /// intensity and drives <see cref="SetIntensity"/> each frame. Setting intensity to 0 removes
    /// the effect entirely.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComfortVignette : MonoBehaviour
    {
        private const int TextureSize = 256;

        private Image image;
        private float intensity;

        public float Intensity => intensity;

        private void Awake()
        {
            BuildOverlay();
            SetIntensity(0f);
        }

        /// <summary>Sets the vignette opacity in the range [0, 1].</summary>
        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
            if (image != null)
                image.color = new Color(0f, 0f, 0f, intensity);
        }

        private void BuildOverlay()
        {
            var canvasObject = new GameObject("ComfortVignette_Canvas", typeof(Canvas));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;

            var imageObject = new GameObject("Vignette_Image", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            image = imageObject.AddComponent<Image>();
            image.sprite = BuildRadialSprite();
            image.type = Image.Type.Simple;
            // The vignette must never intercept controller raycasts.
            image.raycastTarget = false;
            image.color = Color.clear;
        }

        private static Sprite BuildRadialSprite()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "ComfortVignette_Radial",
                wrapMode = TextureWrapMode.Clamp,
            };

            float half = TextureSize * 0.5f;
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x + 0.5f) / half - 1f;
                    float dy = (y + 0.5f) / half - 1f;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    // Transparent in the centre, opaque toward the corners, with a soft ramp.
                    float alpha = Mathf.InverseLerp(0.55f, 1f, distance);
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
                }
            }

            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0, 0, TextureSize, TextureSize), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
