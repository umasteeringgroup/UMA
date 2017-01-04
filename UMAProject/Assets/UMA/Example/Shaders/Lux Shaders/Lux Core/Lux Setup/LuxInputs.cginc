#ifndef LUX_INPUTS_INCLUDED
#define LUX_INPUTS_INCLUDED

#include "UnityCG.cginc"
#include "UnityShaderVariables.cginc"
#include "UnityStandardConfig.cginc"
#include "UnityPBSLighting.cginc" // TBD: remove
#include "UnityStandardUtils.cginc"

//---------------------------------------
// Directional lightmaps & Parallax require tangent space too
// Lux: _NORMALMAP always defined so _TANGENT_TO_WORLD is too
#define _TANGENT_TO_WORLD 1 

#if (_DETAIL_MULX2 || _DETAIL_MUL || _DETAIL_ADD || _DETAIL_LERP)
    #define _DETAIL 1
#endif

// From UnityStandardInput --------------------------------------------------------------------------
half4       _Color;
half        _Cutoff;

sampler2D   _MainTex;
float4      _MainTex_ST;

sampler2D   _DetailAlbedoMap;
float4      _DetailAlbedoMap_ST;

sampler2D   _BumpMap;
half        _BumpScale;

sampler2D   _DetailMask;
sampler2D   _DetailNormalMap;
half        _DetailNormalMapScale;

sampler2D   _SpecGlossMap;
sampler2D   _MetallicGlossMap;
half        _Metallic;
half        _Glossiness;
half        _GlossMapScale;

sampler2D   _OcclusionMap;
half        _OcclusionStrength;

sampler2D   _ParallaxMap;
half        _Parallax;
half        _UVSec;

half4       _EmissionColor;
sampler2D   _EmissionMap;

//-------------------------------------------------------------------------------------

struct LuxVertexInput
{
    float4 vertex   : POSITION;
    half3 normal    : NORMAL;
    float2 uv0      : TEXCOORD0;
    float2 uv1      : TEXCOORD1;
#if defined(DYNAMICLIGHTMAP_ON) || defined(UNITY_PASS_META)
    float2 uv2      : TEXCOORD2;
#endif
#ifdef _TANGENT_TO_WORLD
    half4 tangent   : TANGENT;
#endif
    // Lux
    fixed4 color    : COLOR;
};

float4 LuxTexCoords(LuxVertexInput v)
{
    float4 texcoord;
    texcoord.xy = TRANSFORM_TEX(v.uv0, _MainTex); // Always source from uv0
    texcoord.zw = TRANSFORM_TEX(((_UVSec == 0) ? v.uv0 : v.uv1), _DetailAlbedoMap);
    return texcoord;
} 

// Additional Inputs ------------------------------------------------------------------

float2 _Lux_DetailDistanceFade;         // x: Distance in which details like POM and water bumps are rendered / y: Detail Fade Range

fixed _DiffuseScatteringEnabled;
fixed3 _DiffuseScatteringCol;
half _DiffuseScatteringBias;
half _DiffuseScatteringContraction;

// Mix Mapping
#if defined(GEOM_TYPE_BRANCH_DETAIL)
    fixed4 _Color2;
    fixed _Glossiness2;
    
    fixed4 _SpecColor2;
    sampler2D _SpecGlossMap2;

    half _Metallic2;
    sampler2D _MetallicGlossMap2;

    fixed3 _DiffuseScatteringCol2;
    half _DiffuseScatteringBias2;
    half _DiffuseScatteringContraction2;
#endif

#if defined(EFFECT_BUMP)
    half _LinearSteps;
    // further Inputs in include!
#endif

//  Translucent Lighting
#if defined (LOD_FADE_PERCENTAGE)
    half4 _Lux_Tanslucent_Settings;
    half _Lux_Transluclent_NdotL_Shadowstrength;
    half _TranslucencyStrength;
    half _ScatteringPower;
#endif

// Combined Map
#if defined(GEOM_TYPE_BRANCH)
    sampler2D _CombinedMap;
#endif

// From UnityStandardInput --------------------------------------------------------------------------
half DetailMask(float2 uv)
{
    return tex2D (_DetailMask, uv).a;
}

half Alpha(float2 uv)
{
#if defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A)
    return _Color.a;
#else
    return tex2D(_MainTex, uv).a * _Color.a;
#endif
}       

half3 NormalInTangentSpace(float4 texcoords)
{
    half3 normalTangent = UnpackScaleNormal(tex2D (_BumpMap, texcoords.xy), _BumpScale);
    // SM20: instruction count limitation
    // SM20: no detail normalmaps
#if _DETAIL && !defined(SHADER_API_MOBILE) && (SHADER_TARGET >= 30) 
    half mask = DetailMask(texcoords.xy);
    half3 detailNormalTangent = UnpackScaleNormal(tex2D (_DetailNormalMap, texcoords.zw), _DetailNormalMapScale);
    #if _DETAIL_LERP
        normalTangent = lerp(
            normalTangent,
            detailNormalTangent,
            mask);
    #else               
        normalTangent = lerp(
            normalTangent,
            BlendNormals(normalTangent, detailNormalTangent),
            mask);
    #endif
#endif
    return normalTangent;
}

half Occlusion(float2 uv)
{
#if (SHADER_TARGET < 30)
    // SM20: instruction count limitation
    // SM20: simpler occlusion
    return tex2D(_OcclusionMap, uv).g;
#else
    half occ = tex2D(_OcclusionMap, uv).g;
    return LerpOneTo (occ, _OcclusionStrength);
#endif
}

half3 Emission(float2 uv)
{
#ifndef _EMISSION
    return 0;
#else
    return tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb;
#endif
}

//-------------------------------------------------------------------------------------
// counterpart for NormalizePerPixelNormal
// skips normalization per-vertex and expects normalization to happen per-pixel
half3 NormalizePerVertexNormal (half3 n)
{
    #if (SHADER_TARGET < 30) || UNITY_STANDARD_SIMPLE
        return normalize(n);
    #else
        return n; // will normalize per-pixel instead
    #endif
}

half3 NormalizePerPixelNormal (half3 n)
{
    #if (SHADER_TARGET < 30) || UNITY_STANDARD_SIMPLE
        return n;
    #else
        return normalize(n);
    #endif
}

// Get alpha --------------------------------------------------------------------------
half4 Lux_AlbedoAlpha(float2 uv)
{
    return tex2D(_MainTex, uv) * _Color;
}

// Diffuse/Spec Energy conservation ---------------------------------------------------
inline half4 Lux_EnergyConservationBetweenDiffuseAndSpecular (half4 albedo, half3 specColor, out half oneMinusReflectivity)
{
    oneMinusReflectivity = 1 - SpecularStrength(specColor);
    #if !UNITY_CONSERVE_ENERGY
        return albedo;
    #elif UNITY_CONSERVE_ENERGY_MONOCHROME
        return half4(albedo.rgb * oneMinusReflectivity, albedo.a);
    #else
        return half4(albedo.rgb * (half3(1,1,1) - specColor), albedo.a);
    #endif
}

inline half4 Lux_DiffuseAndSpecularFromMetallic (half4 albedo, half metallic, out half3 specColor, out half oneMinusReflectivity)
{
    specColor = lerp (unity_ColorSpaceDielectricSpec.rgb, albedo.rgb, metallic);
    oneMinusReflectivity = OneMinusReflectivityFromMetallic(metallic);
    //return half4(albedo.rgb * oneMinusReflectivity, albedo.a);
    // We must not do any energy conservation at this stage:
    return half4(albedo);
}

// Get albedo -------------------------------------------------------------------------
// Handles detail blending and mix mapping and return occlusion of detail texture in case mixmapping is ebanabled
half4 Lux_Albedo(half2 mixmapValue, half4 temp_albedo_2ndOcclusion, float4 texcoords)
{
    half3 albedo = temp_albedo_2ndOcclusion.rgb;
#if _DETAIL
    #if (SHADER_TARGET < 30)
        // SM20: instruction count limitation
        // SM20: no detail mask
        half mask = 1; 
    #else
        half mask = DetailMask(texcoords.xy);
    #endif

    // Regular Detail Blending
    #if !defined(GEOM_TYPE_BRANCH_DETAIL)
        half3 detailAlbedo = tex2D (_DetailAlbedoMap, texcoords.zw).rgb;
        #if _DETAIL_MULX2
            albedo *= LerpWhiteTo (detailAlbedo * unity_ColorSpaceDouble.rgb, mask);
        #elif _DETAIL_MUL
            albedo *= LerpWhiteTo (detailAlbedo, mask);
        #elif _DETAIL_ADD
            albedo += detailAlbedo * mask;
        #elif _DETAIL_LERP
            albedo = lerp (albedo, detailAlbedo, mask);
        #endif
        temp_albedo_2ndOcclusion = half4(albedo, 1);
    // Mix Mapping
    #else
        half4 detailAlbedo = tex2D (_DetailAlbedoMap, texcoords.zw).rgba * _Color2.rgba;
        albedo = lerp(albedo, detailAlbedo.rgb, mixmapValue.y);
        temp_albedo_2ndOcclusion = half4(albedo, detailAlbedo.a);
    #endif
#endif
    return temp_albedo_2ndOcclusion;
}


// Get normals TS ------------------------------------------------------------------------
// Handles detail blending and mix mapping
//#ifdef _NORMALMAP
half3 Lux_NormalInTangentSpace(half2 mixmapValue, float4 texcoords)
{
    half3 normalTangent = UnpackScaleNormal(tex2D (_BumpMap, texcoords.xy), _BumpScale);
    // SM20: instruction count limitation
    // SM20: no detail normalmaps

    #if _DETAIL && !defined(SHADER_API_MOBILE) && (SHADER_TARGET >= 30) 
    
        // Regular Detail Blending
        #if !defined(GEOM_TYPE_BRANCH_DETAIL)
            half mask = DetailMask(texcoords.xy);
            half3 detailNormalTangent = UnpackScaleNormal(tex2D (_DetailNormalMap, texcoords.zw), _DetailNormalMapScale);
            #if _DETAIL_LERP
                normalTangent = lerp(
                    normalTangent,
                    detailNormalTangent,
                    mask);
            #else               
                normalTangent = lerp(
                    normalTangent,
                    BlendNormals(normalTangent, detailNormalTangent),
                    mask);
            #endif
        // Mix Mapping
        #else
            half3 detailNormalTangent = UnpackScaleNormal(tex2D (_DetailNormalMap, texcoords.zw), _DetailNormalMapScale);
            normalTangent = normalTangent * mixmapValue.x + detailNormalTangent * mixmapValue.y;
        #endif
    #endif

    return normalTangent;
}
//#endif

// Get normals WS ------------------------------------------------------------------------
half3 Lux_PerPixelWorldNormal(half2 mixmapValue, float4 i_tex, half4 tangentToWorld[3])
{
#ifdef _NORMALMAP
    half3 tangent = tangentToWorld[0].xyz;
    half3 binormal = tangentToWorld[1].xyz;
    half3 normal = tangentToWorld[2].xyz;

    #if UNITY_TANGENT_ORTHONORMALIZE
        normal = NormalizePerPixelNormal(normal);

        // ortho-normalize Tangent
        tangent = normalize (tangent - normal * dot(tangent, normal));

        // recalculate Binormal
        half3 newB = cross(normal, tangent);
        binormal = newB * sign (dot (newB, binormal));
    #endif

    half3 normalTangent = Lux_NormalInTangentSpace(mixmapValue, i_tex);
    half3 normalWorld = NormalizePerPixelNormal(tangent * normalTangent.x + binormal * normalTangent.y + normal * normalTangent.z); // @TODO: see if we can squeeze this normalize on SM2.0 as well
#else
    half3 normalWorld = normalize(tangentToWorld[2].xyz);
#endif
    return normalWorld;
}

// Convert normals to WS ------------------------------------------------------------------------
half3 Lux_ConvertPerPixelWorldNormal(half3 normalTangent, half4 tangentToWorld[3])
{
#ifdef _NORMALMAP
    half3 tangent = tangentToWorld[0].xyz;
    half3 binormal = tangentToWorld[1].xyz;
    half3 normal = tangentToWorld[2].xyz;

    #if UNITY_TANGENT_ORTHONORMALIZE
        normal = NormalizePerPixelNormal(normal);
        // ortho-normalize Tangent
        tangent = normalize (tangent - normal * dot(tangent, normal));
        // recalculate Binormal
        half3 newB = cross(normal, tangent);
        binormal = newB * sign (dot (newB, binormal));
    #endif

    half3 normalWorld = NormalizePerPixelNormal(tangent * normalTangent.x + binormal * normalTangent.y + normal * normalTangent.z); // @TODO: see if we can squeeze this normalize on SM2.0 as well
#else
    half3 normalWorld = normalize(tangentToWorld[2].xyz);
#endif
    return normalWorld;
}


// Get occlusion ----------------------------------------------------------------------------

// Regular Blending
#if !defined(GEOM_TYPE_BRANCH_DETAIL)

//  Base function
    half Lux_Occlusion(float2 uv)
    {
    #if (SHADER_TARGET < 30)
        // SM20: instruction count limitation
        // SM20: simpler occlusion
        half occ = tex2D(_OcclusionMap, uv).g;
        return occ;
    #else
        half occ = tex2D(_OcclusionMap, uv).g;
        occ = LerpOneTo (occ, _OcclusionStrength);
        return occ;
    #endif
    }

//  Overload when using combined map
    half Lux_Occlusion( half occ)
    {
    #if (SHADER_TARGET < 30)
        // SM20: instruction count limitation
        // SM20: simpler occlusion
        return occ;
    #else
        occ = LerpOneTo (occ, _OcclusionStrength);
        return occ;
    #endif
    }

// Mix Mapping
#else

//  Base function
    half Lux_Occlusion(half2 mixmapValue, half occlusion2, float2 uv)
    {
    #if (SHADER_TARGET < 30)
        // SM20: instruction count limitation
        // SM20: simpler occlusion
        half occ = tex2D(_OcclusionMap, uv).g;
        return occ * mixmapValue.x + occlusion2 * mixmapValue.y;
    #else
        half occ = tex2D(_OcclusionMap, uv).g;
        occ = LerpOneTo (occ, _OcclusionStrength);
        return occ * mixmapValue.x + occlusion2 * mixmapValue.y; 
    #endif
    }

//  Overload when using combined map
    half Lux_Occlusion(half2 mixmapValue, half occ, half occlusion2)
    {
    #if (SHADER_TARGET < 30)
        // SM20: instruction count limitation
        // SM20: simpler occlusion
        return occ * mixmapValue.x + occlusion2 * mixmapValue.y;
    #else
        occ = LerpOneTo (occ, _OcclusionStrength);
        return occ * mixmapValue.x + occlusion2 * mixmapValue.y; 
    #endif
    }
#endif

// Get specular gloss ------------------------------------------------------------------------
half4 Lux_SpecularGloss(half2 mixmapValue, float4 uv)
{
    half4 sg;
    half4 sg2;
#ifdef _SPECGLOSSMAP
    sg = tex2D(_SpecGlossMap, uv.xy); 
#else
    sg = half4(_SpecColor.rgb, _Glossiness);
#endif

// mixmapping supports a second spec gloss value
#if defined (GEOM_TYPE_BRANCH_DETAIL)
    #if defined (GEOM_TYPE_FROND)
        sg2 = tex2D(_SpecGlossMap2, uv.zw);
    #else
        sg2 = half4(_SpecColor2.rgb, _Glossiness2);
    #endif
    sg = sg * mixmapValue.x + sg2 * mixmapValue.y;
#endif
    return sg;
}

// Get metallic gloss ------------------------------------------------------------------------
half2 Lux_MetallicGloss(half2 mixmapValue, float4 uv)
{
    half2 mg;
    half2 mg2;
#ifdef _METALLICGLOSSMAP
    mg = tex2D(_MetallicGlossMap, uv.xy).ra;
#else
    mg = half2(_Metallic, _Glossiness);
#endif

// mixmapping supports a second spec gloss value
#if defined (GEOM_TYPE_BRANCH_DETAIL)
    #if defined (GEOM_TYPE_FROND)
        mg2 = tex2D(_MetallicGlossMap2, uv.zw).ra;
    #else
        mg2 = half2(_Metallic2, _Glossiness2);
    #endif
    mg = mg * mixmapValue.x + mg2 * mixmapValue.y;
#endif
    return mg;
}


// -------------------------------------------------------------------------------------
#endif