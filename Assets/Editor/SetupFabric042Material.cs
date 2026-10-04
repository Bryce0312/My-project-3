using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SetupFabric042Material
{
    const string Folder = "Assets/TVArea/Fabric042";
    const string MaterialPath = "Assets/TVArea/Sofa_DarkBrownFabric.mat";

    static SetupFabric042Material() => EditorApplication.delayCall += Setup;

    [MenuItem("Tools/TV Area/Build Fabric042 Dark Material")]
    public static void Setup()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;

        string colorPath = Folder + "/Fabric042_2K-JPG_Color.jpg";
        string normalPath = Folder + "/Fabric042_2K-JPG_NormalGL.jpg";
        string aoPath = Folder + "/Fabric042_2K-JPG_AmbientOcclusion.jpg";
        string displacementPath = Folder + "/Fabric042_2K-JPG_Displacement.jpg";
        string roughnessPath = Folder + "/Fabric042_2K-JPG_Roughness.jpg";
        string packedPath = Folder + "/Fabric042_MetallicSmoothness.png";

        if (!File.Exists(colorPath) || !File.Exists(normalPath) ||
            !File.Exists(aoPath) || !File.Exists(roughnessPath)) return;

        Configure(colorPath, false, true, false);
        Configure(normalPath, true, false, false);
        Configure(aoPath, false, false, false);
        Configure(displacementPath, false, false, false);
        Configure(roughnessPath, false, false, !File.Exists(packedPath));

        if (!File.Exists(packedPath))
        {
            var roughness = AssetDatabase.LoadAssetAtPath<Texture2D>(roughnessPath);
            if (!roughness) return;
            var source = roughness.GetPixels32();
            var packed = new Texture2D(roughness.width, roughness.height, TextureFormat.RGBA32, false, true);
            var output = new Color32[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                byte smoothness = (byte)(255 - source[i].r);
                output[i] = new Color32(0, 0, 0, smoothness);
            }
            packed.SetPixels32(output);
            packed.Apply(false, false);
            File.WriteAllBytes(packedPath, packed.EncodeToPNG());
            Object.DestroyImmediate(packed);
            AssetDatabase.ImportAsset(packedPath, ImportAssetOptions.ForceSynchronousImport);
            Configure(roughnessPath, false, false, false);
        }

        Configure(packedPath, false, false, false);

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!material)
        {
            material = new Material(Shader.Find("Standard")) { name = "Sofa_DarkBrownFabric" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        var tiling = new Vector2(3f, 3f);
        material.color = Color.white;
        material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath));
        material.SetTextureScale("_MainTex", tiling);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        material.SetTextureScale("_BumpMap", tiling);
        material.SetFloat("_BumpScale", 0.4f);
        material.SetTexture("_OcclusionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath));
        material.SetTextureScale("_OcclusionMap", tiling);
        material.SetFloat("_OcclusionStrength", 0.65f);
        material.SetTexture("_ParallaxMap", AssetDatabase.LoadAssetAtPath<Texture2D>(displacementPath));
        material.SetTextureScale("_ParallaxMap", tiling);
        material.SetFloat("_Parallax", 0.015f);
        material.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath));
        material.SetTextureScale("_MetallicGlossMap", tiling);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_GlossMapScale", 0.75f);
        material.EnableKeyword("_NORMALMAP");
        material.EnableKeyword("_PARALLAXMAP");
        material.EnableKeyword("_METALLICGLOSSMAP");
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        Selection.activeObject = material;
        Debug.Log("Sofa dark brown fabric material is ready at " + MaterialPath);
    }

    static void Configure(string path, bool normalMap, bool sRgb, bool readable)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (!importer) return;
        bool changed = false;
        var desiredType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (importer.textureType != desiredType) { importer.textureType = desiredType; changed = true; }
        if (!normalMap && importer.sRGBTexture != sRgb) { importer.sRGBTexture = sRgb; changed = true; }
        if (importer.isReadable != readable) { importer.isReadable = readable; changed = true; }
        if (importer.maxTextureSize != 2048) { importer.maxTextureSize = 2048; changed = true; }
        if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
        { importer.textureCompression = TextureImporterCompression.CompressedHQ; changed = true; }
        if (changed) importer.SaveAndReimport();
    }
}
