//#ifndef UNITY_ANISOTROPIC_BRDF_INCLUDED
//#define UNITY_ANISOTROPIC_BRDF_INCLUDED

// Anisotropic GGX
// From HDRenderPipeline
float D_GGXAnisotropic(float TdotH, float BdotH, float NdotH, float roughnessT, float roughnessB)
{
	float f = TdotH * TdotH / (roughnessT * roughnessT) + BdotH * BdotH / (roughnessB * roughnessB) + NdotH * NdotH;
	return 1.0 / (roughnessT * roughnessB * f * f);
}

// Smith Joint GGX Anisotropic Visibility
// Taken from https://cedec.cesa.or.jp/2015/session/ENG/14698.html
float SmithJointGGXAnisotropic(float TdotV, float BdotV, float NdotV, float TdotL, float BdotL, float NdotL, float roughnessT, float roughnessB)
{
	float aT = roughnessT;
	float aT2 = aT * aT;
	float aB = roughnessB;
	float aB2 = aB * aB;

	float lambdaV = NdotL * sqrt(aT2 * TdotV * TdotV + aB2 * BdotV * BdotV + NdotV * NdotV);
	float lambdaL = NdotV * sqrt(aT2 * TdotL * TdotL + aB2 * BdotL * BdotL + NdotL * NdotL);

	return 0.5 / (lambdaV + lambdaL);
}

// Convert Anistropy to roughness
void ConvertAnisotropyToRoughness(float roughness, float anisotropy, out float roughnessT, out float roughnessB)
{
	// (0 <= anisotropy <= 1), therefore (0 <= anisoAspect <= 1)
	// The 0.9 factor limits the aspect ratio to 10:1.
	float anisoAspect = sqrt(1.0 - 0.9 * anisotropy);
	roughnessT = roughness / anisoAspect; // Distort along tangent (rougher)
	roughnessB = roughness * anisoAspect; // Straighten along bitangent (smoother)
}

// Schlick Fresnel
float FresnelSchlick(float f0, float f90, float u)
{
	float x = 1.0 - u;
	float x5 = x * x;
	x5 = x5 * x5 * x;
	return (f90 - f0) * x5 + f0; // sub mul mul mul sub mad
}

//Clamp roughness
float ClampRoughnessForAnalyticalLights(float roughness)
{
	return max(roughness, 0.000001);
}

// Ref: Donald Revie - Implementing Fur Using Deferred Shading (GPU Pro 2)
// The grain direction (e.g. hair or brush direction) is assumed to be orthogonal to the normal.
// The returned normal is NOT normalized.
float3 ComputeGrainNormal(float3 grainDir, float3 V)
{
	float3 B = cross(-V, grainDir);
	return cross(B, grainDir);
}

//Modify Normal for Anisotropic IBL (Realtime version)
// Fake anisotropic by distorting the normal.
// The grain direction (e.g. hair or brush direction) is assumed to be orthogonal to N.
// Anisotropic ratio (0->no isotropic; 1->full anisotropy in tangent direction)
float3 GetAnisotropicModifiedNormal(float3 grainDir, float3 N, float3 V, float anisotropy)
{
	float3 grainNormal = ComputeGrainNormal(grainDir, V);
	// TODO: test whether normalizing 'grainNormal' is worth it.
	return normalize(lerp(N, grainNormal, anisotropy));
}

float4 AnisotropicBRDF(float3 diffColor, float3 specColor, float oneMinusReflectivity, float smoothness, float3 normal, float3x3 worldVectors,
	float anisotropy, float metallic, float3 viewDir, UnityLight light, UnityIndirect gi)
{
	//Unpack world vectors
	float3 tangent = worldVectors[0];
	float3 bitangent = worldVectors[1];
	//Normal shift
	float shiftAmount = dot(normal, viewDir);
	normal = shiftAmount < 0.0f ? normal + viewDir * (-shiftAmount + 1e-5f) : normal;
	//Regular vectors
	float NdotL = saturate(dot(normal, light.dir));
	float NdotV = abs(dot(normal, viewDir));
	float LdotV = dot(light.dir, viewDir);
	float3 H = Unity_SafeNormalize(light.dir + viewDir);
	float invLenLV = rsqrt(abs(2 + 2 * normalize(LdotV)));
	float NdotH = saturate(dot(normal, H));
	float LdotH = saturate(dot(light.dir, H));
	//Tangent vectors
	float TdotH = dot(tangent, H);
	float TdotL = dot(tangent, light.dir);
	float BdotH = dot(bitangent, H);
	float BdotL = dot(bitangent, light.dir);
	float TdotV = dot(viewDir, tangent);
	float BdotV = dot(viewDir, bitangent);
	//Fresnels
	half grazingTerm = saturate(smoothness + (1 - oneMinusReflectivity));
	float3 F = FresnelLerp(specColor, grazingTerm, NdotV); //Original Schlick - Replace from SRP?
														   //float3 fresnel0 = lerp(specColor, diffColor, metallic);
														   //float3 F = FresnelSchlick(fresnel0, 1.0, LdotH);
														   //Calculate roughness
	float roughnessT;
	float roughnessB;
	float perceptualRoughness = SmoothnessToPerceptualRoughness(smoothness);
	float roughness = PerceptualRoughnessToRoughness(perceptualRoughness);
	ConvertAnisotropyToRoughness(roughness, anisotropy, roughnessT, roughnessB);
	//Clamp roughness
	roughnessT = ClampRoughnessForAnalyticalLights(roughnessT);
	roughnessB = ClampRoughnessForAnalyticalLights(roughnessB);
	//Visibility & Distribution terms
	float V = SmithJointGGXAnisotropic(TdotV, BdotV, NdotV, TdotL, BdotL, NdotL, roughnessT, roughnessB);
	float D = D_GGXAnisotropic(TdotH, BdotH, NdotH, roughnessT, roughnessB);
	//Specular term
	float3 specularTerm = V * D;
#	ifdef UNITY_COLORSPACE_GAMMA
	specularTerm = sqrt(max(1e-4h, specularTerm));
#	endif
	// specularTerm * nl can be NaN on Metal in some cases, use max() to make sure it's a sane value
	specularTerm = max(0, specularTerm * NdotL);
#if defined(_SPECULARHIGHLIGHTS_OFF)
	specularTerm = 0.0;
#endif
	//Diffuse term
	float diffuseTerm = DisneyDiffuse(NdotV, NdotL, LdotH, perceptualRoughness) * NdotL;// - Need this NdotL multiply?
																						//Reduction
	half surfaceReduction;
#	ifdef UNITY_COLORSPACE_GAMMA
	surfaceReduction = 1.0 - 0.28*roughness*perceptualRoughness;		// 1-0.28*x^3 as approximation for (1/(x^4+1))^(1/2.2) on the domain [0;1]
#	else
	surfaceReduction = 1.0 / (roughness*roughness + 1.0);			// fade \in [0.5;1]
#	endif
																	//Final
	half3 color = (diffColor * (gi.diffuse + light.color * diffuseTerm))
		+ specularTerm * light.color * FresnelTerm(specColor, LdotH)
		+ surfaceReduction * gi.specular * FresnelLerp(specColor, grazingTerm, NdotV);
	return half4(color, 1);
}

//#endif UNITY_ANISOTROPIC_BRDF_INCLUDED
