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
    /// 默认禁用，由 SpatialMeasureManager 在 XR 模式启用、桌面模式禁用。
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class XRHeadPoseFollower : MonoBehaviour
    {
        private Camera cam;
        private TrackedPoseDriver tpd;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null) cam = GetComponentInParent<Camera>();
            // 停掉同相机上失效的 TrackedPoseDriver（其 <XRHMD> 单例路径在 Quest Link 下不更新，
            // 且若每帧写回过期/零姿态会覆盖本组件）。
            tpd = GetComponent<TrackedPoseDriver>();
            if (tpd != null) tpd.enabled = false;
            // 关闭 Unity 原生相机自动追踪：TPD 存在时 Unity 会关，这里显式关闭以独占驱动权。
            if (cam != null) XRDevice.DisableAutoXRCameraTracking(cam, true);
        }

        private void OnEnable() => Application.onBeforeRender += ApplyHeadPose;
        private void OnDisable() => Application.onBeforeRender -= ApplyHeadPose;

        private void Update() => ApplyHeadPose();

        private void ApplyHeadPose()
        {
            if (!enabled || cam == null) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return;
            if (head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && !tracked) return;
            if (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 pos) &&
                head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rot))
            {
                cam.transform.localPosition = pos;
                cam.transform.localRotation = rot;
            }
        }
    }
}
