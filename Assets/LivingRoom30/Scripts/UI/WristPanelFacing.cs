using UnityEngine;

namespace LivingRoom30
{
    /// <summary>
    /// Turns a panel to face the reviewer every frame.
    ///
    /// Only the orientation is driven here; the position comes from the panel being parented to a
    /// controller, which is what makes it a wrist panel. The rotation matches the one
    /// <see cref="HudLazyFollow"/> uses — forward points from the viewer to the panel, which is the
    /// side a world space canvas reads correctly from.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WristPanelFacing : MonoBehaviour
    {
        [SerializeField] private Transform headCamera;

        public Transform HeadCamera => headCamera;

        public void Configure(Transform head)
        {
            headCamera = head;
        }

        private void LateUpdate()
        {
            if (headCamera == null)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(transform.position - headCamera.position, Vector3.up);
        }
    }
}
