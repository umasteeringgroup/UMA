#ifndef UMA_REALISTIC_SKIN_SSS_LIGHTING
#define UMA_REALISTIC_SKIN_SSS_LIGHTING
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/BSDF.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonLighting.hlsl"

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
// normalTS shades reflections. ambientNormalWS is the softened normal that ambient light sees.
InputData SkinInput(SkinVaryings v, half3 normalTS, half3 ambientNormalTS)
{
    InputData d = (InputData)0;
    d.positionWS = v.positionWS;
    half3 bitangent = v.tangentWS.w * cross(v.normalWS, v.tangentWS.xyz);
    d.tangentToWorld = half3x3(v.tangentWS.xyz, bitangent, v.normalWS);
    d.normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, d.tangentToWorld));
    half3 ambientNormalWS = NormalizeNormalPerPixel(TransformTangentToWorld(ambientNormalTS, d.tangentToWorld));
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
    d.bakedGI = SAMPLE_GI(v.lightmapUV, v.dynamicUV, v.vertexSH, ambientNormalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(v.lightmapUV);
    #elif !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    d.bakedGI = SAMPLE_GI(v.vertexSH, GetAbsolutePositionWS(v.positionWS), ambientNormalWS,
        d.viewDirectionWS, v.positionCS.xy, v.probeOcclusion, d.shadowMask);
    #else
    d.bakedGI = SAMPLE_GI(v.lightmapUV, v.vertexSH, ambientNormalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(v.lightmapUV);
    #endif
    return d;
}

// ---------------------------------------------------------------------------------------------
// Everything a light needs to shade the skin.
struct SkinShading
{
    float3 positionWS;
    float3 normalWS;            // reflections: full pore detail
    half3 normalR, normalG, normalB; // diffuse: softened by each channel's scattering width
    half3 normalGeometry;       // interpolated vertex normal
    float3 viewWS;
    float NdotV;
    half3 specularColor;
    float alphaBase, alphaOil;  // GGX roughness (perceptual roughness squared)
    half oil, scatter, transmission;
    half3 curvatureRow;         // lookup row per channel for diffuse scattering
    half3 shadowRow;            // lookup row per channel for shadow-edge scattering
    half shadowBlend;
    float3 inverseDistance;     // 1 / scattering distance per channel, in 1/meters
    float curvature;            // 1 / meters
    float thicknessMin;         // meters
    bool hasLookup;
};
struct SkinLightSum
{
    half3 diffuse;      // multiplied by the diffuse color afterwards
    half3 transmission; // multiplied by the diffuse color afterwards
    half3 specular;
};

// Column and row are 0..1 across the texture's first and last texel centers.
half2 SampleSkinLookup(half column, half row)
{
    float2 uv = (saturate(float2(column, row)) * (SKIN_LOOKUP_SIZE - 1.0) + 0.5) / SKIN_LOOKUP_SIZE;
    return SAMPLE_TEXTURE2D_LOD(_SkinScatterLUT, sampler_LinearClamp, uv, 0).rg;
}

// The lookup texture carries a constant in its blue channel. Anything else bound in its place
// (a missing texture shows up as plain white) is detected and the analytic fallback is used.
bool SkinLookupIsValid()
{
    half marker = SAMPLE_TEXTURE2D_LOD(_SkinScatterLUT, sampler_LinearClamp, float2(0.5, 0.5), 0).b;
    return abs(marker - SKIN_LOOKUP_MARKER) < 0.02;
}

// Light transmitted through a slab of the given thickness (meters), normalized Burley profile.
half3 SkinTransmittance(float thickness, float3 inverseDistance)
{
    float3 e = exp(-thickness * inverseDistance * (1.0 / 3.0));
    return 0.25 * e * (e * e + 3.0);
}

// Distance (meters) the surface is expected to be from the lit side when no shadow map can tell:
// the chord through a sphere of the local curvature.
float SkinProxyThickness(SkinShading s, float3 lightDirection)
{
    return 2.0 * max(0.0, -dot(s.normalGeometry, lightDirection)) / max(s.curvature, 1.0);
}

// Shadow-map texel offsets. The map is read with bilinear filtering, so the front surface is
// reconstructed as a smooth sheet instead of one depth per texel.
#define SKIN_THICKNESS_TAPS 9
static const float2 kSkinThicknessTaps[SKIN_THICKNESS_TAPS] =
{
    float2(0, 0), float2(1.2, 0.5), float2(-0.5, 1.2), float2(-1.2, -0.5), float2(0.5, -1.2),
    float2(2.0, -1.6), float2(1.6, 2.0), float2(-2.0, 1.6), float2(-1.6, -2.0)
};

// Where the shadow map is looked up for thickness. Shadow casters are drawn slightly shrunk
// (normal bias), so a ray that runs just under the surface near the light's silhouette misses
// them and the part reads as paper thin. Moving the lookup sideways into the body, by about the
// amount of that shrink but never past the middle of a small part, makes the ray hit again.
// (The sideways vector has the length of the sine of the angle to the light, so a move of one
// radius of curvature lands on the axis of a finger from anywhere on its far side.)
// The move is across the light direction only, so the thickness of flat thin parts is unchanged.
float3 SkinThicknessLookupPosition(SkinShading s, float3 lightDirection, float shrink)
{
    float3 sideways = s.normalGeometry - dot(s.normalGeometry, lightDirection) * lightDirection;
    return s.positionWS - sideways * min(shrink, 1.0 / max(s.curvature, 1.0));
}

// How far the main light travelled inside the body to reach this point, measured in the shadow map.
// known is 0 where no shadow map covers the point, 1 where the measured value can be trusted.
float SkinMainLightThickness(SkinShading s, out half known)
{
    known = 0;
    #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    if (GetMainLightShadowParams().x <= 0) return 0;
    float3 lightDirection = _MainLightPosition.xyz;
    half cascade = ComputeCascadeIndex(s.positionWS);
    float4x4 worldToShadow = _MainLightWorldToShadow[cascade];
    float depthPerMeter = length(worldToShadow[2].xyz);
    float texelsPerMeter = length(worldToShadow[0].xyz) * _MainLightShadowmapSize.z;
    if (depthPerMeter < 1e-6 || texelsPerMeter < 1e-6) return 0;
    float3 p = SkinThicknessLookupPosition(s, lightDirection, 3.0 / texelsPerMeter);
    float3 coord = mul(worldToShadow, float4(p, 1.0)).xyz;
    if (BEYOND_SHADOW_FAR(coord)) return 0;
    float sum = 0;
    for (int i = 0; i < SKIN_THICKNESS_TAPS; ++i)
    {
        float occluder = SAMPLE_TEXTURE2D_LOD(_MainLightShadowmapTexture, sampler_LinearClamp,
            coord.xy + kSkinThicknessTaps[i] * _MainLightShadowmapSize.xy, 0).r;
        #if UNITY_REVERSED_Z
        sum += max(0.0, occluder - coord.z);
        #else
        sum += max(0.0, coord.z - occluder);
        #endif
    }
    known = 1 - GetMainLightShadowFade(s.positionWS);
    return sum / (SKIN_THICKNESS_TAPS * depthPerMeter);
    #else
    return 0;
    #endif
}

// The same for a spot or point light. Its shadow map stores perspective depth, so the occluder is
// found as a fraction of the way from the light to this point.
float SkinAdditionalLightThickness(SkinShading s, int visibleLightIndex, float3 lightDirection, out half known)
{
    known = 0;
    #if defined(ADDITIONAL_LIGHT_CALCULATE_SHADOWS)
    half4 shadowParams = GetAdditionalLightShadowParams(visibleLightIndex);
    int slice = shadowParams.w;
    if (slice < 0 || shadowParams.x <= 0) return 0;
    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 lightPosition = _AdditionalLightsBuffer[visibleLightIndex].position;
    #else
    float4 lightPosition = _AdditionalLightsPosition[visibleLightIndex];
    #endif
    if (lightPosition.w == 0) return 0;
    UNITY_BRANCH if (shadowParams.z) slice += CubeMapFaceID(-lightDirection);
    #if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4x4 worldToShadow = _AdditionalLightsWorldToShadow_SSBO[slice];
    #else
    float4x4 worldToShadow = _AdditionalLightsWorldToShadow[slice];
    #endif
    float3 p = SkinThicknessLookupPosition(s, lightDirection, 0.004);
    float4 clip = mul(worldToShadow, float4(p, 1.0));
    float4 clipLight = mul(worldToShadow, float4(lightPosition.xyz, 1.0));
    if (clip.w <= 1e-6) return 0;
    float3 coord = clip.xyz / clip.w;
    if (BEYOND_SHADOW_FAR(coord)) return 0;
    float lightToPoint = distance(lightPosition.xyz, p);
    float sum = 0;
    for (int i = 0; i < 5; ++i) // the inner ring is enough for the smaller light shadow maps
    {
        float occluder = SAMPLE_TEXTURE2D_LOD(_AdditionalLightsShadowmapTexture, sampler_LinearClamp,
            coord.xy + kSkinThicknessTaps[i] * _AdditionalShadowmapSize.xy, 0).r;
        // Along the ray light -> point, clip z and w are linear in the fraction f. Solve z(f) / w(f) = occluder.
        float denominator = occluder * (clip.w - clipLight.w) - (clip.z - clipLight.z);
        float fraction = abs(denominator) > 1e-9 ? (clipLight.z - occluder * clipLight.w) / denominator : 1.0;
        sum += 1.0 - saturate(fraction);
    }
    known = 1;
    return sum * 0.2 * lightToPoint;
    #else
    return 0;
    #endif
}

// thickness: meters of tissue between the light and this point; known: 1 when it came from a shadow map.
void SkinDirect(SkinShading s, Light light, float thickness, half known, inout SkinLightSum sum)
{
    #ifdef _LIGHT_LAYERS
    if (!IsMatchingLightLayer(light.layerMask, GetMeshRenderingLayer())) return;
    #endif
    float3 L = light.direction;
    half shadow = light.shadowAttenuation;
    half3 radiance = light.color * light.distanceAttenuation;

    // ---- Diffuse: light that entered nearby and comes back out here.
    half3 nl = half3(dot(s.normalR, L), dot(s.normalG, L), dot(s.normalB, L));
    half3 scattered;
    half3 scatteredShadow = shadow;
    UNITY_BRANCH if (s.hasLookup)
    {
        scattered = half3(SampleSkinLookup(nl.r * 0.5 + 0.5, s.curvatureRow.r).r,
            SampleSkinLookup(nl.g * 0.5 + 0.5, s.curvatureRow.g).r,
            SampleSkinLookup(nl.b * 0.5 + 0.5, s.curvatureRow.b).r);
        // Inside a shadow edge red light reaches further into the dark than green and blue.
        UNITY_BRANCH if (s.shadowBlend > 0 && shadow > 0.002 && shadow < 0.998)
        {
            half3 edge = half3(SampleSkinLookup(shadow, s.shadowRow.r).g,
                SampleSkinLookup(shadow, s.shadowRow.g).g, SampleSkinLookup(shadow, s.shadowRow.b).g);
            scatteredShadow = lerp(scatteredShadow, edge, s.shadowBlend);
        }
    }
    else
    {
        // Lookup texture missing: normalized wrap lighting with the same per-channel widths.
        half3 wrap = s.curvatureRow * s.curvatureRow;
        scattered = max(0, nl + wrap) / Sq(1 + wrap);
    }
    sum.diffuse += radiance * lerp(saturate(nl) * shadow, scattered * scatteredShadow, s.scatter);

    // ---- Transmission: light that entered on the far side of a thin part.
    UNITY_BRANCH if (s.transmission > 0)
    {
        // Shadow maps pull casters inward (normal bias), so just past the terminator a ray that
        // runs under the surface finds no occluder and reads as thin. There the chord through the
        // local curvature is the better estimate; it fades out as the surface turns away.
        float backFacing = -dot(s.normalGeometry, L);
        float proxy = SkinProxyThickness(s, L);
        float measured = max(thickness, proxy * saturate(1.0 - backFacing * 1.5));
        float tissue = lerp(proxy, measured, known) + s.thicknessMin;
        // A shadow map measures the body itself, so it already accounts for what blocks the light.
        // Without one, only shadows from elsewhere (baked) are known.
        half blocked = lerp(shadow, 1, known);
        // Light enters the far side at the mirrored angle. No wrap: next to the terminator the
        // shadow map cannot tell a thin part from the edge of a thick one.
        half back = saturate(-dot(s.normalR, L));
        half focus = 0.5 + pow(saturate(dot(-L, s.viewWS)), _TransmissionPower);
        sum.transmission += radiance * SkinTransmittance(tissue, s.inverseDistance) * (back * focus * blocked * s.transmission);
    }

    // ---- Reflection: two GGX lobes, the skin surface and the oil film on top of it.
    float NdotL = saturate(dot(s.normalWS, L));
    UNITY_BRANCH if (NdotL > 0 && shadow > 0)
    {
        float3 H = SafeNormalize(L + s.viewWS);
        float NdotH = saturate(dot(s.normalWS, H));
        float LdotH = saturate(dot(L, H));
        float lobe = lerp(D_GGXNoPI(NdotH, s.alphaBase) * V_SmithJointGGX(NdotL, s.NdotV, s.alphaBase),
            D_GGXNoPI(NdotH, s.alphaOil) * V_SmithJointGGX(NdotL, s.NdotV, s.alphaOil), s.oil);
        float3 fresnel = F_Schlick(float3(s.specularColor), LdotH);
        sum.specular += radiance * shadow * half3(fresnel * (min(lobe, 256.0) * NdotL));
    }
}

// Ref: Karis, "Physically Based Shading on Mobile". Environment reflectance of a GGX lobe.
half3 SkinEnvironmentBRDF(half3 specularColor, half perceptualRoughness, half NdotV)
{
    const half4 c0 = half4(-1, -0.0275, -0.572, 0.022);
    const half4 c1 = half4(1, 0.0425, 1.04, -0.04);
    half4 r = perceptualRoughness * c0 + c1;
    half a004 = min(r.x * r.x, exp2(-9.28 * NdotV)) * r.x + r.y;
    half2 ab = half2(-1.04, 1.04) * a004 + r.zw;
    return specularColor * ab.x + ab.y;
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

    // ---- Scattering distances per color channel, in meters (one Unity unit is one meter).
    half3 channelWidth = max(_SubsurfaceColor.rgb, 0.02);
    half3 relativeWidth = channelWidth / max(channelWidth.r, max(channelWidth.g, channelWidth.b));
    float3 scatterDistance = max(_ScatterRadiusMM, 0.01) * 0.001 * SKIN_RADIUS_TO_DISTANCE * channelWidth;

    // ---- Diffuse normals. Each channel reads the normal map with a footprint as wide as its
    // scattering, measured on the surface, so the softening holds its size as the camera moves.
    float3 dPdx = ddx(v.positionWS), dPdy = ddy(v.positionWS);
    float2 dUVdx = ddx(v.uv), dUVdy = ddy(v.uv);
    float pixelX = max(length(dPdx), 1e-7), pixelY = max(length(dPdy), 1e-7);
    float3 footprint = 4.0 * scatterDistance * (_NormalBlur * s.scatter);
    float3 widenX = clamp(footprint / pixelX, 1.0, 64.0), widenY = clamp(footprint / pixelY, 1.0, 64.0);
    half3 detailKept = _DiffuseNormalStrength * saturate(SKIN_DETAIL_FEATURE_METERS / max(footprint, 1e-6));
    half3 normalRTS = SampleSkinDiffuseNormal(v.uv, dUVdx * widenX.r, dUVdy * widenY.r, s.detailNormalXY * detailKept.r);
    half3 normalGTS = SampleSkinDiffuseNormal(v.uv, dUVdx * widenX.g, dUVdy * widenY.g, s.detailNormalXY * detailKept.g);
    half3 normalBTS = SampleSkinDiffuseNormal(v.uv, dUVdx * widenX.b, dUVdy * widenY.b, s.detailNormalXY * detailKept.b);

    InputData inputData = SkinInput(v, s.surface.normalTS, normalGTS);
    #if defined(_DBUFFER)
    ApplyDecalToSurfaceData(v.positionCS, s.surface, inputData);
    // Metallic is uniform for the material; decal maps cannot override it.
    s.surface.metallic = saturate(_Metallic);
    #endif

    SkinShading shading;
    shading.positionWS = v.positionWS;
    shading.normalWS = inputData.normalWS;
    shading.normalR = normalize(TransformTangentToWorld(normalRTS, inputData.tangentToWorld));
    shading.normalG = normalize(TransformTangentToWorld(normalGTS, inputData.tangentToWorld));
    shading.normalB = normalize(TransformTangentToWorld(normalBTS, inputData.tangentToWorld));
    shading.normalGeometry = normalize(v.normalWS);
    shading.viewWS = inputData.viewDirectionWS;
    shading.NdotV = max(abs(dot(shading.normalWS, shading.viewWS)), 1e-4);

    // World-space derivatives: millimeter radius behaves consistently on scaled avatars.
    float3 geometryNormal = normalize(float3(v.normalWS));
    shading.curvature = _CurvatureScale * (length(ddx(geometryNormal)) + length(ddy(geometryNormal))) / max(pixelX + pixelY, 1e-6);
    shading.curvatureRow = sqrt(saturate(shading.curvature * scatterDistance + _DiffusionWrap * 0.5 * relativeWidth));
    shading.shadowRow = sqrt(saturate(_ShadowScatter * relativeWidth));
    shading.shadowBlend = saturate(_ShadowScatter * 8.0);
    shading.inverseDistance = 1.0 / scatterDistance;
    shading.thicknessMin = _ThicknessMinMM * 0.001;
    shading.hasLookup = SkinLookupIsValid();

    // ---- Surface reflection. Filter the normal distribution, not the albedo: prevents sparkling pores at distance.
    half metallic = s.surface.metallic;
    half oneMinusReflectivity = (1 - ReflectivitySpecular(s.surface.specular)) * (1 - metallic);
    half3 diffuseColor = s.surface.albedo * oneMinusReflectivity;
    shading.specularColor = lerp(s.surface.specular, s.surface.albedo, metallic);
    float variance = max(dot(ddx(inputData.normalWS), ddx(inputData.normalWS)),
        dot(ddy(inputData.normalWS), ddy(inputData.normalWS))) * _SpecularAA;
    half roughnessBase = sqrt(saturate(Sq(1 - s.surface.smoothness) + min(variance, 0.2)));
    half roughnessOil = sqrt(saturate(Sq(1 - _OilSmoothness) + min(variance, 0.2)));
    s.surface.smoothness = 1 - roughnessBase;
    shading.alphaBase = max(Sq(roughnessBase), 0.002);
    shading.alphaOil = max(Sq(roughnessOil), 0.002);
    shading.oil = s.oil;
    shading.scatter = s.scatter;
    shading.transmission = s.transmission * (1 - metallic);

    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(inputData, s.surface);
    Light mainLight = GetMainLight(inputData, shadowMask, ao);
    MixRealtimeAndBakedGI(mainLight, inputData.normalWS, inputData.bakedGI);

    // ---- Ambient. Light bounces inside creases and picks up the skin's color on the way,
    // so occlusion darkens red less than it darkens blue.
    half occlusion = ao.indirectAmbientOcclusion;
    half3 coloredOcclusion = lerp(occlusion.xxx, GTAOMultiBounce(occlusion, s.surface.albedo), _OcclusionColorBleed * s.scatter);
    half3 ambientDiffuse = inputData.bakedGI * coloredOcclusion;

    half3 reflection = reflect(-shading.viewWS, shading.normalWS);
    half horizon = GetHorizonOcclusion(shading.viewWS, shading.normalWS, shading.normalGeometry, 1.3);
    half3 ambientSpecular = GlossyEnvironmentReflection(reflection, inputData.positionWS, roughnessBase, 1.0h, inputData.normalizedScreenSpaceUV)
        * SkinEnvironmentBRDF(shading.specularColor, roughnessBase, shading.NdotV)
        * GetSpecularOcclusionFromAmbientOcclusion(shading.NdotV, occlusion, shading.alphaBase);
    UNITY_BRANCH if (shading.oil > 0)
    {
        half3 oilSpecular = GlossyEnvironmentReflection(reflection, inputData.positionWS, roughnessOil, 1.0h, inputData.normalizedScreenSpaceUV)
            * SkinEnvironmentBRDF(shading.specularColor, roughnessOil, shading.NdotV)
            * GetSpecularOcclusionFromAmbientOcclusion(shading.NdotV, occlusion, shading.alphaOil);
        ambientSpecular = lerp(ambientSpecular, oilSpecular, shading.oil);
    }
    ambientSpecular *= horizon;

    // ---- Lights.
    SkinLightSum sum = (SkinLightSum)0;
    half known;
    float thickness = 0;
    known = 0;
    UNITY_BRANCH if (shading.transmission > 0) thickness = SkinMainLightThickness(shading, known);
    SkinDirect(shading, mainLight, thickness, known, sum);
    half3 mainTransmission = sum.transmission;
    #if defined(_ADDITIONAL_LIGHTS)
    #if USE_CLUSTER_LIGHT_LOOP
    UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        SkinDirect(shading, light, 0, 0, sum);
    }
    #endif
    uint count = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(count)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, ao);
        #if USE_CLUSTER_LIGHT_LOOP
        int visibleLightIndex = lightIndex;
        #else
        int visibleLightIndex = GetPerObjectLightIndex(lightIndex);
        #endif
        half lightKnown = 0;
        float lightThickness = 0;
        UNITY_BRANCH if (shading.transmission > 0)
            lightThickness = SkinAdditionalLightThickness(shading, visibleLightIndex, light.direction, lightKnown);
        SkinDirect(shading, light, lightThickness, lightKnown, sum);
    LIGHT_LOOP_END
    #endif

    half3 result = diffuseColor * (ambientDiffuse + sum.diffuse + sum.transmission) + ambientSpecular + sum.specular;
    #ifdef _ADDITIONAL_LIGHTS_VERTEX
    result += inputData.vertexLighting * diffuseColor;
    #endif
    result = MixFog(result, inputData.fogCoord);
    if (_SkinDebug > 0.5 && _SkinDebug < 1.5) result = s.surface.albedo;
    if (_SkinDebug > 1.5 && _SkinDebug < 2.5) result = inputData.normalWS * 0.5 + 0.5;
    if (_SkinDebug > 2.5 && _SkinDebug < 3.5) result = s.scatter.xxx;
    if (_SkinDebug > 3.5 && _SkinDebug < 4.5) result = s.surface.smoothness.xxx;
    if (_SkinDebug > 4.5 && _SkinDebug < 5.5) result = s.surface.occlusion.xxx;
    if (_SkinDebug > 5.5 && _SkinDebug < 6.5) result = Sq(shading.curvatureRow);
    if (_SkinDebug > 6.5 && _SkinDebug < 7.5) result = mainTransmission;
    if (_SkinDebug > 7.5 && _SkinDebug < 8.5) result = ambientDiffuse + sum.diffuse + sum.transmission;
    if (_SkinDebug > 8.5 && _SkinDebug < 9.5) result = ambientSpecular + sum.specular;
    if (_SkinDebug > 9.5 && _SkinDebug < 10.5) result = shading.normalR * 0.5 + 0.5;
    if (_SkinDebug > 10.5) result = saturate(thickness * 20.0).xxx * known;
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
    half3 normalTS = SampleSkin(v.uv).surface.normalTS;
    InputData d = SkinInput(v, normalTS, normalTS);
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
