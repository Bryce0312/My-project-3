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
    /// XR initializes asynchronously, well after <c>Awake</c>, so the decision cannot be made there.
    /// This keeps the simulator off until XR has had time to come up, then enables it only if no XR
    /// device is running.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(XRDeviceSimulator))]
    public sealed class XRDeviceSimulatorGate : MonoBehaviour
    {
        private const float XrStartupTimeout = 3f;
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
            // Runs before this object's OnEnable phase, so the simulator never subscribes to device
            // changes and therefore never removes a headset that appears later.
            simulator.enabled = false;
        }

        private IEnumerator Start()
        {
            float deadline = Time.unscaledTime + XrStartupTimeout;
            while (!XrIsRunning() && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            if (XrIsRunning())
            {
                Debug.Log("XR device is running (" + XRSettings.loadedDeviceName + "), so the XR device simulator stays off and head tracking comes from the headset.");
                yield break;
            }

            Debug.Log("No XR device after " + XrStartupTimeout + "s, so the XR device simulator is enabled for mouse control.");
            simulator.enabled = true;
            RaiseDesktopView();
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
