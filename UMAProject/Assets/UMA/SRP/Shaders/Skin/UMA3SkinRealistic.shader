Shader "UMA/URP/Realistic Skin"
{
    Properties
    {
        [Header(UMA Texture Inputs)]
        [MainTexture] _BaseMap("Skin Albedo", 2D) = "white" {}
        [MainColor] _Base_Color("Skin Tint", Color) = (1,1,1,1)
        [Normal][NoScaleOffset] _BumpMap("Skin Normal", 2D) = "bump" {}
        _BumpScale("Skin Normal Strength", Range(0,2)) = 1
        [NoScaleOffset] _MaskMap("Packed Mask - R Scattering G AO B Detail A Smoothness", 2D) = "white" {}
        [NoScaleOffset] _Skinmask("Tint Protection - White Preserves Albedo", 2D) = "white" {}
        _Skinmask_Amount("Tint Protection Strength", Range(0,1)) = 1
        _Smoothness_Remap("Smoothness Range - X Min Y Max", Vector) = (0.15,0.6,0,0)
        _Ambient_Occlusion_Remap("Occlusion Range - X Min Y Max", Vector) = (0,1,0,0)
        [Header(Pore Detail)]
        [NoScaleOffset] _DetailNormalMap("Detail - R Albedo G Normal Y B Smoothness A Normal X", 2D) = "gray" {}
        _Detail_Tiling("Detail Tiling - X and Y", Vector) = (24,24,0,0)
        _DetailNormalMapScale("Detail Albedo Strength", Range(0,1)) = 0
        _Detail_Normal_Scale("Detail Normal Strength", Range(0,2)) = 0
        _Detail_Gloss_Scale("Detail Smoothness Strength", Range(0,1)) = 0
        [Header(Skin Diffusion)]
        _SSSBlend("Scattering Strength", Range(0,1)) = 0.65
        _SubsurfaceColor("Scattering Color", Color) = (1,0.38,0.22,1)
        _ScatterRadiusMM("Scattering Radius in Millimeters", Range(0,8)) = 2.5
        _DiffusionWrap("Broad Diffusion", Range(0,0.5)) = 0.12
        _DiffuseNormalStrength("Pore Detail in Diffuse Lighting", Range(0,1)) = 0.35
        [Header(Thin Area Transmission)]
        _TransmissionStrength("Backlight Transmission Strength", Range(0,1)) = 0.12
        _TransmissionPower("Backlight Focus", Range(1,16)) = 4
        [Header(Surface Reflection)]
        _SkinIOR("Skin Index of Refraction", Range(1.3,1.6)) = 1.43
        _OilLobeWeight("Oil Reflection Blend", Range(0,1)) = 0.25
        _OilSmoothness("Oil Reflection Smoothness", Range(0,0.9)) = 0.72
        _SpecularAA("Specular Antialiasing", Range(0,1)) = 0.4
        [Header(Optional Control Texture)]
        [NoScaleOffset] _SkinControlMap("Control - R Diffusion G Oil B Transmission A Pores", 2D) = "white" {}
        [Header(Diagnostics)]
        [Enum(Shaded,0,Albedo,1,Normals,2,Scattering,3,Smoothness,4,Occlusion,5)] _SkinDebug("View", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 400
        HLSLINCLUDE
        #define _NORMALMAP 1
        #define _SPECULAR_SETUP 1
        #include "UMA3SkinInput.hlsl"
        ENDHLSL
        Pass
        {
            Name "ForwardSkin"
            Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On
            Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SkinVertex
            #pragma fragment SkinFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "UMA3SkinLighting.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SkinVertex
            #pragma fragment SkinDepthNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "UMA3SkinLighting.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "Meta"
            Tags { "LightMode"="Meta" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SkinMetaVertex
            #pragma fragment SkinMetaFragment
            #pragma shader_feature EDITOR_VISUALIZATION
            #include "UMA3SkinMeta.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            HLSLPROGRAM
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "XRMotionVectors"
            Tags { "LightMode"="XRMotionVectors" }
            ColorMask RGBA
            Stencil { WriteMask 1 Ref 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma multi_compile _ LOD_FADE_CROSSFADE
            #pragma shader_feature_local_vertex _ADD_PRECOMPUTED_VELOCITY
            #define APPLICATION_SPACE_WARP_MOTION 1
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
