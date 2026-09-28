#ifndef UMA_REALISTIC_SKIN_LIGHTING
#define UMA_REALISTIC_SKIN_LIGHTING
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"

struct SkinAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    float2 dynamicUV : TEXCOORD2;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct SkinVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    half3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    half4 fogAndLight : TEXCOORD4;
    DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 5);
    #ifdef DYNAMICLIGHTMAP_ON
    float2 dynamicUV : TEXCOORD6;
    #endif
    #ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD7;
    #endif
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
SkinVaryings SkinVertex(SkinAttributes v)
{
    SkinVaryings o = (SkinVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
    VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
    o.positionCS = p.positionCS; o.positionWS = p.positionWS;
    o.normalWS = n.normalWS;
    o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
    o.uv = TRANSFORM_TEX(v.uv, _BaseMap); o.color = v.color;
    o.fogAndLight = half4(ComputeFogFactor(p.positionCS.z), VertexLighting(p.positionWS, n.normalWS));
    OUTPUT_LIGHTMAP_UV(v.lightmapUV, unity_LightmapST, o.lightmapUV);
    #ifdef DYNAMICLIGHTMAP_ON
    o.dynamicUV = v.dynamicUV * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
    #endif
    OUTPUT_SH4(p.positionWS, n.normalWS, GetWorldSpaceNormalizeViewDir(p.positionWS), o.vertexSH, o.probeOcclusion);
    return o;
}
InputData SkinInput(SkinVaryings v, half3 normalTS)
{
    InputData d = (InputData)0;
    d.positionWS = v.positionWS;
    half3 bitangent = v.tangentWS.w * cross(v.normalWS, v.tangentWS.xyz);
    d.tangentToWorld = half3x3(v.tangentWS.xyz, bitangent, v.normalWS);
    d.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, d.tangentToWorld));
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(v.positionWS);
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    d.shadowCoord = ComputeScreenPos(TransformWorldToHClip(v.positionWS));
    #else
    d.shadowCoord = TransformWorldToShadowCoord(v.positionWS);
    #endif
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(v.positionCS);
    d.fogCoord = InitializeInputDataFog(float4(v.positionWS,1), v.fogAndLight.x);
    d.vertexLighting = v.fogAndLight.yzw;
    #if defined(DYNAMICLIGHTMAP_ON)
    d.bakedGI = SAMPLE_GI(v.lightmapUV, v.dynamicUV, v.vertexSH, d.normalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(v.lightmapUV);
    #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    d.bakedGI = SAMPLE_GI(v.vertexSH, GetAbsolutePositionWS(v.positionWS), d.normalWS,
        d.viewDirectionWS, v.positionCS.xy, v.probeOcclusion, d.shadowMask);
    #else
    d.bakedGI = SAMPLE_GI(v.lightmapUV, v.vertexSH, d.normalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(v.lightmapUV);
    #endif
    return d;
}

half3 SkinDirect(BRDFData baseBRDF, BRDFData oilBRDF, SkinData s, InputData d,
    half3 diffuseNormal, half3 wrap, Light light)
{
    #ifdef _LIGHT_LAYERS
    if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) return 0;
    #endif
    half nl = dot(diffuseNormal, light.direction);
    // Normalized wrapped diffuse: integral over the full sphere remains pi.
    // RGB diffusion widths retain detail in blue/green while red crosses the terminator.
    half3 diffuse = lerp(saturate(nl).xxx, max(0, nl + wrap) / Sq(1 + wrap), s.scatter);
    half spec = lerp(DirectBRDFSpecular(baseBRDF, d.normalWS, light.direction, d.viewDirectionWS),
        DirectBRDFSpecular(oilBRDF, d.normalWS, light.direction, d.viewDirectionWS), s.oil);
    half back = pow(saturate(dot(-light.direction, d.viewDirectionWS)), _TransmissionPower)
        * saturate(-nl);
    // Transmission shares attenuation/shadows; it never acts as emission in darkness.
    half3 transmitted = s.transmission * back * _SubsurfaceColor.rgb;
    return light.color * light.distanceAttenuation * light.shadowAttenuation *
        (baseBRDF.diffuse * (diffuse + transmitted) + baseBRDF.specular * spec * saturate(dot(d.normalWS, light.direction)));
}

void SkinFragment(SkinVaryings v, out half4 color : SV_Target0
    #ifdef _WRITE_RENDERING_LAYERS
    , out uint renderingLayers : SV_Target1
    #endif
)
{
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    #ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(v.positionCS);
    #endif
    SkinData s = SampleSkin(v.uv);
    s.surface.albedo *= v.color.rgb;
    InputData inputData = SkinInput(v, s.surface.normalTS);
    #if defined(_DBUFFER)
    ApplyDecalToSurfaceData(v.positionCS, s.surface, inputData);
    // A decal must not turn a dielectric skin surface metallic.
    s.surface.metallic = 0;
    #endif
    half3 dn = normalize(TransformTangentToWorld(s.diffuseNormalTS, inputData.tangentToWorld));
    // World-space derivatives: millimeter radius behaves consistently on scaled avatars.
    float curvature = (length(ddx(normalize(v.normalWS))) + length(ddy(normalize(v.normalWS)))) /
        max(length(ddx(v.positionWS)) + length(ddy(v.positionWS)), 0.00001);
    half3 wrap = min(0.75, (_DiffusionWrap + curvature * _ScatterRadiusMM * 0.001) *
        max(half3(0.04,0.04,0.04), _SubsurfaceColor.rgb));
    // Filter the normal distribution, not the albedo. Prevents sparkling pores at distance.
    float variance = max(dot(ddx(inputData.normalWS), ddx(inputData.normalWS)),
        dot(ddy(inputData.normalWS), ddy(inputData.normalWS))) * _SpecularAA;
    s.surface.smoothness = 1 - sqrt(saturate(Sq(1 - s.surface.smoothness) + min(variance, 0.2)));
    BRDFData baseBRDF, oilBRDF;
    InitializeBRDFData(s.surface, baseBRDF);
    SurfaceData oilSurface = s.surface;
    oilSurface.smoothness = 1 - sqrt(saturate(Sq(1 - _OilSmoothness) + min(variance,0.2)));
    InitializeBRDFData(oilSurface, oilBRDF);
    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, s.surface);
    Light mainLight = GetMainLight(inputData, shadowMask, ao);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);
    BRDFData noCoat = (BRDFData)0;
    half3 giBase = GlobalIllumination(baseBRDF, noCoat, 0, inputData.bakedGI, ao.indirectAmbientOcclusion,
        inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
    half3 giOil = GlobalIllumination(oilBRDF, noCoat, 0, inputData.bakedGI, ao.indirectAmbientOcclusion,
        inputData.positionWS, inputData.normalWS, inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV);
    half3 result = lerp(giBase, giOil, s.oil) + SkinDirect(baseBRDF, oilBRDF, s, inputData, dn, wrap, mainLight);
    #if defined(_ADDITIONAL_LIGHTS)
    #if USE_CLUSTER_LIGHT_LOOP
    UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        result += SkinDirect(baseBRDF, oilBRDF, s, inputData, dn, wrap, light);
    }
    #endif
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        result += SkinDirect(baseBRDF, oilBRDF, s, inputData, dn, wrap, light);
    LIGHT_LOOP_END
    #endif
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
    result += inputData.vertexLighting * baseBRDF.diffuse;
    #endif
    result = MixFog(result, inputData.fogCoord);
    if (_SkinDebug > 0.5 && _SkinDebug < 1.5) result = s.surface.albedo;
    if (_SkinDebug > 1.5 && _SkinDebug < 2.5) result = inputData.normalWS * 0.5 + 0.5;
    if (_SkinDebug > 2.5 && _SkinDebug < 3.5) result = s.scatter.xxx;
    if (_SkinDebug > 3.5 && _SkinDebug < 4.5) result = s.surface.smoothness.xxx;
    if (_SkinDebug > 4.5) result = s.surface.occlusion.xxx;
    color = half4(result, 1);
    #ifdef _WRITE_RENDERING_LAYERS
    renderingLayers = EncodeMeshRenderingLayer();
    #endif
}

void SkinDepthNormals(SkinVaryings v, out half4 normal : SV_Target0
    #ifdef _WRITE_RENDERING_LAYERS
    , out uint renderingLayers : SV_Target1
    #endif
)
{
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(v);
    #ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(v.positionCS);
    #endif
    InputData d = SkinInput(v, SampleSkin(v.uv).surface.normalTS);
    #ifdef _GBUFFER_NORMALS_OCT
    normal = half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(d.normalWS) * 0.5 + 0.5)), 0);
    #else
    normal = half4(d.normalWS, 0);
    #endif
    #ifdef _WRITE_RENDERING_LAYERS
    renderingLayers = EncodeMeshRenderingLayer();
    #endif
}
#endif
