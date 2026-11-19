#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LivingRoom30;
using LivingRoom30.Measurement;
using LivingRoom30.Review;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

/// <summary>Run from Assets/Editor in an isolated project copy using -executeMethod ReviewCabinetValidation.Run.</summary>
public static class ReviewCabinetValidation
{
    [Serializable] sealed class Report
    {
        public bool passed;
        public string limitation;
        public string[] checks, errors;
    }

    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly Dictionary<string, Vector3> furniturePositions = new Dictionary<string, Vector3>();
    static readonly Dictionary<string, Quaternion> furnitureRotations = new Dictionary<string, Quaternion>();
    static ReviewJourneyController journey;
    static ReviewCabinetDoors cabinet;
    static SpatialMeasureManager manager;
    static CharacterController playerCapsule;
    static readonly List<Behaviour> locomotionProviders = new List<Behaviour>();
    static TutorialController controls;
    static Camera head;
    static Transform origin, root, leftDoor, rightDoor, cabinetBooks;
    static Button toggle;
    static GameObject blocker;
    static Bounds sourceBounds, closedBounds, closedOccupiedBounds;
    static Vector3 openLeftCenter, closedLeftCenter, closedRightCenter;
    static float stoppedFraction;
    static int phase, originalCabinetCount, originalPivotCount;
    static int booksRootId, booksColliderId, booksAnnotationId;
    static readonly List<int> bookMeshIds = new List<int>();
    static double deadline, waitUntil;
    static string output;
    static bool finishing, requestedPass;

    public static void Run()
    {
        checks.Clear(); errors.Clear(); furniturePositions.Clear(); furnitureRotations.Clear(); bookMeshIds.Clear(); locomotionProviders.Clear();
        finishing = requestedPass = false; phase = 0;
        output = Path.GetFullPath("../Results");
        Directory.CreateDirectory(output);
        Application.logMessageReceived += Log;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorSceneManager.OpenScene("Assets/Scenes/SceneNew.unity");
        GameObject cabinetObject = GameObject.Find("F09_DisplayCabinet");
        Require(cabinetObject != null, "The authored television-side F09 display cabinet exists");
        sourceBounds = GeometryUtils.GetWorldAABB(cabinetObject);
        Require(UnityEngine.Object.FindObjectsOfType<AnnotationSpawner>().Length == 23,
            "The authored scene retains all 23 annotation connections before Play");
        deadline = EditorApplication.timeSinceStartup + 150;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Log(string text, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            errors.Add(text + "\n" + stack);
    }

    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline)
                throw new Exception("Cabinet validation timed out at phase " + phase);
            if (!EditorApplication.isPlaying)
            {
                if (phase > 0 && !EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new Exception("Play Mode stopped externally at cabinet phase " + phase);
                return;
            }
            // Keep the physical capsule active, but freeze synthetic fixture poses instead of
            // allowing locomotion gravity to push them away from the intended sweep location.
            foreach (Behaviour provider in locomotionProviders)
                if (provider != null && provider.enabled) provider.enabled = false;
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            if (cabinet != null && cabinet.IsMoving && WaitForDoorPhase(phase)) return;
            if (phase == 0)
            {
                journey = UnityEngine.Object.FindObjectOfType<ReviewJourneyController>();
                manager = UnityEngine.Object.FindObjectOfType<SpatialMeasureManager>();
                if (journey == null || !journey.IsReady || manager == null) return;
                cabinet = journey.Cabinet;
                Require(cabinet != null && cabinet.IsReady, "The scene journey initialises the cabinet incrementally");
                root = GameObject.Find("F09_DisplayCabinet").transform;
                leftDoor = Descendant(root, "GlassDoor_Left");
                rightDoor = Descendant(root, "GlassDoor_Right");
                Require(leftDoor != null && rightDoor != null, "Both authored door objects are retained");
                Require(leftDoor.parent != root && rightDoor.parent != root && leftDoor.parent != rightDoor.parent,
                    "Each original door has its own hinge parent");
                var body = Descendant(root, "CabinetBody");
                Require(body != null && !body.GetComponent<Renderer>().enabled && !body.GetComponent<Collider>().enabled,
                    "The original solid cabinet body no longer renders or blocks the interior");
                Require(root.GetComponent<FurnitureInfo>() != null && root.GetComponent<AnnotationSpawner>() != null,
                    "The cabinet root retains its furniture identity and annotation component");
                ValidateCabinetBooks(true);
                ValidateInteractionMapping();
                Require(UnityEngine.Object.FindObjectsOfType<AnnotationSpawner>().Length == 23,
                    "Runtime cabinet adaptation preserves all 23 annotation connections");
                originalCabinetCount = UnityEngine.Object.FindObjectsOfType<ReviewCabinetDoors>().Length;
                originalPivotCount = root.childCount;
                SnapshotOtherFurniture();
                foreach (var driver in UnityEngine.Object.FindObjectsOfType<TrackedPoseDriver>(true)) driver.enabled = false;
                foreach (var follower in UnityEngine.Object.FindObjectsOfType<XRHeadPoseFollower>(true)) follower.enabled = false;
                foreach (var simulator in UnityEngine.Object.FindObjectsOfType<XRDeviceSimulator>(true)) simulator.enabled = false;
                foreach (var controller in UnityEngine.Object.FindObjectsOfType<ActionBasedController>(true))
                {
                    controller.enableInputActions = false; controller.enableInputTracking = false;
                    controller.transform.localPosition = new Vector3(controller.name.ToLowerInvariant().Contains("left") ? -.25f : .25f, -.35f, .35f);
                    controller.currentControllerState.isTracked = true;
                    controller.currentControllerState.inputTrackingState = UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation;
                }
                head = (Camera)Field(manager, "xrCam"); origin = manager.XrOriginTransform;
                Set(manager, "forceDesktop", true); Set(manager, "useXrNow", true); Set(manager, "mainCam", head);
                head.enabled = true; manager.SetReviewLocomotionBlocked(false);
                playerCapsule = origin.GetComponent<CharacterController>();
                foreach (var provider in UnityEngine.Object.FindObjectsOfType<ActionBasedContinuousMoveProvider>(true))
                { provider.enabled = false; locomotionProviders.Add(provider); }
                foreach (var provider in UnityEngine.Object.FindObjectsOfType<ActionBasedSnapTurnProvider>(true))
                { provider.enabled = false; locomotionProviders.Add(provider); }
                controls = (TutorialController)Field(journey, "controls");
                journey.SessionDirectoryOverride = Path.Combine(output, "CabinetSessions");
                toggle = FindToggle();
                Require(toggle != null, "The cabinet has a dedicated Trigger UI button");
                Call(journey, "PauseGuidance");
                PlaceAtCabinet();
                closedBounds = GeometryUtils.GetWorldAABB(root.gameObject);
                closedOccupiedBounds = GeometryUtils.GetOccupiedWorldAABB(root.gameObject);
                Require(BoundsEqual(cabinet.ClosedBounds, sourceBounds), "Closed geometry bounds match the original cabinet footprint");
                closedLeftCenter = leftDoor.GetComponent<Collider>().bounds.center;
                closedRightCenter = rightDoor.GetComponent<Collider>().bounds.center;
                Wait(.25, 1);
            }
            else if (phase == 1)
            {
                Require(cabinet.IsClosed && !cabinet.IsMoving, "Doors start fully closed");
                ClickToggle();
                Wait(1.6, 2);
            }
            else if (phase == 2)
            {
                Require(cabinet.IsOpen && !cabinet.IsMoving, "Trigger button opens both doors fully: "
                    + cabinet.OpenFraction + " / " + cabinet.LastMessage + " / "
                    + (cabinet.LastBlockingCollider != null ? cabinet.LastBlockingCollider.name : "no blocker"));
                ValidateDoorCollider(leftDoor, closedLeftCenter);
                ValidateDoorCollider(rightDoor, closedRightCenter);
                Require(BoundsEqual(GeometryUtils.GetWorldAABB(root.gameObject), closedBounds),
                    "Opening doors does not inflate furniture dimension bounds");
                Bounds openOccupied = GeometryUtils.GetOccupiedWorldAABB(root.gameObject);
                Require(openOccupied.min.z < closedOccupiedBounds.min.z - .25f &&
                    openOccupied.size.z > closedOccupiedBounds.size.z + .25f,
                    "Open doors increase the occupied depth used for clearance estimates");
                ValidateInteractionMapping();
                Require(InteriorDepth() > .20f, "Open doors reveal a hollow interior rather than a solid wooden cube");
                openLeftCenter = leftDoor.GetComponent<Collider>().bounds.center;
                Capture("cabinet-open.png");
                Require(!(bool)Field(journey, "cabinetOpenedForTask"), "Free exploration does not complete the guided opening requirement in the background");
                Require(cabinet.TryClose(), "A fully opened cabinet can be closed");
                Wait(1.6, 3);
            }
            else if (phase == 3)
            {
                Require(cabinet.IsClosed, "Doors return to their authored closed pose");
                Require(Vector3.Distance(leftDoor.GetComponent<Collider>().bounds.center, closedLeftCenter) < .005f,
                    "The closed door collider returns without transform drift");
                Require(BoundsEqual(GeometryUtils.GetOccupiedWorldAABB(root.gameObject), closedOccupiedBounds),
                    "Closing doors restores the occupied clearance envelope");
                blocker = MakeBlocker(openLeftCenter, new Vector3(.15f, .25f, .15f));
                cabinet.TryOpen();
                Wait(1.6, 4);
            }
            else if (phase == 4)
            {
                Require(!cabinet.IsOpen && !cabinet.IsMoving, "An obstacle along the opening sweep prevents a fully open state");
                Require(cabinet.LastBlockingCollider == blocker.GetComponent<Collider>(),
                    "The sweep identifies the injected obstacle rather than an input gate");
                Require(!string.IsNullOrWhiteSpace(cabinet.LastMessage), "Blocked door motion provides a readable message");
                RemoveBlocker();
                Require(cabinet.TryClose(), "Blocked opening can recover towards the closed direction");
                Wait(1.6, 5);
            }
            else if (phase == 5)
            {
                Require(cabinet.IsClosed, "The obstacle recovery closes safely");
                Require(cabinet.TryOpen(), "An unobstructed opening can start again");
                Wait(.12, 6);
            }
            else if (phase == 6)
            {
                blocker = MakeBlocker(openLeftCenter, new Vector3(.15f, .25f, .15f));
                Wait(1.6, 7);
            }
            else if (phase == 7)
            {
                Require(!cabinet.IsOpen && !cabinet.IsMoving, "An obstacle introduced during motion stops the door before passing through it");
                Require(cabinet.LastBlockingCollider == blocker.GetComponent<Collider>(),
                    "Mid-motion collision identifies the newly introduced obstacle");
                RemoveBlocker(); Require(cabinet.TryClose(), "A stopped mid-motion door supports safe reversal");
                Wait(1.6, 8);
            }
            else if (phase == 8)
            {
                Require(cabinet.IsClosed, "Safe reversal reaches the closed endpoint");
                PlaceHead(new Vector3(openLeftCenter.x, 1.6f, openLeftCenter.z), root.position + Vector3.up);
                Require(playerCapsule.enabled, "The player capsule is active for physical obstruction testing");
                Wait(.2, 81);
            }
            else if (phase == 81)
            {
                Physics.SyncTransforms();
                bool accepted = cabinet.TryOpen();
                Require(!accepted && cabinet.LastBlockingCollider == playerCapsule,
                    "The real CharacterController is the blocking collider, not a cached interaction gate: "
                    + cabinet.LastMessage + " / " + (cabinet.LastBlockingCollider != null ? cabinet.LastBlockingCollider.name : "no blocker"));
                Wait(.2, 9);
            }
            else if (phase == 9)
            {
                Require(!cabinet.IsOpen && !cabinet.IsMoving, "The real player capsule blocks a door sweep through the viewer");
                PlaceAtCabinet(); Wait(.2, 91);
            }
            else if (phase == 91)
            {
                bool accepted = cabinet.TryClose();
                Require(accepted, "Moving away from the sweep permits closing: " + cabinet.LastMessage);
                Wait(1.6, 10);
            }
            else if (phase == 10)
            {
                Require(cabinet.IsClosed, "Doors are closed before input ownership checks");
                PlaceHead(new Vector3(-2.7f, 1.6f, -2.42f), root.position + Vector3.up); Wait(.2, 11);
            }
            else if (phase == 11)
            {
                Require(!toggle.gameObject.activeInHierarchy || !toggle.interactable, "The door UI is unavailable from across the room");
                toggle.onClick.Invoke();
                Require(!cabinet.TryOpen() && cabinet.IsClosed, "Out-of-range direct and UI commands cannot open the cabinet");
                PlaceAtCabinet(); controls.HelpButton.onClick.Invoke(); Wait(.2, 12);
            }
            else if (phase == 12)
            {
                Require(controls.gameObject.activeInHierarchy, "HELP is open for input ownership testing");
                Require(!cabinet.TryOpen() && cabinet.IsClosed, "HELP blocks cabinet commands");
                controls.Dismiss();
                root.GetComponent<AnnotationSpawner>().OnSelectEntered(null); Wait(.2, 13);
            }
            else if (phase == 13)
            {
                Require(AnnotationSpawner.currentActiveUI != null, "Grip's existing annotation entry still opens the cabinet note editor");
                Require(!cabinet.TryOpen() && cabinet.IsClosed, "The annotation editor blocks cabinet commands");
                var note = AnnotationSpawner.currentActiveUI.GetComponent<AnnotationUI>();
                note.descriptionInput.text = "Cabinet test draft";
                journey.OpenGuidance();
                Require(AnnotationSpawner.currentActiveUI != null && note.descriptionInput.text == "Cabinet test draft",
                    "Reopening GUIDE preserves an active cabinet annotation draft");
                note.CloseUI(); Call(journey, "PauseGuidance"); Wait(.2, 14);
            }
            else if (phase == 14)
            {
                journey.BeginReview(); SetStage("Display"); PlaceAtCabinet(); Wait(.6, 15);
            }
            else if (phase == 15)
            {
                Require(journey.CurrentStage == "Display" && (bool)Field(journey, "arrived"), "The existing storage review point remains the cabinet task entry");
                Call(journey, "Answer", "UNSURE");
                Require(journey.CurrentStage == "Display" && journey.Session.observations.Find(o => o.task == "Display") == null,
                    "Display cannot be rated before opening and observing the doors");
                ClickToggle();
                journey.RequestFinish();
                Require(!journey.IsSummaryMode, "Ending is protected while the cabinet is moving");
                Wait(1.6, 16);
            }
            else if (phase == 16)
            {
                Require(cabinet.IsOpen && (bool)Field(journey, "cabinetOpenedForTask"), "Opening at the storage point satisfies the guided action requirement");
                journey.RequestFinish();
                Require(!journey.IsSummaryMode, "Ending requires closing the fully opened cabinet before its controls can be hidden");
                var targets = (Vector3[])Field(journey, "lookTargets");
                Require(targets.Length > 0, "The open-cabinet stage supplies an observation target");
                head.transform.rotation = Quaternion.LookRotation(targets[0] + Vector3.up * .12f - head.transform.position);
                Wait(1.7, 17);
            }
            else if (phase == 17)
            {
                Require((int)Field(journey, "lookedCount") == ((Vector3[])Field(journey, "lookTargets")).Length,
                    "A real head-direction dwell completes open-cabinet observation");
                Call(journey, "Answer", "UNSURE");
                Require(journey.CurrentStage == "Display" && cabinet.IsOpen,
                    "Rating the open cabinet keeps the task pending until doors are closed");
                Require(journey.Session.observations.Find(o => o.task == "Display" && o.answer == "UNSURE" && o.cabinetDoorsOpen) != null,
                    "The existing Display observation stores the open-space rating");
                Call(journey, "PauseGuidance"); ClickToggle(); Wait(1.6, 18);
            }
            else if (phase == 18)
            {
                Require(cabinet.IsClosed && journey.CurrentStage == "Display", "Closing in free exploration does not advance a paused GUIDE");
                Require((bool)Field(journey, "cabinetClosedAfterReview"),
                    "Closing after a saved rating retains task completion while GUIDE is paused");
                journey.OpenGuidance(); Wait(.2, 19);
            }
            else if (phase == 19)
            {
                Require(journey.CurrentStage == "Bookcase", "Resuming after the completed close continues to Bookcase without another open-close cycle");
                var rows = (List<ReviewSummaryPanel.PointRow>)Call(journey, "SummaryPoints");
                Require(rows.Count == 6, "The upgrade preserves the six existing evaluation points");
                Require(journey.Session.actions.Exists(a => a.action.ToLowerInvariant().Contains("cabinet") && a.action.ToLowerInvariant().Contains("open")),
                    "Session actions record the cabinet opening");
                Require(journey.Session.actions.Exists(a => a.action.ToLowerInvariant().Contains("cabinet") && a.action.ToLowerInvariant().Contains("clos")),
                    "Session actions record the cabinet closing");
                journey.RequestFinish(); Wait(.2, 20);
            }
            else if (phase == 20)
            {
                Require(journey.IsSummaryMode && journey.Summary.IsVisible, "An ordinary completed door task permits the existing summary");
                Require(!cabinet.TryOpen(), "The review summary blocks cabinet commands");
                journey.ResumeReview(); Call(journey, "PauseGuidance"); PlaceAtCabinet(); Wait(.2, 21);
            }
            else if (phase == 21)
            {
                Require(cabinet.TryOpen(), "Free exploration can operate the cabinet after returning from results");
                Wait(.12, 22);
            }
            else if (phase == 22)
            {
                journey.enabled = false; stoppedFraction = cabinet.OpenFraction; Wait(.35, 23);
            }
            else if (phase == 23)
            {
                Require(Mathf.Abs(cabinet.OpenFraction - stoppedFraction) < .001f, "Disabling the journey stops door motion without snapping the geometry");
                journey.enabled = true; Wait(.2, 24);
            }
            else if (phase == 24)
            {
                Require(Mathf.Abs(cabinet.OpenFraction - stoppedFraction) < .001f && !cabinet.IsMoving,
                    "Re-enabling preserves the stopped door pose without automatic motion");
                Require(UnityEngine.Object.FindObjectsOfType<ReviewCabinetDoors>().Length == originalCabinetCount && root.childCount == originalPivotCount,
                    "Enable-disable cycles do not duplicate hinges or cabinet controllers");
                Require(cabinet.TryClose(), "The stopped pose can be closed after re-enabling"); Wait(1.6, 25);
            }
            else if (phase == 25)
            {
                Require(cabinet.IsClosed, "The door lifecycle recovery restores a usable closed cabinet");
                Call(journey, "Replay"); Wait(.2, 26);
            }
            else if (phase == 26)
            {
                Require(journey.CurrentStage == "Entry" && journey.Session.observations.Count == 0,
                    "New review uses the original entry sequence with a fresh observation list");
                Require(cabinet.IsClosed && root.childCount == originalPivotCount, "Replay preserves closed geometry without rebuilding the room");
                ValidateOtherFurniture();
                ValidateCabinetBooks(false);
                ValidateInteractionMapping();
                Require(UnityEngine.Object.FindObjectsOfType<AnnotationSpawner>().Length == 23,
                    "All annotation connections remain present after the complete door regression");
                Finish(true);
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception.ToString());
            Finish(false);
        }
    }

    static void SetStage(string name)
    {
        var method = typeof(ReviewJourneyController).GetMethod("SetStage", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(journey, new[] { Enum.Parse(method.GetParameters()[0].ParameterType, name) });
    }

    static bool WaitForDoorPhase(int value) => value == 2 || value == 3 || value == 4 || value == 5 ||
        value == 7 || value == 8 || value == 9 || value == 10 || value == 16 || value == 18 ||
        value == 19 || value == 25;

    static void PlaceAtCabinet() => PlaceHead(new Vector3(-2.85f, 1.6f, 1.35f), new Vector3(-2.85f, .9f, 2.32f));
    static void PlaceHead(Vector3 eye, Vector3 target)
    {
        origin.position += eye - head.transform.position;
        head.transform.rotation = Quaternion.LookRotation(target - head.transform.position);
        Physics.SyncTransforms();
    }

    static Button FindToggle()
    {
        foreach (var button in journey.GetComponentsInChildren<Button>(true))
            if (button.name == "CabinetToggle") return button;
        return null;
    }

    static void ClickToggle()
    {
        Require(toggle != null && toggle.gameObject.activeInHierarchy && toggle.interactable,
            "The nearby cabinet Trigger button is available");
        toggle.onClick.Invoke();
    }

    static Transform Descendant(Transform parent, string name)
    {
        foreach (var candidate in parent.GetComponentsInChildren<Transform>(true))
            if (candidate.name == name) return candidate;
        return null;
    }

    static void ValidateDoorCollider(Transform door, Vector3 closedCenter)
    {
        var collider = door.GetComponent<Collider>(); var renderer = door.GetComponent<Renderer>();
        Require(collider != null && collider.enabled && renderer != null && renderer.enabled,
            "Door mesh and collision remain enabled for " + door.name);
        Require(Vector3.Distance(collider.bounds.center, closedCenter) > .10f,
            "The collider moves around its hinge for " + door.name);
        Require(Vector3.Distance(collider.bounds.center, renderer.bounds.center) < .005f &&
            Vector3.Distance(collider.bounds.size, renderer.bounds.size) < .01f,
            "Visual and collision bounds move together for " + door.name);
    }

    static void ValidateCabinetBooks(bool captureIdentity)
    {
        Transform books = root.parent.parent.Find("D00_Decoration/D10_Books_Set");
        Require(books != null && books.parent.name == "D00_Decoration",
            "The D10 book set retains its authored name and decoration parent");
        BoxCollider box = books.GetComponent<BoxCollider>();
        AnnotationSpawner annotation = books.GetComponent<AnnotationSpawner>();
        Require(box != null && box.enabled && annotation != null && annotation.annotationUIPrefab != null,
            "D10 retains a live selection collider and the existing annotation entry");
        var filters = new List<MeshFilter>();
        foreach (Transform child in books)
        {
            MeshFilter mesh = child.GetComponent<MeshFilter>();
            if (child.name.StartsWith("Book_", StringComparison.Ordinal) && mesh != null && mesh.sharedMesh != null)
                filters.Add(mesh);
        }
        Require(filters.Count == 5, "All five authored book meshes remain in D10");
        Bounds meshBounds = filters[0].GetComponent<Renderer>().bounds;
        for (int i = 1; i < filters.Count; i++) meshBounds.Encapsulate(filters[i].GetComponent<Renderer>().bounds);
        Require(Vector3.Distance(box.size, Vector3.one) > .5f && BoundsEqual(box.bounds, meshBounds),
            "D10 selection bounds fit its five visible books instead of a one-metre cube");
        Collider shelf = root.Find("Shelf_02").GetComponent<Collider>();
        Require(Mathf.Abs(meshBounds.min.y - shelf.bounds.max.y) < .005f,
            "Book bases rest on the actual Shelf_02 upper surface");
        Require(meshBounds.min.x >= sourceBounds.min.x && meshBounds.max.x <= sourceBounds.max.x &&
            meshBounds.min.z > sourceBounds.min.z + .05f && meshBounds.max.z < sourceBounds.max.z,
            "The visible books sit inside the cabinet and behind the closed-door plane");
        if (captureIdentity)
        {
            cabinetBooks = books;
            booksRootId = books.GetInstanceID(); booksColliderId = box.GetInstanceID(); booksAnnotationId = annotation.GetInstanceID();
            foreach (MeshFilter mesh in filters) bookMeshIds.Add(mesh.GetInstanceID());
        }
        else
        {
            Require(books == cabinetBooks && books.GetInstanceID() == booksRootId &&
                box.GetInstanceID() == booksColliderId && annotation.GetInstanceID() == booksAnnotationId,
                "Book root collider and annotation references survive door and review lifecycles");
            for (int i = 0; i < filters.Count; i++)
                Require(filters[i].GetInstanceID() == bookMeshIds[i], "Original book mesh identity is retained: " + filters[i].name);
        }
    }

    static void ValidateInteractionMapping()
    {
        XRBaseInteractable cabinetInteractable = root.GetComponent<XRBaseInteractable>();
        XRInteractionManager interactionManager = cabinetInteractable.interactionManager;
        if (interactionManager == null) interactionManager = UnityEngine.Object.FindObjectOfType<XRInteractionManager>();
        Require(interactionManager != null, "The scene interaction manager remains available");
        var colliders = new List<Collider>();
        colliders.AddRange(root.Find("ReviewCabinetShell").GetComponentsInChildren<Collider>());
        colliders.Add(leftDoor.GetComponent<Collider>()); colliders.Add(rightDoor.GetComponent<Collider>());
        foreach (Collider collider in colliders)
        {
            IXRInteractable mapped;
            Require(interactionManager.TryGetInteractableForCollider(collider, out mapped) && ReferenceEquals(mapped, cabinetInteractable),
                "XRI maps the physical cabinet part to its original annotation interactable: " + collider.name);
        }
        if (cabinetBooks != null)
        {
            XRBaseInteractable booksInteractable = cabinetBooks.GetComponent<XRBaseInteractable>();
            IXRInteractable mapped;
            Require(booksInteractable != null && interactionManager.TryGetInteractableForCollider(cabinetBooks.GetComponent<Collider>(), out mapped)
                && ReferenceEquals(mapped, booksInteractable), "D10's fitted original collider retains its XRI mapping");
        }
    }

    static float InteriorDepth()
    {
        float deepest = 0f;
        for (int i = 2; i <= 7; i++)
        {
            var start = new Vector3(sourceBounds.center.x, Mathf.Lerp(sourceBounds.min.y, sourceBounds.max.y, i / 10f), sourceBounds.min.z - .1f);
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(start, Vector3.forward, sourceBounds.size.z + .3f, ~0, QueryTriggerInteraction.Ignore))
                if (hit.collider.transform.IsChildOf(root)) nearest = Mathf.Min(nearest, hit.distance);
            if (!float.IsPositiveInfinity(nearest)) deepest = Mathf.Max(deepest, nearest);
        }
        return deepest;
    }

    static GameObject MakeBlocker(Vector3 center, Vector3 size)
    {
        var value = GameObject.CreatePrimitive(PrimitiveType.Cube);
        value.name = "CabinetVerificationObstacle"; value.transform.position = center; value.transform.localScale = size;
        Physics.SyncTransforms(); return value;
    }

    static void RemoveBlocker()
    {
        if (blocker != null) { blocker.SetActive(false); UnityEngine.Object.Destroy(blocker); blocker = null; }
        Physics.SyncTransforms();
    }

    static void SnapshotOtherFurniture()
    {
        foreach (var info in UnityEngine.Object.FindObjectsOfType<FurnitureInfo>())
        {
            if (info.transform == root) continue;
            furniturePositions[info.name] = info.transform.position;
            furnitureRotations[info.name] = info.transform.rotation;
        }
    }

    static void ValidateOtherFurniture()
    {
        foreach (var pair in furniturePositions)
        {
            GameObject value = GameObject.Find(pair.Key);
            Require(value != null && Vector3.Distance(value.transform.position, pair.Value) < .0001f &&
                Quaternion.Angle(value.transform.rotation, furnitureRotations[pair.Key]) < .001f,
                "Unrelated furniture retains its authored pose: " + pair.Key);
        }
    }

    static bool BoundsEqual(Bounds first, Bounds second) =>
        Vector3.Distance(first.center, second.center) < .005f && Vector3.Distance(first.size, second.size) < .005f;
    static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    static void Set(object value, string name, object data) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, data);
    static object Call(object value, string name, params object[] data) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(value, data);
    static void Wait(double seconds, int next) { waitUntil = EditorApplication.timeSinceStartup + seconds; phase = next; }
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAILED: " + message);
        checks.Add(message); Debug.Log("CABINET_CHECK: " + message);
    }

    static void Capture(string name)
    {
        var cameraObject = new GameObject("CabinetVerificationCapture");
        var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(head);
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.transform.SetPositionAndRotation(head.transform.position, head.transform.rotation);
        var target = new RenderTexture(1280, 800, 24); camera.targetTexture = target; camera.Render();
        RenderTexture old = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1280, 800, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); texture.Apply();
        File.WriteAllBytes(Path.Combine(output, name), texture.EncodeToPNG()); RenderTexture.active = old;
        camera.targetTexture = null; UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(cameraObject);
    }

    static void Finish(bool passed)
    {
        if (finishing) return; finishing = true; requestedPass = passed;
        RemoveBlocker(); EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            OnPlayModeChanged(PlayModeStateChange.EnteredEditMode);
            return;
        }
        EditorApplication.ExitPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        bool passed = requestedPass && errors.Count == 0;
        if (passed) checks.Add("Cabinet teardown completes without logged errors");
        File.WriteAllText(Path.Combine(output, "cabinet-validation-report.json"), JsonUtility.ToJson(new Report
        {
            passed = passed,
            limitation = "Isolated Unity Play Mode with actual door colliders, synthetic head/controller state and UI callbacks. Does not replace headset ray-hit, comfort or physical reach testing.",
            checks = checks.ToArray(), errors = errors.ToArray()
        }, true));
        Application.logMessageReceived -= Log;
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
#endif
