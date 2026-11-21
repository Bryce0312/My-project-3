using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace LivingRoom30
{
    /// <summary>
    /// The XR Device Simulator strips every other <c>XRHMD</c> from the Input System: once when it
    /// enables, and again whenever another HMD device is added later. With a headset connected that
    /// leaves the head camera reading a static simulated pose, so the view stops following the head.
    ///
    /// Keep the simulator off until XR has had time to start, and never let it remove real HMDs.
    /// Run before XRDeviceSimulator (-29991), including when an older scene has it enabled.
    /// </summary>
    [DefaultExecutionOrder(-31000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRDeviceSimulator))]
    public sealed class XRDeviceSimulatorGate : MonoBehaviour
    {
        private const float XrStartupTimeout = 6f;
        private const float DesktopEyeHeight = 1.6f;

        [SerializeField] private XROrigin xrOrigin;

        private XRDeviceSimulator simulator;

        public XROrigin Origin => xrOrigin;

        public void Configure(XROrigin origin)
        {
            xrOrigin = origin;
        }

        private void Awake()
        {
            simulator = GetComponent<XRDeviceSimulator>();
            // Disabling it alone is insufficient: a late HMD can arrive while desktop simulation
            // is active. Preserve that device so OpenXR can take over without another Play cycle.
            simulator.removeOtherHMDDevices = false;
            simulator.enabled = false;
        }

        private IEnumerator Start()
        {
            float deadline = Time.unscaledTime + XrStartupTimeout;
            // A connected headset can take longer than the old 3 s to start its display
            // subsystem. Treat a present Head device as "headset connected" so a slow
            // OpenXR/Link bring-up never lets the mouse simulator steal the HMD.
            while (!XrIsRunning() && !HeadDevicePresent() && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            if (XrIsRunning() || HeadDevicePresent())
            {
                Debug.Log("A headset is present (" + XRSettings.loadedDeviceName + "), so the XR device simulator stays off and head tracking comes from the headset.");
                yield break;
            }

            Debug.Log("No XR device or headset after " + XrStartupTimeout + "s, so the XR device simulator is enabled for mouse control.");
            simulator.enabled = true;
            RaiseDesktopView();
        }

        private void Update()
        {
            // Run before the simulator's Update so it cannot write another simulated pose once
            // real XR is ready. OnDisable removes only the simulator's own devices.
            if (simulator != null && simulator.enabled && (XrIsRunning() || HeadDevicePresent()))
            {
                simulator.enabled = false;
                Debug.Log("A headset appeared after the simulator started; simulator disabled so head tracking comes from the headset.");
            }
        }

        /// <summary>
        /// True once the XR runtime reports a tracked Head device. A merely present but
        /// untracked device must not keep the desktop simulator off forever.
        /// </summary>
        private static bool HeadDevicePresent()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid &&
                head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        /// <summary>
        /// The simulator resets its HMD to the origin, and with no XR subsystem the XR Origin never
        /// moves its camera offset (its tracking origin mode stays Unknown, so none of the branches
        /// in <c>MoveOffsetHeight</c> run). Without this a desktop session would start at floor
        /// level. The value survives because nothing else writes that transform while XR is absent.
        /// </summary>
        private void RaiseDesktopView()
        {
            if (xrOrigin == null || xrOrigin.CameraFloorOffsetObject == null)
            {
                return;
            }

            Transform offset = xrOrigin.CameraFloorOffsetObject.transform;
            offset.localPosition = new Vector3(offset.localPosition.x, DesktopEyeHeight, offset.localPosition.z);
        }

        private static bool XrIsRunning()
        {
            if (XRSettings.isDeviceActive)
            {
                return true;
            }

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            foreach (XRDisplaySubsystem display in displays)
            {
                if (display.running)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
