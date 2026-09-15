Shader "UMA/Hair Cards/Swept Hair URP"
{
    Properties
    {
        [MainTexture] _BaseMap("Strand Atlas (RGB + Alpha)", 2D) = "white" {}
        [MainColor] _BaseColor("Overall Tint", Color) = (1,1,1,1)
        _RootColor("Root Color", Color) = (0.035,0.022,0.01,1)
        _TipColor("Tip Color", Color) = (0.42,0.28,0.10,1)
        _RootFade("Root Color Reach", Range(0.01,1)) = 0.7
        _ColorPower("Root to Tip Curve", Range(0.2,4)) = 0.7
        _TextureColor("Use Atlas RGB as Color", Range(0,1)) = 0
        _DepthInfluence("Atlas R Depth Shading", Range(0,1)) = 0.3
        _DepthMap("Optional Strand Depth Atlas (R)", 2D) = "white" {}
        [Toggle] _UseDepthMap("Use Separate Depth Atlas", Float) = 0
        _OcclusionMap("Ambient Occlusion Atlas (R)", 2D) = "white" {}
        _OcclusionStrength("Ambient Occlusion Strength", Range(0,1)) = 1
        _StrandVariation("Strand Color Variation", Range(0,1)) = 0.2
        _ClumpVariation("Clump Color Variation", Range(0,1)) = 0.12
        _SpecularColor("Primary Highlight", Color) = (1,0.87,0.64,1)
        _SpecularStrength("Primary Strength", Range(0,2)) = 0.4
        _Smoothness("Primary Smoothness", Range(0,1)) = 0.7
        _SpecularShift("Primary Shift", Range(-0.5,0.5)) = -0.1
        _SecondaryColor("Secondary Highlight", Color) = (0.65,0.28,0.06,1)
        _SecondaryStrength("Secondary Strength", Range(0,2)) = 0.3
        _SecondarySmoothness("Secondary Smoothness", Range(0,1)) = 0.4
        _SecondaryShift("Secondary Shift", Range(-0.5,0.5)) = 0.2
        _Transmission("Backlit Scattering", Range(0,2)) = 0.5
        _TransmissionColor("Scattering Tint", Color) = (1,0.55,0.15,1)
        _DiffuseWrap("Soft Diffuse Wrap", Range(0,1)) = 0.35
        _AmbientStrength("Ambient Strength", Range(0,2)) = 0.8
        [Normal] _BumpMap("Strand Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Strength", Range(0,2)) = 0.5
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.35
        _ShadowCutoff("Shadow Alpha Cutoff", Range(0,1)) = 0.45
        [Toggle] _AlphaToCoverage("MSAA Alpha to Coverage", Float) = 1
        [Toggle] _DitheredOpacity("Soft Coverage (Dithered)", Float) = 0
        _Coverage("Strand Opacity", Range(0,1)) = 1
        _VertexColorInfluence("Use Vertex RGB as Tint", Range(0,1)) = 0
        _VertexAlphaInfluence("Use Vertex Alpha as Opacity", Range(0,1)) = 0
        [Enum(Shaded,0,RootToTip,1,ClumpIDs,2,StrandIDs,3,Mask,4,Normals,5)] _DebugView("Diagnostic View", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" "IgnoreProjector"="True" }
        Cull Off
        HLSLINCLUDE
        #include "UMASweptHairURP.hlsl"
        ENDHLSL
        Pass
        {
            Name "ForwardHair"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            AlphaToMask [_AlphaToCoverage]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HairVertex
            #pragma fragment HairFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HairShadowVertex
            #pragma fragment HairShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            AlphaToMask [_AlphaToCoverage]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HairVertex
            #pragma fragment HairDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            AlphaToMask [_AlphaToCoverage]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HairVertex
            #pragma fragment HairDepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "UMA.HairCards.Editor.HairSweptShaderGUI"
}
