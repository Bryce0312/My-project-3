using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LivingRoom30.Editor
{
    public static class XRReviewRigPrefabBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/SampleScene_edit.unity";
        private const string PrefabFolder = "Assets/LivingRoom30/Prefabs";
        private const string PrefabPath = PrefabFolder + "/XR_ReviewRig.prefab";

        [MenuItem("Tools/SceneNew/Create or Update XR Review Rig Prefab")]
        public static void CreateOrUpdateXRReviewRigPrefab()
        {
            if (!File.Exists(SourceScenePath))
            {
                throw new FileNotFoundException("The reusable XR source scene was not found.", SourceScenePath);
            }

            EnsurePrefabFolder();

            Scene originalActiveScene = SceneManager.GetActiveScene();
            Scene sourceScene = default;

            try
            {
                sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
                SceneManager.SetActiveScene(sourceScene);
                GameObject sourcePlayer = sourceScene.GetRootGameObjects().FirstOrDefault(item => item.name == "XR_Player");
                GameObject sourceManager = sourceScene.GetRootGameObjects().FirstOrDefault(item => item.name == "XR Interaction Manager");

                if (sourcePlayer == null || sourceManager == null)
                {
                    throw new MissingReferenceException("SampleScene_edit must contain XR_Player and XR Interaction Manager roots.");
                }

                GameObject prefabRoot = new GameObject("XR_ReviewRig");
                SceneManager.MoveGameObjectToScene(prefabRoot, sourceScene);

                GameObject player = UnityEngine.Object.Instantiate(sourcePlayer);
                player.name = "XR_Player";
                SceneManager.MoveGameObjectToScene(player, sourceScene);
                player.transform.SetParent(prefabRoot.transform, false);
                player.transform.localPosition = Vector3.zero;
                player.transform.localRotation = Quaternion.identity;
                player.transform.localScale = Vector3.one;

                GameObject manager = UnityEngine.Object.Instantiate(sourceManager);
                manager.name = "XR Interaction Manager";
                SceneManager.MoveGameObjectToScene(manager, sourceScene);
                manager.transform.SetParent(prefabRoot.transform, false);
                manager.transform.localPosition = Vector3.zero;
                manager.transform.localRotation = Quaternion.identity;
                manager.transform.localScale = Vector3.one;

                GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                if (savedPrefab == null)
                {
                    throw new IOException("Unity could not save the reusable XR review rig prefab.");
                }

                UnityEngine.Object.DestroyImmediate(prefabRoot);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("Reusable XR review rig prefab created: " + PrefabPath);
            }
            finally
            {
                if (sourceScene.IsValid() && sourceScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(sourceScene, false);
                }

                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                {
                    SceneManager.SetActiveScene(originalActiveScene);
                }
            }
        }

        public static void CreateOrUpdateXRReviewRigPrefabBatch()
        {
            CreateOrUpdateXRReviewRigPrefab();
        }

        private static void EnsurePrefabFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/LivingRoom30"))
            {
                AssetDatabase.CreateFolder("Assets", "LivingRoom30");
            }

            if (!AssetDatabase.IsValidFolder(PrefabFolder))
            {
                AssetDatabase.CreateFolder("Assets/LivingRoom30", "Prefabs");
            }
        }
    }
}
