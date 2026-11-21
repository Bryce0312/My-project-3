using System;
using System.Collections.Generic;
using LivingRoom30.Measurement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30.Review
{
    /// <summary>
    /// Adds a hollow shell and two hinged doors to the authored F09 display cabinet.
    /// Existing door meshes, colliders and cabinet annotation events remain in place.
    /// </summary>
    [DefaultExecutionOrder(-105)]
    [DisallowMultipleComponent]
    public sealed class ReviewCabinetDoors : MonoBehaviour
    {
        private const float SweepStepDegrees = 1f;
        private const float ContactMargin = .004f;
        private const float AngleTolerance = .01f;
        private const float HandleReach = .16f;
        private const float ClosedTolerance = 2f;
        private const float OpenTolerance = 8f;
        private const string BlockedMessage = "Door movement is blocked. Step clear and try again.";

        [SerializeField, Range(80f, 110f)] private float openingAngle = 90f;
        [SerializeField, Range(20f, 160f)] private float degreesPerSecond = 75f;

        private sealed class Leaf
        {
            public Transform Door, OriginalParent, Hinge;
            public int OriginalSibling;
            public float Sign;
            public Transform Handle, PullHand;
            public float Angle, TargetAngle, GrabStartAngle;
            public Vector3 GrabStartDirection;
            public bool MotorMoving;
            public readonly List<CollisionShape> Shapes = new List<CollisionShape>();
        }

        private sealed class Hand
        {
            public ActionBasedController Controller;
            public XRRayInteractor[] Rays;
            public XRInteractorLineVisual[] Lines;
            public bool[] RayStates, LineStates;
            public bool Suppressed, WasPressed, OwnsTriggerPress, AwaitRelease = true;
            public int LeafIndex = -1;
        }

        private sealed class CollisionShape
        {
            public BoxCollider Collider;
            public Vector3 CenterOffset, HalfExtents;
            public Quaternion LocalRotation;
            public float Radius;
        }

        private Transform cabinet;
        private MeshRenderer originalBodyRenderer;
        private Collider originalBodyCollider;
        private bool originalRendererEnabled, originalColliderEnabled;
        private Transform cabinetBooks;
        private BoxCollider cabinetBooksCollider;
        private Vector3 originalBooksPosition, originalBooksColliderCenter, originalBooksColliderSize;
        private readonly List<Transform> repositionedBooks = new List<Transform>();
        private readonly List<Vector3> originalBookPositions = new List<Vector3>();
        private XRBaseInteractable interactable;
        private readonly List<Collider> originalInteractionColliders = new List<Collider>();
        private readonly List<Collider> generatedColliders = new List<Collider>();
        private readonly List<GameObject> ownedObjects = new List<GameObject>();
        private readonly Collider[] overlaps = new Collider[128];
        private readonly List<Leaf> leaves = new List<Leaf>();
        private Material handleMaterial;
        private bool interactionMapChanged, interactionAllowed = true, xrAvailable;
        private readonly List<Hand> hands = new List<Hand>(2);
        private int settledState = 1;
        private Vector3 configuredScale;
        private bool cleaning;

        public bool IsReady { get; private set; }
        public bool IsOpen => IsReady && !IsMoving && leaves.TrueForAll(leaf => leaf.Angle >= openingAngle - OpenTolerance);
        public bool IsClosed => IsReady && !IsMoving && leaves.TrueForAll(leaf => leaf.Angle <= ClosedTolerance);
        public bool IsMoving => leaves.Exists(leaf => leaf.MotorMoving || leaf.PullHand != null);
        public bool IsHandPulling => leaves.Exists(leaf => leaf.PullHand != null);
        public float OpenFraction
        {
            get
            {
                if (!IsReady || leaves.Count == 0) return 0f;
                float leastOpen = openingAngle;
                foreach (Leaf leaf in leaves) leastOpen = Mathf.Min(leastOpen, leaf.Angle);
                return Mathf.Clamp01(leastOpen / openingAngle);
            }
        }
        public string LastMessage { get; private set; } = string.Empty;
        public Collider LastBlockingCollider { get; private set; }
        public Bounds ClosedBounds { get; private set; }
        public Func<bool> CanInteract { get; set; }
        public event Action<bool> StateChanged;
        public event Action<string> ActionBlocked;
        public event Action HandPullStarted;
        public Transform CabinetRoot => cabinet;
        public Transform LeftHinge => leaves.Count > 0 ? leaves[0].Hinge : null;
        public Transform RightHinge => leaves.Count > 1 ? leaves[1].Hinge : null;
        public Transform LeftHandle => leaves.Count > 0 ? leaves[0].Handle : null;
        public Transform RightHandle => leaves.Count > 1 ? leaves[1].Handle : null;

        public void ConfigureHands(ActionBasedController left, ActionBasedController right)
        {
            ReleaseHands(true);
            hands.Clear();
            AddHand(left);
            if (right != left) AddHand(right);
        }

        private void AddHand(ActionBasedController controller)
        {
            if (controller == null) return;
            var hand = new Hand
            {
                Controller = controller,
                Rays = controller.GetComponentsInChildren<XRRayInteractor>(true),
                Lines = controller.GetComponentsInChildren<XRInteractorLineVisual>(true)
            };
            hand.RayStates = new bool[hand.Rays.Length];
            hand.LineStates = new bool[hand.Lines.Length];
            hands.Add(hand);
        }

        public void Configure(Transform cabinetRoot)
        {
            if (IsReady)
            {
                if (cabinet != cabinetRoot) LastMessage = "This door control is already assigned to a cabinet.";
                return;
            }
            if (cabinetRoot == null)
            {
                LastMessage = "The display cabinet is unavailable.";
                return;
            }
            cabinet = cabinetRoot;
            Transform body = cabinet.Find("CabinetBody");
            Transform left = cabinet.Find("GlassDoor_Left");
            Transform right = cabinet.Find("GlassDoor_Right");
            if (body == null || left == null || right == null ||
                body.GetComponent<MeshRenderer>() == null || body.GetComponent<Collider>() == null ||
                left.GetComponent<BoxCollider>() == null || right.GetComponent<BoxCollider>() == null)
            {
                LastMessage = "The display cabinet door configuration is incomplete.";
                cabinet = null;
                return;
            }
            configuredScale = cabinet.lossyScale;
            if (configuredScale.x <= 0f || configuredScale.y <= 0f || configuredScale.z <= 0f ||
                Mathf.Abs(configuredScale.x - configuredScale.z) > .001f)
            {
                LastMessage = "The cabinet needs matching positive horizontal scales before its doors can move.";
                cabinet = null;
                return;
            }

            try
            {
                // Cache the original measurement envelope before adding the door hardware.
                ClosedBounds = GeometryUtils.GetWorldAABB(cabinet.gameObject);
                originalBodyRenderer = body.GetComponent<MeshRenderer>();
                originalBodyCollider = body.GetComponent<Collider>();
                originalRendererEnabled = originalBodyRenderer.enabled;
                originalColliderEnabled = originalBodyCollider.enabled;
                Material wood = originalBodyRenderer.sharedMaterial;

                // The original body is a solid box. A shell exposes the authored interior shelves.
                originalBodyRenderer.enabled = false;
                originalBodyCollider.enabled = false;
                Transform shell = OwnedGroup("ReviewCabinetShell", cabinet);
                AddBox(shell, "Side_Left", new Vector3(-.515f, .8f, 0f), new Vector3(.07f, 1.6f, .38f), wood);
                AddBox(shell, "Side_Right", new Vector3(.515f, .8f, 0f), new Vector3(.07f, 1.6f, .38f), wood);
                AddBox(shell, "Top", new Vector3(0f, 1.565f, 0f), new Vector3(.96f, .07f, .38f), wood);
                AddBox(shell, "Bottom", new Vector3(0f, .115f, 0f), new Vector3(.96f, .23f, .38f), wood);
                AddBox(shell, "Back", new Vector3(0f, .88f, .165f), new Vector3(.96f, 1.3f, .05f), wood);
                AdaptCabinetBooks();

                Shader shader = Shader.Find("Standard");
                if (shader != null)
                {
                    handleMaterial = new Material(shader) { name = "ReviewCabinet_Handle", color = new Color(.12f, .12f, .12f) };
                }
                CreateLeaf(left, "ReviewCabinetHinge_Left", -.535f, 1f, handleMaterial != null ? handleMaterial : wood);
                CreateLeaf(right, "ReviewCabinetHinge_Right", .535f, -1f, handleMaterial != null ? handleMaterial : wood);
                UpdateInteractionColliders();
                settledState = 1;
                IsReady = true;
                LastMessage = string.Empty;
                Physics.SyncTransforms();
            }
            catch (Exception exception)
            {
                Cleanup();
                LastMessage = "The cabinet could not be prepared. Its original geometry has been restored.";
                Debug.LogWarning("Could not prepare cabinet doors: " + exception.Message, this);
            }
        }

        private Transform OwnedGroup(string objectName, Transform parent)
        {
            var group = new GameObject(objectName);
            group.layer = cabinet.gameObject.layer;
            group.transform.SetParent(parent, false);
            ownedObjects.Add(group);
            return group.transform;
        }

        private BoxCollider AddBox(Transform parent, string objectName, Vector3 position, Vector3 size, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = objectName;
            part.layer = cabinet.gameObject.layer;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            BoxCollider box = part.GetComponent<BoxCollider>();
            generatedColliders.Add(box);
            return box;
        }

        private void CreateLeaf(Transform door, string hingeName, float hingeX, float sign, Material material)
        {
            var leaf = new Leaf
            {
                Door = door, OriginalParent = door.parent,
                OriginalSibling = door.GetSiblingIndex(), Sign = sign
            };
            // Store the original before any reparenting so a partial setup can also be restored.
            leaves.Add(leaf);
            leaf.Hinge = OwnedGroup(hingeName, cabinet);
            leaf.Hinge.localPosition = new Vector3(hingeX, .88f, -.205f);
            door.SetParent(leaf.Hinge, true);
            var rigidbody = leaf.Hinge.gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            rigidbody.interpolation = RigidbodyInterpolation.None;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            leaf.Handle = AddBox(leaf.Hinge, "DoorHandle", new Vector3(sign * .477f, 0f, -.034f),
                new Vector3(.025f, .15f, .04f), material).transform;
            foreach (Collider collider in leaf.Hinge.GetComponentsInChildren<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                BoxCollider box = collider as BoxCollider;
                if (box == null) throw new InvalidOperationException("Cabinet moving parts require box colliders.");
                var shape = new CollisionShape
                {
                    Collider = box,
                    CenterOffset = Quaternion.Inverse(leaf.Hinge.rotation) *
                        (box.transform.TransformPoint(box.center) - leaf.Hinge.position),
                    HalfExtents = Vector3.Scale(box.size * .5f, Absolute(box.transform.lossyScale)),
                    LocalRotation = Quaternion.Inverse(leaf.Hinge.rotation) * box.transform.rotation
                };
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = shape.CenterOffset + shape.LocalRotation * Vector3.Scale(shape.HalfExtents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    shape.Radius = Mathf.Max(shape.Radius, new Vector2(point.x, point.z).magnitude);
                }
                leaf.Shapes.Add(shape);
            }
        }

        private static Vector3 Absolute(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

        private void AdaptCabinetBooks()
        {
            // Only this authored cabinet's neighbouring book set is adapted. Its original
            // one-metre selection box and forward placement intersect the closed glass doors.
            if (cabinet.name != "F09_DisplayCabinet" || cabinet.parent == null || cabinet.parent.parent == null) return;
            Transform books = cabinet.parent.parent.Find("D00_Decoration/D10_Books_Set");
            Transform shelf = cabinet.Find("Shelf_02");
            if (books == null || shelf == null) return;
            Vector3 relative = cabinet.InverseTransformPoint(books.position);
            if (Mathf.Abs(relative.x) > .4f || relative.y < .8f || relative.y > 1.4f ||
                relative.z < -.45f || relative.z > .25f ||
                Mathf.Abs(Quaternion.Dot(books.rotation, cabinet.rotation)) < .999f) return;
            BoxCollider selectionBox = books.GetComponent<BoxCollider>();
            BoxCollider shelfBox = shelf.GetComponent<BoxCollider>();
            if (selectionBox == null || shelfBox == null) return;
            var bookMeshes = new List<MeshFilter>();
            foreach (Transform child in books)
            {
                MeshFilter mesh = child.GetComponent<MeshFilter>();
                if (child.name.StartsWith("Book_", StringComparison.Ordinal) && mesh != null && mesh.sharedMesh != null)
                    bookMeshes.Add(mesh);
            }
            if (bookMeshes.Count != 5) return;

            cabinetBooks = books;
            cabinetBooksCollider = selectionBox;
            originalBooksPosition = books.localPosition;
            originalBooksColliderCenter = selectionBox.center;
            originalBooksColliderSize = selectionBox.size;
            float shelfTop = cabinet.InverseTransformPoint(shelf.TransformPoint(shelfBox.center +
                Vector3.up * (shelfBox.size.y * .5f))).y;
            books.position = cabinet.TransformPoint(new Vector3(relative.x, shelfTop, 0f));
            foreach (MeshFilter mesh in bookMeshes)
            {
                Transform book = mesh.transform;
                repositionedBooks.Add(book);
                originalBookPositions.Add(book.localPosition);
                Vector3 position = book.localPosition;
                // The authored books are upright boxes of different heights; align their bases.
                position.y = -mesh.sharedMesh.bounds.min.y * book.localScale.y;
                book.localPosition = position;
            }

            bool hasBounds = false;
            Bounds localBounds = default;
            foreach (MeshFilter mesh in bookMeshes)
            {
                Bounds bounds = mesh.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 vertex = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 point = books.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                    if (!hasBounds) { localBounds = new Bounds(point, Vector3.zero); hasBounds = true; }
                    else localBounds.Encapsulate(point);
                }
            }
            selectionBox.center = localBounds.center;
            selectionBox.size = localBounds.size;
        }

        private void UpdateInteractionColliders()
        {
            interactable = cabinet.GetComponent<XRBaseInteractable>();
            if (interactable == null) return;
            originalInteractionColliders.AddRange(interactable.colliders);
            XRInteractionManager manager = interactable.interactionManager;
            bool registered = interactable.isActiveAndEnabled && manager != null;
            // The manager caches collider-to-interactable mappings at registration time.
            if (registered) manager.UnregisterInteractable((IXRInteractable)interactable);
            interactionMapChanged = true;
            foreach (Collider collider in generatedColliders)
                if (!interactable.colliders.Contains(collider)) interactable.colliders.Add(collider);
            if (registered) manager.RegisterInteractable((IXRInteractable)interactable);
        }

        public void SetInteractionAllowed(bool allowed)
        {
            interactionAllowed = allowed;
            if (!allowed) StopMotion();
        }

        public void SetXrAvailable(bool available)
        {
            xrAvailable = available && isActiveAndEnabled;
            if (!xrAvailable) ReleaseHands();
        }

        public bool TryOpen() => TryMove(openingAngle);
        public bool TryClose() => TryMove(0f);
        public bool TryResetClosed() => TryMove(0f);

        private bool TryMove(float destination)
        {
            if (!CommandAllowed(out string reason)) return Block(reason);
            if (IsHandPulling) return Block("Release the cabinet handle before resetting its doors.");
            Physics.SyncTransforms();
            foreach (Leaf leaf in leaves)
            {
                if (!SweepLeafClear(leaf, leaf.Angle, destination))
                {
                    StopMotion();
                    return Block(BlockedMessage);
                }
            }
            foreach (Leaf leaf in leaves)
            {
                leaf.TargetAngle = destination;
                leaf.MotorMoving = Mathf.Abs(leaf.Angle - destination) > AngleTolerance;
            }
            LastMessage = string.Empty;
            return true;
        }

        /// <summary>Only a hand within reach of this handle can acquire a leaf; rays never call this.</summary>
        public bool TryBeginHandPull(int doorIndex, Transform hand)
        {
            if (!xrAvailable || hand == null || doorIndex < 0 || doorIndex >= leaves.Count ||
                !CommandAllowed(out _)) return false;
            Leaf leaf = leaves[doorIndex];
            if (leaf.Handle == null || leaf.PullHand != null ||
                Vector3.Distance(hand.position, leaf.Handle.position) > HandleReach) return false;
            foreach (Leaf other in leaves) if (other.PullHand == hand) return false;
            Vector3 direction = FlatHandDirection(leaf, hand.position);
            if (direction.sqrMagnitude < .01f) return false;
            leaf.MotorMoving = false;
            leaf.TargetAngle = leaf.Angle;
            leaf.GrabStartAngle = leaf.Angle;
            leaf.GrabStartDirection = direction;
            leaf.PullHand = hand;
            LastMessage = string.Empty;
            HandPullStarted?.Invoke();
            return true;
        }

        /// <summary>Release holds the accepted angle; the doors never finish opening by themselves.</summary>
        public void EndHandPull(int doorIndex)
        {
            if (doorIndex < 0 || doorIndex >= leaves.Count) return;
            Leaf leaf = leaves[doorIndex];
            leaf.PullHand = null;
            leaf.TargetAngle = leaf.Angle;
            leaf.MotorMoving = false;
            NotifySettledState();
        }

        private Vector3 FlatHandDirection(Leaf leaf, Vector3 position)
        {
            Vector3 direction = Quaternion.Inverse(cabinet.rotation) * (position - leaf.Hinge.position);
            direction.y = 0f;
            return direction;
        }

        private static bool HandTracked(Hand hand)
        {
            if (hand.Controller == null || !hand.Controller.isActiveAndEnabled) return false;
            var state = hand.Controller.currentControllerState;
            const UnityEngine.XR.InputTrackingState pose = UnityEngine.XR.InputTrackingState.Position |
                UnityEngine.XR.InputTrackingState.Rotation;
            return state.isTracked && (state.inputTrackingState & pose) == pose;
        }

        private static bool IsUiTargeted(Hand hand)
        {
            foreach (XRRayInteractor ray in hand.Rays)
                if (ray != null && ray.isActiveAndEnabled && ray.TryGetCurrentUIRaycastResult(out var result) && result.isValid)
                    return true;
            return false;
        }

        private void UpdateHands()
        {
            foreach (Hand hand in hands)
            {
                bool tracked = xrAvailable && HandTracked(hand);
                bool pressed = tracked && hand.Controller.activateInteractionState.active;
                if (!tracked || !interactionAllowed)
                {
                    if (hand.LeafIndex >= 0) EndHandPull(hand.LeafIndex);
                    hand.LeafIndex = -1;
                    hand.AwaitRelease = true;
                    hand.WasPressed = pressed;
                    if (tracked && !pressed) hand.OwnsTriggerPress = false;
                    if (hand.OwnsTriggerPress) SuppressRay(hand);
                    else RestoreRay(hand);
                    continue;
                }
                if (!pressed)
                {
                    if (hand.LeafIndex >= 0) EndHandPull(hand.LeafIndex);
                    hand.LeafIndex = -1;
                    hand.AwaitRelease = false;
                    hand.OwnsTriggerPress = false;
                    RestoreRay(hand);
                }
                else if (!hand.WasPressed && !hand.AwaitRelease && !IsUiTargeted(hand))
                {
                    int nearest = -1;
                    float distance = HandleReach;
                    for (int i = 0; i < leaves.Count; i++)
                    {
                        Leaf leaf = leaves[i];
                        if (leaf.PullHand != null || leaf.Handle == null) continue;
                        float candidate = Vector3.Distance(hand.Controller.transform.position, leaf.Handle.position);
                        if (candidate <= distance) { nearest = i; distance = candidate; }
                    }
                    if (nearest >= 0 && TryBeginHandPull(nearest, hand.Controller.transform))
                    {
                        hand.LeafIndex = nearest;
                        hand.OwnsTriggerPress = true;
                        SuppressRay(hand);
                        hand.Controller.SendHapticImpulse(.2f, .04f);
                    }
                }
                if (hand.LeafIndex >= 0 || (hand.OwnsTriggerPress && pressed)) SuppressRay(hand);
                hand.WasPressed = pressed;
            }
        }

        private void SuppressRay(Hand hand)
        {
            if (!hand.Suppressed)
            {
                for (int i = 0; i < hand.Rays.Length; i++) hand.RayStates[i] = hand.Rays[i] != null && hand.Rays[i].enabled;
                for (int i = 0; i < hand.Lines.Length; i++) hand.LineStates[i] = hand.Lines[i] != null && hand.Lines[i].enabled;
                hand.Suppressed = true;
            }
            foreach (var ray in hand.Rays) if (ray != null) ray.enabled = false;
            foreach (var line in hand.Lines) if (line != null) line.enabled = false;
        }

        private void RestoreRay(Hand hand)
        {
            // Desktop mode owns the disabled ray state. Restore only after XR returns.
            if (!hand.Suppressed || !xrAvailable) return;
            for (int i = 0; i < hand.Rays.Length; i++)
                if (hand.Rays[i] != null) hand.Rays[i].enabled = hand.RayStates[i];
            for (int i = 0; i < hand.Lines.Length; i++)
                if (hand.Lines[i] != null) hand.Lines[i].enabled = hand.LineStates[i];
            hand.Suppressed = false;
        }

        private void ReleaseHands(bool clearPressOwnership = false)
        {
            foreach (Leaf leaf in leaves) leaf.PullHand = null;
            foreach (Hand hand in hands)
            {
                hand.LeafIndex = -1;
                hand.AwaitRelease = true;
                if (clearPressOwnership) hand.OwnsTriggerPress = false;
                if (!hand.OwnsTriggerPress) RestoreRay(hand);
            }
        }

        private bool CommandAllowed(out string reason)
        {
            reason = null;
            if (!IsReady || cabinet == null) reason = "The display cabinet is not ready.";
            else if (!isActiveAndEnabled || !interactionAllowed) reason = "Cabinet interaction is temporarily unavailable.";
            else if ((cabinet.lossyScale - configuredScale).sqrMagnitude > .000001f)
                reason = "The cabinet scale changed. Restart the review before moving its doors.";
            else if (CanInteract != null)
            {
                try { if (!CanInteract()) reason = "Finish the current interaction before moving the cabinet doors."; }
                catch { reason = "Cabinet interaction is temporarily unavailable."; }
            }
            return reason == null;
        }

        private bool Block(string message)
        {
            bool changed = LastMessage != message;
            LastMessage = message;
            if (changed) ActionBlocked?.Invoke(message);
            return false;
        }

        private void Update()
        {
            UpdateHands();
            if (!IsMoving) return;
            if (!CommandAllowed(out string reason))
            {
                StopMotion();
                Block(reason);
                return;
            }
            Physics.SyncTransforms();
            foreach (Leaf leaf in leaves)
            {
                if (leaf.Hinge == null) { IsReady = false; StopMotion(); return; }
                if (leaf.PullHand != null)
                {
                    // Preserve the pickup offset: touching anywhere within the small handle zone
                    // must not teleport the leaf. Project hand movement onto the vertical hinge.
                    Vector3 hand = leaf.PullHand.position;
                    Vector3 direction = FlatHandDirection(leaf, hand);
                    if (direction.sqrMagnitude < .01f || direction.magnitude > 1f ||
                        Mathf.Abs(hand.y - leaf.Handle.position.y) > .5f)
                    {
                        CancelLeafPull(leaf);
                        continue;
                    }
                    leaf.TargetAngle = Mathf.Clamp(leaf.GrabStartAngle + leaf.Sign *
                        Vector3.SignedAngle(leaf.GrabStartDirection, direction, Vector3.up), 0f, openingAngle);
                    if (leaf.TargetAngle <= ClosedTolerance) leaf.TargetAngle = 0f;
                    if (leaf.TargetAngle >= openingAngle - 2f) leaf.TargetAngle = openingAngle;
                }
                if (!leaf.MotorMoving && leaf.PullHand == null) continue;
                // A tracking spike cannot sweep a full door through the room in one frame.
                float speed = leaf.PullHand != null ? 250f : Mathf.Max(0f, degreesPerSecond);
                float step = Mathf.Min(12f, speed * Time.unscaledDeltaTime);
                float next = Mathf.MoveTowards(leaf.Angle, leaf.TargetAngle, step);
                if (Mathf.Abs(next - leaf.Angle) <= AngleTolerance) continue;
                if (!SweepLeafClear(leaf, leaf.Angle, next))
                {
                    CancelLeafPull(leaf);
                    leaf.MotorMoving = false;
                    leaf.TargetAngle = leaf.Angle;
                    Block(BlockedMessage);
                    continue;
                }
                leaf.Angle = next;
                leaf.Hinge.localRotation = Quaternion.Euler(0f, leaf.Sign * next, 0f);
                if (Mathf.Abs(next - leaf.TargetAngle) <= AngleTolerance) leaf.MotorMoving = false;
            }
            Physics.SyncTransforms();
            NotifySettledState();
        }

        private void CancelLeafPull(Leaf leaf)
        {
            leaf.PullHand = null;
            foreach (Hand hand in hands)
                if (hand.LeafIndex >= 0 && leaves[hand.LeafIndex] == leaf)
                {
                    hand.LeafIndex = -1;
                    hand.AwaitRelease = true;
                    // Retain this press until it ends; never pass a held grab Trigger to UI.
                }
        }

        private void NotifySettledState()
        {
            int state = IsClosed ? 1 : IsOpen ? 2 : 0;
            if (state == settledState) return;
            settledState = state;
            if (state == 0) return;
            StateChanged?.Invoke(state == 2);
        }

        private bool SweepLeafClear(Leaf leaf, float start, float end)
        {
            LastBlockingCollider = null;
            int segments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(end - start) / SweepStepDegrees));
            float segment = (end - start) / segments;
            for (int i = 0; i < segments; i++)
            {
                float middle = start + segment * (i + .5f);
                {
                    if (leaf.Hinge == null) return false;
                    Quaternion rotation = cabinet.rotation * Quaternion.Euler(0f, leaf.Sign * middle, 0f);
                    foreach (CollisionShape shape in leaf.Shapes)
                    {
                        if (shape.Collider == null || !shape.Collider.enabled) return false;
                        // Inflating the middle pose by the maximum rotational displacement
                        // encloses the entire sub-arc, including both endpoint poses.
                        float arcMargin = 2f * shape.Radius * Mathf.Sin(Mathf.Abs(segment) * Mathf.Deg2Rad * .25f);
                        Vector3 extent = shape.HalfExtents + Vector3.one * (arcMargin + ContactMargin);
                        Vector3 center = leaf.Hinge.position + rotation * shape.CenterOffset;
                        int count = Physics.OverlapBoxNonAlloc(center, extent, overlaps,
                            rotation * shape.LocalRotation, ~0, QueryTriggerInteraction.Ignore);
                        if (count >= overlaps.Length) return false;
                        for (int hitIndex = 0; hitIndex < count; hitIndex++)
                        {
                            Collider hit = overlaps[hitIndex];
                            if (hit == null || !hit.enabled || hit.isTrigger || hit.transform.IsChildOf(cabinet)) continue;
                            LastBlockingCollider = hit;
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        private void StopMotion()
        {
            ReleaseHands();
            foreach (Leaf leaf in leaves)
            {
                leaf.MotorMoving = false;
                leaf.TargetAngle = leaf.Angle;
            }
            NotifySettledState();
        }

        private void OnDisable()
        {
            StopMotion();
            // This component will no longer observe a future release. Never strand the ray when
            // the journey itself is disabled; the caller owns any UI/desktop mode transition.
            ReleaseHands(true);
            xrAvailable = false;
        }

        private void OnDestroy()
        {
            IsReady = false;
            StopMotion();
            ReleaseHands(true);
            // The cabinet may already be tearing down. Its generated children follow that
            // hierarchy; do not reparent doors or register interactables during destruction.
            if (handleMaterial != null) Release(handleMaterial);
            handleMaterial = null;
        }

        /// <summary>
        /// Restores the original geometry before explicitly removing this component from a live
        /// cabinet. Scene unload and Play Mode teardown use OnDestroy instead.
        /// </summary>
        public void RestoreOriginalGeometry() => Cleanup();

        private void Cleanup()
        {
            if (cleaning) return;
            cleaning = true;
            IsReady = false;
            StopMotion();
            ReleaseHands(true);
            foreach (Collider collider in generatedColliders) if (collider != null) collider.enabled = false;
            if (interactionMapChanged && interactable != null)
            {
                XRInteractionManager manager = interactable.interactionManager;
                bool registered = interactable.isActiveAndEnabled && manager != null;
                if (registered) manager.UnregisterInteractable((IXRInteractable)interactable);
                interactable.colliders.Clear();
                foreach (Collider collider in originalInteractionColliders)
                    if (collider != null) interactable.colliders.Add(collider);
                if (registered) manager.RegisterInteractable((IXRInteractable)interactable);
            }
            foreach (Leaf leaf in leaves)
            {
                if (leaf.Door == null || leaf.OriginalParent == null) continue;
                // Removing the component does not swing the surviving doors through the player.
                leaf.Door.SetParent(leaf.OriginalParent, true);
                leaf.Door.SetSiblingIndex(leaf.OriginalSibling);
            }
            if (originalBodyRenderer != null) originalBodyRenderer.enabled = originalRendererEnabled;
            if (originalBodyCollider != null) originalBodyCollider.enabled = originalColliderEnabled;
            if (cabinetBooks != null) cabinetBooks.localPosition = originalBooksPosition;
            if (cabinetBooksCollider != null)
            {
                cabinetBooksCollider.center = originalBooksColliderCenter;
                cabinetBooksCollider.size = originalBooksColliderSize;
            }
            for (int i = 0; i < repositionedBooks.Count; i++)
                if (repositionedBooks[i] != null) repositionedBooks[i].localPosition = originalBookPositions[i];
            foreach (GameObject owned in ownedObjects) Release(owned);
            if (handleMaterial != null) Release(handleMaterial);
            originalInteractionColliders.Clear();
            generatedColliders.Clear();
            ownedObjects.Clear();
            leaves.Clear();
            repositionedBooks.Clear();
            originalBookPositions.Clear();
            cabinetBooks = null;
            cabinetBooksCollider = null;
            interactionMapChanged = false;
            originalBodyRenderer = null;
            originalBodyCollider = null;
            interactable = null;
            handleMaterial = null;
            cabinet = null;
            cleaning = false;
        }

        private static void Release(UnityEngine.Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) Destroy(owned);
            else DestroyImmediate(owned);
        }
    }
}
