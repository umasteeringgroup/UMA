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
    UNITY_DECLARE_TEX2D(_MaskTex);
        float _Fudge;        // reserved
        float _ForceLinear;  // reserved
        float4 _UVRect;     // x=minx, y=miny, z=maxx, w=maxy
        float _UseUVRect;   // 0/1 toggle
    float _UseMask;     // 0/1 toggle

        struct appdata
        {
            float4 vertex : POSITION;   // clip-space quad verts provided by caller
            float2 uv     : TEXCOORD0;  // base UV0 (atlas space)
            float2 uv1    : TEXCOORD1;  // overlay planar UV
            fixed4 color  : COLOR;      // unused
        };

        struct v2f
        {
            float4 pos       : SV_POSITION;
            float2 overlayUV : TEXCOORD0;
            float2 baseUV    : TEXCOORD1;
        };

        v2f vert(appdata v)
        {
            v2f o;
            // vertices are already in clip space
            o.pos = float4(v.vertex.xy, 0.0, 1.0);
            o.overlayUV = v.uv1;
            o.baseUV = v.uv;
            return o;
        }

        fixed4 frag(v2f i) : SV_Target
        {
            // Optional UV clipping to the provided rect (atlas region)
            if (_UseUVRect > 0.5)
            {
                if (i.baseUV.x < _UVRect.x || i.baseUV.x > _UVRect.z || i.baseUV.y < _UVRect.y || i.baseUV.y > _UVRect.w)
                    discard;
            }

            float2 uv = clamp(i.overlayUV, 0.0, 1.0);
            fixed4 c = UNITY_SAMPLE_TEX2D(_OverlayTex, uv);

            // Apply global coverage mask (from overlay.textureList[0] alpha or explicit mask)
            if (_UseMask > 0.5)
            {
                fixed ma = UNITY_SAMPLE_TEX2D(_MaskTex, uv).a;
                c.a *= ma;
            }

            // If needed, enable linearization:
            // if (_ForceLinear > 0.5) c.rgb = GammaToLinearSpace(c.rgb);

            return c;
        }
        ENDHLSL
    }
}
Fallback Off
}