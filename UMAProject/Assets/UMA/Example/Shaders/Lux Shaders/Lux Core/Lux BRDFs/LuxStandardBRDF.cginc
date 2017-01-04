#ifndef LUX_STANDARD_BRDF_INCLUDED
#define LUX_STANDARD_BRDF_INCLUDED

#include "UnityCG.cginc"
#include "UnityStandardConfig.cginc"
#include "UnityLightingCommon.cginc"

//-------------------------------------------------------------------------------------
// Standard BRDF for Unity > 5.3 
// and Unity > 5.5

	half4 Lux_BRDF1_PBS (half3 diffColor, half3 specColor, half oneMinusReflectivity, half smoothness/*oneMinusRoughness*/,
		half3 normal, half3 viewDir,
		// Lux
		half3 halfDir, half nh, half nv, half lv, half lh, half nl_diffuse,
		UnityLight light, UnityIndirect gi,
		// Lux
		half specularIntensity,
		half shadow)
	{

//	unity 5.5.
		#if UNITY_VERSION >= 550
			half perceptualRoughness = SmoothnessToPerceptualRoughness (smoothness);
			half roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
		#else
			half roughness = 1-smoothness;
		#endif

		light.color *= shadow;
		half nl = light.ndotl;

		// BRDF expects all other inputs to be calculated up front!
		#if defined (UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
			halfDir = Unity_SafeNormalize (light.dir + viewDir);
			nh = BlinnTerm (normal, halfDir);
			nv = DotClamped (normal, viewDir);
			lv = DotClamped (light.dir, viewDir);
			lh = DotClamped (light.dir, halfDir);
		#endif
		
	#if UNITY_BRDF_GGX
		half V = SmithJointGGXVisibilityTerm (nl, nv, roughness);
		half D = GGXTerm (nh, roughness) * specularIntensity;
	#else
		half V = SmithBeckmannVisibilityTerm (nl, nv, roughness);
//	unity 5.5.
		#if UNITY_VERSION >= 550
			half D = NDFBlinnPhongNormalizedTerm (nh, PerceptualRoughnessToSpecPower(perceptualRoughness));
		#else
			half D = NDFBlinnPhongNormalizedTerm (nh, RoughnessToSpecPower (roughness)) * specularIntensity;
		#endif
	#endif

//	unity 5.5.
	#if UNITY_VERSION >= 550
		half diffuseTerm = DisneyDiffuse(nv, nl_diffuse, lh, perceptualRoughness) * nl_diffuse;
	#else
		half nlPow5 = Pow5 (1-nl_diffuse);
		half nvPow5 = Pow5 (1-nv);
		half Fd90 = 0.5 + 2 * lh * lh * roughness;
		half disneyDiffuse = (1 + (Fd90-1) * nlPow5) * (1 + (Fd90-1) * nvPow5);
		half diffuseTerm = disneyDiffuse * nl_diffuse;
	#endif
		
		// HACK: theoretically we should divide by Pi diffuseTerm and not multiply specularTerm!
		// BUT 1) that will make shader look significantly darker than Legacy ones
		// and 2) on engine side "Non-important" lights have to be divided by Pi to in cases when they are injected into ambient SH
		// NOTE: multiplication by Pi is part of single constant together with 1/4 now

//	unity 5.5.
		#if UNITY_VERSION >= 550
			half specularTerm = V * D * UNITY_PI; // Torrance-Sparrow model
			#ifdef UNITY_COLORSPACE_GAMMA
				specularTerm = sqrt(max(1e-4h, specularTerm));
			#endif
			// specularTerm * nl can be NaN on Metal in some cases, use max() to make sure it's a sane value
			specularTerm = max(0, specularTerm * nl);
			#if defined(_SPECULARHIGHLIGHTS_OFF)
				specularTerm = 0.0;
			#endif
		#else
			half specularTerm = (V * D) * (UNITY_PI/4); // Torrance-Sparrow model
			if (IsGammaSpace())
				specularTerm = sqrt(max(1e-4h, specularTerm));
			specularTerm = max(0, specularTerm * nl);
		#endif
//



	#if LUX_LAZAROV_ENVIRONMENTAL_BRDF
		const half4 c0 = { -1, -0.0275, -0.572, 0.022 };
		const half4 c1 = { 1, 0.0425, 1.04, -0.04 };
		half4 r = (1-oneMinusRoughness) * c0 + c1;
		half a004 = min( r.x * r.x, exp2( -9.28 * nv ) ) * r.x + r.y;
		half2 AB = half2( -1.04, 1.04 ) * a004 + r.zw;
		half3 F_L = specColor * AB.x + AB.y;
	#else
//	unity 5.5.
		#if UNITY_VERSION >= 550
				half surfaceReduction;
			#ifdef UNITY_COLORSPACE_GAMMA
				surfaceReduction = 1.0-0.28*roughness*perceptualRoughness;		// 1-0.28*x^3 as approximation for (1/(x^4+1))^(1/2.2) on the domain [0;1]
			#else
				surfaceReduction = 1.0 / (roughness*roughness + 1.0);			// fade \in [0.5;1]
			#endif
		#else
			// surfaceReduction = Int D(NdotH) * NdotH * Id(NdotL>0) dH = 1/(realRoughness^2+1)
			half realRoughness = roughness*roughness;		// need to square perceptual roughness
			half surfaceReduction;
			if (IsGammaSpace()) surfaceReduction = 1.0 - 0.28*realRoughness*roughness;		// 1-0.28*x^3 as approximation for (1/(x^4+1))^(1/2.2) on the domain [0;1]
			else surfaceReduction = 1.0 / (realRoughness*realRoughness + 1.0);			// fade \in [0.5;1]
		#endif
		half grazingTerm = saturate(smoothness + (1-oneMinusReflectivity));
	#endif

	    half3 color = diffColor * (gi.diffuse + light.color * diffuseTerm)
	                    + specularTerm * light.color * FresnelTerm (specColor, lh)
					#if LUX_LAZAROV_ENVIRONMENTAL_BRDF
						+ gi.specular * F_L;
					#else
						+ surfaceReduction * gi.specular * FresnelLerp(specColor, grazingTerm, nv);
					#endif

		return half4(color, 1);
	}

#endif // LUX_STANDARD_BRDF_INCLUDED
