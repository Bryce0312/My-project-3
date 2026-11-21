using System;
using System.Collections.Generic;
using System.IO;
using LivingRoom30.Measurement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30.Review
{
    /// <summary>
    /// Manages the authored review sequence, session records and runtime review UI.
    /// Runtime review objects are parented under this component's scene root.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class ReviewJourneyController : MonoBehaviour
    {
        private enum Stage { Intro, Entry, Display, Bookcase, TvSide, Coffee, Sit, SeatedLook, Remote, Stand, Complete }

        [Serializable] public sealed class Observation
        {
            public string task, targetIds, question, answer, utc;
            public Vector3 observerPosition;
            public Vector3[] targetPositions;
            public bool cabinetDoorsOpen;
        }
        [Serializable] public sealed class ReviewAction
        {
            public string action, utc;
        }
        [Serializable] public sealed class SessionRecord
        {
            public int schemaVersion = 2;
            public string startedUtc, endedUtc, status;
            public string modelAssumptions = "Representative living room; simulated seated eye at 1.21 m. Not a regulatory or bodily-clearance certification.";
            public List<Observation> observations = new List<Observation>();
            public List<ReviewAction> actions = new List<ReviewAction>();
            public List<ReviewAnnotationRecord> annotations = new List<ReviewAnnotationRecord>();
            public bool routeCompleted;
        }

        private SpatialMeasureManager manager;
        private TutorialController controls;
        private SeatViewController seat;
        private ReviewRemoteControl remote;
        private ReviewCabinetDoors cabinet;
        private Canvas cabinetCanvas;
        private Text cabinetHintTitle;
        private Text cabinetStatus;
        private bool cabinetHintDismissed;
        private bool cabinetTaskStarted, cabinetOpenedForTask, cabinetObservationComplete, cabinetClosedAfterReview;
        private ReviewHudEntry hudEntry;
        private ReviewAnnotationStore annotationStore;
        private ReviewSummaryPanel summary;
        private bool summaryMode, ending, routeStarted, finishAfterSeatTransition;
        private bool guidanceBeforeSummary;
        private bool proximityBeforeSummary;
        private bool ownsSummaryProximity;
        private readonly List<FurnitureProximityDisplay> summaryDimensionDisplays = new List<FurnitureProximityDisplay>();
        private Stage stage;
        private bool guided = true, arrived, remoteGrabSeen, remoteReturned, ready;
        private float arrivalSeconds, routeTimer, lookSeconds;
        private int lookedCount;
        private readonly ReviewRouteGraph graph = new ReviewRouteGraph();
        private readonly List<Vector3> route = new List<Vector3>();
        private readonly Collider[] overlaps = new Collider[96];
        private readonly RaycastHit[] castHits = new RaycastHit[96];
        private readonly List<GameObject> arrows = new List<GameObject>();
        private Vector3[] lookTargets = Array.Empty<Vector3>();
        private string[] targetNames = Array.Empty<string>();
        private Material lineMaterial;
        private GameObject floorVisuals, circle;
        private LineRenderer standRing;
        private GameObject targetBeacon, targetGuideObject;
        private LineRenderer[] beaconLines;
        private LineRenderer targetGuideLine;
        private GameObject guideArrowHead;
        private LineRenderer[] guideArrowLines;
        private ReviewIconGraphic bearingArrow;
        private Canvas panel, targetCanvas, seatCanvas, utilityCanvas;
        private Text title, instruction, progress, utilityMessage;
        private Image panelBack;
        private ReviewIconGraphic mainIcon;
        private ReviewIconGraphic targetIcon;
        private Button startButton, freeButton, controlsButton, replayButton;
        private readonly Button[] answers = new Button[3];
        private Button sitButton, standButton, resetButton;
        private Button pauseButton;
        private SessionRecord record;
        private string recordPath, saveMessage = string.Empty, transientMessage = string.Empty;
        private float transientUntil;
        private float targetAlignment;
        private float targetConfirmationUntil, arrivedFlashUntil;
        private ReviewIconGraphic.Kind targetConfirmationSymbol = ReviewIconGraphic.Kind.Eye;
        private string observeInstructionBase = string.Empty;
        private bool panelPositioned, panelFollowingRightWrist;
        private Transform seatEye, exitFloor, remoteHome;
        private Canvas[] canvases;
        private ActionBasedController leftController, rightController;
        private string currentHint;
        private bool ownsControlHint;
        private static readonly Vector3 Tv = new Vector3(0f, .995f, 2.414f);
        private const float TargetConfirmSeconds = .55f;
        private const float ArrivedFlashSeconds = .6f;

        public string CurrentStage => summaryMode ? "Complete" : stage.ToString();
        public bool IsReady => ready;
        public SeatViewController Seat => seat;
        public ReviewRemoteControl Remote => remote;
        public ReviewCabinetDoors Cabinet => cabinet;
        public ReviewSummaryPanel Summary => summary;
        public SessionRecord Session => record;
        public bool IsSummaryMode => summaryMode;
        public bool CabinetTaskStarted => stage == Stage.Display && cabinetTaskStarted;
        public string LastSavedPath => recordPath;
        public string SessionDirectoryOverride { get; set; }

        private void Start()
        {
            manager = FindObjectOfType<SpatialMeasureManager>();
            controls = FindObjectOfType<TutorialController>(true);
            if (manager == null || GameObject.Find("F01_Sofa_ThreeSeat") == null
                || GameObject.Find("F04_CoffeeTable") == null)
            {
                Debug.LogWarning("ReviewJourney needs the authored living room and measurement/XR manager.", this);
                enabled = false;
                return;
            }

            // The original controls tutorial remains available through HELP/CONTROLS. The
            // journey supplies the default first-run orientation without two competing panels.
            controls?.Dismiss();
            lineMaterial = new Material(Shader.Find("Sprites/Default")) { name = "ReviewGuide_Ink" };
            BuildFloorVisuals();
            BuildPanel();
            BuildTargetCues();
            BuildSeatAndRemote();
            BuildCabinet();
            canvases = new[] { panel, targetCanvas, seatCanvas, utilityCanvas, cabinetCanvas };
            ready = true;
            annotationStore = gameObject.AddComponent<ReviewAnnotationStore>();
            annotationStore.Submit = SaveAnnotation;
            annotationStore.ContainsTarget = id => record != null && record.annotations.Exists(note => note.targetId == id);
            annotationStore.CanOpen = () => ready && isActiveAndEnabled && !summaryMode && !ending;
            summary = gameObject.AddComponent<ReviewSummaryPanel>();
            summary.Configure(ResumeReview, ResumeReview, Replay, TryStand);
            hudEntry = gameObject.AddComponent<ReviewHudEntry>();
            hudEntry.Configure(controls != null ? controls.HelpButton : null, OpenGuidance, RequestFinish);
            SetStage(Stage.Intro);
        }

        private Transform Anchor(string name, Vector3 position)
        {
            var anchor = new GameObject(name).transform;
            anchor.SetParent(transform, false);
            anchor.position = position;
            return anchor;
        }

        private void BuildCabinet()
        {
            GameObject target = GameObject.Find("F09_DisplayCabinet");
            if (target != null)
            {
                cabinet = target.GetComponent<ReviewCabinetDoors>() ?? target.AddComponent<ReviewCabinetDoors>();
                cabinet.Configure(target.transform);
                cabinet.ConfigureHands(leftController, rightController);
                cabinet.CanInteract = CanUseCabinet;
                cabinet.StateChanged += OnCabinetStateChanged;
                cabinet.ActionBlocked += OnCabinetBlocked;
                cabinet.HandPullStarted += OnCabinetHandPullStarted;
                foreach (var display in FindObjectsOfType<FurnitureProximityDisplay>()) display.RefreshFurniture();
            }
            cabinetCanvas = ReviewVisuals.Canvas(transform, "CabinetHandHint", new Vector2(460, 272), .0015f);
            ReviewVisuals.Image(cabinetCanvas.transform, "Back", Vector2.zero, new Vector2(460, 272), ReviewVisuals.Back);
            cabinetHintTitle = ReviewVisuals.Text(cabinetCanvas.transform, "HandHint", new Vector2(-18, 109),
                new Vector2(386, 32), "CABINET TASK - QUICK STEPS", 20, TextAnchor.MiddleCenter);
            ReviewVisuals.Button(cabinetCanvas.transform, "CloseHint", new Vector2(203, 109),
                new Vector2(34, 34), "x", DismissCabinetHint);
            ReviewVisuals.Text(cabinetCanvas.transform, "Steps", new Vector2(0, 10),
                new Vector2(420, 164), "1  Stand in the circle once to start.\n"
                    + "2  Hold TRIGGER at each handle; open both doors.\n"
                    + "3  Release TRIGGER; look at the eye marker.\n"
                    + "4  Problem? Use GRIP to add an optional note.\n"
                    + "5  Push both doors closed.\n"
                    + "6  Continue to the next task.", 18, TextAnchor.MiddleLeft);
            cabinetStatus = ReviewVisuals.Text(cabinetCanvas.transform, "Status", new Vector2(0, -104),
                new Vector2(420, 46), "No issue? Skip the note and close both doors.", 17, TextAnchor.MiddleCenter);
            cabinetCanvas.gameObject.SetActive(false);
        }

        private void DismissCabinetHint()
        {
            // This is only a reminder; closing it must not pause or change the task.
            cabinetHintDismissed = true;
            cabinetCanvas.gameObject.SetActive(false);
        }

        private bool CanUseCabinet()
        {
            return ready && isActiveAndEnabled && cabinet != null && cabinet.IsReady && !summaryMode && !ending
                && (!guided || stage != Stage.Display || cabinetTaskStarted)
                && manager != null && manager.ActiveCamera != null && seat != null && !seat.IsSeated
                && !seat.IsTransitioning && !remote.IsHeld && AnnotationSpawner.currentActiveUI == null
                && (controls == null || !controls.gameObject.activeInHierarchy)
                && FlatDistance(manager.ActiveCamera.transform.position, cabinet.transform.position) < 1.8f;
        }

        private void OnCabinetHandPullStarted()
        {
            if (guided && stage == Stage.Display) RefreshCard();
        }

        private void OnCabinetStateChanged(bool open)
        {
            if (summaryMode || stage != Stage.Display || !cabinetTaskStarted) return;
            if (open && !cabinetOpenedForTask && !cabinetObservationComplete)
            {
                cabinetOpenedForTask = true;
                RecordAction("display_cabinet_opened");
            }
            else if (!open && cabinetObservationComplete)
            {
                cabinetClosedAfterReview = true;
            }
            RefreshCard();
        }

        private bool CabinetReadyForObservation => cabinet != null && cabinet.IsOpen
            && cabinetTaskStarted && cabinetOpenedForTask && !cabinetObservationComplete;

        private void OnCabinetBlocked(string message)
        {
            if (guided && stage == Stage.Display) RefreshCard();
        }

        private void UpdateCabinet(bool controlsOpen, bool noteOpen)
        {
            if (cabinet == null || !cabinet.IsReady) { cabinetCanvas.gameObject.SetActive(false); return; }
            cabinet.SetXrAvailable(manager.IsXrMode);
            cabinet.SetInteractionAllowed(CanUseCabinet());
            bool near = FlatDistance(manager.ActiveCamera.transform.position, cabinet.transform.position) < 1.8f;
            cabinetCanvas.gameObject.SetActive(near && guided && stage == Stage.Display && !cabinetHintDismissed && !summaryMode && !seat.IsSeated && !seat.IsTransitioning
                && !controlsOpen && !noteOpen);
            cabinetStatus.text = guided && stage == Stage.Display && !cabinetTaskStarted
                ? "Confirm the circle once, then leave it to reach the handles."
                : cabinet.IsHandPulling ? "Pull / push with your hand. Release TRIGGER to let go."
                : guided && stage == Stage.Display && cabinet.IsOpen && !cabinetObservationComplete
                    ? "Now look at the eye marker. Feedback is optional."
                : guided && stage == Stage.Display && cabinetObservationComplete
                    ? "No issue? Skip the note and close both doors to continue."
                : "TRIGGER pulls doors. GRIP adds an optional note.";
            if (!string.IsNullOrEmpty(cabinet.LastMessage)) cabinetStatus.text = cabinet.LastMessage;
        }

        private void BuildSeatAndRemote()
        {
            seatEye = Anchor("SeatEye_Simulated_1.21m", new Vector3(0f, 1.21f, -.55f));
            exitFloor = Anchor("SeatExit_ClearFloor", graph.Nodes[9]);
            exitFloor.rotation = Quaternion.LookRotation(Vector3.forward);
            Transform tvTarget = Anchor("TV_ReviewTarget", Tv);
            seat = gameObject.AddComponent<SeatViewController>();
            seat.Configure(manager, seatEye, exitFloor, tvTarget);
            seat.SeatedChanged += OnSeatedChanged;

            remoteHome = Anchor("Remote_Home_TableFront", new Vector3(-.20f, .445f, .14f));
            var remoteObject = new GameObject("ReviewRemoteControl");
            remoteObject.transform.SetParent(transform, false);
            remote = remoteObject.AddComponent<ReviewRemoteControl>();
            ActionBasedController left = null, right = null;
            foreach (var controller in FindObjectsOfType<ActionBasedController>(true))
            {
                string controllerName = controller.gameObject.name.ToLowerInvariant();
                if (controllerName.Contains("left")) left = controller;
                if (controllerName.Contains("right")) right = controller;
            }
            remote.Configure(remoteHome, left, right, FindObjectOfType<XRInteractionManager>());
            leftController = left; rightController = right;
            remote.Grabbed += OnRemoteGrabbed;
            remote.Returned += OnRemoteReturned;
            seat.CanTransition = () => remote == null || !remote.IsHeld;

            seatCanvas = ReviewVisuals.Canvas(transform, "SeatAction", new Vector2(210, 125), .0018f);
            ReviewVisuals.Image(seatCanvas.transform, "Back", Vector2.zero, new Vector2(210, 125), ReviewVisuals.Back);
            ReviewVisuals.Icon(seatCanvas.transform, new Vector2(0, 25), 47, ReviewIconGraphic.Kind.Chair);
            sitButton = ReviewVisuals.Button(seatCanvas.transform, "Sit", new Vector2(0, -33),
                new Vector2(190, 43), "SIT & OBSERVE", TrySit);
            seatCanvas.transform.position = new Vector3(-.85f, .91f, -.32f);

            utilityCanvas = ReviewVisuals.Canvas(transform, "SeatRecoveryControls", new Vector2(430, 90), .0014f);
            ReviewVisuals.Image(utilityCanvas.transform, "Back", Vector2.zero, new Vector2(430, 90), ReviewVisuals.Back);
            standButton = ReviewVisuals.Button(utilityCanvas.transform, "Stand", new Vector2(-105, 10),
                new Vector2(195, 40), "STAND UP", TryStand);
            resetButton = ReviewVisuals.Button(utilityCanvas.transform, "ResetRemote", new Vector2(105, 10),
                new Vector2(195, 40), "RESET REMOTE", ResetRemote);
            utilityMessage = ReviewVisuals.Text(utilityCanvas.transform, "Message", new Vector2(0, -28),
                new Vector2(410, 25), "", 16, TextAnchor.MiddleCenter);
        }

        private void BuildPanel()
        {
            panel = ReviewVisuals.Canvas(transform, "ReviewTaskCard", new Vector2(610, 205), .00115f);
            panelBack = ReviewVisuals.Image(panel.transform, "Back", Vector2.zero, new Vector2(610, 205), ReviewVisuals.Back);
            mainIcon = ReviewVisuals.Icon(panel.transform, new Vector2(-260, 45), 58, ReviewIconGraphic.Kind.Walk);
            title = ReviewVisuals.Text(panel.transform, "Title", new Vector2(25, 70), new Vector2(505, 31), "", 25);
            instruction = ReviewVisuals.Text(panel.transform, "Action", new Vector2(25, 28), new Vector2(505, 56), "", 20);
            progress = ReviewVisuals.Text(panel.transform, "Progress", new Vector2(25, -14), new Vector2(505, 26), "", 16);
            // Live bearing arrow: rotates toward the current look target so the card itself
            // answers "which way do I turn?" when the target sits in open space.
            bearingArrow = ReviewVisuals.Icon(panel.transform, new Vector2(-260, -6), 36, ReviewIconGraphic.Kind.Arrow);
            bearingArrow.gameObject.SetActive(false);
            startButton = ReviewVisuals.Button(panel.transform, "Start", new Vector2(-190, -65), new Vector2(178, 42), "START REVIEW", BeginReview);
            freeButton = ReviewVisuals.Button(panel.transform, "Explore", new Vector2(0, -65), new Vector2(178, 42), "FREE EXPLORE", PauseGuidance);
            controlsButton = ReviewVisuals.Button(panel.transform, "Controls", new Vector2(190, -65), new Vector2(178, 42), "CONTROLS", ShowControls);
            pauseButton = ReviewVisuals.Button(panel.transform, "PauseRoute", new Vector2(277, 76), new Vector2(40, 40), "x", PauseGuidance);
            replayButton = ReviewVisuals.Button(panel.transform, "Replay", new Vector2(-190, -65), new Vector2(178, 42), "REPLAY REVIEW", Replay);
            string[] labels = { "WORKS WELL", "CONCERN", "UNSURE" };
            for (int i = 0; i < 3; i++)
            {
                int answer = i;
                answers[i] = ReviewVisuals.Button(panel.transform, "Answer" + i,
                    new Vector2(-190 + i * 190, -65), new Vector2(178, 42), labels[i], () => Answer(labels[answer]));
            }
            targetCanvas = ReviewVisuals.Canvas(transform, "CurrentLookTarget", new Vector2(85, 85), .0013f);
            // Keep the marker light: a smaller translucent plate reads as a reticle, not a big label.
            ReviewVisuals.Image(targetCanvas.transform, "Back", Vector2.zero, new Vector2(68, 68),
                new Color(0.07f, 0.08f, 0.09f, .55f));
            targetIcon = ReviewVisuals.Icon(targetCanvas.transform, Vector2.zero, 65, ReviewIconGraphic.Kind.Eye);
            // A target beacon is visual only and cannot intercept controller UI or physics picks.
            foreach (var raycaster in targetCanvas.GetComponents<UnityEngine.EventSystems.BaseRaycaster>())
                raycaster.enabled = false;
        }

        /// <summary>
        /// Builds a camera-facing halo at the look target and a thin gaze guide line.
        /// Both are visual-only LineRenderers: no colliders, no raycast targets, no shadow casting.
        /// </summary>
        private void BuildTargetCues()
        {
            targetBeacon = new GameObject("LookTarget_Halo");
            targetBeacon.transform.SetParent(transform, false);

            const int segments = 40;
            var ring = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                ring[i] = new Vector3(Mathf.Cos(angle) * .10f, Mathf.Sin(angle) * .10f, 0f);
            }
            var lines = new List<LineRenderer>(5);
            lines.Add(ReviewVisuals.Line(targetBeacon.transform, "Halo", lineMaterial, ring, .012f, ReviewVisuals.Ink, true));
            for (int tick = 0; tick < 4; tick++)
            {
                float angle = tick * Mathf.PI * .5f;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                lines.Add(ReviewVisuals.Line(targetBeacon.transform, "Tick" + tick, lineMaterial,
                    new[] { direction * .12f, direction * .16f }, .012f, ReviewVisuals.Ink));
            }
            beaconLines = lines.ToArray();
            targetBeacon.SetActive(false);

            targetGuideObject = new GameObject("LookTarget_Guide");
            targetGuideObject.transform.SetParent(transform, false);
            targetGuideLine = targetGuideObject.AddComponent<LineRenderer>();
            targetGuideLine.sharedMaterial = lineMaterial;
            targetGuideLine.useWorldSpace = true;
            targetGuideLine.positionCount = 2;
            targetGuideLine.startWidth = .014f;
            targetGuideLine.endWidth = .022f;
            targetGuideLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            targetGuideLine.receiveShadows = false;

            // A V-shaped arrow head at the target end turns the guide line into an explicit
            // "look here" pointer instead of a thin line that reads as decoration.
            guideArrowHead = new GameObject("GuideArrowHead");
            guideArrowHead.transform.SetParent(targetGuideObject.transform, false);
            guideArrowLines = new[]
            {
                ReviewVisuals.Line(guideArrowHead.transform, "Left", lineMaterial,
                    new[] { Vector3.zero, new Vector3(-.05f, 0f, -.09f) }, .018f, ReviewVisuals.Ink),
                ReviewVisuals.Line(guideArrowHead.transform, "Right", lineMaterial,
                    new[] { Vector3.zero, new Vector3(.05f, 0f, -.09f) }, .018f, ReviewVisuals.Ink),
            };
            targetGuideObject.SetActive(false);
        }

        private void BuildFloorVisuals()
        {
            floorVisuals = new GameObject("ReviewFloorVisuals_NoColliders");
            floorVisuals.transform.SetParent(transform, false);
            circle = new GameObject("StandHere_Footprints");
            circle.transform.SetParent(floorVisuals.transform, false);
            var points = new Vector3[64];
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / points.Length;
                points[i] = new Vector3(Mathf.Cos(angle) * .27f, 0, Mathf.Sin(angle) * .27f);
            }
            ReviewVisuals.Line(circle.transform, "RingShadow", lineMaterial, points, .045f, Color.black, true);
            standRing = ReviewVisuals.Line(circle.transform, "Ring", lineMaterial, points, .022f, ReviewVisuals.Ink, true);
            standRing.transform.localPosition = Vector3.up * .003f;
            for (int foot = -1; foot <= 1; foot += 2)
                ReviewVisuals.Line(circle.transform, "Footprint", lineMaterial,
                    new[] { new Vector3(foot * .07f, .005f, -.08f), new Vector3(foot * .07f, .005f, .08f) }, .045f, ReviewVisuals.Ink);
            for (int i = 0; i < 70; i++)
            {
                var arrow = new GameObject("FloorArrow");
                arrow.transform.SetParent(floorVisuals.transform, false);
                Vector3[] shape = { new Vector3(-.075f, 0, -.035f), new Vector3(0, 0, .055f), new Vector3(.075f, 0, -.035f) };
                ReviewVisuals.Line(arrow.transform, "Shadow", lineMaterial, shape, .045f, Color.black);
                var ink = ReviewVisuals.Line(arrow.transform, "Ink", lineMaterial, shape, .022f, ReviewVisuals.Ink);
                ink.transform.localPosition = Vector3.up * .003f;
                arrow.SetActive(false);
                arrows.Add(arrow);
            }
        }

        public void BeginReview()
        {
            if (!ready) return;
            if (summaryMode) { ShowResults(); return; }
            if (seat.IsSeated || seat.IsTransitioning) { Message("Stand up before starting a new review."); return; }
            if (remote.IsHeld) { Message("Put down the remote first."); return; }
            if (cabinet != null && !cabinet.IsClosed)
            { Message("Close the cabinet doors before starting a new review."); return; }
            EnsureSession(); // Keep annotations submitted during initial free exploration.
            saveMessage = string.Empty;
            remote.ResetToHome();
            remoteGrabSeen = remoteReturned = false;
            guided = true;
            routeStarted = true;
            SetStage(Stage.Entry);
            SaveRecord();
        }

        private void Replay()
        {
            if (seat.IsSeated || seat.IsTransitioning || remote.IsHeld)
            { Message("Stand up and put down the remote before starting a new review."); return; }
            if (AnnotationSpawner.currentActiveUI != null)
            { Message("Finish or cancel the open annotation first."); return; }
            if (cabinet != null && !cabinet.IsClosed)
            { Message("Close the cabinet doors before starting a new review."); return; }
            if (record != null && !SaveRecord()) return;
            CloseSummary();
            summary.ResetView();
            summaryMode = false;
            record = null; recordPath = null; routeStarted = false;
            BeginReview();
        }
        private void PauseGuidance()
        {
            guided = false;
            arrivalSeconds = lookSeconds = 0f;
            arrived = false;
            targetAlignment = 0f;
            targetConfirmationUntil = arrivedFlashUntil = 0f;
            transientUntil = 0f;
            panel.gameObject.SetActive(false);
            HideRoute();
            ReleaseControlHint();
        }

        /// <summary>Reopens the review from the HUD without restarting its session or observations.</summary>
        public void OpenGuidance()
        {
            if (!ready || !isActiveAndEnabled) return;
            if (summaryMode) { ShowResults(); return; }
            bool controlsOpen = controls != null && controls.gameObject.activeInHierarchy;
            if (guided && panel.gameObject.activeSelf && !controlsOpen) return;
            ReleaseControlHint();
            controls?.Dismiss();
            guided = true;
            arrivalSeconds = lookSeconds = 0f;
            routeTimer = 0f;
            panelPositioned = false;
            // Free exploration can change the physical seat/object state while tasks are paused.
            if (record != null && record.status == "in_progress")
            {
                if (stage == Stage.Sit && seat.IsSeated) SetStage(Stage.SeatedLook);
                else if (stage >= Stage.SeatedLook && stage <= Stage.Remote && !seat.IsSeated)
                {
                    remoteGrabSeen = remoteReturned = false;
                    SetStage(Stage.Sit);
                }
                else if (stage == Stage.Stand && !seat.IsSeated) CompleteReview();
                else if (stage == Stage.Remote)
                {
                    remoteGrabSeen = remote.IsHeld;
                    remoteReturned = false;
                }
            }
            // Restore the live guidance state on the same frame, without waiting for Update.
            // Re-entry must show the real progress (lookedCount is preserved).
            // Drop any in-flight confirmation/flash so switching back never replays an old Check icon.
            targetConfirmationUntil = 0f;
            arrivedFlashUntil = 0f;
            targetAlignment = 0f;
            ReevaluateArrival();
            RefreshCard(); // Refresh with the corrected arrived flag and preserved lookedCount.
            RedrawGuidance();
            panel.gameObject.SetActive(AnnotationSpawner.currentActiveUI == null);
        }
        private void ShowControls()
        {
            if (summaryMode)
            {
                ResumeReview();
                if (summaryMode) return;
            }
            // HELP overrides the guide card; discard pending confirmations before it is hidden.
            targetConfirmationUntil = 0f;
            arrivedFlashUntil = 0f;
            targetAlignment = 0f;
            CloseSummary(); ReleaseControlHint(); controls?.Show();
        }

        /// <summary>Re-checks the footprint circle using the current observer, instantly on re-entry.</summary>
        private void ReevaluateArrival()
        {
            if (manager == null || manager.ActiveCamera == null) return;
            // Cabinet arrival confirms the task once; approaching a handle must not revoke it.
            if (stage == Stage.Display)
            {
                arrived = cabinetTaskStarted;
                // Before confirmation, require the real dwell/OnArrived edge after resuming.
                arrivalSeconds = cabinetTaskStarted ? .45f : 0f;
                lookSeconds = 0f;
                return;
            }
            int destination = Destination();
            if (destination < 0) { arrived = false; arrivalSeconds = 0f; return; }
            bool inside = FlatDistance(manager.ActiveCamera.transform.position, graph.Nodes[destination]) < .29f;
            arrived = inside;
            arrivalSeconds = inside ? .45f : 0f;
            lookSeconds = 0f; // A switched-away gaze must not carry over into the resumed target.
        }

        /// <summary>Immediately redraws route and target cues so re-entry never shows a stale card.</summary>
        private void RedrawGuidance()
        {
            if (!ready || manager == null || manager.ActiveCamera == null) return;
            bool temporaryOverlay = (controls != null && controls.gameObject.activeInHierarchy) ||
                AnnotationSpawner.currentActiveUI != null || summaryMode || seat.IsTransitioning;
            if (temporaryOverlay) { HideRoute(); return; }
            Camera observer = manager.ActiveCamera;
            int destination = Destination();
            if (destination >= 0) DrawRoute(observer.transform.position, destination);
            else HideFloor();
            routeTimer = .8f;
            UpdateLookTarget(observer);
        }

        private void SetStage(Stage next)
        {
            stage = next;
            arrived = false; arrivalSeconds = lookSeconds = 0f; lookedCount = 0; routeTimer = 0f;
            targetAlignment = 0f;
            targetConfirmationUntil = arrivedFlashUntil = 0f;
            lookTargets = Array.Empty<Vector3>(); targetNames = Array.Empty<string>();
            if (next == Stage.Remote)
            {
                remoteGrabSeen = remote.IsHeld;
                remoteReturned = false;
            }
            if (next == Stage.Display)
                cabinetTaskStarted = cabinetOpenedForTask = cabinetObservationComplete = cabinetClosedAfterReview = false;
            switch (next)
            {
                case Stage.Entry: Targets(new[] { new Vector3(0, .7f, -.78f), Tv }, new[] { "sofa", "TV wall" }); break;
                case Stage.Display: Targets(new[] { new Vector3(-2.85f, .90f, 2.53f) }, new[] { "inside the open cabinet" }); break;
                case Stage.Bookcase: Targets(new[] { new Vector3(1.85f, 1.05f, 2.44f), new Vector3(1.98f, .65f, .28f) }, new[] { "bookcase", "chair beside the route" }); break;
                case Stage.TvSide: Targets(new[] { Tv }, new[] { "TV from this side angle" }); break;
                case Stage.Coffee: Targets(new[] { new Vector3(0, .42f, .38f), new Vector3(0, .51f, -.37f) }, new[] { "table surface", "sofa front" }); break;
                case Stage.SeatedLook: Targets(new[] { Tv, new Vector3(0, .42f, .38f) }, new[] { "TV at seated eye height", "table from the seat" }); break;
            }
            RefreshCard();
            HideRoute();
        }

        private void Targets(Vector3[] positions, string[] names) { lookTargets = positions; targetNames = names; }

        private int Destination()
        {
            switch (stage)
            {
                case Stage.Entry: return 1;
                case Stage.Display: return cabinetTaskStarted ? -1 : 6;
                case Stage.Bookcase: return 7;
                case Stage.TvSide: return 4;
                case Stage.Coffee: return 5;
                case Stage.Sit: return 9;
                default: return -1;
            }
        }

        private void Update()
        {
            if (!ready || manager == null) return;
            if (transientUntil > 0f && Time.unscaledTime >= transientUntil)
            {
                transientUntil = 0f;
                RefreshCard();
            }
            remote.SetXrAvailable(manager.IsXrMode && !summaryMode);
            bool controlsOpen = controls != null && controls.gameObject.activeInHierarchy;
            bool noteOpen = AnnotationSpawner.currentActiveUI != null;
            if (controlsOpen && summary.IsVisible)
            {
                ResumeReview();
                if (!summaryMode) controls?.Show();
                controlsOpen = controls != null && controls.gameObject.activeInHierarchy;
            }
            hudEntry?.SetResultsAvailable(summaryMode);
            summary.SetSeated(seat.IsSeated, seat.IsTransitioning);
            if (finishAfterSeatTransition && !seat.IsTransitioning)
            { finishAfterSeatTransition = false; RequestFinish(); }
            UpdateControlHint(controlsOpen, noteOpen);
            panel.gameObject.SetActive(!summaryMode && (guided || transientUntil > 0f) && !controlsOpen && !noteOpen);
            Camera observer = manager.ActiveCamera;
            if (observer == null) return;
            remote.Viewer = observer.transform;
            UpdateCabinet(controlsOpen, noteOpen);

            float seatDistance = FlatDistance(observer.transform.position, exitFloor.position);
            seatCanvas.gameObject.SetActive(!summaryMode && !seat.IsSeated && !seat.IsTransitioning && seatDistance < 1.05f && !controlsOpen && !noteOpen);
            sitButton.interactable = manager.IsXrMode && !remote.IsHeld;
            utilityCanvas.gameObject.SetActive(!summary.IsVisible && (seat.IsSeated || seat.IsTransitioning || (!remote.IsAtHome && manager.IsXrMode)));
            standButton.gameObject.SetActive(seat.IsSeated || seat.IsTransitioning);
            standButton.interactable = seat.IsSeated && !seat.IsTransitioning && !remote.IsHeld;
            resetButton.interactable = !seat.IsTransitioning;
            utilityMessage.text = remote.IsHeld ? "Put down the remote before standing." : seat.IsTransitioning ? seat.LastMessage : "";

            bool canGuide = guided && !summaryMode && !controlsOpen && !noteOpen && !seat.IsTransitioning;
            if (!canGuide) { lookSeconds = 0f; arrivalSeconds = 0f; HideRoute(); return; }
            if (stage == Stage.Display && cabinetObservationComplete && cabinetClosedAfterReview
                && cabinet != null && cabinet.IsClosed)
            { RecordAction("display_cabinet_closed_after_review"); SetStage(Stage.Bookcase); return; }
            int destination = Destination();
            if (seat.IsSeated && stage != Stage.SeatedLook && stage != Stage.Remote && stage != Stage.Stand)
            {
                HideRoute(); instruction.text = "Stand up to continue the walking route."; return;
            }
            if (destination >= 0)
            {
                if (FlatDistance(observer.transform.position, graph.Nodes[destination]) < .29f)
                {
                    arrivalSeconds += Time.unscaledDeltaTime;
                    if (!arrived && arrivalSeconds >= .45f) OnArrived();
                }
                else
                {
                    arrivalSeconds = 0f;
                    if (arrived) { arrived = false; RefreshCard(); }
                }
                if (stage == Stage.Display && cabinetTaskStarted) HideFloor();
                else
                {
                    routeTimer -= Time.unscaledDeltaTime;
                    if (routeTimer <= 0f) { DrawRoute(observer.transform.position, destination); routeTimer = .8f; }
                }
            }
            else HideFloor();

            UpdateArrivalRingFlash();
            UpdateLookTarget(observer);
            if (stage == Stage.Remote && seat.IsSeated)
            {
                // The remote supplies its own hand/progress hint once the hand is in range.
                targetCanvas.gameObject.SetActive(!remote.ReadyToGrab && !remote.IsHeld);
                targetIcon.Symbol = remoteGrabSeen ? ReviewIconGraphic.Kind.Return : ReviewIconGraphic.Kind.Hand;
                targetIcon.color = ReviewVisuals.Ink;
                targetIcon.rectTransform.localScale = Vector3.one;
                targetIcon.SetVerticesDirty();
                float unusedScale;
                targetCanvas.transform.position = SafeVisualPosition(observer.transform.position,
                    remoteHome.position + Vector3.up * .16f, .015f, .10f, out unusedScale);
                HideTargetCues();
            }
            if (stage == Stage.Remote && remoteGrabSeen && remoteReturned)
            {
                RecordAction("remote_grabbed_and_returned");
                SetStage(Stage.Stand);
            }
            if (Time.unscaledTime < transientUntil) progress.text = transientMessage;
        }

        /// <summary>Fires once on the false->true arrival edge: haptic, icon/ring flash and a short caption.</summary>
        private void OnArrived()
        {
            arrived = true;
            if (stage == Stage.Display && !cabinetTaskStarted)
            {
                cabinetTaskStarted = true;
                RecordAction("display_cabinet_start_confirmed");
                // A resumed/free-exploration cabinet may already be open. Do not wait for an
                // open event that has already happened before confirmation.
                if (cabinet != null && cabinet.IsOpen) OnCabinetStateChanged(true);
                HideFloor();
            }
            arrivedFlashUntil = Time.unscaledTime + ArrivedFlashSeconds;
            Pulse();
            RefreshCard(); // Sets the observe-base instruction with the corrected arrived flag.
            transientMessage = stage == Stage.Display
                ? "Start point confirmed. Leave the circle and reach each handle."
                : lookTargets.Length > 0
                ? "Arrived — now observe the highlighted target."
                : "Arrived — point at the icon to continue.";
            transientUntil = Time.unscaledTime + 2.2f;
            ApplyCardLayout(false); // Expand so the transient caption in progress is legible.
            progress.text = transientMessage;
        }

        /// <summary>Keeps the stand-here ring flashing briefly after arrival (XR and desktop).</summary>
        private void UpdateArrivalRingFlash()
        {
            if (standRing == null) return;
            float left = arrivedFlashUntil - Time.unscaledTime;
            float flash = left > 0f ? Mathf.Clamp01(left / ArrivedFlashSeconds) : 0f;
            float width = .022f + .03f * flash * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 24f));
            if (!Mathf.Approximately(standRing.startWidth, width)) standRing.startWidth = standRing.endWidth = width;
        }

        /// <summary>
        /// Draws the current look target: gaze-driven breathing emphasis, halo beacon,
        /// gaze guide line and a temporary Check confirmation after each observation.
        /// All animation uses unscaledTime so it still runs while the timeline is paused.
        /// </summary>
        private void UpdateLookTarget(Camera observer)
        {
            if (stage == Stage.Display && !CabinetReadyForObservation)
            {
                lookSeconds = 0f;
                targetCanvas.gameObject.SetActive(false);
                HideTargetCues();
                if (bearingArrow != null) bearingArrow.gameObject.SetActive(false);
                return;
            }
            bool confirming = Time.unscaledTime < targetConfirmationUntil && lookedCount > 0;
            bool canObserve = (arrived || stage == Stage.SeatedLook) && lookedCount < lookTargets.Length;
            if (!canObserve && !confirming)
            {
                targetCanvas.gameObject.SetActive(false);
                HideTargetCues();
                if (bearingArrow != null) bearingArrow.gameObject.SetActive(false);
                return;
            }

            int index = confirming ? lookedCount - 1 : lookedCount;
            Vector3 target = lookTargets[index];
            float unusedScale;
            Vector3 position = SafeVisualPosition(observer.transform.position,
                target + Vector3.up * .12f, .015f, .10f, out unusedScale);
            targetCanvas.transform.position = position;
            targetCanvas.gameObject.SetActive(true);

            if (confirming)
            {
                // Desktop and XR both see a short Check before the next Eye target appears.
                targetIcon.Symbol = targetConfirmationSymbol;
                float remain = Mathf.Clamp01((targetConfirmationUntil - Time.unscaledTime) / TargetConfirmSeconds);
                float pop = 1f + .38f * remain;
                targetIcon.rectTransform.localScale = Vector3.one * pop;
                Color confirmColor = ReviewVisuals.Ink;
                confirmColor.a = 1f;
                targetIcon.color = confirmColor;
                targetIcon.SetVerticesDirty();
                lookSeconds = 0f;
                UpdateTargetCues(observer, target, 1f);
                if (bearingArrow != null) bearingArrow.gameObject.SetActive(false);
                return;
            }

            targetIcon.Symbol = ReviewIconGraphic.Kind.Eye;
            Vector3 direction = (position - observer.transform.position).normalized;
            float dot = Vector3.Dot(observer.transform.forward, direction);
            float aligned = Mathf.InverseLerp(.70f, .90f, dot);
            targetAlignment = Mathf.Lerp(targetAlignment, aligned, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));

            // Breathing emphasis: strong while searching, converging to a steady highlight on alignment.
            float breath = .5f + .5f * Mathf.Sin(Time.unscaledTime * 6.3f);
            float emphasis = 1f - targetAlignment;
            Color ink = ReviewVisuals.Ink;
            ink.a = Mathf.Lerp(1f, .42f + .58f * breath, emphasis);
            float scale = 1f + .15f * emphasis * breath;
            float arrivedLeft = arrivedFlashUntil - Time.unscaledTime;
            if (arrivedLeft > 0f)
            {
                float flash = Mathf.Clamp01(arrivedLeft / ArrivedFlashSeconds);
                scale += .5f * flash * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 26f));
                ink = Color.Lerp(ink, Color.white, flash * .8f);
                ink.a = 1f;
            }
            targetIcon.color = ink;
            targetIcon.rectTransform.localScale = Vector3.one * scale;
            targetIcon.SetVerticesDirty();

            UpdateTargetCues(observer, target, targetAlignment);
            UpdateBearingArrow(observer, target);

            // Heading word helps when a target floats in open space; only while not aligned.
            string suffix = string.Empty;
            if (Time.unscaledTime >= arrivedFlashUntil && targetAlignment < .80f)
            {
                string word = DirectionWord(observer.transform, target);
                // Keep it short: the card has two text lines, the bearing arrow carries the detail.
                if (word == "BEHIND") suffix = "  Turn around.";
                else if (word == "AHEAD") suffix = "  Look ahead.";
                else suffix = "  Turn " + word + ".";
            }
            if (!string.IsNullOrEmpty(observeInstructionBase))
            {
                string composed = observeInstructionBase + suffix;
                if (instruction.text != composed) instruction.text = composed;
            }

            if (dot > .90f) lookSeconds += Time.unscaledDeltaTime;
            else lookSeconds = 0f;
            if (lookSeconds >= 1.2f)
            {
                lookedCount++;
                lookSeconds = 0f;
                // The "Arrived" caption must not overwrite RefreshCard's evaluation prompt.
                transientUntil = 0f;
                targetConfirmationSymbol = ReviewIconGraphic.Kind.Check;
                targetConfirmationUntil = Time.unscaledTime + TargetConfirmSeconds;
                if (stage == Stage.Display && lookedCount >= lookTargets.Length)
                {
                    // This task records an observed action, not an invented user rating.
                    cabinetObservationComplete = true;
                    targetConfirmationUntil = 0f;
                    RecordAction("display_cabinet_observed");
                    HideRoute();
                }
                RefreshCard();
                Pulse();
            }
        }

        /// <summary>Camera-facing ring and thin gaze line; both fade as the gaze aligns and on arrival.</summary>
        private void UpdateTargetCues(Camera observer, Vector3 target, float alignment)
        {
            Vector3 halo = target + Vector3.up * .12f;
            targetBeacon.transform.position = halo;
            targetBeacon.transform.rotation = observer.transform.rotation; // billboard toward the viewer
            float breath = .5f + .5f * Mathf.Sin(Time.unscaledTime * 6.3f);
            float emphasis = 1f - alignment;
            targetBeacon.transform.localScale = Vector3.one * (1f + .18f * emphasis * breath);
            Color haloColor = ReviewVisuals.Ink;
            haloColor.a = Mathf.Lerp(.32f, .95f, .25f + .75f * emphasis * breath);
            foreach (var line in beaconLines) line.startColor = line.endColor = haloColor;
            targetBeacon.SetActive(true);

            Vector3 start = observer.transform.position + observer.transform.forward * .25f - Vector3.up * .18f;
            targetGuideLine.SetPosition(0, start);
            targetGuideLine.SetPosition(1, halo);
            float fade = Mathf.Lerp(.20f, .70f, emphasis) * (arrived ? .55f : 1f);
            Color lineColor = ReviewVisuals.Ink;
            lineColor.a = fade * (.75f + .25f * breath);
            targetGuideLine.startColor = targetGuideLine.endColor = lineColor;

            Vector3 toward = halo - start;
            if (guideArrowHead != null && toward.sqrMagnitude > .0001f)
            {
                guideArrowHead.transform.position = halo;
                guideArrowHead.transform.rotation = Quaternion.LookRotation(toward.normalized, Vector3.up);
            }
            if (guideArrowLines != null)
                foreach (var line in guideArrowLines) line.startColor = line.endColor = lineColor;

            targetGuideObject.SetActive(true);
        }

        /// <summary>
        /// Rotates the small arrow on the task card toward the current target. The card is
        /// always in front of the reviewer, so this answers "which way do I turn?" even when
        /// the target itself is outside the field of view.
        /// </summary>
        private void UpdateBearingArrow(Camera observer, Vector3 target)
        {
            if (bearingArrow == null) return;
            Vector3 local = observer.transform.InverseTransformDirection(target - observer.transform.position);
            float bearing;
            if (local.z < 0f)
                // Behind: point to the shorter turn instead of flipping unpredictably.
                bearing = local.x >= 0f ? 90f : -90f;
            else
                bearing = Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;

            bearingArrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -bearing);
            Color arrowColor = ReviewVisuals.Ink;
            arrowColor.a = Mathf.Lerp(1f, .55f, targetAlignment);
            bearingArrow.color = arrowColor;
            bearingArrow.SetVerticesDirty();
            bearingArrow.gameObject.SetActive(true);
        }

        private void HideTargetCues()
        {
            if (targetBeacon != null) targetBeacon.SetActive(false);
            if (targetGuideObject != null) targetGuideObject.SetActive(false);
            if (bearingArrow != null) bearingArrow.gameObject.SetActive(false);
        }

        private static string DirectionWord(Transform head, Vector3 target)
        {
            // Judge the horizontal bearing first: a steep head pitch must not turn a
            // target that is behind into UP/DOWN. Elevation is a separate override.
            Vector3 offset = target - head.position;
            Vector3 flat = Vector3.ProjectOnPlane(offset, Vector3.up);
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float ahead = Vector3.Dot(flat, forward);
            float sideways = Vector3.Dot(flat, right);
            string bearing = Mathf.Abs(sideways) > Mathf.Abs(ahead)
                ? (sideways > 0f ? "RIGHT" : "LEFT")
                : (ahead < 0f ? "BEHIND" : "AHEAD");
            // Height only wins when it clearly dominates the horizontal distance.
            float height = offset.y;
            if (Mathf.Abs(height) > flat.magnitude * 1.2f && Mathf.Abs(height) > .15f)
                return height > 0f ? "UP" : "DOWN";
            return bearing;
        }

        private void LateUpdate()
        {
            if (!ready || manager == null || manager.ActiveCamera == null) return;
            Camera camera = manager.ActiveCamera;
            Transform head = camera.transform;
            foreach (var canvas in canvases) canvas.worldCamera = camera;
            if (cabinetCanvas != null && cabinet != null && cabinetCanvas.gameObject.activeSelf)
            {
                float scale;
                cabinetCanvas.transform.position = SafeVisualPosition(head.position,
                    cabinet.transform.TransformPoint(new Vector3(0f, 2.12f, -.28f)), .045f, .08f, out scale);
                Face(cabinetCanvas.transform, head);
                cabinetCanvas.transform.localScale = Vector3.one * (.0015f * scale);
            }
            if (summary.IsVisible)
            {
                float summaryScale;
                Vector3 summaryPosition = SafeVisualPosition(head.position,
                    head.position + head.forward * 1.3f - head.up * .06f, .28f, .3f, out summaryScale);
                summary.SetPose(camera, summaryPosition, summaryScale);
            }
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 desired;
            bool followWrist = TryGetRightWristCardPosition(head, forward, out desired);
            if (!followWrist)
                desired = head.position + forward * .85f - Vector3.up * .32f;
            float panelScale;
            desired = SafeVisualPosition(head.position, desired, .16f, .15f, out panelScale);
            float currentScale;
            SafeVisualPosition(head.position, panel.transform.position, .16f, .15f, out currentScale);
            if (followWrist != panelFollowingRightWrist) panelPositioned = false;
            if (followWrist || !panelPositioned || Vector3.Distance(panel.transform.position, desired) > .15f || panelScale < .999f || currentScale < .999f)
            {
                bool snap = !panelPositioned || panelScale < .999f || currentScale < .999f;
                float follow = 1f - Mathf.Exp(-(followWrist ? 18f : 7f) * Time.unscaledDeltaTime);
                panel.transform.position = snap ? desired : Vector3.Lerp(panel.transform.position, desired, follow);
                panelPositioned = true;
            }
            panelFollowingRightWrist = followWrist;
            panel.transform.localScale = Vector3.one * ((followWrist ? .00085f : .00115f) * panelScale);
            Face(panel.transform, head);
            Face(targetCanvas.transform, head);
            Face(seatCanvas.transform, head);
            float utilityScale;
            utilityCanvas.transform.position = SafeVisualPosition(head.position,
                head.position + forward * 1.05f - right * .43f - Vector3.up * .43f, .12f, .12f, out utilityScale);
            utilityCanvas.transform.localScale = Vector3.one * (.0014f * utilityScale);
            Face(utilityCanvas.transform, head);
        }

        private bool TryGetRightWristCardPosition(Transform head, Vector3 forward, out Vector3 position)
        {
            position = Vector3.zero;
            if (!manager.IsXrMode || rightController == null || !rightController.isActiveAndEnabled)
                return false;
            var state = rightController.currentControllerState;
            if (!state.isTracked || (state.inputTrackingState & UnityEngine.XR.InputTrackingState.Position) == 0)
                return false;
            Vector3 hand = rightController.transform.position;
            // Keep the guide reachable when the right hand is behind the reviewer.
            if (Vector3.Dot(hand - head.position, forward) < -.15f) return false;

            // Use the rig's upright frame: turning the wrist or looking aside must not swing
            // the card around the hand. Keep the canvas under ReviewJourney, not the controller.
            Transform origin = manager.XrOriginTransform;
            Vector3 wristForward = origin != null
                ? Vector3.ProjectOnPlane(origin.forward, Vector3.up).normalized : forward;
            if (wristForward.sqrMagnitude < .1f) wristForward = forward;
            Vector3 wristRight = Vector3.Cross(Vector3.up, wristForward);
            position = hand + wristForward * .35f + Vector3.up * .19f + wristRight * .15f;
            Vector3 fromHead = position - head.position;
            if (fromHead.sqrMagnitude < .55f * .55f)
                position = head.position + (fromHead.sqrMagnitude > .001f ? fromHead.normalized : forward) * .55f;
            return true;
        }

        private Vector3 SafeVisualPosition(Vector3 head, Vector3 desired, float radius, float margin, out float scale)
        {
            Vector3 delta = desired - head;
            float distance = delta.magnitude;
            scale = 1f;
            if (distance < .001f) return desired;
            float nearest = distance;
            int hits = Physics.SphereCastNonAlloc(head, radius, delta / distance, castHits,
                distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits; i++)
                if (BlocksRoute(castHits[i].collider)) nearest = Mathf.Min(nearest, castHits[i].distance - margin);
            nearest = Mathf.Clamp(nearest, Mathf.Min(.30f, distance), distance);
            scale = nearest / distance;
            return head + delta * scale;
        }

        private static void Face(Transform canvas, Transform head)
        {
            Vector3 forward = canvas.position - head.position;
            if (forward.sqrMagnitude > .001f) canvas.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        private void DrawRoute(Vector3 observer, int destination)
        {
            HideFloor();
            floorVisuals.SetActive(true);
            circle.SetActive(true);
            circle.transform.position = graph.Nodes[destination] + Vector3.up * .04f;
            if (arrived) return;
            if (!graph.TryRoute(observer, destination, SegmentClear, route))
            {
                instruction.text = "Route blocked. Return to an open passage, or close the guide.";
                return;
            }
            instruction.text = "Follow the arrows. Stand inside the footprint circle.";
            int used = 0;
            for (int segment = 1; segment < route.Count && used < arrows.Count; segment++)
            {
                Vector3 a = route[segment - 1], b = route[segment];
                Vector3 direction = (b-a).normalized;
                float length = Vector3.Distance(a, b);
                // Each segment keeps its authored corner; no spline can cut across furniture.
                for (float distance = .22f; distance < length - .10f && used < arrows.Count; distance += .34f)
                {
                    GameObject arrow = arrows[used++];
                    arrow.transform.position = a + direction * distance + Vector3.up * .04f;
                    arrow.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                    arrow.SetActive(true);
                }
            }
        }

        private bool SegmentClear(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0;
            Vector3 bottom = a + Vector3.up * .34f;
            Vector3 top = a + Vector3.up * 1.48f;
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, .24f,
                overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (BlocksRoute(overlaps[i])) return false;
            float length = Vector3.Distance(a, b);
            if (length < .001f) return true;
            count = Physics.CapsuleCastNonAlloc(bottom, top, .24f, (b-a) / length,
                castHits, length, ~0, QueryTriggerInteraction.Ignore);
            if (count == castHits.Length) return false;
            for (int i = 0; i < count; i++) if (BlocksRoute(castHits[i].collider)) return false;
            return true;
        }

        private bool BlocksRoute(Collider collider)
        {
            if (collider == null || collider is CharacterController || collider.transform.IsChildOf(transform)) return false;
            if (collider.bounds.max.y < .08f) return false;
            return true;
        }

        private void HideFloor()
        {
            if (floorVisuals != null) floorVisuals.SetActive(false);
            foreach (var arrow in arrows) arrow.SetActive(false);
        }
        private void HideRoute() { HideFloor(); if (targetCanvas != null) targetCanvas.gameObject.SetActive(false); HideTargetCues(); }
        private static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

        private void RefreshCard()
        {
            observeInstructionBase = string.Empty;
            ApplyCardLayout(false);
            foreach (var answer in answers) answer.gameObject.SetActive(false);
            startButton.gameObject.SetActive(stage == Stage.Intro && guided);
            replayButton.gameObject.SetActive(stage == Stage.Complete && guided);
            freeButton.gameObject.SetActive(true);
            controlsButton.gameObject.SetActive(true);
            mainIcon.Symbol = ReviewIconGraphic.Kind.Walk;
            mainIcon.SetVerticesDirty();
            if (!guided)
            {
                title.text = "FREE EXPLORATION"; instruction.text = "Sitting, notes and the remote remain available.";
                progress.text = "Use GUIDE beside HELP to resume the review.";
                startButton.gameObject.SetActive(stage == Stage.Intro);
                return;
            }
            title.text = stage == Stage.Intro ? "LIVING ROOM REVIEW" : stage == Stage.Complete ? "REVIEW COMPLETE" : ((int)stage) + " / 9  " + StageTitle();
            if (stage == Stage.Intro)
            {
                instruction.text = "Follow footprints. Observe storage and views. Sit, reach and review.";
                progress.text = "White shapes guide actions. Existing measurement colours keep their meaning.";
                return;
            }
            if (stage == Stage.Complete)
            {
                instruction.text = "Your observations are ready. Explore again or replay the review.";
                progress.text = saveMessage;
                mainIcon.Symbol = ReviewIconGraphic.Kind.Check; mainIcon.SetVerticesDirty();
                return;
            }
            if (stage == Stage.Sit)
            {
                instruction.text = arrived ? "Point at the chair icon. Press TRIGGER to sit." : "Follow the footprints to the sofa's side.";
                progress.text = manager.IsXrMode ? "Simulated seated view; use STAND UP to leave." : "Seated interaction requires XR or the XR simulator.";
                ApplyCardLayout(true);
                return;
            }
            if (stage == Stage.Remote)
            {
                mainIcon.Symbol = remoteGrabSeen ? ReviewIconGraphic.Kind.Return : ReviewIconGraphic.Kind.Hand; mainIcon.SetVerticesDirty();
                instruction.text = remoteGrabSeen ? "Release TRIGGER inside the white home outline." : "Reach to the remote. Hold TRIGGER for 0.5s to pick up.";
                progress.text = "Try the table's position from this seat. RESET does not complete the task.";
                ApplyCardLayout(true);
                return;
            }
            if (stage == Stage.Stand)
            {
                instruction.text = "Use STAND UP to return to the marked floor position.";
                progress.text = "The standing viewpoint and walking controls will be restored.";
                ApplyCardLayout(true);
                return;
            }
            if (stage == Stage.Display && (cabinetObservationComplete || !CabinetReadyForObservation))
            {
                instruction.text = cabinetObservationComplete
                    ? "Observation confirmed. Push both doors closed to finish this task."
                    : !cabinetTaskStarted ? "Stand in the cabinet circle briefly to confirm the start point."
                    : "Reach to each handle. Hold TRIGGER and pull both doors open.";
                progress.text = cabinet == null || !cabinet.IsReady ? "Cabinet interaction is unavailable."
                    : cabinet.IsHandPulling ? "Pull or push with your hand; release TRIGGER to let go."
                    : cabinet.IsMoving ? "Wait for the doors to stop."
                    : !cabinetTaskStarted ? "Confirm once; you will not need to stay in the circle."
                    : cabinetObservationComplete ? "Close both doors from any comfortable position near the handles."
                    : "Start confirmed. You may leave the circle to reach both handles.";
                ApplyCardLayout(false);
                return;
            }
            bool canObserve = arrived || stage == Stage.SeatedLook;
            if (!canObserve)
            {
                instruction.text = "Follow the arrows. Stand inside the footprint circle.";
                progress.text = "Next: " + StageQuestion();
                ApplyCardLayout(true);
                return;
            }
            mainIcon.Symbol = ReviewIconGraphic.Kind.Eye; mainIcon.SetVerticesDirty();
            if (lookedCount < lookTargets.Length)
            {
                instruction.text = "Look toward the eye icon: " + targetNames[lookedCount] + ".";
                observeInstructionBase = instruction.text;
                progress.text = "Observe " + (lookedCount+1) + " / " + lookTargets.Length + ". Turn your head naturally.";
                title.text += "  (" + (lookedCount+1) + "/" + lookTargets.Length + ")";
                ApplyCardLayout(true);
            }
            else
            {
                instruction.text = StageQuestion();
                progress.text = "Concern about an object? Point and press side GRIP to add a category and reason.";
                progress.fontSize = 16;
                progress.rectTransform.sizeDelta = new Vector2(505, 43);
                progress.rectTransform.anchoredPosition = new Vector2(25, -20);
                foreach (var answer in answers) answer.gameObject.SetActive(true);
                freeButton.gameObject.SetActive(false); controlsButton.gameObject.SetActive(false);
            }
        }

        private void ApplyCardLayout(bool compact)
        {
            float height = compact ? 105f : 205f;
            panel.GetComponent<RectTransform>().sizeDelta = new Vector2(610, height);
            panelBack.rectTransform.sizeDelta = new Vector2(610, height);
            title.rectTransform.anchoredPosition = new Vector2(25, compact ? 25 : 70);
            title.fontSize = compact ? 21 : 25;
            instruction.rectTransform.anchoredPosition = new Vector2(25, compact ? -17 : 28);
            instruction.rectTransform.sizeDelta = new Vector2(505, compact ? 42 : 56);
            instruction.fontSize = compact ? 18 : 20;
            mainIcon.rectTransform.anchoredPosition = new Vector2(-260, compact ? 4 : 35);
            mainIcon.rectTransform.sizeDelta = Vector2.one * (compact ? 42 : 60);
            pauseButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(277, compact ? 25 : 76);
            progress.gameObject.SetActive(!compact);
            if (compact) { freeButton.gameObject.SetActive(false); controlsButton.gameObject.SetActive(false); }
        }

        private string StageTitle()
        {
            switch (stage)
            {
                case Stage.Entry: return "ENTER & ORIENT";
                case Stage.Display: return "STORAGE ACCESS";
                case Stage.Bookcase: return "CIRCULATION";
                case Stage.TvSide: return "SIDE VIEW";
                case Stage.Coffee: return "SOFA & TABLE";
                case Stage.Sit: return "TAKE A SEAT";
                case Stage.SeatedLook: return "SEATED VIEW";
                case Stage.Remote: return "REACH & RETURN";
                default: return "FINISH";
            }
        }

        private string StageQuestion()
        {
            switch (stage)
            {
                case Stage.Entry: return "Is the living area easy to understand from the entrance?";
                case Stage.Display: return "With the doors open, is there room to access storage and pass?";
                case Stage.Bookcase: return "Is access past the chair to the bookcase comfortable?";
                case Stage.TvSide: return "Is the TV visible comfortably from this side angle?";
                case Stage.Coffee: return "Are the table height and sofa clearance comfortable?";
                default: return "Are the TV height and table relationship comfortable when seated?";
            }
        }

        private string TargetIds()
        {
            switch (stage)
            {
                case Stage.Entry: return "F01,F06";
                case Stage.Display: return "F09";
                case Stage.Bookcase: return "F11,F03";
                case Stage.TvSide: return "F06";
                default: return "F01,F04,F06";
            }
        }

        private void Answer(string answer)
        {
            // The cabinet step completes by opening, looking, and closing; no rating is required.
            if (stage == Stage.Display) return;
            if (!guided || record == null || lookedCount < lookTargets.Length || lookTargets.Length == 0) return;
            if (Destination() >= 0 && !arrived) return;
            Observation previous = record.observations.Find(item => item.task == stage.ToString());
            record.observations.RemoveAll(item => item.task == stage.ToString());
            var observation = new Observation { task = stage.ToString(), targetIds = TargetIds(),
                question = StageQuestion(), answer = answer, utc = Utc(), observerPosition = manager.ActiveCamera.transform.position,
                targetPositions = (Vector3[])lookTargets.Clone(),
                cabinetDoorsOpen = stage == Stage.Display && cabinet.IsOpen };
            record.observations.Add(observation);
            if (!SaveRecord())
            {
                record.observations.Remove(observation);
                if (previous != null) record.observations.Add(previous);
                Message(saveMessage); return;
            }
            Pulse();
            SetStage(stage == Stage.SeatedLook ? Stage.Remote : stage + 1);
        }

        private void UpdateControlHint(bool controlsOpen, bool noteOpen)
        {
            if (controlsOpen)
            {
                // The original tutorial has already set its own hint in OnEnable. Hand over
                // ownership without clearing that hint when HELP was opened externally.
                ownsControlHint = false; currentHint = null; return;
            }
            string part = null;
            HighlightTarget hand = HighlightTarget.Both;
            if (guided && !noteOpen && manager.IsXrMode && !seat.IsTransitioning)
            {
                if (stage == Stage.Intro || stage == Stage.Stand) part = "Trigger";
                else if (stage == Stage.Remote) part = "Trigger";
                else if (stage == Stage.Sit && arrived) part = "Trigger";
                else if (Destination() >= 0 && !arrived && !seat.IsSeated)
                { part = "ThumbStick"; hand = HighlightTarget.Left; }
                else if (lookTargets.Length > 0 && lookedCount == lookTargets.Length) part = "Trigger";
            }
            if (part == currentHint) return;
            ReleaseControlHint();
            if (part != null && controls != null && controls.Highlighter != null)
            {
                controls.Highlighter.Highlight(part, hand, part != "ThumbStick");
                ownsControlHint = true; currentHint = part;
            }
        }

        private void ReleaseControlHint()
        {
            if (ownsControlHint && controls != null && controls.Highlighter != null) controls.Highlighter.Clear();
            ownsControlHint = false; currentHint = null;
        }

        private void Pulse()
        {
            if (manager == null || !manager.IsXrMode) return;
            if (leftController != null) leftController.SendHapticImpulse(.18f, .045f);
            if (rightController != null) rightController.SendHapticImpulse(.18f, .045f);
        }

        private void TrySit()
        {
            if (FlatDistance(manager.ActiveCamera.transform.position, exitFloor.position) > 1.05f)
            { Message("Move to the seat icon first."); return; }
            if (!seat.TrySit()) Message(seat.LastMessage);
        }
        private void TryStand() { if (!seat.TryStand()) Message(seat.LastMessage); }
        private void ResetRemote()
        {
            remoteGrabSeen = remoteReturned = false;
            remote.ResetToHome();
            RecordAction("remote_reset");
            RefreshCard();
        }
        private void OnSeatedChanged(bool seated)
        {
            if (summaryMode) { summary?.SetSeated(seated, seat.IsTransitioning); return; }
            if (!guided) return;
            RecordAction(seated ? "entered_simulated_seated_view" : "returned_to_standing");
            if (seated && stage == Stage.Sit) SetStage(Stage.SeatedLook);
            else if (!seated && stage == Stage.Stand && manager.IsXrMode)
            {
                CompleteReview();
            }
            else if (!seated && stage >= Stage.SeatedLook && stage <= Stage.Stand)
            {
                remoteGrabSeen = remoteReturned = false;
                SetStage(Stage.Sit);
            }
            else RefreshCard();
        }
        private void OnRemoteGrabbed()
        {
            if (!guided || stage != Stage.Remote || !seat.IsSeated) return;
            remoteGrabSeen = true; remoteReturned = false;
            RecordAction("remote_grabbed"); RefreshCard();
        }
        private void OnRemoteReturned()
        {
            if (guided && stage == Stage.Remote && remoteGrabSeen && manager.IsXrMode) remoteReturned = true;
        }
        private void CompleteReview()
        {
            if (record == null || record.status != "in_progress") return;
            record.routeCompleted = true;
            SetStage(Stage.Complete);
            if (seat.IsTransitioning) finishAfterSeatTransition = true;
            else RequestFinish();
        }

        private void EnsureSession()
        {
            if (record != null) return;
            record = new SessionRecord { startedUtc = Utc(), status = "in_progress" };
            string directory = string.IsNullOrEmpty(SessionDirectoryOverride)
                ? Path.Combine(Application.persistentDataPath, "ReviewSessions") : SessionDirectoryOverride;
            recordPath = Path.Combine(directory, "living-room-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
        }

        private string SaveAnnotation(ReviewAnnotationRecord annotation)
        {
            if (!ready || !isActiveAndEnabled || summaryMode || ending)
                return "Return to review before adding a note. Your draft has been kept.";
            EnsureSession();
            annotation.pointId = routeStarted && guided && IsReviewPoint(stage) ? stage.ToString() : string.Empty;
            record.annotations.Add(annotation);
            if (SaveRecord()) return null;
            record.annotations.Remove(annotation);
            return "Could not save the note. Your draft has been kept; please try again.";
        }

        public void RequestFinish()
        {
            if (!ready || !isActiveAndEnabled || ending) return;
            if (summaryMode)
            {
                if (summary != null && summary.IsVisible) ResumeReview();
                else ShowResults();
                return;
            }
            if (remote.IsHeld) { Message("Put down the remote before ending the review."); return; }
            if (cabinet != null && cabinet.IsMoving)
            { Message("Wait for the cabinet doors to stop before ending the review."); return; }
            if (cabinet != null && !cabinet.IsClosed)
            { Message("Close the cabinet doors before ending the review."); return; }
            if (seat.IsTransitioning) { Message("Wait for the seated view to finish changing."); return; }
            if (AnnotationSpawner.currentActiveUI != null)
            {
                var editor = AnnotationSpawner.currentActiveUI.GetComponent<AnnotationUI>();
                if (editor != null) editor.RequestFinish(RequestFinish);
                return;
            }
            EnsureSession();
            ending = true;
            string previousStatus = record.status;
            string previousEnd = record.endedUtc;
            record.status = record.routeCompleted ? "completed" : "ended_early";
            record.endedUtc = Utc();
            if (!SaveRecord())
            {
                record.status = previousStatus; record.endedUtc = previousEnd;
                ending = false; Message(saveMessage); return;
            }
            guidanceBeforeSummary = guided;
            summaryMode = true;
            guided = false;
            ending = false;
            transientUntil = 0f;
            ReleaseControlHint(); HideRoute();
            panel.gameObject.SetActive(false);
            seatCanvas.gameObject.SetActive(false);
            utilityCanvas.gameObject.SetActive(false);
            hudEntry.SetResultsAvailable(true);
            ShowResults();
        }

        private void ShowResults()
        {
            if (record == null || summary == null) return;
            controls?.Dismiss();
            ReleaseControlHint(); HideRoute();
            panel.gameObject.SetActive(false);
            seatCanvas.gameObject.SetActive(false);
            utilityCanvas.gameObject.SetActive(false);
            if (!ownsSummaryProximity)
            {
                proximityBeforeSummary = manager.ProximityEnabled;
                manager.SetProximityEnabled(false);
                ownsSummaryProximity = true;
                foreach (var display in FindObjectsOfType<FurnitureProximityDisplay>())
                    if (display.enabled) { summaryDimensionDisplays.Add(display); display.enabled = false; }
            }
            summary.Show(SummaryPoints(), record.annotations,
                record.status == "completed" ? "COMPLETED" : "ENDED EARLY", saveMessage, seat.IsSeated);
        }

        private void CloseSummary()
        {
            summary?.Hide();
            if (!ownsSummaryProximity) return;
            if (manager != null && !manager.ProximityEnabled) manager.SetProximityEnabled(proximityBeforeSummary);
            foreach (var display in summaryDimensionDisplays) if (display != null) display.enabled = true;
            summaryDimensionDisplays.Clear();
            ownsSummaryProximity = false;
        }

        public void ResumeReview()
        {
            if (!ready || record == null || seat.IsTransitioning) return;
            string previousStatus = record.status, previousEnd = record.endedUtc;
            bool previousCompleted = record.routeCompleted;
            if (stage == Stage.Complete) record.routeCompleted = false;
            record.status = "in_progress"; record.endedUtc = null;
            if (!SaveRecord())
            {
                record.status = previousStatus; record.endedUtc = previousEnd;
                record.routeCompleted = previousCompleted;
                ShowResults(); return;
            }
            CloseSummary();
            summaryMode = false;
            bool resumeGuidance = guidanceBeforeSummary;
            if (stage == Stage.Complete)
            { record.routeCompleted = false; SetStage(Stage.Entry); routeStarted = true; resumeGuidance = true; }
            guided = false; // OpenGuidance reconciles any free-exploration seat changes.
            if (resumeGuidance) OpenGuidance();
            else PauseGuidance();
            hudEntry.SetResultsAvailable(false);
        }

        private static bool IsReviewPoint(Stage value) => value == Stage.Entry || value == Stage.Display ||
            value == Stage.Bookcase || value == Stage.TvSide || value == Stage.Coffee || value == Stage.SeatedLook;

        private List<ReviewSummaryPanel.PointRow> SummaryPoints()
        {
            Stage[] stages = { Stage.Entry, Stage.Display, Stage.Bookcase, Stage.TvSide, Stage.Coffee, Stage.SeatedLook };
            string[] titles = { "Entrance overview", "Display cabinet", "Bookcase access", "TV side view", "Sofa & coffee table", "Seated view" };
            string[] questions = {
                "Is the living area easy to understand from the entrance?",
                "With the doors open, is there room to access storage and pass?",
                "Is access past the chair to the bookcase comfortable?",
                "Is the TV visible comfortably from this side angle?",
                "Are the table height and sofa clearance comfortable?",
                "Are the TV height and table relationship comfortable when seated?" };
            var rows = new List<ReviewSummaryPanel.PointRow>(6);
            for (int i = 0; i < stages.Length; i++)
            {
                string id = stages[i].ToString();
                Observation observation = record.observations.Find(item => item.task == id);
                rows.Add(new ReviewSummaryPanel.PointRow { id = id, title = titles[i], question = questions[i],
                    answer = observation != null ? observation.answer : "UNRATED",
                    noteCount = record.annotations.FindAll(item => item.pointId == id).Count });
            }
            return rows;
        }
        private void RecordAction(string action)
        {
            if (!guided || record == null || record.status == "completed") return;
            record.actions.Add(new ReviewAction { action = action, utc = Utc() }); SaveRecord();
        }
        private bool SaveRecord()
        {
            if (record == null || string.IsNullOrEmpty(recordPath)) return false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(recordPath));
                string temporaryPath = recordPath + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(record, true));
                if (File.Exists(recordPath)) File.Replace(temporaryPath, recordPath, null);
                else File.Move(temporaryPath, recordPath);
                saveMessage = "Saved locally: " + record.observations.Count + " point reviews, " + record.annotations.Count + " object notes.";
                return true;
            }
            catch (Exception exception)
            {
                saveMessage = "Could not save this review. Check local storage and try again.";
                Debug.LogWarning("Review session save failed: " + exception.Message, this);
                return false;
            }
        }
        private static string Utc() => DateTime.UtcNow.ToString("o");
        private void Message(string message)
        {
            transientMessage = message; transientUntil = Time.unscaledTime + 4f;
            ApplyCardLayout(false);
            progress.text = message;
        }

        private void OnDestroy()
        {
            ReleaseControlHint();
            if (seat != null) seat.SeatedChanged -= OnSeatedChanged;
            if (remote != null) { remote.Grabbed -= OnRemoteGrabbed; remote.Returned -= OnRemoteReturned; }
            if (cabinet != null)
            {
                cabinet.StateChanged -= OnCabinetStateChanged;
                cabinet.ActionBlocked -= OnCabinetBlocked;
                cabinet.HandPullStarted -= OnCabinetHandPullStarted;
                cabinet.CanInteract = null;
            }
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        private void OnDisable()
        {
            if (!ready) return;
            CloseSummary();
            if (annotationStore != null) annotationStore.enabled = false;
            ReleaseControlHint();
            if (hudEntry != null) hudEntry.enabled = false;
            seat.SeatedChanged -= OnSeatedChanged;
            remote.Grabbed -= OnRemoteGrabbed; remote.Returned -= OnRemoteReturned;
            seat.enabled = false; remote.enabled = false;
            if (cabinet != null) { cabinet.SetInteractionAllowed(false); cabinet.enabled = false; }
            HideRoute();
            if (panel != null) panel.gameObject.SetActive(false);
            if (seatCanvas != null) seatCanvas.gameObject.SetActive(false);
            if (utilityCanvas != null) utilityCanvas.gameObject.SetActive(false);
            if (cabinetCanvas != null) cabinetCanvas.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            if (!ready) return;
            if (annotationStore != null) annotationStore.enabled = true;
            if (hudEntry != null) hudEntry.enabled = true;
            seat.enabled = true; remote.enabled = true;
            if (cabinet != null) cabinet.enabled = true;
            seat.SeatedChanged += OnSeatedChanged;
            remote.Grabbed += OnRemoteGrabbed; remote.Returned += OnRemoteReturned;
            if (stage >= Stage.SeatedLook && stage <= Stage.Stand) SetStage(Stage.Sit);
        }
    }
}
