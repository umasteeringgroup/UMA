// HDRP-compatible UMA realistic skin lightmapping support.
#ifndef UMA_REALISTIC_SKIN_META_HDRP
#define UMA_REALISTIC_SKIN_META_HDRP

#include "UMA3SkinInputHDRP.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/MetaPass.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

struct SkinMetaAttributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    float2 dynamicUV : TEXCOORD2;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SkinMetaVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    #ifdef EDITOR_VISUALIZATION
    float2 vizUV : TEXCOORD1;
    float4 lightCoord : TEXCOORD2;
    #endif
};

SkinMetaVaryings SkinMetaVertex(SkinMetaAttributes v)
{
    SkinMetaVaryings o = (SkinMetaVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    UNITY_TRANSFER_INSTANCE_ID(v, o);
    o.positionCS = UnityMetaVertexPosition(v.positionOS.xyz, v.lightmapUV, v.dynamicUV);
    o.uv = v.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
    o.color = v.color;
    #ifdef EDITOR_VISUALIZATION
    UnityEditorVizData(v.positionOS.xyz, v.uv, v.lightmapUV, v.dynamicUV, o.vizUV, o.lightCoord);
    #endif
    return o;
}

float4 SkinMetaFragment(SkinMetaVaryings v) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(v);
    SkinData skin = SampleSkin(v.uv);
    UnityMetaInput meta = (UnityMetaInput)0;
    meta.Albedo = skin.surface.albedo * v.color.rgb * (1 - skin.surface.specular.r);
    #ifdef EDITOR_VISUALIZATION
    meta.VizUV = v.vizUV;
    meta.LightCoord = v.lightCoord;
    #endif
    return UnityMetaFragment(meta);
}
#endif
