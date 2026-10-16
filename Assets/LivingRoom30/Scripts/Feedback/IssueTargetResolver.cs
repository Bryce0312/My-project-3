using UnityEngine;
using LivingRoom30;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 命中物体的语义解析：把射线命中的任意 Transform 归一化为「家具根 + 可读名称 + 家具 ID」。
    ///
    /// 这是 E 对外公开的稳定工具，D（选择与标注）在创建标注时可直接复用，
    /// 从而保证「选择」与「反馈」对同一物体的命名完全一致（PROJECT_BRIEF：
    /// 能关联具体家具或构件时，应保存目标对象标识）。
    ///
    /// 与测量模块 <c>GeometryUtils.FindFurnitureRoot</c> 逻辑等价但**刻意独立实现**，
    /// 避免 E 对 C 的分支产生编译期依赖；两者可并存，互不影响。
    /// </summary>
    public static class IssueTargetResolver
    {
        /// <summary>家具分组节点名（由 SceneNewBuilder 生成的固定层级）。</summary>
        public const string FurnitureGroupName = "F00_Furniture";

        /// <summary>
        /// 反查家具根物体：优先取带 <see cref="FurnitureInfo"/> 的祖先；
        /// 否则若某级父节点为 F00_Furniture 则取该子物体；都不满足时回退为命中的物体本身。
        /// </summary>
        public static GameObject FindFurnitureRoot(Transform hit)
        {
            if (hit == null) return null;

            var info = hit.GetComponentInParent<FurnitureInfo>();
            if (info != null) return info.gameObject;

            Transform cursor = hit;
            while (cursor != null)
            {
                if (cursor.parent != null && cursor.parent.name == FurnitureGroupName)
                    return cursor.gameObject;
                cursor = cursor.parent;
            }
            return hit.gameObject;
        }

        /// <summary>
        /// 解析目标语义。非家具（墙、地面、玻璃等建筑构件）也能得到可读名称，
        /// 只是 <paramref name="furnitureId"/> 为空——反馈仍可通过世界坐标定位。
        /// </summary>
        public static void Describe(GameObject root, out string furnitureId, out string displayName, out string objectName)
        {
            furnitureId = "";
            objectName = root != null ? root.name : "";
            displayName = objectName;

            if (root == null) return;

            var info = root.GetComponentInParent<FurnitureInfo>();
            if (info == null) return;

            if (!string.IsNullOrEmpty(info.FurnitureId)) furnitureId = info.FurnitureId;
            if (!string.IsNullOrEmpty(info.EnglishName)) displayName = info.EnglishName;
            else if (!string.IsNullOrEmpty(info.FurnitureId)) displayName = info.FurnitureId;
        }

        /// <summary>人类可读的一行目标描述，用于 UI 与 Toast。</summary>
        public static string DescribeLine(GameObject root)
        {
            string id, name, objectName;
            Describe(root, out id, out name, out objectName);
            if (string.IsNullOrEmpty(name)) return "空间位置";
            return string.IsNullOrEmpty(id) ? name : name + "（" + id + "）";
        }
    }
}
