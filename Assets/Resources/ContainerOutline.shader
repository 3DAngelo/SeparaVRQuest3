// Outline of the highlighted container (see Container.cs, which draws it with Graphics.RenderMesh).
//
// Technique, two passes over the same mesh:
//  1. "Mask": the mesh as it is, invisible (ColorMask 0), marking its silhouette in the stencil buffer.
//  2. "Outline": the mesh with every vertex pushed away, on screen, from the projected centre of the container by a
//     constant width in WORLD units, drawn only where the stencil is NOT marked. So the colour can only appear around
//     the real object, never on top of it (open bins, single-sided walls or transparent materials included).
// It does not use the normals, so the hard edges of a box do not open gaps (a normal-extruded hull would split there).
//
// URP draws the SRPDefaultUnlit pass of a shader before its UniversalForward pass, which is what orders Mask -> Outline.
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
                    // x is scaled by the aspect ratio so that the width is the same in both directions.
                    float aspect = _ScreenParams.x / _ScreenParams.y;
                    float2 fromCenter = positionCS.xy / positionCS.w - centerCS.xy / centerCS.w;
                    fromCenter.x *= aspect;
                    float len = length(fromCenter);
                    if (len > 1e-5)
                    {
                        float2 dir = fromCenter / len;
                        dir.x /= aspect;
                        // A world length d at view depth w spans d * P[1][1] / w in NDC; the clip-space offset is that
                        // times w, so the depth cancels and the border stays _OutlineWidth world units wide at any distance.
                        positionCS.xy += dir * (_OutlineWidth * UNITY_MATRIX_P._m11);
                    }
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
