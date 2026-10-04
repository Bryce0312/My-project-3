using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ApplyTvAreaMaterials
{
    const string Marker = "Tools/tv-area/tv-materials-applied.txt";
    const string TextureRoot = "Assets/Scenes/Models/Apartment/Textures";

    static ApplyTvAreaMaterials()
    {
        EditorApplication.delayCall += ApplyOnce;
    }

    [MenuItem("Tools/TV Area/Apply TV Area Materials")]
    public static void ApplyOnce()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        Directory.CreateDirectory("Tools/tv-area");

        if (File.Exists(Marker))
        {
            Debug.Log("TV area materials already applied. Delete " + Marker + " to run again.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded)
        {
            Directory.CreateDirectory("Backups/tv-area-materials");
            EditorSceneManager.SaveScene(scene, "Backups/tv-area-materials/BeforeTvAreaMaterials.unity", true);
        }

        ApplyMaterial("Material #4062384", m =>
        {
            SetMain(m, TextureRoot + "/Me/marble tv new.jpg", new Vector2(1.25f, 1.25f));
            m.color = Color.white;
            m.SetFloat("_Glossiness", 0.42f);
            m.SetFloat("_Metallic", 0f);
        });

        ApplyMaterial("Material #24442", m =>
        {
            SetMain(m, TextureRoot + "/Me/marble tv new.jpg", Vector2.one);
            m.color = new Color(0.82f, 0.78f, 0.72f, 1f);
            m.SetFloat("_Glossiness", 0.55f);
            m.SetFloat("_Metallic", 0f);
        });

        ApplyMaterial("Material #4062572", m =>
        {
            SetMain(m, TextureRoot + "/Me/3439733487fecea5c86a76d14e8ceb39.jpg", Vector2.one);
            m.color = Color.white;
            m.SetFloat("_Glossiness", 0.32f);
            m.SetFloat("_Metallic", 0f);
        });

        ApplyMaterial("Material #2147483610", m =>
        {
            SetMain(m, TextureRoot + "/Me/db5da091ff6a22632d32014aea776d0a.jpg", Vector2.one);
            m.color = Color.white;
            m.SetFloat("_Glossiness", 0.28f);
            m.SetFloat("_Metallic", 0f);
        });

        ApplyMaterial("ZEMİN", m =>
        {
            SetMain(m, TextureRoot + "/Me/bedroom tile.jpg", new Vector2(2.2f, 2.2f));
            m.color = Color.white;
            m.SetFloat("_Glossiness", 0.24f);
            m.SetFloat("_Metallic", 0f);
        });

        ApplyMaterial("Carpet Soft Rug Beige Pattern 1", m =>
        {
            SetMain(m, TextureRoot + "/assets/4c1de2cf743d.jpg", new Vector2(40f, 40f));
            m.color = new Color(0.82f, 0.76f, 0.66f, 1f);
            m.SetFloat("_Glossiness", 0.08f);
            m.SetFloat("_Metallic", 0f);
        });

        ApplyMaterial("PVC Black Glossy", GlossyBlack);
        ApplyMaterial("PVC Black Glossy0", GlossyBlack);
        ApplyMaterial("PVC Black Glossy1", GlossyBlack);
        ApplyMaterial("PVC Black Glossy2", GlossyBlack);
        ApplyMaterial("PVC Black Glossy23", GlossyBlack);

        ApplyMaterial("PVC White Matte0", MatteWhite);
        ApplyMaterial("PVC White Matte03", MatteWhite);
        ApplyMaterial("PVC White Matte2", MatteWhite);
        ApplyMaterial("PVC White Matte4", MatteWhite);

        ApplyMaterial("Brass Satin", m =>
        {
            m.color = new Color(0.73f, 0.55f, 0.34f, 1f);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Glossiness", 0.38f);
        });

        ApplyMaterial("Glass Clear0", ClearGlass);
        ApplyMaterial("Glass Safety", ClearGlass);
        ApplyMaterial("glass", ClearGlass);

        ApplyMaterial("WALL 1", WarmWall);
        ApplyMaterial("WALL23", WarmWall);
        ApplyMaterial("wall", WarmWall);
        ApplyMaterial("WALL LIGHT", WarmWall);

        ApplyRendererSpecifics();

        AssetDatabase.SaveAssets();
        if (scene.IsValid() && scene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        File.WriteAllText(Marker, "Applied TV area material pass. Scene backup: Backups/tv-area-materials/BeforeTvAreaMaterials.unity");
        Debug.Log("TV area material pass applied.");
    }

    static void ApplyRendererSpecifics()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;

        var renderers = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Renderer>(true));
        foreach (var renderer in renderers)
        {
            if (renderer.name == "crp_Koltuk minotti")
            {
                var mats = renderer.sharedMaterials;
                var dark = AssetDatabase.LoadAssetAtPath<Material>("Assets/TVArea/Sofa_DarkBrownFabric.mat");
                var ivory = AssetDatabase.LoadAssetAtPath<Material>("Assets/TVArea/Sofa_WarmIvory.mat");
                if (dark && mats.Length > 3) mats[3] = dark;
                if (ivory)
                {
                    for (int i = 0; i < Mathf.Min(2, mats.Length); i++) mats[i] = ivory;
                }
                renderer.sharedMaterials = mats;
                EditorUtility.SetDirty(renderer);
            }
        }
    }

    static void GlossyBlack(Material material)
    {
        material.color = new Color(0.015f, 0.014f, 0.013f, 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0.72f);
    }

    static void MatteWhite(Material material)
    {
        material.color = new Color(0.86f, 0.84f, 0.8f, 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0.18f);
    }

    static void WarmWall(Material material)
    {
        material.color = new Color(0.78f, 0.74f, 0.68f, 1f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Glossiness", 0.12f);
    }

    static void ClearGlass(Material material)
    {
        material.color = new Color(0.72f, 0.86f, 0.92f, 0.22f);
        material.SetFloat("_Mode", 3f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Glossiness", 0.92f);
        material.SetFloat("_Metallic", 0f);
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        material.SetOverrideTag("RenderType", "Transparent");
    }

    static void ApplyMaterial(string materialName, System.Action<Material> edit)
    {
        var material = FindMaterial(materialName);
        if (!material)
        {
            Debug.LogWarning("TV area material not found: " + materialName);
            return;
        }

        edit(material);
        EditorUtility.SetDirty(material);
    }

    static Material FindMaterial(string materialName)
    {
        var directPath = TextureRoot + "/" + materialName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(directPath);
        if (material) return material;

        return AssetDatabase.FindAssets("t:Material " + materialName)
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .FirstOrDefault(m => m && (Normalize(m.name) == Normalize(materialName)));
    }

    static void SetMain(Material material, string texturePath, Vector2 scale)
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (!texture)
        {
            Debug.LogWarning("TV area texture not found: " + texturePath);
            return;
        }

        material.SetTexture("_MainTex", texture);
        material.SetTextureScale("_MainTex", scale);
        material.SetTextureOffset("_MainTex", Vector2.zero);
    }

    static string Normalize(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    }
}
