Shader "Hidden/UMA/HairCards/GuideOccluderDepth"
{
    Properties
    {
        _MainTex ("Alpha texture", 2D) = "white" {}
        _AlphaClip ("Alpha clip", Float) = 0
        _Cutoff ("Alpha cutoff", Float) = 0.5
        _BaseAlpha ("Base alpha", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Off
            // Keep handles sitting on the scalp visible without revealing back-side guides.
            Offset 1, 1
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _AlphaClip, _Cutoff, _BaseAlpha;
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(float4 position : POSITION, float2 uv : TEXCOORD0)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(position);
                output.uv = TRANSFORM_TEX(uv, _MainTex);
                return output;
            }
            fixed4 Frag(Varyings input) : SV_Target
            {
                if (_AlphaClip > 0.5) clip(tex2D(_MainTex, input.uv).a * _BaseAlpha - _Cutoff);
                return 0;
            }
            ENDCG
        }
    }
    Fallback Off
}
