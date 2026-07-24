Shader "Hidden/UMA/AlbedoDerivedNormal"
{
    Properties
    {
        _ReferenceAlbedo ("Reference Albedo", 2D) = "white" {}
        _ReferenceNormal ("Reference Normal", 2D) = "bump" {}
        _ModifiedAlbedo ("Modified Albedo", 2D) = "white" {}
        _EffectMask ("Effect Mask", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _ReferenceAlbedo;
            sampler2D _ReferenceNormal;
            sampler2D _ModifiedAlbedo;
            sampler2D _EffectMask;
            float4 _OutputTexelSize;
            float _NormalDecodeMode;
            float _HeightSource;
            float _MaskChannel;
            float _HasMask;
            float _InvertMask;
            float _InvertHeight;
            float _Bumpiness;
            float _DifferenceGain;
            float _DifferenceThreshold;
            float _SmoothingRadius;
            float _OutputMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            float GetHeightSource(float3 color)
            {
                // HeightSource: Luminance=0, RedOnly=1, GreenAndBlue=2.
                if (_HeightSource > 1.5)
                {
                    return (color.g + color.b) * 0.5;
                }
                if (_HeightSource > 0.5)
                {
                    return color.r;
                }
                return dot(color, float3(0.2126, 0.7152, 0.0722));
            }

            float SampleDifference(float2 uv)
            {
                float referenceHeight = GetHeightSource(tex2D(_ReferenceAlbedo, uv).rgb);
                float modifiedHeight = GetHeightSource(tex2D(_ModifiedAlbedo, uv).rgb);
                float difference = (modifiedHeight - referenceHeight) * _DifferenceGain;
                float magnitude = max(0.0, abs(difference) - _DifferenceThreshold);
                difference = sign(difference) * magnitude;
                return _InvertHeight > 0.5 ? -difference : difference;
            }

            float SampleSmoothedDifference(float2 uv)
            {
                if (_SmoothingRadius < 0.01)
                {
                    return SampleDifference(uv);
                }

                float2 horizontal = float2(_OutputTexelSize.x * _SmoothingRadius, 0.0);
                float2 vertical = float2(0.0, _OutputTexelSize.y * _SmoothingRadius);
                return (
                    SampleDifference(uv) * 4.0
                    + SampleDifference(uv - horizontal)
                    + SampleDifference(uv + horizontal)
                    + SampleDifference(uv - vertical)
                    + SampleDifference(uv + vertical)
                ) * 0.125;
            }

            float3 DecodeReferenceNormal(float4 packedNormal)
            {
                // NormalMapDecodeMode: Auto=0, RawRgb=1, UnityNormal=2, Dxt5nm=3.
                if (_NormalDecodeMode > 2.5)
                {
                    float2 xy = packedNormal.ag * 2.0 - 1.0;
                    return normalize(float3(xy, sqrt(saturate(1.0 - dot(xy, xy)))));
                }
                if (_NormalDecodeMode > 1.5)
                {
                    return normalize(UnpackNormal(packedNormal));
                }

                float3 normal = packedNormal.rgb * 2.0 - 1.0;
                return dot(normal, normal) > 0.0001 ? normalize(normal) : float3(0.0, 0.0, 1.0);
            }

            float ResolveMask(float4 maskSample)
            {
                if (_HasMask < 0.5)
                {
                    return 1.0;
                }

                float value;
                if (_MaskChannel < 0.5)
                {
                    value = maskSample.a;
                }
                else if (_MaskChannel < 1.5)
                {
                    value = dot(maskSample.rgb, float3(0.2126, 0.7152, 0.0722));
                }
                else if (_MaskChannel < 2.5)
                {
                    value = maskSample.r;
                }
                else if (_MaskChannel < 3.5)
                {
                    value = maskSample.g;
                }
                else
                {
                    value = maskSample.b;
                }

                return _InvertMask > 0.5 ? 1.0 - value : value;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                if (_OutputMode > 1.5)
                {
                    float maskPreview = saturate(ResolveMask(tex2D(_EffectMask, input.uv)));
                    return fixed4(maskPreview, maskPreview, maskPreview, 1.0);
                }
                if (_OutputMode > 0.5)
                {
                    float heightPreview = saturate(0.5 + SampleSmoothedDifference(input.uv));
                    return fixed4(heightPreview, heightPreview, heightPreview, 1.0);
                }

                float2 xOffset = float2(_OutputTexelSize.x, 0.0);
                float2 yOffset = float2(0.0, _OutputTexelSize.y);
                float heightLeft = SampleSmoothedDifference(input.uv - xOffset);
                float heightRight = SampleSmoothedDifference(input.uv + xOffset);
                float heightDown = SampleSmoothedDifference(input.uv - yOffset);
                float heightUp = SampleSmoothedDifference(input.uv + yOffset);
                float slopeX = (heightRight - heightLeft) * 0.5 * _Bumpiness;
                float slopeY = (heightUp - heightDown) * 0.5 * _Bumpiness;

                float3 baseNormal = DecodeReferenceNormal(tex2D(_ReferenceNormal, input.uv));
                float3 detailNormal = normalize(float3(-slopeX, -slopeY, 1.0));

                // Whiteout-style blending retains the reference face normal while
                // adding the slope derived from the edited albedo.
                float3 combinedNormal = normalize(float3(
                    baseNormal.xy + detailNormal.xy,
                    baseNormal.z * detailNormal.z));

                float mask = saturate(ResolveMask(tex2D(_EffectMask, input.uv)));
                float3 outputNormal = normalize(lerp(baseNormal, combinedNormal, mask));
                return fixed4(outputNormal * 0.5 + 0.5, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
