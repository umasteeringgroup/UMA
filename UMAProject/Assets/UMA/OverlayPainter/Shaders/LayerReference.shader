Shader "Hidden/UMA/TexturePaint/LayerReference"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _BaseMask;
            float4 _Tint, _Transform;
            float _Rotation;
            int _Component, _Invert, _Mask, _Normal;
            float4 Frag(v2f_img input) : SV_Target
            {
                float s, c; sincos(_Rotation, s, c);
                float2 p = (input.uv - 0.5) * _Transform.xy;
                float2 uv = float2(c*p.x+s*p.y,-s*p.x+c*p.y) + 0.5 + _Transform.zw;
                float4 value = tex2D(_MainTex, uv);
                if (any(uv < 0) || any(uv > 1)) value = 0;
                if (_Normal != 0 && _Component == 0)
                {
                    float3 n = value.rgb*2-1;
                    n.xy *= sign(_Transform.xy);
                    n.xy = float2(c*n.x-s*n.y,s*n.x+c*n.y);
                    value.rgb = normalize(n)*0.5+0.5;
                }
                float scalar = _Component == 1 ? value.a : _Component == 2 ? dot(value.rgb,float3(0.2126,0.7152,0.0722)) :
                    _Component == 3 || _Component == 6 ? value.r : _Component == 4 ? value.g : value.b;
                if (_Invert != 0) scalar = 1-scalar;
                if (_Mask != 0) { scalar *= tex2D(_BaseMask,input.uv).r; return float4(scalar,scalar,scalar,1); }
                if (_Component != 0) return float4(_Tint.rgb,scalar*_Tint.a);
                return value * _Tint;
            }
            ENDCG
        }
    }
}
