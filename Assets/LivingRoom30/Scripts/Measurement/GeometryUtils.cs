using UnityEngine;
using LivingRoom30;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 纯几何工具：包围盒聚合、物体对齐长宽高、12 条棱角点、表面间隙、家具根反查。
    /// 不依赖任何 UnityEditor 或 XR 命名空间，可在运行时安全使用。
    /// </summary>
    public static class GeometryUtils
    {
        /// <summary>
        /// 聚合 root 下所有 MeshRenderer（含未激活子物体）的世界 AABB。
        /// 若没有任何 MeshRenderer，则退化为 Collider 聚合；再不行返回以 root 为中心、零尺寸包围盒。
        /// </summary>
        public static Bounds GetWorldAABB(GameObject root)
        {
            if (root == null) return new Bounds(Vector3.zero, Vector3.zero);

            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    b.Encapsulate(renderers[i].bounds);
                return b;
            }

            var colliders = root.GetComponentsInChildren<Collider>(true);
            if (colliders.Length > 0)
            {
                Bounds b = colliders[0].bounds;
                for (int i = 1; i < colliders.Length; i++)
                    b.Encapsulate(colliders[i].bounds);
                return b;
            }

            return new Bounds(root.transform.position, Vector3.zero);
        }

        /// <summary>
        /// 物体对齐的长/宽/高：把世界 AABB 的 8 个角点用 root 的 worldToLocalMatrix 变换到局部空间，
        /// 再求局部 min/max。可避免物体旋转导致世界 AABB 被“撑大”的伪高值。
        /// 返回值按局部坐标系的 (x=长, y=高, z=宽) 方向给出尺寸（即 localScale 所定义的方向）。
        /// </summary>
        public static Vector3 GetObjectAlignedSize(GameObject root)
        {
            Bounds world = GetWorldAABB(root);
            if (world.size == Vector3.zero) return Vector3.zero;

            Matrix4x4 toLocal = root.transform.worldToLocalMatrix;
            Vector3 c = world.center;
            Vector3 e = world.size * 0.5f;

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
                Vector3 local = toLocal.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            return max - min;
        }

        /// <summary>
        /// 取出包围盒的 8 个角点与 12 条棱（每条棱标注所属轴：0=X / 1=Y / 2=Z）。
        /// 角点索引位：bit0 = x 符号, bit1 = y 符号, bit2 = z 符号。
        /// </summary>
        public static void GetBoxEdges(Bounds b, out Vector3[] corners, out int[][] edges, out int[] edgeAxis)
        {
            corners = new Vector3[8];
            Vector3 c = b.center;
            Vector3 e = b.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                corners[i] = c + new Vector3(
                    (i & 1) == 0 ? -e.x : e.x,
                    (i & 2) == 0 ? -e.y : e.y,
                    (i & 4) == 0 ? -e.z : e.z);
            }

            edges = new int[12][];
            edgeAxis = new int[12];
            int k = 0;
            // 沿 X 的 4 条棱（两端仅 x 不同）
            AddEdge(edges, edgeAxis, ref k, 0, 1, 0);
            AddEdge(edges, edgeAxis, ref k, 2, 3, 0);
            AddEdge(edges, edgeAxis, ref k, 4, 5, 0);
            AddEdge(edges, edgeAxis, ref k, 6, 7, 0);
            // 沿 Y 的 4 条棱
            AddEdge(edges, edgeAxis, ref k, 0, 2, 1);
            AddEdge(edges, edgeAxis, ref k, 1, 3, 1);
            AddEdge(edges, edgeAxis, ref k, 4, 6, 1);
            AddEdge(edges, edgeAxis, ref k, 5, 7, 1);
            // 沿 Z 的 4 条棱
            AddEdge(edges, edgeAxis, ref k, 0, 4, 2);
            AddEdge(edges, edgeAxis, ref k, 1, 5, 2);
            AddEdge(edges, edgeAxis, ref k, 2, 6, 2);
            AddEdge(edges, edgeAxis, ref k, 3, 7, 2);
        }

        private static void AddEdge(int[][] edges, int[] edgeAxis, ref int k, int a, int b, int axis)
        {
            edges[k] = new int[] { a, b };
            edgeAxis[k] = axis;
            k++;
        }

        /// <summary>两包围盒表面最近距离（互取 ClosestPoint），用于“两物体距离”。</summary>
        public static float SurfaceGap(Bounds a, Bounds b)
        {
            Vector3 pa = a.ClosestPoint(b.center);
            Vector3 pb = b.ClosestPoint(a.center);
            return Vector3.Distance(pa, pb);
        }

        /// <summary>两包围盒中心距离（补充展示）。</summary>
        public static float CenterDistance(Bounds a, Bounds b)
        {
            return Vector3.Distance(a.center, b.center);
        }

        /// <summary>
        /// 从被射线命中的 Transform 反查家具根物体：
        /// 优先取带 FurnitureInfo 的祖先；否则若其父为 F00_Furniture 则取该子物体；否则回退为命中的物体本身。
        /// </summary>
        public static GameObject FindFurnitureRoot(Transform t)
        {
            if (t == null) return null;

            var info = t.GetComponentInParent<FurnitureInfo>();
            if (info != null) return info.gameObject;

            Transform p = t;
            while (p != null)
            {
                if (p.parent != null && p.parent.name == "F00_Furniture")
                    return p.gameObject;
                p = p.parent;
            }
            return t.gameObject;
        }

        /// <summary>米制距离转字符串（2 位小数）。</summary>
        public static string Meters(float meters)
        {
            return meters.ToString("F2") + " m";
        }
    }
}
