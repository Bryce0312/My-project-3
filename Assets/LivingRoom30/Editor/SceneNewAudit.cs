using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace LivingRoom30.Editor
{
    public static class SceneNewAudit
    {
        private const string ScenePath = "Assets/Scenes/SceneNew.unity";

        public static void RunBatch()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            List<string> failures = new List<string>();

            GameObject sceneRoot = scene.GetRootGameObjects().FirstOrDefault(item => item.name == "SceneNew_40sqm_LivingRoom");
            if (sceneRoot == null)
            {
                throw new MissingReferenceException("SceneNew root was not found.");
            }

            Transform architecture = sceneRoot.transform.Find("A00_Architecture");
            Transform furniture = sceneRoot.transform.Find("F00_Furniture");
            Transform system = sceneRoot.transform.Find("S00_SceneSystem");

            if (architecture == null || furniture == null || system == null)
            {
                throw new MissingReferenceException("SceneNew required root groups are missing.");
            }

            int environmentLayer = LayerMask.NameToLayer("Environment");
            int furnitureLayer = LayerMask.NameToLayer("Furniture");
            int playerBodyLayer = LayerMask.NameToLayer("PlayerBody");

            Collider floorCollider = architecture.Find("A01_Floor")?.GetComponent<Collider>();
            if (floorCollider == null) failures.Add("Floor collider is missing.");
            if (architecture.GetComponentsInChildren<Collider>(true).Length < 15) failures.Add("Architecture collider coverage is incomplete.");
            if (architecture.gameObject.layer != environmentLayer) failures.Add("Architecture layer is not Environment.");

            int furnitureColliderCount = 0;
            foreach (Transform item in furniture)
            {
                if (!item.name.StartsWith("F", StringComparison.Ordinal)) continue;

                if (item.GetComponent<Collider>() != null)
                {
                    failures.Add(item.name + " still has a coarse root collider.");
                }

                Collider[] childColliders = item.GetComponentsInChildren<Collider>(true);
                furnitureColliderCount += childColliders.Length;
                if (childColliders.Length == 0)
                {
                    failures.Add(item.name + " has no surface colliders.");
                }

                foreach (MeshRenderer renderer in item.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string partName = renderer.gameObject.name;
                    if (partName.StartsWith("Label_", StringComparison.Ordinal) ||
                        partName.StartsWith("Book_", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (renderer.GetComponent<Collider>() == null)
                    {
                        failures.Add(item.name + "/" + partName + " has a visible mesh without a matching collider.");
                    }
                }
            }

            if (furnitureColliderCount < 75) failures.Add("Furniture collider count is lower than expected: " + furnitureColliderCount);
            if (furniture.gameObject.layer != furnitureLayer) failures.Add("Furniture layer is not Furniture.");
            if (Physics.GetIgnoreLayerCollision(playerBodyLayer, environmentLayer)) failures.Add("PlayerBody is configured to ignore Environment collisions.");
            if (Physics.GetIgnoreLayerCollision(playerBodyLayer, furnitureLayer)) failures.Add("PlayerBody is configured to ignore Furniture collisions.");

            Transform xrRig = system.Find("S03_XR_ReviewRig");
            if (xrRig == null)
            {
                failures.Add("Reusable XR review rig is missing.");
            }
            else
            {
                CharacterController body = xrRig.GetComponentInChildren<CharacterController>(true);
                if (body == null) failures.Add("XR body CharacterController is missing.");
                else
                {
                    if (Mathf.Abs(body.height - 1.75f) > 0.001f) failures.Add("XR body height is not 1.75 m.");
                    if (Mathf.Abs(body.radius - 0.22f) > 0.001f) failures.Add("XR body radius is not 0.22 m.");
                    if (body.gameObject.layer != playerBodyLayer) failures.Add("XR body layer is not PlayerBody.");
                }

                string[] requiredTypes =
                {
                    "XROrigin",
                    "InputActionManager",
                    "CharacterControllerDriver",
                    "ActionBasedContinuousMoveProvider",
                    "ActionBasedSnapTurnProvider",
                    "LocomotionSystem",
                    "XRInteractionManager"
                };

                HashSet<string> componentTypes = xrRig.GetComponentsInChildren<Component>(true)
                    .Where(item => item != null)
                    .Select(item => item.GetType().Name)
                    .ToHashSet();

                foreach (string requiredType in requiredTypes)
                {
                    if (!componentTypes.Contains(requiredType)) failures.Add("XR component is missing: " + requiredType);
                }

                Camera xrCamera = xrRig.GetComponentsInChildren<Camera>(true).FirstOrDefault(item => item.name == "Main Camera");
                if (xrCamera == null || !xrCamera.enabled) failures.Add("XR Main Camera is missing or disabled.");

                XRBaseController[] controllers = xrRig.GetComponentsInChildren<XRBaseController>(true);
                if (controllers.Length != 2) failures.Add("Expected two XR controllers in the review rig, found " + controllers.Length + ".");

                foreach (XRBaseController controller in controllers)
                {
                    if (controller.modelPrefab == null)
                    {
                        failures.Add("XR controller has no visible model: " + controller.gameObject.name);
                    }
                }
            }

            Camera previewCamera = system.Find("S02_PreviewCamera")?.GetComponent<Camera>();
            if (previewCamera == null || previewCamera.enabled) failures.Add("Preview camera must exist and remain disabled during XR play.");
            AudioListener previewListener = system.Find("S02_PreviewCamera")?.GetComponent<AudioListener>();
            if (previewListener == null || previewListener.enabled) failures.Add("Preview AudioListener must exist and remain disabled during XR play.");

            Transform simulator = system.Find("S04_XR_DeviceSimulator_EditorOnly");
            if (simulator == null || !simulator.CompareTag("EditorOnly")) failures.Add("Editor-only XR Device Simulator is missing or incorrectly tagged.");
            else
            {
                XRDeviceSimulatorGate gate = simulator.GetComponent<XRDeviceSimulatorGate>();
                if (gate == null)
                {
                    failures.Add("XR Device Simulator has no gate; with a headset connected it would strip the real HMD from the Input System and head tracking would stop.");
                }
                else if (gate.Origin == null)
                {
                    failures.Add("XR Device Simulator gate has no XR Origin, so a desktop session would start at floor level.");
                }
            }

            // The HUD lives under the left controller, not under the system group: looking it up
            // from there makes the mounting itself part of the check.
            ActionBasedController leftController = xrRig != null
                ? xrRig.GetComponentsInChildren<ActionBasedController>(true)
                    .FirstOrDefault(item => item.gameObject.name.StartsWith("Left", StringComparison.Ordinal))
                : null;
            Transform hud = leftController != null
                ? leftController.GetComponentsInChildren<Transform>(true).FirstOrDefault(item => item.name == "S05_ReviewHud")
                : null;

            if (hud == null)
            {
                failures.Add("Review HUD is missing from the left controller, so it would not follow the wrist.");
            }
            else
            {
                Canvas hudCanvas = hud.GetComponent<Canvas>();
                if (hudCanvas == null || hudCanvas.renderMode != RenderMode.WorldSpace)
                {
                    failures.Add("Review HUD canvas is missing or is not world space.");
                }
                else if (hudCanvas.worldCamera == null)
                {
                    failures.Add("Review HUD canvas has no world camera; tracked device raycasts would silently fall back to Camera.main.");
                }

                if (hud.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                {
                    failures.Add("Review HUD canvas has no TrackedDeviceGraphicRaycaster, so the controller ray cannot reach it.");
                }

                Text hudLabel = hud.GetComponentInChildren<Text>(true);
                if (hudLabel == null || hudLabel.fontSize <= 0) failures.Add("Review HUD label is missing or has no font size.");

                if (hud.GetComponent<HudLazyFollow>() != null)
                {
                    failures.Add("Review HUD still carries HudLazyFollow; it is mounted on the wrist now and two positioning schemes would fight.");
                }

                WristPanelFacing facing = hud.GetComponent<WristPanelFacing>();
                if (facing == null || facing.HeadCamera == null)
                {
                    failures.Add("Review HUD has no WristPanelFacing wired to the head camera, so it would not be readable on the wrist.");
                }

                StopwatchHud stopwatch = hud.GetComponentInChildren<StopwatchHud>(true);
                if (stopwatch == null || stopwatch.Label == null) failures.Add("Review HUD stopwatch is not wired to its label.");

                Button pauseButton = hud.GetComponentInChildren<Button>(true);
                HudPauseButton pauseHandler = hud.GetComponentInChildren<HudPauseButton>(true);
                if (pauseButton == null || pauseHandler == null)
                {
                    failures.Add("Review HUD pause button is missing.");
                }
                else if (pauseButton.gameObject != pauseHandler.gameObject)
                {
                    failures.Add("Review HUD pause button and its handler are on different objects; the click would never reach the stopwatch.");
                }
                else if (pauseHandler.Stopwatch != stopwatch || pauseHandler.Label == null)
                {
                    failures.Add("Review HUD pause button is not wired to the stopwatch and its own label.");
                }
                else if (!pauseHandler.Label.resizeTextForBestFit)
                {
                    failures.Add("Review HUD button label is not set to best fit, so PAUSE / RESUME would wrap onto two lines.");
                }

                if (stopwatch != null && stopwatch.Label != null && stopwatch.Label.horizontalOverflow != HorizontalWrapMode.Overflow)
                {
                    failures.Add("Review HUD time label is allowed to wrap; a session past one hour would break onto two lines.");
                }

                Transform helpButton = hud.transform.Find("Button_Help");
                if (helpButton == null || helpButton.GetComponent<Button>() == null)
                {
                    failures.Add("Review HUD has no HELP button, so the tutorial could not be replayed.");
                }
            }

            Transform uiEventSystem = system.Find("S06_XR_UIEventSystem");
            if (uiEventSystem == null)
            {
                failures.Add("XR UI EventSystem is missing; the controller ray cannot drive UI.");
            }
            else
            {
                if (uiEventSystem.GetComponent<EventSystem>() == null) failures.Add("XR UI EventSystem object has no EventSystem component.");

                XRUIInputModule inputModule = uiEventSystem.GetComponent<XRUIInputModule>();
                if (inputModule == null)
                {
                    failures.Add("XR UI EventSystem object has no XRUIInputModule.");
                }
                else
                {
                    if (!inputModule.enableXRInput) failures.Add("XR UI input module has XR input disabled.");
                    if (inputModule.activeInputMode != XRUIInputModule.ActiveInputMode.InputSystemActions)
                    {
                        failures.Add("XR UI input module is not using Input System actions; this project runs with activeInputHandler set to Both.");
                    }
                }
            }

            Transform tutorial = system.Find("S07_TutorialPanel");
            if (tutorial == null)
            {
                failures.Add("Tutorial panel is missing.");
            }
            else
            {
                Canvas tutorialCanvas = tutorial.GetComponent<Canvas>();
                if (tutorialCanvas == null || tutorialCanvas.renderMode != RenderMode.WorldSpace)
                {
                    failures.Add("Tutorial canvas is missing or is not world space.");
                }
                else if (tutorialCanvas.worldCamera == null)
                {
                    failures.Add("Tutorial canvas has no world camera; tracked device raycasts would fall back to Camera.main.");
                }

                if (tutorial.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                {
                    failures.Add("Tutorial canvas has no TrackedDeviceGraphicRaycaster, so the practice button could not be clicked.");
                }

                TutorialController controller = tutorial.GetComponent<TutorialController>();
                if (controller == null)
                {
                    failures.Add("Tutorial panel has no TutorialController.");
                }
                else
                {
                    if (controller.StepLabel == null) failures.Add("Tutorial controller is not wired to its step label.");
                    if (controller.PracticeButton == null) failures.Add("Tutorial controller has no practice button.");
                    if (controller.SkipButton == null) failures.Add("Tutorial controller has no skip button.");
                    if (controller.HelpButton == null) failures.Add("Tutorial controller is not wired to the HUD HELP button.");
                    if (controller.MoveProvider == null) failures.Add("Tutorial cannot read the move provider, so step 1 would never advance.");
                    if (controller.TurnProvider == null) failures.Add("Tutorial cannot read the snap turn provider, so step 2 would never advance.");
                    if (controller.Highlighter == null) failures.Add("Tutorial controller has no ControlHighlighter, so the key highlight would never appear.");
                }

                ControlHighlighter highlighter = tutorial.GetComponent<ControlHighlighter>();
                if (highlighter == null)
                {
                    failures.Add("Tutorial panel has no ControlHighlighter.");
                }
                else
                {
                    if (highlighter.LeftController == null || highlighter.RightController == null)
                    {
                        failures.Add("ControlHighlighter is not wired to both controllers, so the key highlight would never appear.");
                    }

                    if (highlighter.LeftBeacon == null || highlighter.RightBeacon == null)
                    {
                        failures.Add("ControlHighlighter has no hand beacons, so the trigger step would have no visible cue.");
                    }

                    foreach (Transform beacon in new[] { highlighter.LeftBeacon, highlighter.RightBeacon })
                    {
                        // A collider here would block the interactor ray for anything behind the hand.
                        if (beacon != null && beacon.GetComponent<Collider>() != null)
                        {
                            failures.Add("Hand beacon has a collider, which would block the interactor ray: " + beacon.name);
                        }
                    }
                }

                foreach (Button button in tutorial.GetComponentsInChildren<Button>(true))
                {
                    Text buttonLabel = button.GetComponentInChildren<Text>(true);
                    if (buttonLabel == null)
                    {
                        continue;
                    }

                    // Best fit can only shrink to resizeTextMinSize; a shorter rect clips the text away.
                    if (buttonLabel.rectTransform.rect.height < buttonLabel.resizeTextMinSize)
                    {
                        failures.Add("Tutorial button label is shorter than its minimum font size and would be clipped: " + button.name);
                    }
                }
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("SceneNew audit failed:\n- " + string.Join("\n- ", failures));
            }

            Debug.Log(
                "SceneNew audit passed. Architecture colliders=" + architecture.GetComponentsInChildren<Collider>(true).Length +
                ", furniture surface colliders=" + furnitureColliderCount +
                ", XR rig/body/controllers/continuous movement, review HUD, XR UI event system and control tutorial present.");
        }
    }
}
