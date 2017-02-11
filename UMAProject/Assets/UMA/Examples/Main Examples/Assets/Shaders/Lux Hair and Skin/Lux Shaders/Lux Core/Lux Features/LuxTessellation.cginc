// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'
// Upgrade NOTE: replaced '_World2Object' with 'unity_WorldToObject'

#ifndef LUX_TESSELLATION_INCLUDED
#define LUX_TESSELLATION_INCLUDE

//  Additional Inputs ------------------------------------------------------------------

#ifndef LUX_STANDARD_CORE_INCLUDED
    // when using tessellation
    #if defined (TESSELLATION_ON)
        float _Tess;
        float _Phong;
        float _MinDist;
        float _MaxDist;
        half _EdgeLength;
        half2 _ParallaxToBaseRatio;
    #endif 
#endif

//	Lux Tesellation functions and helper -----------------------------------------------

float4 LuxTessEdge (appdata v0, appdata v1, appdata v2)
{
    float3 wpos0 = mul(unity_ObjectToWorld, v0.vertex).xyz;
    float3 wpos1 = mul(unity_ObjectToWorld, v1.vertex).xyz;
    float3 wpos2 = mul(unity_ObjectToWorld, v2.vertex).xyz;

    // distance to edge center
    float3 dist = float3 ( distance(0.5 * (wpos1+wpos2), _WorldSpaceCameraPos), distance(0.5 * (wpos0+wpos2), _WorldSpaceCameraPos), distance(0.5 * (wpos0+wpos1), _WorldSpaceCameraPos));
    float4 tess;
    tess.xyz = clamp(1.0 - (dist - _MinDist) / (_MaxDist - _MinDist), 0.01, 1.0);
    // length of the edges
    float3 len = float3(distance(wpos1, wpos2), distance(wpos2, wpos0), distance(wpos0, wpos1));
    // _EdgeLength is approximate desired size in pixels
    float3 f = max(len * _ScreenParams.y / (_EdgeLength * dist), 1.0);
    tess.xyz *= f;
    tess.w = (tess.x + tess.y + tess.z) / 3.0f;
    return tess;
}

void LuxTessellationDisplace (inout appdata v) 
{
    // Please note: We push down the vertices instead of raising them! which makes it a) better fit POM and b) ensures that vertices never leaves the collider
    // float d = 1.0 - tex2Dlod(_ParallaxMap, float4( v.texcoord.xy * _ParallaxToBaseRatio,0,0)).g;
    // Lets push the vertices up and down:
    float d = tex2Dlod(_ParallaxMap, float4( v.texcoord.xy * _ParallaxToBaseRatio,0,0)).g * 2.0 - 1.0;

    // Unfortunately we have to calculate distance based attenuation again
    float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
    float dist = distance (wpos, _WorldSpaceCameraPos);
    float f = saturate(1.0 - (dist - _MinDist) / (_MaxDist - _MinDist));
    v.vertex.xyz += v.normal * d * _Parallax * f;

    // Calc Tangent Space Rotation
    float3 binormal = cross( v.normal, v.tangent.xyz ) * v.tangent.w;
    float3x3 rotation = float3x3( v.tangent.xyz, binormal, v.normal.xyz );
    // Store FlowDirection
    v.color.rg = normalize( mul(rotation, mul(unity_WorldToObject, float4(0,1,0,0)).xyz)).xy;
}

void LuxTessellationDisplaceMixMapped (inout appdata v) 
{
    // mix mapping
    half4 heightMask = tex2Dlod(_ParallaxMap, float4( v.texcoord.xy * _ParallaxToBaseRatio,0,0));
    half2 mixmapValue = half2(heightMask.b, 1.0 - heightMask.b);
    half2 heights = heightMask.ga;
    heights = saturate(heights + 0.001);
    // blend according to height and mixmapValue
    mixmapValue *= float2( dot(heights.x, mixmapValue.x), dot(heights.y, mixmapValue.y));
    // sharpen mask
    mixmapValue *= mixmapValue;
    mixmapValue *= mixmapValue;
    mixmapValue = mixmapValue / dot(mixmapValue, 1);

    // Nice try.. but normal slope damp does not work properly at this stage as v.normal is the original normal
    // half snowAmount = ComputeSnowAccumulationVertex(v.texcoord * _ParallaxToBaseRatio, v.normal);

    // Please note: We push down the vertices instead of raising them! which makes it a) better fit POM and b) ensures that vertices never leaves the collider
    // float d = 1.0 - (heightMask.g * mixmapValue.x + heightMask.a * mixmapValue.y);
    
    // Lets push the vertices up and down:
    float d = (heightMask.g * mixmapValue.x + heightMask.a * mixmapValue.y) * 2.0 - 1.0;
    //d = lerp(d, d * 0.25, snowAmount);
    //d = lerp(d, d+0.25, snowAmount);
    //v.vertex.xyz -= v.normal * d * _Parallax; // * (UnityCalcDistanceTessFactor(v.vertex, _MinDist, _MaxDist, _Tess) / _Tess );
    
    // Unfortunately we have to calculate distance based attenuation again
    float3 wpos = mul(unity_ObjectToWorld, v.vertex).xyz;
    float dist = distance (wpos, _WorldSpaceCameraPos);
    float f = saturate(1.0 - (dist - _MinDist) / (_MaxDist - _MinDist));
    v.vertex.xyz += v.normal * d * _Parallax * f;

    // Calc Tangent Space Rotation
    float3 binormal = cross( v.normal, v.tangent.xyz ) * v.tangent.w;
    float3x3 rotation = float3x3( v.tangent.xyz, binormal, v.normal.xyz );
    // Store FlowDirection in vertex color as we can't declare any other inputs...
    v.color.gb = normalize( mul(rotation, mul(unity_WorldToObject, float4(0,1,0,0)).xyz)).xy;
}



#endif