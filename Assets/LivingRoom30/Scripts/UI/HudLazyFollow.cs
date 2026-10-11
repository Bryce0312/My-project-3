using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace LivingRoom30
{
    /// <summary>
    /// XRI 2.3.2 derives its billboard up vector from Cross(forward, up), which points down and
    /// renders world space canvases upside down. Only the rotation math is overridden here;
    /// position following and the lazy thresholds stay in LazyFollow.
    /// </summary>
    public sealed class HudLazyFollow : LazyFollow
    {
        protected override bool TryGetThresholdTargetRotation(out Quaternion newTarget)
        {
            newTarget = Quaternion.LookRotation(transform.position - target.position, Vector3.up);
            return true;
        }
    }
}
