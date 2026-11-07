Shader "Hidden/UMA/Dismemberment/RuntimeDecalComposite"
{
    Properties
    {
        [NoScaleOffset]_MainTex("State", 2D) = "black" {}
        [NoScaleOffset]_OverlayTex("Overlay", 2D) = "white" {}
        [NoScaleOffset]_MaskTex("Mask", 2D) = "white" {}
        [NoScaleOffset]_FluidTexture("Fluid Texture", 2D) = "white" {}
        [NoScaleOffset]_FluidMask("Fluid Mask", 2D) = "white" {}
        _FluidColor("Fluid Color", Color) = (0.32,0.005,0.003,0.92)
        _Opacity("Opacity", Range(0,1)) = 1
        _ThicknessScale("Thickness Scale", Float) = 12000
        _ThicknessThreshold("Thickness Threshold", Float) = 0.000002
        _DepositedTrailOpacityBoost("Deposited Trail Opacity Boost", Float) = 8
        _DepositedTrailAlpha("Deposited Trail Alpha", Range(0,1)) = 0.95
        _UseMask("Use Mask", Float) = 0
        _UseFluidTexture("Use Fluid Texture", Float) = 0
        _UseFluidMask("Use Fluid Mask", Float) = 0
        _FluidTextureScale("Fluid Texture Scale", Float) = 4
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
            #pragma vertex vert_img
            #pragma fragment FluidFrag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            sampler2D _FluidTexture;
            sampler2D _FluidMask;
            fixed4 _FluidColor;
            float _Opacity;
            float _ThicknessScale;
            float _ThicknessThreshold;
            float _DepositedTrailOpacityBoost;
            float _DepositedTrailAlpha;
            float _UseFluidTexture;
            float _UseFluidMask;
            float _FluidTextureScale;
            fixed4 FluidFrag(v2f_img input) : SV_Target
            {
                float4 state = tex2D(_MainTex, input.uv);
                // A threshold suppresses bilinear haze when the capped simulation is enlarged
                // to the UMA atlas. The smooth high-contrast response keeps deposited trails
                // dark without increasing their physical width or emission rate.
                float mobileFilm = max(0.0, state.x - _ThicknessThreshold);
                float depositedFilm = max(0.0, state.y - _ThicknessThreshold);
                float mobileCoverage = saturate(mobileFilm * _ThicknessScale);
                mobileCoverage = mobileCoverage * mobileCoverage *
                    (3.0 - 2.0 * mobileCoverage);
                float trailCoverage = saturate(depositedFilm * _ThicknessScale *
                    _DepositedTrailOpacityBoost);
                trailCoverage = trailCoverage * trailCoverage *
                    (3.0 - 2.0 * trailCoverage);
                float coverage = max(mobileCoverage,
                    trailCoverage * saturate(_DepositedTrailAlpha));
                fixed4 color = _FluidColor;
                float2 appearanceUV = frac(input.uv * max(1.0, _FluidTextureScale));
                if (_UseFluidTexture > 0.5) color *= tex2D(_FluidTexture, appearanceUV);
                if (_UseFluidMask > 0.5) color.a *= tex2D(_FluidMask, appearanceUV).a;
                color.a *= coverage * _Opacity;
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex StampVert
            #pragma fragment StampFrag
            #include "UnityCG.cginc"
            sampler2D _OverlayTex;
            sampler2D _MaskTex;
            float _UseMask;
            float _Opacity;
            float _FlipY;
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct Varyings { float4 position : SV_POSITION; float2 overlayUV : TEXCOORD0; };
            Varyings StampVert(Attributes input)
            {
                Varyings output;
                float2 position = input.vertex.xy;
                float2 overlayUV = input.uv1;
                if (_FlipY > 0.5) { position.y = -position.y; overlayUV.y = 1.0 - overlayUV.y; }
                output.position = float4(position, 0.0, 1.0);
                output.overlayUV = overlayUV;
                return output;
            }
            fixed4 StampFrag(Varyings input) : SV_Target
            {
                fixed4 color = tex2D(_OverlayTex, saturate(input.overlayUV));
                if (_UseMask > 0.5) color.a *= tex2D(_MaskTex, saturate(input.overlayUV)).a;
                color.a *= _Opacity;
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
