Shader "Hidden/UMA/DecalRTStamp" 
{ 
    SubShader 
    { 
        Tags { "Queue"="Transparent" "RenderType"="Transparent" } 
        ZTest Always 
        Cull Off 
        ZWrite Off 
        Blend SrcAlpha OneMinusSrcAlpha
    Pass
    {
        HLSLPROGRAM
        #pragma vertex vert
        #pragma fragment frag
        #pragma target 3.0
        #include "UnityCG.cginc"

        UNITY_DECLARE_TEX2D(_OverlayTex);
        float _Fudge;        // reserved
        float _ForceLinear;  // reserved

        struct appdata
        {
            float4 vertex : POSITION;   // clip-space quad verts provided by caller
            float2 uv     : TEXCOORD0;  // unused
            float2 uv1    : TEXCOORD1;  // overlay planar UV
            fixed4 color  : COLOR;      // unused
        };

        struct v2f
        {
            float4 pos       : SV_POSITION;
            float2 overlayUV : TEXCOORD0;
        };

        v2f vert(appdata v)
        {
            v2f o;
            // vertices are already in clip space
            o.pos = float4(v.vertex.xy, 0.0, 1.0);
            o.overlayUV = v.uv1;
            return o;
        }

        fixed4 frag(v2f i) : SV_Target
        {
            float2 uv = saturate(i.overlayUV);
            fixed4 c = UNITY_SAMPLE_TEX2D(_OverlayTex, uv);

            // If needed, enable linearization:
            // if (_ForceLinear > 0.5) c.rgb = GammaToLinearSpace(c.rgb);

            return c;
        }
        ENDHLSL
    }
}
Fallback Off
}