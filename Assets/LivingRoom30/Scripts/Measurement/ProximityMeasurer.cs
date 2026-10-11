using UnityEngine;
using System.Collections.Generic;
using LivingRoom30;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 邻近自动测距：当观察者靠近「家具↔家具」「家具↔墙」的过道/间隙时，自动显示宽度
    /// （3D 标签 + 连线 + 侧栏汇总，按离观察者由近到远只取最近 maxDisplays 项）。
    /// 独立于点击式测量模式，作为常驻后台功能运行，拥有独立的可视化绘制池（不挤占点击测量的绘制）。
    /// </summary>
    public class ProximityMeasurer : MonoBehaviour
    {
        [Header("触发阈值")]
        public float observerThreshold = 3.5f;   // 观察者到特征点的最远距离，超过则不显示
        public float maxGap = 2.4f;              // 家具-家具 / 家具-墙 间隙超过此值不视为“过道”
        public float updateInterval = 0.25f;     // 刷新间隔（秒）
        public float eyeHeight = 0.9f;           // 桌面模式下观察点相对轨道焦点的抬高（模拟人眼高度）

        [Header("显示控制")]
        public int maxDisplays = 4;              // 最多同时绘制的测量项（按离观察者由近到远截取，防止糊屏）
        public float minGap = 0.10f;             // 小于此间隙视为贴面/噪声，不显示

        [Header("外观")]
        public Color proxColor = Color.green;

        private MeasurementVisualizer viz;
        private MeasureUIPanel ui;
        private Camera observerCam;
        private DesktopOrbitCamera orbit;

        private readonly List<GameObject> furniture = new List<GameObject>();
        private readonly List<Bounds> furnitureBounds = new List<Bounds>(); // 家具静态，Gather 时缓存 AABB
        private readonly List<Collider> walls = new List<Collider>();
        private float timer;
        private bool initialized;

        public void Init(Camera cam, DesktopOrbitCamera orb, MeasurementVisualizer v, MeasureUIPanel panel)
        {
            observerCam = cam;
            orbit = orb;
            viz = v;
            ui = panel;
            if (viz != null) viz.SetColors(proxColor, proxColor);
            Gather();
            initialized = true;
        }

        public void SetObserver(Camera cam) { observerCam = cam; }

        private void Gather()
        {
            furniture.Clear();
            furnitureBounds.Clear();
            walls.Clear();

            var fRoot = GameObject.Find("F00_Furniture");
            if (fRoot != null)
                foreach (var fi in fRoot.GetComponentsInChildren<FurnitureInfo>(true))
                {
                    furniture.Add(fi.gameObject);
                    furnitureBounds.Add(GeometryUtils.GetWorldAABB(fi.gameObject));
                }

            // 墙体取自建筑层；排除地板与天花板（否则家具到地板几乎为 0，会一直刷屏）。
            var aRoot = GameObject.Find("A00_Architecture");
            if (aRoot != null)
                foreach (var c in aRoot.GetComponentsInChildren<Collider>(true))
                {
                    string n = c.gameObject.name;
                    if (n.Contains("Floor") || n.Contains("Ceiling")) continue;
                    walls.Add(c);
                }
        }

        private void Update()
        {
            if (!initialized || !enabled || viz == null) return;
            timer += Time.deltaTime;
            if (timer < updateInterval) return;
            timer = 0f;
            Refresh();
        }

        /// <summary>观察者位置：桌面用轨道焦点（抬高到眼高），XR 用相机（头显）位置。</summary>
        private Vector3 Observer()
        {
            if (orbit != null && orbit.enabled && observerCam != null)
                return orbit.target + Vector3.up * eyeHeight;
            if (observerCam != null) return observerCam.transform.position;
            return Vector3.zero;
        }

        /// <summary>一条待显示的测量项：线段两端、测量值、离观察者距离与侧栏文案。</summary>
        private struct ProxCandidate
        {
            public Vector3 a;
            public Vector3 b;
            public float gap;
            public float obsDist;
            public string desc;
        }

        private readonly List<ProxCandidate> candidates = new List<ProxCandidate>();

        private void Refresh()
        {
            viz.Clear();
            Vector3 obs = Observer();
            if (obs == Vector3.zero) return;

            candidates.Clear();

            // 1) 家具与家具之间的过道/间隙
            for (int i = 0; i < furniture.Count; i++)
            {
                for (int j = i + 1; j < furniture.Count; j++)
                {
                    var bi = furnitureBounds[i];
                    var bj = furnitureBounds[j];
                    float gap = GeometryUtils.SurfaceGap(bi, bj);
                    if (gap > maxGap || gap < minGap) continue;
                    Vector3 pi = bi.ClosestPoint(bj.center);
                    Vector3 pj = bj.ClosestPoint(bi.center);
                    Vector3 mid = (pi + pj) * 0.5f;
                    float od = Vector3.Distance(obs, mid);
                    if (od > observerThreshold) continue;
                    string ni = furniture[i].GetComponent<FurnitureInfo>()?.EnglishName ?? furniture[i].name;
                    string nj = furniture[j].GetComponent<FurnitureInfo>()?.EnglishName ?? furniture[j].name;
                    candidates.Add(new ProxCandidate
                    {
                        a = pi, b = pj, gap = gap, obsDist = od,
                        desc = $"↔ {ni} / {nj}: {GeometryUtils.Meters(gap)}"
                    });
                }
            }

            // 2) 家具与墙之间的过道（每个家具只取间隙最小的一面墙，而非遍历中首个合格墙）
            for (int i = 0; i < furniture.Count; i++)
            {
                var b = furnitureBounds[i];
                string nf = furniture[i].GetComponent<FurnitureInfo>()?.EnglishName ?? furniture[i].name;

                float bestGap = float.MaxValue;
                Vector3 bestW = Vector3.zero, bestB = Vector3.zero;
                foreach (var w in walls)
                {
                    Vector3 cpW = w.bounds.ClosestPoint(b.center);
                    Vector3 cpB = b.ClosestPoint(cpW);
                    float gap = Vector3.Distance(cpW, cpB);
                    if (gap < bestGap)
                    {
                        bestGap = gap;
                        bestW = cpW;
                        bestB = cpB;
                    }
                }
                if (bestGap > maxGap || bestGap < minGap) continue;

                Vector3 mid = (bestW + bestB) * 0.5f;
                float od = Vector3.Distance(obs, mid);
                if (od > observerThreshold) continue;
                candidates.Add(new ProxCandidate
                {
                    a = bestW, b = bestB, gap = bestGap, obsDist = od,
                    desc = $"▢ {nf}—墙: {GeometryUtils.Meters(bestGap)}"
                });
            }

            // 按离观察者由近到远排序，只绘制最近 maxDisplays 项，避免几十个标签糊屏
            candidates.Sort((x, y) => x.obsDist.CompareTo(y.obsDist));

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("邻近自动测距");
            int shown = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (shown >= maxDisplays)
                {
                    sb.AppendLine($"…另有 {candidates.Count - shown} 项未显示");
                    break;
                }
                var c = candidates[i];
                viz.DrawLine(c.a, c.b, false, GeometryUtils.Meters(c.gap));
                sb.AppendLine(c.desc);
                shown++;
            }
            if (shown == 0) sb.AppendLine("（附近无可显示距离）");
            ui?.SetProximityReadout(sb.ToString());
        }

        private void OnDisable()
        {
            if (viz != null) viz.Clear();
            ui?.SetProximityReadout("邻近自动测距：已关闭");
        }
    }
}
