Shader "Hidden/UMA/Dismemberment/SurfaceCutPreview"
{
    Properties
    {
        _Color("Color", Color) = (1,0,0,0.95)
    }

    HLSLINCLUDE
    #include "UnityCG.cginc"

    float4 _Color;

    struct Attributes
    {
        float4 vertex : POSITION;
        float4 color : COLOR;
    };

    struct Varyings
    {
        float4 position : SV_POSITION;
        float4 color : COLOR;
    };

    Varyings PreviewVertex(Attributes input)
    {
        Varyings output;
        output.position = UnityObjectToClipPos(input.vertex);
        output.color = input.color * _Color;
        return output;
    }

    float4 PreviewFragment(Varyings input) : SV_Target
    {
        return input.color;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline"="HDRenderPipeline"
            "Queue"="Transparent+50"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }
        Pass
        {
            Name "HighDefinitionPreview"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PreviewVertex
            #pragma fragment PreviewFragment
            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent+50"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }
        Pass
        {
            Name "UniversalPreview"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PreviewVertex
            #pragma fragment PreviewFragment
            ENDHLSL
        }
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent+50"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }
        Pass
        {
            Name "BuiltInPreview"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PreviewVertex
            #pragma fragment PreviewFragment
            ENDHLSL
        }
    }

    Fallback Off
}
