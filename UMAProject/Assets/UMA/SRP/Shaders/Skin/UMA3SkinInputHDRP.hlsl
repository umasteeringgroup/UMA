// HDRP-compatible UMA realistic skin inputs.
// Same property names / texture contract as the URP variant so existing
// materials, UMA overlays and atlas channels keep working. Only the
// pipeline includes and platform helpers are HDRP.
#ifndef UMA_REALISTIC_SKIN_INPUT
#define UMA_REALISTIC_SKIN_INPUT

#include "../UMACommonHDRP.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

// Identical layout in every pass: compatible with the SRP Batcher.
CBUFFER_START(UnityPerMaterial)
float4 _BaseMap_ST;
float4 _Base_Color, _SubsurfaceColor;
float4 _Smoothness_Remap, _Ambient_Occlusion_Remap, _Detail_Tiling;
float _BumpScale, _Skinmask_Amount, _DetailNormalMapScale, _Detail_Normal_Scale;
float _Detail_Gloss_Scale, _SSSBlend, _ScatterRadiusMM, _DiffusionWrap;
float _DiffuseNormalStrength, _TransmissionStrength, _TransmissionPower, _SkinIOR;
float _OilLobeWeight, _OilSmoothness, _SpecularAA, _SkinDebug;
CBUFFER_END

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);
TEXTURE2D(_Skinmask); SAMPLER(sampler_Skinmask);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_DetailNormalMap); SAMPLER(sampler_DetailNormalMap);
TEXTURE2D(_SkinControlMap); SAMPLER(sampler_SkinControlMap);

#ifndef UMA_SKIN_UINT_PER_MATERIAL_DECLARED
#define UMA_SKIN_UINT_PER_MATERIAL_DECLARED
#endif

struct UmaSkinSurface
{
    float3 albedo;
    float3 normalTS;
    float smoothness;
    float occlusion;
    float3 specular;
    float alpha;
};

struct SkinData
{
    UmaSkinSurface surface;
    float3 diffuseNormalTS;
    float scatter;
    float transmission;
    float oil;
};

struct SkinAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    float2 dynamicUV : TEXCOORD2;
    float3 previousPositionOS : TEXCOORD4;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SkinVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float4 tangentWS : TEXCOORD3;
    float2 lightmapUV : TEXCOORD4;
    float2 dynamicUV : TEXCOORD5;
    float4 currentCS : TEXCOORD6;
    float4 previousCS : TEXCOORD7;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

#define UMA_SKIN_STRUCTS_DECLARED 1

float3 UmaSkinSampleNormal(float2 uv, TEXTURE2D_PARAM(_BumpMap, sampler_BumpMap), float scale)
{
    float4 packedNormal = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv);
    return UnpackNormalScale(packedNormal, scale);
}

float3 UmaSkinBlendNormalRNM(float3 n1, float3 n2)
{
    // Reoriented normal mapping: keeps pore detail stable over the base normal.
    float3 t = n1 + float3(0.0, 0.0, 1.0);
    float3 u = n2 * float3(-1.0, -1.0, 1.0);
    return normalize(t * dot(t, u) / max(t.z, 1e-4) - u);
}

SkinData SampleSkin(float2 uv)
{
    SkinData s = (SkinData)0;
    float4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
    float4 controls = SAMPLE_TEXTURE2D(_SkinControlMap, sampler_SkinControlMap, uv);
    float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
    // UMA's skin mask protects eyes/lips/nails from tint; it is NOT a thickness map.
    float3 protection = saturate(SAMPLE_TEXTURE2D(_Skinmask, sampler_Skinmask, uv).rgb * _Skinmask_Amount);
    albedo *= lerp(_Base_Color.rgb, float3(1,1,1), protection);
    float detailMask = saturate(mask.b * controls.a);
    float4 detail = SAMPLE_TEXTURE2D(_DetailNormalMap, sampler_DetailNormalMap, uv * _Detail_Tiling.xy) * 2 - 1;
    // Matches the existing HDRP-style UMA packed detail contract (normal in AG).
    float2 detailXY = detail.ag * _Detail_Normal_Scale * detailMask;
    float3 detailNormal = float3(detailXY, sqrt(saturate(1 - dot(detailXY, detailXY))));
    float3 macroNormal = UmaSkinSampleNormal(uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    s.surface.normalTS = normalize(UmaSkinBlendNormalRNM(macroNormal, detailNormal));
    s.diffuseNormalTS = normalize(lerp(macroNormal, s.surface.normalTS, _DiffuseNormalStrength));
    s.surface.albedo = max(0, albedo * (1 + detail.r * _DetailNormalMapScale * detailMask));
    s.surface.smoothness = saturate(lerp(_Smoothness_Remap.x, _Smoothness_Remap.y, mask.a)
        + detail.b * _Detail_Gloss_Scale * detailMask);
    s.surface.occlusion = saturate(lerp(_Ambient_Occlusion_Remap.x, _Ambient_Occlusion_Remap.y, mask.g));
    float f0Base = (_SkinIOR - 1) / (_SkinIOR + 1);
    float f0 = f0Base * f0Base;
    s.surface.specular = f0.xxx;
    s.surface.alpha = 1;
    s.scatter = saturate(mask.r * controls.r * _SSSBlend);
    s.transmission = saturate(mask.r * controls.b) * _TransmissionStrength;
    s.oil = saturate(_OilLobeWeight * controls.g);
    return s;
}
#endif
