using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;

namespace LivingRoom30.Review
{
    /// <summary>A small physical remote with hand-only grabbing and a separate tabletop home marker.</summary>
    [DisallowMultipleComponent]
    // Read controller poses/buttons before the interaction manager evaluates either hand's ray.
    [DefaultExecutionOrder(-110)]
    public sealed class ReviewRemoteControl : MonoBehaviour
    {
        private const int RemoteInteractionLayer = 1 << 30;
        private const float HandRadius = 0.10f;
        private static readonly Vector3 RemoteSize = new Vector3(0.18f, 0.025f, 0.045f);

        private sealed class Hand
        {
            public ActionBasedController Controller;
            public GameObject Host;
            public SphereCollider Trigger;
            public ReviewRemoteGrabInteractor Direct;
            public XRRayInteractor[] Rays;
            public XRInteractorLineVisual[] Lines;
            public bool[] RayStates;
            public bool[] LineStates;
            public bool Suppressed;
        }

        private readonly List<Hand> hands = new List<Hand>(2);
        private readonly List<Material> materials = new List<Material>(3);
        private Transform home;
        private GameObject model;
        private GameObject homeFrame;
        private Rigidbody body;
        private BoxCollider remoteCollider;
        private XRGrabInteractable grab;
        private XRInteractionManager interactionManager;
        private XRSelectFilterDelegate selectFilter;
        private XRHoverFilterDelegate hoverFilter;
        private Hand heldHand;
        private bool configured;
        private bool xrAvailable;
        private bool resetting;
        private bool atHome;
        private Canvas grabHint;
        private Text grabHintText;
        private LineRenderer holdRing;
        private ReviewIconGraphic grabHintIcon;
        private readonly Vector3[] ringPoints = new Vector3[41];

        public event Action Grabbed;
        public event Action Returned;
        public bool IsHeld => grab != null && grab.isSelected;
        public bool HasBeenGrabbed { get; private set; }
        public bool IsAtHome => configured && atHome && !IsHeld;
        public Transform Viewer { get; set; }
        public bool ReadyToGrab => hands.Exists(hand => hand.Direct != null && hand.Direct.ReadyToGrab);
        public float HoldProgress
        {
            get
            {
                float progress = 0f;
                foreach (Hand hand in hands) if (hand.Direct != null) progress = Mathf.Max(progress, hand.Direct.HoldProgress);
                return progress;
            }
        }

        /// <param name="tableHome">World pose of the remote's center, at least 0.0125 m above the tabletop.</param>
        public void Configure(Transform tableHome, ActionBasedController left,
            ActionBasedController right, XRInteractionManager manager)
        {
            if (tableHome == null) throw new ArgumentNullException(nameof(tableHome));
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (configured) DisposeOwnedObjects();

            home = tableHome;
            interactionManager = manager;
            HasBeenGrabbed = false;
            xrAvailable = false;
            BuildRemote();
            CreateHand(left, "ReviewRemote_LeftHand");
            if (right != left) CreateHand(right, "ReviewRemote_RightHand");

            // Existing project rays use All interaction layers, so a private bit alone is insufficient.
            selectFilter = new XRSelectFilterDelegate((interactor, interactable) =>
            {
                Hand hand = FindHand(interactor);
                return xrAvailable && hand != null && !hand.Direct.AwaitRelease;
            });
            hoverFilter = new XRHoverFilterDelegate((interactor, interactable) =>
                xrAvailable && FindHand(interactor) != null);
            grab.selectFilters.Add(selectFilter);
            grab.hoverFilters.Add(hoverFilter);
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
            configured = true;
            SnapToHome(false);
        }

        public void SetXrAvailable(bool available)
        {
            if (!configured) return;
            xrAvailable = available && isActiveAndEnabled;
            if (!xrAvailable && (IsHeld || !atHome)) SnapToHome(true);

            grab.enabled = xrAvailable;
            foreach (Hand hand in hands)
            {
                bool enabledForHand = xrAvailable && hand.Controller != null && hand.Controller.isActiveAndEnabled;
                hand.Direct.enabled = enabledForHand;
                hand.Trigger.enabled = enabledForHand && !hand.Direct.usingSphereColliderAccuracyImprovement;
            }
            RefreshRaySuppression();
        }

        /// <summary>Safely releases and returns the remote. Grab history is retained for the current review.</summary>
        public void ResetToHome()
        {
            if (configured) SnapToHome(true);
        }

        private void Update()
        {
            if (!configured) return;
            UpdateHomeFrame();
            RefreshRaySuppression();
        }

        private void LateUpdate()
        {
            if (!configured || grabHint == null) return;
            bool show = xrAvailable && (ReadyToGrab || IsHeld);
            grabHint.gameObject.SetActive(show);
            if (!show) return;
            Vector3 hintPosition = transform.position + Vector3.up * .16f;
            if (Viewer != null)
            {
                // Offset toward and beside the observer so the hand model and existing table
                // dimension labels do not cover the small progress ring.
                hintPosition += (Viewer.position - hintPosition).normalized * .16f - Viewer.right * .18f;
                grabHint.transform.rotation = Quaternion.LookRotation(hintPosition - Viewer.position);
            }
            grabHint.transform.position = hintPosition;
            grabHintText.text = IsHeld ? "Release Trigger to put down" : HoldProgress > 0f ? "Keep holding Trigger..." : "Hold Trigger 0.5s to pick up";
            grabHintIcon.gameObject.SetActive(HoldProgress <= 0f && !IsHeld);
            holdRing.gameObject.SetActive(HoldProgress > 0f && !IsHeld);
            if (holdRing.gameObject.activeSelf)
            {
                for (int i = 0; i < ringPoints.Length; i++)
                {
                    float angle = Mathf.PI * .5f - Mathf.PI * 2f * HoldProgress * i / (ringPoints.Length - 1);
                    ringPoints[i] = new Vector3(Mathf.Cos(angle) * 25f, Mathf.Sin(angle) * 25f + 12f, 0f);
                }
                holdRing.SetPositions(ringPoints);
            }
        }

        private void BuildRemote()
        {
            Material black = MakeMaterial("ReviewRemote_Black", new Color(0.035f, 0.04f, 0.045f));
            Material grey = MakeMaterial("ReviewRemote_Grey", new Color(0.25f, 0.27f, 0.29f));
            Material white = MakeMaterial("ReviewRemote_White", new Color(0.92f, 0.92f, 0.89f));
            model = new GameObject("RemoteModel");
            model.transform.SetParent(transform, false);
            AddVisual(model.transform, "Body", PrimitiveType.Cube, Vector3.zero, RemoteSize, black);
            AddVisual(model.transform, "TopInset", PrimitiveType.Cube, new Vector3(0f, 0.013f, 0f),
                new Vector3(0.164f, 0.002f, 0.034f), grey);
            AddVisual(model.transform, "Power", PrimitiveType.Sphere, new Vector3(-0.067f, 0.016f, 0f),
                new Vector3(0.012f, 0.005f, 0.012f), white);
            AddVisual(model.transform, "NavigationPad", PrimitiveType.Sphere, new Vector3(-0.026f, 0.016f, 0f),
                new Vector3(0.031f, 0.005f, 0.029f), black);
            AddVisual(model.transform, "Confirm", PrimitiveType.Sphere, new Vector3(-0.026f, 0.019f, 0f),
                new Vector3(0.012f, 0.004f, 0.012f), white);
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 2; column++)
                    AddVisual(model.transform, "Button_" + row + "_" + column, PrimitiveType.Sphere,
                        new Vector3(0.016f + row * 0.021f, 0.016f, (column - 0.5f) * 0.018f),
                        new Vector3(0.011f, 0.005f, 0.010f), white);

            remoteCollider = GetComponent<BoxCollider>();
            if (remoteCollider == null) remoteCollider = gameObject.AddComponent<BoxCollider>();
            remoteCollider.size = RemoteSize;
            remoteCollider.center = Vector3.zero;
            remoteCollider.isTrigger = false;
            body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.mass = 0.12f;
            body.drag = 2f;
            body.angularDrag = 4f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            body.useGravity = false;

            grab = GetComponent<XRGrabInteractable>();
            if (grab == null) grab = gameObject.AddComponent<XRGrabInteractable>();
            grab.enabled = false;
            grab.interactionManager = interactionManager;
            grab.interactionLayers = RemoteInteractionLayer;
            grab.colliders.Clear();
            grab.colliders.Add(remoteCollider);
            grab.selectMode = InteractableSelectMode.Single;
            grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            grab.throwOnDetach = false;
            grab.forceGravityOnDetach = true;
            grab.useDynamicAttach = true;
            grab.attachEaseInTime = 0.08f;

            // This root is never parented to the table, so it cannot affect furniture dimensions.
            homeFrame = new GameObject("ReviewRemote_HomeFrame");
            AddVisual(homeFrame.transform, "Border_Front", PrimitiveType.Cube, new Vector3(0f, 0f, -0.034f),
                new Vector3(0.205f, 0.002f, 0.003f), white);
            AddVisual(homeFrame.transform, "Border_Back", PrimitiveType.Cube, new Vector3(0f, 0f, 0.034f),
                new Vector3(0.205f, 0.002f, 0.003f), white);
            AddVisual(homeFrame.transform, "Border_Left", PrimitiveType.Cube, new Vector3(-0.101f, 0f, 0f),
                new Vector3(0.003f, 0.002f, 0.068f), white);
            AddVisual(homeFrame.transform, "Border_Right", PrimitiveType.Cube, new Vector3(0.101f, 0f, 0f),
                new Vector3(0.003f, 0.002f, 0.068f), white);
            UpdateHomeFrame();

            // This non-interactive hint never takes UI focus from the normal controller ray.
            var hintObject = new GameObject("RemoteGrabHint", typeof(RectTransform), typeof(Canvas));
            hintObject.transform.SetParent(transform, false);
            hintObject.transform.localScale = Vector3.one * .0012f;
            grabHint = hintObject.GetComponent<Canvas>();
            grabHint.renderMode = RenderMode.WorldSpace;
            hintObject.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, 100f);
            grabHintText = ReviewVisuals.Text(hintObject.transform, "Instruction", new Vector2(0, -30),
                new Vector2(280, 35), "Hold Trigger 0.5s to pick up", 18, TextAnchor.MiddleCenter);
            grabHintIcon = ReviewVisuals.Icon(hintObject.transform, new Vector2(0, 12), 45f, ReviewIconGraphic.Kind.Hand);
            Material ringMaterial = new Material(Shader.Find("Sprites/Default")) { name = "RemoteGrab_Progress" };
            materials.Add(ringMaterial);
            // LineRenderer widths are world units even under the scaled hint canvas.
            holdRing = ReviewVisuals.Line(hintObject.transform, "HoldProgress", ringMaterial, ringPoints, .003f, ReviewVisuals.Ink);
            hintObject.SetActive(false);
        }

        private void CreateHand(ActionBasedController controller, string objectName)
        {
            if (controller == null) return;
            var host = new GameObject(objectName);
            host.SetActive(false);
            host.transform.SetParent(controller.transform, false);
            var trigger = host.AddComponent<SphereCollider>();
            trigger.radius = HandRadius;
            trigger.isTrigger = true;
            trigger.enabled = false;
            var direct = host.AddComponent<ReviewRemoteGrabInteractor>();
            direct.enabled = false;
            direct.interactionManager = interactionManager;
            direct.xrController = controller;
            direct.interactionLayers = RemoteInteractionLayer;
            direct.improveAccuracyWithSphereCollider = true;
            direct.physicsLayerMask = ~0;
            direct.Target = grab;
            direct.allowActivate = false;
            // Prevent our hand-only interactors from selecting existing furniture with an All mask.
            direct.selectFilters.Add(new XRSelectFilterDelegate((interactor, interactable) =>
                xrAvailable && ReferenceEquals(interactable, grab)));
            direct.hoverFilters.Add(new XRHoverFilterDelegate((interactor, interactable) =>
                xrAvailable && ReferenceEquals(interactable, grab)));
            var hand = new Hand
            {
                Controller = controller,
                Host = host,
                Trigger = trigger,
                Direct = direct,
                Rays = controller.GetComponentsInChildren<XRRayInteractor>(true),
                Lines = controller.GetComponentsInChildren<XRInteractorLineVisual>(true)
            };
            hand.RayStates = new bool[hand.Rays.Length];
            hand.LineStates = new bool[hand.Lines.Length];
            direct.IsUiTargeted = () => IsUiTargeted(hand);
            hands.Add(hand);
            host.SetActive(true);
        }

        private static bool IsUiTargeted(Hand hand)
        {
            foreach (XRRayInteractor ray in hand.Rays)
                if (ray != null && ray.isActiveAndEnabled && ray.TryGetCurrentUIRaycastResult(out var result) && result.isValid)
                    return true;
            return false;
        }

        private Hand FindHand(object interactor)
        {
            foreach (Hand hand in hands)
                if (ReferenceEquals(hand.Direct, interactor)) return hand;
            return null;
        }

        private void OnGrabbed(SelectEnterEventArgs args)
        {
            heldHand = FindHand(args.interactorObject);
            atHome = false;
            HasBeenGrabbed = true;
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.useGravity = false;
            SuppressRay(heldHand);
            Grabbed?.Invoke();
        }

        private void OnReleased(SelectExitEventArgs args)
        {
            if (IsHeld) return;
            heldHand = null;
            if (resetting) return;
            if (!xrAvailable || IsNearHome())
            {
                SnapToHome(true);
                return;
            }

            // XRGrabInteractable restores the pre-grab kinematic state on drop. A released remote
            // must instead become a physical object, including when it was picked up from home.
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.useGravity = true;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            atHome = false;
            RefreshRaySuppression();
        }

        private bool IsNearHome()
        {
            if (home == null) return false;
            Vector3 delta = transform.position - home.position;
            return delta.x * delta.x + delta.z * delta.z < 0.12f * 0.12f &&
                   delta.y >= -0.025f && delta.y <= 0.08f;
        }

        private void SnapToHome(bool notify)
        {
            if (home == null || body == null) return;
            bool changed = !atHome || IsHeld;
            resetting = true;
            CancelGrab();
            heldHand = null;
            body.isKinematic = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = home.position;
            body.rotation = home.rotation;
            transform.SetPositionAndRotation(home.position, home.rotation);
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.isKinematic = true;
            body.useGravity = false;
            body.Sleep();
            atHome = true;
            resetting = false;
            RefreshRaySuppression();
            if (notify && changed) Returned?.Invoke();
        }

        private void CancelGrab()
        {
            if (grab == null) return;
            XRInteractionManager manager = grab.interactionManager != null ? grab.interactionManager : interactionManager;
            while (manager != null && grab.interactorsSelecting.Count > 0)
            {
                IXRSelectInteractor interactor = grab.interactorsSelecting[grab.interactorsSelecting.Count - 1];
                Hand hand = FindHand(interactor);
                if (hand != null) hand.Direct.CancelPress();
                manager.SelectExit(interactor, (IXRSelectInteractable)grab);
            }
            // Also cancel a partially completed long press.
            foreach (Hand hand in hands) if (hand.Direct != null) hand.Direct.CancelPress();
        }

        private void RefreshRaySuppression()
        {
            foreach (Hand hand in hands)
            {
                bool pressed = hand.Controller != null && hand.Controller.activateInteractionState.active;
                // Leave rays available during approach/long press so actual UI hits take priority.
                // Once held (or reset while pressed), do not send that same Trigger press to UI.
                if (xrAvailable && (hand == heldHand ||
                    (hand.Direct.AwaitRelease && hand.Direct.OwnsTriggerPress && pressed)))
                    SuppressRay(hand);
                else
                    RestoreRay(hand);
            }
        }

        private void SuppressRay(Hand hand)
        {
            if (hand == null) return;
            if (!hand.Suppressed)
            {
                for (int i = 0; i < hand.Rays.Length; i++) hand.RayStates[i] = hand.Rays[i] != null && hand.Rays[i].enabled;
                for (int i = 0; i < hand.Lines.Length; i++) hand.LineStates[i] = hand.Lines[i] != null && hand.Lines[i].enabled;
                hand.Suppressed = true;
            }
            foreach (XRRayInteractor ray in hand.Rays) if (ray != null) ray.enabled = false;
            foreach (XRInteractorLineVisual line in hand.Lines) if (line != null) line.enabled = false;
        }

        private void RestoreRay(Hand hand)
        {
            if (!hand.Suppressed) return;
            // An inactive parent already prevents use. Restore each component's saved enabled
            // flag so reactivating that controller can bring its ray back without losing state.
            // Keep pending restoration while desktop mode owns the ray's disabled state.
            if (!xrAvailable) return;
            for (int i = 0; i < hand.Rays.Length; i++)
                if (hand.Rays[i] != null) hand.Rays[i].enabled = hand.RayStates[i];
            for (int i = 0; i < hand.Lines.Length; i++)
                if (hand.Lines[i] != null) hand.Lines[i].enabled = hand.LineStates[i];
            hand.Suppressed = false;
        }

        private void UpdateHomeFrame()
        {
            if (home != null && homeFrame != null)
                homeFrame.transform.SetPositionAndRotation(home.position - home.up * 0.0115f, home.rotation);
        }

        private Material MakeMaterial(string materialName, Color color)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { name = materialName, color = color };
            materials.Add(material);
            return material;
        }

        private static void AddVisual(Transform parent, string objectName, PrimitiveType type,
            Vector3 position, Vector3 scale, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = objectName;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = scale;
            Collider collider = visual.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            visual.GetComponent<Renderer>().sharedMaterial = material;
        }

        private void OnDisable()
        {
            if (!configured) return;
            resetting = true;
            CancelGrab();
            resetting = false;
            // Keep the current XR availability while restoring rays, then make the remote dormant.
            heldHand = null;
            foreach (Hand hand in hands)
            {
                RestoreRay(hand);
                if (hand.Direct != null) hand.Direct.enabled = false;
                if (hand.Trigger != null) hand.Trigger.enabled = false;
            }
            xrAvailable = false;
            SnapToHome(false);
            if (grab != null) grab.enabled = false;
            if (grabHint != null) grabHint.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            DisposeOwnedObjects();
        }

        private void DisposeOwnedObjects()
        {
            resetting = true;
            CancelGrab();
            if (grab != null)
            {
                grab.selectEntered.RemoveListener(OnGrabbed);
                grab.selectExited.RemoveListener(OnReleased);
                if (selectFilter != null) grab.selectFilters.Remove(selectFilter);
                if (hoverFilter != null) grab.hoverFilters.Remove(hoverFilter);
                grab.enabled = false;
            }
            foreach (Hand hand in hands)
            {
                RestoreRay(hand);
                if (hand.Direct != null) hand.Direct.enabled = false;
                if (hand.Host != null) Destroy(hand.Host);
            }
            hands.Clear();
            if (model != null) Destroy(model);
            if (homeFrame != null) Destroy(homeFrame);
            if (grabHint != null) Destroy(grabHint.gameObject);
            foreach (Material material in materials) if (material != null) Destroy(material);
            materials.Clear();
            configured = false;
            resetting = false;
            heldHand = null;
        }
    }
}
