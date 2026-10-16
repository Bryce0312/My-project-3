#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LivingRoom30.Feedback;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace LivingRoom30.Feedback.Editor
{
    /// <summary>
    /// E 部分（反馈与问题管理）的集成自检工具。
    ///
    /// 反馈功能是运行期自举的，**不应**在场景中保存任何 E 的对象；因此自检的重点不是
    /// "E 装了没有"，而是"E 依赖的前置条件是否齐备"——即 D 的标注链路与场景的 XR UI 输入链路。
    /// 任意一环不成立，E 的分类/严重程度/提交都会静默失效，所以这里逐项显式验证并给出修复建议。
    ///
    /// 报告输出到 <c>Tools/xr-review/feedback-integration-report.txt</c>（沿用项目既有报告目录约定）。
    /// </summary>
    public static class FeedbackSetup
    {
        private const string ScenePath = "Assets/Scenes/SceneNew.unity";
        private const string AnnotationPrefabPath = "Assets/XR interaction/Canvas.prefab";
        private const string ReportPath = "Tools/xr-review/feedback-integration-report.txt";
        private const string SceneRootName = "SceneNew_40sqm_LivingRoom";
        private const string XrUiEventSystemName = "S06_XR_UIEventSystem";
        private const string RuntimeBootstrapName = "FeedbackSystem";

        [MenuItem("LivingRoom30/反馈/集成自检并生成报告")]
        public static void RunSelfCheck()
        {
            string report = BuildReport();
            WriteReport(report);
            Debug.Log("[反馈自检]\n" + report);
            EditorUtility.DisplayDialog("反馈模块集成自检",
                "自检完成，报告已写入：\n" + ReportPath + "\n\n" + FirstLines(report, 12), "好");
        }

        /// <summary>批处理入口（CI / 命令行 -executeMethod 可用）。</summary>
        public static void RunBatchSelfCheck()
        {
            string report = BuildReport();
            WriteReport(report);
            Debug.Log("[反馈自检]\n" + report);
        }

        [MenuItem("LivingRoom30/反馈/打开反馈数据导出目录")]
        public static void OpenExportDirectory()
        {
            string directory = IssueStore.DefaultExportDirectory;
            Directory.CreateDirectory(directory);
            EditorUtility.RevealInFinder(directory);
        }

        [MenuItem("LivingRoom30/反馈/导出当前会话（需 Play 中）")]
        public static void ExportCurrentSession()
        {
            if (!Application.isPlaying || FeedbackService.Instance == null)
            {
                EditorUtility.DisplayDialog("导出反馈数据",
                    "请在 Play 模式下运行，并确保至少已提交一条问题记录。", "好");
                return;
            }

            string json = FeedbackService.Instance.ExportJson();
            string csv = FeedbackService.Instance.ExportCsv();
            EditorUtility.DisplayDialog("导出反馈数据",
                "JSON：" + (json ?? "失败") + "\n\nCSV：" + (csv ?? "失败"), "好");
        }

        [MenuItem("LivingRoom30/反馈/汇总已导出的反馈数据")]
        public static void SummarizeExports()
        {
            string directory = IssueStore.DefaultExportDirectory;
            if (!Directory.Exists(directory))
            {
                EditorUtility.DisplayDialog("反馈数据汇总", "导出目录尚不存在：\n" + directory, "好");
                return;
            }

            string[] files = Directory.GetFiles(directory, "issues_*.csv");
            var builder = new StringBuilder();
            builder.AppendLine("导出目录：" + directory);
            builder.AppendLine("CSV 文件数：" + files.Length);
            int totalIssues = 0;
            for (int i = 0; i < files.Length; i++)
            {
                int lines = Mathf.Max(0, File.ReadAllLines(files[i]).Length - 1);
                totalIssues += lines;
                builder.AppendLine("  · " + Path.GetFileName(files[i]) + " → " + lines + " 条");
            }
            builder.AppendLine("合计问题数：" + totalIssues);
            Debug.Log("[反馈数据汇总]\n" + builder);
            EditorUtility.DisplayDialog("反馈数据汇总", builder.ToString(), "好");
        }

        // ------------------------------------------------------------------
        // 自检实现
        // ------------------------------------------------------------------

        private static string BuildReport()
        {
            var builder = new StringBuilder();
            builder.AppendLine("XR Spatial Review — E（反馈与问题管理）集成自检报告");
            builder.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            builder.AppendLine(new string('-', 70));

            // 为了不改动用户当前打开的场景集合，仅在目标场景未加载时增量打开，并在结束时关闭。
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedByUs = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                if (!File.Exists(ScenePath))
                {
                    builder.AppendLine("[失败] 场景文件不存在：" + ScenePath);
                    return builder.ToString();
                }
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                openedByUs = true;
            }

            try
            {
                GameObject root = GameObject.Find(SceneRootName);
                AppendSceneSkeleton(builder, root);
                AppendAnnotationChain(builder);
                AppendXrUiInput(builder);
                AppendFeedbackDependencies(builder);
                AppendConventions(builder);
            }
            finally
            {
                if (openedByUs && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            }

            builder.AppendLine(new string('-', 70));
            builder.AppendLine("结论：E 在运行期自举，不依赖场景序列化内容。");
            builder.AppendLine("      只要「D 标注链路」与「XR UI 输入链路」两项全部通过，");
            builder.AppendLine("      「指向物体 → 弹出标注面板 → 选分类/严重程度 → 填描述 → 提交 → 空间标注 + 汇总」即可闭环。");
            return builder.ToString();
        }

        private static void AppendSceneSkeleton(StringBuilder builder, GameObject root)
        {
            builder.AppendLine("[场景骨架]　（A / B 的产出，E 只依赖其中的命名与 FurnitureInfo）");
            if (root == null)
            {
                builder.AppendLine("  [失败] 未找到场景根对象 " + SceneRootName
                    + " → 请确认 " + ScenePath + " 已打开且完整");
                return;
            }

            AppendGroup(builder, root, "A00_Architecture", "建筑");
            AppendGroup(builder, root, "F00_Furniture", "家具");
            AppendGroup(builder, root, "S00_SceneSystem", "场景系统");
            AppendFurnitureInfoCoverage(builder, root);
        }

        private static void AppendGroup(StringBuilder builder, GameObject root, string groupName, string label)
        {
            Transform group = root.transform.Find(groupName);
            builder.AppendLine("  " + (group != null ? "[通过]" : "[失败]") + " " + label
                + "（" + groupName + "）"
                + (group != null ? "：子节点 " + group.childCount + " 个" : "：缺失"));
        }

        private static void AppendFurnitureInfoCoverage(StringBuilder builder, GameObject root)
        {
            Transform furniture = root.transform.Find("F00_Furniture");
            if (furniture == null) return;

            var ids = new List<string>();
            int missing = 0;
            for (int i = 0; i < furniture.childCount; i++)
            {
                Transform child = furniture.GetChild(i);
                if (!child.name.StartsWith("F", StringComparison.Ordinal)) continue;

                var info = child.GetComponent<FurnitureInfo>();
                if (info == null) { missing++; continue; }
                ids.Add(info.FurnitureId + ":" + info.EnglishName);
            }

            builder.AppendLine("  " + (missing == 0 ? "[通过]" : "[警告]") + " 家具语义："
                + ids.Count + " 个带 FurnitureInfo" + (missing > 0 ? "，" + missing + " 个缺失" : ""));
            if (ids.Count > 0) builder.AppendLine("        " + string.Join(" | ", ids.ToArray()));
        }

        /// <summary>
        /// 逐项验证 D 的标注链路：E 的提交入口挂在 <c>XRSimpleInteractable.selectEntered</c>
        /// 与 <c>AnnotationUI</c> 上，这两者的绑定关系一旦缺失，整条反馈链路都会失效。
        /// </summary>
        private static void AppendAnnotationChain(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("[D 标注链路]　（E 的分类 / 严重程度 / 提交都挂在这条链路上）");

            int interactableCount = 0;
            int wiredToSpawner = 0;
            int missingCollider = 0;

            var interactables = UnityEngine.Object.FindObjectsOfType<XRSimpleInteractable>(true);
            for (int i = 0; i < interactables.Length; i++)
            {
                XRSimpleInteractable interactable = interactables[i];
                if (interactable == null) continue;
                interactableCount++;

                if (interactable.GetComponent<Collider>() == null) missingCollider++;

                int persistent = interactable.selectEntered.GetPersistentEventCount();
                for (int k = 0; k < persistent; k++)
                {
                    if (interactable.selectEntered.GetPersistentMethodName(k) == "OnSelectEntered")
                    {
                        wiredToSpawner++;
                        break;
                    }
                }
            }

            var spawners = UnityEngine.Object.FindObjectsOfType<AnnotationSpawner>(true);
            var highlighters = UnityEngine.Object.FindObjectsOfType<HoverDwellHighlighter>(true);

            builder.AppendLine("  " + (interactableCount > 0 ? "[通过]" : "[失败]")
                + " XRSimpleInteractable：" + interactableCount + " 个（E 用它捕获标注目标与锚点）");
            builder.AppendLine("  " + (wiredToSpawner > 0 ? "[通过]" : "[失败]")
                + " selectEntered → AnnotationSpawner.OnSelectEntered：" + wiredToSpawner + " 个"
                + (wiredToSpawner == 0 ? " → 标注面板不会弹出，反馈链路不可用" : ""));
            builder.AppendLine("  " + (spawners.Length > 0 ? "[通过]" : "[失败]")
                + " AnnotationSpawner：" + spawners.Length + " 个");
            builder.AppendLine("  " + (highlighters.Length > 0 ? "[通过]" : "[警告]")
                + " HoverDwellHighlighter：" + highlighters.Length + " 个（dwell 高亮来源）");
            if (missingCollider > 0)
                builder.AppendLine("  [警告] " + missingCollider + " 个交互对象没有 Collider → 射线悬停不到，无法触发标注");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnnotationPrefabPath);
            if (prefab == null)
            {
                builder.AppendLine("  [失败] 未找到标注面板预制体 " + AnnotationPrefabPath);
                return;
            }

            Canvas canvas = prefab.GetComponent<Canvas>();
            bool worldSpace = canvas != null && canvas.renderMode == RenderMode.WorldSpace;
            builder.AppendLine("  " + (worldSpace ? "[通过]" : "[警告]") + " 标注面板画布："
                + (canvas == null ? "缺少 Canvas" : canvas.renderMode.ToString()));

            int total = 0, wired = 0;
            string unwired = "";
            var buttons = prefab.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                total++;
                if (buttons[i].onClick.GetPersistentEventCount() > 0) { wired++; continue; }
                unwired += (unwired.Length == 0 ? "" : ", ") + buttons[i].gameObject.name;
            }
            builder.AppendLine("  [信息] 预制体按钮 " + total + " 个，其中 " + wired
                + " 个已在预制体中接线；未接线：" + (unwired.Length == 0 ? "无" : unwired));
            builder.AppendLine("         E 会在运行期接管分类区与提交按钮（AnnotationBinding）——");
            builder.AppendLine("         上述未接线按钮正是 E 需要补上的部分，属预期状态，不是缺陷。");
        }

        private static void AppendXrUiInput(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("[XR UI 输入链路]　（世界空间画布必须靠它才能被手柄射线点击）");

            var eventSystems = UnityEngine.Object.FindObjectsOfType<EventSystem>(true);
            builder.AppendLine("  " + (eventSystems.Length > 0 ? "[通过]" : "[失败]")
                + " EventSystem：" + eventSystems.Length + " 个");

            var modules = UnityEngine.Object.FindObjectsOfType<XRUIInputModule>(true);
            if (modules.Length == 0)
            {
                builder.AppendLine("  [失败] 未找到 XRUIInputModule → 手柄射线点不到任何世界空间画布");
            }
            else
            {
                for (int i = 0; i < modules.Length; i++)
                {
                    XRUIInputModule module = modules[i];
                    builder.AppendLine("  [信息] " + module.gameObject.name
                        + "：enableXRInput=" + (module.enableXRInput ? "1" : "0")
                        + "，enableMouseInput=" + (module.enableMouseInput ? "1" : "0")
                        + "，uiCamera=" + (module.uiCamera != null ? module.uiCamera.name : "（空）"));
                }
            }

            var interactors = UnityEngine.Object.FindObjectsOfType<XRRayInteractor>(true);
            int uiEnabled = 0;
            for (int i = 0; i < interactors.Length; i++)
                if (interactors[i] != null && interactors[i].enableUIInteraction) uiEnabled++;

            builder.AppendLine("  " + (uiEnabled > 0 ? "[通过]" : "[警告]")
                + " XRRayInteractor：" + interactors.Length + " 个，其中 enableUIInteraction=1 的 "
                + uiEnabled + " 个");

            Transform right = FeedbackUi.FindControllerTransform("Right");
            Transform left = FeedbackUi.FindControllerTransform("Left");
            builder.AppendLine("  " + (right != null ? "[通过]" : "[失败]")
                + " 右手柄控制器：" + (right != null ? right.name : "未找到")
                + "（E 的 ISSUES 入口挂在这里）");
            builder.AppendLine("  " + (left != null ? "[通过]" : "[警告]")
                + " 左手柄控制器：" + (left != null ? left.name : "未找到"));

            Camera head = FeedbackUi.ResolveHeadCamera();
            XROrigin origin = UnityEngine.Object.FindObjectOfType<XROrigin>(true);
            builder.AppendLine("  " + (head != null ? "[通过]" : "[警告]") + " 头显相机："
                + (head != null ? head.name : "未解析到")
                + "（XROrigin " + (origin != null ? "存在" : "缺失") + "）");

            GameObject eventSystemObject = GameObject.Find(XrUiEventSystemName);
            builder.AppendLine("  " + (eventSystemObject != null ? "[通过]" : "[信息]")
                + " " + XrUiEventSystemName + "：" + (eventSystemObject != null ? "存在" : "缺失"));
        }

        private static void AppendFeedbackDependencies(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("[E 依赖项]");

            TMP_FontAsset font = null;
            try { font = TMP_Settings.defaultFontAsset; } catch { }
            builder.AppendLine("  " + (font != null ? "[通过]" : "[警告]")
                + " TMP 默认字体：" + (font != null ? font.name : "不可用")
                + "（仅用于改写 D 面板上的按钮标签）");

            Font legacy = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            builder.AppendLine("  " + (legacy != null ? "[通过]" : "[失败]")
                + " 内置字体 LegacyRuntime.ttf（E 自建界面全部使用它，支持中文）");

            builder.AppendLine("  [信息] 图层：Environment=" + LayerMask.NameToLayer("Environment")
                + "，Furniture=" + LayerMask.NameToLayer("Furniture")
                + "，UI=" + LayerMask.NameToLayer("UI")
                + "，PlayerBody=" + LayerMask.NameToLayer("PlayerBody"));

            builder.AppendLine("  [信息] 测量软桥接：" + (Application.isPlaying
                ? (MeasurementBridge.IsAvailable ? "已检测到读数面板" : "未检测到（自动降级，不影响反馈）")
                : "非 Play 模式，跳过"));

            string[] scripts =
            {
                "FeedbackService", "AnnotationBridge", "AnnotationBinding",
                "IssueListHud", "IssueMarker", "IssueStore"
            };
            int found = 0;
            for (int i = 0; i < scripts.Length; i++)
            {
                string[] guids = AssetDatabase.FindAssets(scripts[i] + " t:MonoScript");
                if (guids.Length > 0) found++;
            }
            builder.AppendLine("  " + (found == scripts.Length ? "[通过]" : "[失败]")
                + " E 的脚本：" + found + "/" + scripts.Length + " 个可定位");
        }

        private static void AppendConventions(StringBuilder builder)
        {
            builder.AppendLine();
            builder.AppendLine("[运行时自举约定]");

            GameObject baked = GameObject.Find(RuntimeBootstrapName);
            builder.AppendLine(baked == null
                ? "  [通过] 场景中未保存 " + RuntimeBootstrapName + "（符合运行时自举约定）"
                : "  [警告] 场景中存在 " + RuntimeBootstrapName + " 对象 → 会与自举实例冲突，请删除");
            builder.AppendLine("  导出目录：" + IssueStore.DefaultExportDirectory);
        }

        private static void WriteReport(string content)
        {
            string directory = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(ReportPath, content, new UTF8Encoding(false));
        }

        private static string FirstLines(string content, int count)
        {
            string[] lines = content.Split('\n');
            var builder = new StringBuilder();
            for (int i = 0; i < lines.Length && i < count; i++)
                builder.AppendLine(lines[i]);
            return builder.ToString();
        }
    }
}
#endif
