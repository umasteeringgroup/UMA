Shader "Hidden/UMA/DecalRTDilate"
{
    SubShader
    {
        Tags { "Queue"="Transparent" }
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // x,y = 1/width,1/height

            struct v2f { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };

            v2f vert(uint id: SV_VertexID)
            {
                v2f o;
                float2 verts[4] = {
                    float2(-1,-1), float2(1,-1),
                    float2(1,1),   float2(-1,1)
                };
                float2 uvs[4] = {
                    float2(0,0), float2(1,0),
                    float2(1,1), float2(0,1)
                };
                o.pos = float4(verts[id],0,1);
                o.uv  = uvs[id];
                return o;
            }

            fixed4 SampleClamp(float2 uv)
            {
                return tex2D(_MainTex, clamp(uv, 0.0, 1.0));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseCol = SampleClamp(i.uv);
                // Early out if already mostly opaque
                if (baseCol.a >= 0.99) return baseCol;

                float2 dx = float2(_MainTex_TexelSize.x, 0);
                float2 dy = float2(0, _MainTex_TexelSize.y);

                // 8 neighbors
                fixed4 n0 = SampleClamp(i.uv + dx);
                fixed4 n1 = SampleClamp(i.uv - dx);
                fixed4 n2 = SampleClamp(i.uv + dy);
                fixed4 n3 = SampleClamp(i.uv - dy);
                fixed4 n4 = SampleClamp(i.uv + dx + dy);
                fixed4 n5 = SampleClamp(i.uv + dx - dy);
                fixed4 n6 = SampleClamp(i.uv - dx + dy);
                fixed4 n7 = SampleClamp(i.uv - dx - dy);

                fixed4 best = baseCol;
                if (n0.a > best.a) best = n0;
                if (n1.a > best.a) best = n1;
                if (n2.a > best.a) best = n2;
                if (n3.a > best.a) best = n3;
                if (n4.a > best.a) best = n4;
                if (n5.a > best.a) best = n5;
                if (n6.a > best.a) best = n6;
                if (n7.a > best.a) best = n7;

                // Blend toward best based on how much alpha we are missing
                // (keeps soft interiors soft)
                if (best.a > baseCol.a)
                {
                    float k = 1.0 - baseCol.a;
                    baseCol.rgb = lerp(baseCol.rgb, best.rgb, k);
                    baseCol.a = max(baseCol.a, best.a);
                }
                return baseCol;
            }
            ENDCG
        }
    }
}