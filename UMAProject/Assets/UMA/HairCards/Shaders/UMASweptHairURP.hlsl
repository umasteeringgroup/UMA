#ifndef UMA_SWEPT_HAIR_INCLUDED
#define UMA_SWEPT_HAIR_INCLUDED
#define _ALPHATEST_ON 1
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
TEXTURE2D(_DepthMap); SAMPLER(sampler_DepthMap);
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor, _RootColor, _TipColor, _SpecularColor, _SecondaryColor, _TransmissionColor;
    half _RootFade, _ColorPower, _TextureColor, _DepthInfluence, _StrandVariation, _ClumpVariation;
    half _SpecularStrength, _Smoothness, _SpecularShift, _SecondaryStrength, _SecondarySmoothness, _SecondaryShift;
    half _Transmission, _DiffuseWrap, _AmbientStrength, _BumpScale, _Cutoff, _ShadowCutoff;
    half _AlphaToCoverage, _VertexColorInfluence, _VertexAlphaInfluence, _DebugView;
    half _OcclusionStrength, _UseDepthMap;
CBUFFER_END

struct HairAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 strand : TEXCOORD1; // normalized root->tip, stable strand random
    float2 clump : TEXCOORD2;  // stable parent-clump random, inspection mask
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct HairVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    half3 strandWS : TEXCOORD2;
    float2 uv : TEXCOORD3;
    float4 data : TEXCOORD4;
    half4 color : COLOR;
    half fog : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
HairVaryings HairVertex(HairAttributes input)
{
    HairVaryings output = (HairVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs frame = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    output.positionCS = position.positionCS;
    output.positionWS = position.positionWS;
    output.normalWS = frame.normalWS;
    output.strandWS = frame.tangentWS;
    output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
    output.data = float4(input.strand, input.clump);
    output.color = input.color;
    output.fog = ComputeFogFactor(position.positionCS.z);
    return output;
}

half HairAlpha(HairVaryings input, half atlasAlpha)
{
    return saturate(atlasAlpha * _BaseColor.a * lerp(1.0h, input.color.a, _VertexAlphaInfluence));
}
half HairClip(half alpha)
{
    #if defined(SHADER_STAGE_FRAGMENT)
    if (_AlphaToCoverage > 0.5h) return AlphaClip(alpha, _Cutoff);
    clip(alpha - _Cutoff);
    #endif
    return alpha;
}
half3 HairNormal(HairVaryings input, half faceSign)
{
    half3 n = normalize(input.normalWS) * faceSign;
    half3 t = SafeNormalize(input.strandWS - n * dot(n, input.strandWS));
    half3 side = SafeNormalize(cross(n, t));
    half3 map = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
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
half HairLobe(half3 tangent, half3 normal, half3 halfVector, half shift, half smoothness)
{
    half3 shifted = SafeNormalize(tangent + normal * shift);
    float dotTH = dot(shifted, halfVector);
    float sinTH = sqrt(saturate(1.0 - dotTH * dotTH));
    float exponent = exp2(lerp(3.0, 10.0, smoothness));
    return (half)(pow(max(sinTH, 0.0001), exponent) * smoothstep(-1.0, 0.0, dotTH));
}
half3 HairLight(Light light, half3 n, half3 t, half3 v, half3 albedo, half depth)
{
    half3 l = light.direction;
    half attenuation = light.distanceAttenuation * light.shadowAttenuation;
    half3 h = SafeNormalize(l + v);
    half wrap = saturate((dot(n,l) + _DiffuseWrap) / (1.0h + _DiffuseWrap));
    half fiberDiffuse = sqrt(saturate(1.0h - dot(t,l) * dot(t,l)));
    half diffuse = lerp(wrap, fiberDiffuse * (0.35h + 0.65h * wrap), 0.6h);
    half shiftNoise = (depth - 0.5h) * 0.08h;
    half primary = HairLobe(t,n,h,_SpecularShift + shiftNoise,_Smoothness);
    half secondary = HairLobe(t,n,h,_SecondaryShift + shiftNoise,_SecondarySmoothness);
    half3 specular = (_SpecularColor.rgb * primary * _SpecularStrength +
                     _SecondaryColor.rgb * secondary * _SecondaryStrength) * (0.25h + 0.75h * wrap);
    half scatter = pow(saturate(dot(-l,v)), 5.0h) * _Transmission * saturate(1.0h - wrap * 0.6h);
    return (albedo * diffuse + specular + albedo * _TransmissionColor.rgb * scatter) * light.color * attenuation;
}
half3 HairIdColor(half value)
{
    return 0.25h + 0.75h * saturate(abs(frac(value + half3(0,0.333333h,0.666667h)) * 6.0h - 3.0h) - 1.0h);
}
half4 HairFragment(HairVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half4 atlas = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
    half alpha = HairClip(HairAlpha(input, atlas.a));
    half3 n = HairNormal(input, IS_FRONT_VFACE(facing, 1.0h, -1.0h));
    half3 t = SafeNormalize(input.strandWS - n * dot(n,input.strandWS));
    half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half along = pow(saturate(input.data.x / max(_RootFade, 0.001h)), _ColorPower);
    half depth = atlas.r;
    if (_UseDepthMap > 0.5h) depth = SAMPLE_TEXTURE2D(_DepthMap, sampler_DepthMap, input.uv).r;
    half3 albedo = lerp(_RootColor.rgb, _TipColor.rgb, along) * _BaseColor.rgb;
    albedo *= lerp(1.0h.xxx, atlas.rgb, _TextureColor);
    albedo *= lerp(1.0h, lerp(0.08h,1.0h,depth), _DepthInfluence);
    albedo *= lerp(1.0h, SAMPLE_TEXTURE2D(_OcclusionMap,sampler_OcclusionMap,input.uv).r, _OcclusionStrength);
    albedo *= max(0.05h, 1.0h + (input.data.y * 2.0h - 1.0h) * _StrandVariation + (input.data.z * 2.0h - 1.0h) * _ClumpVariation);
    albedo *= lerp(1.0h.xxx,input.color.rgb,_VertexColorInfluence);
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = n;
    inputData.viewDirectionWS = v;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    half3 color = max(0.0h.xxx, SampleSH(n)) * albedo * _AmbientStrength;
    Light mainLight = GetMainLight(inputData.shadowCoord, input.positionWS, half4(1,1,1,1));
    color += HairLight(mainLight,n,t,v,albedo,depth);
    #if defined(_ADDITIONAL_LIGHTS)
        #if USE_CLUSTER_LIGHT_LOOP
            UNITY_LOOP for (uint lightIndex = 0u; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                color += HairLight(light,n,t,v,albedo,depth);
            }
        #endif
        uint pixelLightCount = GetAdditionalLightsCount();
        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
            color += HairLight(light,n,t,v,albedo,depth);
        LIGHT_LOOP_END
    #endif
    #if defined(_SCREEN_SPACE_OCCLUSION)
        AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(inputData.normalizedScreenSpaceUV);
        color *= ao.indirectAmbientOcclusion;
    #endif
    if (_DebugView > 0.5h)
    {
        if (_DebugView < 1.5h) color = lerp(half3(0.1h,0.2h,1),half3(1,0.2h,0.1h),input.data.x);
        else if (_DebugView < 2.5h) color = HairIdColor(input.data.z);
        else if (_DebugView < 3.5h) color = HairIdColor(input.data.y);
        else if (_DebugView < 4.5h) color = input.data.www;
        else color = n * 0.5h + 0.5h;
    }
    return half4(MixFog(color,input.fog), alpha);
}

float3 _LightDirection, _LightPosition;
HairVaryings HairShadowVertex(HairAttributes input)
{
    HairVaryings output = HairVertex(input);
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - output.positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    // Flip the normal toward the light on backfaces to avoid two-sided normal-bias acne.
    float3 normal = normalize(output.normalWS);
    normal *= dot(normal, lightDirectionWS) < 0 ? -1 : 1;
    output.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(output.positionWS, normal, lightDirectionWS)));
    return output;
}
half4 HairShadowFragment(HairVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    clip(HairAlpha(input, SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).a) - _ShadowCutoff);
    return 0;
}
half4 HairDepthFragment(HairVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    half alpha = HairClip(HairAlpha(input, SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).a));
    return half4(input.positionCS.zzz, alpha);
}
half4 HairDepthNormalsFragment(HairVaryings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    half alpha = HairClip(HairAlpha(input, SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).a));
    float3 n = HairNormal(input, IS_FRONT_VFACE(facing,1.0h,-1.0h));
    #if defined(_GBUFFER_NORMALS_OCT)
        float2 oct = PackNormalOctQuadEncode(n) * 0.5 + 0.5;
        return half4(PackFloat2To888(saturate(oct)),alpha);
    #else
        return half4(n,alpha);
    #endif
}
#endif
