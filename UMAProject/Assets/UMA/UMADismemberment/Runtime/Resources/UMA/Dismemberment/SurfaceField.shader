Shader "Hidden/UMA/Dismemberment/SurfaceField"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            float3 _SurfaceGravity;

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.position = float4(input.uv * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                output.position.y = -output.position.y;
                #endif
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            struct FieldOutput
            {
                float4 position : SV_Target0;
                float4 flow : SV_Target1;
            };

            FieldOutput Frag(Varyings input)
            {
                FieldOutput output;
                float3 normal = normalize(input.worldNormal);
                float3 gravity = normalize(_SurfaceGravity);
                float3 tangentGravity = gravity - normal * dot(gravity, normal);
                float3 dx = ddx(input.worldPosition);
                float3 dy = ddy(input.worldPosition);
                float dx2 = max(dot(dx, dx), 1e-10);
                float dy2 = max(dot(dy, dy), 1e-10);
                float2 pixelsPerMeter = float2(dot(tangentGravity, dx) / dx2,
                    dot(tangentGravity, dy) / dy2);
                output.position = float4(input.worldPosition, 1.0);
                output.flow = float4(pixelsPerMeter, sqrt(dx2), 1.0);
                return output;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
