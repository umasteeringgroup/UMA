//-----------------------------------------------------------------------------
// Helper functions 
//-----------------------------------------------------------------------------

float RoughnessToPerceptualRoughness(float roughness)
{
    return sqrt(roughness);
}

float RoughnessToPerceptualSmoothness(float roughness)
{
    return 1.0 - sqrt(roughness);
}

float PerceptualSmoothnessToRoughness(float perceptualSmoothness)
{
    return (1.0 - perceptualSmoothness) * (1.0 - perceptualSmoothness);
}

float PerceptualSmoothnessToPerceptualRoughness(float perceptualSmoothness)
{
    return (1.0 - perceptualSmoothness);
}

float PerceptualRoughnessToPerceptualSmoothness(float perceptualRoughness)
{
    return (1.0 - perceptualRoughness);
}

// Return modified perceptualSmoothness based on provided variance (get from GeometricNormalVariance + TextureNormalVariance)
float NormalFiltering(float perceptualSmoothness, float variance, float threshold)
{
    float roughness = PerceptualSmoothnessToRoughness(perceptualSmoothness);
    // Ref: Geometry into Shading - http://graphics.pixar.com/library/BumpRoughness/paper.pdf - equation (3)
    float squaredRoughness = saturate(roughness * roughness + min(2.0 * variance, threshold * threshold)); // threshold can be really low, square the value for easier control

    return RoughnessToPerceptualSmoothness(sqrt(squaredRoughness));
}

// Reference: Error Reduction and Simplification for Shading Anti-Aliasing
// Specular antialiasing for geometry-induced normal (and NDF) variations: Tokuyoshi / Kaplanyan et al.'s method.
// This is the deferred approximation, which works reasonably well so we keep it for forward too for now.
// screenSpaceVariance should be at most 0.5^2 = 0.25, as that corresponds to considering
// a gaussian pixel reconstruction kernel with a standard deviation of 0.5 of a pixel, thus 2 sigma covering the whole pixel.
float GeometricNormalVariance(float3 geometricNormalWS, float screenSpaceVariance)
{
    float3 deltaU = ddx(geometricNormalWS);
    float3 deltaV = ddy(geometricNormalWS);

    return screenSpaceVariance * (dot(deltaU, deltaU) + dot(deltaV, deltaV));
}

// Return modified perceptualSmoothness
float GeometricNormalFiltering(float perceptualSmoothness, float3 geometricNormalWS, float screenSpaceVariance, float threshold)
{
    float variance = GeometricNormalVariance(geometricNormalWS, screenSpaceVariance);
    return NormalFiltering(perceptualSmoothness, variance, threshold);
}

//SSS method from GDC 2011 conference by Colin Barre-Bresebois & Marc Bouchard and modified by Xiexe
float3 getSubsurfaceScatteringLight (float3 lightColor, float3 lightDirection, float3 normalDirection, float3 viewDirection, 
	float attenuation, float3 thickness, float3 indirectLight, float3 subsurfaceColour)
{
	float3 vLTLight = lightDirection + normalDirection * _SSSDist; // Distortion
	float3 fLTDot = pow(saturate(dot(viewDirection, -vLTLight)), _SSSPow) 
		* _SSSIntensity; 
	
	return lerp(1, attenuation, float(any(_WorldSpaceLightPos0.xyz))) 
				* (fLTDot + _SSSAmbient) * abs(_ThicknessMapInvert-thickness)
				* (lightColor + indirectLight) * subsurfaceColour;
				
}

inline float3 BlendNormalsPD(float3 n1, float3 n2) {
	return normalize(float3(n1.xy*n2.z + n2.xy*n1.z, n1.z*n2.z));
}

// Based on NormalInTangentSpace from UnityStandardInput
inline float3 NormalInTangentSpace(float2 texcoords, float2 texcoords2, half mask)
{
	//float3 normalTangent = UnpackNormal(tex2D(_BumpMap,TRANSFORM_TEX(texcoords.xy, _MainTex)));
	//float3 normalTangent = UnpackNormal(tex2D(_BumpMap,texcoords.xy));
	half3 normalTangent = UnpackScaleNormal(tex2D (_BumpMap, texcoords.xy), _BumpScale);

    half3 detailNormalTangent = UnpackScaleNormal(tex2D (_DetailBumpMap, TRANSFORM_TEX(texcoords2.xy, _DetailBumpMap)), _DetailBumpMapScale);
    #if _DETAIL_LERP
        normalTangent = lerp(
            normalTangent,
            detailNormalTangent,
            mask);
    #else
        normalTangent = lerp(
            normalTangent,
            BlendNormalsPD(normalTangent, detailNormalTangent),
            mask);
    #endif

    return normalTangent;
}