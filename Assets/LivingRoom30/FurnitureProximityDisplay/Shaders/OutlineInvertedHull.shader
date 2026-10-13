Shader "LivingRoom30/OutlineInvertedHull"
{
    // =========================================================================
    // Inverted-hull (back-face normal extrusion) outline.
    //
    // WHY THIS TECHNIQUE (instead of an axis-aligned bounding box):
    //   The outline is generated directly from the focused furniture's REAL mesh geometry. Curved
    //   surfaces therefore produce curved outlines and polygonal boundaries produce polygonal
    //   outlines -- the silhouette traces the actual modeling contour exactly. There is NO
    //   rectangular / AABB approximation and NO gap between the outline and the surface: the
    //   outline shell shares the furniture mesh as its source, merely offset outward by a small,
    //   constant WORLD-space width (_OutlineWidth).
    //
    // HOW IT WORKS (classic inverted hull):
    //   We render ONLY the mesh's back faces (Cull Front) and push each vertex outward along its
    //   world-space normal by _OutlineWidth. The furniture's own front faces (drawn by its own
    //   material at the true surface) are closer to the camera and occlude every part of this shell
    //   EXCEPT the rim that pokes past the silhouette. The result is a clean rim hugging the contour.
    //
    // DEPTH / XR NOTES:
    //   - ZTest LEqual + ZWrite On: the rim is occluded by real geometry in front of it (so it reads
    //     as a solid 3D rim around the furniture and never z-fights the surface, because the shell is
    //     spatially offset from it by _OutlineWidth). Geometry-based, so it works in stereo VR with
    //     no post-process render targets -- safe for Quest 3 / Quest Link.
    //   - Assumes the focused furniture uses an OPAQUE material that writes depth. If the furniture
    //     were transparent (depth write off), the inner shell would also show (still frames the
    //     piece, just looks filled rather than rim-only).
    //   - Extrusion is done in WORLD space using the scale-independent world normal, so the rim width
    //     stays constant in metres even under non-uniform furniture scaling (1 unit = 1 m).
    // =========================================================================

    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.2, 0.9, 1.0, 1.0)
        _OutlineWidth ("Outline Width (metres, world space)", Float) = 0.012
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        // Draw back faces only; they are extruded outward (see vert) so the furniture's own
        // front faces occlude everything but the rim. This is the "inverted hull".
        Cull Front
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            fixed4 _OutlineColor;
            float _OutlineWidth;

            v2f vert (appdata v)
            {
                v2f o;

                // 1) Vertex -> world space (model matrix already applied here).
                float4 worldPos = mul(unity_ObjectToWorld, v.vertex);

                // 2) World-space, scale-independent normal (handles non-uniform scaling correctly).
                float3 worldNormal = UnityObjectToWorldNormal(v.normal);

                // 3) Extrude outward by a constant WORLD-space width (1 unit = 1 m). This is what
                //    guarantees a uniform, gap-free rim that follows the actual mesh contour.
                worldPos.xyz += worldNormal * _OutlineWidth;

                // 4) World -> clip via view-projection (model matrix already applied in step 1).
                o.pos = mul(UNITY_MATRIX_VP, worldPos);

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Flat unlit outline colour -- no lighting, so the rim reads clearly in VR.
                return _OutlineColor;
            }
            ENDCG
        }
    }

    // No fallback: this is a purpose-built unlit opaque outline pass.
    FallBack Off
}
