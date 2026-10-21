// Outline of a highlighted object (see HighlightPulse.cs, which draws it with Graphics.RenderMesh).
//
// Technique, two passes over the same mesh:
//  1. "Mask": the mesh as it is, invisible (ColorMask 0), marking its silhouette in the stencil buffer.
//  2. "Outline": the mesh with every vertex pushed away, on screen, from the projected centre of the object, drawn only
//     where the stencil is NOT marked. So the colour can only appear around the real object, never on top of it (open
//     bins, single-sided walls or transparent materials included).
// It does not use the normals, so the hard edges of a box do not open gaps (a normal-extruded hull would split there).
//
// Border width: _OutlineWidth world units, but never less than _OutlineMinPixels pixels of the render target (per eye
// in XR), so it still reads as a border on small or distant objects. The push is per screen axis (left/right by the
// width, up/down by the width), not radial: a radial push only moves the side edges of a tall object by a fraction of
// the width (the cosine of the angle), which made those sides vanish.
//
// HighlightPulse draws the two passes with two materials (pass toggled off with SetShaderPassEnabled) in two render
// queues, Mask first, so the masks of ALL the renderers of the object are in the stencil before any outline is drawn.
//
// Lives in Assets/Resources so that it is always included in the build; loaded with Resources.Load<Shader>("ContainerOutline").
// Single Pass Instanced (Quest): the stereo macros below are required, do not remove them.
Shader "SeparaVR/ContainerOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.45, 0, 1)
        _OutlineWidth ("Width (world units)", Float) = 0.05
        _OutlineCenter ("Center (world)", Vector) = (0, 0, 0, 1)
        _OutlineMinPixels ("Min width (pixels)", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _OutlineColor;
            float4 _OutlineCenter;
            float _OutlineWidth;
            float _OutlineMinPixels;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        ENDHLSL

        Pass
        {
            Name "Mask"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            ZTest Always
            ColorMask 0
            // Top stencil bit only, so it does not clash with anything else using the stencil.
            Stencil { Ref 128 WriteMask 128 Comp Always Pass Replace }

            HLSLPROGRAM
            #pragma vertex vertMask
            #pragma fragment fragMask
            #pragma multi_compile_instancing

            Varyings vertMask(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 fragMask(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            // Draw only outside the silhouette marked by "Mask".
            Stencil { Ref 0 ReadMask 128 WriteMask 128 Comp Equal }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float4 centerCS = TransformWorldToHClip(_OutlineCenter.xyz);

                // Only when both the vertex and the centre are in front of the camera (otherwise the division is meaningless).
                if (positionCS.w > 0.01 && centerCS.w > 0.01)
                {
                    // Size of the render target in pixels, render scale included (per eye in XR).
                    float2 screenPx = _ScaledScreenParams.xy;
                    // Offset of the vertex from the centre, in pixels.
                    float2 fromCenterPx = (positionCS.xy / positionCS.w - centerCS.xy / centerCS.w) * screenPx * 0.5;
                    // Per axis: -1 / +1 beyond one pixel from the centre lines, linear within it. Continuous in the
                    // position, so vertices that share a position (seams, hard edges) move together and no gaps open.
                    float2 dir = clamp(fromCenterPx, -1.0, 1.0);
                    // A world length d at view depth w spans d * |P[0][0]| / w (x) or d * |P[1][1]| / w (y) in NDC; the
                    // clip-space offset is that times w, so the depth cancels and the border stays _OutlineWidth world
                    // units wide at any distance. abs: the GPU projection has P[1][1] < 0 when it is flipped to render
                    // into a texture (URP intermediate / XR eye textures), which would push the vertices inward.
                    float2 worldCS = _OutlineWidth * abs(float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11));
                    // n pixels are 2 n / size in NDC, times w in clip space.
                    float2 minCS = (2.0 * _OutlineMinPixels / screenPx) * positionCS.w;
                    positionCS.xy += dir * max(worldCS, minCS);
                }
                output.positionCS = positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(_OutlineColor.rgb, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
