Shader "Hidden/UMA/DecalRTStamp"
{
    SubShader
    {
        Tags { "Queue"="Transparent" }
        ZTest Always Cull Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _OverlayTex;
            float _Fudge;
            float _ForceLinear;

            struct appdata
            {
                float4 vertex : POSITION;   // already clip-space mapped
                float2 uv     : TEXCOORD0;  // main (unused in shader but parallels structure)
                float2 uv1    : TEXCOORD1;  // overlay planar UV
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 overlayUV : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = float4(v.vertex.xy, 0, 1);
                o.overlayUV = v.uv1;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.overlayUV;
                uv = clamp(uv, 0.0, 1.0); // clamp if needed
                fixed4 c = tex2D(_OverlayTex, uv);

                /*float2 d = (i.overlayUV - 0.5) * 2.0;
                float r = length(d);
                if (r > 1.0) discard;

                float edgeInner = 1.0 - max(_Fudge, 0.0001);
                float falloff = smoothstep(1.0, edgeInner, 1.0 - r);

                fixed4 c = tex2D(_OverlayTex, i.overlayUV);
                if (_ForceLinear > 0.5) c.rgb = GammaToLinearSpace(c.rgb);
                c.a *= falloff;
                if (c.a <= 0) discard;*/

                return c;
            }
            ENDCG
        }
    }
}