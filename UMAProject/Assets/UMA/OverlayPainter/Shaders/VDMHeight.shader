Shader "Hidden/UMA/TexturePaint/VDMHeight"
{
    Properties { [HideInInspector] _MainTex ("Source", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Mask;
            float3 _Neutral, _ComponentScale, _NormalAxis;
            float _HeightScale;
            int _FlipY, _CoverageMode;
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv;
                if (_FlipY != 0) uv.y = 1.0 - uv.y;
                float4 raw = tex2D(_MainTex, uv);
                float3 displacement = (raw.rgb - _Neutral) * _ComponentScale;
                float height = 0.5 + dot(displacement, _NormalAxis) * _HeightScale;
                float coverage = _CoverageMode == 1 ? raw.a : _CoverageMode == 2 ? tex2D(_Mask, uv).r : 1.0;
                // Straight height; coverage is applied once by the placement/compositor.
                return float4(height.xxx, saturate(coverage));
            }
            ENDCG
        }
    }
}
