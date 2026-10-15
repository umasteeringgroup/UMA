Shader "Hidden/UMA/TexturePaint/SpriteMaterialPreviewPack"
{
    Properties
    {
        _Metallic("Metallic", 2D) = "black" {}
        _Roughness("Roughness", 2D) = "gray" {}
        _Normal("Normal", 2D) = "bump" {}
        _Height("Height", 2D) = "gray" {}
        _Albedo("Albedo", 2D) = "white" {}
        _Coverage("Coverage", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Metallic, _Roughness, _Normal, _Height;
            sampler2D _Albedo, _Coverage;
            float4 _Height_TexelSize;
            float _HasNormal, _HasHeight, _Mode;
            float H(float2 uv) { float4 h = tex2D(_Height, uv); return (h.r - .5) * h.a; }
            float4 frag(v2f_img i) : SV_Target
            {
                if (_Mode > 1.5) return float4(tex2D(_Albedo, i.uv).rgb,
                    tex2D(_Coverage, i.uv).a);
                if (_Mode < .5) return float4(tex2D(_Metallic, i.uv).r, 0, 0, 1 - tex2D(_Roughness, i.uv).r);
                float3 n = _HasNormal > .5 ? tex2D(_Normal, i.uv).rgb * 2 - 1 : float3(0,0,1);
                if (_HasHeight > .5)
                {
                    float2 d = _Height_TexelSize.xy;
                    float2 slope = float2(H(i.uv + float2(d.x,0)) - H(i.uv - float2(d.x,0)),
                                          H(i.uv + float2(0,d.y)) - H(i.uv - float2(0,d.y))) * 4;
                    n = normalize(float3(n.xy - slope, n.z));
                }
                n = normalize(n) * .5 + .5;
                #if defined(UNITY_NO_DXT5nm)
                    return float4(n,1);
                #else
                    return float4(1,n.y,1,n.x);
                #endif
            }
            ENDCG
        }
    }
}
