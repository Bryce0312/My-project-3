using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
internal static class SceneNewStartup
{
    // Keeps the lightweight review scene as the default editor entry point.
    private const string ScenePath = "Assets/Scenes/SceneNew.unity";
    private const string SessionKey = "LivingRoom30.SceneNewOpenedThisSession";

    static SceneNewStartup()
    {
        if (!Application.isBatchMode)
        {
            EditorApplication.delayCall += OpenSceneNewOnce;
        }
    }

    [MenuItem("Tools/SceneNew/Open Main Scene")]
    private static void OpenSceneNewFromMenu()
    {
        OpenSceneNew(false);
    }

    private static void OpenSceneNewOnce()
    {
        if (SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.delayCall += OpenSceneNewOnce;
            return;
        }

        SessionState.SetBool(SessionKey, true);
        OpenSceneNew(true);
    }

    private static void OpenSceneNew(bool preserveDirtyScene)
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

        if (preserveDirtyScene && activeScene.IsValid() && activeScene.isDirty)
        {
            Debug.LogWarning("SceneNew was not opened automatically because the current scene has unsaved changes.");
            return;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
