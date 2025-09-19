Shader "Hidden/UMA/DecalRTDilate" 
{ 
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

        fixed4 frag(v2f i) : SV_Target
        {
            fixed4 baseCol = SampleClamp(i.uv);
            if (baseCol.a >= 0.99) return baseCol;

            float2 dx = float2(_MainTex_TexelSize.x, 0);
            float2 dy = float2(0, abs(_MainTex_TexelSize.y)); // use magnitude for offsets

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

            if (best.a > baseCol.a)
            {
                float k = 1.0 - baseCol.a;
                baseCol.rgb = lerp(baseCol.rgb, best.rgb, k);
                baseCol.a = max(baseCol.a, best.a);
            }
            return baseCol;
        }
        ENDHLSL
    }
}
Fallback Off
}