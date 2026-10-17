// Input de SeparaVR/Lit Dirty: LitInput.hlsl de URP 17.3 recortado (solo albedo, metallic/smoothness, alpha clip)
// + suciedad global estilo "BaldosaDetail" (detalle x2 de URP) proyectada en mundo.
// Sin normal map, emisión, detalle, parallax, oclusión, clear coat ni workflow specular.
#ifndef SEPARAVR_LIT_DIRTY_INPUT_INCLUDED
#define SEPARAVR_LIT_DIRTY_INPUT_INCLUDED
// bloquea un include accidental del LitInput.hlsl original (CBUFFER duplicado)
#define UNIVERSAL_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/DebugMipmapStreamingMacros.hlsl"

// Igual en todas las pases (SRP Batcher): no poner #if aquí.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _BaseMap_TexelSize;
half4 _BaseColor;
half _Cutoff;
half _Smoothness;
half _Metallic;
half _Surface;
// suciedad (por material)
half _DirtAmount;
float _DirtTiling;
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// suciedad (globales, Shader.SetGlobalFloat): fuera del CBUFFER y NO declarados en Properties
float _Cleanliness;   // 0 = sucio, 1 = limpio
float _DirtGroundY;   // altura del suelo en mundo

#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
    UNITY_DOTS_INSTANCED_PROP(float , _Smoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
    UNITY_DOTS_INSTANCED_PROP(float , _Surface)
    UNITY_DOTS_INSTANCED_PROP(float , _DirtAmount)
    UNITY_DOTS_INSTANCED_PROP(float , _DirtTiling)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

// caches estáticos como en LitInput (evita recargar la propiedad en cada uso)
static float4 unity_DOTS_Sampled_BaseColor;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_Surface;
static float  unity_DOTS_Sampled_DirtAmount;
static float  unity_DOTS_Sampled_DirtTiling;

void SetupDOTSLitDirtyMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_Cutoff     = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
    unity_DOTS_Sampled_Smoothness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
    unity_DOTS_Sampled_Metallic   = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Metallic);
    unity_DOTS_Sampled_Surface    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Surface);
    unity_DOTS_Sampled_DirtAmount = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DirtAmount);
    unity_DOTS_Sampled_DirtTiling = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DirtTiling);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSLitDirtyMaterialPropertyCaches()

#define _BaseColor   unity_DOTS_Sampled_BaseColor
#define _Cutoff      unity_DOTS_Sampled_Cutoff
#define _Smoothness  unity_DOTS_Sampled_Smoothness
#define _Metallic    unity_DOTS_Sampled_Metallic
#define _Surface     unity_DOTS_Sampled_Surface
#define _DirtAmount  unity_DOTS_Sampled_DirtAmount
#define _DirtTiling  unity_DOTS_Sampled_DirtTiling

#endif

TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);
TEXTURE2D(_DirtMap);            SAMPLER(sampler_DirtMap);

half4 SampleMetallicSpecGloss(float2 uv, half albedoAlpha)
{
    half4 specGloss;

#ifdef _METALLICSPECGLOSSMAP
    specGloss = half4(SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MetallicGlossMap, uv));
    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a *= _Smoothness;
    #endif
#else
    specGloss.rgb = _Metallic.rrr;
    #ifdef _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
        specGloss.a = albedoAlpha * _Smoothness;
    #else
        specGloss.a = _Smoothness;
    #endif
#endif

    return specGloss;
}

// Sin suciedad: la usan Meta y el resto de pases de URP. Emisión siempre negra.
inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half4 specGloss = SampleMetallicSpecGloss(uv, albedoAlpha.a);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb;
    outSurfaceData.albedo = AlphaModulate(outSurfaceData.albedo, outSurfaceData.alpha);

    outSurfaceData.metallic = specGloss.r;
    outSurfaceData.specular = half3(0.0, 0.0, 0.0);
    outSurfaceData.smoothness = specGloss.a;
    outSurfaceData.normalTS = half3(0.0, 0.0, 1.0); // sin normal map: la luz usa la normal de vértice
    outSurfaceData.occlusion = half(1.0);
    outSurfaceData.emission = half3(0.0, 0.0, 0.0);
    outSurfaceData.clearCoatMask = half(0.0);
    outSurfaceData.clearCoatSmoothness = half(0.0);
}

// ---------------------------------------------------------------------------
// Suciedad (como el viejo Vereda + BaldosaDetail: albedo *= LerpWhiteTo(detalle * 2, cantidad))
// ---------------------------------------------------------------------------
#define SEPARAVR_DIRT_NOISE_FREQ 0.05  // ruido de baja frecuencia (~20 m) que modula la cantidad
#define SEPARAVR_DIRT_NOISE_AMP  0.25  // +-25 %
#define SEPARAVR_DIRT_HEIGHT     1.5   // metros sobre _DirtGroundY con algo más de suciedad

// hash sin seno (Dave Hoskins), estable en float de móvil
float SeparaVR_Hash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float SeparaVR_ValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = p - i;
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = SeparaVR_Hash12(i);
    float b = SeparaVR_Hash12(i + float2(1.0, 0.0));
    float c = SeparaVR_Hash12(i + float2(0.0, 1.0));
    float d = SeparaVR_Hash12(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// una sola muestra: proyección por eje dominante de la normal (XZ suelos/techos, ZY o XY paredes).
// Gradientes de la posición completa para que no aparezcan costuras de mip al cambiar de eje.
half3 SeparaVR_SampleDirt(float3 positionWS, half3 normalWS)
{
    float3 p = positionWS * _DirtTiling;
    float3 dpx = ddx(p);
    float3 dpy = ddy(p);
    half3 an = abs(normalWS);
    bool top = an.y >= max(an.x, an.z);
    bool side = an.x >= an.z;
    float2 uv  = top ? p.xz   : (side ? p.zy   : p.xy);
    float2 gx  = top ? dpx.xz : (side ? dpx.zy : dpx.xy);
    float2 gy  = top ? dpy.xz : (side ? dpy.zy : dpy.xy);
    return half3(SAMPLE_TEXTURE2D_GRAD(_DirtMap, sampler_DirtMap, uv, gx, gy).rgb);
}

// cantidad de suciedad 0..1 para este píxel
half SeparaVR_Dirtiness(float3 positionWS, half3 normalWS)
{
    half d = half(saturate(1.0 - _Cleanliness)) * saturate(_DirtAmount);
    half n = half(SeparaVR_ValueNoise(positionWS.xz * SEPARAVR_DIRT_NOISE_FREQ));
    half variation = half(1.0) + half(SEPARAVR_DIRT_NOISE_AMP) * (n * half(2.0) - half(1.0));
    // sesgo suave: un poco más en superficies hacia arriba y cerca del suelo
    half up = saturate(normalWS.y);
    half ground = half(saturate(1.0 - (positionWS.y - _DirtGroundY) * (1.0 / SEPARAVR_DIRT_HEIGHT)));
    half bias = lerp(half(0.9), half(1.1), saturate(half(0.5) * up + half(0.5) * ground));
    return saturate(d * variation * bias);
}

// aplica suciedad sobre SurfaceData ya inicializado (normalWS geométrica)
void SeparaVR_ApplyDirt(inout SurfaceData surfaceData, float3 positionWS, half3 normalWS)
{
#if !defined(SEPARAVR_DIRT_OFF)
    half dirtiness = SeparaVR_Dirtiness(positionWS, normalWS);
    half3 dirt = SeparaVR_SampleDirt(positionWS, normalWS);
    // igual que ApplyDetailAlbedo de URP con _DETAIL_MULX2 (x2 en espacio lineal)
    surfaceData.albedo *= LerpWhiteTo(half(2.0) * dirt, dirtiness);
    surfaceData.smoothness *= half(1.0) - half(0.5) * dirtiness;
#endif
}

#endif // SEPARAVR_LIT_DIRTY_INPUT_INCLUDED
