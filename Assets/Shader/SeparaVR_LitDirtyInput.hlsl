// Input de SeparaVR/Lit Dirty: LitInput.hlsl de URP 17.3 recortado (solo albedo, metallic/smoothness, alpha clip)
// + suciedad global procedural (ruido 3D en posición de mundo, sin textura) + brillo limpio por material.
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
half _CleanBrightness; // multiplica el albedo limpio (también lo hornea Meta)
UNITY_TEXTURE_STREAMING_DEBUG_VARS;
CBUFFER_END

// suciedad (globales, Shader.SetGlobalFloat): fuera del CBUFFER y NO declarados en Properties
float _Cleanliness;   // 0 = sucio, 1 = limpio

#ifdef UNITY_DOTS_INSTANCING_ENABLED

UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float4, _BaseColor)
    UNITY_DOTS_INSTANCED_PROP(float , _Cutoff)
    UNITY_DOTS_INSTANCED_PROP(float , _Smoothness)
    UNITY_DOTS_INSTANCED_PROP(float , _Metallic)
    UNITY_DOTS_INSTANCED_PROP(float , _Surface)
    UNITY_DOTS_INSTANCED_PROP(float , _DirtAmount)
    UNITY_DOTS_INSTANCED_PROP(float , _CleanBrightness)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)

// caches estáticos como en LitInput (evita recargar la propiedad en cada uso)
static float4 unity_DOTS_Sampled_BaseColor;
static float  unity_DOTS_Sampled_Cutoff;
static float  unity_DOTS_Sampled_Smoothness;
static float  unity_DOTS_Sampled_Metallic;
static float  unity_DOTS_Sampled_Surface;
static float  unity_DOTS_Sampled_DirtAmount;
static float  unity_DOTS_Sampled_CleanBrightness;

void SetupDOTSLitDirtyMaterialPropertyCaches()
{
    unity_DOTS_Sampled_BaseColor  = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, _BaseColor);
    unity_DOTS_Sampled_Cutoff     = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Cutoff);
    unity_DOTS_Sampled_Smoothness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Smoothness);
    unity_DOTS_Sampled_Metallic   = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Metallic);
    unity_DOTS_Sampled_Surface    = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _Surface);
    unity_DOTS_Sampled_DirtAmount = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _DirtAmount);
    unity_DOTS_Sampled_CleanBrightness = UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float , _CleanBrightness);
}

#undef UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES
#define UNITY_SETUP_DOTS_MATERIAL_PROPERTY_CACHES() SetupDOTSLitDirtyMaterialPropertyCaches()

#define _BaseColor   unity_DOTS_Sampled_BaseColor
#define _Cutoff      unity_DOTS_Sampled_Cutoff
#define _Smoothness  unity_DOTS_Sampled_Smoothness
#define _Metallic    unity_DOTS_Sampled_Metallic
#define _Surface     unity_DOTS_Sampled_Surface
#define _DirtAmount  unity_DOTS_Sampled_DirtAmount
#define _CleanBrightness unity_DOTS_Sampled_CleanBrightness

#endif

TEXTURE2D(_MetallicGlossMap);   SAMPLER(sampler_MetallicGlossMap);

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
// _CleanBrightness va aquí (no en la suciedad) para que Meta hornee el mismo albedo limpio.
inline void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData outSurfaceData)
{
    half4 albedoAlpha = SampleAlbedoAlpha(uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
    outSurfaceData.alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);

    half4 specGloss = SampleMetallicSpecGloss(uv, albedoAlpha.a);
    outSurfaceData.albedo = albedoAlpha.rgb * _BaseColor.rgb * half(_CleanBrightness);
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
// Suciedad procedural (sin textura): ruido de valor 3D evaluado en la posición de mundo.
// Equivale a un triplanar (igual en suelos, paredes y techos, sin estirar ni costuras) pero con
// una sola evaluación en vez de tres proyecciones. No depende de la altura, de la cercanía al
// suelo ni de la dirección de la normal: mismo tamaño y regularidad en veredas, edificios y props.
// Las manchas crecen con la cobertura (umbral sobre el ruido): al limpiar se achican, no se desvanecen parejo.
// Parámetros globales como #define: idénticos para todos los materiales por construcción,
// sin costo de CBUFFER y el compilador los pliega como constantes. Por material solo _DirtAmount.
// ---------------------------------------------------------------------------
#define SEPARAVR_DIRT_SCALE        1.5   // frecuencia de las manchas (celdas/m) -> manchas de ~0.6-1 m
#define SEPARAVR_DIRT_SCALE2       3.4   // segunda octava (bordes irregulares), rotada
#define SEPARAVR_DIRT_OCT2         0.35  // peso de la segunda octava
#define SEPARAVR_DIRT_GRAIN_SCALE  12.0  // grano fino (celdas/m) -> ~8 cm
#define SEPARAVR_DIRT_GRAIN_AMP    0.25  // variación de color del grano dentro de la mancha (+-)
#define SEPARAVR_DIRT_GRAIN_EDGE   0.10  // cuánto el grano desarma el borde de la mancha
#define SEPARAVR_DIRT_T_CLEAN      0.80  // umbral con cobertura ~0 (casi sin manchas)
#define SEPARAVR_DIRT_T_DIRTY      0.45  // umbral con cobertura 1 (~mitad cubierto, quedan claros)
#define SEPARAVR_DIRT_EDGE_IN      0.05  // borde exterior de la mancha (en unidades de ruido)
#define SEPARAVR_DIRT_EDGE_OUT     0.22  // hacia adentro se sigue oscureciendo (borde más suave)
#define SEPARAVR_DIRT_TINT         half3(0.70, 0.66, 0.58) // gris-marrón suave (multiplica el albedo)
#define SEPARAVR_DIRT_SMOOTH       0.4   // la suciedad quita hasta 40 % de smoothness
#define SEPARAVR_DIRT_FAR_START    0.08  // manchas/píxel donde empieza a promediarse la máscara (lejos)
#define SEPARAVR_DIRT_FAR_END      0.25  // manchas/píxel donde ya es el promedio (evita "camuflaje" a distancia)

// hash sin seno (Dave Hoskins, hash13), estable en float de móvil
float SeparaVR_Hash13(float3 p3)
{
    p3 = frac(p3 * 0.1031);
    p3 += dot(p3, p3.zyx + 31.32);
    return frac((p3.x + p3.y) * p3.z);
}

// ruido de valor 3D (8 hashes, interpolación suave). Resultado 0..1
half SeparaVR_ValueNoise3(float3 p)
{
    float3 i = floor(p);
    half3 f = half3(p - i);
    half3 u = f * f * (half(3.0) - half(2.0) * f);
    half a = half(SeparaVR_Hash13(i));
    half b = half(SeparaVR_Hash13(i + float3(1.0, 0.0, 0.0)));
    half c = half(SeparaVR_Hash13(i + float3(0.0, 1.0, 0.0)));
    half d = half(SeparaVR_Hash13(i + float3(1.0, 1.0, 0.0)));
    half e = half(SeparaVR_Hash13(i + float3(0.0, 0.0, 1.0)));
    half g = half(SeparaVR_Hash13(i + float3(1.0, 0.0, 1.0)));
    half h = half(SeparaVR_Hash13(i + float3(0.0, 1.0, 1.0)));
    half k = half(SeparaVR_Hash13(i + float3(1.0, 1.0, 1.0)));
    half lo = lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    half hi = lerp(lerp(e, g, u.x), lerp(h, k, u.x), u.y);
    return lerp(lo, hi, u.z);
}

// rotación fija entre octavas: rompe la alineación a ejes del ruido de valor
static const float3x3 SeparaVR_DirtRot = float3x3(0.00, 0.80, 0.60, -0.80, 0.36, -0.48, -0.60, -0.48, 0.64);

// celdas de grano por píxel (derivadas de pantalla); /8 aprox. da manchas por píxel
half SeparaVR_DirtGrainCellsPerPixel(float3 positionWS)
{
    float3 pg = positionWS * SEPARAVR_DIRT_GRAIN_SCALE;
    return half(max(length(ddx(pg)), length(ddy(pg))));
}

// grano fino 0..1; a distancia tiende a 0.5 para que no titile (VR)
half SeparaVR_DirtGrain(float3 positionWS, half grainCellsPerPixel)
{
    half fade = saturate(half(2.0) - half(2.0) * grainCellsPerPixel);
    return lerp(half(0.5), SeparaVR_ValueNoise3(positionWS * SEPARAVR_DIRT_GRAIN_SCALE), fade);
}

// máscara de suciedad 0..1 (manchas) para este píxel; grain = SeparaVR_DirtGrain
half SeparaVR_Dirtiness(float3 positionWS, half grain, half grainCellsPerPixel)
{
    half coverage = half(saturate(1.0 - _Cleanliness)) * saturate(_DirtAmount);
    half n = SeparaVR_ValueNoise3(positionWS * SEPARAVR_DIRT_SCALE) * half(1.0 - SEPARAVR_DIRT_OCT2)
           + SeparaVR_ValueNoise3(mul(SeparaVR_DirtRot, positionWS) * SEPARAVR_DIRT_SCALE2 + 17.0) * half(SEPARAVR_DIRT_OCT2);
    n += (grain - half(0.5)) * half(SEPARAVR_DIRT_GRAIN_EDGE); // borde granulado, no liso
    half t = lerp(half(SEPARAVR_DIRT_T_CLEAN), half(SEPARAVR_DIRT_T_DIRTY), coverage);
    half mask = smoothstep(t - half(SEPARAVR_DIRT_EDGE_IN), t + half(SEPARAVR_DIRT_EDGE_OUT), n);
    // lejos las manchas ocupan pocos píxeles: se reemplazan por su promedio aproximado (como un mip)
    half meanMask = smoothstep(half(SEPARAVR_DIRT_T_CLEAN), half(SEPARAVR_DIRT_T_DIRTY - 0.02), t) * half(0.8);
    half far = smoothstep(half(SEPARAVR_DIRT_FAR_START), half(SEPARAVR_DIRT_FAR_END),
                          grainCellsPerPixel * half(SEPARAVR_DIRT_SCALE / SEPARAVR_DIRT_GRAIN_SCALE));
    mask = lerp(mask, meanMask, far);
    return mask * saturate(coverage * half(8.0)); // cobertura 0 -> exactamente limpio
}

// aplica suciedad sobre SurfaceData ya inicializado
void SeparaVR_ApplyDirt(inout SurfaceData surfaceData, float3 positionWS)
{
#if !defined(SEPARAVR_DIRT_OFF)
    half cpp = SeparaVR_DirtGrainCellsPerPixel(positionWS);
    half grain = SeparaVR_DirtGrain(positionWS, cpp);
    half mask = SeparaVR_Dirtiness(positionWS, grain, cpp);
    half3 dirt = SEPARAVR_DIRT_TINT * (half(1.0) + half(SEPARAVR_DIRT_GRAIN_AMP) * (grain * half(2.0) - half(1.0)));
    surfaceData.albedo *= lerp(half3(1.0, 1.0, 1.0), dirt, mask);
    surfaceData.smoothness *= half(1.0) - half(SEPARAVR_DIRT_SMOOTH) * mask;
#endif
}

#endif // SEPARAVR_LIT_DIRTY_INPUT_INCLUDED
