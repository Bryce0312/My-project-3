using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ApplySofaMaterial
{
    static ApplySofaMaterial()
    {
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.delayCall += ApplyOnce; };
        EditorApplication.delayCall += () => {
            if (File.Exists("Tools/tv-area/sofa-applied.txt")) { RefineOnce(); return; }
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            else ApplyOnce();
        };
    }

    static void RefineOnce()
    {
        const string done="Tools/tv-area/sofa-refined.txt";
        if (File.Exists(done) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var mat=AssetDatabase.LoadAssetAtPath<Material>("Assets/TVArea/Sofa_WarmIvory.mat");
        if (!mat) return;
        mat.mainTexture=null;
        mat.color=new Color(0.87f,0.79f,0.67f,1);
        mat.SetTexture("_DetailAlbedoMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Scenes/Models/Apartment/Textures/Maps/nsr_lawrence sofa_fbr d1.jpg"));
        mat.EnableKeyword("_DETAIL_MULX2");
        mat.SetFloat("_UVSec",0);
        EditorUtility.SetDirty(mat);
        var scene=SceneManager.GetActiveScene();
        var sofa=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)).First(r=>r.name=="crp_Koltuk minotti");
        var go=new GameObject("Sofa - Soft Window Fill");
        Undo.RegisterCreatedObjectUndo(go,"Add sofa soft fill");
        go.transform.position=sofa.bounds.center+new Vector3(-0.7f,1.9f,0.3f);
        var light=go.AddComponent<Light>(); light.type=LightType.Point;
        light.color=new Color(1f,0.91f,0.78f); light.intensity=2.4f; light.range=5f;
        light.shadows=LightShadows.Soft; light.shadowStrength=0.65f; light.shadowBias=0.025f;
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cannot save refined sofa.");
        SceneView.RepaintAll();
        File.WriteAllText(done,"Warm ivory upholstery refined; one local soft fill light added; scene saved.");
    }

    [MenuItem("Tools/TV Area/Apply Warm Sofa Fabric")]
    public static void ApplyOnce()
    {
        const string marker = "Tools/tv-area/sofa-applied.txt";
        if (File.Exists(marker) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = SceneManager.GetActiveScene();
        var sofa = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true))
            .FirstOrDefault(r => r.name == "crp_Koltuk minotti");
        if (sofa == null) { Debug.LogWarning("Sofa material: target sofa not found; no changes made."); return; }
        Directory.CreateDirectory("Tools/tv-area");
        Directory.CreateDirectory("Backups/sofa-material");
        if (!EditorSceneManager.SaveScene(scene, "Backups/sofa-material/BeforeSofa.unity", true))
            throw new IOException("Cannot back up scene; sofa unchanged.");
        if (!AssetDatabase.IsValidFolder("Assets/TVArea")) AssetDatabase.CreateFolder("Assets", "TVArea");
        var material = new Material(Shader.Find("Standard"));
        material.name = "Sofa - Warm Ivory Fabric";
        material.color = new Color(0.78f, 0.70f, 0.59f, 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0.16f);
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Scenes/Models/Apartment/Textures/Maps/nsr_lawrence sofa_fbr d1.jpg");
        material.mainTexture = texture;
        const string assetPath = "Assets/TVArea/Sofa_WarmIvory.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (existing != null) { Object.DestroyImmediate(material); material = existing; }
        else AssetDatabase.CreateAsset(material, assetPath);
        var original = sofa.sharedMaterials;
        File.WriteAllText("Tools/tv-area/sofa-original-materials.txt", string.Join("\n", original.Select(m => AssetDatabase.GetAssetPath(m))));
        Undo.RecordObject(sofa, "Apply warm ivory sofa fabric");
        var replacement = (Material[])original.Clone();
        int changed = 0;
        for (int i = 0; i < replacement.Length; i++)
            if (replacement[i] != null && (replacement[i].name == "Fabric Velvet0" || replacement[i].name == "Minotti_Lawrence Sofa 5_052"))
            { replacement[i] = material; changed++; }
        if (changed == 0) throw new System.InvalidOperationException("Expected upholstery slots not found.");
        sofa.sharedMaterials = replacement;
        PrefabUtility.RecordPrefabInstancePropertyModifications(sofa);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = sofa.gameObject;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(sofa.bounds, false);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Sofa changed but scene save failed.");
        File.WriteAllText(marker, "Applied warm ivory fabric to " + changed + " upholstery slots on " + sofa.name + ". Scene saved; backup: Backups/sofa-material/BeforeSofa.unity");
        Debug.Log("Sofa material applied: " + changed + " upholstery slots. Original pillows and frame preserved.");
    }
}
