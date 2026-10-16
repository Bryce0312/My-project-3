using System.Collections.Generic;
using UnityEngine;

namespace LivingRoom30.Feedback
{
    /// <summary>
    /// 空间标注的生命周期管理：负责在场景中生成 / 维护 / 清理 <see cref="IssueMarker"/>，
    /// 并保证「已提交的问题在场景中持续可见」（需求 UN-04、目标 G5）。
    ///
    /// 与数据层的关系：只读 <see cref="IssueStore"/> 的记录来渲染，不修改数据。
    /// 与相机的关系：相机在 XR / 桌面之间切换时，由 FeedbackService 统一调用 <see cref="SetFacingCamera"/>
    /// 刷新所有标签朝向。
    /// </summary>
    public sealed class IssueMarkerManager
    {
        private readonly Dictionary<string, IssueMarker> markers = new Dictionary<string, IssueMarker>();
        private readonly Transform container;
        private Camera facingCamera;

        public IssueMarkerManager(Transform parent)
        {
            var go = new GameObject("IssueMarkers");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            container = go.transform;
        }

        public int Count { get { return markers.Count; } }

        public Transform Container { get { return container; } }

        /// <summary>相机切换时统一注入（XR ↔ 桌面）。</summary>
        public void SetFacingCamera(Camera cam)
        {
            facingCamera = cam;
            foreach (var pair in markers)
                if (pair.Value != null) pair.Value.SetFacingCamera(cam);
        }

        /// <summary>
        /// 为一条记录生成标注。若同 ID 已存在则先移除，保证幂等。
        /// </summary>
        public IssueMarker Spawn(IssueRecord record)
        {
            if (record == null) return null;

            Remove(record.id);

            string labelText = "ISSUE #" + record.id + "\n" + IssueCatalog.CategoryEnglish(record.CategoryEnum);
            var marker = IssueMarker.Create(container, record.id, record.position.ToVector3(),
                record.CategoryEnum, labelText, facingCamera);
            markers[record.id] = marker;
            return marker;
        }

        /// <summary>根据数据层全量重建（用于载入历史会话或数据被外部替换后同步）。</summary>
        public void RebuildFrom(IssueStore store)
        {
            Clear();
            if (store == null) return;
            var all = store.All;
            for (int i = 0; i < all.Count; i++)
                Spawn(all[i]);
        }

        public bool Remove(string id)
        {
            IssueMarker marker;
            if (!markers.TryGetValue(id, out marker)) return false;
            markers.Remove(id);
            if (marker != null) Object.Destroy(marker.gameObject);
            return true;
        }

        public void Clear()
        {
            foreach (var pair in markers)
                if (pair.Value != null) Object.Destroy(pair.Value.gameObject);
            markers.Clear();
        }

        /// <summary>脉冲高亮某条标注（问题汇总列表点击「定位」时调用）。</summary>
        public bool Highlight(string id)
        {
            IssueMarker marker;
            if (!markers.TryGetValue(id, out marker) || marker == null) return false;
            marker.Highlight();
            return true;
        }

        public IssueMarker Get(string id)
        {
            IssueMarker marker;
            return markers.TryGetValue(id, out marker) ? marker : null;
        }
    }
}
