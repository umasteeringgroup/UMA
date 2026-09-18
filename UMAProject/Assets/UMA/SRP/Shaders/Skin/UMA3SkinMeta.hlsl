#ifndef UMA_REALISTIC_SKIN_META
#define UMA_REALISTIC_SKIN_META
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
struct SkinMetaAttributes
{
    float4 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    float2 lightmapUV : TEXCOORD1;
    float2 dynamicUV : TEXCOORD2;
    half4 color : COLOR;
};
struct SkinMetaVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    half4 color : COLOR;
    #ifdef EDITOR_VISUALIZATION
    float2 vizUV : TEXCOORD1;
    float4 lightCoord : TEXCOORD2;
    #endif
};
SkinMetaVaryings SkinMetaVertex(SkinMetaAttributes v)
{
    SkinMetaVaryings o = (SkinMetaVaryings)0;
    o.positionCS = UnityMetaVertexPosition(v.positionOS.xyz, v.lightmapUV, v.dynamicUV);
    o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
    o.color = v.color;
    #ifdef EDITOR_VISUALIZATION
    UnityEditorVizData(v.positionOS.xyz, v.uv, v.lightmapUV, v.dynamicUV, o.vizUV, o.lightCoord);
    #endif
    return o;
}
half4 SkinMetaFragment(SkinMetaVaryings v) : SV_Target
{
    SkinData skin = SampleSkin(v.uv);
    MetaInput meta = (MetaInput)0;
    meta.Albedo = skin.surface.albedo * v.color.rgb * (1 - skin.surface.specular.r);
    #ifdef EDITOR_VISUALIZATION
    meta.VizUV = v.vizUV;
    meta.LightCoord = v.lightCoord;
    #endif
    return UnityMetaFragment(meta);
}
#endif
