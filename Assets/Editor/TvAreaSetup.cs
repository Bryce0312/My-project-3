using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class TvAreaSetup
{
    [Serializable] public class Entry { public string path; public string[] materials; public Vector3 center; public Vector3 size; }
    [Serializable] public class Audit { public string scene; public Entry[] renderers; }
    static TvAreaSetup() { EditorApplication.delayCall += ExportAudit; }
    static string PathOf(Transform t) { return t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name; }
    [MenuItem("Tools/TV Area/Export Scene Audit")]
    public static void ExportAudit()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;
        Directory.CreateDirectory("Tools/tv-area");
        var renderers=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<Renderer>(true)).ToArray();
        var audit=new Audit {scene=scene.path, renderers=renderers.Select(r=>new Entry {path=PathOf(r.transform), materials=r.sharedMaterials.Select(m=>m?m.name:"MISSING").ToArray(),center=r.bounds.center,size=r.bounds.size}).ToArray()};
        File.WriteAllText("Tools/tv-area/scene-audit.json",JsonUtility.ToJson(audit,true));
        Debug.Log("TV area: exported " + renderers.Length + " renderer bounds.");
    }
}
