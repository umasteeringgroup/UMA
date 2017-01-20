// Upgrade NOTE: replaced '_LightMatrix0' with 'unity_WorldToLight'

#ifndef LUX_AREALIGHTS_INCLUDED
#define LUX_AREALIGHTS_INCLUDED

// Collection of outputs of the intermediate area lighting functions
struct LuxAreaLightDef {
	// calculated specular area lighting direction
	half3 specLightDir;
	// calculateddiffuse area lighting direction
	half3 diffLightDir;
	// calculated specular lighting normalization factor
	fixed energy;
};

// Adapted from https://www.shadertoy.com/view/ldfGWs
void Lux_CalcSphereLightToLight(
	inout LuxAreaLightDef areaLight,
	float3 worldPos,
	float3 lightPos,
	float3 eyeVec,
	half3 normal,
	float sphereRad,
	half3 lightDir,
	float roughness)
{
	half3 viewDir = -eyeVec;
	half3 r = reflect (viewDir, normal);

	float normalization = 1.0;
	float3 L = lightPos - worldPos;
	float invDistToLight = rsqrt( dot(L, L));

	// Energy conservation
	float sphereAngle = saturate(sphereRad * invDistToLight);
	float m_square = roughness / saturate(roughness + 0.5 * sphereAngle);
	normalization = m_square * m_square; 

	float3 centerToRay	= dot(L, r) * r - L;
	float3 closestPoint = L + centerToRay * saturate(sphereRad * rsqrt(dot(centerToRay, centerToRay)));

	areaLight.specLightDir = normalize(closestPoint);
	areaLight.diffLightDir = lightDir;
	areaLight.energy = normalization;
}

void Lux_CalcTubeLightToLight (
	inout LuxAreaLightDef areaLight,
	float3 worldPos,
	float3 lightPos,
	float3 eyeVec,
	half3 normal,
	float tubeRad,
	float lightLength,
	half3 lightDir,
	half3 lightAxisX,
	float roughness)
{

	half3 viewDir = -eyeVec;
	half3 r = reflect (viewDir, normal);

	float normalization = 1.0;
	float3 L = lightPos - worldPos;
	float invDistToLight = rsqrt(dot(L, L));

	float3 tubeStart = lightPos + lightAxisX * lightLength;
	float3 tubeEnd = lightPos - lightAxisX * lightLength;

//	///////////////
// 	Length
//  Energy conservation
//	We do not reduce energy according to length here
//	float lineAngle = saturate( lightLength * invDistToLight );
//	normalization = roughness / saturate( roughness + 0.5 * lineAngle );

	float3 L0 = tubeStart - worldPos;
	float3 L1 = tubeEnd - worldPos;
	float3 Ld = L1 - L0;
	float RoL0 = dot( r, L0 );
	float RoLd = dot( r, Ld );
	float L0oLd = dot( L0, Ld);
	float t = (RoL0 * RoLd - L0oLd) / (dot(Ld, Ld) - RoLd * RoLd);
	float3 closestPoint	= L0 + Ld * saturate(t);
	
//	///////////////
// 	Radius
	// Energy conservation
	float sphereAngle = saturate(tubeRad * invDistToLight);
	float m_square = roughness / saturate(roughness + 0.5 * sphereAngle);
	normalization *= m_square * m_square; 
	
	float3 centerToRay	= dot(closestPoint, r) * r - closestPoint;
	closestPoint = closestPoint + centerToRay * saturate(tubeRad * rsqrt(dot(centerToRay, centerToRay)));

	float3 diffLightDir = L - clamp(dot(L, lightAxisX), -lightLength, lightLength) * lightAxisX;
	half invDistToDiffLightDir = rsqrt(dot(diffLightDir, diffLightDir));
	
	areaLight.specLightDir = normalize(closestPoint);
	areaLight.diffLightDir = diffLightDir * invDistToDiffLightDir;
	areaLight.energy = normalization;
}

void Lux_AreaLight (
	
	// Inouts for specular lighting
	inout UnityLight light,
	inout half specularIntensity,
	
	// Inouts for diffuse lighting
	inout half3 diffuseLightDir,
	inout half ndotlDiffuse,
	
	half3 lightDir,
	float lightColorAlpha,
	// light pos in world space
	float3 lightPos,
	// frag position in world space
	float3 worldPos,				// no problem in deferred, needs a custom surface parameter in forward surface shaders
	half3 viewDir,
	fixed3 normal,
	fixed3 diffuseNormal,
	half roughness
) {
	#if defined(POINT) || defined(SPOT)
		diffuseLightDir = lightDir;	// set up front to make it compile
		
		// Area Spot and Point lights
		UNITY_BRANCH
        if (lightColorAlpha > 8.0f) {

        	float decodeLightData = floor(lightColorAlpha) / 2048.0;
            const half lightRadius = floor(decodeLightData) / 2047.0 * 80;
            const half lightLength = frac(decodeLightData) * 40;
            specularIntensity = frac(lightColorAlpha) * 2.0;

            LuxAreaLightDef areaLight;
            UNITY_INITIALIZE_OUTPUT(LuxAreaLightDef, areaLight);

			roughness *= roughness;
			roughness = max(0.05, roughness);

			#if defined(POINT)
				//	Tube Light			
				UNITY_BRANCH
				if(lightLength > 0) {
					float3 lightAxisX = normalize(unity_WorldToLight[1].xyz);
					Lux_CalcTubeLightToLight(areaLight, worldPos, lightPos, viewDir, normal, lightRadius, lightLength, lightDir, lightAxisX, roughness);
				}
				//	Sphere Light
				UNITY_BRANCH
				if (lightLength == 0) {
					Lux_CalcSphereLightToLight(areaLight, worldPos, lightPos, viewDir, normal, lightRadius, lightDir, roughness);
				}
			#endif
			
			#if defined(SPOT)
				//	Sphere Light
				Lux_CalcSphereLightToLight(areaLight, worldPos, lightPos, viewDir, normal, lightRadius, lightDir, roughness);
			#endif

			light.dir = areaLight.specLightDir; 
			light.ndotl = LambertTerm (normal, light.dir);
			specularIntensity *= areaLight.energy;
			diffuseLightDir = areaLight.diffLightDir;
			ndotlDiffuse = LambertTerm (diffuseNormal, areaLight.diffLightDir);	
        }

		// Regular Spot and Point lights
		UNITY_BRANCH
		if (lightColorAlpha <= 8.0f) {
        	specularIntensity = saturate(lightColorAlpha);
			ndotlDiffuse = LambertTerm(diffuseNormal, lightDir);
        }
	#else
	// Directional Light
		diffuseLightDir = lightDir;
		ndotlDiffuse = LambertTerm(diffuseNormal, lightDir);
	#endif

}
#endif