// Reconstruccion aproximada: el shader original no es recuperable del APK.
// Grilla en espacio de mundo (plano XZ) con lineas anti-aliasadas y desvanecimiento por distancia a la camara.
// Las propiedades de color se declaran como Color (AssetRipper las exporta todas como Vector).
Shader "Unlit/Grid" {
	Properties {
		_GridColour ("Grid Colour", Color) = (1,1,1,1)
		_BaseColour ("Base Colour", Color) = (1,1,1,0)
		_GridSpacing ("Grid Spacing", Float) = 1
		_LineThickness ("Line Thickness", Float) = 0.1
		_ODistance ("Start Transparency Distance", Float) = 5
		_TDistance ("Full Transparency Distance", Float) = 10
	}
	SubShader {
		Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
		LOD 100

		Pass {
			Name "Unlit"

			Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
			ZWrite Off
			ZTest LEqual
			Cull Back

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half4 _GridColour;
				half4 _BaseColour;
				float _GridSpacing;
				float _LineThickness;
				float _ODistance;
				float _TDistance;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 gridUV : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
				output.positionWS = positionWS;
				output.positionCS = TransformWorldToHClip(positionWS);
				output.gridUV = positionWS.xz / max(_GridSpacing, 1e-4);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				// Distancia (en celdas) a la linea mas cercana, convertida a pixeles con las derivadas de pantalla.
				float2 wrapped = frac(input.gridUV) - 0.5;
				float2 range = abs(wrapped);
				float2 speeds = max(fwidth(input.gridUV), 1e-6);
				float2 pixelRange = range / speeds;
				float lineWeight = saturate(min(pixelRange.x, pixelRange.y) - _LineThickness);

				half4 color = lerp(_GridColour, _BaseColour, lineWeight);

				// Desvanecimiento entre _ODistance (opaco) y _TDistance (transparente).
				float viewDist = length(GetCameraPositionWS() - input.positionWS);
				float falloff = saturate((viewDist - _ODistance) / max(_TDistance - _ODistance, 1e-4));
				color.a *= (1.0 - falloff);
				return color;
			}
			ENDHLSL
		}
	}
	Fallback Off
}
