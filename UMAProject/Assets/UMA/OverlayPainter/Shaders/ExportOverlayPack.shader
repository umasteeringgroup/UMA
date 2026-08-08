Shader "Hidden/UMA/TexturePaint/ExportOverlayPack"
{
    Properties
    {
        _MainTex ("Main", 2D) = "black" {}
        _Red ("Red", 2D) = "black" {}
        _Green ("Green", 2D) = "black" {}
        _Blue ("Blue", 2D) = "black" {}
        _Alpha ("Alpha", 2D) = "black" {}
        _Coverage ("Coverage", 2D) = "black" {}
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

            sampler2D _Red;
            sampler2D _Green;
            sampler2D _Blue;
            sampler2D _Alpha;
            sampler2D _Coverage;
            float4 _Defaults;
            float4 _HasSource;
            float4 _SourceComponent;
            float4 _Invert;
            int _AlphaFromCoverage;

            float ReadComponent(float4 value, int component)
            {
                return component == 0 ? value.r : component == 1 ? value.g :
                    component == 2 ? value.b : value.a;
            }

            fixed4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                float4 sampled = float4(
                    ReadComponent(tex2D(_Red, uv), (int)_SourceComponent.x),
                    ReadComponent(tex2D(_Green, uv), (int)_SourceComponent.y),
                    ReadComponent(tex2D(_Blue, uv), (int)_SourceComponent.z),
                    ReadComponent(tex2D(_Alpha, uv), (int)_SourceComponent.w));
                float4 result = lerp(_Defaults, sampled, step(0.5, _HasSource));
                result = lerp(result, 1.0 - result, step(0.5, _Invert));
                if (_AlphaFromCoverage != 0) result.a = tex2D(_Coverage, uv).r;
                return result;
            }
            ENDCG
        }

        Pass
        {
            BlendOp Max
            Blend One One

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment fragCoverage
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            fixed4 fragCoverage(v2f_img input) : SV_Target
            {
                float coverage = tex2D(_MainTex, input.uv).a;
                return coverage.xxxx;
            }
            ENDCG
        }
    }
}
