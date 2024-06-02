#ifndef UNIVERSAL_LIGHTING_INCLUDED
#define UNIVERSAL_LIGHTING_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/BRDF.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/Debugging3D.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/AmbientOcclusion.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
#include "HairLitInput.hlsl"

#include "MathFunc.hlsl"


#if defined(LIGHTMAP_ON)
    #define DECLARE_LIGHTMAP_OR_SH(lmName, shName, index) float2 lmName : TEXCOORD##index
    #define OUTPUT_LIGHTMAP_UV(lightmapUV, lightmapScaleOffset, OUT) OUT.xy = lightmapUV.xy * lightmapScaleOffset.xy + lightmapScaleOffset.zw;
    #define OUTPUT_SH(normalWS, OUT)
#else
    #define DECLARE_LIGHTMAP_OR_SH(lmName, shName, index) half3 shName : TEXCOORD##index
    #define OUTPUT_LIGHTMAP_UV(lightmapUV, lightmapScaleOffset, OUT)
    #define OUTPUT_SH(normalWS, OUT) OUT.xyz = SampleSHVertex(normalWS)
#endif

///////////////////////////////////////////////////////////////////////////////
//                      Lighting Functions                                   //
///////////////////////////////////////////////////////////////////////////////

// Return view direction in tangent space, make sure tangentWS.w is already multiplied by GetOddNegativeScale()
half3 GetViewDirectionTangentSpace(half4 tangentWS, half3 normalWS, half3 viewDirWS)
{
    // must use interpolated tangent, bitangent and normal before they are normalized in the pixel shader.
    half3 unnormalizedNormalWS = normalWS;
    const half renormFactor = 1.0 / length(unnormalizedNormalWS);

    // use bitangent on the fly like in hdrp
    // IMPORTANT! If we ever support Flip on double sided materials ensure bitangent and tangent are NOT flipped.
    half crossSign = (tangentWS.w > 0.0 ? 1.0 : -1.0); // we do not need to multiple GetOddNegativeScale() here, as it is done in vertex shader
    half3 bitang = crossSign * cross(normalWS.xyz, tangentWS.xyz);

    half3 WorldSpaceNormal = renormFactor * normalWS.xyz;       // we want a unit length Normal Vector node in shader graph

    // to preserve mikktspace compliance we use same scale renormFactor as was used on the normal.
    // This is explained in section 2.2 in "surface gradient based bump mapping framework"
    half3 WorldSpaceTangent = renormFactor * tangentWS.xyz;
    half3 WorldSpaceBiTangent = renormFactor * bitang;

    half3x3 tangentSpaceTransform = half3x3(WorldSpaceTangent, WorldSpaceBiTangent, WorldSpaceNormal);
    half3 viewDirTS = mul(tangentSpaceTransform, viewDirWS);

    return viewDirTS;
}


half3 HairSpecular(BRDFData brdfData,float3 normalWS, half3 tangentWS, half3 lightDirectionWS, half3 viewDirectionWS, float iorOffset, float3 specularColor)
{
    float3 lightDirectionWSFloat3 = float3(lightDirectionWS);
    float3 halfVec = SafeNormalize(lightDirectionWSFloat3 + float3(viewDirectionWS));
    float NdotH = saturate(dot(float3(normalWS), halfVec));


    //half LdotH = half(saturate(dot(lightDirectionWSFloat3, halfVec)));
    
    float d = NdotH * NdotH * brdfData.roughness2MinusOne + 1.00001f;

    //half LoH2 = LdotH * LdotH;
    //half highlight = brdfData.roughness2 / ((d * d) * max(0.1h, LdotH) * brdfData.normalizationTerm);

 

    half ior = lerp(1 , 2, brdfData.roughness2);
    ior += iorOffset;
    float specularTerm = 0;
    specularTerm = CustomFresnel(viewDirectionWS,tangentWS,ior) ;
    specularTerm = clamp(specularTerm,0,1);
    specularTerm *=  max(0.01f,1 -  d);
    specularTerm = saturate(specularTerm);

    
    float3 spec = lerp( specularTerm  * specularColor * brdfData.albedo, specularTerm * specularColor, specularTerm );
    return spec;
}



half3 HairLighting(BRDFData brdfData, half3 lightColor, half3 lightDirectionWS, half lightAttenuation, half3 normalWS, half3 viewDirectionWS , float4 tangentWS, float baseTanShift)
{
    // Diffuse Lighting
    // based on lambert but brightend NdotL to approximate subsurface scattering
    // all of this prob not physically accurate
    half NdotL = saturate(dot(normalWS, lightDirectionWS));
    half NdotLModified = lerp(_Subsurface, 1.0f,NdotL);
    
    half3 brdf = (0.0f,0.0f,0.0f);
    float3 diffuse = lerp(brdfData.diffuse,brdfData.albedo,_Subsurface);
    brdf = clamp(NdotLModified, 0.0f,1.0f) * diffuse;

    // radiance is basically the lit aras with falloff from sourcs and light color
    half3 radiance = lightColor * (lightAttenuation * NdotL);

    // just for debug highlights
    //brdf = (0.0f,0.0f,0.0f);
    
    // Specular Highlights
    
    float primaryshift = _PrimaryShift;
    float secondaryShift = _SecondaryShift;
    float tanRoation = _AnisoRot;
    

    float3 t1 = ShiftTangent(normalWS, tangentWS, primaryshift + baseTanShift,tanRoation);
    float3 t2 = ShiftTangent(normalWS, tangentWS, secondaryShift + baseTanShift,tanRoation);

    
    float3 specularColor = _SpecColor;
    //specularColor = (1,1,1);
    float3 spec = (0,0,0);
    spec += HairSpecular(brdfData, normalWS,t2, lightDirectionWS, viewDirectionWS, 0.008f, specularColor);
    spec += HairSpecular(brdfData, normalWS, t1, lightDirectionWS, viewDirectionWS,0.0f, specularColor);

    brdf += spec;

    //return spec * radiance;
    return brdf * radiance;
    
}


half3 LightingPhysicallyBased(BRDFData brdfData, Light light, half3 normalWS, half3 viewDirectionWS, float4 tangentWS,float baseTanShift)
{
    return HairLighting(brdfData, light.color, light.direction, light.distanceAttenuation * light.shadowAttenuation, normalWS, viewDirectionWS, tangentWS, baseTanShift);
}


half3 VertexLighting(float3 positionWS, half3 normalWS)
{
    half3 vertexLightColor = half3(0.0, 0.0, 0.0);

#ifdef _ADDITIONAL_LIGHTS_VERTEX
    uint lightsCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(lightsCount)
        Light light = GetAdditionalLight(lightIndex, positionWS);
        half3 lightColor = light.color * light.distanceAttenuation;
        vertexLightColor += LightingLambert(lightColor, light.direction, normalWS);
    LIGHT_LOOP_END
#endif

    return vertexLightColor;
}

struct LightingData
{
    half3 giColor;
    half3 mainLightColor;
    half3 additionalLightsColor;
    half3 vertexLightingColor;
};


half4 CalculateFinalColor(LightingData lightingData, half alpha)
{
    
    half3 lightingColor = 0;

    // Look into metallic and smoothness of gi color
    // final produced by environment lighting / GI
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_GLOBAL_ILLUMINATION))
    {
        lightingColor += lightingData.giColor;
    }
    // Final Produced by Main Light
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_MAIN_LIGHT))
    {
        lightingColor += lightingData.mainLightColor;
    }
    // Final produced by other lights
    if (IsLightingFeatureEnabled(DEBUGLIGHTINGFEATUREFLAGS_ADDITIONAL_LIGHTS))
    {
        lightingColor += lightingData.additionalLightsColor;
    }

    half4 finalColor = half4(lightingColor,alpha);
    return finalColor;
}

LightingData CreateLightingData(InputData inputData, SurfaceData surfaceData)
{
    LightingData lightingData;

    lightingData.giColor = inputData.bakedGI;
    lightingData.vertexLightingColor = 0;
    lightingData.mainLightColor = 0;
    lightingData.additionalLightsColor = 0;

    return lightingData;
}


	#if UNITY_VERSION < 202220
	/*
	GetMeshRenderingLayer() is only available in 2022.2+
	Previous versions need to use GetMeshRenderingLightLayer()
	*/
	uint GetMeshRenderingLayer(){
		return GetMeshRenderingLightLayer();
	}
	#endif

/// Called By Fragment
half4 UniversalFragmentPBR(InputData inputData, SurfaceData surfaceData, float baseTanShift, float4 tangentWS)
{
    #if defined(_SPECULARHIGHLIGHTS_OFF)
    bool specularHighlightsOff = true;
    #else
    bool specularHighlightsOff = false;
    #endif
    
    BRDFData brdfData;

    // NOTE: can modify "surfaceData"...
    InitializeBRDFData(surfaceData, brdfData);
    
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);

	#if UNITY_VERSION < 202220
       uint meshRenderingLayers = GetMeshRenderingLightLayer();
    #else
       uint meshRenderingLayers = GetMeshRenderingLayer();
    #endif

    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    LightingData lightingData = CreateLightingData(inputData, surfaceData);
    lightingData.giColor = GlobalIllumination(brdfData,inputData.bakedGI, aoFactor.indirectAmbientOcclusion, inputData.positionWS,inputData.normalWS, inputData.viewDirectionWS);
    

    
    if (IsMatchingLightLayer(mainLight.layerMask, meshRenderingLayers))
    {
        lightingData.mainLightColor = LightingPhysicallyBased(brdfData,mainLight,inputData.normalWS, inputData.viewDirectionWS, tangentWS,baseTanShift);
    }
    
    // Loop over all lights
    #if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);

        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
        {
            lightingData.additionalLightsColor += LightingPhysicallyBased(brdfData, light,inputData.normalWS, inputData.viewDirectionWS,tangentWS, baseTanShift) ;
        }
    
    LIGHT_LOOP_END
    #endif

    // add up all lighting together
    return CalculateFinalColor(lightingData, surfaceData.alpha);
}

#endif
