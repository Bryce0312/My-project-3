using System;
using System.Collections.Generic;
using UnityEngine;

namespace LivingRoom30.Review
{
    [Serializable]
    public sealed class ReviewAnnotationRecord
    {
        public string id, targetId, targetName, category, reason, utc, pointId;
        public Vector3 targetPosition;
    }

    /// <summary>
    /// The journey owns this bridge and its persistence callback. There is deliberately no
    /// automatic bootstrap: annotations cannot silently claim success without a saved review.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReviewAnnotationStore : MonoBehaviour
    {
        public const string SizeCategory = "inappropriate size";
        public const string CrampedCategory = "too cramped";
        public const string LayoutCategory = "strange layout";
        public const int ReasonLimit = 500;

        public static ReviewAnnotationStore Active { get; private set; }
        public Func<ReviewAnnotationRecord, string> Submit { get; set; }
        public Func<bool> CanOpen { get; set; }

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            if (Active == this) Active = null;
        }

        public static string NormalizeCategory(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized == SizeCategory || normalized == CrampedCategory || normalized == LayoutCategory
                ? normalized : null;
        }

        public bool TrySubmit(GameObject target, string category, string reason, out string error)
        {
            error = null;
            string normalizedCategory = NormalizeCategory(category);
            if (target == null) error = "This object is no longer available. Keep the draft and try again.";
            else if (normalizedCategory == null) error = "Choose an issue category first.";
            else if (string.IsNullOrWhiteSpace(reason)) error = "Add a reason before saving the annotation.";
            else if (reason.Trim().Length > ReasonLimit) error = "Keep the reason within 500 characters.";
            else if (!isActiveAndEnabled || Active != this || Submit == null)
                error = "The review is not ready to save. Your draft is still here.";
            if (error != null) return false;

            FurnitureInfo info = target.GetComponent<FurnitureInfo>();
            var annotation = new ReviewAnnotationRecord
            {
                id = Guid.NewGuid().ToString("N"),
                targetId = info != null && !string.IsNullOrWhiteSpace(info.FurnitureId)
                    ? info.FurnitureId : HierarchyId(target.transform),
                targetName = info != null && !string.IsNullOrWhiteSpace(info.EnglishName)
                    ? info.EnglishName : target.name,
                category = normalizedCategory,
                reason = reason.Trim(),
                utc = DateTime.UtcNow.ToString("o"),
                pointId = string.Empty,
                targetPosition = target.transform.position,
            };
            try
            {
                error = Submit(annotation);
                return string.IsNullOrEmpty(error);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not save the review annotation: " + exception.Message, this);
                error = "Could not save the annotation. Your draft is still here; try again.";
                return false;
            }
        }

        private static string HierarchyId(Transform target)
        {
            var names = new List<string>();
            Transform current = target;
            while (current != null)
            {
                names.Add(current.name.Replace("\\", "\\\\").Replace("/", "\\/")
                    + "[" + current.GetSiblingIndex() + "]");
                current = current.parent;
            }
            names.Reverse();
            string scene = string.IsNullOrEmpty(target.gameObject.scene.path)
                ? target.gameObject.scene.name : target.gameObject.scene.path;
            return scene + ":/" + string.Join("/", names);
        }
    }
}
