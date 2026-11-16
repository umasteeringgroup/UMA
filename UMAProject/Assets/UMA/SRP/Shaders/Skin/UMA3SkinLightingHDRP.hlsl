// HDRP-compatible UMA realistic skin lighting.
// Same texture contract / material properties as the URP variant.
// Forward-only custom skin lighting over HDRP light data + baked GI,
// without URP ShaderLibrary dependencies.
#ifndef UMA_REALISTIC_SKIN_LIGHTING_HDRP
#define UMA_REALISTIC_SKIN_LIGHTING_HDRP

#include "UMA3SkinInputHDRP.hlsl"
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
float UmaSq(float x) { return x * x; }
float3 UmaSq3(float3 x) { return x * x; }

SkinVaryings SkinVertex(SkinAttributes v)
{
    SkinVaryings o = (SkinVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
    o.positionCS = TransformWorldToHClip(positionWS);
    o.positionWS = positionWS;
    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
    float3 tangentWS = TransformObjectToWorldDir(v.tangentOS.xyz);
    o.tangentWS = float4(tangentWS, v.tangentOS.w * GetOddNegativeScale());
    o.uv = v.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    o.color = v.color;
    o.lightmapUV = v.lightmapUV; // Sampling applies the atlas transform.
    o.dynamicUV = v.dynamicUV;
    return o;
}

SkinVaryings SkinMotionVertex(SkinAttributes v)
{
    SkinVaryings o = SkinVertex(v);
    UNITY_SETUP_INSTANCE_ID(v);
    UmaMotionPositions(v.positionOS.xyz, v.previousPositionOS, o.currentCS, o.previousCS);
    return o;
}

float3 UmaSkinWorldNormal(SkinVaryings v, float3 normalTS)
{
    float3 bitangent = v.tangentWS.w * cross(v.normalWS, v.tangentWS.xyz);
    float3x3 tbn = float3x3(v.tangentWS.xyz, bitangent, v.normalWS);
    return normalize(mul(normalTS, tbn));
}

float3 UmaBakedSkinGI(float3 normalWS, float2 lightmapUV, float2 dynamicUV, float3 positionWS, uint2 positionSS)
{
    float3 baked = 0;
    #if defined(LIGHTMAP_ON)
        #ifdef DIRLIGHTMAP_COMBINED
            float3 backBaked = 0;
            SampleDirectionalLightmap(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap),
                TEXTURE2D_ARGS(unity_LightmapInd, samplerunity_Lightmap),
                lightmapUV, unity_LightmapST, normalWS, -normalWS, true, baked, backBaked);
        #else
            baked += SampleSingleLightmap(TEXTURE2D_ARGS(unity_Lightmap, samplerunity_Lightmap),
                lightmapUV, unity_LightmapST, true);
        #endif
    #endif
    #if defined(DYNAMICLIGHTMAP_ON)
        baked += SampleSingleLightmap(TEXTURE2D_ARGS(unity_DynamicLightmap, samplerunity_DynamicLightmap),
            dynamicUV, unity_DynamicLightmapST, false);
    #endif
    #if !defined(LIGHTMAP_ON) && !defined(DYNAMICLIGHTMAP_ON)
        baked += EvaluateAmbientProbe(normalWS);
    #endif
    return max(baked, 0);
}

float3 UmaSkinGGX(float3 N, float3 V, float3 L, float roughness, float3 F0)
{
    float3 H = SafeNormalize(L + V);
    float NoH = saturate(dot(N, H));
    float NoV = saturate(dot(N, V));
    float NoL = saturate(dot(N, L));
    float a = max(roughness * roughness, 0.002);
    float a2 = a * a;
    float d = (NoH * NoH * (a2 - 1.0) + 1.0);
    float D = a2 / (PI * d * d + 1e-5);
    float k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    float G = (NoV / (NoV * (1.0 - k) + k + 1e-5)) * (NoL / (NoL * (1.0 - k) + k + 1e-5));
    float3 F = F0 + (1.0 - F0) * pow(1.0 - saturate(dot(H, V)), 5.0);
    return D * G * F / max(4.0 * NoV * NoL, 1e-4);
}

// One HDRP light contribution. Mirrors the URP SkinDirect() response:
// wrapped RGB diffusion + dual GGX lobes + masked backlight transmission.
float3 UmaSkinLight(SkinData s, float3 diffuseNormalWS, float3 normalWS, float3 viewWS,
    float3 positionWS, float2 positionSS, float3 wrap,
    float3 lightDir, float3 lightColor, float diffuseDimmer, float specularDimmer, float attenuation, float shadow)
{
    float nl = dot(diffuseNormalWS, lightDir);
    float3 diffuse = lerp(saturate(nl).xxx, max(0, nl + wrap) / UmaSq3(1 + wrap), s.scatter);
    float baseRough = saturate(1.0 - s.surface.smoothness);
    float oilRough = saturate(1.0 - _OilSmoothness);
    float3 specBase = UmaSkinGGX(normalWS, viewWS, lightDir, baseRough, s.surface.specular);
    float3 specOil = UmaSkinGGX(normalWS, viewWS, lightDir, oilRough, s.surface.specular);
    float3 spec = lerp(specBase, specOil, s.oil);
    float back = pow(saturate(dot(-lightDir, viewWS)), _TransmissionPower) * saturate(-nl);
    float3 transmitted = s.transmission * back * _SubsurfaceColor.rgb;
    float3 albedo = s.surface.albedo * s.surface.occlusion;
    float ndl = saturate(dot(normalWS, lightDir));
    return lightColor * attenuation * shadow * (albedo * (diffuse + transmitted) * diffuseDimmer / PI + spec * ndl * specularDimmer);
}

float UmaDirectionalShadow(float3 positionWS, float2 positionSS, float3 normalWS, DirectionalLightData light)
{
    if (light.shadowIndex < 0 || light.shadowDimmer <= 0 || light.lightDimmer <= 0)
        return 1.0;
    float3 L = -light.forward;
    HDShadowContext shadowContext = InitShadowContext();
    float shadow = GetDirectionalShadowAttenuation(shadowContext, positionSS, positionWS, normalWS, light.shadowIndex, L);
    return lerp(1.0, shadow, saturate(light.shadowDimmer));
}

float UmaPunctualShadow(float3 positionWS, float2 positionSS, float3 normalWS, LightData light, float3 L, float distance)
{
    if (light.shadowIndex < 0 || light.shadowDimmer <= 0 || light.lightDimmer <= 0)
        return 1.0;
    bool isPointLight = light.lightType == GPULIGHTTYPE_POINT;
    bool perspective = light.lightType != GPULIGHTTYPE_PROJECTOR_BOX;
    float shadow = GetPunctualShadowAttenuation(InitShadowContext(), positionSS, positionWS, normalWS,
        light.shadowIndex, L, distance, isPointLight, perspective);
    return lerp(1.0, shadow, saturate(light.shadowDimmer));
}

void SkinFragment(SkinVaryings v, out float4 color : SV_Target0)
{
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    SkinData s = SampleSkin(v.uv);
    s.surface.albedo *= v.color.rgb;

    float3 normalWS = UmaSkinWorldNormal(v, s.surface.normalTS);
    float3 diffuseNormalWS = UmaSkinWorldNormal(v, s.diffuseNormalTS);
    float3 viewWS = GetWorldSpaceNormalizeViewDir(v.positionWS);
    uint2 positionSS = uint2(v.positionCS.xy);
    float2 screenUV = v.positionCS.xy * _ScreenSize.zw;

    float curvature = (length(ddx(normalize(v.normalWS))) + length(ddy(normalize(v.normalWS)))) /
        max(length(ddx(v.positionWS)) + length(ddy(v.positionWS)), 0.00001);
    float3 wrap = min(0.75, (_DiffusionWrap + curvature * _ScatterRadiusMM * 0.001) *
        max(float3(0.04,0.04,0.04), _SubsurfaceColor.rgb));
    float variance = max(dot(ddx(normalWS), ddx(normalWS)),
        dot(ddy(normalWS), ddy(normalWS))) * _SpecularAA;
    s.surface.smoothness = 1 - sqrt(saturate(UmaSq(1 - s.surface.smoothness) + min(variance, 0.2)));

    float exposure = GetCurrentExposureMultiplier();
    uint layers = GetMeshRenderingLayerMask();

    float3 baked = UmaBakedSkinGI(normalWS, v.lightmapUV, v.dynamicUV, v.positionWS, positionSS);
    float3 result = s.surface.albedo * baked * s.surface.occlusion * exposure;

    [loop] for (uint i = 0; i < _DirectionalLightCount; i++)
    {
        DirectionalLightData light = _DirectionalLightDatas[i];
        if ((light.lightLayers & layers) == 0)
            continue;
        if (light.lightDimmer <= 0)
            continue;
        float3 L = -light.forward;
        float shadow = UmaDirectionalShadow(v.positionWS, screenUV * _ScreenSize.xy, normalWS, light);
        float3 lightColor = light.color * exposure;
        result += UmaSkinLight(s, diffuseNormalWS, normalWS, viewWS, v.positionWS, screenUV,
            wrap, L, lightColor, light.diffuseDimmer, light.specularDimmer, 1.0, shadow);
    }

    [loop] for (uint j = 0; j < _PunctualLightCount; j++)
    {
        LightData light = _LightDatas[j];
        if ((light.lightLayers & layers) == 0)
            continue;
        if (light.lightDimmer <= 0)
            continue;
        float3 L; float4 distances;
        GetPunctualLightVectors(v.positionWS, light, L, distances);
        float attenuation = PunctualLightAttenuation(distances, light.rangeAttenuationScale,
            light.rangeAttenuationBias, light.angleScale, light.angleOffset);
        if (attenuation <= 0)
            continue;
        float shadow = UmaPunctualShadow(v.positionWS, screenUV * _ScreenSize.xy, normalWS, light, L, distances.x);
        float3 lightColor = light.color * exposure;
        result += UmaSkinLight(s, diffuseNormalWS, normalWS, viewWS, v.positionWS, screenUV,
            wrap, L, lightColor, light.diffuseDimmer, light.specularDimmer, attenuation, shadow);
    }

    if (_SkinDebug > 0.5 && _SkinDebug < 1.5) result = s.surface.albedo;
    if (_SkinDebug > 1.5 && _SkinDebug < 2.5) result = normalWS * 0.5 + 0.5;
    if (_SkinDebug > 2.5 && _SkinDebug < 3.5) result = s.scatter.xxx;
    if (_SkinDebug > 3.5 && _SkinDebug < 4.5) result = s.surface.smoothness.xxx;
    if (_SkinDebug > 4.5) result = s.surface.occlusion.xxx;
    color = float4(result, 1);
}

UmaDepthOutput SkinDepthNormals(SkinVaryings v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    SkinData s = SampleSkin(v.uv);
    return UmaDepth(v.positionCS, UmaSkinWorldNormal(v, s.surface.normalTS),
        normalize(v.normalWS), 1 - s.surface.smoothness, 1);
}

UmaMotionOutput SkinMotionFragment(SkinVaryings v)
{
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    SkinData s = SampleSkin(v.uv);
    return UmaMotion(v.positionCS, v.currentCS, v.previousCS, UmaSkinWorldNormal(v, s.surface.normalTS),
        normalize(v.normalWS), 1 - s.surface.smoothness, 1);
}
#endif