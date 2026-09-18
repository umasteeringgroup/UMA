#ifndef UMA_REALISTIC_SKIN_INPUT
#define UMA_REALISTIC_SKIN_INPUT
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
// Identical layout in every pass: compatible with the SRP Batcher.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
half4 _Base_Color, _SubsurfaceColor;
float4 _Smoothness_Remap, _Ambient_Occlusion_Remap, _Detail_Tiling;
half _BumpScale, _Skinmask_Amount, _DetailNormalMapScale, _Detail_Normal_Scale;
half _Detail_Gloss_Scale, _SSSBlend, _ScatterRadiusMM, _DiffusionWrap;
half _DiffuseNormalStrength, _TransmissionStrength, _TransmissionPower, _SkinIOR;
half _OilLobeWeight, _OilSmoothness, _SpecularAA, _SkinDebug;
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

SkinData SampleSkin(float2 uv)
{
    SkinData s = (SkinData)0;
    half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
    half4 controls = SAMPLE_TEXTURE2D(_SkinControlMap, sampler_SkinControlMap, uv);
    half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
    // UMA's skin mask protects eyes/lips/nails from tint; it is NOT a thickness map.
    half3 protection = saturate(SAMPLE_TEXTURE2D(_Skinmask, sampler_Skinmask, uv).rgb * _Skinmask_Amount);
    albedo *= lerp(_Base_Color.rgb, half3(1,1,1), protection);
    half detailMask = saturate(mask.b * controls.a);
    half4 detail = SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uv * _Detail_Tiling.xy) * 2 - 1;
    // Matches the existing HDRP-style UMA packed detail contract (normal in AG).
    half2 detailXY = detail.ag * _Detail_Normal_Scale * detailMask;
    half3 detailNormal = half3(detailXY, sqrt(saturate(1 - dot(detailXY, detailXY))));
    half3 macroNormal = SampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    s.surface.normalTS = normalize(BlendNormalRNM(macroNormal, detailNormal));
    s.diffuseNormalTS = normalize(lerp(macroNormal, s.surface.normalTS, _DiffuseNormalStrength));
    s.surface.albedo = max(0, albedo * (1 + detail.r * _DetailNormalMapScale * detailMask));
    s.surface.smoothness = saturate(lerp(_Smoothness_Remap.x, _Smoothness_Remap.y, mask.a)
        + detail.b * _Detail_Gloss_Scale * detailMask);
    s.surface.occlusion = saturate(lerp(_Ambient_Occlusion_Remap.x, _Ambient_Occlusion_Remap.y, mask.g));
    half f0 = Sq((_SkinIOR - 1) / (_SkinIOR + 1));
    s.surface.specular = f0.xxx;
    s.surface.alpha = 1;
    s.scatter = saturate(mask.r * controls.r * _SSSBlend);
    s.transmission = saturate(mask.r * controls.b) * _TransmissionStrength;
    s.oil = saturate(_OilLobeWeight * controls.g);
    return s;
}

// Used by URP's lightmapping pass. No light-dependent color or emission is baked.
void InitializeStandardLitSurfaceData(float2 uv, out SurfaceData surfaceData)
{
    surfaceData = SampleSkin(uv).surface;
}
#endif
