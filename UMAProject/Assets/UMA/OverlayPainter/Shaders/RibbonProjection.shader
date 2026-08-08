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
            sampler2D _BeginningSource;
            sampler2D _EndSource;
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
            int _EdgeFadeEnabled;
            int _RibbonPaintEnabled;
            float _EdgeFadeStart;
            float _EdgeFadeSize;
            int _HasBeginningSource;
            int _HasEndSource;
            float _RibbonMinimumAlong;
            float _RibbonMaximumAlong;
            int _OuterRibbonEffectsEnabled;

            int _StrokeEnabled;
            float4 _StrokeColor;
            float _StrokeWidth;
            float _StrokeOffset;
            float _StrokeSmoothness;
            float _StrokeLevel;

            int _InnerShadowEnabled;
            int _InnerShadowSide;
            float4 _InnerShadowColor;
            float _InnerShadowWidth;
            float _InnerShadowOffset;
            float _InnerShadowLevel;
            sampler2D _InnerShadowCurve;
            int _OuterShadowEnabled;
            int _OuterShadowSide;
            float4 _OuterShadowColor;
            float _OuterShadowWidth;
            float _OuterShadowOffset;
            float _OuterShadowLevel;
            sampler2D _OuterShadowCurve;
            int _InnerGlowEnabled;
            int _InnerGlowSide;
            float4 _InnerGlowColor;
            float _InnerGlowWidth;
            float _InnerGlowOffset;
            float _InnerGlowLevel;
            sampler2D _InnerGlowCurve;
            int _OuterGlowEnabled;
            int _OuterGlowSide;
            float4 _OuterGlowColor;
            float _OuterGlowWidth;
            float _OuterGlowOffset;
            float _OuterGlowLevel;
            sampler2D _OuterGlowCurve;

            int _BevelEnabled;
            int _BevelSide;
            float4 _BevelLightColor;
            float4 _BevelDarkColor;
            float _BevelWidth;
            float _BevelSmoothness;
            float _BevelLevel;
            int _BevelLeftTone;
            int _BevelRightTone;
            float _BevelLeftOffset;
            float _BevelRightOffset;

            int _StitchEnabled;
            int _StitchSide;
            float4 _StitchColor;
            int _StitchRows;
            float _StitchThreadSize;
            float _StitchLength;
            float _StitchInset;
            float _StitchLevel;

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
                // Outer ribbon effects need intrinsic coordinates just beyond the long edges. A
                // full ribbon-width margin is intentionally conservative; final pixel coverage is
                // still limited by each effect's width and never extends past the open caps.
                float lateralOwnershipMargin = _OuterRibbonEffectsEnabled != 0
                    ? 1.0 : sideOwnershipMargin;
                // Interior segments need a modest overlap to close the wedge between adjacent
                // bilinear surfaces. This must remain bounded: ProjectionDepth is commonly longer
                // than several dense path segments, so unlimited endpoint ownership lets nearby
                // interior segments reach beyond the whole spline and smear their edge source row.
                const float jointOwnershipMargin = 0.35;
                float minimumLongitudinal = allowBeforeStart ? -jointOwnershipMargin : -0.001;
                float maximumLongitudinal = allowAfterEnd ? 1.0 + jointOwnershipMargin : 1.001;
                if (across < -lateralOwnershipMargin || across > 1.0 + lateralOwnershipMargin ||
                    longitudinal < minimumLongitudinal || longitudinal > maximumLongitudinal ||
                    (!allowBeforeStart && centerlineLongitudinal < -0.001) ||
                    (!allowAfterEnd && centerlineLongitudinal > 1.001))
                {
                    surfaceDistance = 1e20;
                    return false;
                }
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

            float RibbonSideFade(float across)
            {
                if (_EdgeFadeEnabled == 0) return 1.0;
                // across is intrinsic to the generated world-space ribbon: 0/1 are its two side
                // edges and 0.5 is its centerline. It is intentionally unrelated to either the
                // source texture orientation or the destination mesh UV orientation.
                float centerDistance = abs(saturate(across) * 2.0 - 1.0);
                float fadeStart = saturate(_EdgeFadeStart);
                if (centerDistance < fadeStart) return 1.0;
                float fadeSize = saturate(_EdgeFadeSize);
                if (fadeSize <= 0.00001) return 0.0;
                float fadeEnd = lerp(fadeStart, 1.0, fadeSize);
                if (fadeEnd <= fadeStart + 0.00001) return 0.0;
                return 1.0 - smoothstep(fadeStart, fadeEnd, centerDistance);
            }

            bool IncludesLeft(int side)
            {
                return side == 0 || side == 2;
            }

            bool IncludesRight(int side)
            {
                return side == 1 || side == 2;
            }

            float SampleRibbonCurve(sampler2D curveTexture, float normalizedDistance)
            {
                return saturate(tex2Dlod(curveTexture,
                    float4(saturate(normalizedDistance), 0.5, 0.0, 0.0)).r);
            }

            float RibbonDistanceCoverage(float across, float acrossPerPixel, int side,
                float width, float offset, bool inner, sampler2D curveTexture)
            {
                float coverage = 0.0;
                width = max(0.5, width);
                // The original ribbon vertex stream stores its historical "left" vertex at
                // across=0 even though that point is visually on the traveler's right when the
                // surface normal faces the viewer. Preserve that ordering (and therefore source
                // texture orientation), but expose user-facing Left/Right relative to travel
                // from spline beginning to end: left is across=1, right is across=0.
                if (IncludesLeft(side))
                {
                    float signedPixels = (1.0 - across) / acrossPerPixel;
                    if ((inner && signedPixels >= 0.0) || (!inner && signedPixels < 0.0))
                    {
                        float distancePixels = (inner ? signedPixels : -signedPixels) - offset;
                        if (distancePixels >= 0.0 && distancePixels <= width)
                            coverage = max(coverage,
                                SampleRibbonCurve(curveTexture, distancePixels / width));
                    }
                }
                if (IncludesRight(side))
                {
                    float signedPixels = across / acrossPerPixel;
                    if ((inner && signedPixels >= 0.0) || (!inner && signedPixels < 0.0))
                    {
                        float distancePixels = (inner ? signedPixels : -signedPixels) - offset;
                        if (distancePixels >= 0.0 && distancePixels <= width)
                            coverage = max(coverage,
                                SampleRibbonCurve(curveTexture, distancePixels / width));
                    }
                }
                return coverage;
            }

            // Strokes follow the generated ribbon's long edges, rather than the alpha bounds of
            // the projected layer. This is important when an outer glow or shadow is present:
            // those effects deliberately extend the layer alpha beyond the ribbon itself.
            float RibbonStrokeCoverage(float across, float acrossPerPixel, float width,
                float offset, float smoothness)
            {
                width = max(0.5, width);
                float featherStart = width * (1.0 - saturate(smoothness));
                float coverage = 0.0;

                float leftDistance = (across - 1.0) / acrossPerPixel - offset;
                if (leftDistance >= 0.0 && leftDistance <= width)
                {
                    coverage = smoothness <= 0.0001 ? 1.0 :
                        1.0 - smoothstep(featherStart, width, leftDistance);
                }
                float rightDistance = -across / acrossPerPixel - offset;
                if (rightDistance >= 0.0 && rightDistance <= width)
                {
                    coverage = max(coverage, smoothness <= 0.0001 ? 1.0 :
                        1.0 - smoothstep(featherStart, width, rightDistance));
                }
                return coverage;
            }

            float BevelCoverage(float signedPixels, float width, float offset, float smoothness)
            {
                float distancePixels = signedPixels - offset;
                if (distancePixels < 0.0 || distancePixels > width) return 0.0;
                float featherStart = width * (1.0 - saturate(smoothness));
                return 1.0 - smoothstep(featherStart, width, distancePixels);
            }

            float StitchRow(float across, float center, float threadWidth)
            {
                float halfWidth = max(0.0005, threadWidth * 0.5);
                float antialias = max(fwidth(across), 0.0001);
                return 1.0 - smoothstep(halfWidth - antialias,
                    halfWidth + antialias, abs(across - center));
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
                float3 bestAcrossVector = float3(1.0, 0.0, 0.0);
                bool found = false;
                float3 surfaceNormal = normalize(input.worldNormal);

                [loop] for (int segmentIndex = 0; segmentIndex < _RibbonSegmentCount; segmentIndex++)
                {
                    RibbonSegment segment = _RibbonSegments[segmentIndex];
                    float3 left0 = segment.leftStartAlong.xyz;
                    float3 right0 = segment.rightStartFlow.xyz;
                    float3 left1 = segment.leftEndAlong.xyz;
                    float3 right1 = segment.rightEndFlow.xyz;
                    float outerReach = _OuterRibbonEffectsEnabled != 0
                        ? max(length(right0 - left0), length(right1 - left1)) : 0.0;
                    float3 boundsMinimum = min(min(left0, right0), min(left1, right1)) -
                        (_ProjectionDepth + outerReach);
                    float3 boundsMaximum = max(max(left0, right0), max(left1, right1)) +
                        (_ProjectionDepth + outerReach);
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
                    bestAcross = across;
                    bestAlong = along;
                    bestFlow = flow;
                    bestPressure = pressure;
                    bestColor = color;
                    bestAcrossVector = lerp(right0 - left0, right1 - left1, longitudinal);
                }

                if (!found) discard;
                // Keep an unwrapped coordinate for derivatives. Taking frac() before tex2D makes
                // ddx/ddy jump by almost a complete tile at every repeat boundary, selecting a
                // coarse mip for that pixel row and producing a visible zipper/fabric mismatch.
                float sourceAcross = saturate(bestAcross);
                float unwrappedLongitudinal = _ReverseSourceAxis != 0 ? -bestAlong : bestAlong;
                float2 unwrappedSourceUV = _SourceAlongY != 0
                    ? float2(sourceAcross, unwrappedLongitudinal)
                    : float2(unwrappedLongitudinal, sourceAcross);
                float2 sourceUV = unwrappedSourceUV;
                if (_SourceAlongY != 0) sourceUV.y = frac(sourceUV.y);
                else sourceUV.x = frac(sourceUV.x);
                float4 desired;
                float localAlong = bestAlong - _RibbonMinimumAlong;
                float alongSpan = max(0.0001, _RibbonMaximumAlong - _RibbonMinimumAlong);
                bool useBeginning = _HasBeginningSource != 0 && localAlong < 1.0 - 0.0001;
                bool useEnd = _HasEndSource != 0 && localAlong >= alongSpan - 1.0 - 0.0001;
                if (useEnd)
                    desired = tex2Dgrad(_EndSource, sourceUV,
                        ddx(unwrappedSourceUV), ddy(unwrappedSourceUV));
                else if (useBeginning)
                    desired = tex2Dgrad(_BeginningSource, sourceUV,
                        ddx(unwrappedSourceUV), ddy(unwrappedSourceUV));
                else if (_PaintSourceKind == 2) desired = bestColor;
                else desired = tex2Dgrad(_PaintSource, sourceUV,
                    ddx(unwrappedSourceUV), ddy(unwrappedSourceUV));

                float pressure = _PressureAffectsFlow != 0 ? saturate(bestPressure) : 1.0;
                float commonWeight = saturate(_Strength * _BrushFlow * max(0.0, bestFlow) * pressure * mask);
                float shapeAlpha = saturate(desired.a);
                // Derivatives of bestAcross are undefined at the boundary where two ribbon
                // segments (or overlapping UV owners) exchange closest ownership. The resulting
                // spike made pixels a full ribbon width away look only a few texels from the edge,
                // leaving a jagged outer-effect contour. Derive the local texel scale from the
                // smoothly interpolated destination surface instead; do not differentiate the
                // discontinuous winning-segment coordinate itself.
                float inverseAcrossLengthSquared = rcp(max(dot(bestAcrossVector,
                    bestAcrossVector), 1e-10));
                float acrossDx = dot(ddx(input.worldPosition), bestAcrossVector) *
                    inverseAcrossLengthSquared;
                float acrossDy = dot(ddy(input.worldPosition), bestAcrossVector) *
                    inverseAcrossLengthSquared;
                float acrossPerPixel = max(length(float2(acrossDx, acrossDy)), 0.00001);
                bool insideRibbon = bestAcross >= 0.0 && bestAcross <= 1.0;
                float4 result = current;
                bool contributed = false;

                float coverage = RibbonDistanceCoverage(bestAcross, acrossPerPixel,
                    _OuterShadowSide, _OuterShadowWidth, _OuterShadowOffset, false,
                    _OuterShadowCurve);
                float alpha = coverage * _OuterShadowColor.a * _OuterShadowLevel * commonWeight * shapeAlpha;
                if (_OuterShadowEnabled != 0 && alpha > 0.00001)
                {
                    result = CompositeStraightAlpha(result, _OuterShadowColor.rgb, alpha);
                    contributed = true;
                }
                coverage = RibbonDistanceCoverage(bestAcross, acrossPerPixel,
                    _OuterGlowSide, _OuterGlowWidth, _OuterGlowOffset, false,
                    _OuterGlowCurve);
                alpha = coverage * _OuterGlowColor.a * _OuterGlowLevel * commonWeight * shapeAlpha;
                if (_OuterGlowEnabled != 0 && alpha > 0.00001)
                {
                    result = CompositeStraightAlpha(result, _OuterGlowColor.rgb, alpha);
                    contributed = true;
                }

                coverage = RibbonStrokeCoverage(bestAcross, acrossPerPixel,
                    _StrokeWidth, _StrokeOffset, _StrokeSmoothness);
                alpha = coverage * _StrokeColor.a * _StrokeLevel * commonWeight * shapeAlpha;
                if (_StrokeEnabled != 0 && alpha > 0.00001)
                {
                    result = CompositeStraightAlpha(result, _StrokeColor.rgb, alpha);
                    contributed = true;
                }

                if (insideRibbon)
                {
                    float sideFade = RibbonSideFade(bestAcross);
                    float sourceWeight = commonWeight * sideFade * shapeAlpha;
                    if (_RibbonPaintEnabled != 0 && sourceWeight > 0.00001)
                    {
                        if (_VectorNormal != 0)
                        {
                            float3 a = normalize(result.rgb * 2.0 - 1.0);
                            float3 b = normalize(desired.rgb * 2.0 - 1.0);
                            result.rgb = normalize(lerp(a, b, sourceWeight)) * 0.5 + 0.5;
                            result.a = sourceWeight + result.a * (1.0 - sourceWeight);
                        }
                        else
                        {
                            float3 blended = BlendRGB(result.rgb, desired.rgb, _BlendMode);
                            result = CompositeStraightAlpha(result, blended, sourceWeight);
                        }
                        contributed = true;
                    }

                    coverage = RibbonDistanceCoverage(bestAcross, acrossPerPixel,
                        _InnerShadowSide, _InnerShadowWidth, _InnerShadowOffset, true,
                        _InnerShadowCurve);
                    alpha = coverage * _InnerShadowColor.a * _InnerShadowLevel * commonWeight * shapeAlpha;
                    if (_InnerShadowEnabled != 0 && alpha > 0.00001)
                    {
                        result = CompositeStraightAlpha(result, _InnerShadowColor.rgb, alpha);
                        contributed = true;
                    }
                    coverage = RibbonDistanceCoverage(bestAcross, acrossPerPixel,
                        _InnerGlowSide, _InnerGlowWidth, _InnerGlowOffset, true,
                        _InnerGlowCurve);
                    alpha = coverage * _InnerGlowColor.a * _InnerGlowLevel * commonWeight * shapeAlpha;
                    if (_InnerGlowEnabled != 0 && alpha > 0.00001)
                    {
                        result = CompositeStraightAlpha(result, _InnerGlowColor.rgb, alpha);
                        contributed = true;
                    }

                    if (_BevelEnabled != 0)
                    {
                        if (IncludesLeft(_BevelSide))
                        {
                            coverage = BevelCoverage((1.0 - bestAcross) / acrossPerPixel,
                                _BevelWidth, _BevelLeftOffset, _BevelSmoothness);
                            float4 bevelColor = _BevelLeftTone == 0 ? _BevelLightColor : _BevelDarkColor;
                            alpha = coverage * bevelColor.a * _BevelLevel * commonWeight * shapeAlpha;
                            if (alpha > 0.00001)
                            {
                                float3 tone = _BevelLeftTone == 0
                                    ? BlendRGB(result.rgb, bevelColor.rgb, 4)
                                    : BlendRGB(result.rgb, bevelColor.rgb, 1);
                                result = CompositeStraightAlpha(result, tone, alpha);
                                contributed = true;
                            }
                        }
                        if (IncludesRight(_BevelSide))
                        {
                            coverage = BevelCoverage(bestAcross / acrossPerPixel,
                                _BevelWidth, _BevelRightOffset, _BevelSmoothness);
                            float4 bevelColor = _BevelRightTone == 0 ? _BevelLightColor : _BevelDarkColor;
                            alpha = coverage * bevelColor.a * _BevelLevel * commonWeight * shapeAlpha;
                            if (alpha > 0.00001)
                            {
                                float3 tone = _BevelRightTone == 0
                                    ? BlendRGB(result.rgb, bevelColor.rgb, 4)
                                    : BlendRGB(result.rgb, bevelColor.rgb, 1);
                                result = CompositeStraightAlpha(result, tone, alpha);
                                contributed = true;
                            }
                        }
                    }

                    if (_StitchEnabled != 0)
                    {
                        float stitchLength = max(0.01, _StitchLength);
                        float phase = frac(localAlong / (stitchLength * 2.0));
                        float phaseAA = max(fwidth(localAlong / (stitchLength * 2.0)), 0.0001);
                        float dash = 1.0 - smoothstep(0.5 - phaseAA, 0.5 + phaseAA, phase);
                        float rowCoverage = 0.0;
                        float rowStep = _StitchThreadSize * 2.5;
                        if (IncludesLeft(_StitchSide))
                        {
                            rowCoverage = max(rowCoverage, StitchRow(bestAcross,
                                1.0 - _StitchInset, _StitchThreadSize));
                            if (_StitchRows > 1) rowCoverage = max(rowCoverage, StitchRow(bestAcross,
                                1.0 - _StitchInset - rowStep, _StitchThreadSize));
                        }
                        if (IncludesRight(_StitchSide))
                        {
                            rowCoverage = max(rowCoverage, StitchRow(bestAcross,
                                _StitchInset, _StitchThreadSize));
                            if (_StitchRows > 1) rowCoverage = max(rowCoverage, StitchRow(bestAcross,
                                _StitchInset + rowStep, _StitchThreadSize));
                        }
                        alpha = dash * rowCoverage * _StitchColor.a * _StitchLevel *
                            commonWeight * shapeAlpha;
                        if (alpha > 0.00001)
                        {
                            result = CompositeStraightAlpha(result, _StitchColor.rgb, alpha);
                            contributed = true;
                        }
                    }
                }

                if (!contributed) discard;
                return result;
            }
            ENDCG
        }
    }
}
