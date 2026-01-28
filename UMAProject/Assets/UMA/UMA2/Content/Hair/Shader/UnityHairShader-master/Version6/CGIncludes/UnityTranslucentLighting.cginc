float _Distortion;
float _Scale;
float _Power;
float _Fresnel;
float _FresnelDamp;

float4 TranslucentBRDF(float3 diffColor, float3 specColor, float oneMinusReflectivity, float smoothness, float3 normal, float3x3 worldVectors,
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
		+ specularTerm * light.color * (FresnelTerm(specColor, LdotH))
		+ (surfaceReduction * gi.specular * FresnelLerp(specColor, grazingTerm, NdotV) * _Fresnel * lerp(float3(1,1,1),specColor,_FresnelDamp));
	return half4(color, 1);
}

inline half4 LightFunctionStandardAnisotropic(SurfaceOutputStandardAnisotropic s, half3 viewDir, UnityGI gi)
{
	s.Normal = normalize(s.Normal);

	half oneMinusReflectivity;
	half3 specColor;
	s.Albedo = DiffuseAndSpecularFromMetallic(s.Albedo, s.Metallic, /*out*/ specColor, /*out*/ oneMinusReflectivity);

	// shader relies on pre-multiply alpha-blend (_SrcBlend = One, _DstBlend = OneMinusSrcAlpha)
	// this is necessary to handle transparency in physically correct way - only diffuse component gets affected by alpha
	half outputAlpha;
	s.Albedo = PreMultiplyAlpha(s.Albedo, s.Alpha, oneMinusReflectivity, /*out*/ outputAlpha);

	half4 c = TranslucentBRDF(s.Albedo, specColor, oneMinusReflectivity, s.Smoothness, s.Normal, s.WorldVectors, s.Anisotropy, s.Metallic, viewDir, gi.light, gi.indirect);
	c.a = outputAlpha;
	return c;
}


inline fixed4 LightingStandardTranslucent(SurfaceOutputStandardAnisotropic s, fixed3 viewDir, UnityGI gi)
{
	// Original colour
	fixed4 pbr = LightFunctionStandardAnisotropic(s, viewDir, gi);
	
	// --- Translucency ---
	float3 L = gi.light.dir;
	float3 V = viewDir;
	float3 N = s.Normal;
 
	float3 H = normalize(L + N * _Distortion);
	float I = (pow(saturate(dot(V, -H)), _Power) * _Scale);
 
	// Final add
	pbr.rgb = pbr.rgb + gi.light.color * (I*s.Albedo);
	
	return pbr;
}

//This is pointless as always forward?
inline half4 LightingStandardTranslucent_Deferred(SurfaceOutputStandardAnisotropic s, half3 viewDir, UnityGI gi, out half4 outGBuffer0, out half4 outGBuffer1, out half4 outGBuffer2)
{
	half oneMinusReflectivity;
	half3 specColor;
	s.Albedo = DiffuseAndSpecularFromMetallic(s.Albedo, s.Metallic, /*out*/ specColor, /*out*/ oneMinusReflectivity);

	half4 c = TranslucentBRDF(s.Albedo, specColor, oneMinusReflectivity, s.Smoothness, s.Normal, s.WorldVectors, s.Anisotropy, s.Metallic, viewDir, gi.light, gi.indirect);

	UnityStandardData data;
	data.diffuseColor = s.Albedo;
	data.occlusion = s.Occlusion;
	data.specularColor = specColor;
	data.smoothness = s.Smoothness;
	data.normalWorld = s.Normal;

	UnityStandardDataToGbuffer(data, outGBuffer0, outGBuffer1, outGBuffer2);

	// --- Translucency ---
	float3 L = gi.light.dir;
	float3 V = viewDir;
	float3 N = s.Normal;
 
	float3 H = normalize(L + N * _Distortion);
	float I = (pow(saturate(dot(V, -H)), _Power) * _Scale);
	
	
	half4 emission = half4(s.Emission + c.rgb, 1);
	return emission * (float4(1,1,1,1) + float4(I * c.rgb,0));
}

inline void LightingStandardTranslucent_GI(SurfaceOutputStandardAnisotropic s, UnityGIInput data, inout UnityGI gi)
{
#if defined(UNITY_PASS_DEFERRED) && UNITY_ENABLE_REFLECTION_BUFFERS
	gi = UnityGlobalIllumination(data, s.Occlusion, s.Normal);
#else
	Unity_GlossyEnvironmentData g = UnityGlossyEnvironmentSetup(s.Smoothness, data.worldViewDir, s.Normal, lerp(unity_ColorSpaceDielectricSpec.rgb, s.Albedo, s.Metallic));
	gi = UnityAnisotropicGlobalIllumination(data, s.Occlusion, s.Normal, g, s.Anisotropy, s.WorldVectors);
#endif
}