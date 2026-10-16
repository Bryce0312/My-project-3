using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LivingRoom30.Measurement;
using LivingRoom30.Review;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

public static class ReviewJourneyValidation
{
    static ReviewJourneyController journey;
    static SpatialMeasureManager manager;
    static Camera head;
    static Transform origin;
    static double deadline, waitUntil;
    static int phase;
    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static string output;
    static ActionBasedController[] controllers;
    static ActionBasedController grabController;
    static ReviewRemoteGrabInteractor grabHand;
    static Transform remoteHome;
    static bool gripPressed, triggerPressed;
    static bool grabTracked = true;
    static Func<bool> savedUiTargeted;
    static bool uiCallbackOverridden;
    static int lastInputFrame = -1;
    static Button hudGuideButton;
    static LivingRoom30.TutorialController controlsTutorial;
    static int hudButtonCount, guideListenerCount, guideInstanceId, guideCycles, savedEntryLooked;
    static bool entryCloseVerified;
    static string savedCloseStage, completedRecordPath;
    static ReviewHudEntry lifecycleHudEntry;
    static ReviewHudLifecycleProbe hudLifecycleProbe;
    static GameObject hudRoot;
    static Transform hudRow;
    static int hudLifecycleErrors;
    static bool finishing, requestedPass;
    static readonly List<HudLayoutSnapshot> originalHudLayouts = new List<HudLayoutSnapshot>();
    static readonly Dictionary<string, int> originalHudListeners = new Dictionary<string, int>();
    sealed class HudLayoutSnapshot
    {
        public string name;
        public Vector2 anchorMin, anchorMax, position, size;
    }

    [Serializable] class Report { public bool passed; public string limitation; public string[] checks, errors; }
    public static void Run()
    {
        output = Path.GetFullPath("../Results");
        Directory.CreateDirectory(output);
        Application.logMessageReceived += Log;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorSceneManager.OpenScene("Assets/Scenes/SceneNew.unity");
        CaptureOriginalHudLayouts();
        Require(UnityEngine.Object.FindObjectsOfType<AnnotationSpawner>().Length == 23, "23 existing annotation connections preserved");
        EditorApplication.update += Tick;
        deadline = EditorApplication.timeSinceStartup + 180;
        gripPressed = triggerPressed = false;
        grabTracked = true;
        uiCallbackOverridden = false;
        savedUiTargeted = null;
        lastInputFrame = -1;
        controllers = null;
        grabController = null;
        hudGuideButton = null;
        entryCloseVerified = false;
        guideCycles = 0;
        finishing = requestedPass = false;
        hudLifecycleProbe = null;
        phase = 0;
        EditorApplication.EnterPlaymode();
    }
    static void Log(string message, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
            errors.Add(message + "\n" + stack);
    }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out at phase " + phase);
            if (!EditorApplication.isPlaying) return;
            DriveControllerInput();
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            if (phase == 0)
            {
                journey = UnityEngine.Object.FindObjectOfType<ReviewJourneyController>();
                manager = UnityEngine.Object.FindObjectOfType<SpatialMeasureManager>();
                if (journey == null || !journey.IsReady || manager == null) return;
                head = (Camera)Field(manager, "xrCam"); origin = manager.XrOriginTransform;
                foreach (var driver in UnityEngine.Object.FindObjectsOfType<TrackedPoseDriver>(true)) driver.enabled = false;
                foreach (var simulator in UnityEngine.Object.FindObjectsOfType<XRDeviceSimulator>(true)) simulator.enabled = false;
                controllers = UnityEngine.Object.FindObjectsOfType<ActionBasedController>(true);
                foreach (var controller in controllers)
                {
                    // Batch mode has no tracked hands. Give the synthetic hands normal poses
                    // instead of leaving an untracked controller model inside the eye camera.
                    controller.enableInputTracking = false;
                    controller.enableInputActions = false;
                    controller.transform.localPosition = new Vector3(controller.name.ToLowerInvariant().Contains("left") ? -.25f : .25f,-.35f,.35f);
                    controller.transform.localRotation = Quaternion.identity;
                    var state = controller.currentControllerState;
                    state.isTracked = true;
                    state.inputTrackingState = UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation;
                    state.position = controller.transform.localPosition;
                    state.rotation = controller.transform.localRotation;
                }
                Set(manager, "forceDesktop", true); Set(manager, "useXrNow", true); Set(manager, "mainCam", head);
                head.enabled = true;
                manager.SetReviewLocomotionBlocked(false);
                Require(journey.Seat != null && journey.Remote != null, "Seat and remote explicitly initialized by the scene component");
                Require(journey.GetComponentsInChildren<Collider>(true).Length == 1, "Guide visuals and UI have no physical colliders");
                Require(journey.Remote.IsAtHome && !journey.Remote.IsHeld, "Remote starts at its tabletop home");
                ValidateRoutes();
                journey.SessionDirectoryOverride = Path.Combine(output, "Sessions");
                PlaceHead(new Vector3(-2.70f,1.6f,-2.42f),new Vector3(-2.45f,0,-1.55f));
                waitUntil = EditorApplication.timeSinceStartup + .3;
                phase = 70;
            }
            else if (phase == 70)
            {
                var hudEntry = (ReviewHudEntry)Field(journey, "hudEntry");
                controlsTutorial = (LivingRoom30.TutorialController)Field(journey, "controls");
                Require(hudEntry != null && hudEntry.IsAvailable && hudEntry.EntryButton != null,
                    "Existing HUD exposes an available GUIDE entry");
                hudGuideButton = hudEntry.EntryButton;
                Require(controlsTutorial != null && controlsTutorial.HelpButton != null &&
                    hudGuideButton.transform.parent == controlsTutorial.HelpButton.transform.parent,
                    "GUIDE is placed in the original HELP/PAUSE/GAPS HUD row");
                hudButtonCount = hudGuideButton.transform.parent.GetComponentsInChildren<Button>(true).Length;
                guideListenerCount = ListenerCount(hudGuideButton);
                guideInstanceId = hudGuideButton.GetInstanceID();
                originalHudListeners.Clear();
                foreach (var layout in originalHudLayouts)
                    originalHudListeners[layout.name] = ListenerCount(HudOriginalButton(layout.name));
                Require(guideListenerCount == 1, "GUIDE has one registered click callback");
                Require(journey.CurrentStage == "Intro" && GuideVisible(), "Initial guide opens at Intro");
                Click((Button)Field(journey, "pauseButton"));
                Require(!GuideVisible(), "The task card X hides the guide immediately");
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 71;
            }
            else if (phase == 71)
            {
                Require(!GuideVisible() && !(bool)Field(journey, "guided") && RouteHidden(),
                    "Guide stays closed across Update frames and hides route visuals");
                Require(hudGuideButton.gameObject.activeInHierarchy && hudGuideButton.interactable,
                    "HUD GUIDE remains available after closing the task card");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 72;
            }
            else if (phase == 72)
            {
                Require(GuideVisible() && journey.CurrentStage == "Intro", "HUD GUIDE reopens the same Intro stage");
                Click((Button)Field(journey, "freeButton"));
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 73;
            }
            else if (phase == 73)
            {
                Require(!GuideVisible() && !(bool)Field(journey, "guided") && RouteHidden(),
                    "FREE EXPLORE closes the guide through its real button callback");
                Click(controlsTutorial.HelpButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 74;
            }
            else if (phase == 74)
            {
                Require(controlsTutorial.gameObject.activeInHierarchy && !GuideVisible(),
                    "HELP opens the original controls tutorial while the guide remains hidden");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 75;
            }
            else if (phase == 75)
            {
                Require(GuideVisible() && !controlsTutorial.gameObject.activeInHierarchy && journey.CurrentStage == "Intro",
                    "Opening GUIDE dismisses HELP and preserves the guide stage");
                Click(controlsTutorial.HelpButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 78;
            }
            else if (phase == 78)
            {
                Require(controlsTutorial.gameObject.activeInHierarchy && !GuideVisible() && RouteHidden(),
                    "Opening HELP while GUIDE is visible hides guide cards and route visuals");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 79;
            }
            else if (phase == 79)
            {
                Require(GuideVisible() && !controlsTutorial.gameObject.activeInHierarchy,
                    "GUIDE takes focus back from HELP without overlapping tutorials");
                Capture("06-guide-HUD.png");
                Click((Button)Field(journey, "pauseButton"));
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 76;
            }
            else if (phase == 76)
            {
                Require(!GuideVisible() && RouteHidden(), "Repeated X close remains closed across frames");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 77;
            }
            else if (phase == 77)
            {
                Require(GuideVisible() && journey.CurrentStage == "Intro", "Repeated HUD reopen preserves Intro");
                Require(hudGuideButton.GetInstanceID() == guideInstanceId &&
                    hudGuideButton.transform.parent.GetComponentsInChildren<Button>(true).Length == hudButtonCount &&
                    ListenerCount(hudGuideButton) == guideListenerCount,
                    "Close/reopen cycles preserve one GUIDE button and one click callback");
                if (++guideCycles < 3)
                {
                    Click((Button)Field(journey, "pauseButton"));
                    waitUntil = EditorApplication.timeSinceStartup + .15;
                    phase = 76;
                    return;
                }
                lifecycleHudEntry = (ReviewHudEntry)Field(journey, "hudEntry");
                hudRow = controlsTutorial.HelpButton.transform.parent;
                Transform hudAncestor = hudRow;
                while (hudAncestor != null && hudAncestor.name != "S05_ReviewHud") hudAncestor = hudAncestor.parent;
                Require(hudAncestor != null, "Original S05_ReviewHud parent is available for lifecycle regression");
                hudRoot = hudAncestor.gameObject;
                hudLifecycleErrors = errors.Count;
                hudRoot.SetActive(false);
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 100;
            }
            else if (phase == 100)
            {
                Require(!hudRoot.activeInHierarchy && !lifecycleHudEntry.IsAvailable,
                    "Hiding the original parent HUD makes GUIDE unavailable without a hierarchy error");
                Require(errors.Count == hudLifecycleErrors, "Parent HUD hide logs no activation lifecycle errors");
                hudRoot.SetActive(true);
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 101;
            }
            else if (phase == 101)
            {
                RefreshHudGuideReference();
                Require(lifecycleHudEntry.IsAvailable && GuideButtonCount(true) == 1,
                    "Restoring the parent HUD restores exactly one active GUIDE entry");
                hudLifecycleProbe = hudRoot.AddComponent<ReviewHudLifecycleProbe>();
                hudLifecycleProbe.Target = lifecycleHudEntry;
                hudLifecycleProbe.Armed = true;
                // OnDisable runs inside the parent's SetActive stack, where reparenting its
                // children is forbidden. This reproduces the reported activation guard.
                hudRoot.SetActive(false);
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 102;
            }
            else if (phase == 102)
            {
                Require(hudLifecycleProbe.DisableCalls == 1 && !lifecycleHudEntry.enabled,
                    "HUD parent OnDisable really disabled the GUIDE owner inside the activation callback");
                Require(errors.Count == hudLifecycleErrors,
                    "Disabling GUIDE during parent HUD deactivation logs no forbidden hierarchy mutation");
                Require(OriginalHudLayoutRestored(), "Parent deactivation restores all three original HUD button layouts");
                hudRoot.SetActive(true);
                lifecycleHudEntry.enabled = true;
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 103;
            }
            else if (phase == 103)
            {
                RefreshHudGuideReference();
                Require(lifecycleHudEntry.IsAvailable && GuideButtonCount(true) == 1 && GuideButtonCount(false) == 1,
                    "GUIDE recovers once after being disabled inside parent HUD deactivation");
                lifecycleHudEntry.enabled = false;
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 104;
            }
            else if (phase == 104)
            {
                Require(OriginalHudLayoutRestored() && hudRow.GetComponentsInChildren<Button>(true).Length == 3,
                    "Disabling the GUIDE owner restores the authored three-button HUD layout");
                ValidateOriginalHudCallbacks();
                // Exercise deferred Destroy and repeated Configure before Unity can finish a frame.
                lifecycleHudEntry.enabled = true;
                lifecycleHudEntry.enabled = false;
                lifecycleHudEntry.enabled = true;
                lifecycleHudEntry.Configure(null, journey.OpenGuidance);
                lifecycleHudEntry.Configure(controlsTutorial.HelpButton, journey.OpenGuidance);
                lifecycleHudEntry.Configure(controlsTutorial.HelpButton, journey.OpenGuidance);
                RefreshHudGuideReference();
                Require(GuideButtonCount(true) == 1, "Same-frame disable/enable and Configure reentry keep one active GUIDE");
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 105;
            }
            else if (phase == 105)
            {
                RefreshHudGuideReference();
                Require(GuideButtonCount(true) == 1 && GuideButtonCount(false) == 1 && ListenerCount(hudGuideButton) == 1,
                    "Reentry settles to one GUIDE object and one click callback after deferred destruction");
                Require(errors.Count == hudLifecycleErrors, "HUD hide, lifecycle disable and same-frame reentry complete without errors");
                foreach (var layout in originalHudLayouts)
                    Require(ListenerCount(HudOriginalButton(layout.name)) == originalHudListeners[layout.name],
                        "Original HUD callback count is preserved for " + layout.name);
                hudLifecycleProbe.Armed = false;
                UnityEngine.Object.Destroy(hudLifecycleProbe);
                Click(hudGuideButton);
                Require(GuideVisible() && journey.CurrentStage == "Intro", "Recovered HUD GUIDE still opens its original review stage");
                journey.BeginReview();
                PlaceHead(new Vector3(-2.70f,1.6f,-2.42f),new Vector3(-2.45f,0,-1.55f));
                waitUntil = EditorApplication.timeSinceStartup + .4;
                phase = 10;
            }
            else if (phase == 1)
            {
                if (journey.CurrentStage == "Sit")
                {
                    Vector3 exit = ((Transform)Field(journey, "exitFloor")).position;
                    PlaceHead(exit + Vector3.up * 1.6f, new Vector3(0, 1.21f, -.55f));
                    Require(journey.Seat.TrySit(), "Seat accepts an explicit sit request");
                    Require(!journey.Seat.TrySit(), "Repeated sit is rejected during transition");
                    waitUntil = EditorApplication.timeSinceStartup + .6;
                    phase = 2;
                    return;
                }
                int destination = (int)Call(journey, "Destination");
                Vector3[] nodes = new ReviewRouteGraph().Nodes;
                Vector3[] targets = (Vector3[])Field(journey, "lookTargets");
                int looked = (int)Field(journey, "lookedCount");
                PlaceHead(nodes[destination] + Vector3.up * 1.6f,
                    targets[Mathf.Min(looked, targets.Length - 1)] + Vector3.up * .12f);
                if (!entryCloseVerified && journey.CurrentStage == "Entry" && looked > 0 && looked < targets.Length)
                {
                    savedEntryLooked = looked;
                    Click((Button)Field(journey, "pauseButton"));
                    waitUntil = EditorApplication.timeSinceStartup + .2;
                    phase = 80;
                    return;
                }
                if ((bool)Field(journey, "arrived") && looked == targets.Length)
                {
                    Require(CardVisible(), "Task buttons remain in front of geometry at " + journey.CurrentStage);
                    if(journey.CurrentStage == "Bookcase") Capture("04-bookcase-feedback.png");
                    Call(journey, "Answer", "UNSURE");
                }
            }
            else if (phase == 2)
            {
                Require(journey.Seat.IsSeated && !journey.Seat.IsTransitioning, "Sit fade and pose transition completes");
                Require(Vector3.Distance(head.transform.position, new Vector3(0, 1.21f, -.55f)) < .03f, "Seated eye aligns to configured anchor");
                Require(!origin.GetComponent<CharacterController>().enabled && !origin.GetComponent<ActionBasedContinuousMoveProvider>().enabled,
                    "Seated capsule and movement are blocked together");
                // A physical local head displacement must remain possible after the alignment.
                Vector3 before = head.transform.position;
                head.transform.localPosition += new Vector3(.03f, 0, 0);
                Require(Vector3.Distance(before, head.transform.position) > .025f, "Natural head displacement is not camera-locked");
                head.transform.rotation = Quaternion.LookRotation(new Vector3(0,1.115f,2.414f)-head.transform.position);
                Capture("02-seated.png");
                savedCloseStage = journey.CurrentStage;
                Click((Button)Field(journey, "pauseButton"));
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 90;
            }
            else if (phase == 3)
            {
                if (journey.CurrentStage == "Remote") { phase = 4; return; }
                Vector3[] targets = (Vector3[])Field(journey, "lookTargets");
                int looked = (int)Field(journey, "lookedCount");
                head.transform.rotation = Quaternion.LookRotation(targets[Mathf.Min(looked, targets.Length-1)] + Vector3.up * .12f - head.transform.position);
                if (looked == targets.Length) Call(journey, "Answer", "UNSURE");
            }
            else if (phase == 4)
            {
                var grab = journey.Remote.GetComponent<XRGrabInteractable>();
                var interaction = UnityEngine.Object.FindObjectOfType<XRInteractionManager>();
                foreach (var direct in UnityEngine.Object.FindObjectsOfType<ReviewRemoteGrabInteractor>(true))
                    if (direct.name == "ReviewRemote_LeftHand") { grabHand = direct; break; }
                Require(grabHand != null && interaction != null, "Hand-only grab interactors exist");
                grabController = grabHand.GetComponentInParent<ActionBasedController>();
                Require(grabController != null && grabHand.xrController == grabController,
                    "Remote hand uses its parent controller input state");
                var ray = grabController.GetComponentInChildren<XRRayInteractor>(true);
                Require(ray != null, "Existing furniture ray is present on the grab controller");
                Require(!interaction.IsSelectPossible(ray, grab), "Existing remote rays cannot force-grab the remote");
                remoteHome = (Transform)Field(journey, "remoteHome");
                PlaceGrabHand(true);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 40;
            }
            else if (phase == 40)
            {
                Require(grabHand.hasHover && journey.Remote.ReadyToGrab,
                    "Physical near-hand overlap reaches the remote through normal XRI hover processing");
                Require(!journey.Remote.IsHeld, "Near-hand hover alone does not grab the remote");
                gripPressed = true;
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 41;
            }
            else if (phase == 41)
            {
                Require(!journey.Remote.IsHeld, "Holding Grip alone does not grab the remote");
                gripPressed = false;
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 60;
            }
            else if (phase == 60)
            {
                savedUiTargeted = grabHand.IsUiTargeted;
                uiCallbackOverridden = true;
                grabHand.IsUiTargeted = () => true;
                triggerPressed = true;
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 61;
            }
            else if (phase == 61)
            {
                Require(!journey.Remote.IsHeld && grabHand.AwaitRelease,
                    "A near-hand Trigger press on UI cannot grab the remote");
                grabHand.IsUiTargeted = () => false;
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 62;
            }
            else if (phase == 62)
            {
                Require(!journey.Remote.IsHeld && grabHand.AwaitRelease,
                    "Sliding a held UI press onto the remote cannot turn it into a grab");
                triggerPressed = false;
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 63;
            }
            else if (phase == 63)
            {
                try
                {
                    Require(!journey.Remote.IsHeld && !grabHand.AwaitRelease,
                        "Releasing the UI press restores eligibility for a fresh object press");
                }
                finally { RestoreUiCallback(); }
                phase = 42;
            }
            else if (phase == 42)
            {
                triggerPressed = true;
                waitUntil = EditorApplication.timeSinceStartup + .12;
                phase = 43;
            }
            else if (phase == 43)
            {
                Require(!journey.Remote.IsHeld && journey.Remote.HoldProgress > 0f && journey.Remote.HoldProgress < 1f,
                    "A short Trigger press shows progress without grabbing");
                head.transform.rotation = Quaternion.LookRotation(remoteHome.position + Vector3.up * .14f - head.transform.position);
                Capture("05-remote-trigger-progress.png");
                triggerPressed = false;
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 44;
            }
            else if (phase == 44)
            {
                Require(!journey.Remote.IsHeld && journey.Remote.HoldProgress < .01f,
                    "Releasing a short Trigger press clears the dwell progress");
                grabTracked = false;
                triggerPressed = true;
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 64;
            }
            else if (phase == 64)
            {
                Require(!journey.Remote.IsHeld && !grabHand.ReadyToGrab,
                    "An untracked controller cannot grab using a stale near-hand pose");
                grabTracked = true;
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 65;
            }
            else if (phase == 65)
            {
                Require(!journey.Remote.IsHeld && grabHand.AwaitRelease,
                    "Tracking recovery while Trigger is held waits for a fresh press");
                triggerPressed = false;
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 66;
            }
            else if (phase == 66)
            {
                Require(journey.Remote.ReadyToGrab && !grabHand.AwaitRelease,
                    "Releasing Trigger after tracking recovery restores near-hand grabbing");
                triggerPressed = true;
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 45;
            }
            else if (phase == 45)
            {
                Require(journey.Remote.IsHeld && grabHand.hasSelection,
                    "Holding Trigger for at least 0.5 seconds grabs through normal XRI selection");
                Require(!journey.Seat.TryStand(), "Standing is guarded while an object is held");
                Click((Button)Field(journey, "pauseButton"));
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 94;
            }
            else if (phase == 94)
            {
                Require(!GuideVisible() && journey.Seat.IsSeated && journey.Remote.IsHeld,
                    "Closing GUIDE while holding the remote preserves seat and grab state");
                Require(((Canvas)Field(journey, "utilityCanvas")).gameObject.activeInHierarchy &&
                    ((Button)Field(journey, "resetButton")).interactable &&
                    !((Button)Field(journey, "standButton")).interactable,
                    "Closed guide keeps reset recovery available and the held-object stand guard");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 95;
            }
            else if (phase == 95)
            {
                Require(GuideVisible() && journey.CurrentStage == "Remote" && journey.Remote.IsHeld,
                    "HUD GUIDE resumes the remote task without dropping the held remote");
                PlaceGrabHand(false);
                waitUntil = EditorApplication.timeSinceStartup + .3;
                phase = 46;
            }
            else if (phase == 46)
            {
                Require(journey.Remote.IsHeld && Vector3.Distance(journey.Remote.transform.position, remoteHome.position) > .08f,
                    "Held remote follows the hand away from its original tabletop position");
                Call(journey, "ResetRemote");
                Require(journey.Remote.IsAtHome && !journey.Remote.IsHeld, "Reset releases and returns the remote safely");
                Require(!(bool)Field(journey, "remoteGrabSeen"), "Reset is not counted as task completion");
                PlaceGrabHand(true);
                // Keep Trigger held after reset. A fresh dwell must require a release first.
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 47;
            }
            else if (phase == 47)
            {
                Require(!journey.Remote.IsHeld && journey.Remote.IsAtHome,
                    "Reset while Trigger is held prevents an immediate automatic re-grab");
                Require(journey.CurrentStage == "Remote", "Reset leaves reach-and-return task pending");
                triggerPressed = false;
                PlaceGrabHand(false);
                foreach (var direct in UnityEngine.Object.FindObjectsOfType<ReviewRemoteGrabInteractor>(true))
                    if (direct.name == "ReviewRemote_RightHand") { grabHand = direct; break; }
                Require(grabHand.name == "ReviewRemote_RightHand", "Right hand has its own remote grab interactor");
                grabController = grabHand.GetComponentInParent<ActionBasedController>();
                PlaceGrabHand(true);
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 67;
            }
            else if (phase == 67)
            {
                Require(grabHand.hasHover && journey.Remote.ReadyToGrab, "Right hand reaches normal remote hover");
                triggerPressed = true;
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 68;
            }
            else if (phase == 68)
            {
                Require(journey.Remote.IsHeld && grabHand.hasSelection,
                    "Right-hand Trigger hold grabs without changing shared input bindings");
                Call(journey, "ResetRemote");
                Require(journey.Remote.IsAtHome && !(bool)Field(journey, "remoteGrabSeen"),
                    "Resetting a right-hand grab leaves the review task pending");
                triggerPressed = false;
                PlaceGrabHand(false);
                foreach (var direct in UnityEngine.Object.FindObjectsOfType<ReviewRemoteGrabInteractor>(true))
                    if (direct.name == "ReviewRemote_LeftHand") { grabHand = direct; break; }
                grabController = grabHand.GetComponentInParent<ActionBasedController>();
                PlaceGrabHand(false);
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 48;
            }
            else if (phase == 48)
            {
                Require(!journey.Remote.IsHeld && !journey.Remote.ReadyToGrab,
                    "Releasing after reset clears the latch while the hand is away");
                triggerPressed = true;
                waitUntil = EditorApplication.timeSinceStartup + .7;
                phase = 49;
            }
            else if (phase == 49)
            {
                Require(!journey.Remote.IsHeld, "Holding Trigger away from the remote cannot grab it");
                PlaceGrabHand(true);
                waitUntil = EditorApplication.timeSinceStartup + .15;
                phase = 50;
            }
            else if (phase == 50)
            {
                Require(!journey.Remote.IsHeld && journey.Remote.ReadyToGrab,
                    "Approaching with Trigger already held starts a new near-hand dwell");
                waitUntil = EditorApplication.timeSinceStartup + .55;
                phase = 51;
            }
            else if (phase == 51)
            {
                Require(journey.Remote.IsHeld,
                    "Trigger pressed before approach can grab after a full near-hand dwell");
                PlaceGrabHand(false);
                waitUntil = EditorApplication.timeSinceStartup + .3;
                phase = 52;
            }
            else if (phase == 52)
            {
                Require(journey.Remote.IsHeld, "Trigger hold keeps the remote selected away from home");
                PlaceGrabHand(true);
                waitUntil = EditorApplication.timeSinceStartup + .35;
                phase = 53;
            }
            else if (phase == 53)
            {
                Require(journey.Remote.IsHeld,
                    "Returning the held remote over its outline does not release it early");
                triggerPressed = false;
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 5;
            }
            else if (phase == 5)
            {
                Require(!journey.Remote.IsHeld && !grabHand.hasSelection,
                    "Releasing Trigger releases normal XRI selection");
                Require(journey.Remote.IsAtHome, "Releasing within home outline returns the remote");
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 6;
            }
            else if (phase == 6)
            {
                Require(journey.CurrentStage == "Stand", "Actual grab-and-return advances to standing task");
                Require(journey.Seat.TryStand(), "Standing request succeeds after return");
                waitUntil = EditorApplication.timeSinceStartup + .6;
                phase = 7;
            }
            else if (phase == 7)
            {
                Require(!journey.Seat.IsSeated && !journey.Seat.IsTransitioning, "Standing pose transition completes");
                Require(origin.GetComponent<CharacterController>().enabled && origin.GetComponent<ActionBasedContinuousMoveProvider>().enabled,
                    "Walking and capsule are restored together");
                Require(journey.CurrentStage == "Complete", "Full nine-stage journey completes");
                var session = JsonUtility.FromJson<ReviewJourneyController.SessionRecord>(File.ReadAllText(journey.LastSavedPath));
                Require(session.status == "completed" && session.observations.Count == 6 && session.observations[0].targetPositions.Length == 2,
                    "Six located observations, target coordinates and completion saved to JSON");
                Capture("03-complete.png");
                completedRecordPath = journey.LastSavedPath;
                Require(journey.Summary.IsVisible && journey.Summary.PointCount == 6, "Normal route completion opens the unified six-point results card");
                Click(journey.Summary.Canvas.transform.Find("Summary_Close").GetComponent<Button>());
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 92;
            }
            else if (phase == 92)
            {
                Require(!GuideVisible() && journey.CurrentStage == "Complete", "Closing the completed guide preserves completion");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 93;
            }
            else if (phase == 93)
            {
                Require(journey.Summary.IsVisible && journey.CurrentStage == "Complete" && journey.LastSavedPath == completedRecordPath,
                    "HUD GUIDE reopens Complete without restarting or replacing the saved review");
                Click(journey.Summary.Canvas.transform.Find("Summary_Resume").GetComponent<Button>());
                Require(journey.Session.observations.Count == 6 && !journey.Session.routeCompleted && journey.CurrentStage == "Entry",
                    "Returning from completed results preserves all evaluations and reopens the route");
                Require(!JsonUtility.FromJson<ReviewJourneyController.SessionRecord>(File.ReadAllText(journey.LastSavedPath)).routeCompleted,
                    "Resuming completed results persists the reopened route state");
                journey.RequestFinish();
                Click(journey.Summary.Canvas.transform.Find("Summary_Replay").GetComponent<Button>());
                Require(journey.CurrentStage == "Entry", "Replay resets journey progress");
                Require(journey.Seat.TrySit(), "Seat remains independently usable on replay");
                waitUntil = EditorApplication.timeSinceStartup + .6;
                phase = 8;
            }
            else if (phase == 8)
            {
                Require(journey.Seat.IsSeated, "Repeat sit completes without drift");
                journey.enabled = false;
                Require(!journey.Seat.IsSeated && !(bool)Field(manager,"reviewLocomotionBlocked"),"Disabling journey safely restores standing and releases movement");
                journey.enabled = true;
                Require(journey.Seat.TrySit(),"Journey can be re-enabled without duplicate state listeners");
                waitUntil = EditorApplication.timeSinceStartup + .6;
                phase = 11;
            }
            else if (phase == 11)
            {
                Call(manager, "ConfigureInput"); // forceDesktop=true, exercising the real input reconfiguration path
                waitUntil = EditorApplication.timeSinceStartup + .3;
                phase = 9;
            }
            else if (phase == 9)
            {
                Require(!journey.Seat.IsSeated && !journey.Seat.IsTransitioning, "XR-to-desktop reconfiguration exits the seat safely");
                Require(!(bool)Field(manager, "reviewLocomotionBlocked"), "Input-mode change releases the seat locomotion block");
                Require(journey.Remote.IsAtHome && !journey.Remote.IsHeld, "Input-mode change leaves no held remote");
                Require(UnityEngine.Object.FindObjectsOfType<AnnotationSpawner>().Length == 23, "Existing annotation objects remain after full interaction cycle");
                Finish(errors.Count == 0);
            }
            else if (phase == 10)
            {
                Capture("01-entry-route.png");
                Require(VisibleArrowCount() > 0, "Active walking route has visible floor arrows before closing");
                Click((Button)Field(journey, "pauseButton"));
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 83;
            }
            else if (phase == 83)
            {
                Require(!GuideVisible() && RouteHidden() && VisibleArrowCount() == 0,
                    "X removes a visible walking route and its floor arrows across frames");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 84;
            }
            else if (phase == 84)
            {
                Require(GuideVisible() && journey.CurrentStage == "Entry" && VisibleArrowCount() > 0,
                    "HUD GUIDE restores the current walking route and arrows");
                phase = 1;
            }
            else if (phase == 80)
            {
                Require(!GuideVisible() && RouteHidden() && journey.CurrentStage == "Entry" &&
                    (int)Field(journey, "lookedCount") == savedEntryLooked,
                    "Closing mid-observation preserves completed Entry look targets");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 81;
            }
            else if (phase == 81)
            {
                Require(GuideVisible() && journey.CurrentStage == "Entry" &&
                    (int)Field(journey, "lookedCount") == savedEntryLooked,
                    "HUD GUIDE resumes Entry at the same completed look count");
                entryCloseVerified = true;
                phase = 1;
            }
            else if (phase == 90)
            {
                Require(!GuideVisible() && journey.Seat.IsSeated && journey.CurrentStage == savedCloseStage,
                    "Closing a seated guide preserves the current seated task");
                Require(((Canvas)Field(journey, "utilityCanvas")).gameObject.activeInHierarchy &&
                    ((Button)Field(journey, "standButton")).gameObject.activeInHierarchy &&
                    ((Button)Field(journey, "standButton")).interactable,
                    "Seated STAND UP recovery remains available with the guide closed");
                Require(!origin.GetComponent<CharacterController>().enabled &&
                    !origin.GetComponent<ActionBasedContinuousMoveProvider>().enabled,
                    "Closing a seated guide keeps seated locomotion blocked");
                Click(hudGuideButton);
                waitUntil = EditorApplication.timeSinceStartup + .2;
                phase = 91;
            }
            else if (phase == 91)
            {
                Require(GuideVisible() && journey.Seat.IsSeated && journey.CurrentStage == savedCloseStage,
                    "HUD GUIDE reopens the same seated task without moving the viewer");
                phase = 3;
            }
        }
        catch (Exception exception)
        {
            errors.Add(exception.ToString());
            Finish(false);
        }
    }
    static void ValidateRoutes()
    {
        var graph = new ReviewRouteGraph(); var route = new List<Vector3>();
        int[] itinerary = { 0, 1, 6, 7, 4, 5, 9 };
        for (int i = 1; i < itinerary.Length; i++)
        {
            Require(graph.TryRoute(graph.Nodes[itinerary[i-1]], itinerary[i],
                (a,b) => (bool)Call(journey, "SegmentClear", a, b), route),
                "Physics-checked route " + itinerary[i-1] + " -> " + itinerary[i]);
        }
        Require(!graph.TryRoute(Vector3.zero, 6, (a,b) => false, route) && route.Count == 0,
            "Fully obstructed route fails without a through-furniture fallback");
    }
    static void PlaceHead(Vector3 eye, Vector3 target)
    {
        origin.position += eye - head.transform.position;
        head.transform.rotation = Quaternion.LookRotation(target - head.transform.position);
    }
    static void DriveControllerInput()
    {
        // Keep the real controller and interaction-manager update loop. Only the source state
        // is synthetic: no SelectEnter/SelectExit calls bypass hover, dwell or release logic.
        if (controllers == null || lastInputFrame == Time.frameCount) return;
        lastInputFrame = Time.frameCount;
        foreach (var controller in controllers)
        {
            if (controller == null) continue;
            var state = controller.currentControllerState;
            bool activeHand = controller == grabController;
            state.isTracked = !activeHand || grabTracked;
            state.inputTrackingState = state.isTracked
                ? UnityEngine.XR.InputTrackingState.Position | UnityEngine.XR.InputTrackingState.Rotation
                : UnityEngine.XR.InputTrackingState.None;
            state.position = controller.transform.localPosition;
            state.rotation = controller.transform.localRotation;
            state.selectInteractionState.SetFrameState(activeHand && gripPressed);
            state.activateInteractionState.SetFrameState(activeHand && triggerPressed);
            state.uiPressInteractionState.SetFrameState(activeHand && triggerPressed);
        }
    }
    static void PlaceGrabHand(bool nearHome)
    {
        // Point down at the table rather than at the task card, so these tests exercise
        // object contact instead of the UI-press exclusion rule.
        Vector3 offset = nearHome ? new Vector3(0f, .035f, 0f) : new Vector3(.2f, .3f, 0f);
        grabController.transform.SetPositionAndRotation(remoteHome.position + offset, Quaternion.Euler(90f, 0f, 0f));
        // ApplyControllerState runs before the next EditorApplication.update, so commit the
        // new local pose now as well as maintaining it in DriveControllerInput each frame.
        grabController.currentControllerState.position = grabController.transform.localPosition;
        grabController.currentControllerState.rotation = grabController.transform.localRotation;
        Physics.SyncTransforms();
    }
    static void RestoreUiCallback()
    {
        if (uiCallbackOverridden && grabHand != null) grabHand.IsUiTargeted = savedUiTargeted;
        uiCallbackOverridden = false;
        savedUiTargeted = null;
    }
    static bool GuideVisible() => ((Canvas)Field(journey, "panel")).gameObject.activeInHierarchy;
    static void CaptureOriginalHudLayouts()
    {
        originalHudLayouts.Clear();
        var tutorial = UnityEngine.Object.FindObjectOfType<LivingRoom30.TutorialController>(true);
        if (tutorial == null || tutorial.HelpButton == null) throw new Exception("Authored HELP button is unavailable before Play Mode");
        var helpRect = (RectTransform)tutorial.HelpButton.transform;
        foreach (Transform child in helpRect.parent)
        {
            var rect = child as RectTransform;
            if (rect == null || child.GetComponent<Button>() == null ||
                Mathf.Abs(rect.anchorMin.y - helpRect.anchorMin.y) > .01f ||
                Mathf.Abs(rect.anchorMax.y - helpRect.anchorMax.y) > .01f) continue;
            originalHudLayouts.Add(new HudLayoutSnapshot {
                name=child.name, anchorMin=rect.anchorMin, anchorMax=rect.anchorMax,
                position=rect.anchoredPosition, size=rect.sizeDelta });
        }
        if (originalHudLayouts.Count != 3) throw new Exception("Expected three authored HUD buttons before runtime GUIDE insertion");
    }
    static Button HudOriginalButton(string name)
    {
        Transform row = controlsTutorial != null ? controlsTutorial.HelpButton.transform.parent : hudRow;
        foreach (Transform child in row)
            if (child.name == name && child.GetComponent<Button>() != null) return child.GetComponent<Button>();
        throw new Exception("Original HUD button is missing: " + name);
    }
    static bool OriginalHudLayoutRestored()
    {
        foreach (var layout in originalHudLayouts)
        {
            var rect = (RectTransform)HudOriginalButton(layout.name).transform;
            if (Vector2.Distance(rect.anchorMin, layout.anchorMin) > .0001f ||
                Vector2.Distance(rect.anchorMax, layout.anchorMax) > .0001f ||
                Vector2.Distance(rect.anchoredPosition, layout.position) > .0001f ||
                Vector2.Distance(rect.sizeDelta, layout.size) > .0001f) return false;
        }
        return true;
    }
    static int GuideButtonCount(bool activeOnly)
    {
        int count = 0;
        foreach (var button in hudRow.GetComponentsInChildren<Button>(true))
            if (button.name == "Button_Guide" && (!activeOnly || button.gameObject.activeInHierarchy)) count++;
        return count;
    }
    static void RefreshHudGuideReference()
    {
        hudGuideButton = lifecycleHudEntry.EntryButton;
        if (hudGuideButton == null) throw new Exception("GUIDE did not rebuild after its owner was enabled");
    }
    static void ValidateOriginalHudCallbacks()
    {
        Click(controlsTutorial.HelpButton);
        Require(controlsTutorial.gameObject.activeInHierarchy, "Original HELP callback works after GUIDE owner teardown");
        Click(controlsTutorial.SkipButton);
        Require(!controlsTutorial.gameObject.activeInHierarchy, "Original controls tutorial can still be dismissed");
        var pause = hudRow.GetComponentInChildren<LivingRoom30.HudPauseButton>(true);
        Require(pause != null && pause.Stopwatch != null, "Original PAUSE stopwatch wiring is preserved");
        bool running = pause.Stopwatch.IsRunning;
        Click(pause.GetComponent<Button>());
        Require(pause.Stopwatch.IsRunning != running, "Original PAUSE callback toggles the stopwatch");
        Click(pause.GetComponent<Button>());
        Require(pause.Stopwatch.IsRunning == running, "Original PAUSE callback restores its prior stopwatch state");
        var gaps = hudRow.GetComponentInChildren<LivingRoom30.HudGapsButton>(true);
        Require(gaps != null, "Original GAPS button is preserved");
        bool showing = manager.ProximityEnabled;
        Click(gaps.GetComponent<Button>());
        Require(manager.ProximityEnabled != showing, "Original GAPS callback still toggles clearance visibility");
        Click(gaps.GetComponent<Button>());
        Require(manager.ProximityEnabled == showing, "Original GAPS callback restores its prior visibility state");
    }
    static int VisibleArrowCount()
    {
        int count = 0;
        foreach (var arrow in (List<GameObject>)Field(journey, "arrows"))
            if (arrow.activeInHierarchy) count++;
        return count;
    }
    static bool RouteHidden() => !((GameObject)Field(journey, "floorVisuals")).activeInHierarchy &&
        !((Canvas)Field(journey, "targetCanvas")).gameObject.activeInHierarchy && VisibleArrowCount() == 0;
    static void Click(Button button)
    {
        if (button == null || !button.gameObject.activeInHierarchy || !button.interactable)
            throw new Exception("UI callback target is unavailable: " + (button == null ? "null" : button.name));
        button.onClick.Invoke();
    }
    static int ListenerCount(Button button)
    {
        // Read Unity's prepared invocation list so a repeated runtime AddListener is detected.
        var method = typeof(UnityEventBase).GetMethod("PrepareInvoke", BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new Exception("UnityEvent invocation list is unavailable for listener verification");
        return ((System.Collections.ICollection)method.Invoke(button.onClick, null)).Count;
    }
    static bool CardVisible()
    {
        var canvas = (Canvas)Field(journey,"panel");
        Vector3 delta = canvas.transform.position-head.transform.position;
        foreach(var hit in Physics.RaycastAll(head.transform.position,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider is CharacterController || hit.collider.transform.IsChildOf(journey.transform)) continue;
            if(hit.collider.bounds.max.y<.08f) continue;
            return false;
        }
        return true;
    }
    static object Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance);
    static void Set(object instance, string name, object value) => instance.GetType().GetField(name, BindingFlags.Instance|BindingFlags.NonPublic).SetValue(instance,value);
    static object Call(object instance, string name, params object[] values) => instance.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(instance, values);
    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("FAILED: " + message);
        checks.Add(message); Debug.Log("REVIEW_CHECK: " + message);
    }
    static void Capture(string name)
    {
        var cameraObject = new GameObject("VerificationCapture");
        var camera = cameraObject.AddComponent<Camera>(); camera.CopyFrom(head);
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.transform.SetPositionAndRotation(head.transform.position, head.transform.rotation);
        var target = new RenderTexture(1280, 800, 24);
        camera.targetTexture = target; camera.Render();
        RenderTexture old = RenderTexture.active; RenderTexture.active = target;
        var texture = new Texture2D(1280,800,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1280,800),0,0); texture.Apply();
        File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());
        RenderTexture.active = old; camera.targetTexture = null;
        UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(cameraObject);
    }
    static void Finish(bool passed)
    {
        if (finishing) return;
        finishing = true;
        requestedPass = passed;
        RestoreUiCallback();
        EditorApplication.update -= Tick;
        // Keep collecting errors through OnDisable/OnDestroy and EnteredEditMode. The HUD
        // activation regression previously escaped because logging stopped before teardown.
        EditorApplication.playModeStateChanged += OnValidationPlayModeChanged;
        EditorApplication.ExitPlaymode();
    }
    static void OnValidationPlayModeChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= OnValidationPlayModeChanged;
        bool passed = requestedPass && errors.Count == 0;
        if (passed) checks.Add("Play Mode exit lifecycle completes without logged errors");
        File.WriteAllText(Path.Combine(output,"validation-report.json"),JsonUtility.ToJson(new Report {
            passed=passed, limitation="Isolated Unity Play Mode with synthetic controller button states and real XRI hover/selection processing. Does not replace a real-headset comfort/reach test.",
            checks=checks.ToArray(), errors=errors.ToArray() },true));
        Application.logMessageReceived -= Log;
        EditorApplication.Exit(passed ? 0 : 1);
    }
}
