using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using LivingRoom30.Measurement;
using LivingRoom30.Review;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

public static class ReviewResultsValidation
{
    static ReviewJourneyController journey;
    static SpatialMeasureManager manager;
    static Camera head;
    static ActionBasedController hand;
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static string output, originalSession;
    static int phase;
    static double waitUntil, deadline;
    static bool pressed, finishing;
    static int inputFrame = -1;
    [Serializable] class Report { public bool passed; public string limitation; public string[] checks, errors; }
    public static void Run()
    {
        output = Path.GetFullPath("../Results"); Directory.CreateDirectory(output);
        Application.logMessageReceived += Log;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorSceneManager.OpenScene("Assets/Scenes/SceneNew.unity");
        EditorApplication.update += Tick; deadline = EditorApplication.timeSinceStartup + 100;
        EditorApplication.EnterPlaymode();
    }
    static void Log(string text, string stack, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text + "\n" + stack); }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timeout at results phase " + phase);
            if (!EditorApplication.isPlaying) return;
            if (hand != null && inputFrame != Time.frameCount)
            {
                inputFrame = Time.frameCount;
                var state = hand.currentControllerState; state.isTracked = true;
                state.inputTrackingState = InputTrackingState.Position | InputTrackingState.Rotation;
                state.position = hand.transform.localPosition; state.rotation = hand.transform.localRotation;
                state.activateInteractionState.SetFrameState(pressed); state.uiPressInteractionState.SetFrameState(pressed);
            }
            if (EditorApplication.timeSinceStartup < waitUntil) return;
            if (phase == 0)
            {
                journey = UnityEngine.Object.FindObjectOfType<ReviewJourneyController>();
                manager = UnityEngine.Object.FindObjectOfType<SpatialMeasureManager>();
                if (journey == null || !journey.IsReady) return;
                foreach (var driver in UnityEngine.Object.FindObjectsOfType<TrackedPoseDriver>(true)) driver.enabled = false;
                foreach (var simulator in UnityEngine.Object.FindObjectsOfType<XRDeviceSimulator>(true)) simulator.enabled = false;
                foreach (var controller in UnityEngine.Object.FindObjectsOfType<ActionBasedController>(true))
                { controller.enableInputActions = false; controller.enableInputTracking = false;
                  controller.transform.localPosition=new Vector3(controller.name.ToLowerInvariant().Contains("left")?-.25f:.25f,-.35f,.35f);
                  if (controller.name.ToLowerInvariant().Contains("left")) hand = controller; }
                head = (Camera)Field(manager,"xrCam");
                Set(manager,"forceDesktop",true); Set(manager,"useXrNow",true); Set(manager,"mainCam",head);
                manager.SetReviewLocomotionBlocked(false);
                journey.SessionDirectoryOverride = Path.Combine(output,"ResultSessions");
                Call(journey,"PauseGuidance");
                ValidateAnnotationsAndSummary();
                Wait(.3, 1);
            }
            else if (phase == 1)
            {
                Capture("07-review-results-points.png");
                Click("Summary_TabNotes"); Click("Summary_Row_0");
                Require(journey.Summary.IsDetailOpen && journey.Summary.PageCount > 1,"Long reasons and multiple notes are fully paginated in object details");
                Capture("08-review-results-reason.png");
                Click("Summary_BackToList");
                Require(journey.Summary.ObjectCount >= 4 && journey.Summary.PageCount >= 2,"Object groups paginate without discarding any annotated objects");
                Click("Summary_Next"); Require(journey.Summary.CurrentPage == 1,"Object list next-page button advances the page");
                Click("Summary_Close");
                Require(journey.IsSummaryMode && !journey.Summary.IsVisible,"Closing results keeps the finished session intact");
                var hud = (ReviewHudEntry)Field(journey,"hudEntry");
                Require(hud.EndButton.GetComponentInChildren<Text>().text == "RESULTS","Wrist END becomes RESULTS after finishing");
                hud.EndButton.onClick.Invoke();
                Require(journey.Summary.IsVisible && journey.LastSavedPath == originalSession,"RESULTS reopens the same saved session");
                Click("Summary_Resume");
                Require(!journey.IsSummaryMode && journey.Session.annotations.Count >= 7,"Return to review preserves every saved annotation");
                Require(journey.Seat.TrySit(),"Seated result flow can start a seat transition");
                Wait(.6,2);
            }
            else if (phase == 2)
            {
                journey.RequestFinish();
                Require(journey.IsSummaryMode && journey.Summary.IsVisible && journey.Seat.IsSeated,"Review can end seated without moving the viewer");
                Require(!((Canvas)Field(journey,"utilityCanvas")).gameObject.activeSelf,"Results replace the separate seated recovery card");
                Click("Summary_Stand"); Wait(.6,3);
            }
            else if (phase == 3)
            {
                Require(!journey.Seat.IsSeated && journey.Summary.IsVisible,"Standing inside the results card preserves the summary");
                Require(manager.XrOriginTransform.GetComponent<CharacterController>().enabled,"Summary stand restores the player capsule");
                Click("Summary_Resume");
                Transform home = (Transform)Field(journey,"remoteHome");
                hand.transform.SetPositionAndRotation(home.position + Vector3.up * .035f,Quaternion.Euler(90,0,0));
                hand.currentControllerState.position = hand.transform.localPosition;
                hand.currentControllerState.rotation = hand.transform.localRotation;
                pressed = false; Physics.SyncTransforms(); Wait(.2,31);
            }
            else if (phase == 31)
            {
                pressed = true; Wait(.8,4);
            }
            else if (phase == 4)
            {
                Require(journey.Remote.IsHeld,"Real near-hand Trigger dwell prepares held-object end validation");
                journey.RequestFinish();
                Require(!journey.IsSummaryMode && journey.Session.status == "in_progress","END is blocked while the remote is held");
                pressed = false; Wait(.3,5);
            }
            else if (phase == 5)
            {
                Require(!journey.Remote.IsHeld,"Trigger release frees the held-object finish guard");
                journey.RequestFinish();
                string saved = journey.LastSavedPath;
                int noteCount = journey.Session.annotations.Count;
                Click("Summary_Replay");
                Require(journey.LastSavedPath != saved && journey.Session.annotations.Count == 0,"NEW REVIEW creates an isolated empty session");
                var previous = JsonUtility.FromJson<ReviewJourneyController.SessionRecord>(File.ReadAllText(saved));
                Require(previous.annotations.Count == noteCount,"Starting a new review leaves the previous results file intact");
                Require(UnityEngine.Object.FindObjectsOfType<AnnotationSpawner>().Length == 23,"All 23 scene annotation connections survive summary and replay");
                journey.RequestFinish();
                Require(journey.Summary.IsVisible,"Exit Play regression runs while the final summary is still open");
                Require(journey.Summary.ActiveTab==ReviewSummaryPanel.Tab.Points,"A new session starts its result view on point reviews");
                var hud=(ReviewHudEntry)Field(journey,"hudEntry");
                var tutorial=(LivingRoom30.TutorialController)Field(journey,"controls");
                hud.enabled=false;hud.enabled=true;
                hud.Configure(tutorial.HelpButton,journey.OpenGuidance,journey.RequestFinish);
                int guides=0,ends=0;
                foreach(var button in tutorial.HelpButton.transform.parent.GetComponentsInChildren<Button>(true))
                    if(button.gameObject.activeInHierarchy){if(button.name=="Button_Guide")guides++;if(button==hud.EndButton)ends++;}
                Require(guides==1 && ends==1,"Rebuilding the extended wrist menu in one frame leaves one GUIDE and one END entry");
                Finish();
            }
        }
        catch (Exception exception) { errors.Add(exception.ToString()); Finish(); }
    }
    static void ValidateAnnotationsAndSummary()
    {
        var spawners = UnityEngine.Object.FindObjectsOfType<AnnotationSpawner>();
        var table = GameObject.Find("F04_CoffeeTable").GetComponent<AnnotationSpawner>();
        table.OnSelectEntered(null);
        var ui = AnnotationSpawner.currentActiveUI.GetComponent<AnnotationUI>();
        ui.OnSelectCategory("inappropriate size");
        Require(ui.descriptionInput != null,"The authored annotation prefab exposes a usable reason input");
        Require(!ui.TrySubmit() && !ui.IsClosing && !string.IsNullOrEmpty(ui.LastError),"Empty reasons are rejected without closing the draft");
        CategoryClick(ui,"too cramped"); ui.descriptionInput.text = "The table is too close to the sofa.";
        Require(ui.SelectedCategory == "too cramped","The existing category button is wired to actual category selection");
        SubmitClick(ui);
        Require(AnnotationSpawner.currentActiveUI == null && journey.Session.annotations.Count == 1,"The actual Submit button persists the first free-exploration note");
        originalSession = journey.LastSavedPath;
        journey.BeginReview();
        Require(journey.LastSavedPath == originalSession && journey.Session.annotations.Count == 1,"Starting the guided route keeps the pre-existing free-exploration note");
        table.OnSelectEntered(null); ui = AnnotationSpawner.currentActiveUI.GetComponent<AnnotationUI>();
        ui.OnSelectCategory("strange layout"); ui.descriptionInput.text = new string('A',480);
        string validPath = journey.LastSavedPath;
        string blockedDirectory=Path.Combine(output,"blocked-review-directory");
        File.WriteAllText(blockedDirectory,"Verification fixture: this file prevents creating a directory here.");
        Set(journey,"recordPath",Path.Combine(blockedDirectory,"review.json"));
        Require(!ui.TrySubmit() && !ui.IsClosing && ui.descriptionInput.text.Length == 480 && journey.Session.annotations.Count == 1,
            "Storage failure preserves the draft and rolls back the unsaved annotation");
        Set(journey,"recordPath",validPath);
        Require(ui.TrySubmit(),"The preserved draft can be submitted after storage recovers");
        Require(journey.Session.annotations[1].pointId == "Entry","Notes submitted from a review point retain its point association");
        string error;
        int added=0;
        foreach(var spawner in spawners)
        {
            if(spawner==table)continue;
            Require(ReviewAnnotationStore.Active.TrySubmit(spawner.gameObject,"inappropriate size","A further distinct-object reason " + added,out error),"Additional object note is saved " + added);
            if(++added==3)break;
        }
        Require(ReviewAnnotationStore.Active.TrySubmit(table.gameObject,"inappropriate size","Another reason for the same table.",out error),"A second category for the same object is retained");
        table.OnSelectEntered(null); ui = AnnotationSpawner.currentActiveUI.GetComponent<AnnotationUI>();
        ui.OnSelectCategory("too cramped"); ui.descriptionInput.text = "This draft must survive END.";
        journey.RequestFinish();
        Require(ui.IsFinishing && !journey.IsSummaryMode && ui.descriptionInput.text.Contains("survive"),"END offers a finish choice without discarding the current draft");
        ui.KeepEditing();
        Require(!ui.IsFinishing && ui.descriptionInput.text.Contains("survive"),"KEEP EDITING preserves category and reason");
        journey.RequestFinish(); SubmitClick(ui);
        Require(journey.IsSummaryMode && journey.Summary.IsVisible,"SAVE & END saves the draft before opening results");
        Require(journey.Summary.PointCount == 6,"Early end lists all six review points, including unanswered ones");
        var points = (List<ReviewSummaryPanel.PointRow>)Field(journey.Summary,"points");
        Require(points.TrueForAll(point=>point.answer == "UNRATED"),"Unanswered points are explicitly UNRATED instead of invented evaluations");
        var saved = JsonUtility.FromJson<ReviewJourneyController.SessionRecord>(File.ReadAllText(journey.LastSavedPath));
        Require(saved.schemaVersion == 2 && saved.status == "ended_early" && saved.annotations.Count == journey.Session.annotations.Count,
            "The ended JSON contains both evaluation and tagged annotation data");
        Require(saved.annotations[0].category == "too cramped" && saved.annotations[0].reason.Contains("sofa"),"Saved category and reason match the submitted object note");
        Require(!((Canvas)Field(journey,"panel")).gameObject.activeSelf && !((Canvas)Field(journey,"targetCanvas")).gameObject.activeSelf,
            "Results replace the original task card and target beacon");
        table.OnSelectEntered(null); Require(AnnotationSpawner.currentActiveUI == null,"Read-only results do not spawn a competing annotation editor");
    }
    static void CategoryClick(AnnotationUI ui,string category)
    {
        foreach(var b in ui.GetComponentsInChildren<Button>(true))
        { var t=b.GetComponentInChildren<TextMeshProUGUI>(); if(t!=null && t.text == category) {b.onClick.Invoke();return;} }
        throw new Exception("Missing category button " + category);
    }
    static void SubmitClick(AnnotationUI ui)
    { var b=ui.submitButtonText.GetComponentInParent<Button>(); Require(b!=null,"The submit label belongs to an interactive button"); b.onClick.Invoke(); }
    static void Click(string name)
    {
        Button button=null;
        foreach(var candidate in journey.Summary.Canvas.GetComponentsInChildren<Button>(true)) if(candidate.name==name){button=candidate;break;}
        Require(button!=null && button.gameObject.activeInHierarchy && button.interactable,"Summary control is available: " + name);
        button.onClick.Invoke();
    }
    static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(value);
    static void Set(object value,string name,object data)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(value,data);
    static object Call(object value,string name,params object[] data)=>value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,data);
    static void Wait(double seconds,int next){waitUntil=EditorApplication.timeSinceStartup+seconds;phase=next;}
    static void Require(bool value,string message){if(!value)throw new Exception("FAILED: "+message);checks.Add(message);Debug.Log("RESULT_CHECK: "+message);}
    static void Capture(string name)
    {
        var cameraObject=new GameObject("ResultsCapture");var camera=cameraObject.AddComponent<Camera>();camera.CopyFrom(head);
        camera.stereoTargetEye=StereoTargetEyeMask.None;camera.transform.SetPositionAndRotation(head.transform.position,head.transform.rotation);
        var target=new RenderTexture(1280,800,24);camera.targetTexture=target;camera.Render();var old=RenderTexture.active;RenderTexture.active=target;
        var texture=new Texture2D(1280,800,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,800),0,0);texture.Apply();
        File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());RenderTexture.active=old;UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(target);UnityEngine.Object.Destroy(cameraObject);
    }
    static void Finish()
    {
        if(finishing)return;finishing=true;EditorApplication.update-=Tick;
        EditorApplication.playModeStateChanged+=state=>{if(state!=PlayModeStateChange.EnteredEditMode)return;
            File.WriteAllText(Path.Combine(output,"results-validation-report.json"),JsonUtility.ToJson(new Report {passed=errors.Count==0,
                limitation="Isolated Unity with real annotation prefab callbacks, synthetic controller state and full Play Mode teardown. Hardware comfort and hit accuracy still require headset testing.",
                checks=checks.ToArray(),errors=errors.ToArray()},true));Application.logMessageReceived-=Log;EditorApplication.Exit(errors.Count==0?0:1);};
        EditorApplication.ExitPlaymode();
    }
}
