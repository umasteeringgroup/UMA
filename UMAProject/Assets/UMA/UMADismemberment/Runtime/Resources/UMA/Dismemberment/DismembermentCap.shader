Shader "UMA/Dismemberment/Cap Unlit"
{
    Properties
    {
        _MainTex ("Cap Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }

    HLSLINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_ST;
    float4 _Color;

    struct Attributes
    {
        float4 positionOS : POSITION;
        float2 uv : TEXCOORD0;
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float2 uv : TEXCOORD0;
    };

    Varyings CapVertex(Attributes input)
    {
        Varyings output;
        output.positionCS = UnityObjectToClipPos(input.positionOS);
        output.uv = TRANSFORM_TEX(input.uv, _MainTex);
        return output;
    }

    half4 CapFragment(Varyings input) : SV_Target
    {
        return tex2D(_MainTex, input.uv) * _Color;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "UniversalCap"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CapVertex
            #pragma fragment CapFragment
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "HighDefinitionCap"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CapVertex
            #pragma fragment CapFragment
            ENDHLSL
        }
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "BuiltInCap"
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex CapVertex
            #pragma fragment CapFragment
            ENDHLSL
        }
    }

    Fallback Off
}
