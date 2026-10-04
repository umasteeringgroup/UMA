Shader "Hidden/UMA/TexturePaint/RibbonProjection"
{
    Properties
    {
        _DestinationTexture ("Destination", 2D) = "black" {}
        _PaintSource ("Paint Source", 2D) = "white" {}
        _GeometryMask ("Geometry Mask", 2D) = "white" {}
        _PaintColor ("Paint Color", Color) = (1,1,1,1)
        [HideInInspector] _MainTex ("UV Gutter Source", 2D) = "black" {}
        [HideInInspector] _StrokeEnabled ("Stroke Enabled", Int) = 0
        [HideInInspector] _StrokeColor ("Stroke Color", Color) = (0,0,0,1)
        [HideInInspector] _StrokeParameters ("Stroke Width Offset Smoothness Level", Vector) = (2,0,0.25,1)
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
            #include "HemSeam.hlsl"
            #include "Garment.hlsl"

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
            sampler2D _RibbonCoverage;
            float _RibbonCoverageAlpha;
            int _UseRibbonCoverage;
            sampler2D _BeginningSource;
            sampler2D _EndSource;
            sampler2D _GeometryMask;
            sampler2D _RegionMask;
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
            int _TextureFlipX, _TextureFlipY, _TextureFlipSeed;
            float _JoinOverlap;
            int _SourceAlongY;
            int _ReverseSourceAxis;
            int _RibbonClosed;
            int _EdgeFadeEnabled;
            int _RibbonPaintEnabled;
            float _EdgeFadeStart;
            float _EdgeFadeSize;
            float _StartFade, _EndFade;
            // Generated curves all use bilinear/clamp sampling. Sharing their sampler keeps
            // the ribbon (including garment inputs and albedo coverage) within the GPU limit.
            Texture2D _EdgeFadeCurve, _StartFadeCurve, _EndFadeCurve;
            SamplerState sampler_EdgeFadeCurve;
            int _HasBeginningSource;
            int _HasEndSource;
            float _RibbonMinimumAlong;
            float _RibbonMaximumAlong;
            int _OuterRibbonEffectsEnabled;

            int _StrokeEnabled;
            float4 _StrokeColor;
            float4 _StrokeParameters;

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
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPosition : TEXCOORD1;
                float3 worldNormal : TEXCOORD2;
                float4 worldTangent : TEXCOORD3;
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
                output.worldTangent = float4(mul((float3x3)unity_ObjectToWorld, input.tangent.xyz), input.tangent.w * unity_WorldTransformParams.w);
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
                // Negative starts extend the falloff past the centerline, allowing its opacity
                // to decrease too. Existing 0..100% distances retain their original coverage.
                float fadeStart = clamp(_EdgeFadeStart, -1.0, 1.0);
                float fadeEnd = lerp(fadeStart, 1.0, saturate(_EdgeFadeSize));
                float position;
                if (fadeEnd <= fadeStart + 0.00001)
                {
                    if (centerDistance >= fadeStart) return 0.0;
                    position = 0.0;
                }
                else position = saturate((centerDistance - fadeStart) / (fadeEnd - fadeStart));
                return saturate(_EdgeFadeCurve.SampleLevel(sampler_EdgeFadeCurve,
                    float2((position * 255.0 + 0.5) / 256.0, 0.5), 0).r);
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

            // Matches TexturePaintPathMirroring.ShouldFlip; no frame/global random state.
            bool PathTextureFlip(int mode, uint index, uint salt)
            {
                if (mode == 1) return true;
                if (mode == 2) return (index & 1u) != 0;
                if (mode != 3) return false;
                uint hash = index ^ (uint)_TextureFlipSeed ^ salt;
                hash ^= hash >> 16; hash *= 0x7feb352du;
                hash ^= hash >> 15; hash *= 0x846ca68bu; hash ^= hash >> 16;
                return (hash & 1u) != 0;
            }

            float4 SampleOverlappingRibbonTile(float along, int tile, int tileCount, float span,
                float halfOverlap, float across, float4 color, float2 sourceDx, float2 sourceDy)
            {
                // Virtual neighbors at -1/tileCount allow the last and first images to overlap
                // across a loop's closing join while keeping their original seed indices.
                int index = _RibbonClosed != 0 ? (tile % tileCount + tileCount) % tileCount : tile;
                float before = _RibbonClosed != 0 || index > 0 ? halfOverlap : 0.0;
                float after = _RibbonClosed != 0 || index + 1 < tileCount ? halfOverlap : 0.0;
                float start = tile - before;
                float end = (_RibbonClosed != 0 ? tile + 1.0 : min(tile + 1.0, span)) + after;
                float inverseLength = rcp(max(end - start, 0.00001));
                float longitudinal = saturate((along - start) * inverseLength);
                if (_ReverseSourceAxis != 0) longitudinal = 1.0 - longitudinal;
                float2 uv = _SourceAlongY != 0 ? float2(across, longitudinal) : float2(longitudinal, across);
                // Gradients come from the continuous, unflipped coordinate, never from the
                // changing tile identity or the overlap selection branch.
                float2 stretch = _SourceAlongY != 0 ? float2(1, inverseLength) : float2(inverseLength, 1);
                float2 flip = float2(PathTextureFlip(_TextureFlipX, (uint)index, 0x02e5be93u) ? 1.0 : 0.0,
                    PathTextureFlip(_TextureFlipY, (uint)index, 0x68bc21ebu) ? 1.0 : 0.0);
                float2 flipSign = 1.0 - 2.0 * flip;
                uv = uv * flipSign + flip;
                sourceDx *= stretch * flipSign; sourceDy *= stretch * flipSign;
                bool beginning = _HasBeginningSource != 0 && index == 0;
                bool ending = _HasEndSource != 0 && index + 1 == tileCount;
                float4 value;
                if (ending) value = tex2Dgrad(_EndSource, uv, sourceDx, sourceDy);
                else if (beginning) value = tex2Dgrad(_BeginningSource, uv, sourceDx, sourceDy);
                else if (_PaintSourceKind == 2) value = color;
                else value = tex2Dgrad(_PaintSource, uv, sourceDx, sourceDy);
                // Replace channel alpha before premultiplied tile blending so transparent
                // albedo pixels cannot contribute hidden RGB from normal or control maps.
                if (_UseRibbonCoverage != 0 && !beginning && !ending)
                    value.a = tex2Dgrad(_RibbonCoverage, uv, sourceDx, sourceDy).a * _RibbonCoverageAlpha;
                if (_VectorNormal != 0 && (_PaintSourceKind != 2 || beginning || ending))
                    value.xy = (value.xy * 2.0 - 1.0) * flipSign * 0.5 + 0.5;
                return value;
            }

            float4 CrossfadeRibbonTiles(float along, float span, float across, float4 color,
                float2 sourceDx, float2 sourceDy)
            {
                // The spline fits an integer number of tiles. A division roundoff at the
                // final segment must not invent a tiny extra tile and move the End source.
                int count = max(1, (int)floor(span + 0.5));
                float fittedScale = count / span;
                along *= fittedScale;
                float2 gradientScale = _SourceAlongY != 0 ? float2(1, fittedScale) : float2(fittedScale, 1);
                sourceDx *= gradientScale; sourceDy *= gradientScale;
                span = count;
                if (_RibbonClosed != 0) along -= floor(along / span) * span;
                int tile = (int)clamp(floor(along), 0.0, count - 1.0);
                float phase = along - tile;
                float halfOverlap = _JoinOverlap * 0.5;
                int left = tile, right = tile;
                float join = tile;
                if (phase < halfOverlap && (_RibbonClosed != 0 || tile > 0)) left = tile - 1;
                else if (phase > 1.0 - halfOverlap && (_RibbonClosed != 0 || tile + 1 < count))
                { right = tile + 1; join = tile + 1.0; }
                float4 a = SampleOverlappingRibbonTile(along, left, count, span, halfOverlap, across, color, sourceDx, sourceDy);
                if (left == right) return a;
                float4 b = SampleOverlappingRibbonTile(along, right, count, span, halfOverlap, across, color, sourceDx, sourceDy);
                float incoming = smoothstep(join - halfOverlap, join + halfOverlap, along);
                // Complementary premultiplied weights prevent an opacity dip for opaque tiles
                // and exclude hidden RGB in transparent pixels from the color/normal blend.
                float aWeight = saturate(a.a) * (1.0 - incoming), bWeight = saturate(b.a) * incoming;
                float alpha = aWeight + bWeight;
                float3 rgb = alpha > 0.000001 ? (a.rgb * aWeight + b.rgb * bWeight) / alpha : float3(0,0,0);
                if (_VectorNormal != 0 && alpha > 0.000001)
                {
                    float3 normal = rgb * 2.0 - 1.0;
                    normal = dot(normal, normal) > 0.000001 ? normalize(normal) : float3(0,0,1);
                    rgb = normal * 0.5 + 0.5;
                }
                return float4(rgb, alpha);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 current = tex2D(_DestinationTexture, input.uv);
                float mask = (tex2D(_GeometryMask, input.uv).r * tex2D(_RegionMask, input.uv).r);
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
                    // Symmetry copies can be interleaved or concatenated. Their intrinsic along
                    // coordinates retain each path's own endpoints; global buffer indices do not.
                    bool atStart = segment.leftStartAlong.w <= _RibbonMinimumAlong + 0.00001;
                    bool atEnd = segment.leftEndAlong.w >= _RibbonMaximumAlong - 0.00001;
                    if (_RibbonClosed == 0 && atStart &&
                        dot(input.worldPosition - (left0 + right0) * 0.5, segmentDirection) < 0.0)
                        continue;
                    if (_RibbonClosed == 0 && atEnd &&
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
                    bool allowBeforeStart = _RibbonClosed != 0 || !atStart;
                    bool allowAfterEnd = _RibbonClosed != 0 || !atEnd;
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
                if (_GarmentEnabled != 0)
                {
                    float width=max(length(bestAcrossVector),.0001);
                    float pixelWidth=max(length(float2(dot(ddx(input.worldPosition),bestAcrossVector),
                        dot(ddy(input.worldPosition),bestAcrossVector)))/(width*width),.0001);
                    desired=ShadeGarment(float2(bestAcross,localAlong/alongSpan),input.uv,pixelWidth,
                        _RibbonClosed!=0,input.worldPosition,input.worldNormal,input.worldTangent);
                }
                else if (_HemEnabled != 0)
                {
                    float width=max(length(bestAcrossVector),.0001);
                    float perPixel=max(length(float2(dot(ddx(input.worldPosition),bestAcrossVector),
                        dot(ddy(input.worldPosition),bestAcrossVector)))/(width*width),.0001);
                    desired=ShadeHem(bestAcross,localAlong,alongSpan,_RibbonClosed!=0,perPixel,
                        input.worldPosition,input.worldNormal,input.worldTangent,input.uv,width);
                }
                else if (_JoinOverlap > 0.00001)
                    desired = CrossfadeRibbonTiles(localAlong, alongSpan, sourceAcross, bestColor,
                        ddx(unwrappedSourceUV), ddy(unwrappedSourceUV));
                else
                {
                    // Decide from the complete image tile, not from tessellated ribbon segments.
                    uint tileIndex = (uint)clamp(floor(localAlong), 0.0, max(0.0, ceil(alongSpan) - 1.0));
                    float2 flip = float2(PathTextureFlip(_TextureFlipX, tileIndex, 0x02e5be93u) ? 1.0 : 0.0,
                        PathTextureFlip(_TextureFlipY, tileIndex, 0x68bc21ebu) ? 1.0 : 0.0);
                    float2 flipSign = 1.0 - 2.0 * flip;
                    // Differentiate before mirroring: a flip can change abruptly at a tile join.
                    float2 sourceDx = ddx(unwrappedSourceUV) * flipSign;
                    float2 sourceDy = ddy(unwrappedSourceUV) * flipSign;
                    sourceUV = sourceUV * flipSign + flip;
                    bool useBeginning = _HasBeginningSource != 0 && localAlong < 1.0 - 0.0001;
                    bool useEnd = _HasEndSource != 0 && localAlong >= alongSpan - 1.0 - 0.0001;
                    if (useEnd)
                        desired = tex2Dgrad(_EndSource, sourceUV,
                            sourceDx, sourceDy);
                    else if (useBeginning)
                        desired = tex2Dgrad(_BeginningSource, sourceUV,
                            sourceDx, sourceDy);
                    else if (_PaintSourceKind == 2) desired = bestColor;
                    else desired = tex2Dgrad(_PaintSource, sourceUV,
                        sourceDx, sourceDy);
                    if (_UseRibbonCoverage != 0 && !useBeginning && !useEnd)
                        desired.a = tex2Dgrad(_RibbonCoverage, sourceUV, sourceDx, sourceDy).a * _RibbonCoverageAlpha;

                    if (_VectorNormal != 0 && (_PaintSourceKind != 2 || useBeginning || useEnd))
                        desired.xy = (desired.xy * 2.0 - 1.0) * flipSign * 0.5 + 0.5;
                }

                float pressure = _PressureAffectsFlow != 0 ? saturate(bestPressure) : 1.0;
                float commonWeight = saturate(_Strength * _BrushFlow * max(0.0, bestFlow) * pressure * mask);
                if (_RibbonClosed == 0)
                {
                    float alongFraction = saturate(localAlong / alongSpan);
                    if (_StartFade > 0)
                    {
                        float position = 1.0 - saturate(alongFraction / _StartFade);
                        commonWeight *= saturate(_StartFadeCurve.SampleLevel(sampler_EdgeFadeCurve,
                            float2((position * 255.0 + 0.5) / 256.0, 0.5), 0).r);
                    }
                    if (_EndFade > 0)
                    {
                        float position = 1.0 - saturate((1.0 - alongFraction) / _EndFade);
                        commonWeight *= saturate(_EndFadeCurve.SampleLevel(sampler_EdgeFadeCurve,
                            float2((position * 255.0 + 0.5) / 256.0, 0.5), 0).r);
                    }
                }
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
                            // Generated partial-coverage normals are straight, not blended with
                            // the invalid RGB of a transparent raster before layer compositing.
                            result.rgb = normalize(b * sourceWeight +
                                a * result.a * (1.0 - sourceWeight)) * 0.5 + 0.5;
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

                // Stroke is foreground decoration. An inward (negative) offset deliberately
                // moves some or all of it over the authored ribbon, so it must composite after
                // the ribbon paint contribution instead of being immediately painted over.
                coverage = RibbonStrokeCoverage(bestAcross, acrossPerPixel,
                    _StrokeParameters.x, _StrokeParameters.y, _StrokeParameters.z);
                alpha = coverage * _StrokeColor.a * _StrokeParameters.w * commonWeight * shapeAlpha;
                if (_StrokeEnabled != 0 && alpha > 0.00001)
                {
                    result = CompositeStraightAlpha(result, _StrokeColor.rgb, alpha);
                    contributed = true;
                }

                if (!contributed) discard;
                return result;
            }
            ENDCG
        }
        // Copy the nearest UV owner's complete value into a two-texel exterior gutter. Using
        // geometry instead of alpha for ownership preserves transparent source holes and partial
        // opacity, and cannot expand the path into unpainted pixels inside a UV island.
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One Zero
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment PadUVGutter
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _RibbonGeometryCoverage;
            float4 _MainTex_TexelSize;

            float4 PadUVGutter(v2f_img input) : SV_Target
            {
                float4 center = tex2D(_MainTex, input.uv);
                if (center.a > 0.00001 || tex2D(_RibbonGeometryCoverage, input.uv).r > 0.5)
                    return center;
                float nearestDistance = 1e20;
                float2 nearestUV = input.uv;
                [unroll] for (int y = -2; y <= 2; y++)
                [unroll] for (int x = -2; x <= 2; x++)
                {
                    float distance = x * x + y * y;
                    float2 uv = input.uv + float2(x, y) * _MainTex_TexelSize.xy;
                    if (distance < nearestDistance && all(uv >= 0) && all(uv <= 1) &&
                        tex2D(_RibbonGeometryCoverage, uv).r > 0.5)
                    {
                        nearestDistance = distance;
                        nearestUV = uv;
                    }
                }
                return tex2D(_MainTex, nearestUV);
            }
            ENDCG
        }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One Zero
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex GeometryVert
            #pragma fragment GeometryFrag
            #include "UnityCG.cginc"
            struct GeometryAttributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            float4 GeometryVert(GeometryAttributes input) : SV_POSITION
            {
                float2 clipPosition = input.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                    clipPosition.y = -clipPosition.y;
                #endif
                return float4(clipPosition, 0.0, 1.0);
            }
            float4 GeometryFrag() : SV_Target { return 1; }
            ENDCG
        }
    }
}
