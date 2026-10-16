using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class SceneNewStartup
{
    // Scene selection is manual so authoring other scenes does not reopen the living room.
    private const string ScenePath = "Assets/Scenes/SceneNew.unity";

    [MenuItem("Tools/SceneNew/Open Main Scene")]
    private static void OpenSceneNewFromMenu()
    {
        if (!File.Exists(ScenePath))
        {
            Debug.LogWarning("SceneNew startup scene was not found: " + ScenePath);
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.path == ScenePath)
        {
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            return;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
