#ifndef LUX_ANISO_BRDF_INCLUDED
#define LUX_ANISO_BRDF_INCLUDED

#include "UnityCG.cginc"
#include "UnityStandardConfig.cginc"
#include "UnityLightingCommon.cginc"


// TODO specIntensity is too low ???????????

// http://blog.selfshadow.com/publications/s2013-shading-course/rad/s2013_pbs_rad_notes.pdf



	// -- helper for computing the GGX visibility term
	float GGX_V1(in float m2, in float nDotX)
	{
	    return 1.0f / (nDotX + sqrt(m2 + (1 - m2) * nDotX * nDotX));
	}


//	Brent, Burley, "Physically-Based Shading at Disney", 2012
//-------------------------------------------------------------------------------------

	half4 Lux_ANISO_BRDF (half3 diffColor, half3 specColor, half oneMinusReflectivity, half oneMinusRoughness, half3 normal, half3 viewDir,
		half3 halfDir, half nh, half nv, half lv, half lh,
		half3 worldTangentDir, half RoughnessX, half RoughnessY,
		half nl_diffuse,
		UnityLight light, UnityIndirect gi, half specularIntensity, half shadow)
	{

		light.color *= shadow;

		half nl = light.ndotl;
		// BRDF expects all other inputs to be calculated up front!
		#if defined (UNITY_PASS_FORWARDBASE) || defined(UNITY_PASS_FORWARDADD)
			halfDir = normalize (light.dir + viewDir);
			nh = BlinnTerm (normal, halfDir);
			nv = DotClamped (normal, viewDir);
			lv = DotClamped (light.dir, viewDir);
			lh = DotClamped (light.dir, halfDir);
		#endif

		half roughness = max(RoughnessX, RoughnessY);

		float3 X = worldTangentDir.xyz;
		float3 Y = cross(normal, worldTangentDir);

		float mx = RoughnessX * RoughnessX;
		float my = RoughnessY * RoughnessY;
		float XdotH = dot( X, halfDir );
		float YdotH = dot( Y, halfDir );
		float d = XdotH * XdotH / (mx * mx) + YdotH * YdotH / (my * my) + nh * nh;

		half D = 1 / ( UNITY_PI * mx*my * d*d); // Normalization is: 1/(pi*mx*my)
		half V = SmithJointGGXVisibilityTerm ( nl, nv, roughness); // <- As V is based on NdotL it i sprone to artifacts so it is currently skipped.


		half nlPow5 = Pow5 (1-nl_diffuse);
		half nvPow5 = Pow5 (1-nv);
		half Fd90 = 0.5 + 2 * lh * lh * roughness;
		half disneyDiffuse = (1 + (Fd90-1) * nlPow5) * (1 + (Fd90-1) * nvPow5);

		half diffuseTerm = disneyDiffuse * nl_diffuse;

		//diffuseTerm = (1.0 - sqrt(saturate(dot(worldTangentDir, light.dir)))) * nl_diffuse;		

		half specularTerm = (V * D); // * (UNITY_PI/4); // Torrance-Sparrow model, Fresnel is applied later (for optimization reasons)
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

		half3 color = diffColor * (gi.diffuse + light.color * diffuseTerm)
				+ specularTerm * light.color * FresnelTerm (specColor, lh) * specularIntensity
			#if LUX_LAZAROV_ENVIRONMENTAL_BRDF
					+ gi.specular * F_L;
			#else
				+ surfaceReduction * gi.specular * FresnelLerp(specColor, grazingTerm, nv); // from unity 5.4.b5
			#endif

		return half4(color, 1);
	}


#endif // LUX_ANISO_BRDF_INCLUDED
