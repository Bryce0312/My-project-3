using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class XREnvironmentCollisionBuilder
{
    private const string ScenePath = "Assets/Scenes/SampleScene_edit.unity";
    private const string ModelRootName = "VLOGGER'S APARTMENT (FBX)";
    private const string RequestFileName = "XR_BUILD_ENV_COLLISION.request";
    private const string ClassificationRequestFileName = "XR_CLASSIFY_SCENE.request";
    private const string CollisionRootName = "XR_Collision";

    static XREnvironmentCollisionBuilder()
    {
        EditorApplication.delayCall += RunIfRequested;
    }

    [MenuItem("Tools/XR Design Review/Build Floor and Wall Colliders")]
    public static void BuildFromMenu()
    {
        BuildColliders();
    }

    private static void RunIfRequested()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string requestPath = Path.Combine(projectRoot, RequestFileName);
        if (!File.Exists(requestPath) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        try
        {
            XRSceneClassifier.ClassifyFromMenu();
            BuildColliders();
            File.Delete(requestPath);

            string classificationRequest = Path.Combine(projectRoot, ClassificationRequestFileName);
            if (File.Exists(classificationRequest))
                File.Delete(classificationRequest);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void BuildColliders()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForTask = !scene.IsValid() || !scene.isLoaded;
        if (openedForTask)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        GameObject modelRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name == ModelRootName);
        if (modelRoot == null)
            throw new InvalidOperationException("Could not find model root: " + ModelRootName);

        Transform architecture = modelRoot.transform.Find("01_Architecture_Static");
        if (architecture == null)
            throw new InvalidOperationException("Run scene classification before building collision proxies.");

        Renderer[] allModelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        if (allModelRenderers.Length == 0)
            throw new InvalidOperationException("The apartment model has no renderers.");

        Bounds modelBounds = CombinedBounds(allModelRenderers);
        if (modelBounds.size.y < 2f || modelBounds.size.y > 25f)
            throw new InvalidOperationException("Model scale requires review before collision generation. Bounds: " + modelBounds.size);

        GameObject oldCollisionRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name == CollisionRootName);
        if (oldCollisionRoot != null)
            UnityEngine.Object.DestroyImmediate(oldCollisionRoot);

        GameObject collisionRoot = new GameObject(CollisionRootName);
        SceneManager.MoveGameObjectToScene(collisionRoot, scene);
        GameObject floorRoot = CreateGroup("COL_Floors", collisionRoot.transform);
        GameObject wallRoot = CreateGroup("COL_Walls", collisionRoot.transform);

        List<ProxyRecord> floorRecords = new List<ProxyRecord>();
        List<ProxyRecord> wallRecords = new List<ProxyRecord>();
        HashSet<string> signatures = new HashSet<string>();

        Renderer[] architectureRenderers = architecture.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in architectureRenderers)
        {
            Bounds bounds = renderer.bounds;
            if (!IsFinite(bounds) || bounds.size.sqrMagnitude < 0.0001f)
                continue;

            bool floorCandidate = IsFloor(renderer, bounds, modelBounds);
            bool wallCandidate = IsWall(bounds);
            if (!floorCandidate && !wallCandidate)
                continue;

            string signature = Signature(bounds);
            if (!signatures.Add(signature))
                continue;

            if (floorCandidate)
                floorRecords.Add(CreateProxy(renderer, bounds, floorRoot.transform, "COL_Floor", 0.08f));
            else
                wallRecords.Add(CreateProxy(renderer, bounds, wallRoot.transform, "COL_Wall", 0.12f));
        }

        BoxCollider importedRootCollider = modelRoot.GetComponent<BoxCollider>();
        if (importedRootCollider != null)
            importedRootCollider.enabled = false;

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Unity could not save " + ScenePath);

        WriteReport(modelBounds, floorRecords, wallRecords, importedRootCollider != null);
        Debug.Log("XR collision proxies complete. Floors: " + floorRecords.Count +
                  ", walls: " + wallRecords.Count + ", scene: " + ScenePath);

        if (openedForTask)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static bool IsFloor(Renderer renderer, Bounds bounds, Bounds modelBounds)
    {
        Vector3 size = bounds.size;
        bool broadHorizontalSurface = size.y <= 0.45f && size.x >= 2f && size.z >= 2f;
        float modelArea = modelBounds.size.x * modelBounds.size.z;
        float surfaceArea = size.x * size.z;
        bool dominantLowerSlab = surfaceArea >= modelArea * 0.5f && bounds.center.y < modelBounds.center.y;
        bool floorMaterial = renderer.sharedMaterials.Any(material =>
            material != null && material.name.ToLowerInvariant().Contains("zem"));
        return broadHorizontalSurface && (dominantLowerSlab || floorMaterial);
    }

    private static bool IsWall(Bounds bounds)
    {
        Vector3 size = bounds.size;
        bool wallAlongX = size.y >= 2.1f && size.x >= 1.5f && size.z <= 0.65f;
        bool wallAlongZ = size.y >= 2.1f && size.z >= 1.5f && size.x <= 0.65f;
        return wallAlongX || wallAlongZ;
    }

    private static ProxyRecord CreateProxy(Renderer source, Bounds bounds, Transform parent, string prefix, float minimumThickness)
    {
        string safeName = Sanitize(source.gameObject.name);
        int index = parent.childCount + 1;
        GameObject proxy = new GameObject(prefix + "_" + index.ToString("000") + "_" + safeName);
        proxy.transform.SetParent(parent, false);
        proxy.transform.position = bounds.center;

        Vector3 size = bounds.size;
        size.x = Mathf.Max(size.x, minimumThickness);
        size.y = Mathf.Max(size.y, minimumThickness);
        size.z = Mathf.Max(size.z, minimumThickness);

        BoxCollider collider = proxy.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = size;
        collider.isTrigger = false;

        return new ProxyRecord
        {
            proxy = proxy.name,
            source = HierarchyPath(source.transform),
            center = bounds.center,
            size = size
        };
    }

    private static GameObject CreateGroup(string name, Transform parent)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group;
    }

    private static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static bool IsFinite(Bounds bounds)
    {
        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        return IsFinite(center.x) && IsFinite(center.y) && IsFinite(center.z) &&
               IsFinite(size.x) && IsFinite(size.y) && IsFinite(size.z);
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static string Signature(Bounds bounds)
    {
        Vector3 c = bounds.center;
        Vector3 s = bounds.size;
        return Math.Round(c.x, 2) + "|" + Math.Round(c.y, 2) + "|" + Math.Round(c.z, 2) + "|" +
               Math.Round(s.x, 2) + "|" + Math.Round(s.y, 2) + "|" + Math.Round(s.z, 2);
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new string(value.Where(character => !invalid.Contains(character)).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "Unnamed" : cleaned;
    }

    private static string HierarchyPath(Transform transform)
    {
        return transform.parent == null ? transform.name : HierarchyPath(transform.parent) + "/" + transform.name;
    }

    private static void WriteReport(Bounds modelBounds, List<ProxyRecord> floors, List<ProxyRecord> walls, bool disabledImportedCollider)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string reportDirectory = Path.Combine(projectRoot, "Tools", "xr-review");
        Directory.CreateDirectory(reportDirectory);
        string reportPath = Path.Combine(reportDirectory, "environment-collision-report.txt");

        using (StreamWriter writer = new StreamWriter(reportPath, false))
        {
            writer.WriteLine("Scene: " + ScenePath);
            writer.WriteLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            writer.WriteLine("Model center: " + modelBounds.center.ToString("F3"));
            writer.WriteLine("Model size: " + modelBounds.size.ToString("F3"));
            writer.WriteLine("Imported root BoxCollider disabled: " + disabledImportedCollider);
            writer.WriteLine("Floor proxies: " + floors.Count);
            writer.WriteLine("Wall proxies: " + walls.Count);
            writer.WriteLine();

            WriteRecords(writer, "FLOORS", floors);
            WriteRecords(writer, "WALLS", walls);
        }
    }

    private static void WriteRecords(StreamWriter writer, string title, IEnumerable<ProxyRecord> records)
    {
        writer.WriteLine(title);
        foreach (ProxyRecord record in records)
            writer.WriteLine(record.proxy + " | " + record.source + " | center=" + record.center.ToString("F3") + " | size=" + record.size.ToString("F3"));
        writer.WriteLine();
    }

    private sealed class ProxyRecord
    {
        public string proxy;
        public string source;
        public Vector3 center;
        public Vector3 size;
    }
}
