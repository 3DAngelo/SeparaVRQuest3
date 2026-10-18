// Forward de SeparaVR/Lit Dirty: reutiliza Varyings/vertex/InitializeInputData de URP 17.3 LitForwardPass.hlsl
// y solo reemplaza el fragment (copia de LitPassFragment + suciedad). Mantener en sync con URP.
// Sin _NORMALMAP/_DETAIL, InitializeInputData usa input.normalWS (normal de vértice) directamente.
#ifndef SEPARAVR_LIT_DIRTY_FORWARD_PASS_INCLUDED
#define SEPARAVR_LIT_DIRTY_FORWARD_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

void LitDirtyPassFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    // (sin parallax: _PARALLAXMAP no existe en este shader)

    SurfaceData surfaceData;
    InitializeStandardLitSurfaceData(input.uv, surfaceData);
    // suciedad: positionWS siempre existe en forward (Shadows.hlsl define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR)
    SeparaVR_ApplyDirt(surfaceData, input.positionWS);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));

#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    InitializeBakedGIData(input, inputData);

    half4 color = UniversalFragmentPBR(inputData, surfaceData);
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
