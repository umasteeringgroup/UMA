Shader "Hidden/UMA/DecalRTDilate" 
{ 
    Properties
    {
        _Radius("Dilation Radius (px, 0-16)", Range(0,16)) = 2
        [Toggle]_PreserveAlpha("Preserve Original Alpha", Float) = 1
        _MinNeighborAlpha("Min Neighbor Alpha", Range(0,1)) = 0.10
        [Toggle]_RGBOnly("Dilate RGB Only (ignore alpha gating)", Float) = 0
    }
    SubShader 
    { 
        Tags { "Queue"="Transparent" } 
        ZTest Always 
        Cull Off 
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            UNITY_DECLARE_TEX2D(_MainTex);
            float4 _MainTex_TexelSize; // x=1/w, y=1/h (y can be negative depending on RT flip)
            float _Radius;             // dilation radius in pixels (0..16)
            float _PreserveAlpha;      // 0/1: keep original alpha
            float _MinNeighborAlpha;   // threshold for considering a neighbor valid
            float _RGBOnly;            // 0/1: modify only RGB, do not gate by base alpha or change alpha

            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
            };

            v2f vert(uint id : SV_VertexID)
            {
                // Fullscreen quad from 4 vertices
                const float2 verts[4] = {
                    float2(-1,-1), float2( 1,-1),
                    float2( 1, 1), float2(-1, 1)
                };
                const float2 uvs[4] = {
                    float2(0,0), float2(1,0),
                    float2(1,1), float2(0,1)
                };

                v2f o;
                o.pos = float4(verts[id], 0, 1);
                o.uv  = uvs[id];

                // Handle RT UV orientation (DX vs GL) when sourcing from a RT
                #if defined(UNITY_UV_STARTS_AT_TOP)
                    // Unity sets _MainTex_TexelSize.y negative when UVs are inverted
                    if (_MainTex_TexelSize.y < 0.0)
                        o.uv.y = 1.0 - o.uv.y;
                #endif

                return o;
            }

            fixed4 SampleClamp(float2 uv)
            {
                return UNITY_SAMPLE_TEX2D(_MainTex, saturate(uv));
            }

            // Compare by alpha, keep the color with higher alpha
            inline void KeepBestAlpha(in fixed4 candidate, inout fixed4 best)
            {
                // Only consider neighbors with enough coverage to be meaningful
                if (candidate.a >= _MinNeighborAlpha && candidate.a > best.a) best = candidate;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseCol = SampleClamp(i.uv);

                // Early out for legacy alpha-based dilation only
                if (_RGBOnly < 0.5 && baseCol.a >= 0.99) return baseCol;

                // Remember original alpha to optionally preserve coverage
                float origA = baseCol.a;

                // Magnitudes (positive) for pixel step in UV units
                float2 stepX = float2(_MainTex_TexelSize.x, 0.0);
                float2 stepY = float2(0.0, abs(_MainTex_TexelSize.y));

                // Search up to radius R in 8 directions, keep the highest alpha neighbor
                int R = (int)clamp(_Radius, 0.0, 16.0);
                fixed4 best = baseCol;

                [loop]
                for (int s = 1; s <= R; s++)
                {
                    float2 dx = stepX * s;
                    float2 dy = stepY * s;

                    // 4-axis
                    KeepBestAlpha(SampleClamp(i.uv + dx), best);
                    KeepBestAlpha(SampleClamp(i.uv - dx), best);
                    KeepBestAlpha(SampleClamp(i.uv + dy), best);
                    KeepBestAlpha(SampleClamp(i.uv - dy), best);

                    // 4-diagonals
                    KeepBestAlpha(SampleClamp(i.uv + dx + dy), best);
                    KeepBestAlpha(SampleClamp(i.uv + dx - dy), best);
                    KeepBestAlpha(SampleClamp(i.uv - dx + dy), best);
                    KeepBestAlpha(SampleClamp(i.uv - dx - dy), best);
                }

                if (_RGBOnly > 0.5)
                {
                    // In RGB-only mode, always adopt best RGB if it has any meaningful alpha
                    if (best.a >= _MinNeighborAlpha)
                    {
                        baseCol.rgb = best.rgb;
                        // Alpha preserved by default, but allow optional expansion if desired
                        if (_PreserveAlpha > 0.5)
                            baseCol.a = origA;
                        else
                            baseCol.a = max(baseCol.a, best.a);
                    }
                    return baseCol;
                }

                // Alpha-aware mode: Blend toward best based on how much alpha we are missing
                if (best.a > baseCol.a)
                {
                    float k = saturate(1.0 - baseCol.a);
                    baseCol.rgb = lerp(baseCol.rgb, best.rgb, k);
                    if (_PreserveAlpha > 0.5)
                        baseCol.a = origA;
                    else
                        baseCol.a = max(baseCol.a, best.a);
                }
                return baseCol;
            }
            ENDHLSL
        }
    }
    Fallback Off
}