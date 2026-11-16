// HDRP-compatible UMA swept/soft hair.
// Same material property names and strand response as the URP variant,
// lit through HDRP directional + punctual light data and baked GI.
// No Universal RP ShaderLibrary dependencies.
#ifndef UMA_SWEPT_HAIR_HDRP_INCLUDED
#define UMA_SWEPT_HAIR_HDRP_INCLUDED
#define _ALPHATEST_ON 1

#include "../UMACommonHDRP.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"
// Depth/shadow passes also parse this include, but do not select lighting variants.
#if !defined(PUNCTUAL_SHADOW_LOW) && !defined(PUNCTUAL_SHADOW_MEDIUM) && !defined(PUNCTUAL_SHADOW_HIGH)
#define PUNCTUAL_SHADOW_MEDIUM
#endif
#if !defined(DIRECTIONAL_SHADOW_LOW) && !defined(DIRECTIONAL_SHADOW_MEDIUM) && !defined(DIRECTIONAL_SHADOW_HIGH)
#define DIRECTIONAL_SHADOW_MEDIUM
#endif
// Area lighting is not evaluated by this custom BRDF; HDRP's shared shadow
// include nevertheless requires an area filter definition.
#define AREA_SHADOW_MEDIUM
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/PunctualLightCommon.hlsl"
#if defined(UMA_HAIR_ALPHA_BLEND)
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/AtmosphericScattering/AtmosphericScattering.hlsl"
#endif

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DepthMap); SAMPLER(sampler_DepthMap);
CBUFFER_START(UnityPerMaterial)
    float _DitheredOpacity, _Coverage, _AlphaDensity;
    float4 _BaseMap_ST;
    float4 _BaseColor, _RootColor, _TipColor, _SpecularColor, _SecondaryColor, _TransmissionColor;
    float _RootFade, _ColorPower, _TextureColor, _DepthInfluence, _StrandVariation, _ClumpVariation;
    float _SpecularStrength, _Smoothness, _SpecularShift, _SecondaryStrength, _SecondarySmoothness, _SecondaryShift;
    float _Transmission, _DiffuseWrap, _AmbientStrength, _BumpScale, _Cutoff, _ShadowCutoff;
    float _AlphaToCoverage, _VertexColorInfluence, _VertexAlphaInfluence, _DebugView;
    float _OcclusionStrength, _UseDepthMap, _RootOpacityFade, _HybridCore;
CBUFFER_END

struct HairAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 strand : TEXCOORD1; // normalized root->tip, stable strand random
    float3 previousPositionOS : TEXCOORD4;
    float2 clump : TEXCOORD2;  // stable parent-clump random, inspection mask
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct HairVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 strandWS : TEXCOORD2;
    float2 uv : TEXCOORD3;
    float4 data : TEXCOORD4;
    float4 currentCS : TEXCOORD5;
    float4 previousCS : TEXCOORD6;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
HairVaryings HairVertex(HairAttributes input)
{
    HairVaryings output = (HairVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    output.positionCS = TransformWorldToHClip(positionWS);
    output.positionWS = positionWS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.strandWS = TransformObjectToWorldDir(input.tangentOS.xyz);
    output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    output.data = float4(input.strand, input.clump);
    output.color = input.color;
    return output;
}

HairVaryings HairMotionVertex(HairAttributes input)
{
    HairVaryings o = HairVertex(input);
    UNITY_SETUP_INSTANCE_ID(input);
    UmaMotionPositions(input.positionOS.xyz, input.previousPositionOS, o.currentCS, o.previousCS);
    return o;
}

float HairRootTipBlend(HairVaryings input)
{
    return pow(saturate(input.data.x / max(_RootFade, 0.001)), max(_ColorPower, 0.001));
}
float HairStrandOpacity(HairVaryings input)
{
    // UV1 stores unflipped root-to-tip length, so rotating/flipping atlas UVs cannot
    // move this fade to the tip. Zero preserves existing materials exactly.
    float rootOpacity = _RootOpacityFade > 0 ? smoothstep(0.0, _RootOpacityFade, input.data.x) : 1.0;
    float tintOpacity = lerp(saturate(_RootColor.a), saturate(_TipColor.a), HairRootTipBlend(input));
    return rootOpacity * tintOpacity * saturate(_BaseColor.a) *
        saturate(lerp(1.0, input.color.a, _VertexAlphaInfluence));
}
float HairAlpha(HairVaryings input, float atlasAlpha)
{
    // Density boosts the atlas only, never the artist's root/tip opacity.
    return HairStrandOpacity(input) * saturate(atlasAlpha * _AlphaDensity);
}
void HairSoftCoverage(float alpha, HairVaryings input)
{
    // Strand-specific, stationary screen-door opacity: different overlapping cards
    // do not share a single coverage mask. The same function is used by all passes.
    if (_DitheredOpacity > 0.5)
    {
        float2 offset = input.data.y * float2(137.23, 91.73);
        clip(alpha * _Coverage - InterleavedGradientNoise(input.positionCS.xy + offset, 0) - 0.00001);
    }
}
float HairClip(float alpha, HairVaryings input)
{
    #if defined(SHADER_STAGE_FRAGMENT)
    #if defined(UMA_HAIR_ALPHA_BLEND)
        alpha *= _Coverage;
        clip(alpha - 0.0001);
        return alpha;
    #else
    // A paired transparent pass owns fading roots/tips. Writing opaque color/depth
    // here would replace a soft fade with a hard cutoff (especially at low cutoff).
    // Keep standalone cutout behavior unchanged; this is enabled only for Hybrid.
    if (_HybridCore > 0.5) clip(HairStrandOpacity(input) * saturate(_Coverage) - 0.999);
    if (_DitheredOpacity > 0.5)
    {
        // One threshold, not hard clipping followed by another raw-alpha rejection.
        float coverage = SharpenAlpha(alpha, max(_Cutoff, 0.0001));
        HairSoftCoverage(coverage, input); return 1;
    }
    clip(alpha - max(_Cutoff, 0.0001));
    // Surviving cutouts are opaque. Never feed raw texture alpha into an MSAA mask
    // when rendering outside the pipeline's alpha-to-coverage-aware path.
    return 1;
    #endif
    #endif
    return alpha;
}
float3 HairNormal(HairVaryings input, float faceSign)
{
    float3 n = normalize(input.normalWS) * faceSign;
    float3 t = SafeNormalize(input.strandWS - n * dot(n, input.strandWS));
    float3 side = SafeNormalize(cross(n, t));
    float3 map = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
    // Build the normal-map basis from actual atlas UVs. This handles flipped strip regions
    // without reversing the longitudinal tangent used by the anisotropic highlights.
    float3 dx = ddx(input.positionWS), dy = ddy(input.positionWS);
    float2 ux = ddx(input.uv), uy = ddy(input.uv);
    float determinant = ux.x * uy.y - ux.y * uy.x;
    if (abs(determinant) > 1e-12)
    {
        float signUV = determinant < 0 ? -1 : 1;
        side = SafeNormalize((dx * uy.y - dy * ux.y) * signUV);
        t = SafeNormalize((dy * ux.x - dx * uy.x) * signUV);
    }
    return normalize(side * map.x + t * map.y + n * map.z);
}
float HairLobe(float3 tangent, float3 normal, float3 halfVector, float shift, float smoothness)
{
    float3 shifted = SafeNormalize(tangent + normal * shift);
    float dotTH = dot(shifted, halfVector);
    float sinTH = sqrt(saturate(1.0 - dotTH * dotTH));
    float exponent = exp2(lerp(3.0, 10.0, smoothness));
    return pow(max(sinTH, 0.0001), exponent) * smoothstep(-1.0, 0.0, dotTH);
}

float UmaHairDirectionalShadow(float3 positionWS, float2 positionSS, float3 normalWS, DirectionalLightData light)
{
    if (light.shadowIndex < 0 || light.shadowDimmer <= 0 || light.lightDimmer <= 0)
        return 1.0;
    float3 L = -light.forward;
    HDShadowContext shadowContext = InitShadowContext();
    float shadow = GetDirectionalShadowAttenuation(shadowContext, positionSS, positionWS, normalWS, light.shadowIndex, L);
    return lerp(1.0, shadow, saturate(light.shadowDimmer));
}

float UmaHairPunctualShadow(float3 positionWS, float2 positionSS, float3 normalWS, LightData light, float3 L, float distance)
{
    if (light.shadowIndex < 0 || light.shadowDimmer <= 0 || light.lightDimmer <= 0)
        return 1.0;
    bool isPointLight = light.lightType == GPULIGHTTYPE_POINT;
    bool perspective = light.lightType != GPULIGHTTYPE_PROJECTOR_BOX;
    float shadow = GetPunctualShadowAttenuation(InitShadowContext(), positionSS, positionWS, normalWS,
        light.shadowIndex, L, distance, isPointLight, perspective);
    return lerp(1.0, shadow, saturate(light.shadowDimmer));
}

float3 HairLightHDRP(float3 lightDir, float3 lightColor, float diffuseDimmer, float specularDimmer, float attenuation, float shadow,
    float3 n, float3 t, float3 v, float3 albedo, float depth)
{
    float3 h = SafeNormalize(lightDir + v);
    float wrap = saturate((dot(n, lightDir) + _DiffuseWrap) / (1.0 + _DiffuseWrap));
    float fiberDiffuse = sqrt(saturate(1.0 - dot(t, lightDir) * dot(t, lightDir)));
    float diffuse = lerp(wrap, fiberDiffuse * (0.35 + 0.65 * wrap), 0.6);
    float shiftNoise = (depth - 0.5) * 0.08;
    float primary = HairLobe(t,n,h,_SpecularShift + shiftNoise,_Smoothness);
    float secondary = HairLobe(t,n,h,_SecondaryShift + shiftNoise,_SecondarySmoothness);
    float3 specular = (_SpecularColor.rgb * primary * _SpecularStrength +
                     _SecondaryColor.rgb * secondary * _SecondaryStrength) * (0.25 + 0.75 * wrap);
    float scatter = pow(saturate(dot(-lightDir,v)), 5.0) * _Transmission * saturate(1.0 - wrap * 0.6);
    return ((albedo * diffuse + albedo * _TransmissionColor.rgb * scatter) * diffuseDimmer + specular * specularDimmer) * lightColor * attenuation * shadow;
}
float3 HairIdColor(float value)
{
    return 0.25 + 0.75 * saturate(abs(frac(value + float3(0,0.333333,0.666667)) * 6.0 - 3.0) - 1.0);
}
float4 HairFragment(HairVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float faceSign = IS_FRONT_VFACE(facing, 1.0, -1.0);
    float4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    float alpha = HairClip(HairAlpha(input, atlas.a), input);
    float3 n = HairNormal(input, faceSign);
    float3 t = SafeNormalize(input.strandWS - n * dot(n,input.strandWS));
    float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float along = HairRootTipBlend(input);
    float depth = atlas.r;
    if (_UseDepthMap > 0.5) depth = SAMPLE_TEXTURE2D(_DepthMap, sampler_DepthMap, input.uv).r;
    float3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, along) * _BaseColor.rgb;
    albedo *= lerp(1.0.xxx, atlas.rgb, _TextureColor);
    albedo *= lerp(1.0, lerp(0.08,1.0,depth), _DepthInfluence);
    albedo *= lerp(1.0, SAMPLE_TEXTURE2D(_OcclusionMap,sampler_OcclusionMap,input.uv).r, _OcclusionStrength);
    albedo *= max(0.05, 1.0 + (input.data.y * 2.0 - 1.0) * _StrandVariation + (input.data.z * 2.0 - 1.0) * _ClumpVariation);
    albedo *= lerp(1.0.xxx,input.color.rgb,_VertexColorInfluence);
    uint2 positionSS = uint2(input.positionCS.xy);
    float2 screenUV = input.positionCS.xy * _ScreenSize.zw;
    float exposure = GetCurrentExposureMultiplier();
    uint layers = GetMeshRenderingLayerMask();
    float3 baked = max(EvaluateAmbientProbe(n), 0);
    float3 color = baked * albedo * _AmbientStrength * exposure;
    [loop] for (uint i = 0; i < _DirectionalLightCount; i++)
    {
        DirectionalLightData light = _DirectionalLightDatas[i];
        if ((light.lightLayers & layers) == 0 || light.lightDimmer <= 0)
            continue;
        float3 L = -light.forward;
        float shadow = UmaHairDirectionalShadow(input.positionWS, screenUV * _ScreenSize.xy, n, light);
        color += HairLightHDRP(L, light.color * exposure, light.diffuseDimmer, light.specularDimmer, 1.0, shadow, n, t, v, albedo, depth);
    }
    [loop] for (uint j = 0; j < _PunctualLightCount; j++)
    {
        LightData light = _LightDatas[j];
        if ((light.lightLayers & layers) == 0 || light.lightDimmer <= 0)
            continue;
        float3 L; float4 distances;
        GetPunctualLightVectors(input.positionWS, light, L, distances);
        float attenuation = PunctualLightAttenuation(distances, light.rangeAttenuationScale,
            light.rangeAttenuationBias, light.angleScale, light.angleOffset);
        if (attenuation <= 0)
            continue;
        float shadow = UmaHairPunctualShadow(input.positionWS, screenUV * _ScreenSize.xy, n, light, L, distances.x);
        color += HairLightHDRP(L, light.color * exposure, light.diffuseDimmer, light.specularDimmer, attenuation, shadow, n, t, v, albedo, depth);
    }
    if (_DebugView > 0.5)
    {
        if (_DebugView < 1.5) color = lerp(float3(0.1,0.2,1),float3(1,0.2,0.1),input.data.x);
        else if (_DebugView < 2.5) color = HairIdColor(input.data.z);
        else if (_DebugView < 3.5) color = HairIdColor(input.data.y);
        else if (_DebugView < 4.5) color = input.data.www;
        else color = n * 0.5 + 0.5;
    }
#if defined(UMA_HAIR_ALPHA_BLEND)
    PositionInputs posInput = GetPositionInput(input.positionCS.xy, _ScreenSize.zw,
        input.positionCS.z, input.positionCS.w, input.positionWS);
    float3 fogColor, fogOpacity;
    EvaluateAtmosphericScattering(posInput, v, fogColor, fogOpacity);
    color = color * (1 - fogOpacity) + fogColor;
#endif
    return float4(color, alpha);
}

HairVaryings HairShadowVertex(HairAttributes input)
{
    // HDRP applies raster depth bias and receiver normal bias. No per-light
    // URP-style caster globals are bound by HDRP.
    return HairVertex(input);
}

float4 HairShadowFragment(HairVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    float alpha = HairAlpha(input, SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).a);
    #if defined(UMA_HAIR_ALPHA_BLEND)
        alpha *= _Coverage;
        clip(alpha - max(_ShadowCutoff, 0.0001));
    #else
    if (_DitheredOpacity > 0.5)
        HairSoftCoverage(SharpenAlpha(alpha, max(_ShadowCutoff, 0.0001)), input);
    else clip(alpha - max(_ShadowCutoff, 0.0001));
    #endif
    return 0;
}
UmaDepthOutput HairDepthNormalsFragment(HairVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float faceSign = IS_FRONT_VFACE(facing, 1.0, -1.0);
    float alpha = HairClip(HairAlpha(input, SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).a), input);
    return UmaDepth(input.positionCS, HairNormal(input, faceSign), normalize(input.normalWS) * faceSign,
        1 - _Smoothness, alpha);
}

UmaMotionOutput HairMotionFragment(HairVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    float faceSign = IS_FRONT_VFACE(facing, 1.0, -1.0);
    float alpha = HairClip(HairAlpha(input, SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).a), input);
    return UmaMotion(input.positionCS, input.currentCS, input.previousCS, HairNormal(input, faceSign),
        normalize(input.normalWS) * faceSign, 1 - _Smoothness, alpha);
}
#endif