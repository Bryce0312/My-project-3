using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
            }

            Camera previewCamera = system.Find("S02_PreviewCamera")?.GetComponent<Camera>();
            if (previewCamera == null || previewCamera.enabled) failures.Add("Preview camera must exist and remain disabled during XR play.");
            AudioListener previewListener = system.Find("S02_PreviewCamera")?.GetComponent<AudioListener>();
            if (previewListener == null || previewListener.enabled) failures.Add("Preview AudioListener must exist and remain disabled during XR play.");

            Transform simulator = system.Find("S04_XR_DeviceSimulator_EditorOnly");
            if (simulator == null || !simulator.CompareTag("EditorOnly")) failures.Add("Editor-only XR Device Simulator is missing or incorrectly tagged.");

            if (failures.Count > 0)
            {
                throw new InvalidOperationException("SceneNew audit failed:\n- " + string.Join("\n- ", failures));
            }

            Debug.Log(
                "SceneNew audit passed. Architecture colliders=" + architecture.GetComponentsInChildren<Collider>(true).Length +
                ", furniture surface colliders=" + furnitureColliderCount +
                ", XR rig/body/continuous movement present.");
        }
    }
}
