#ifndef UMA_REALISTIC_SKIN_INPUT
#define UMA_REALISTIC_SKIN_INPUT
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
// Identical layout in every pass: compatible with the SRP Batcher.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _Base_Color, _SubsurfaceColor;
float4 _Smoothness_Remap, _Ambient_Occlusion_Remap, _Detail_Tiling;
half _BumpScale, _Skinmask_Amount, _DetailNormalMapScale, _Detail_Normal_Scale;
half _Detail_Gloss_Scale, _SSSBlend, _ScatterRadiusMM, _DiffusionWrap;
half _DiffuseNormalStrength, _TransmissionStrength, _TransmissionPower, _SkinIOR;
half _OilLobeWeight, _OilSmoothness, _SpecularAA, _SkinDebug;
half _Metallic;
CBUFFER_END
TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
TEXTURE2D(_Skinmask); SAMPLER(sampler_Skinmask);
TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_SkinControlMap); SAMPLER(sampler_SkinControlMap);

struct SkinData
{
    SurfaceData surface;
    half3 diffuseNormalTS;
    half scatter;
    half transmission;
    half oil;
};

// Detail map coordinates: the mesh's fourth UV channel when it has one, otherwise the base UV.
// UMASkinDetailUVUtility bakes that channel so pores keep one size on the surface across texture
// tiles laid out at different scales. Baked coordinates are never below 1. A mesh without the channel supplies
// (0,0), and an ordinary 0..1 layout left in the channel is below 1 too: both keep the base UV.
float2 SkinDetailUV(float2 baseUV, float2 meshDetailUV)
{
    return all(meshDetailUV >= 0.999) ? meshDetailUV : baseUV;
}

SkinData SampleSkin(float2 uv, float2 detailUV)
{
    SkinData s = (SkinData)0;
    half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
    half4 controls = SAMPLE_TEXTURE2D(_SkinControlMap, sampler_SkinControlMap, uv);
    half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
    // UMA's skin mask protects eyes/lips/nails from tint; it is NOT a thickness map.
    half3 protection = saturate(SAMPLE_TEXTURE2D(_Skinmask, sampler_Skinmask, uv).rgb * _Skinmask_Amount);
    albedo *= lerp(_Base_Color.rgb, half3(1,1,1), protection);
    half detailMask = saturate(mask.b * controls.a);
    half4 detail = SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, detailUV * _Detail_Tiling.xy) * 2 - 1;
    // The detail map's smallest mip level is its average. Coverage fades albedo and smoothness detail toward
    // that average, not toward zero: a map whose channel is not centered on 0.5 then keeps the skin's overall
    // brightness and gloss the same where the coverage mask is dark as where it is white.
    half4 detailAverage = SAMPLE_TEXTURE2D_LOD(_DetailNormalMap, sampler_DetailNormalMap, float2(0.5, 0.5), 16) * 2 - 1;
    // Matches the existing HDRP-style UMA packed detail contract (normal in AG).
    half2 detailXY = detail.ag * _Detail_Normal_Scale * detailMask;
    half3 detailNormal = half3(detailXY, sqrt(saturate(1 - dot(detailXY, detailXY))));
    half3 macroNormal = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    s.surface.normalTS = normalize(BlendNormalRNM(macroNormal, detailNormal));
    s.diffuseNormalTS = normalize(lerp(macroNormal, s.surface.normalTS, _DiffuseNormalStrength));
    s.surface.albedo = max(0, albedo * (1 + lerp(detailAverage.r, detail.r, detailMask) * _DetailNormalMapScale));
    s.surface.smoothness = saturate(lerp(_Smoothness_Remap.x, _Smoothness_Remap.y, mask.a)
        + lerp(detailAverage.b, detail.b, detailMask) * _Detail_Gloss_Scale);
    s.surface.occlusion = saturate(lerp(_Ambient_Occlusion_Remap.x, _Ambient_Occlusion_Remap.y, mask.g));
    half f0 = Sq((_SkinIOR - 1) / (_SkinIOR + 1));
    s.surface.specular = f0.xxx;
    s.surface.metallic = saturate(_Metallic);
    s.surface.alpha = 1;
    s.scatter = saturate(mask.r * controls.r * _SSSBlend);
    s.transmission = saturate(mask.r * controls.b) * _TransmissionStrength;
    s.oil = saturate(_OilLobeWeight * controls.g);
    return s;
}

// URP's specular workflow ignores SurfaceData.metallic. Blend explicitly so the
// skin IOR remains unchanged at zero and both reflection lobes become metallic.
void InitializeSkinBRDFData(SurfaceData surface, out BRDFData brdf)
{
    half metallic = saturate(_Metallic);
    half oneMinusReflectivity = (1 - ReflectivitySpecular(surface.specular)) * (1 - metallic);
    half3 specular = lerp(surface.specular, surface.albedo, metallic);
    InitializeBRDFDataDirect(surface.albedo, surface.albedo * oneMinusReflectivity,
        specular, 1 - oneMinusReflectivity, oneMinusReflectivity,
        surface.smoothness, surface.alpha, brdf);
}

// Used by URP's lightmapping pass. No light-dependent color or emission is baked.
void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surfaceData)
{
    surfaceData = SampleSkin(uv, uv).surface;
}
#endif
