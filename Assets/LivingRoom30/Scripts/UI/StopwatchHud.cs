using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30
{
    [DisallowMultipleComponent]
    public sealed class StopwatchHud : MonoBehaviour
    {
        [SerializeField] private Text label;
        [SerializeField] private Color runningColor = new Color(0.92f, 0.93f, 0.95f);
        [SerializeField] private Color pausedColor = new Color(1f, 0.72f, 0.29f);

        // Accumulated instead of Time.timeSinceLevelLoad so the timer can be paused later.
        private float elapsedSeconds;
        private bool running = true;

        public Text Label => label;
        public bool IsRunning => running;
        public float ElapsedSeconds => elapsedSeconds;

        private void Update()
        {
            if (!running)
            {
                return;
            }

            elapsedSeconds += Time.deltaTime;
            RefreshLabel();
        }

        public void Configure(Text labelComponent)
        {
            label = labelComponent;
            RefreshLabel();
            ApplyColor();
        }

        public void SetRunning(bool value)
        {
            running = value;
            ApplyColor();
        }

        public void ResetTimer()
        {
            elapsedSeconds = 0f;
            RefreshLabel();
        }

        private void ApplyColor()
        {
            if (label == null)
            {
                return;
            }

            label.color = running ? runningColor : pausedColor;
        }

        private void RefreshLabel()
        {
            if (label == null)
            {
                return;
            }

            int totalSeconds = Mathf.FloorToInt(elapsedSeconds);
            int hours = totalSeconds / 3600;
            int minutes = totalSeconds % 3600 / 60;
            int seconds = totalSeconds % 60;

            label.text = hours > 0
                ? $"{hours}:{minutes:00}:{seconds:00}"
                : $"{minutes:00}:{seconds:00}";
        }
    }
}
