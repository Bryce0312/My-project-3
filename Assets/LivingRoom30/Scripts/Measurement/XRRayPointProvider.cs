using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.InputSystem;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// XR 模式拾取：复用 rig 内已有的 XR Ray Interactor 射线，监听控制器“选择(select)”扳机作为拾取确认。
    /// 不依赖家具是否带 XR Interactable——直接对射线命中做 Physics.Raycast，因此能选中任意带碰撞体的物体/表面。
    /// </summary>
    public class XRRayPointProvider : IPointProvider
    {
        public event System.Action<Vector3, GameObject> OnPick;

        private XRRayInteractor interactor;
        private ActionBasedController controller;
        private float maxDistance = 30f;
        private int layerMask = -1; // -1 = 全部层
        private bool subscribed;

        public Transform CameraTransform
            => (interactor != null && interactor.rayOriginTransform != null) ? interactor.rayOriginTransform : null;

        /// <summary>绑定一个 XR Ray Interactor（通常来自 S03_XR_ReviewRig）。</summary>
        public void Bind(XRRayInteractor interactor)
        {
            this.interactor = interactor;

            // 修复 prefab 里可能缺失的 InteractionManager 引用
            if (interactor.interactionManager == null)
            {
                var mgr = Object.FindObjectOfType<XRInteractionManager>();
                if (mgr != null) interactor.interactionManager = mgr;
            }

            controller = interactor.xrController as ActionBasedController;
            Subscribe();
        }

        private void Subscribe()
        {
            if (subscribed || controller == null) return;
            // InputActionProperty 是结构体，不能用 `?.` 或 `!= null` 比较，
            // 直接取 .action（可能为 null）再做判空。
            var action = controller.selectAction.action;
            if (action != null)
            {
                action.performed += OnSelectPerformed;
                subscribed = true;
            }
        }

        private void OnSelectPerformed(InputAction.CallbackContext ctx)
        {
            if (interactor == null) return;
            Transform origin = interactor.rayOriginTransform;
            if (origin == null) return;

            Ray ray = new Ray(origin.position, origin.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, layerMask))
            {
                GameObject root = GeometryUtils.FindFurnitureRoot(hit.transform);
                OnPick?.Invoke(hit.point, root);
            }
        }

        public void Enable() => Subscribe();

        public void Disable()
        {
            if (subscribed && controller != null)
            {
                var action = controller.selectAction.action;
                if (action != null)
                {
                    action.performed -= OnSelectPerformed;
                    subscribed = false;
                }
            }
        }

        // XR 模式由控制器事件驱动，无需每帧轮询
        public void Tick() { }
    }
}
