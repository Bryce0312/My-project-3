using LivingRoom30.Measurement;
using UnityEngine;
using UnityEngine.UI;

namespace LivingRoom30
{
    /// <summary>
    /// Toggles the clearance readout from the wrist panel.
    ///
    /// This is currently the only entry point for that feature: the measurement panel sets
    /// <c>showSidePanel = false</c> and returns from its build before creating any controls, so the
    /// buttons that were meant to own this switch never exist.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class HudGapsButton : MonoBehaviour
    {
        [SerializeField] private Text label;
        [SerializeField] private string onText = "GAPS ON";
        [SerializeField] private string offText = "GAPS OFF";

        private SpatialMeasureManager measureManager;

        public Text Label => label;

        private void Awake()
        {
            // Runtime listener, matching HudPauseButton: the scene is maintained by hand now, and
            // runtime listeners are the ones that survive that.
            GetComponent<Button>().onClick.AddListener(Toggle);
        }

        private void Update()
        {
            // The measurement system bootstraps itself with RuntimeInitializeOnLoadMethod
            // (AfterSceneLoad), so it does not exist yet when this Awake runs. Keep retrying until
            // it appears; once resolved the label is kept in sync by Toggle.
            if (measureManager == null)
            {
                RefreshLabel();
            }
        }

        public void Configure(Text buttonLabel)
        {
            label = buttonLabel;
            RefreshLabel();
        }

        public void Toggle()
        {
            SpatialMeasureManager manager = ResolveManager();
            if (manager == null)
            {
                Debug.LogWarning("HudGapsButton could not find a SpatialMeasureManager to toggle.", this);
                return;
            }

            manager.SetProximityEnabled(!manager.ProximityEnabled);
            RefreshLabel();
        }

        private SpatialMeasureManager ResolveManager()
        {
            if (measureManager == null)
            {
                measureManager = FindObjectOfType<SpatialMeasureManager>();
            }

            return measureManager;
        }

        private void RefreshLabel()
        {
            if (label == null)
            {
                return;
            }

            SpatialMeasureManager manager = ResolveManager();
            bool showing = manager == null || manager.ProximityEnabled;
            label.text = showing ? onText : offText;
        }
    }
}
