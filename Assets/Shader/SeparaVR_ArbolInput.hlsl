// Input de SeparaVR/Arbol (mismo patrón que SimpleLitInput.hlsl de URP 17.3, recortado).
// Lo usan todos los pases (Forward, ShadowCaster, DepthOnly, DepthNormals, Meta): CBUFFER idéntico (SRP Batcher).
// Provee lo que piden los pases de URP: _BaseMap/sampler_BaseMap (SurfaceInput.hlsl), _BaseMap_ST, _BaseColor, _Cutoff,
// Alpha(), SampleAlbedoAlpha().
#ifndef SEPARAVR_ARBOL_INPUT_INCLUDED
#define SEPARAVR_ARBOL_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

// Igual en todos los pases (SRP Batcher): no poner #if aquí.
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseMap_TexelSize;
    half4 _BaseColor;
    half _Cutoff;
    UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

static float4 unity_DOTS_Sampled_BaseColor;
static float  unity_DOTS_Sampled_Cutoff;

void SetupDOTSArbolMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_Cutoff    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSArbolMaterialPropertyCaches()

#define _BaseColor  unity_DOTS_Sampled_BaseColor
#define _Cutoff     unity_DOTS_Sampled_Cutoff
#endif

#endif
