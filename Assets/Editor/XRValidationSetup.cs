using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class XRValidationSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene_edit.unity";
    private const string RequestFileName = "XR_SETUP_VALIDATION.request";

    private static readonly LayerDefinition[] Layers =
    {
        new LayerDefinition(8, "Environment"),
        new LayerDefinition(9, "Furniture"),
        new LayerDefinition(10, "Interactable"),
        new LayerDefinition(11, "Teleport"),
        new LayerDefinition(12, "PlayerBody"),
        new LayerDefinition(13, "SafetyTrigger")
    };

    static XRValidationSetup()
    {
        EditorApplication.delayCall += RunIfRequested;
    }

    [MenuItem("Tools/XR Design Review/Set Up Layers And Clearance Probe")]
    public static void SetupFromMenu()
    {
        Setup();
    }

    private static void RunIfRequested()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string requestPath = Path.Combine(projectRoot, RequestFileName);
        if (!File.Exists(requestPath) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        try
        {
            Setup();
            File.Delete(requestPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void Setup()
    {
        EnsureLayers();

        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForTask = !scene.IsValid() || !scene.isLoaded;
        if (openedForTask)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        GameObject collisionRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "XR_Collision");
        GameObject modelRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "VLOGGER'S APARTMENT (FBX)");
        if (collisionRoot == null || modelRoot == null)
            throw new InvalidOperationException("XR_Collision or apartment model root is missing.");

        SetLayerByPath(collisionRoot.transform, "COL_Floors", "Environment");
        SetLayerByPath(collisionRoot.transform, "COL_Walls", "Environment");
        SetLayerByPath(collisionRoot.transform, "COL_WindowBoundaries", "Environment");
        SetLayerByPath(collisionRoot.transform, "COL_Furniture", "Furniture");

        SetLayerByPath(modelRoot.transform, "01_Architecture_Static", "Environment");
        SetLayerByPath(modelRoot.transform, "02_Furniture_Fixed", "Furniture");
        SetLayerByPath(modelRoot.transform, "03_Furniture_Movable_Candidates", "Furniture");
        SetLayerByPath(modelRoot.transform, "04_Small_Interactable_Candidates", "Interactable");

        GameObject existingValidation = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "XR_Validation");
        if (existingValidation != null)
            UnityEngine.Object.DestroyImmediate(existingValidation);

        float floorSurface = FindFloorSurface(collisionRoot.transform);
        Vector3 spawn = FindClearSpawn(floorSurface);

        GameObject validationRoot = new GameObject("XR_Validation");
        SceneManager.MoveGameObjectToScene(validationRoot, scene);
        GameObject probe = new GameObject();
        probe.name = "Player_Clearance_Probe_1.75m_R0.22m";
        probe.transform.SetParent(validationRoot.transform, false);
        probe.transform.position = spawn;
        probe.layer = LayerMask.NameToLayer("PlayerBody");

        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Probe_Visual";
        visual.transform.SetParent(probe.transform, false);
        visual.transform.localPosition = new Vector3(0f, 0.875f, 0f);
        visual.transform.localScale = new Vector3(0.44f, 0.875f, 0.44f);
        visual.layer = probe.layer;
        CapsuleCollider primitiveCollider = visual.GetComponent<CapsuleCollider>();
        if (primitiveCollider != null)
            UnityEngine.Object.DestroyImmediate(primitiveCollider);

        CharacterController controller = probe.AddComponent<CharacterController>();
        controller.height = 1.75f;
        controller.radius = 0.22f;
        controller.center = new Vector3(0f, 0.875f, 0f);
        controller.skinWidth = 0.02f;
        controller.stepOffset = 0.25f;
        controller.slopeLimit = 45f;

        GameObject cameraObject = new GameObject("Validation_Camera_1.65m");
        cameraObject.transform.SetParent(probe.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        Camera validationCamera = cameraObject.AddComponent<Camera>();
        validationCamera.enabled = false;
        AudioListener listener = cameraObject.AddComponent<AudioListener>();
        listener.enabled = false;

        XRDesktopClearanceController movement = probe.AddComponent<XRDesktopClearanceController>();
        movement.SetValidationCamera(validationCamera);
        movement.enabled = false;

        validationRoot.SetActive(false);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Unity could not save " + ScenePath);

        WriteReport(floorSurface, spawn);
        Debug.Log("XR validation setup complete. Layers configured; clearance probe at " + spawn + ".");

        if (openedForTask)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static void EnsureLayers()
    {
        UnityEngine.Object tagManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
        SerializedObject serialized = new SerializedObject(tagManager);
        SerializedProperty layers = serialized.FindProperty("layers");

        foreach (LayerDefinition definition in Layers)
        {
            SerializedProperty slot = layers.GetArrayElementAtIndex(definition.index);
            if (!string.IsNullOrEmpty(slot.stringValue) && slot.stringValue != definition.name)
                throw new InvalidOperationException("Layer " + definition.index + " is already used by " + slot.stringValue);
            slot.stringValue = definition.name;
        }

        serialized.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }

    private static void SetLayerByPath(Transform root, string childName, string layerName)
    {
        Transform child = root.Find(childName);
        if (child == null)
            return;
        int layer = LayerMask.NameToLayer(layerName);
        foreach (Transform transform in child.GetComponentsInChildren<Transform>(true))
            transform.gameObject.layer = layer;
    }

    private static float FindFloorSurface(Transform collisionRoot)
    {
        Transform floorGroup = collisionRoot.Find("COL_Floors");
        BoxCollider floor = floorGroup == null ? null : floorGroup.GetComponentInChildren<BoxCollider>(true);
        if (floor == null)
            throw new InvalidOperationException("Review floor collider is missing.");
        return floor.bounds.max.y;
    }

    private static Vector3 FindClearSpawn(float floorSurface)
    {
        const float radius = 0.22f;
        const float height = 1.75f;
        Vector3 desired = new Vector3(-24.5f, floorSurface, -4.0f);
        int mask = LayerMask.GetMask("Environment", "Furniture");

        for (int ring = 0; ring <= 12; ring++)
        {
            for (int z = -ring; z <= ring; z++)
            {
                for (int x = -ring; x <= ring; x++)
                {
                    if (ring > 0 && Mathf.Abs(x) != ring && Mathf.Abs(z) != ring)
                        continue;
                    Vector3 candidate = desired + new Vector3(x * 0.4f, 0f, z * 0.4f);
                    Vector3 bottom = candidate + Vector3.up * (radius + 0.03f);
                    Vector3 top = candidate + Vector3.up * (height - radius + 0.03f);
                    if (!Physics.CheckCapsule(bottom, top, radius, mask, QueryTriggerInteraction.Ignore))
                        return candidate;
                }
            }
        }

        throw new InvalidOperationException("Could not find a clear validation spawn near the living room.");
    }

    private static void WriteReport(float floorSurface, Vector3 spawn)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string directory = Path.Combine(projectRoot, "Tools", "xr-review");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "validation-setup-report.txt"),
            "Scene: " + ScenePath + Environment.NewLine +
            "Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
            "Scale: 1 Unity unit = 1 metre" + Environment.NewLine +
            "Review floor surface Y: " + floorSurface.ToString("F3") + Environment.NewLine +
            "Probe height: 1.750" + Environment.NewLine +
            "Probe radius: 0.220" + Environment.NewLine +
            "Probe spawn: " + spawn.ToString("F3") + Environment.NewLine +
            "Validation root active: False" + Environment.NewLine +
            "Validation camera active: False" + Environment.NewLine);
    }

    private readonly struct LayerDefinition
    {
        public readonly int index;
        public readonly string name;

        public LayerDefinition(int index, string name)
        {
            this.index = index;
            this.name = name;
        }
    }
}
