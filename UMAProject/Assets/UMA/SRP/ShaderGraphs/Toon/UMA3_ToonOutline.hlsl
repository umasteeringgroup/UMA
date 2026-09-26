#ifndef UMA_TOON_OUTLINE_INCLUDED
#define UMA_TOON_OUTLINE_INCLUDED
void UMA_ToonOutline_float(float3 PositionOS, float3 NormalOS, float Size, out float3 Position)
{
    float3 positionWS = TransformObjectToWorld(PositionOS);
    float3 normalWS = TransformObjectToWorldNormal(NormalOS);
    Position = TransformWorldToObject(positionWS + normalWS * max(0, Size));
}

void UMA_ToonOutlineSurface_float(float2 UV, UnityTexture2D BaseMap, float4 OutlineColor, out float3 Color, out float Alpha)
{
    Color = OutlineColor.rgb;
    Alpha = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, UV * BaseMap.scaleTranslate.xy + BaseMap.scaleTranslate.zw).a * OutlineColor.a;
}
#endif
