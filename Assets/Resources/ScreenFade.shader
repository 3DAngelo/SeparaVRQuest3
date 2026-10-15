// Full-screen fade overlay (see ScreenFader.cs, which draws it every frame with Graphics.RenderMesh while it is visible).
//
// The mesh is a quad whose vertices are already clip-space coordinates (-1..1): the vertex shader ignores every matrix and
// outputs them as they are, so the quad covers the whole view of each eye whatever the camera, head pose or projection.
// ZTest Always + the last transparent queue: drawn after (on top of) everything else in the scene.
// The colour is uniform, so a vertical flip of the projection (rendering into an intermediate texture) does not matter,
// and Cull Off makes the winding irrelevant.
//
// Replaces VRScreenFade's OnPostRender/GL path, which URP never calls.
//
// Lives in Assets/Resources so that it is always included in the build; loaded with Resources.Load<Shader>("ScreenFade").
// Single Pass Instanced (Quest): the stereo macros below are required, do not remove them.
Shader "SeparaVR/ScreenFade"
{
    Properties
    {
        _Color ("Color (alpha = opacity)", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        // Queue 5000 = Overlay+1000, the highest queue and still inside URP's transparent range (2501..5000).
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay+1000" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "ScreenFade"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
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
                // Halfway towards the near plane: UNITY_NEAR_CLIP_VALUE is 1 with reversed Z (D3D, Vulkan, Metal) and -1
                // on OpenGL, so 0.5 * it is inside the clip volume on every API and the quad is never clipped
                // (exactly on the near plane it could be, by precision). With ZTest Always the depth itself is irrelevant.
                output.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE * 0.5, 1.0);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return _Color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
