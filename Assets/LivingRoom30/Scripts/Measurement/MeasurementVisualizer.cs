using UnityEngine;
using System.Collections.Generic;

namespace LivingRoom30.Measurement
{
    /// <summary>
    /// 测量可视化：管理 LineRenderer 对象池与 3D 文本标签对象池，
    /// 提供绘制线段（含可选路径样式与数值标签）、绘制包围盒棱线、整体回收等能力。
    /// 材质在运行时按 Unlit/Color 创建；文本使用旧版 TextMesh + 内置字体 LegacyRuntime.ttf，
    /// 不依赖 TextMeshPro 的 Essential Resources（该项目未导入，访问 TMP_Settings 会抛 NRE）。
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public class MeasurementVisualizer : MonoBehaviour
    {
        [Header("外观")]
        public Color lineColor = Color.yellow;
        public Color pathColor = Color.cyan;
        public float lineWidth = 0.012f;
        public float lineHeightOffset = 0.003f; // 线段整体向上浮起，避免与家具表面 Z-fight / 穿模

        [Header("3D 标签")]
        public float labelCharSize = 0.1f;   // 字符世界尺寸（越大标签越大）
        public int labelFontSize = 64;       // 字体光栅分辨率（越大越清晰）
        public Color labelColor = Color.white;
        public float labelOffset = 0.02f;    // 朝相机的推出偏移，避免嵌入表面

        public Camera facingCamera;          // 用于标签朝相机与偏移方向

        [Header("标签恒定屏幕尺寸")]
        public float labelScreenFraction = 0.02f; // 文字行高占屏高比例（0.02 ≈ 2%）
        public float labelMinScale = 0.02f;
        public float labelMaxScale = 0.50f;
        public float labelUnitHeight = 1f;        // 校准系数：实际字高 = charSize×fontSize×0.1×unitHeight

        private Material lineMaterial;
        private Material pathMaterial;
        private Material labelMaterial;
        private Font font;

        private readonly List<LineRenderer> linePool = new List<LineRenderer>();
        private readonly List<GameObject> labelPool = new List<GameObject>();
        private Transform poolParent;

        private void Awake()
        {
            poolParent = new GameObject("MeasurePool").transform;
            poolParent.SetParent(transform);
            poolParent.localPosition = Vector3.zero;

            if (lineMaterial == null) lineMaterial = new Material(Shader.Find("Unlit/Color")) { color = lineColor };
            if (pathMaterial == null) pathMaterial = new Material(Shader.Find("Unlit/Color")) { color = pathColor };

            // Unity 2022.2+ 已移除内置 Arial.ttf，改用 LegacyRuntime.ttf。
            // 旧版内置字体无需任何资源导入即可用，避免 TMP Essential Resources 缺失导致的 NRE。
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 标签材质：使用 GUI/Text Shader 并关闭深度测试，确保不会被家具表面遮挡
            if (font != null)
            {
                labelMaterial = new Material(font.material);
                labelMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                labelMaterial.SetInt("_ZWrite", 0);
                labelMaterial.renderQueue = 4000;
            }
        }

        // ---- 对象池 ----

        private LineRenderer GetLine(bool isPath)
        {
            foreach (var lr in linePool)
            {
                if (!lr.enabled) { lr.enabled = true; ConfigureLine(lr, isPath); return lr; }
            }
            var go = new GameObject("MeasureLine");
            go.transform.SetParent(poolParent);
            var lr2 = go.AddComponent<LineRenderer>();
            lr2.useWorldSpace = true;
            linePool.Add(lr2);
            lr2.enabled = true;
            ConfigureLine(lr2, isPath);
            return lr2;
        }

        private void ConfigureLine(LineRenderer lr, bool isPath)
        {
            lr.material = isPath ? pathMaterial : lineMaterial;
            lr.startWidth = lr.endWidth = lineWidth;
            lr.positionCount = 0;
            lr.numCapVertices = 4;
        }

        private GameObject GetLabel()
        {
            foreach (var g in labelPool)
            {
                if (!g.activeSelf)
                {
                    g.SetActive(true);
                    g.GetComponent<BillboardLabel>()?.ResetScale(); // 复用时重置缩放，防第一帧闪烁
                    return g;
                }
            }
            var go = new GameObject("MeasureLabel");
            go.transform.SetParent(poolParent);

            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = labelFontSize;
            tm.characterSize = labelCharSize;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Normal;
            tm.color = labelColor;

            // TextMesh 需 MeshRenderer 使用字体材质才能正确显示
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                if (labelMaterial != null) mr.sharedMaterial = labelMaterial;
                else if (font != null) mr.sharedMaterial = font.material;
                mr.sortingOrder = 10;
            }

            var billboard = go.AddComponent<BillboardLabel>();
            if (facingCamera != null) billboard.SetCamera(facingCamera.transform);
            billboard.SetScaling(labelScreenFraction, labelMinScale, labelMaxScale,
                labelCharSize * labelFontSize * 0.1f * labelUnitHeight);
            labelPool.Add(go);
            return go;
        }

        private static void SetLabelText(GameObject label, string text)
        {
            var tm = label.GetComponent<TextMesh>();
            if (tm != null) tm.text = text;
        }

        /// <summary>运行时修改线框/路径颜色（邻近测距等独立实例用于区分配色）。</summary>
        public void SetColors(Color line, Color path)
        {
            lineColor = line;
            pathColor = path;
            if (lineMaterial != null) lineMaterial.color = line;
            if (pathMaterial != null) pathMaterial.color = path;
        }

        /// <summary>切换标签朝向相机，并同步更新对象池里所有已创建标签的 BillboardLabel。</summary>
        public void SetFacingCamera(Camera cam)
        {
            facingCamera = cam;
            foreach (var lbl in labelPool)
            {
                if (lbl == null) continue;
                var billboard = lbl.GetComponent<BillboardLabel>();
                if (billboard != null) billboard.SetCamera(cam != null ? cam.transform : null);
            }
        }

        /// <summary>统一设置标签恒定屏幕尺寸参数，并同步到池内所有已创建标签。
        /// TextMesh 实际字高 ≈ characterSize × fontSize × 0.1（em 高），unitHeight 用于手动校准。</summary>
        public void SetLabelScaling(float fraction, float min, float max, float unitHeight)
        {
            labelScreenFraction = fraction;
            labelMinScale = min;
            labelMaxScale = max;
            labelUnitHeight = unitHeight;
            float baseHeight = labelCharSize * labelFontSize * 0.1f * labelUnitHeight;
            foreach (var lbl in labelPool)
            {
                if (lbl == null) continue;
                lbl.GetComponent<BillboardLabel>()?.SetScaling(fraction, min, max, baseHeight);
            }
        }

        // ---- 对外绘制 API ----

        /// <summary>绘制一条线段，并在中点放置数值标签（可选）。返回该线段的 LineRenderer。</summary>
        public LineRenderer DrawLine(Vector3 a, Vector3 b, bool isPath, string label = null)
        {
            var lr = GetLine(isPath);
            lr.positionCount = 2;
            Vector3 lift = Vector3.up * lineHeightOffset;
            lr.SetPosition(0, a + lift);
            lr.SetPosition(1, b + lift);
            if (!string.IsNullOrEmpty(label))
            {
                var lbl = GetLabel();
                // 数值放在线段中点正上方，不做朝相机偏移（避免贴脸感）
                lbl.transform.position = (a + b) * 0.5f + Vector3.up * labelOffset;
                SetLabelText(lbl, label);
            }
            return lr;
        }

        /// <summary>绘制包围盒 12 条棱；showEdgeLengths 为真时逐条标注边长。</summary>
        public void DrawBox(Bounds b, bool showEdgeLengths)
        {
            GeometryUtils.GetBoxEdges(b, out Vector3[] corners, out int[][] edges, out int[] edgeAxis);
            for (int i = 0; i < edges.Length; i++)
            {
                Vector3 p0 = corners[edges[i][0]];
                Vector3 p1 = corners[edges[i][1]];
                string label = showEdgeLengths ? GeometryUtils.Meters(Vector3.Distance(p0, p1)) : null;
                DrawLine(p0, p1, false, label);
            }
        }

        /// <summary>在指定位置放置一个独立的数值标签（用于路径总长等），默认向上抬升一点。</summary>
        public GameObject PlaceLabel(Vector3 position, string text)
        {
            var lbl = GetLabel();
            lbl.transform.position = position + Vector3.up * labelOffset;
            SetLabelText(lbl, text);
            return lbl;
        }

        /// <summary>回收所有线框与标签（切换模式/清空时调用）。</summary>
        public void Clear()
        {
            foreach (var lr in linePool) { lr.enabled = false; lr.positionCount = 0; }
            foreach (var g in labelPool) g.SetActive(false);
        }
    }
}
