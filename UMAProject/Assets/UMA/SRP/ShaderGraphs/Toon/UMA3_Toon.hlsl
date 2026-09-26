#ifndef UMA_TOON_INCLUDED
#define UMA_TOON_INCLUDED

#if !defined(SHADERGRAPH_PREVIEW) && defined(UNIVERSAL_PIPELINE_CORE_INCLUDED)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

float3 UMA_ToonBand(float value, float low, float high, float softness, float3 shadow, float3 mid,
    UnityTexture2D ramp, float useRamp)
{
    float width = max(softness, 0.0001);
    float lo = min(low, high), hi = max(low, high);
    float3 bands = lerp(shadow, mid, smoothstep(lo - width, lo + width, value));
    bands = lerp(bands, float3(1,1,1), smoothstep(hi - width, hi + width, value));
    if (useRamp > 0.5)
        bands = SAMPLE_TEXTURE2D_LOD(ramp.tex, ramp.samplerstate, float2(clamp(value, 0.001, 0.999), 0.5), 0).rgb;
    return bands;
}

void UMA_ToonAccumulate(float3 normal, float3 direction, float3 color, float attenuation, float shadow,
    float low, float high, float softness, float3 shadowTint, float3 midTint, UnityTexture2D ramp,
    float useRamp, inout float3 lighting, inout float tone)
{
    float value = saturate(dot(normal, direction)) * saturate(shadow);
    lighting += UMA_ToonBand(value, low, high, softness, shadowTint, midTint, ramp, useRamp) * max(color, 0) * attenuation;
    tone = max(tone, value * saturate(dot(color, float3(0.2126,0.7152,0.0722)) * attenuation));
}

void UMA_ToonLighting(float3 positionWS, float3 normalWS, float low, float high, float softness,
    float3 shadowTint, float3 midTint, UnityTexture2D ramp, float useRamp, float shadowStrength,
    out float3 lighting, out float tone)
{
    lighting = 0; tone = 0;
#if defined(SHADERGRAPH_PREVIEW)
    UMA_ToonAccumulate(normalWS, normalize(float3(0.5,0.7,0.4)), 1, 1, 1,
        low, high, softness, shadowTint, midTint, ramp, useRamp, lighting, tone);
#elif defined(UNIVERSAL_PIPELINE_CORE_INCLUDED)
    float4 clipPos = TransformWorldToHClip(positionWS);
    float4 screenPos = ComputeScreenPos(clipPos);
    float2 screenUV = screenPos.xy / screenPos.w;
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        float4 shadowCoord = screenPos;
    #else
        float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    #endif
    Light mainLight = GetMainLight(shadowCoord, positionWS, half4(1,1,1,1));
    #ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer()))
    #endif
    UMA_ToonAccumulate(normalWS, mainLight.direction, mainLight.color, mainLight.distanceAttenuation,
        lerp(1, mainLight.shadowAttenuation, shadowStrength), low, high, softness, shadowTint, midTint, ramp, useRamp, lighting, tone);
    #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
        InputData inputData = (InputData)0;
        inputData.positionWS = positionWS;
        inputData.normalizedScreenSpaceUV = screenUV;
        #if USE_CLUSTER_LIGHT_LOOP
        UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {
            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
            Light light = GetAdditionalLight(lightIndex, positionWS, half4(1,1,1,1));
            #ifdef _LIGHT_LAYERS
            if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) continue;
            #endif
            UMA_ToonAccumulate(normalWS, light.direction, light.color, light.distanceAttenuation,
                lerp(1, light.shadowAttenuation, shadowStrength), low, high, softness, shadowTint, midTint, ramp, useRamp, lighting, tone);
        }
        #endif
        uint lightCount = GetAdditionalLightsCount();
        LIGHT_LOOP_BEGIN(lightCount)
            Light light = GetAdditionalLight(lightIndex, positionWS, half4(1,1,1,1));
            #ifdef _LIGHT_LAYERS
            if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) continue;
            #endif
            UMA_ToonAccumulate(normalWS, light.direction, light.color, light.distanceAttenuation,
                lerp(1, light.shadowAttenuation, shadowStrength), low, high, softness, shadowTint, midTint, ramp, useRamp, lighting, tone);
        LIGHT_LOOP_END
    #endif
#elif defined(SHADERPASS_FORWARD_UNLIT) && (SHADERPASS == SHADERPASS_FORWARD_UNLIT)
    // HDRP's shadow-matte target supplies the shadow context and light-loop declarations.
    // ShadowTint alpha is zero in the graph: shadows are applied here, once per light.
    HDShadowContext context = InitShadowContext();
    float4 clipPos = TransformWorldToHClip(positionWS);
    float2 screenUV = clipPos.xy / clipPos.w * 0.5 + 0.5;
    #if UNITY_UV_STARTS_AT_TOP
        screenUV.y = 1 - screenUV.y;
    #endif
    float2 positionSS = screenUV * _ScreenSize.xy;
    ApplyCameraRelativeXR(positionWS);
    uint layers = GetMeshRenderingLayerMask();
    float exposure = GetCurrentExposureMultiplier();
    float sunShadow = 1;
    if (_DirectionalShadowIndex >= 0)
    {
        DirectionalLightData sun = _DirectionalLightDatas[_DirectionalShadowIndex];
        if (sun.shadowIndex >= 0 && sun.shadowDimmer > 0)
            sunShadow = lerp(1, GetDirectionalShadowAttenuation(context, positionSS, positionWS, normalWS,
                sun.shadowIndex, -sun.forward), sun.shadowDimmer * shadowStrength);
    }
    [loop] for (uint i = 0; i < _DirectionalLightCount; i++)
    {
        DirectionalLightData light = _DirectionalLightDatas[i];
        if ((light.lightLayers & layers) == 0) continue;
        float3 direction = -light.forward;
        float shadow = (int)i == _DirectionalShadowIndex ? sunShadow : 1;
        UMA_ToonAccumulate(normalWS, direction, light.color * light.diffuseDimmer * exposure, 1, shadow,
            low, high, softness, shadowTint, midTint, ramp, useRamp, lighting, tone);
    }
    [loop] for (uint j = 0; j < _PunctualLightCount; j++)
    {
        LightData light = _LightDatas[j];
        if ((light.lightLayers & layers) == 0) continue;
        float3 direction; float4 distances;
        GetPunctualLightVectors(positionWS, light, direction, distances);
        float attenuation = PunctualLightAttenuation(distances, light.rangeAttenuationScale, light.rangeAttenuationBias,
            light.angleScale, light.angleOffset);
        float shadow = 1;
        if (light.shadowIndex >= 0 && light.shadowDimmer > 0 && attenuation > 0)
            shadow = lerp(1, GetPunctualShadowAttenuation(context, positionSS, positionWS, normalWS, light.shadowIndex,
                direction, distances.x, light.lightType == GPULIGHTTYPE_POINT, light.lightType != GPULIGHTTYPE_PROJECTOR_BOX), light.shadowDimmer * shadowStrength);
        UMA_ToonAccumulate(normalWS, direction, light.color * light.diffuseDimmer * exposure, attenuation, shadow,
            low, high, softness, shadowTint, midTint, ramp, useRamp, lighting, tone);
    }
#else
    // Meta, picking, depth and ray-tracing passes do not run the raster light loop.
    lighting = 1; tone = 1;
#endif
}

float3 UMA_ToonGrade(float3 color, float saturation, float contrast, float levels, float hue)
{
    const float3 axis = float3(0.577350269,0.577350269,0.577350269);
    float angle = hue * 6.28318530718;
    float c = cos(angle), s = sin(angle);
    color = color * c + cross(axis, color) * s + axis * dot(axis, color) * (1 - c);
    float luma = dot(color, float3(0.2126,0.7152,0.0722));
    color = max(0, lerp(luma.xxx, color, max(0, saturation)));
    color = max(0, (color - 0.5) * max(0, contrast) + 0.5);
    if (levels >= 2)
    {
        float steps = max(1, floor(levels + 0.5) - 1);
        color = floor(saturate(color) * steps + 0.5) / steps;
    }
    return color;
}

float UMA_ToonLine(float coordinate, float width)
{
    float distance = abs(frac(coordinate) - 0.5);
    float aa = max(fwidth(coordinate), 0.0001);
    return 1 - smoothstep(width * 0.5 - aa, width * 0.5 + aa, distance);
}

void UMA_ToonSurface_float(float2 UV, float3 PositionWS, float3 NormalWS, float3 TangentWS, float3 BitangentWS,
    UnityTexture2D BaseMap, UnityTexture2D NormalMap, UnityTexture2D Ramp, float4 BaseColor, float NormalStrength,
    float LowThreshold, float HighThreshold, float Softness, float3 ShadowColor, float3 MidColor, float UseRamp,
    float ShadowStrength, float3 AmbientColor, float LightIntensity, float Saturation, float Contrast,
    float Posterization, float HueShift, float HatchStrength, float HalftoneStrength, float PatternScale,
    float PatternAngle, float PatternWidth, float3 InkColor, float PatternScreenSpace,
    out float3 Color, out float Alpha)
{
    float2 uv = UV * BaseMap.scaleTranslate.xy + BaseMap.scaleTranslate.zw;
    float4 albedo = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, uv) * BaseColor;
    float3 normalTS = UnpackNormal(SAMPLE_TEXTURE2D(NormalMap.tex, NormalMap.samplerstate,
        UV * NormalMap.scaleTranslate.xy + NormalMap.scaleTranslate.zw));
    normalTS.xy *= max(0, NormalStrength);
    normalTS = normalize(normalTS);
    float3 normal = normalize(normalTS.x * normalize(TangentWS) + normalTS.y * normalize(BitangentWS) + normalTS.z * normalize(NormalWS));
    float3 lighting; float tone;
    UMA_ToonLighting(PositionWS, normal, LowThreshold, HighThreshold, Softness, ShadowColor, MidColor,
        Ramp, UseRamp, saturate(ShadowStrength), lighting, tone);
    Color = albedo.rgb * (max(0, AmbientColor) + lighting * max(0, LightIntensity));
    Color = UMA_ToonGrade(Color, Saturation, Contrast, Posterization, HueShift);
    if (HatchStrength > 0 || HalftoneStrength > 0)
    {
        float2 patternUV = UV;
        if (PatternScreenSpace > 0.5)
        {
            float4 clipPos = TransformWorldToHClip(PositionWS);
            patternUV = clipPos.xy / max(abs(clipPos.w), 0.0001);
            patternUV.x *= _ScreenParams.x / max(_ScreenParams.y, 1);
        }
        float angle = PatternAngle * 0.01745329252;
        float2 p = float2(cos(angle)*patternUV.x - sin(angle)*patternUV.y,
            sin(angle)*patternUV.x + cos(angle)*patternUV.y) * max(1, PatternScale);
        float darkness = 1 - saturate(tone + dot(AmbientColor, float3(0.2126,0.7152,0.0722)));
        float width = clamp(PatternWidth, 0.01, 0.9);
        float hatch = UMA_ToonLine(p.x, width) * smoothstep(0.15,0.4,darkness);
        hatch = max(hatch, UMA_ToonLine(p.y, width) * smoothstep(0.5,0.8,darkness));
        float radius = sqrt(darkness) * 0.55;
        float d = length(frac(p) - 0.5);
        float aa = max(fwidth(d), 0.0001);
        float dots = (1 - smoothstep(radius-aa, radius+aa, d)) * step(0.01,darkness);
        float ink = max(hatch * saturate(HatchStrength), dots * saturate(HalftoneStrength));
        Color = lerp(Color, InkColor, saturate(ink));
    }
    Alpha = albedo.a;
}

#endif
