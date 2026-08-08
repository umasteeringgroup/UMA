Shader "Hidden/UMA/TexturePaint/ExportDilate"
{
    Properties
    {
        // Graphics.Blit only guarantees its implicit source binding for a declared
        // _MainTex property. Without this declaration some backends sample Unity's
        // default gray texture instead of the supplied export texture.
        [HideInInspector] _MainTex ("Source", 2D) = "black" {}
        [HideInInspector] _ValidityTex ("Validity", 2D) = "black" {}
        [HideInInspector] _ReplaceMask ("Replace Mask", Vector) = (0,0,0,0)
        [HideInInspector] _NeutralValues ("Neutral Values", Vector) = (0,0,0,0)
        [HideInInspector] _InvertGreen ("Invert Green", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        // Copy the nearest valid RGB into transparent texels while preserving the
        // destination alpha. Validity lives in a separate texture so padding never
        // changes the exported overlay opacity.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _ValidityTex;
            float4 _MainTex_TexelSize;

            fixed4 frag(v2f_img input) : SV_Target
            {
                fixed4 center = tex2D(_MainTex, input.uv);
                if (tex2D(_ValidityTex, input.uv).r > 0.5)
                    return center;

                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        float2 uv = input.uv + float2(x, y) * _MainTex_TexelSize.xy;
                        if (tex2D(_ValidityTex, uv).r <= 0.5) continue;
                        fixed4 neighbor = tex2D(_MainTex, uv);
                        return fixed4(neighbor.rgb, center.a);
                    }
                }
                return center;
            }
            ENDCG
        }

        // Expand the one-channel validity mask by one texel.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            fixed4 frag(v2f_img input) : SV_Target
            {
                if (tex2D(_MainTex, input.uv).r > 0.5) return 1.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        float2 uv = input.uv + float2(x, y) * _MainTex_TexelSize.xy;
                        if (tex2D(_MainTex, uv).r > 0.5) return 1.0;
                    }
                }
                return 0.0;
            }
            ENDCG
        }

        // Initialize validity from the original alpha.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 frag(v2f_img input) : SV_Target
            {
                return tex2D(_MainTex, input.uv).a > 0.0 ? 1.0 : 0.0;
            }
            ENDCG
        }

        // Apply descriptor-declared neutral components and normal convention.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _ReplaceMask;
            float4 _NeutralValues;
            float _InvertGreen;

            float4 frag(v2f_img input) : SV_Target
            {
                float4 value = tex2D(_MainTex, input.uv);
                value = lerp(value, _NeutralValues, _ReplaceMask);
                if (_InvertGreen > 0.5) value.g = 1.0 - value.g;
                return value;
            }
            ENDCG
        }
    }
    Fallback Off
}
