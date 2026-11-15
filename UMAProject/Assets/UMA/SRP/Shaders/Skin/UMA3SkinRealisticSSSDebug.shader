// Editor debugging only. Renders exactly like "UMA/URP/Realistic Skin SSS" when Show is Lit, and can show each
// input map, mesh attribute or lighting term by itself. The UMA Toolbar's Skin View menu puts scene skin
// materials on this shader while a view is active. Keep the Properties block identical to the production shader.
Shader "UMA/URP/Realistic Skin SSS Debug"
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
        _Skinmask_Amount("Tint Protection Strength", Range(0,2)) = 1
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
        _CurvatureScale("Curvature Response", Range(0,4)) = 1
        _NormalBlur("Wrinkle Softening in Diffuse Lighting", Range(0,2)) = 1
        _DiffuseNormalStrength("Pore Detail in Diffuse Lighting", Range(0,1)) = 0.35
        _ShadowScatter("Shadow Edge Scattering", Range(0,1)) = 0.5
        _OcclusionColorBleed("Occlusion Color Bleed", Range(0,1)) = 1
        [Header(Thin Area Transmission)]
        _TransmissionStrength("Backlight Transmission Strength", Range(0,1)) = 0.12
        _TransmissionPower("Backlight Focus", Range(1,16)) = 4
        _ThicknessMinMM("Minimum Thickness in Millimeters", Range(0.25,20)) = 2
        [Header(Surface Reflection)]
        _Metallic("Metallic (Entire Material)", Range(0,1)) = 0
        _SkinIOR("Skin Index of Refraction", Range(1.3,1.6)) = 1.43
        _OilLobeWeight("Oil Reflection Blend", Range(0,1)) = 0.25
        _OilSmoothness("Oil Reflection Smoothness", Range(0,0.9)) = 0.72
        _SpecularAA("Specular Antialiasing", Range(0,1)) = 0.4
        [Header(Optional Control Texture)]
        [NoScaleOffset] _SkinControlMap("Control - R Diffusion G Oil B Transmission A Pores", 2D) = "white" {}
        [Header(Diagnostics)]
        [Enum(UMA.Editors.UMASkinSSSDebugView)] _SkinDebug("View", Float) = 0
        [HideInInspector][NoScaleOffset] _SkinScatterLUT("Scatter Lookup", 2D) = "white" {}
        [Header(Debug Shader)]
        [Enum(UMA.Editors.UMASkinDebugMap)] _SkinDebugMap("Show", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 400
        HLSLINCLUDE
        #define _NORMALMAP 1
        #define _SPECULAR_SETUP 1
        // Adds the map views; see UMA3SkinSSSInput.hlsl. Never defined in the production shader.
        #define UMA_SKIN_DEBUG_MAPS 1
        #include "UMA3SkinSSSInput.hlsl"
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
            #include "UMA3SkinSSSLighting.hlsl"
            ENDHLSL
        }
        UsePass "UMA/URP/Realistic Skin SSS/SHADOWCASTER"
        UsePass "UMA/URP/Realistic Skin SSS/DEPTHONLY"
        UsePass "UMA/URP/Realistic Skin SSS/DEPTHNORMALS"
        UsePass "UMA/URP/Realistic Skin SSS/META"
        UsePass "UMA/URP/Realistic Skin SSS/MOTIONVECTORS"
        UsePass "UMA/URP/Realistic Skin SSS/XRMOTIONVECTORS"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
