Shader "Hidden/UMA/TexturePaint/FillGenerator"
{
    Properties
    {
        _FillSource ("Source", 2D) = "white" {}
        _FillColor ("Color", Color) = (1,1,1,1)
        [HideInInspector] _MainTex ("Dilation Source", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _FillSource;
            float4 _FillColor;
            float2 _Tiling;
            float2 _Offset;
            float _Rotation;
            float _BlendOffset;
            float _BlendSharpness;
            int _SourceKind;
            int _Projection;
            int _TriplanarBlend;

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float2 clipPosition = input.uv * 2.0 - 1.0;
                // Direct UV-space rendering does not pass through Unity's camera projection,
                // so render-target APIs with a top-left origin need the clip-space Y correction
                // applied explicitly. Sampling continues to use the mesh UV unchanged.
                #if UNITY_UV_STARTS_AT_TOP
                    clipPosition.y = -clipPosition.y;
                #endif
                output.position = float4(clipPosition, 0.0, 1.0);
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                output.uv = input.uv;
                return output;
            }

            float4 SampleTiled(float2 uv)
            {
                return tex2D(_FillSource, frac(uv));
            }

            float2 TransformFillUV(float2 uv, float2 center)
            {
                float angle = radians(_Rotation);
                float sine = sin(angle);
                float cosine = cos(angle);
                float2 centered = uv - center;
                float2 rotated = float2(centered.x * cosine - centered.y * sine,
                    centered.x * sine + centered.y * cosine);
                return rotated * _Tiling + center + _Offset;
            }

            float3 TriplanarWeights(float3 normal)
            {
                float3 weights = abs(normalize(normal));
                if (_TriplanarBlend == 0)
                {
                    if (weights.x >= weights.y && weights.x >= weights.z) return float3(1, 0, 0);
                    if (weights.y >= weights.z) return float3(0, 1, 0);
                    return float3(0, 0, 1);
                }
                weights = saturate((weights - _BlendOffset) / max(1.0 - _BlendOffset, 1e-4));
                weights = pow(max(weights, 1e-5), _BlendSharpness);
                return weights / max(weights.x + weights.y + weights.z, 1e-5);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                if (_SourceKind != 0) return _FillColor;
                if (_Projection == 0)
                    return SampleTiled(TransformFillUV(input.uv, float2(0.5, 0.5)));

                float3 normal = normalize(input.worldNormal);
                float3 weights = TriplanarWeights(normal);
                float2 uvX = TransformFillUV(input.worldPosition.zy, float2(0.0, 0.0));
                float2 uvY = TransformFillUV(input.worldPosition.xz, float2(0.0, 0.0));
                float2 uvZ = TransformFillUV(input.worldPosition.xy, float2(0.0, 0.0));
                uvX.x *= normal.x < 0.0 ? -1.0 : 1.0;
                uvY.x *= normal.y < 0.0 ? -1.0 : 1.0;
                uvZ.x *= normal.z >= 0.0 ? -1.0 : 1.0;
                return SampleTiled(uvX) * weights.x +
                    SampleTiled(uvY) * weights.y +
                    SampleTiled(uvZ) * weights.z;
            }
            ENDCG
        }

        // Extend generated fill pixels into a small UV gutter. Unlike export padding, this pass
        // copies alpha as well as RGB because the layer compositor uses alpha as fill coverage.
        // The C# generator repeats the pass with ping-pong targets to create the required width.
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment FragDilate
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;

            float4 FragDilate(v2f_img input) : SV_Target
            {
                float4 center = tex2D(_MainTex, input.uv);
                if (center.a > 1e-5) return center;

                float4 nearest = center;
                float strongestAlpha = 0.0;
                [unroll]
                for (int y = -1; y <= 1; y++)
                {
                    [unroll]
                    for (int x = -1; x <= 1; x++)
                    {
                        if (x == 0 && y == 0) continue;
                        float2 uv = input.uv + float2(x, y) * _MainTex_TexelSize.xy;
                        float4 candidate = tex2D(_MainTex, uv);
                        if (candidate.a <= strongestAlpha) continue;
                        strongestAlpha = candidate.a;
                        nearest = candidate;
                    }
                }
                return nearest;
            }
            ENDCG
        }
    }
}
