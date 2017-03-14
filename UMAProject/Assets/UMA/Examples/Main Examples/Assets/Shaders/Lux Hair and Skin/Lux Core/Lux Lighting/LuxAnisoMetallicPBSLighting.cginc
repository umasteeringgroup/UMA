#ifndef LUX_ANISO_METALLIC_PBS_LIGHTING_INCLUDED
#define LUX_ANISO_METALLIC_PBS_LIGHTING_INCLUDED

#include "UnityShaderVariables.cginc"
#include "UnityStandardConfig.cginc"
#include "UnityLightingCommon.cginc"
#include "UnityGlobalIllumination.cginc"

#include "../Lux Core/Lux Lighting/LuxAreaLights.cginc"
#include "../Lux Core/Lux BRDFs/LuxAnisoBRDF.cginc"

//-------------------------------------------------------------------------------------
// Default BRDF to use:
#if !defined (UNITY_BRDF_PBS) // allow to explicitly override BRDF in custom shader
	// still add safe net for low shader models, otherwise we might end up with shaders failing to compile
	// the only exception is WebGL in 5.3 - it will be built with shader target 2.0 but we want it to get rid of constraints, as it is effectively desktop
	#if SHADER_TARGET < 30 && !UNITY_53_SPECIFIC_TARGET_WEBGL
		#define UNITY_BRDF_PBS BRDF3_Unity_PBS
	#elif UNITY_PBS_USE_BRDF3
		#define UNITY_BRDF_PBS BRDF3_Unity_PBS
	#elif UNITY_PBS_USE_BRDF2
		#define UNITY_BRDF_PBS BRDF2_Unity_PBS
	#elif UNITY_PBS_USE_BRDF1
		#define UNITY_BRDF_PBS BRDF1_Unity_PBS
	#elif defined(SHADER_TARGET_SURFACE_ANALYSIS)
		// we do preprocess pass during shader analysis and we dont actually care about brdf as we need only inputs/outputs
		#define UNITY_BRDF_PBS BRDF1_Unity_PBS
	#else
		#error something broke in auto-choosing BRDF
	#endif
#endif

//-------------------------------------------------------------------------------------
// BRDF for lights extracted from *indirect* directional lightmaps (baked and realtime).
// Baked directional lightmap with *direct* light uses UNITY_BRDF_PBS.
// For better quality change to BRDF1_Unity_PBS.
// No directional lightmaps in SM2.0.

#if !defined(UNITY_BRDF_PBS_LIGHTMAP_INDIRECT)
	#define UNITY_BRDF_PBS_LIGHTMAP_INDIRECT BRDF2_Unity_PBS
#endif
#if !defined (UNITY_BRDF_GI)
	#define UNITY_BRDF_GI BRDF_Unity_Indirect
#endif

//-------------------------------------------------------------------------------------

inline half3 BRDF_Unity_Indirect (half3 baseColor, half3 specColor, half oneMinusReflectivity, half oneMinusRoughness, half3 normal, half3 viewDir, half occlusion, UnityGI gi)
{
	half3 c = 0;
	#if defined(DIRLIGHTMAP_SEPARATE)
		gi.indirect.diffuse = 0;
		gi.indirect.specular = 0;

		#ifdef LIGHTMAP_ON
			c += UNITY_BRDF_PBS_LIGHTMAP_INDIRECT (baseColor, specColor, oneMinusReflectivity, oneMinusRoughness, normal, viewDir, gi.light2, gi.indirect).rgb * occlusion;
		#endif
		#ifdef DYNAMICLIGHTMAP_ON
			c += UNITY_BRDF_PBS_LIGHTMAP_INDIRECT (baseColor, specColor, oneMinusReflectivity, oneMinusRoughness, normal, viewDir, gi.light3, gi.indirect).rgb * occlusion;
		#endif
	#endif
	return c;
}

//-------------------------------------------------------------------------------------

// little helpers for GI calculation

#define UNITY_GLOSSY_ENV_FROM_SURFACE(x, s, data)						\
	Unity_GlossyEnvironmentData g;										\
	g.roughness		= 1 - s.Smoothness;									\
	g.reflUVW		= reflect( -data.worldViewDir, s.TangentDir); 		\
	// g.reflUVW		= reflect(-data.worldViewDir, normalize (s.Normal + s.worldTangentDir * 0.5) );
	//s.worldTangentDir * half3(1, -1, 1));	\


// g.roughness		= 1 - s.Smoothness;	
// g.reflUVW		= reflect(-data.worldViewDir, s.Normal);	\


#if defined(UNITY_PASS_DEFERRED) && UNITY_ENABLE_REFLECTION_BUFFERS
	#define UNITY_GI(x, s, data) x = UnityGlobalIllumination (data, s.Occlusion, s.Normal);
#else
	#define UNITY_GI(x, s, data) 								\
		UNITY_GLOSSY_ENV_FROM_SURFACE(g, s, data);				\
		x = UnityGlobalIllumination (data, s.Occlusion, s.Normal, g);
#endif

//-------------------------------------------------------------------------------------

// Surface shader output structure to be used with physically
// based shading model.

//-------------------------------------------------------------------------------------
// Metallic workflow

struct SurfaceOutputLuxAnisoMetallic
{
	fixed3 Albedo;			// diffuse color
	fixed3 Normal;			// tangent space normal, if written
	half3 Emission;
	half Metallic;			// 0=non-metal, 1=metal
	half Smoothness;		// 0=rough, 1=smooth
	half Occlusion;			// occlusion (default 1)
	fixed Alpha;			// alpha for transparencies
	
	fixed Shadow;
	fixed Translucency;		// as we use anisotropic lighting also for hair / it is either on (1.0) or off (0.0)
	float3 worldPosition;	// as it is needed by area lights
	float3 worldTangentDir;	// as it is needed by aniso lighting ???????????
	half3 TangentDir;		// as it is needed by aniso lighting
	//half SmoothnessX;
	//half SmoothnessY;
};

//half _SmoothnessX;
//half _SmoothnessY;

half4 _Lux_Anisotropic_Settings; // x: bump distortion, y: power, z: 1.0 - shadow Strength

#if !defined(LUX_STANDARD_CORE_INCLUDED)
	sampler2D _TangentDir;
	half _TangentDirStrength;
	half3 _BaseTangentDir;
	fixed _Translucency;
#endif

//	//////////////////////////////

inline half4 LightingLuxAnisoMetallic (SurfaceOutputLuxAnisoMetallic s, half3 viewDir, UnityGI gi)
{
	s.Normal = normalize(s.Normal);

	// energy conservation
	half oneMinusReflectivity;
	half3 specColor;
	s.Albedo = DiffuseAndSpecularFromMetallic (s.Albedo, s.Metallic, /*out*/ specColor, /*out*/ oneMinusReflectivity);

// ??? aber dann brauchen wir metallic nicht mehr als trans maske...
fixed3 diffColor = s.Albedo;

	// shader relies on pre-multiply alpha-blend (_SrcBlend = One, _DstBlend = OneMinusSrcAlpha)
	// this is necessary to handle transparency in physically correct way - only diffuse component gets affected by alpha
	half outputAlpha;



	s.Albedo = PreMultiplyAlpha (s.Albedo, s.Alpha, oneMinusReflectivity, /*out*/ outputAlpha);

//	///////////////////////////////////////	
//	Lux 
	half specularIntensity = 1.0;
	fixed3 diffuseNormal = s.Normal;
	half3 diffuseLightDir = 0;
	half ndotlDiffuse = gi.light.ndotl;

//	///////////////////////////////////////	
//	Lux Area lights
	#if defined(LUX_AREALIGHTS)
		// NOTE: Forward needs other inputs than deferred
		Lux_AreaLight (gi.light, specularIntensity, diffuseLightDir, ndotlDiffuse, gi.light.dir, _LightColor0.a, _WorldSpaceLightPos0.xyz, s.worldPosition, viewDir, s.Normal, diffuseNormal, 1.0 - s.Smoothness);
	#else
		diffuseLightDir = gi.light.dir;
		ndotlDiffuse = gi.light.ndotl;
		// If area lights are disabled we still have to reduce specular intensity
		#if !defined(DIRECTIONAL) && !defined(DIRECTIONAL_COOKIE)
			specularIntensity = saturate(_LightColor0.a);
		#endif
	#endif
//	///////////////////////////////////////	
//	Direct lighting uses the Lux BRDF
	half4 c = Lux_ANISO_BRDF (s.Albedo, specColor, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir,
				half3(0, 0, 0), 0, 0, 0, 0,
				//s.worldTangentDir, 1.0 - s.SmoothnessX, 1.0 - s.SmoothnessY,
				s.worldTangentDir, 1.0 - s.Smoothness, 1.0,
				ndotlDiffuse,
				gi.light, gi.indirect, specularIntensity, s.Shadow);
//	///////////////////////////////////////
	UNITY_BRANCH
	if (s.Translucency > 0) {
		// https://colinbarrebrisebois.com/2012/04/09/approximating-translucency-revisited-with-simplified-spherical-gaussian/
		half3 transLightDir = diffuseLightDir + diffuseNormal * _Lux_Anisotropic_Settings.x;
		half transDot = dot( -transLightDir, viewDir );
		transDot = exp2(saturate(transDot) * _Lux_Anisotropic_Settings.y - _Lux_Anisotropic_Settings.y); // * s.Translucency;
		half shadowFactor = saturate(transDot) * _Lux_Anisotropic_Settings.z; // * s.Translucency;
		half3 lightScattering = transDot * gi.light.color * lerp(s.Shadow, 1, shadowFactor);
		c.rgb += lightScattering * diffColor * _Lux_Anisotropic_Settings.w * s.Metallic;
	}
	c.rgb += UNITY_BRDF_GI (s.Albedo, specColor, oneMinusReflectivity, s.Smoothness, s.Normal, viewDir, s.Occlusion, gi);
	c.a = outputAlpha;

	return c;
}

// ----------------------------------------------------------

inline void LightingLuxAnisoMetallic_GI (
	SurfaceOutputLuxAnisoMetallic s,
	UnityGIInput data,
	inout UnityGI gi)
{
	UNITY_GI(gi, s, data);
}

#endif
