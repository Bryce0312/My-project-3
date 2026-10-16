using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using LivingRoom30;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// D（选择与标注）与 E（反馈与问题管理）之间的运行期桥接。
    ///
    /// 职责：
    /// 1. <b>捕获目标与锚点</b>——订阅场景中所有 <see cref="XRSimpleInteractable"/> 的
    ///    <c>selectEntered</c>（D 的 23 个标注对象在场景里就是靠这个事件触发
    ///    <c>AnnotationSpawner.OnSelectEntered</c> 弹出标注面板的），从中记录被选中的对象，
    ///    并按相机视线在该对象表面求出空间锚点。
    /// 2. <b>管理绑定的生命周期</b>——轮询 D 生成的 <see cref="AnnotationUI"/> 实例，
    ///    出现时建立 <see cref="AnnotationBinding"/>，销毁时回收。
    ///
    /// 为什么用事件而不是射线：E 若自己再发一条射线去猜"用户刚才点了什么"，在遮挡、
    /// 多物体重叠、以及 D 的 dwell 高亮语义下都会猜错。直接复用 D 正在使用的那条事件，
    /// 拿到的是**同一个**目标对象，语义 100% 一致，且不产生任何射线开销。
    ///
    /// 为什么按帧轮询而不是监听 UnityEvent：<c>AnnotationSpawner</c> 用
    /// <c>static currentActiveUI</c> 记录当前面板，没有对外事件；<c>FindObjectOfType</c>
    /// 每 0.25 秒一次的开销远低于一帧渲染，且实现最简单、最不容易出错。
    /// </summary>
    public sealed class AnnotationBridge
    {
        /// <summary>轮询标注面板的间隔（秒）。</summary>
        private const float PanelPollInterval = 0.25f;

        /// <summary>重新扫描 XRSimpleInteractable 的间隔（秒）。
        /// <c>HoverDwellHighlighter.Start</c> 会给缺交互器的对象补挂一个，因此需要周期性复查。</summary>
        private const float InteractableRescanInterval = 3f;

        private readonly FeedbackService service;
        private readonly Dictionary<int, InteractableSubscription> subscriptions =
            new Dictionary<int, InteractableSubscription>();
        private readonly List<int> staleIds = new List<int>();

        private AnnotationUI boundUi;
        private AnnotationBinding binding;

        private float panelPollTimer;
        private float rescanTimer;

        // 最近一次被 D 选中的对象及其锚点（下一次面板出现时归属给该面板）
        private GameObject pendingTarget;
        private Vector3 pendingAnchor;
        private string pendingTargetFurnitureId = "";
        private string pendingTargetName = "";
        private string pendingTargetObjectName = "";

        public AnnotationBridge(FeedbackService feedbackService)
        {
            service = feedbackService;
        }

        /// <summary>已订阅的交互对象数量（自检与日志用）。</summary>
        public int SubscribedInteractableCount { get { return subscriptions.Count; } }

        /// <summary>当前是否已绑定到 D 的标注面板。</summary>
        public bool HasActivePanel { get { return binding != null && binding.IsAlive; } }

        public void Initialize()
        {
            RescanInteractables();
            panelPollTimer = PanelPollInterval;
        }

        public void Tick()
        {
            rescanTimer -= Time.deltaTime;
            if (rescanTimer <= 0f)
            {
                rescanTimer = InteractableRescanInterval;
                RescanInteractables();
            }

            panelPollTimer -= Time.deltaTime;
            if (panelPollTimer <= 0f)
            {
                panelPollTimer = PanelPollInterval;
                RefreshPanelBinding();
            }

            if (binding != null)
            {
                if (!binding.IsAlive)
                {
                    binding = null;
                    boundUi = null;
                }
                else
                {
                    binding.TryBuild();
                }
            }
        }

        public void Shutdown()
        {
            if (binding != null) binding.Dispose();
            binding = null;
            boundUi = null;

            foreach (var pair in subscriptions) pair.Value.Dispose();
            subscriptions.Clear();
        }

        /// <summary>供日志/自检使用：最近一次捕获的目标描述。</summary>
        public string PendingTargetDescription
        {
            get
            {
                if (pendingTarget == null) return "（尚未选择任何对象）";
                return pendingTargetName + (string.IsNullOrEmpty(pendingTargetFurnitureId)
                    ? "" : "（" + pendingTargetFurnitureId + "）");
            }
        }

        // ------------------------------------------------------------------
        // 交互对象订阅
        // ------------------------------------------------------------------

        private void RescanInteractables()
        {
            // 回收已销毁对象留下的订阅
            staleIds.Clear();
            foreach (var pair in subscriptions)
                if (pair.Value.Interactable == null) staleIds.Add(pair.Key);

            for (int i = 0; i < staleIds.Count; i++)
            {
                InteractableSubscription stale;
                if (subscriptions.TryGetValue(staleIds[i], out stale)) stale.Dispose();
                subscriptions.Remove(staleIds[i]);
            }

            var interactables = UnityEngine.Object.FindObjectsOfType<XRSimpleInteractable>(true);
            for (int i = 0; i < interactables.Length; i++)
            {
                XRSimpleInteractable interactable = interactables[i];
                if (interactable == null) continue;

                int id = interactable.GetInstanceID();
                if (subscriptions.ContainsKey(id)) continue;

                subscriptions[id] = InteractableSubscription.Create(interactable, OnInteractableSelected);
            }
        }

        /// <summary>D 的 <c>AnnotationSpawner.OnSelectEntered</c> 会在同一帧弹出面板；E 先记录目标。</summary>
        private void OnInteractableSelected(GameObject target)
        {
            pendingTarget = target;

            string furnitureId, displayName, objectName;
            IssueTargetResolver.Describe(target, out furnitureId, out displayName, out objectName);
            pendingTargetFurnitureId = furnitureId;
            pendingTargetName = displayName;
            pendingTargetObjectName = objectName;

            pendingAnchor = ComputeAnchorPoint(target);

            if (service != null && service.LogBridgeActivity)
            {
                Debug.Log("[反馈] 已捕获标注目标：" + pendingTargetName
                    + "，锚点 " + pendingAnchor.ToString("F2"));
            }
        }

        /// <summary>
        /// 在目标表面求空间锚点：优先取「相机 → 目标包围盒中心」射线命中目标自身的点，
        /// 否则退化为包围盒面向相机的最近点。这样标注会落在物体表面而不是物体内部或空中。
        /// </summary>
        private static Vector3 ComputeAnchorPoint(GameObject target)
        {
            Camera camera = FeedbackUi.ResolveHeadCamera();
            Vector3 origin = camera != null ? camera.transform.position : Vector3.zero;
            Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;

            if (target == null) return origin + forward * 1.5f;

            Bounds bounds = AggregateBounds(target);
            if (bounds.size.sqrMagnitude <= 0f) return origin + forward * 1.5f;

            Vector3 direction = bounds.center - origin;
            float distance = direction.magnitude;
            if (distance > 0.001f)
            {
                Ray ray = new Ray(origin, direction / distance);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit, distance + 1f, ~0, QueryTriggerInteraction.Ignore)
                    && hit.transform != null && hit.transform.IsChildOf(target.transform))
                {
                    return hit.point;
                }
            }

            return bounds.ClosestPoint(origin);
        }

        private static Bounds AggregateBounds(GameObject target)
        {
            var renderers = target.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                return bounds;
            }

            var colliders = target.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                Bounds bounds = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++) bounds.Encapsulate(colliders[i].bounds);
                return bounds;
            }

            return new Bounds(target.transform.position, Vector3.zero);
        }

        // ------------------------------------------------------------------
        // 面板绑定
        // ------------------------------------------------------------------

        private void RefreshPanelBinding()
        {
            AnnotationUI current = UnityEngine.Object.FindObjectOfType<AnnotationUI>(true);

            if (current == boundUi) return;

            if (binding != null)
            {
                binding.Dispose();
                binding = null;
            }

            boundUi = current;
            if (current == null) return;

            binding = new AnnotationBinding(current, service, pendingTarget, pendingAnchor);
            if (service != null && service.LogBridgeActivity)
            {
                Debug.Log("[反馈] 已接管 D 的标注面板，目标：" + PendingTargetDescription
                    + "；锚点 " + pendingAnchor.ToString("F2"));
            }
        }

        // ------------------------------------------------------------------
        // 订阅句柄
        // ------------------------------------------------------------------

        private sealed class InteractableSubscription
        {
            private XRSimpleInteractable interactable;
            private UnityAction<SelectEnterEventArgs> handler;

            public XRSimpleInteractable Interactable { get { return interactable; } }

            /// <summary>
            /// 用闭包捕获具体交互对象，而不是从 <c>SelectEnterEventArgs</c> 里取成员——
            /// XRI 各版本对 args 成员命名有过调整，闭包写法不依赖任何 args 成员，跨版本稳定。
            /// </summary>
            public static InteractableSubscription Create(XRSimpleInteractable target, Action<GameObject> onSelected)
            {
                var subscription = new InteractableSubscription();
                subscription.interactable = target;

                GameObject selectedObject = target.gameObject;
                subscription.handler = _ => onSelected(selectedObject);

                target.selectEntered.AddListener(subscription.handler);
                return subscription;
            }

            public void Dispose()
            {
                if (interactable != null && handler != null)
                    interactable.selectEntered.RemoveListener(handler);
                handler = null;
                interactable = null;
            }
        }
    }
}
