Shader "Hidden/UMA/TransparentPrefill"
{
    Properties
    {
        _Color ("Prefill Color", Color) = (1, 1, 1, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata input)
            {
                v2f output;
                output.vertex = UnityObjectToClipPos(input.vertex);
                return output;
            }

            fixed4 frag(v2f input) : SV_Target
            {
                return fixed4(_Color.rgb, 0);
            }
            ENDCG
        }
    }

    Fallback Off
}
