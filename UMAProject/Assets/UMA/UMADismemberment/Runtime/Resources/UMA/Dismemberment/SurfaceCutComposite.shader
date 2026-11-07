Shader "Hidden/UMA/Dismemberment/SurfaceCutComposite"
{
    Properties
    {
        _CenterColor("Center Color", Color) = (0.16,0.002,0.004,1)
        _EdgeColor("Edge Color", Color) = (0.95,0.22,0.28,0.82)
        _HalfWidthMeters("Half Width (m)", Float) = 0.004
        _LengthMeters("Length (m)", Float) = 0.1
        _CenterFraction("Center Fraction", Range(0,1)) = 0.32
        _EdgeSoftness("Edge Softness", Range(0,1)) = 0.14
        _EndTaperFraction("End Taper", Range(0,0.5)) = 0.12
        _FlipY("Flip Y", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        ZTest Always Cull Off ZWrite Off
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            fixed4 _CenterColor;
            fixed4 _EdgeColor;
            float _HalfWidthMeters;
            float _LengthMeters;
            float _CenterFraction;
            float _EdgeSoftness;
            float _EndTaperFraction;
            float _FlipY;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 cutCoordinates : TEXCOORD0;
            };

            struct Varyings
            {
                float4 position : SV_POSITION;
                float2 cutCoordinates : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float2 position = input.vertex.xy;
                if (_FlipY > 0.5) position.y = -position.y;
                output.position = float4(position, 0.0, 1.0);
                output.cutCoordinates = input.cutCoordinates;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float lengthMeters = max(0.000001, _LengthMeters);
                float along = saturate(input.cutCoordinates.y / lengthMeters);
                float endDistance = min(along, 1.0 - along);
                float taperSpan = max(0.0001, _EndTaperFraction);
                float taper = smoothstep(0.0, taperSpan, endDistance);
                float halfWidth = max(0.000001, _HalfWidthMeters * taper);
                float across = abs(input.cutCoordinates.x) / halfWidth;
                clip(1.0 - across);

                float center = saturate(_CenterFraction);
                float sideBlend = smoothstep(center, 1.0, across);
                fixed4 color = lerp(_CenterColor, _EdgeColor, sideBlend);
                float softness = max(0.001, _EdgeSoftness);
                color.a *= 1.0 - smoothstep(1.0 - softness, 1.0, across);
                color.a *= taper;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
