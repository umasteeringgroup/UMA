Shader "Hidden/UMA/Dismemberment/FallbackTrail"
{
    Properties
    {
        _Color("Color", Color) = (0.32,0.005,0.003,0.92)
        _BaseColor("Base Color", Color) = (0.32,0.005,0.003,0.92)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Attributes { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; fixed4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color;
                return output;
            }
            fixed4 Frag(Varyings input) : SV_Target { return _Color * input.color; }
            ENDHLSL
        }
    }
    Fallback Off
}
