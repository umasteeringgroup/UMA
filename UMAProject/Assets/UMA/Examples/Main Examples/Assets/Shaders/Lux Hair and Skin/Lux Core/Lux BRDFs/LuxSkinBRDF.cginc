#ifndef LUX_SKIN_BRDF_INCLUDED
#define LUX_SKIN_BRDF_INCLUDED

#include "UnityCG.cginc"
#include "UnityStandardConfig.cginc"
#include "UnityLightingCommon.cginc"

float _Power; // = 2;
float _Distortion; // = 0.1;
float _Scale; // = 2;
sampler2D _BRDFTex;
half4 _SubColor;

float4 _Lux_Skin_DeepSubsurface;
float2 _Lux_Skin_DistanceRange;

half3 BentNormalsDiffuseLighting(float3 normal, float3 blurredNormal, float3 L, float Curvature, sampler2D _LookUp, float nl, half shadow, float3 wpos)
{	
	float NdotLBlurredUnclamped = dot(blurredNormal, L) ;
	half3 diffuseLookUp = tex2Dlod( _LookUp, float4( (NdotLBlurredUnclamped * 0.5 + 0.5) , Curvature, 0, 0 ) );

	#if defined (LUX_LIGHTINGFADE)
		float fade = distance(_WorldSpaceCameraPos, wpos);
		fade = saturate( (_Lux_Skin_DistanceRange.x - fade) / _Lux_Skin_DistanceRange.y);
		return lerp( (nl * shadow).xxx, diffuseLookUp, fade ); // fixed for ps4
	#else
		return diffuseLookUp;
	#endif
}

half4 LUX_SKIN_BRDF (half3 diffColor, half3 specColor, half translucency, half oneMinusReflectivity, half oneMinusRoughness,
	half3 normal, half3 blurredNormalWorld, half3 viewDir,
	half3 halfDir, half nh, half nv, half lv, half lh,
	half3 diffuseLightDir,
	half dotNL_diffuse,
	fixed curvature,
	UnityLight light, UnityIndirect gi, half specularStrength, half shadow, float3 wpos)

{
	light.color *= shadow;

	half roughness = 1-oneMinusRoughness;
//	///////////////////////////
	half nl = light.ndotl;

	// BRDF expects all other inputs to be calculated up front!
	#if defined (UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
		halfDir = normalize(light.dir + viewDir);
		nh = BlinnTerm(normal, halfDir);
		nv = DotClamped(normal, viewDir);
		lv = DotClamped(light.dir, viewDir);
		lh = DotClamped(light.dir, halfDir);
	#endif

//	////////////////////////////////////////////////////////////
//	Skin Lighting

//	////////////////////////////////////////////////////////////
//	Diffuse Lighting
	half3 brdf = BentNormalsDiffuseLighting(normal, blurredNormalWorld, diffuseLightDir, curvature, _BRDFTex, nl, shadow, wpos);

//	////////////////////////////////////////////////////////////
//	Light Scattering
	// Only lights that cast shadows may add translucency
	#if defined(SHADOWS_DEPTH) || defined(SHADOWS_SCREEN) || defined(SHADOWS_CUBE)
		half3 transLightDir = diffuseLightDir + blurredNormalWorld * _Lux_Skin_DeepSubsurface.y;
		half transDot = dot( -transLightDir, viewDir );
		transDot = exp2(saturate(transDot) * _Lux_Skin_DeepSubsurface.x - _Lux_Skin_DeepSubsurface.x) * translucency * _Lux_Skin_DeepSubsurface.z;
		half3 lightScattering = transDot * _SubColor * light.color;
	#endif

//	////////////////////////////////////////////////////////////
//	Final composition

	half V = SmithJointGGXVisibilityTerm (nl, nv, roughness);
	half D = GGXTerm(nh, roughness) * specularStrength;					// kill specular
	half specularTerm = (V * D) * (UNITY_PI/4);
	if (IsGammaSpace())
		specularTerm = sqrt(max(1e-4h, specularTerm));
	specularTerm = max(0, specularTerm * nl);

#if LUX_LAZAROV_ENVIRONMENTAL_BRDF
	const half4 c0 = { -1, -0.0275, -0.572, 0.022 };
	const half4 c1 = { 1, 0.0425, 1.04, -0.04 };
	half4 r = (1-oneMinusRoughness) * c0 + c1;
	half a004 = min( r.x * r.x, exp2( -9.28 * nv ) ) * r.x + r.y;
	half2 AB = half2( -1.04, 1.04 ) * a004 + r.zw;
	half3 F_L = specColor * AB.x + AB.y;
#else
	// surfaceReduction = Int D(NdotH) * NdotH * Id(NdotL>0) dH = 1/(realRoughness^2+1)
	half realRoughness = roughness*roughness;		// need to square perceptual roughness
	half surfaceReduction;
	if (IsGammaSpace()) surfaceReduction = 1.0 - 0.28*realRoughness*roughness;		// 1-0.28*x^3 as approximation for (1/(x^4+1))^(1/2.2) on the domain [0;1]
	else surfaceReduction = 1.0 / (realRoughness*realRoughness + 1.0);			// fade \in [0.5;1]
	half grazingTerm = saturate(oneMinusRoughness + (1-oneMinusReflectivity));
#endif

	half3 color = diffColor * (gi.diffuse + light.color * brdf)							// diffuse lighting
	#if defined(SHADOWS_DEPTH) || defined(SHADOWS_SCREEN) || defined(SHADOWS_CUBE)
		+ lightScattering																// light scattering		
	#endif
		+ specularTerm * light.color * FresnelTerm (specColor, lh)						// direct specular
	#if LUX_LAZAROV_ENVIRONMENTAL_BRDF
		+ gi.specular * F_L;
	#else
		+ surfaceReduction * gi.specular * FresnelLerp(specColor, grazingTerm, nv); // from unity 5.4.b5
	#endif

	return half4(color, 1.0h);
}

#endif //LUX_SKIN_BRDF_INCLUDED