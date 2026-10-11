using UnityEngine;
using UnityEngine.EventSystems;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 桌面模式拾取：从活动相机发射 Physics.Raycast，监听鼠标按键作为拾取确认。
    /// 用于无头显的打包运行，或编辑器内未启用 XR Device Simulator 时的快速测量。
    /// 指针悬停在 UGUI 上（如测量面板按钮）时不触发世界拾取，避免点按钮误测。
    /// </summary>
    public class MousePointProvider : IPointProvider
    {
        public event System.Action<Vector3, GameObject> OnPick;

        /// <summary>射线源相机（桌面模式由 DesktopOrbitCamera / DesktopWalkController 驱动）。</summary>
        public Camera activeCamera;

        public int mouseButton = 0;     // 0 = 左键
        public float maxDistance = 100f;
        public int layerMask = -1;      // -1 = 全部层
        public bool useScreenCenter = false; // true = 从屏幕中心发射（指针锁定行走模式的预留）

        private bool enabled;

        public Transform CameraTransform => activeCamera != null ? activeCamera.transform : null;

        public void Enable() => enabled = true;
        public void Disable() => enabled = false;

        public void Tick()
        {
            if (!enabled || activeCamera == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return; // 悬停 UGUI 不拾取
            if (Input.GetMouseButtonDown(mouseButton))
            {
                Vector2 sp = useScreenCenter
                    ? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                    : (Vector2)Input.mousePosition;
                Ray ray = activeCamera.ScreenPointToRay(sp);
                if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, layerMask))
                {
                    GameObject root = GeometryUtils.FindFurnitureRoot(hit.transform);
                    OnPick?.Invoke(hit.point, root);
                }
            }
        }
    }
}
