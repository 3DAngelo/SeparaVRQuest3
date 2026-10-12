// Reconstruccion aproximada: el shader original no es recuperable del APK.
// Original: Shader Graph "InteractablePrimitive" (XR Interaction Toolkit Starter Assets). Esta version escrita a mano
// es un Lit PBR de URP (mapa base, normal, metallic/smoothness) con un resalte de borde tipo fresnel sumado como emision:
// emision = _EdgeHighlightColor.rgb * _EdgeHighlightColor.a * pow(1 - N.V, _EdgeHighlightFalloff).
// Las propiedades de color se declaran como Color (AssetRipper las exporta todas como Vector).
Shader "Shader Graphs/InteractablePrimitive" {
	Properties {
		[NoScaleOffset] _BaseMap ("BaseMap", 2D) = "white" {}
		_BaseColor ("BaseColor", Color) = (0.5,0.5,0.5,1)
		[NoScaleOffset] [Normal] _BumpMap ("NormalMap", 2D) = "bump" {}
		_BumpScale ("NormalStrength", Float) = 0
		_Metallic ("Metallic", Float) = 0
		_Smoothness ("Smoothness", Float) = 0
		_EdgeHighlightColor ("EdgeHighlightColor", Color) = (1,1,1,0)
		_EdgeHighlightFalloff ("EdgeHighlightFalloff", Float) = 1.5
		[HideInInspector] _WorkflowMode ("_WorkflowMode", Float) = 1
		[HideInInspector] _CastShadows ("_CastShadows", Float) = 1
		[HideInInspector] _ReceiveShadows ("_ReceiveShadows", Float) = 1
		[HideInInspector] _Surface ("_Surface", Float) = 0
		[HideInInspector] _Blend ("_Blend", Float) = 0
		[HideInInspector] _AlphaClip ("_AlphaClip", Float) = 0
		[HideInInspector] _BlendModePreserveSpecular ("_BlendModePreserveSpecular", Float) = 0
		[HideInInspector] _SrcBlend ("_SrcBlend", Float) = 1
		[HideInInspector] _DstBlend ("_DstBlend", Float) = 0
		[ToggleUI] [HideInInspector] _ZWrite ("_ZWrite", Float) = 1
		[HideInInspector] _ZWriteControl ("_ZWriteControl", Float) = 0
		[HideInInspector] _ZTest ("_ZTest", Float) = 4
		[HideInInspector] _Cull ("_Cull", Float) = 2
		[HideInInspector] _AlphaToMask ("_AlphaToMask", Float) = 0
		[HideInInspector] _QueueOffset ("_QueueOffset", Float) = 0
		[HideInInspector] _QueueControl ("_QueueControl", Float) = -1
		[HideInInspector] [NoScaleOffset] unity_Lightmaps ("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_ShadowMasks ("unity_ShadowMasks", 2DArray) = "" {}
	}
	SubShader {
		Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" "UniversalMaterialType" = "Lit" "IgnoreProjector" = "True" }
		LOD 300

		HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

		TEXTURE2D(_BaseMap);
		SAMPLER(sampler_BaseMap);
		TEXTURE2D(_BumpMap);
		SAMPLER(sampler_BumpMap);

		CBUFFER_START(UnityPerMaterial)
			half4 _BaseColor;
			half4 _EdgeHighlightColor;
			half _BumpScale;
			half _Metallic;
			half _Smoothness;
			half _EdgeHighlightFalloff;
			float _WorkflowMode;
			float _CastShadows;
			float _ReceiveShadows;
			float _Surface;
			float _Blend;
			float _AlphaClip;
			float _BlendModePreserveSpecular;
			float _SrcBlend;
			float _DstBlend;
			float _ZWrite;
			float _ZWriteControl;
			float _ZTest;
			float _Cull;
			float _AlphaToMask;
			float _QueueOffset;
			float _QueueControl;
		CBUFFER_END
		ENDHLSL

		Pass {
			Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }

			Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]
			ZTest [_ZTest]
			Cull [_Cull]

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag

			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
			#pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile_fragment _ _SHADOWS_SOFT
			#pragma multi_compile _ _CLUSTER_LIGHT_LOOP
			#pragma multi_compile_fog
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 tangentOS : TANGENT;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float3 positionWS : TEXCOORD1;
				half3 normalWS : TEXCOORD2;
				half4 tangentWS : TEXCOORD3;
				half4 fogFactorAndVertexLight : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
				VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

				output.positionCS = positionInputs.positionCS;
				output.positionWS = positionInputs.positionWS;
				output.uv = input.uv;
				output.normalWS = normalInputs.normalWS;
				output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());

				half3 vertexLight = VertexLighting(positionInputs.positionWS, normalInputs.normalWS);
				half fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
				output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
				half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);

				half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
				half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
				normalWS = NormalizeNormalPerPixel(normalWS);

				half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

				half fresnel = pow(saturate(1.0 - saturate(dot(normalWS, viewDirWS))), max(_EdgeHighlightFalloff, 1e-3));

				SurfaceData surfaceData = (SurfaceData)0;
				surfaceData.albedo = albedoAlpha.rgb;
				surfaceData.alpha = albedoAlpha.a;
				surfaceData.metallic = saturate(_Metallic);
				surfaceData.specular = half3(0.0, 0.0, 0.0);
				surfaceData.smoothness = saturate(_Smoothness);
				surfaceData.occlusion = 1.0;
				surfaceData.normalTS = normalTS;
				surfaceData.emission = _EdgeHighlightColor.rgb * _EdgeHighlightColor.a * fresnel;

				InputData inputData = (InputData)0;
				inputData.positionWS = input.positionWS;
				inputData.positionCS = input.positionCS;
				inputData.normalWS = normalWS;
				inputData.viewDirectionWS = viewDirWS;
				inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
				inputData.fogCoord = input.fogFactorAndVertexLight.x;
				inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
				inputData.bakedGI = SampleSH(normalWS);
				inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
				inputData.shadowMask = half4(1.0, 1.0, 1.0, 1.0);

				half4 color = UniversalFragmentPBR(inputData, surfaceData);
				color.rgb = MixFog(color.rgb, inputData.fogCoord);
				return color;
			}
			ENDHLSL
		}

		Pass {
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On
			ZTest LEqual
			ColorMask 0
			Cull [_Cull]

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

			float3 _LightDirection;
			float3 _LightPosition;

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
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
				float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

				#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
					float3 lightDirectionWS = normalize(_LightPosition - positionWS);
				#else
					float3 lightDirectionWS = _LightDirection;
				#endif

				float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
				#if UNITY_REVERSED_Z
					positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#else
					positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
				#endif
				output.positionCS = positionCS;
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				return 0;
			}
			ENDHLSL
		}

		Pass {
			Name "DepthOnly"
			Tags { "LightMode" = "DepthOnly" }

			ZWrite On
			ColorMask R
			Cull [_Cull]

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
				return input.positionCS.z;
			}
			ENDHLSL
		}

		Pass {
			Name "DepthNormals"
			Tags { "LightMode" = "DepthNormals" }

			ZWrite On
			Cull [_Cull]

			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma multi_compile_instancing

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				float4 tangentOS : TANGENT;
				float2 uv : TEXCOORD0;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				half4 tangentWS : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = input.uv;
				output.normalWS = normalInputs.normalWS;
				output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
				return output;
			}

			half4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
				half3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
				half3 normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS));
				return half4(NormalizeNormalPerPixel(normalWS), 0.0);
			}
			ENDHLSL
		}
	}
	Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
