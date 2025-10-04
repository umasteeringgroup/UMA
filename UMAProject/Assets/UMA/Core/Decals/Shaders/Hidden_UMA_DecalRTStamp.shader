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
    float _UseFixedLOD;  // 0/1 toggle: force a fixed LOD for sampling to avoid cross-island mip seams
    float _FixedLOD;     // LOD level when _UseFixedLOD==1

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

        inline fixed4 SampleOverlay(float2 uv)
        {
            uv = saturate(uv);
            #if defined(SHADER_API_D3D11) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL)
                if (_UseFixedLOD > 0.5)
                {
                    return UNITY_SAMPLE_TEX2D_LOD(_OverlayTex, uv, _FixedLOD);
                }
            #endif
            return UNITY_SAMPLE_TEX2D(_OverlayTex, uv);
        }

        inline fixed SampleMask(float2 uv)
        {
            uv = saturate(uv);
            #if defined(SHADER_API_D3D11) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL)
                if (_UseFixedLOD > 0.5)
                {
                    return UNITY_SAMPLE_TEX2D_LOD(_MaskTex, uv, _FixedLOD).a;
                }
            #endif
            return UNITY_SAMPLE_TEX2D(_MaskTex, uv).a;
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
            fixed4 c = SampleOverlay(uv);

            // Apply global coverage mask (from overlay.textureList[0] alpha or explicit mask)
            if (_UseMask > 0.5)
            {
                fixed ma = SampleMask(uv);
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