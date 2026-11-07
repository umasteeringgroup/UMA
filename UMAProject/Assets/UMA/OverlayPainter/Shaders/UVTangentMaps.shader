Shader "Hidden/UMA/TexturePaint/UVTangentMaps"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "UnityCG.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalOS : TEXCOORD0;
                float4 tangentOS : TEXCOORD1;
            };

            struct Output
            {
                half4 normal : SV_Target0;
                half4 tangent : SV_Target1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.uv * 2.0 - 1.0, 0.0, 1.0);
                output.normalOS = input.normalOS;
                output.tangentOS = input.tangentOS;
                return output;
            }

            Output Frag(Varyings input)
            {
                Output output;
                float3 normal = normalize(input.normalOS);
                float3 tangent = normalize(input.tangentOS.xyz);
                output.normal = half4(normal * 0.5 + 0.5, 1.0);
                output.tangent = half4(tangent * 0.5 + 0.5, input.tangentOS.w >= 0.0 ? 1.0 : 0.0);
                return output;
            }
            ENDHLSL
        }
    }
}
