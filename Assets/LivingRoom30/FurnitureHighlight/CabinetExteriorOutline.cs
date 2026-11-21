using UnityEngine;
using UnityEngine.Rendering;

namespace LivingRoom30
{
    /// <summary>One exterior frame for F09; internal shelves never become outline targets.</summary>
    [DisallowMultipleComponent]
    public sealed class CabinetExteriorOutline : MonoBehaviour
    {
        // A walk along the twelve box edges. Repeated edges connect the path without diagonals.
        private static readonly int[] EdgeWalk = { 0, 1, 2, 3, 0, 4, 5, 1, 5, 6, 2, 6, 7, 3, 7, 4, 0 };
        private readonly Vector3[] framePoints = new Vector3[17];
        private readonly Vector3[] doorPoints = new Vector3[5];
        private MeshFilter body;
        private readonly MeshFilter[] doors = new MeshFilter[2];
        private LineRenderer frame;
        private readonly LineRenderer[] doorLines = new LineRenderer[2];
        private Material ink;
        private bool highlightVisible, proximityVisible;
        private Color highlightColor, proximityColor;
        private float highlightWidth = .007f, proximityWidth = .007f;

        public static CabinetExteriorOutline GetOrCreate(GameObject root)
        {
            var info = root.GetComponent<FurnitureInfo>();
            if (info == null || info.FurnitureId != "F09") return null;
            var existing = root.GetComponent<CabinetExteriorOutline>();
            if (existing != null) return existing;
            Transform source = root.transform.Find("CabinetBody");
            if (source == null || source.GetComponent<MeshFilter>() == null) return null;
            return root.AddComponent<CabinetExteriorOutline>();
        }

        private void Awake()
        {
            body = transform.Find("CabinetBody")?.GetComponent<MeshFilter>();
            // The review system may already have reparented the doors under their hinges.
            foreach (var mesh in GetComponentsInChildren<MeshFilter>())
            {
                if (mesh.name == "GlassDoor_Left") doors[0] = mesh;
                else if (mesh.name == "GlassDoor_Right") doors[1] = mesh;
            }
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null || body == null || body.sharedMesh == null) { enabled = false; return; }
            ink = new Material(shader) { name = "CabinetExteriorOutline_Ink" };
            frame = CreateLine("CabinetExterior_Frame", framePoints.Length);
            for (int i = 0; i < doors.Length; i++)
                if (doors[i] != null) doorLines[i] = CreateLine("CabinetExterior_Door_" + i, 5);
        }

        private LineRenderer CreateLine(string objectName, int count)
        {
            var host = new GameObject(objectName);
            host.transform.SetParent(transform, false);
            var line = host.AddComponent<LineRenderer>();
            line.sharedMaterial = ink;
            line.useWorldSpace = true;
            line.positionCount = count;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        public void SetHighlight(bool visible, Color color, float pixelWidth)
        {
            highlightVisible = visible;
            highlightColor = color;
            highlightWidth = Mathf.Clamp(pixelWidth * .0014f, .002f, .014f);
            ApplyStyle();
        }

        public void SetProximity(bool visible, Color color, float worldWidth)
        {
            proximityVisible = visible;
            proximityColor = color;
            proximityWidth = Mathf.Clamp(worldWidth * .6f, .002f, .014f);
            ApplyStyle();
        }

        private void ApplyStyle()
        {
            bool visible = isActiveAndEnabled && (highlightVisible || proximityVisible);
            Color color = highlightVisible ? highlightColor : proximityColor;
            float width = highlightVisible ? highlightWidth : proximityWidth;
            ApplyLine(frame, visible, color, width);
            for (int i = 0; i < doorLines.Length; i++)
            {
                // Closed doors share the cabinet's exterior; omit the centre seam.
                bool swung = doors[i] != null && Mathf.Abs(Mathf.DeltaAngle(transform.eulerAngles.y,
                    doors[i].transform.eulerAngles.y)) > 2f;
                ApplyLine(doorLines[i], visible && swung, color, width);
            }
            if (visible) UpdatePoints();
        }

        private static void ApplyLine(LineRenderer line, bool visible, Color color, float width)
        {
            if (line == null) return;
            line.startColor = line.endColor = color;
            line.startWidth = line.endWidth = width;
            line.enabled = visible;
        }

        private void LateUpdate()
        {
            if (highlightVisible || proximityVisible) ApplyStyle();
        }

        private void UpdatePoints()
        {
            if (body == null || body.sharedMesh == null || frame == null) return;
            Bounds bounds = body.sharedMesh.bounds;
            for (int i = 0; i < EdgeWalk.Length; i++)
                framePoints[i] = body.transform.TransformPoint(Corner(bounds, EdgeWalk[i]));
            frame.SetPositions(framePoints);
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] == null || doors[i].sharedMesh == null || doorLines[i] == null || !doorLines[i].enabled) continue;
                Bounds door = doors[i].sharedMesh.bounds;
                for (int p = 0; p < doorPoints.Length; p++)
                    doorPoints[p] = doors[i].transform.TransformPoint(Corner(door, p % 4));
                doorLines[i].SetPositions(doorPoints);
            }
        }

        private static Vector3 Corner(Bounds bounds, int index)
        {
            bool right = index == 1 || index == 2 || index == 5 || index == 6;
            bool top = index == 2 || index == 3 || index == 6 || index == 7;
            return bounds.center + Vector3.Scale(bounds.extents,
                new Vector3(right ? 1 : -1, top ? 1 : -1, index >= 4 ? 1 : -1));
        }

        private void OnDisable()
        {
            if (frame != null) frame.enabled = false;
            foreach (var line in doorLines) if (line != null) line.enabled = false;
        }

        private void OnDestroy()
        {
            if (ink != null) Destroy(ink);
        }
    }
}
