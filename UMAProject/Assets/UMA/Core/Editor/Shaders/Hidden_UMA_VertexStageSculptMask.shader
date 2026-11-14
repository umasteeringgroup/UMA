// Draws the vertex editor stage's sculpt mask as a soft colored wash over the mesh,
// the same way the Overlay Painter shows its selection. The mask is one float per
// mesh vertex in _SculptMask, indexed by SV_VertexID and blended across triangles.
// Depth-tested with a small offset so it hugs the visible surface without flicker.
Shader "Hidden/UMA/VertexStage/SculptMask"
{
    Properties
    {
        _Color ("Mask Color", Color) = (0.04, 0.12, 0.55, 0.75)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #include "UnityCG.cginc"

            StructuredBuffer<float> _SculptMask;
            int _SculptMaskCount;
            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                uint id : SV_VertexID;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float mask : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.mask = (int)v.id < _SculptMaskCount ? _SculptMask[v.id] : 0;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float mask = saturate(i.mask);
                clip(mask - 0.004);
                // Lift partial masks so soft brush edges stay readable; full masks use _Color.a.
                return float4(_Color.rgb, _Color.a * sqrt(mask));
            }
            ENDCG
        }
    }
    Fallback Off
}
