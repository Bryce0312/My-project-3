using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class XRFurnitureCollisionBuilder
{
    private const string ScenePath = "Assets/Scenes/SampleScene_edit.unity";
    private const string ModelRootName = "VLOGGER'S APARTMENT (FBX)";
    private const string RequestFileName = "XR_BUILD_FURNITURE_COLLISION.request";

    private static readonly string[] FixedCabinetNames =
    {
        "Box2138775881", "Group2146513366", "Group2146513367"
    };

    private static readonly string[] SofaMeshNames =
    {
        "crp_Koltuk minotti", "Object2113134102"
    };

    private static readonly string[] SofaBoxNames =
    {
        "Group007", "Group2146513355", "Group004", "Group005"
    };

    private static readonly string[] CoffeeTableNames =
    {
        "blotches", "Minotti_Lawrence Sofa 5_052"
    };

    private static readonly string[] DiningChairNames =
    {
        "ChamferCyl008", "ChamferCyl009", "ChamferCyl010", "ChamferCyl011", "ChamferCyl017",
        "lenox_004", "lenox_005", "lenox_006", "lenox_007"
    };

    static XRFurnitureCollisionBuilder()
    {
        EditorApplication.delayCall += RunIfRequested;
    }

    [MenuItem("Tools/XR Design Review/Build Furniture Colliders")]
    public static void BuildFromMenu()
    {
        BuildFurnitureColliders();
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
            BuildFurnitureColliders();
            File.Delete(requestPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void BuildFurnitureColliders()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForTask = !scene.IsValid() || !scene.isLoaded;
        if (openedForTask)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        GameObject modelRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name == ModelRootName);
        GameObject collisionRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name == "XR_Collision");
        if (modelRoot == null || collisionRoot == null)
            throw new InvalidOperationException("Model root or XR_Collision root is missing.");

        Transform oldFurniture = collisionRoot.transform.Find("COL_Furniture");
        if (oldFurniture != null)
            UnityEngine.Object.DestroyImmediate(oldFurniture.gameObject);

        GameObject furnitureRoot = CreateGroup("COL_Furniture", collisionRoot.transform);
        GameObject fixedRoot = CreateGroup("COL_Fixed_Kitchen", furnitureRoot.transform);
        GameObject sofaRoot = CreateGroup("COL_Sofas_And_Seats", furnitureRoot.transform);
        GameObject coffeeRoot = CreateGroup("COL_Coffee_And_Side_Tables", furnitureRoot.transform);
        GameObject diningTableRoot = CreateGroup("COL_Dining_Table", furnitureRoot.transform);
        GameObject diningChairRoot = CreateGroup("COL_Dining_Chairs", furnitureRoot.transform);

        List<Record> records = new List<Record>();

        Transform island = Find(modelRoot.transform, "Object2113134037");
        AddBox(island, fixedRoot.transform, "COL_Island", records);
        AddCombinedBox(FixedCabinetNames.Select(name => Find(modelRoot.transform, name)).Where(value => value != null),
            fixedRoot.transform, "COL_TallCabinets", records);

        foreach (string name in SofaMeshNames)
            AddFootprintBoxes(Find(modelRoot.transform, name), sofaRoot.transform, "COL_Sofa", records);
        foreach (string name in SofaBoxNames)
            AddBox(Find(modelRoot.transform, name), sofaRoot.transform, "COL_Seat", records);

        foreach (string name in CoffeeTableNames)
            AddBox(Find(modelRoot.transform, name), coffeeRoot.transform, "COL_Table", records);

        AddBox(Find(modelRoot.transform, "Object2113133998"), diningTableRoot.transform, "COL_DiningTable", records);
        foreach (string name in DiningChairNames)
            AddBox(Find(modelRoot.transform, name), diningChairRoot.transform, "COL_Chair", records);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Unity could not save " + ScenePath);

        WriteReport(records);
        Debug.Log("XR furniture collision complete. Proxies: " + records.Count + ", scene: " + ScenePath);

        if (openedForTask)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static void AddBox(Transform source, Transform parent, string prefix, List<Record> records)
    {
        if (source == null)
            return;
        Renderer[] renderers = source.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;
        Bounds bounds = CombinedBounds(renderers);
        GameObject proxy = new GameObject(prefix + "_" + Sanitize(source.name));
        proxy.transform.SetParent(parent, false);
        proxy.transform.position = bounds.center;
        BoxCollider collider = proxy.AddComponent<BoxCollider>();
        collider.size = bounds.size;
        records.Add(new Record(proxy.name, PathOf(source), "BoxCollider", bounds.center, bounds.size, 0));
    }

    private static void AddCombinedBox(IEnumerable<Transform> sources, Transform parent, string name, List<Record> records)
    {
        Renderer[] renderers = sources.SelectMany(source => source.GetComponentsInChildren<Renderer>(true)).ToArray();
        if (renderers.Length == 0)
            return;
        Bounds bounds = CombinedBounds(renderers);
        GameObject proxy = new GameObject(name);
        proxy.transform.SetParent(parent, false);
        proxy.transform.position = bounds.center;
        BoxCollider collider = proxy.AddComponent<BoxCollider>();
        collider.size = bounds.size;
        records.Add(new Record(proxy.name, "combined fixed cabinet group", "BoxCollider", bounds.center, bounds.size, 0));
    }

    private static void AddFootprintBoxes(Transform source, Transform parent, string prefix, List<Record> records)
    {
        if (source == null)
            return;
        MeshFilter[] filters = source.GetComponentsInChildren<MeshFilter>(true)
            .Where(filter => filter.sharedMesh != null && filter.GetComponent<Renderer>() != null)
            .ToArray();
        foreach (MeshFilter filter in filters)
        {
            CreateFootprintBoxes(filter, source.name, parent, prefix, records);
        }
    }

    private static void CreateFootprintBoxes(MeshFilter filter, string sourceName, Transform parent, string prefix,
        List<Record> records)
    {
        Renderer renderer = filter.GetComponent<Renderer>();
        Bounds bounds = renderer.bounds;
        const float targetCellSize = 0.28f;
        int countX = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / targetCellSize));
        int countZ = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / targetCellSize));
        float stepX = bounds.size.x / countX;
        float stepZ = bounds.size.z / countZ;
        bool[,] occupied = new bool[countX, countZ];

        GameObject temporary = new GameObject("TEMP_SofaColliderSampler") { hideFlags = HideFlags.HideAndDontSave };
        temporary.transform.position = filter.transform.position;
        temporary.transform.rotation = filter.transform.rotation;
        temporary.transform.localScale = filter.transform.lossyScale;
        MeshCollider sampler = temporary.AddComponent<MeshCollider>();
        sampler.sharedMesh = filter.sharedMesh;
        sampler.convex = false;
        Physics.SyncTransforms();

        float rayDistance = bounds.size.y + 1f;
        for (int z = 0; z < countZ; z++)
        {
            for (int x = 0; x < countX; x++)
            {
                float worldX = bounds.min.x + (x + 0.5f) * stepX;
                float worldZ = bounds.min.z + (z + 0.5f) * stepZ;
                RaycastHit hit;
                bool hitFromAbove = sampler.Raycast(
                    new Ray(new Vector3(worldX, bounds.max.y + 0.25f, worldZ), Vector3.down), out hit, rayDistance);
                bool hitFromBelow = sampler.Raycast(
                    new Ray(new Vector3(worldX, bounds.min.y - 0.25f, worldZ), Vector3.up), out hit, rayDistance);
                occupied[x, z] = hitFromAbove || hitFromBelow;
            }
        }

        UnityEngine.Object.DestroyImmediate(temporary);

        bool[,] used = new bool[countX, countZ];
        int generated = 0;
        for (int z = 0; z < countZ; z++)
        {
            for (int x = 0; x < countX; x++)
            {
                if (!occupied[x, z] || used[x, z])
                    continue;

                int width = 1;
                while (x + width < countX && occupied[x + width, z] && !used[x + width, z])
                    width++;

                int depth = 1;
                bool canExtend = true;
                while (z + depth < countZ && canExtend)
                {
                    for (int testX = x; testX < x + width; testX++)
                    {
                        if (!occupied[testX, z + depth] || used[testX, z + depth])
                        {
                            canExtend = false;
                            break;
                        }
                    }
                    if (canExtend)
                        depth++;
                }

                for (int markZ = z; markZ < z + depth; markZ++)
                    for (int markX = x; markX < x + width; markX++)
                        used[markX, markZ] = true;

                Vector3 size = new Vector3(width * stepX + 0.03f, bounds.size.y, depth * stepZ + 0.03f);
                Vector3 center = new Vector3(
                    bounds.min.x + (x + width * 0.5f) * stepX,
                    bounds.center.y,
                    bounds.min.z + (z + depth * 0.5f) * stepZ);
                string proxyName = prefix + "_" + Sanitize(sourceName) + "_Part" + (++generated).ToString("00");
                CreateBoxProxy(proxyName, PathOf(filter.transform), center, size, parent, records);
            }
        }

        if (generated == 0)
            CreateBoxProxy(prefix + "_" + Sanitize(sourceName) + "_Fallback", PathOf(filter.transform),
                bounds.center, bounds.size, parent, records);
    }

    private static void CreateBoxProxy(string name, string source, Vector3 center, Vector3 size, Transform parent,
        List<Record> records)
    {
        GameObject proxy = new GameObject(name);
        proxy.transform.SetParent(parent, false);
        proxy.transform.position = center;
        BoxCollider collider = proxy.AddComponent<BoxCollider>();
        collider.size = size;
        records.Add(new Record(proxy.name, source, "Compound BoxCollider", center, size, 0));
    }

    private static Transform Find(Transform root, string name)
    {
        return root.GetComponentsInChildren<Transform>(true).FirstOrDefault(transform => transform.name == name);
    }

    private static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static GameObject CreateGroup(string name, Transform parent)
    {
        GameObject group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group;
    }

    private static string Sanitize(string value)
    {
        return value.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
    }

    private static string PathOf(Transform transform)
    {
        return transform.parent == null ? transform.name : PathOf(transform.parent) + "/" + transform.name;
    }

    private static void WriteReport(List<Record> records)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string reportDirectory = Path.Combine(projectRoot, "Tools", "xr-review");
        Directory.CreateDirectory(reportDirectory);
        string reportPath = Path.Combine(reportDirectory, "furniture-collision-report.txt");
        using (StreamWriter writer = new StreamWriter(reportPath, false))
        {
            writer.WriteLine("Scene: " + ScenePath);
            writer.WriteLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            writer.WriteLine("Furniture proxies: " + records.Count);
            writer.WriteLine("No Rigidbody components were added.");
            writer.WriteLine();
            foreach (Record record in records)
                writer.WriteLine(record.name + " | " + record.type + " | source=" + record.source +
                                 " | center=" + record.center.ToString("F3") + " | size=" + record.size.ToString("F3") +
                                 (record.triangles > 0 ? " | triangles=" + record.triangles : string.Empty));
        }
    }

    private sealed class Record
    {
        public readonly string name;
        public readonly string source;
        public readonly string type;
        public readonly Vector3 center;
        public readonly Vector3 size;
        public readonly int triangles;

        public Record(string name, string source, string type, Vector3 center, Vector3 size, int triangles)
        {
            this.name = name;
            this.source = source;
            this.type = type;
            this.center = center;
            this.size = size;
            this.triangles = triangles;
        }
    }
}
