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
    [DisallowMultipleComponent]
    public sealed class ReviewCabinetDoors : MonoBehaviour
    {
        private const float SweepStepDegrees = 1f;
        private const float ContactMargin = .004f;
        private const float AngleTolerance = .01f;
        private const string BlockedMessage = "Door movement is blocked. Step clear and try again.";

        [SerializeField, Range(80f, 110f)] private float openingAngle = 90f;
        [SerializeField, Range(20f, 160f)] private float degreesPerSecond = 75f;

        private sealed class Leaf
        {
            public Transform Door, OriginalParent, Hinge;
            public int OriginalSibling;
            public float Sign;
            public readonly List<CollisionShape> Shapes = new List<CollisionShape>();
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
        private bool interactionMapChanged, interactionAllowed = true;
        private float angle, targetAngle;
        private Vector3 configuredScale;
        private bool cleaning;

        public bool IsReady { get; private set; }
        public bool IsOpen => IsReady && !IsMoving && Mathf.Abs(angle - openingAngle) <= AngleTolerance;
        public bool IsClosed => IsReady && !IsMoving && angle <= AngleTolerance;
        public bool IsMoving { get; private set; }
        public float OpenFraction => IsReady ? Mathf.Clamp01(angle / openingAngle) : 0f;
        public string LastMessage { get; private set; } = string.Empty;
        public Collider LastBlockingCollider { get; private set; }
        public Bounds ClosedBounds { get; private set; }
        public Func<bool> CanInteract { get; set; }
        public event Action<bool> StateChanged;
        public event Action<string> ActionBlocked;
        public Transform CabinetRoot => cabinet;
        public Transform LeftHinge => leaves.Count > 0 ? leaves[0].Hinge : null;
        public Transform RightHinge => leaves.Count > 1 ? leaves[1].Hinge : null;

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
                angle = targetAngle = 0f;
                IsMoving = false;
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
            AddBox(leaf.Hinge, "DoorHandle", new Vector3(sign * .477f, 0f, -.034f),
                new Vector3(.025f, .15f, .04f), material);
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

        public bool TryOpen() => TryMove(openingAngle);
        public bool TryClose() => TryMove(0f);
        public bool TryResetClosed() => TryMove(0f);

        private bool TryMove(float destination)
        {
            if (!CommandAllowed(out string reason)) return Block(reason);
            if (Mathf.Abs(angle - destination) <= AngleTolerance)
            {
                targetAngle = angle;
                IsMoving = false;
                LastMessage = string.Empty;
                return true;
            }
            Physics.SyncTransforms();
            if (!SweepClear(angle, destination))
            {
                StopMotion();
                return Block(BlockedMessage);
            }
            targetAngle = destination;
            IsMoving = true;
            LastMessage = string.Empty;
            return true;
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
            LastMessage = message;
            ActionBlocked?.Invoke(message);
            return false;
        }

        private void Update()
        {
            if (!IsMoving) return;
            if (!CommandAllowed(out string reason))
            {
                StopMotion();
                Block(reason);
                return;
            }
            // Limit angular movement after a slow frame; every accepted step is swept below.
            float step = Mathf.Min(4f, Mathf.Max(0f, degreesPerSecond) * Time.unscaledDeltaTime);
            float next = Mathf.MoveTowards(angle, targetAngle, step);
            if (next == angle) return;
            Physics.SyncTransforms();
            if (!SweepClear(angle, next))
            {
                StopMotion();
                Block(BlockedMessage);
                return;
            }
            angle = next;
            foreach (Leaf leaf in leaves)
            {
                if (leaf.Hinge == null) { IsReady = false; StopMotion(); return; }
                leaf.Hinge.localRotation = Quaternion.Euler(0f, leaf.Sign * angle, 0f);
            }
            Physics.SyncTransforms();
            if (Mathf.Abs(angle - targetAngle) <= AngleTolerance)
            {
                IsMoving = false;
                LastMessage = string.Empty;
                StateChanged?.Invoke(IsOpen);
            }
        }

        private bool SweepClear(float start, float end)
        {
            LastBlockingCollider = null;
            int segments = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(end - start) / SweepStepDegrees));
            float segment = (end - start) / segments;
            for (int i = 0; i < segments; i++)
            {
                float middle = start + segment * (i + .5f);
                foreach (Leaf leaf in leaves)
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
            IsMoving = false;
            targetAngle = angle;
        }

        private void OnDisable() => StopMotion();

        private void OnDestroy()
        {
            IsReady = false;
            StopMotion();
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
