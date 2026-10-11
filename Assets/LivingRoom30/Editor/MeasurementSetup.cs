#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace LivingRoom30.Measurement.Editor
{
    /// <summary>
    /// 编辑器装配工具：为「空间测量」功能的可行路线（NavMesh）烘焙导航网格。
    ///
    /// 设计要点：
    /// - 客厅场景由 SceneNewBuilder 生成，根物体名为 "SceneNew_40sqm_LivingRoom"，
    ///   其下 A00_Architecture 含地面/墙体/天花，F00_Furniture 含全部家具。
    /// - 烘焙策略：仅把这两棵子树标记为 Navigation Static（避免把 XR 相机/灯光/地毯等
    ///   误标为静态）。地面（A01_Floor）设为可行走区域（Walkable），其余（墙体、家具、
    ///   天花、门窗）设为不可行走区域（Not Walkable），从而把可行走区域“雕刻”成房间
    ///   地面并绕开家具。
    /// - 运行时 NavMeshPathDrawer 用 NavMesh.AllAreas 采样并计算路径，因此只要地面可
    ///   行走、家具为障碍即可正确给出沿地面可走的可行路线。
    ///
    /// 用法（菜单）：LivingRoom30 ▸ 烘焙测量用 NavMesh
    /// 也可手动 Window ▸ AI ▸ Navigation ▸ Bake 完成同样效果。
    /// </summary>
    public static class MeasurementSetup
    {
        private const string MenuBake = "LivingRoom30/烘焙测量用 NavMesh";
        private const string MenuClear = "LivingRoom30/清除测量 NavMesh";

        private const string RootName = "SceneNew_40sqm_LivingRoom";
        private const string FloorName = "A01_Floor";

        [MenuItem(MenuBake)]
        public static void BakeMeasurementNavMesh()
        {
            GameObject root = FindSceneRoot();
            if (root == null)
            {
                EditorUtility.DisplayDialog("测量 NavMesh",
                    "未找到客厅场景根物体（" + RootName + "）。\n请先通过 LivingRoom30 的生成菜单创建客厅场景。",
                    "好");
                return;
            }

            Transform architecture = root.transform.Find("A00_Architecture");
            Transform furniture = root.transform.Find("F00_Furniture");
            if (architecture == null && furniture == null)
            {
                EditorUtility.DisplayDialog("测量 NavMesh",
                    "场景中未找到 A00_Architecture 或 F00_Furniture 分组，无法定位可烘焙几何。",
                    "好");
                return;
            }

            // 内置区域：Walkable=0，Not Walkable=1（若被改名则回退 1）
            int walkableArea = 0;
            int notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");
            if (notWalkableArea < 0) notWalkableArea = 1;

            var all = new List<GameObject>();
            if (architecture != null) CollectAll(architecture, all);
            if (furniture != null) CollectAll(furniture, all);

            int marked = 0;
            // Unity 2022.3 起 StaticEditorFlags.NavigationStatic 被标记为过时（CS0618），
            // 但未安装 AI Navigation 包时旧版烘焙仍只认这个标志，故显式抑制警告。
#pragma warning disable 0618
            for (int i = 0; i < all.Count; i++)
            {
                GameObject go = all[i];
                if (go == null) continue;

                // 叠加 Navigation Static 标志，保留其它已有静态标志
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go) | StaticEditorFlags.NavigationStatic;
                GameObjectUtility.SetStaticEditorFlags(go, flags);

                // 地面可行走，其余一律不可行走（障碍）
                int area = go.name.Contains(FloorName) ? walkableArea : notWalkableArea;
                GameObjectUtility.SetNavMeshArea(go, area);

                marked++;
                if (i % 20 == 0)
                    EditorUtility.DisplayProgressBar("标记 Navigation Static", go.name, (float)i / all.Count);
            }
#pragma warning restore 0618
            EditorUtility.ClearProgressBar();

            Debug.Log("[测量NavMesh] 已标记 " + marked + " 个物体为 Navigation Static（地面可行走，其余障碍），开始烘焙…");

            // UnityEditor.AI.NavMeshBuilder 是同步 void 方法（烘焙在调用内完成），
            // 全限定名以避开与 UnityEngine.AI.NavMeshBuilder 的二义性（CS0104）。
#pragma warning disable 0618
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
#pragma warning restore 0618

            EditorSceneManager.MarkSceneDirty(root.scene);
            EditorSceneManager.SaveScene(root.scene);
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("测量 NavMesh",
                "导航网格烘焙完成并保存到场景。\n空间测量的「可行路线」现在可用。",
                "好");
            Debug.Log("[测量NavMesh] 烘焙完成并保存。");
        }

        [MenuItem(MenuClear)]
        public static void ClearMeasurementNavMesh()
        {
            NavMesh.RemoveAllNavMeshData();
            EditorUtility.DisplayDialog("测量 NavMesh", "已清除烘焙的导航网格数据。", "好");
        }

        /// <summary>定位场景根：优先按名称查找，回退到含 Architecture/Furniture 分组的根物体。</summary>
        private static GameObject FindSceneRoot()
        {
            GameObject exact = GameObject.Find(RootName);
            if (exact != null) return exact;

            Scene scene = SceneManager.GetActiveScene();
            foreach (GameObject r in scene.GetRootGameObjects())
            {
                if (r.transform.Find("A00_Architecture") != null || r.transform.Find("F00_Furniture") != null)
                    return r;
            }
            return null;
        }

        /// <summary>递归收集整棵子树的所有 GameObject（含根）。</summary>
        private static void CollectAll(Transform t, List<GameObject> outList)
        {
            outList.Add(t.gameObject);
            for (int i = 0; i < t.childCount; i++)
                CollectAll(t.GetChild(i), outList);
        }
    }
}
#endif
