Shader "Hidden/UMA/TexturePaint/RibbonProjection"
{
    Properties
    {
        _DestinationTexture ("Destination", 2D) = "black" {}
        _PaintSource ("Paint Source", 2D) = "white" {}
        _GeometryMask ("Geometry Mask", 2D) = "white" {}
        _PaintColor ("Paint Color", Color) = (1,1,1,1)
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

            struct RibbonSegment
            {
                float4 leftStartAlong;
                float4 rightStartFlow;
                float4 leftEndAlong;
                float4 rightEndFlow;
                float4 normalStartPressure;
                float4 normalEndPressure;
                float4 colorStart;
                float4 colorEnd;
            };

            StructuredBuffer<RibbonSegment> _RibbonSegments;
            int _RibbonSegmentCount;
            sampler2D _DestinationTexture;
            sampler2D _PaintSource;
            sampler2D _GeometryMask;
            float4 _PaintColor;
            float _Strength;
            float _BrushFlow;
            float _ProjectionDepth;
            float _NormalCosLimit;
            int _PaintBackfaces;
            int _PressureAffectsFlow;
            int _PaintSourceKind;
            int _BlendMode;
            int _VectorNormal;
            int _SourceAlongY;
            int _ReverseSourceAxis;
            int _RibbonClosed;

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
                #if UNITY_UV_STARTS_AT_TOP
                    clipPosition.y = -clipPosition.y;
                #endif
                output.position = float4(clipPosition, 0.0, 1.0);
                output.uv = input.uv;
                output.worldPosition = mul(unity_ObjectToWorld, input.vertex).xyz;
                output.worldNormal = UnityObjectToWorldNormal(input.normal);
                return output;
            }

            bool RibbonCoordinates(float3 worldPoint, float3 left0, float3 right0,
                float3 left1, float3 right1, out float across, out float longitudinal,
                out float surfaceDistance, bool allowBeforeStart, bool allowAfterEnd)
            {
                float3 center0 = (left0 + right0) * 0.5;
                float3 center1 = (left1 + right1) * 0.5;
                float3 centerDirection = center1 - center0;
                float centerLengthSquared = dot(centerDirection, centerDirection);
                if (centerLengthSquared <= 1e-10)
                {
                    across = 0;
                    longitudinal = 0;
                    surfaceDistance = 1e20;
                    return false;
                }

                float centerlineLongitudinal =
                    dot(worldPoint - center0, centerDirection) / centerLengthSquared;
                longitudinal = centerlineLongitudinal;
                float initialLongitudinal = saturate(longitudinal);
                float3 initialLeft = lerp(left0, left1, initialLongitudinal);
                float3 initialWidth = lerp(right0 - left0, right1 - left1, initialLongitudinal);
                float initialWidthSquared = dot(initialWidth, initialWidth);
                if (initialWidthSquared <= 1e-10)
                {
                    across = 0;
                    surfaceDistance = 1e20;
                    return false;
                }
                across = dot(worldPoint - initialLeft, initialWidth) / initialWidthSquared;

                // A ribbon quad is generally non-planar on curved geometry. Solve coordinates on
                // its continuous bilinear surface instead of treating it as two planar triangles;
                // the latter creates narrow uncovered wedges along the artificial diagonal.
                [unroll] for (int iteration = 0; iteration < 3; iteration++)
                {
                    float3 cross0 = lerp(left0, right0, across);
                    float3 cross1 = lerp(left1, right1, across);
                    float3 ribbonPosition = lerp(cross0, cross1, longitudinal);
                    float3 derivativeAcross = lerp(right0 - left0, right1 - left1, longitudinal);
                    float3 derivativeAlong = cross1 - cross0;
                    float3 residual = worldPoint - ribbonPosition;
                    float aa = dot(derivativeAcross, derivativeAcross);
                    float ab = dot(derivativeAcross, derivativeAlong);
                    float bb = dot(derivativeAlong, derivativeAlong);
                    float determinant = aa * bb - ab * ab;
                    if (abs(determinant) <= 1e-12) break;
                    float ra = dot(derivativeAcross, residual);
                    float rb = dot(derivativeAlong, residual);
                    across += (bb * ra - ab * rb) / determinant;
                    longitudinal += (aa * rb - ab * ra) / determinant;
                }

                // The generated side edge is tangent to the surface, while the destination bends
                // beneath it. Give that edge a small conservative ownership margin and clamp the
                // lookup back to the source edge. This closes thin inward needles without using
                // ProjectionDepth as a lateral expansion (which would visibly widen the ribbon).
                const float sideOwnershipMargin = 0.02;
                // Interior segments need a modest overlap to close the wedge between adjacent
                // bilinear surfaces. This must remain bounded: ProjectionDepth is commonly longer
                // than several dense path segments, so unlimited endpoint ownership lets nearby
                // interior segments reach beyond the whole spline and smear their edge source row.
                const float jointOwnershipMargin = 0.35;
                float minimumLongitudinal = allowBeforeStart ? -jointOwnershipMargin : -0.001;
                float maximumLongitudinal = allowAfterEnd ? 1.0 + jointOwnershipMargin : 1.001;
                if (across < -sideOwnershipMargin || across > 1.0 + sideOwnershipMargin ||
                    longitudinal < minimumLongitudinal || longitudinal > maximumLongitudinal ||
                    (!allowBeforeStart && centerlineLongitudinal < -0.001) ||
                    (!allowAfterEnd && centerlineLongitudinal > 1.001))
                {
                    surfaceDistance = 1e20;
                    return false;
                }
                across = saturate(across);
                longitudinal = saturate(longitudinal);
                float3 ribbonPosition = lerp(lerp(left0, right0, across),
                    lerp(left1, right1, across), longitudinal);
                surfaceDistance = length(worldPoint - ribbonPosition);
                return true;
            }

            float3 BlendRGB(float3 baseColor, float3 paintColor, int mode)
            {
                if (mode == 1) return baseColor * paintColor;
                if (mode == 2) return baseColor + paintColor;
                if (mode == 3) return baseColor - paintColor;
                if (mode == 4) return 1.0 - (1.0 - baseColor) * (1.0 - paintColor);
                if (mode == 5)
                    return lerp(2.0 * baseColor * paintColor,
                        1.0 - 2.0 * (1.0 - baseColor) * (1.0 - paintColor), step(0.5, baseColor));
                return paintColor;
            }

            float4 CompositeStraightAlpha(float4 destination, float3 paintRGB, float paintAlpha)
            {
                paintAlpha = saturate(paintAlpha);
                float outputAlpha = paintAlpha + destination.a * (1.0 - paintAlpha);
                float3 premultiplied = paintRGB * paintAlpha +
                    destination.rgb * destination.a * (1.0 - paintAlpha);
                float3 outputRGB = outputAlpha > 1e-6 ? premultiplied / outputAlpha : 0;
                return float4(outputRGB, outputAlpha);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 current = tex2D(_DestinationTexture, input.uv);
                float mask = tex2D(_GeometryMask, input.uv).r;
                // Back already contains an exact copy of DestinationTexture. Do not write an
                // unchanged value here: multiple world-space triangles may intentionally share
                // the same (often mirrored) UVs, and a non-contributing triangle drawn later
                // would otherwise erase a ribbon contribution from the other triangle.
                if (mask <= 0.00001) discard;

                float bestDistance = 1e20;
                float bestAcross = 0;
                float bestAlong = 0;
                float bestFlow = 0;
                float bestPressure = 1;
                float4 bestColor = _PaintColor;
                bool found = false;
                float3 surfaceNormal = normalize(input.worldNormal);

                [loop] for (int segmentIndex = 0; segmentIndex < _RibbonSegmentCount; segmentIndex++)
                {
                    RibbonSegment segment = _RibbonSegments[segmentIndex];
                    float3 left0 = segment.leftStartAlong.xyz;
                    float3 right0 = segment.rightStartFlow.xyz;
                    float3 left1 = segment.leftEndAlong.xyz;
                    float3 right1 = segment.rightEndFlow.xyz;
                    float3 boundsMinimum = min(min(left0, right0), min(left1, right1)) - _ProjectionDepth;
                    float3 boundsMaximum = max(max(left0, right0), max(left1, right1)) + _ProjectionDepth;
                    if (any(input.worldPosition < boundsMinimum) ||
                        any(input.worldPosition > boundsMaximum)) continue;
                    // Open ribbons use butt caps. Enforce their endpoint planes before the
                    // bilinear closest-point solve so neither solver clamping nor an overlapping
                    // UV owner can turn the first/last source row into a rounded smear.
                    float3 segmentDirection = (left1 + right1) - (left0 + right0);
                    if (_RibbonClosed == 0 && segmentIndex == 0 &&
                        dot(input.worldPosition - (left0 + right0) * 0.5, segmentDirection) < 0.0)
                        continue;
                    if (_RibbonClosed == 0 && segmentIndex + 1 == _RibbonSegmentCount &&
                        dot(input.worldPosition - (left1 + right1) * 0.5, segmentDirection) > 0.0)
                        continue;
                    float across;
                    float along;
                    float longitudinal;
                    float surfaceDistance;
                    float flow;
                    float pressure;
                    float4 color;
                    float3 ribbonNormal;
                    bool allowBeforeStart = _RibbonClosed != 0 || segmentIndex > 0;
                    bool allowAfterEnd = _RibbonClosed != 0 || segmentIndex + 1 < _RibbonSegmentCount;
                    if (!RibbonCoordinates(input.worldPosition, left0, right0, left1, right1,
                        across, longitudinal, surfaceDistance, allowBeforeStart, allowAfterEnd)) continue;
                    if (surfaceDistance > _ProjectionDepth || surfaceDistance >= bestDistance) continue;
                    along = lerp(segment.leftStartAlong.w, segment.leftEndAlong.w, longitudinal);
                    flow = lerp(segment.rightStartFlow.w, segment.rightEndFlow.w, longitudinal);
                    pressure = lerp(segment.normalStartPressure.w, segment.normalEndPressure.w, longitudinal);
                    color = lerp(segment.colorStart, segment.colorEnd, longitudinal);
                    ribbonNormal = normalize(lerp(segment.normalStartPressure.xyz,
                        segment.normalEndPressure.xyz, longitudinal));
                    float normalAlignment = dot(surfaceNormal, ribbonNormal);
                    if (_PaintBackfaces != 0) normalAlignment = abs(normalAlignment);
                    if (normalAlignment < _NormalCosLimit) continue;
                    found = true;
                    bestDistance = surfaceDistance;
                    bestAcross = saturate(across);
                    bestAlong = along;
                    bestFlow = flow;
                    bestPressure = pressure;
                    bestColor = color;
                }

                if (!found) discard;
                // Keep an unwrapped coordinate for derivatives. Taking frac() before tex2D makes
                // ddx/ddy jump by almost a complete tile at every repeat boundary, selecting a
                // coarse mip for that pixel row and producing a visible zipper/fabric mismatch.
                float unwrappedLongitudinal = _ReverseSourceAxis != 0 ? -bestAlong : bestAlong;
                float2 unwrappedSourceUV = _SourceAlongY != 0
                    ? float2(bestAcross, unwrappedLongitudinal)
                    : float2(unwrappedLongitudinal, bestAcross);
                float2 sourceUV = unwrappedSourceUV;
                if (_SourceAlongY != 0) sourceUV.y = frac(sourceUV.y);
                else sourceUV.x = frac(sourceUV.x);
                float4 desired = _PaintSourceKind == 2
                    ? bestColor
                    : tex2Dgrad(_PaintSource, sourceUV,
                        ddx(unwrappedSourceUV), ddy(unwrappedSourceUV));
                float pressure = _PressureAffectsFlow != 0 ? saturate(bestPressure) : 1.0;
                float weight = saturate(_Strength * _BrushFlow * max(0.0, bestFlow) * pressure * mask);
                float sourceWeight = weight * desired.a;
                if (sourceWeight <= 0.00001) discard;
                float3 blended = BlendRGB(current.rgb, desired.rgb, _BlendMode);
                if (_VectorNormal != 0)
                {
                    float3 a = normalize(current.rgb * 2.0 - 1.0);
                    float3 b = normalize(desired.rgb * 2.0 - 1.0);
                    float3 normal = normalize(lerp(a, b, sourceWeight)) * 0.5 + 0.5;
                    return float4(normal, sourceWeight + current.a * (1.0 - sourceWeight));
                }
                return CompositeStraightAlpha(current, blended, sourceWeight);
            }
            ENDCG
        }
    }
}
