using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class XRSceneClassifier
{
    private const string ScenePath = "Assets/Scenes/SampleScene_edit.unity";
    private const string ModelRootName = "VLOGGER'S APARTMENT (FBX)";
    private const string RequestFileName = "XR_CLASSIFY_SCENE.request";

    private static readonly string[] CategoryNames =
    {
        "01_Architecture_Static",
        "02_Furniture_Fixed",
        "03_Furniture_Movable_Candidates",
        "04_Small_Interactable_Candidates",
        "05_Decor_Static",
        "06_Lighting_Helpers"
    };

    private static readonly string[] ArchitectureWords =
    {
        "wall", "floor", "ceiling", "door", "window", "frame", "baseboard",
        "skirting", "column", "beam", "stairs", "step", "zemin", "tavan",
        "duvar", "kapi", "nalichnik", "arch", "room", "apartment", "destek"
    };

    private static readonly string[] FixedFurnitureWords =
    {
        "island", "cabinet", "counter", "kitchen", "mutfak", "raf", "shelf",
        "wardrobe", "closet", "tv raf", "built-in", "built in", "console"
    };

    private static readonly string[] MovableFurnitureWords =
    {
        "sofa", "chair", "armchair", "table", "ottoman", "stool", "bench",
        "seat", "coffee", "minotti", "koltuk", "sandalye", "masa", "lamp"
    };

    private static readonly string[] SmallInteractableWords =
    {
        "glass", "cup", "mug", "plate", "fork", "knife", "spoon", "napkin",
        "book", "remote", "bowl", "bottle", "vase", "tray", "candle", "pillow",
        "cushion", "phone", "magazine"
    };

    private static readonly string[] DecorWords =
    {
        "curtain", "carpet", "rug", "pampas", "plant", "picture", "painting",
        "art", "decor", "sculpture", "ornament"
    };

    private static readonly string[] LightFixtureWords =
    {
        "pendant", "chand", "wall light", "ceiling light"
    };

    private static readonly string[] ExactFixedFurnitureNames =
    {
        "object2113134037", "box2138775881", "group2146513366", "group2146513367"
    };

    private static readonly string[] ExactMovableFurnitureNames =
    {
        "object2113133998", "object2113134102", "blotches", "group004", "group005",
        "chamfercyl008", "chamfercyl009", "chamfercyl010", "chamfercyl011", "chamfercyl017",
        "lenox_004", "lenox_005", "lenox_006", "lenox_007"
    };

    static XRSceneClassifier()
    {
        EditorApplication.delayCall += RunIfRequested;
    }

    [MenuItem("Tools/XR Design Review/Classify SampleScene Edit")]
    public static void ClassifyFromMenu()
    {
        ClassifyScene();
    }

    private static void RunIfRequested()
    {
        string requestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, RequestFileName);
        if (!File.Exists(requestPath) || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        try
        {
            ClassifyScene();
            File.Delete(requestPath);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void ClassifyScene()
    {
        Scene scene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForTask = !scene.IsValid() || !scene.isLoaded;
        if (openedForTask)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        GameObject modelRoot = scene.GetRootGameObjects().FirstOrDefault(go => go.name == ModelRootName);
        if (modelRoot == null)
            throw new InvalidOperationException("Could not find model root: " + ModelRootName);

        Dictionary<string, Transform> groups = new Dictionary<string, Transform>();
        foreach (string categoryName in CategoryNames)
        {
            Transform existing = modelRoot.transform.Find(categoryName);
            if (existing == null)
            {
                GameObject group = new GameObject(categoryName);
                group.transform.SetParent(modelRoot.transform, false);
                existing = group.transform;
            }
            groups[categoryName] = existing;
        }

        List<Transform> candidates = new List<Transform>();
        foreach (Transform child in modelRoot.transform)
        {
            if (CategoryNames.Contains(child.name))
                candidates.AddRange(child.Cast<Transform>());
            else
                candidates.Add(child);
        }

        Dictionary<string, List<string>> report = CategoryNames.ToDictionary(name => name, _ => new List<string>());
        foreach (Transform candidate in candidates)
        {
            string category = Classify(candidate);
            candidate.SetParent(groups[category], true);
            report[category].Add(candidate.name);
        }

        for (int index = 0; index < CategoryNames.Length; index++)
            groups[CategoryNames[index]].SetSiblingIndex(index);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("Unity could not save " + ScenePath);

        WriteReport(report, candidates.Count);
        Debug.Log("XR scene classification complete. Classified " + candidates.Count + " top-level model objects in " + ScenePath);

        if (openedForTask)
            EditorSceneManager.CloseScene(scene, true);
    }

    private static string Classify(Transform item)
    {
        string searchableName = string.Join(" ", item.GetComponentsInChildren<Transform>(true).Select(t => t.name)).ToLowerInvariant();
        string itemName = item.name.ToLowerInvariant();

        if (item.GetComponentInChildren<Light>(true) != null ||
            searchableName.Contains(".target") || searchableName.StartsWith("ies") ||
            Regex.IsMatch(searchableName, @"^c\d{2}$"))
            return CategoryNames[5];

        if (ContainsAny(searchableName, LightFixtureWords))
            return CategoryNames[4];
        if (ExactFixedFurnitureNames.Contains(itemName))
            return CategoryNames[1];
        if (ExactMovableFurnitureNames.Contains(itemName))
            return CategoryNames[2];
        if (ContainsAny(searchableName, ArchitectureWords))
            return CategoryNames[0];
        if (ContainsAny(searchableName, FixedFurnitureWords))
            return CategoryNames[1];
        if (ContainsAny(searchableName, MovableFurnitureWords))
            return CategoryNames[2];
        if (ContainsAny(searchableName, DecorWords))
            return CategoryNames[4];

        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return CategoryNames[4];

        Bounds bounds = CombinedBounds(renderers);
        string materialNames = string.Join(" ", renderers
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Select(material => material.name)).ToLowerInvariant();
        bool usesArchitectureMaterial = materialNames.Contains("wall") || materialNames.Contains("zem");
        bool looksLikeBuiltInCabinet = materialNames.Contains("pvc black") && bounds.size.y >= 1.5f;
        bool looksLikeLargeGlass = searchableName.Contains("glass") &&
                                   (bounds.size.y > 1.5f || bounds.size.x > 2f || bounds.size.z > 2f);
        bool looksStructural = LooksStructural(bounds);

        if (usesArchitectureMaterial || looksLikeLargeGlass)
            return CategoryNames[0];
        if (looksLikeBuiltInCabinet)
            return CategoryNames[1];
        if (looksStructural)
            return CategoryNames[0];
        if (ContainsAny(searchableName, SmallInteractableWords) && bounds.size.magnitude < 2.5f)
            return CategoryNames[3];

        float longest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (longest <= 0.65f && bounds.size.magnitude <= 0.9f)
            return CategoryNames[3];

        return CategoryNames[4];
    }

    private static bool LooksStructural(Bounds bounds)
    {
        Vector3 size = bounds.size;
        bool horizontalSlab = size.y <= 0.35f && size.x >= 4f && size.z >= 4f;
        bool wallAlongX = size.y >= 2.1f && size.x >= 1.5f && size.z <= 0.65f;
        bool wallAlongZ = size.y >= 2.1f && size.z >= 1.5f && size.x <= 0.65f;
        return horizontalSlab || wallAlongX || wallAlongZ;
    }

    private static Bounds CombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static bool ContainsAny(string value, IEnumerable<string> words)
    {
        return words.Any(value.Contains);
    }

    private static void WriteReport(Dictionary<string, List<string>> report, int total)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string reportDirectory = Path.Combine(projectRoot, "Tools", "xr-review");
        Directory.CreateDirectory(reportDirectory);
        string reportPath = Path.Combine(reportDirectory, "classification-report.txt");

        using (StreamWriter writer = new StreamWriter(reportPath, false))
        {
            writer.WriteLine("Scene: " + ScenePath);
            writer.WriteLine("Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            writer.WriteLine("Classified top-level objects: " + total);
            writer.WriteLine();

            foreach (string categoryName in CategoryNames)
            {
                List<string> names = report[categoryName];
                writer.WriteLine(categoryName + " (" + names.Count + ")");
                foreach (string name in names.OrderBy(value => value))
                    writer.WriteLine("  - " + name);
                writer.WriteLine();
            }
        }
    }
}
