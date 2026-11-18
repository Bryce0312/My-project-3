using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.InputSystem.XR;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 头部姿态跟随器（Quest/OpenXR 修复）：用旧版 UnityEngine.XR.InputDevices API 读取
    /// XRNode.Head 的 centerEyePosition/Rotation（已在诊断中验证 Quest Link 下可用），
    /// 1:1 写入 rig 主相机的 localPosition/localRotation，使物理转头实时驱动相机视角。
    /// 取代 TrackedPoseDriver 在 Quest Link 下失效的 &lt;XRHMD&gt; 单例动作路径；
    /// SnapTurn（手柄转视野）通过旋转 rig 根节点独立工作，二者叠加即为沉浸视角。
    ///
    /// 由 SpatialMeasureManager 在真实 XR 模式下运行时挂载/启用、桌面模式禁用。
    /// 禁用时会把 TrackedPoseDriver 恢复到进入前的状态，避免桌面模拟器失去相机驱动。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class XRHeadPoseFollower : MonoBehaviour
    {
        private Camera cam;
        private TrackedPoseDriver tpd;
        private XROrigin origin;
        private bool tpdWasEnabled;
        private bool originModeChecked, floorOrigin;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = GetComponentInParent<Camera>();
            origin = GetComponentInParent<XROrigin>();

            // 停掉同相机上失效的 TrackedPoseDriver（其 <XRHMD> 单例路径在 Quest Link 下不更新，
            // 且若每帧写回过期/零姿态会覆盖本组件）。记住原状态，禁用本组件时恢复。
            tpd = GetComponent<TrackedPoseDriver>();
            if (tpd != null)
            {
                tpdWasEnabled = tpd.enabled;
                tpd.enabled = false;
            }

            // 关闭 Unity 原生相机自动追踪：TPD 存在时 Unity 会关，这里显式关闭以独占驱动权。
            if (cam != null)
            {
                try { XRDevice.DisableAutoXRCameraTracking(cam, true); }
                catch { /* older/newer XR runtimes may not expose this; the TPD is already off. */ }
            }
        }

        private void OnEnable()
        {
            // A re-enable after a desktop spell must take the camera back from the TPD again.
            if (tpd != null) tpd.enabled = false;
            Application.onBeforeRender += ApplyHeadPose;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyHeadPose;
            // Desktop fallback (XR device simulator) needs the original TPD to drive the camera.
            if (tpd != null) tpd.enabled = tpdWasEnabled;
        }

        private void Update() => ApplyHeadPose();

        /// <summary>
        /// Floor tracking already reports eye height in centerEyePosition. If the XROrigin
        /// still carries its 1.6 m Device-mode camera offset, writing the pose would double
        /// the height. Keep that offset at zero in Floor mode, but leave Device mode untouched.
        /// </summary>
        private void EnsureFloorOffsetCleared()
        {
            if (origin == null) return;
            if (!originModeChecked)
            {
                // XROrigin mirrors the subsystem's mode (and updates it on tracking-origin
                // changed events). Unknown means the subsystem isn't ready yet: retry next frame.
                TrackingOriginModeFlags mode = origin.CurrentTrackingOriginMode;
                if (mode == TrackingOriginModeFlags.Unknown) return;
                floorOrigin = mode == TrackingOriginModeFlags.Floor;
                originModeChecked = true;
            }
            if (!originModeChecked || !floorOrigin) return;
            if (origin.CameraFloorOffsetObject == null) return;
            Transform offset = origin.CameraFloorOffsetObject.transform;
            Vector3 local = offset.localPosition;
            if (Mathf.Abs(local.y) > .01f)
                offset.localPosition = new Vector3(local.x, 0f, local.z);
        }

        private void ApplyHeadPose()
        {
            if (!enabled || cam == null) return;
            EnsureFloorOffsetCleared();
            if (!TryReadHeadPose(out Vector3 pos, out Quaternion rot)) return;
            cam.transform.localPosition = pos;
            cam.transform.localRotation = rot;
        }

        /// <summary>
        /// Prefer the legacy InputDevices head pose (it survives the simulator stripping the
        /// Input System's XRHMD). Fall back to the Input System device for runtimes where the
        /// legacy API is unavailable.
        /// </summary>
        private static bool TryReadHeadPose(out Vector3 pos, out Quaternion rot)
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (head.isValid)
            {
                if (head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked)
                {
                    pos = Vector3.zero;
                    rot = Quaternion.identity;
                    return false;
                }
                if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out pos) &&
                    head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rot))
                    return true;
            }

            var hmd = UnityEngine.InputSystem.InputSystem.GetDevice<XRHMD>();
            if (hmd != null)
            {
                pos = hmd.centerEyePosition.ReadValue();
                rot = hmd.centerEyeRotation.ReadValue();
                return true;
            }

            pos = Vector3.zero;
            rot = Quaternion.identity;
            return false;
        }
    }
}
