Shader "Hidden/UMA/Dismemberment/SourceMask"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off Blend One One
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _MaskTex;
            float _UseMask;
            float _Intensity;
            float _UseRadialLimit;
            float2 _RadialCenter;
            float _RadialRadius;
            float _RadialFeather;
            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD1;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = float4(input.vertex.xy, 0.0, 1.0);
                output.uv = input.uv;
                #if UNITY_UV_STARTS_AT_TOP
                output.position.y = -output.position.y;
                #endif
                return output;
            }
            float Frag(Varyings input) : SV_Target
            {
                float coverage = _UseMask > 0.5
                    ? tex2D(_MaskTex, saturate(input.uv)).a
                    : 1.0;
                if (_UseRadialLimit > 0.5)
                {
                    float radius = max(0.00001, _RadialRadius);
                    float feather = max(0.00001, _RadialFeather);
                    float radialCoverage = 1.0 - smoothstep(radius,
                        radius + feather, distance(input.uv, _RadialCenter));
                    coverage *= radialCoverage;
                }
                return coverage * _Intensity;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
