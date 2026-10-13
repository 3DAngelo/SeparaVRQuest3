// Outline of the highlighted container (see Container.cs, which draws it with Graphics.RenderMesh).
//
// Technique: the mesh is drawn a second time showing only its BACK faces, with every vertex pushed away, on screen,
// from the projected centre of the container by a constant width. What sticks out around the real mesh is the outline.
// It does not use the normals, so the hard edges of a box do not open gaps (a normal-extruded hull would split there).
//
// Lives in Assets/Resources so that it is always included in the build; loaded with Resources.Load<Shader>("ContainerOutline").
// Single Pass Instanced (Quest): the stereo macros below are required, do not remove them.
Shader "SeparaVR/ContainerOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 0.45, 0, 1)
        _OutlineWidth ("Width (fraction of view height)", Float) = 0.012
        _OutlineCenter ("Center (world)", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

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
                    // x is scaled by the aspect ratio so that the width is the same in pixels in both directions.
                    float aspect = _ScreenParams.x / _ScreenParams.y;
                    float2 fromCenter = positionCS.xy / positionCS.w - centerCS.xy / centerCS.w;
                    fromCenter.x *= aspect;
                    float len = length(fromCenter);
                    if (len > 1e-5)
                    {
                        float2 dir = fromCenter / len;
                        dir.x /= aspect;
                        // _OutlineWidth is a fraction of the view height; NDC spans 2 units.
                        positionCS.xy += dir * (2.0 * _OutlineWidth) * positionCS.w;
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
