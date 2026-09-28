// Minimal built-in-pipeline fixture for testing the UMA Shader Graph's _BaseMap contract.
Shader "Hidden/UMA/HairCards/Tests/AtlasAlpha"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            float _Cutoff;
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(appdata_base input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                return output;
            }
            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = tex2D(_BaseMap, input.uv);
                clip(color.a - _Cutoff);
                return color;
            }
            ENDCG
        }
    }
}
