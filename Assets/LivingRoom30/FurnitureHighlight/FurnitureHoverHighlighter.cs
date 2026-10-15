using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace LivingRoom30
{
    /// <summary>
    /// demo-D 分支「点击高亮」功能的融合版（原 HoverDwellHighlighter + QuickOutline Outline）。
    ///
    /// 行为（与 demo-D 保持一致，仅做自动化与去耦适配）：
    ///  - 注视/悬停（射线 hover）家具超过 <see cref="m_DwellTime"/> 秒后，显示黄色描边；
    ///    移开视线立即熄灭（取消未完成的 dwell 计时）。
    ///  - 点击/选中（select）时显示青色描边，取消选中后熄灭。
    ///  - 描边使用 QuickOutline（<see cref="Outline"/>），材质来自
    ///    Resources/Materials/OutlineMask、OutlineFill（已随本目录带入）。
    ///
    /// 融合适配（相对 demo-D 原版）：
    ///  - 遵循本工程惯例：[RuntimeInitializeOnLoadMethod(AfterSceneLoad)] 自举安装，
    ///    不改场景/prefab；运行时扫描所有 <see cref="FurnitureInfo"/>（及可选 D00_Decoration
    ///    子树装饰物），逐个补挂 Outline + XRSimpleInteractable，等价于 demo-D 手工逐件挂载。
    ///  - 加载前预检 Outline 材质资源；缺失时仅告警一次并禁用本组件，
    ///    绝不给 Outline 留下空材质（避免 Awake 空引用）。
    ///  - 与 FurnitureProximityDisplay（走近显示青色贴合轮廓）完全独立、互不读写，
    ///    二者可同时生效（注视高亮为黄色/选中为青色填充描边，样式不同不冲突）。
    ///  - 桌面预览（无 XR 射线交互器）时 hover 事件不会触发，组件静默待命，不报错。
    /// </summary>
    [AddComponentMenu("LivingRoom30/Furniture Hover Highlighter")]
    [DisallowMultipleComponent]
    public sealed class FurnitureHoverHighlighter : MonoBehaviour
    {
        [Header("Dwell (hover to highlight)")]
        [Tooltip("注视多久后亮起黄色描边（秒）。与 demo-D 原版一致默认 1.5s。")]
        [SerializeField] private float m_DwellTime = 1.5f;

        [Tooltip("注视 dwell 完成后的描边颜色（demo-D 原版为黄色）。")]
        [SerializeField] private Color m_DwellColor = Color.yellow;

        [Header("Selection (click to highlight)")]
        [Tooltip("点击/选中期间显示的描边颜色（demo-D 原版为青色）。")]
        [SerializeField] private Color m_SelectedColor = Color.cyan;

        [Header("Outline Style")]
        [Tooltip("QuickOutline 描边宽度（视图深度千分比，demo-D 原版为 5）。")]
        [SerializeField, Range(0f, 10f)] private float m_OutlineWidth = 5f;

        [Header("Scope")]
        [Tooltip("是否同时为 D00_Decoration 子树下的装饰物启用高亮（demo-D 覆盖家具 + 装饰）。")]
        [SerializeField] private bool m_IncludeDecorations = true;

        private sealed class Target
        {
            public GameObject Root;
            public Outline Outline;
            public XRSimpleInteractable Interactable;
            public Coroutine Dwell;
            public bool DwellShown;
            public int SelectCount;
        }

        private readonly List<Target> m_Targets = new List<Target>();
        private static bool s_MaterialMissingWarned;

        // 自动安装：无需手工挂组件或改场景（与 FurnitureProximityDisplay 同一惯例）。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstall()
        {
            if (Object.FindObjectOfType(typeof(FurnitureHoverHighlighter)) != null)
            {
                return;
            }

            new GameObject("FurnitureHoverHighlighter").AddComponent<FurnitureHoverHighlighter>();
        }

        private void Start()
        {
            // 资源预检：Outline.Awake 会 Instantiate 两个 Resources 材质，
            // 缺失时绝不能挂 Outline，否则 Awake 内空引用。只告警一次。
            if (Resources.Load<Material>("Materials/OutlineMask") == null ||
                Resources.Load<Material>("Materials/OutlineFill") == null)
            {
                if (!s_MaterialMissingWarned)
                {
                    Debug.LogError("[FurnitureHoverHighlighter] QuickOutline 材质缺失（Resources/Materials/" +
                        "OutlineMask、OutlineFill）。注视/点击高亮已禁用。");
                    s_MaterialMissingWarned = true;
                }

                enabled = false;
                return;
            }

            CollectTargets();
            for (int i = 0; i < m_Targets.Count; i++)
            {
                SetupTarget(m_Targets[i]);
            }
        }

        private void OnDestroy()
        {
            // 移除监听（目标物体销毁时会连同组件一起销毁，这里只做防御性清理）。
            for (int i = 0; i < m_Targets.Count; i++)
            {
                Target t = m_Targets[i];
                if (t.Interactable != null)
                {
                    t.Interactable.hoverEntered.RemoveListener(OnHoverEntered);
                    t.Interactable.hoverExited.RemoveListener(OnHoverExited);
                    t.Interactable.selectEntered.RemoveListener(OnSelectEntered);
                    t.Interactable.selectExited.RemoveListener(OnSelectExited);
                }
            }

            m_Targets.Clear();
        }

        /// <summary>
        /// 收集高亮目标：所有 FurnitureInfo 根物体（11 件家具），可选 D00_Decoration 子树装饰物。
        /// 与 FurnitureProximityDisplay 的扫描方式一致，按 GameObject 去重。
        /// </summary>
        private void CollectTargets()
        {
            m_Targets.Clear();
            HashSet<GameObject> seen = new HashSet<GameObject>();

            Object[] found = Object.FindObjectsOfType(typeof(FurnitureInfo));
            for (int i = 0; i < found.Length; i++)
            {
                FurnitureInfo info = found[i] as FurnitureInfo;
                if (info == null || !seen.Add(info.gameObject))
                {
                    continue;
                }

                m_Targets.Add(new Target { Root = info.gameObject });
            }

            if (m_IncludeDecorations)
            {
                GameObject decorations = GameObject.Find("D00_Decoration");
                if (decorations != null)
                {
                    for (int i = 0; i < decorations.transform.childCount; i++)
                    {
                        Transform child = decorations.transform.GetChild(i);
                        if (child.gameObject.activeInHierarchy && seen.Add(child.gameObject))
                        {
                            m_Targets.Add(new Target { Root = child.gameObject });
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 为单个目标补挂 Outline + XRSimpleInteractable 并接好事件
        /// （demo-D 原版 HoverDwellHighlighter.Start 的等价逻辑，参数化到 Inspector）。
        /// </summary>
        private void SetupTarget(Target t)
        {
            Outline outline = t.Root.GetComponent<Outline>();
            if (outline == null)
            {
                outline = t.Root.AddComponent<Outline>();
            }

            outline.enabled = false;
            outline.OutlineColor = m_DwellColor;
            outline.OutlineWidth = m_OutlineWidth;
            t.Outline = outline;

            XRSimpleInteractable interactable = t.Root.GetComponent<XRSimpleInteractable>();
            if (interactable == null)
            {
                interactable = t.Root.AddComponent<XRSimpleInteractable>();
            }

            interactable.hoverEntered.AddListener(OnHoverEntered);
            interactable.hoverExited.AddListener(OnHoverExited);
            interactable.selectEntered.AddListener(OnSelectEntered);
            interactable.selectExited.AddListener(OnSelectExited);
            t.Interactable = interactable;
        }

        // ---- 事件（与 demo-D 原版一致：hover 计时 dwell，select 直接高亮）----

        private void OnHoverEntered(HoverEnterEventArgs args)
        {
            Target t = FindTarget(((Component)args.interactableObject).gameObject);
            if (t == null)
            {
                return;
            }

            if (t.Dwell != null)
            {
                StopCoroutine(t.Dwell);
            }

            t.Dwell = StartCoroutine(DwellTimer(t));
        }

        private void OnHoverExited(HoverExitEventArgs args)
        {
            Target t = FindTarget(((Component)args.interactableObject).gameObject);
            if (t == null)
            {
                return;
            }

            if (t.Dwell != null)
            {
                StopCoroutine(t.Dwell);
                t.Dwell = null;
            }

            t.DwellShown = false;
            Refresh(t);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            Target t = FindTarget(((Component)args.interactableObject).gameObject);
            if (t == null)
            {
                return;
            }

            t.SelectCount++;
            Refresh(t);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            Target t = FindTarget(((Component)args.interactableObject).gameObject);
            if (t == null)
            {
                return;
            }

            t.SelectCount = Mathf.Max(0, t.SelectCount - 1);
            Refresh(t);
        }

        private IEnumerator DwellTimer(Target t)
        {
            yield return new WaitForSeconds(m_DwellTime);
            t.Dwell = null;
            t.DwellShown = true;
            Refresh(t);
        }

        /// <summary>
        /// 按「选中优先、其次 dwell」重算描边颜色与开关：
        /// 选中 = 青色（点击高亮），dwell 完成 = 黄色（注视高亮），都没有 = 熄灭。
        /// </summary>
        private static void Refresh(Target t)
        {
            if (t.Outline == null)
            {
                return;
            }

            if (t.SelectCount > 0)
            {
                t.Outline.OutlineColor = Color.cyan;
                t.Outline.enabled = true;
            }
            else if (t.DwellShown)
            {
                t.Outline.OutlineColor = Color.yellow;
                t.Outline.enabled = true;
            }
            else
            {
                t.Outline.enabled = false;
            }
        }

        /// <summary>XRI 事件里的 interactableObject 对应的就是挂载根（我们只把监听挂根上），
        /// 但 hover 命中会冒泡到根，因此按根反查；未登记的物体忽略。</summary>
        private Target FindTarget(GameObject source)
        {
            for (int i = 0; i < m_Targets.Count; i++)
            {
                if (m_Targets[i].Interactable != null &&
                    source == m_Targets[i].Interactable.gameObject)
                {
                    return m_Targets[i];
                }
            }

            return null;
        }
    }
}
