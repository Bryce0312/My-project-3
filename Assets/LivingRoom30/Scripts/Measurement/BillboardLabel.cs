using UnityEngine;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 让挂载的物体始终正对相机（公告板效果），并按与相机的距离缩放，
    /// 使标签保持「恒定屏幕尺寸」（近大远小按比例补偿，限制 min/max）。
    /// 默认用 Camera.main；也可通过 cameraToFace 指定参考相机（由 MeasurementVisualizer 注入）。
    /// 只改 localScale，不重建 TextMesh 网格（无 GC）；距离变化 <1% 时跳过（死区防抖）。
    /// </summary>
    public class BillboardLabel : MonoBehaviour
    {
        [Header("朝向")]
        public Transform cameraToFace;

        [Header("恒定屏幕尺寸")]
        public bool constantScreenSize = true;
        public float screenFraction = 0.02f;   // 文字行高占屏幕高度的比例（0.02 ≈ 2%）
        public float minScale = 0.02f;         // 缩放下限（基准字高约 0.64m，0.02 ≈ 0.013m 行高）
        public float maxScale = 0.50f;         // 缩放上限（0.50 ≈ 0.32m 行高，仅超远距离触顶）
        public float baseWorldHeight = 0.64f;  // localScale=1 时的文字 em 高（米）≈ characterSize × fontSize × 0.1

        private Camera camSource;
        private float lastDist = -1f;

        public void SetCamera(Transform cam)
        {
            cameraToFace = cam;
            camSource = cam != null ? cam.GetComponent<Camera>() : null;
        }

        /// <summary>由 MeasurementVisualizer 统一注入缩放参数（baseHeight = characterSize × unitHeight）。</summary>
        public void SetScaling(float fraction, float min, float max, float baseHeight)
        {
            constantScreenSize = true;
            screenFraction = fraction;
            minScale = min;
            maxScale = max;
            baseWorldHeight = Mathf.Max(0.001f, baseHeight);
        }

        /// <summary>标签被池复用/重新定位后调用，强制下一帧重算缩放。</summary>
        public void ResetScale()
        {
            lastDist = -1f;
            transform.localScale = Vector3.one;
        }

        private void LateUpdate()
        {
            Transform camT = cameraToFace;
            Camera cam = camSource;
            if (camT == null)
            {
                cam = Camera.main;
                camT = cam != null ? cam.transform : null;
            }
            if (camT == null) return;

            transform.rotation = camT.rotation;
            if (!constantScreenSize) return;

            float dist = Vector3.Distance(camT.position, transform.position);
            if (lastDist > 0f && Mathf.Abs(dist - lastDist) < lastDist * 0.01f) return;
            lastDist = dist;

            float halfFov = (cam != null ? cam.fieldOfView : 60f) * 0.5f * Mathf.Deg2Rad;
            float k = screenFraction * 2f * Mathf.Tan(halfFov);        // 每米距离对应的期望世界行高
            float s = Mathf.Clamp(k * dist / baseWorldHeight, minScale, maxScale);
            transform.localScale = Vector3.one * s;
        }
    }
}
