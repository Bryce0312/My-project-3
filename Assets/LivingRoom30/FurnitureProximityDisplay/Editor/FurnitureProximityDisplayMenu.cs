using UnityEditor;
using UnityEngine;
using LivingRoom30;

namespace LivingRoom30.Editor
{
    /// <summary>
    /// Editor helper to install the <see cref="FurnitureProximityDisplay"/> into the current scene.
    ///
    /// This is the lowest-risk mounting path: it adds the single new component at editor time to the
    /// active player's root (resolved via Camera.main.transform.root, which works for both the XR rig
    /// instance and the desktop camera) and never edits any .prefab asset or other existing file.
    /// </summary>
    public static class FurnitureProximityDisplayMenu
    {
        [MenuItem("Tools/SceneNew/Add Furniture Dimension Display")]
        public static void AddFurnitureDimensionDisplay()
        {
            // If one already exists anywhere in the scene (e.g. auto-installed at runtime), just
            // select it instead of creating a duplicate.
            FurnitureProximityDisplay existing =
                Object.FindObjectOfType(typeof(FurnitureProximityDisplay)) as FurnitureProximityDisplay;
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            GameObject host = FindPlayerHost();
            if (host == null)
            {
                host = new GameObject("FurnitureProximityDisplay");
            }

            host.AddComponent<FurnitureProximityDisplay>();
            Selection.activeGameObject = host;
            EditorUtility.SetDirty(host);
        }

        private static GameObject FindPlayerHost()
        {
            // Resolve the active player root from the main camera so we do not depend on the rig's
            // instance name (it is "S03_XR_ReviewRig" in the built scene). Works for XR and desktop.
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                return mainCamera.transform.root.gameObject;
            }

            return GameObject.Find("FurnitureProximityDisplay");
        }
    }
}
