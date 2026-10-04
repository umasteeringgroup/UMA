Shader "Hidden/UMA/TexturePaint/GarmentPreview"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "Garment.hlsl"
            #include "HemSeam.hlsl"
            float4 _PreviewSize;
            float4 _PreviewFrame;
            int _PreviewHem, _PreviewClosed;
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output; output.position = float4(input.vertex.xy, 0, 1);
                output.uv = input.uv; return output;
            }
            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = (input.uv - .5) / _PreviewFrame.xy + .5;
                float3 position = float3(uv * _PreviewSize.xy, 0);
                float4 value;
                if (_PreviewHem != 0)
                    value = ShadeHem(uv.x, uv.y * _PreviewSize.y / _PreviewSize.x,
                        _PreviewSize.y / _PreviewSize.x, _PreviewClosed != 0,
                        1.0 / (128 * _PreviewFrame.x), position, float3(0,0,1), float4(1,0,0,1), uv, _PreviewSize.x);
                else
                    value = ShadeGarment(uv, uv, 1.0 / (128 * _PreviewFrame.x), _PreviewClosed != 0,
                        position, float3(0,0,1), float4(1,0,0,1));
                // Evaluate derivatives before clipping the surrounding sample frame.
                return any(uv < 0) || any(uv > 1) ? 0 : value;
            }
            ENDHLSL
        }
    }
}
