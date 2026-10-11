using UnityEngine;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 轻量轨道相机：用于“无头显桌面模式”下查看场景并瞄准鼠标射线。
    /// 右键拖拽旋转、中键拖拽平移目标、滚轮缩放。仅在桌面模式（SpatialMeasureManager 配置时）启用，
    /// XR 模式下由 TrackedPoseDriver 接管相机，本组件保持禁用。
    /// </summary>
    public class DesktopOrbitCamera : MonoBehaviour
    {
        public float distance = 4.5f;
        public float minDistance = 1f;
        public float maxDistance = 25f;
        public float pitch = 22f;
        public float yaw = 0f;
        public float rotateSpeed = 120f;
        public float panSpeed = 4f;
        public float zoomSpeed = 6f;
        public Vector3 target = new Vector3(0f, 1.1f, 0f); // 初始焦点抬到约人眼/家具面之上，避免起始视角贴地

        [Header("房间约束")]
        public bool clampToRoom = false;   // true 时相机位置始终夹在建筑包围盒内，防止跑出建筑
        public Bounds roomBounds;          // 由 SpatialMeasureManager 在 Awake 注入

        private Camera cam;

        private void Awake() { cam = GetComponent<Camera>(); }

        /// <summary>
        /// 把任意相机位姿反算回轨道参数（target/pitch/yaw/distance）。
        /// 轨道相机 pos = target + Euler(pitch,yaw)*(0,0,dist)，故 pitch = -相机俯仰、yaw = 相机偏航 + 180。
        /// 用于初始化（复刻 rig 起始视角）与「行走→轨道」回切，保证两处公式不漂移。
        /// </summary>
        public void SyncFromTransform(Transform t, float dist)
        {
            distance = Mathf.Clamp(dist, minDistance, maxDistance);
            target = t.position + t.forward * distance;
            float p = t.eulerAngles.x;
            if (p > 180f) p -= 360f;
            pitch = Mathf.Clamp(-p, -85f, 85f);
            yaw = t.eulerAngles.y + 180f;
            ClampTargetToRoom();
        }

        /// <summary>把焦点夹在建筑包围盒内（与相机位置的夹取同源），防止焦点落在墙外。</summary>
        private void ClampTargetToRoom()
        {
            if (!clampToRoom || roomBounds.size == Vector3.zero) return;
            Vector3 tp = target;
            tp.x = Mathf.Clamp(tp.x, roomBounds.min.x + 0.35f, roomBounds.max.x - 0.35f);
            tp.z = Mathf.Clamp(tp.z, roomBounds.min.z + 0.35f, roomBounds.max.z - 0.35f);
            tp.y = Mathf.Clamp(tp.y, roomBounds.min.y + 0.4f, roomBounds.max.y - 0.3f);
            target = tp;
        }

        private void Update()
        {
            if (!enabled || cam == null) return;

            if (Input.GetMouseButton(1)) // 右键旋转
            {
                yaw -= Input.GetAxis("Mouse X") * rotateSpeed * Time.deltaTime;
                pitch -= Input.GetAxis("Mouse Y") * rotateSpeed * Time.deltaTime;
                pitch = Mathf.Clamp(pitch, -85f, 85f);
            }

            if (Input.GetMouseButton(2)) // 中键平移
            {
                Vector3 right = cam.transform.right;
                Vector3 fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                target -= right * Input.GetAxis("Mouse X") * panSpeed * Time.deltaTime;
                target -= fwd * Input.GetAxis("Mouse Y") * panSpeed * Time.deltaTime;
            }

            distance -= Input.GetAxis("Mouse ScrollWheel") * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            // WASD/方向键平移焦点（轨道模式下的地面平移，速度随距离缩放；
            // 行走模式的移动由 DesktopWalkController 接管，两者互斥不会冲突）
            Vector3 wasd = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
            if (wasd.sqrMagnitude > 0.001f)
            {
                if (wasd.sqrMagnitude > 1f) wasd.Normalize();
                Quaternion yawQ = Quaternion.Euler(0f, yaw, 0f);
                target += yawQ * wasd * (panSpeed * Mathf.Max(1f, distance * 0.4f)) * Time.deltaTime;
                ClampTargetToRoom();
            }

            Quaternion q = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 offset = q * new Vector3(0f, 0f, distance);
            cam.transform.position = target + offset;

            // 房间约束：把相机位置夹在建筑包围盒内（留出墙体边距），旋转/平移/缩放都不会穿出建筑
            if (clampToRoom && roomBounds.size != Vector3.zero)
            {
                Vector3 p = cam.transform.position;
                p.x = Mathf.Clamp(p.x, roomBounds.min.x + 0.35f, roomBounds.max.x - 0.35f);
                p.z = Mathf.Clamp(p.z, roomBounds.min.z + 0.35f, roomBounds.max.z - 0.35f);
                p.y = Mathf.Clamp(p.y, roomBounds.min.y + 0.4f, roomBounds.max.y - 0.3f);
                cam.transform.position = p;
            }

            cam.transform.LookAt(target);
        }
    }
}
