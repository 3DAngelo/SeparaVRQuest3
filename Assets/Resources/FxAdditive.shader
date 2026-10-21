// Additive, unlit particle shader for the feedback effects (see FeedbackFX.cs): hand beam, sparkles over the pointed
// object, container fireworks.
//
// colour = particle vertex colour (rgb * alpha) * texture alpha * _Intensity, added on top of what is behind (Blend One
// One). No depth write, no lighting, no fog, no soft particles: as cheap as a particle can be on the Quest GPU.
// The texture is the procedural glow atlas made by FeedbackFX (white rgb, the shape in alpha).
//
// Lives in Assets/Resources so that it is always included in the build; loaded with Resources.Load<Shader>("FxAdditive").
// Single Pass Instanced (Quest): the stereo macros below are required, do not remove them.
Shader "SeparaVR/FxAdditive"
{
    Properties
    {
        _MainTex ("Glow atlas (alpha)", 2D) = "white" {}
        _Intensity ("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Pass
        {
            Name "FxAdditive"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half shape = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                half3 rgb = input.color.rgb * (input.color.a * shape * _Intensity);
                return half4(rgb, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
