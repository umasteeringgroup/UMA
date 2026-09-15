Shader "UMA/Hair Cards/Scalp Vertex Preview URP"
{
    Properties { [MainColor] _BaseColor("Surface Tint", Color) = (0.6,0.42,0.32,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; half4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; half3 n:TEXCOORD1; half4 c:COLOR; };
            V Vert(A i) { V o; o.world=TransformObjectToWorld(i.p.xyz); o.p=TransformWorldToHClip(o.world); o.n=TransformObjectToWorldNormal(i.n); o.c=i.c; return o; }
            half4 Frag(V i):SV_Target
            {
                half3 n=normalize(i.n); Light l=GetMainLight(TransformWorldToShadowCoord(i.world));
                half3 diffuse=SampleSH(n)+l.color*l.distanceAttenuation*l.shadowAttenuation*saturate(dot(n,l.direction));
                return half4(_BaseColor.rgb*i.c.rgb*diffuse,1);
            }
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
