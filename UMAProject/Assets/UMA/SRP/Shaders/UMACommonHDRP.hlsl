// Shared raster-pass contract for UMA's custom HDRP forward shaders.
#ifndef UMA_COMMON_HDRP_INCLUDED
#define UMA_COMMON_HDRP_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/AmbientProbe.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Decal/DecalPrepassBuffer.hlsl"

struct UmaDepthOutput
{
#if defined(WRITE_MSAA_DEPTH)
    float4 depth : SV_Target0;
    float4 normal : SV_Target1;
    #if defined(WRITE_DECAL_BUFFER) || defined(WRITE_RENDERING_LAYER)
    float4 layers : SV_Target2;
    #endif
#else
    float4 normal : SV_Target0;
    #if defined(WRITE_DECAL_BUFFER) || defined(WRITE_RENDERING_LAYER)
    float4 layers : SV_Target1;
    #endif
#endif
};

UmaDepthOutput UmaDepth(float4 positionCS, float3 normalWS, float3 geomNormalWS, float roughness, float alpha)
{
    UmaDepthOutput o = (UmaDepthOutput)0;
    NormalData n;
    n.normalWS = normalWS;
    n.perceptualRoughness = roughness;
    EncodeIntoNormalBuffer(n, o.normal);
#if defined(WRITE_MSAA_DEPTH)
    o.depth = float4(positionCS.zzz, alpha);
#endif
#if defined(WRITE_DECAL_BUFFER) || defined(WRITE_RENDERING_LAYER)
    DecalPrepassData d;
    d.geomNormalWS = geomNormalWS;
    d.renderingLayerMask = GetMeshRenderingLayerMask();
    EncodeIntoDecalPrepassBuffer(d, o.layers);
#endif
    return o;
}

void UmaMotionPositions(float3 positionOS, float3 previousPositionOS, out float4 currentCS, out float4 previousCS)
{
    currentCS = mul(UNITY_MATRIX_UNJITTERED_VP, float4(TransformObjectToWorld(positionOS), 1));
    float3 previousOS = unity_MotionVectorsParams.x > 0 ? previousPositionOS : positionOS;
    previousCS = mul(UNITY_MATRIX_PREV_VP, float4(TransformPreviousObjectToWorld(previousOS), 1));
}

// Motion-vector MRT order differs from the depth prepass: normals come last.
struct UmaMotionOutput
{
#if defined(WRITE_MSAA_DEPTH)
    float4 depth : SV_Target0;
    float4 motion : SV_Target1;
    #if defined(WRITE_DECAL_BUFFER)
    float4 layers : SV_Target2;
    float4 normal : SV_Target3;
    #else
    float4 normal : SV_Target2;
    #endif
#else
    float4 motion : SV_Target0;
    #if defined(WRITE_DECAL_BUFFER)
    float4 layers : SV_Target1;
    float4 normal : SV_Target2;
    #else
    float4 normal : SV_Target1;
    #endif
#endif
};

UmaMotionOutput UmaMotion(float4 positionCS, float4 currentCS, float4 previousCS,
    float3 normalWS, float3 geomNormalWS, float roughness, float alpha)
{
    UmaMotionOutput o = (UmaMotionOutput)0;
    float2 velocity = clamp(currentCS.xy / currentCS.w - previousCS.xy / previousCS.w,
        -2.0 + 0.01 * _ScreenSize.zw, 2.0 - 0.01 * _ScreenSize.zw) * 0.5;
#if UNITY_UV_STARTS_AT_TOP
    velocity.y = -velocity.y;
#endif
    o.motion = unity_MotionVectorsParams.y == 0 ? float4(2, 0, 0, 0) : float4(velocity, 0, 0);
    NormalData n;
    n.normalWS = normalWS;
    n.perceptualRoughness = roughness;
    EncodeIntoNormalBuffer(n, o.normal);
#if defined(WRITE_MSAA_DEPTH)
    o.depth = float4(positionCS.zzz, alpha);
#endif
#if defined(WRITE_DECAL_BUFFER)
    DecalPrepassData d;
    d.geomNormalWS = geomNormalWS;
    d.renderingLayerMask = GetMeshRenderingLayerMask();
    EncodeIntoDecalPrepassBuffer(d, o.layers);
#endif
    return o;
}
#endif
