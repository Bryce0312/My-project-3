using UnityEngine;
using UnityEngine.AI;
using System;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// NavMesh 可行路线计算：把起止点投影到地面采样出合法 NavMesh 位置，
    /// 用 NavMesh.CalculatePath 求沿地面可行走的折线，并给出路径长度。
    /// 需场景已烘焙 NavMesh（见 MeasurementSetup 菜单或手动 Window→AI→Navigation→Bake）。
    /// </summary>
    public static class NavMeshPathDrawer
    {
        /// <summary>
        /// 尝试求 a→b 的可行走路径。
        /// 返回 true 表示成功且路径完整；corners 为折线点（含起止），length 为折线路径总长（米）。
        /// </summary>
        public static bool TryGetPath(Vector3 a, Vector3 b, out Vector3[] corners, out float length)
        {
            corners = null;
            length = 0f;

            if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 2.0f, NavMesh.AllAreas))
                return false;
            if (!NavMesh.SamplePosition(b, out NavMeshHit hb, 2.0f, NavMesh.AllAreas))
                return false;

            var path = new NavMeshPath();
            if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path))
                return false;
            if (path.status != NavMeshPathStatus.PathComplete)
                return false;

            corners = path.corners;
            length = PathLength(corners);
            return true;
        }

        private static float PathLength(Vector3[] c)
        {
            float l = 0f;
            for (int i = 1; i < c.Length; i++)
                l += Vector3.Distance(c[i - 1], c[i]);
            return l;
        }
    }
}
