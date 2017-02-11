// Upgrade NOTE: replaced '_CameraToWorld' with 'unity_CameraToWorld'

Shader "Hidden/Lux Pro Internal-DeferredReflections" {
Properties {
	_SrcBlend ("", Float) = 1
	_DstBlend ("", Float) = 1
}
SubShader {

// Calculates reflection contribution from a single probe (rendered as cubes) or default reflection (rendered as full screen quad)
Pass {
	ZWrite Off
	ZTest LEqual
	Blend [_SrcBlend] [_DstBlend]
CGPROGRAM
#pragma target 3.0
#pragma vertex vert_deferred
#pragma fragment frag

#include "UnityCG.cginc"
#include "UnityDeferredLibrary.cginc"
#include "UnityStandardUtils.cginc"
#include "UnityStandardBRDF.cginc"
#include "UnityPBSLighting.cginc"

#include "../Lux Config.cginc"

sampler2D _CameraGBufferTexture0;
sampler2D _CameraGBufferTexture1;
sampler2D _CameraGBufferTexture2;

half3 distanceFromAABB(half3 p, half3 aabbMin, half3 aabbMax)
{
	return max(max(p - aabbMax, aabbMin - p), half3(0.0, 0.0, 0.0));
}


half4 frag (unity_v2f_deferred i) : SV_Target
{
	// Stripped from UnityDeferredCalculateLightParams, refactor into function ?
	i.ray = i.ray * (_ProjectionParams.z / i.ray.z);
	float2 uv = i.uv.xy / i.uv.w;

	// read depth and reconstruct world position
	float depth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv);
	depth = Linear01Depth (depth);
	float4 viewPos = float4(i.ray * depth,1);
	float3 worldPos = mul (unity_CameraToWorld, viewPos).xyz;

	half4 gbuffer0 = tex2D (_CameraGBufferTexture0, uv);
	half4 gbuffer1 = tex2D (_CameraGBufferTexture1, uv);
	half4 gbuffer2 = tex2D (_CameraGBufferTexture2, uv);

	half3 worldNormal = gbuffer2.rgb * 2 - 1;
	half3 worldNormalRefl;
	half oneMinusRoughness = gbuffer1.a;
	half3 specColor = gbuffer1.rgb;

	float3 eyeVec = normalize(worldPos - _WorldSpaceCameraPos);

	worldNormalRefl = reflect(eyeVec, normalize(worldNormal));
	half oneMinusReflectivity = 1 - SpecularStrength(specColor.rgb);
	half occlusion = gbuffer0.a;

//	/////////

	float blendDistance = unity_SpecCube1_ProbePosition.w; // will be set to blend distance for this probe
	#if UNITY_SPECCUBE_BOX_PROJECTION
		// For box projection, use expanded bounds as they are rendered; otherwise
		// box projection artifacts when outside of the box.
		float4 boxMin = unity_SpecCube0_BoxMin - float4(blendDistance,blendDistance,blendDistance,0);
		float4 boxMax = unity_SpecCube0_BoxMax + float4(blendDistance,blendDistance,blendDistance,0);
		half3 worldNormal0 = BoxProjectedCubemapDirection (worldNormalRefl, worldPos, unity_SpecCube0_ProbePosition, boxMin, boxMax);
	#else
		half3 worldNormal0 = worldNormalRefl;
	#endif

	Unity_GlossyEnvironmentData g;
	g.roughness		= 1 - oneMinusRoughness;
	g.reflUVW		= worldNormal0;

	half3 env0 = Unity_GlossyEnvironment (UNITY_PASS_TEXCUBE(unity_SpecCube0), unity_SpecCube0_HDR, g);

	UnityLight light;
	light.color = 0;
	light.dir = 0;
	light.ndotl = 0;

	UnityIndirect ind;
	ind.diffuse = 0;
	ind.specular = env0 * occlusion;


#if LUX_LAZAROV_ENVIRONMENTAL_BRDF 
//	Lazarov 2013, "Getting More Physical in Call of Duty: Black Ops II", changed by EPIC
	half dotNV = DotClamped(worldNormal, -eyeVec);
	const half4 c0 = { -1, -0.0275, -0.572, 0.022 };
	const half4 c1 = { 1, 0.0425, 1.04, -0.04 };
	half4 r = (1-oneMinusRoughness) * c0 + c1;
	half a004 = min( r.x * r.x, exp2( -9.28 * dotNV ) ) * r.x + r.y;
	half2 AB = half2( -1.04, 1.04 ) * a004 + r.zw;
	half3 F_L = specColor * AB.x + AB.y;
	half3 rgb = ind.specular * F_L;
#else
	half3 rgb = UNITY_BRDF_PBS (0, specColor, oneMinusReflectivity, oneMinusRoughness, worldNormal, -eyeVec, light, ind).rgb;
#endif


	// Calculate falloff value, so reflections on the edges of the probe would gradually blend to previous reflection.
	// Also this ensures that pixels not located in the reflection probe AABB won't
	// accidentally pick up reflections from this probe.
	half3 distance = distanceFromAABB(worldPos, unity_SpecCube0_BoxMin.xyz, unity_SpecCube0_BoxMax.xyz);
	half falloff = saturate(1.0 - length(distance)/blendDistance);

	return half4(rgb, falloff);
}

ENDCG
}

// Adds reflection buffer to the lighting buffer
Pass
{
	ZWrite Off
	ZTest Always
	Blend [_SrcBlend] [_DstBlend]

	CGPROGRAM
		#pragma target 3.0
		#pragma vertex vert
		#pragma fragment frag
		#pragma multi_compile ___ UNITY_HDR_ON

		#include "UnityCG.cginc"

		sampler2D _CameraReflectionsTexture;

		struct v2f {
			float2 uv : TEXCOORD0;
			float4 pos : SV_POSITION;
		};

		v2f vert (float4 vertex : POSITION)
		{
			v2f o;
			o.pos = mul(UNITY_MATRIX_MVP, vertex);
			o.uv = ComputeScreenPos (o.pos).xy;
			return o;
		}

		half4 frag (v2f i) : SV_Target
		{
			half4 c = tex2D (_CameraReflectionsTexture, i.uv);
			#ifdef UNITY_HDR_ON
			return float4(c.rgb, 0.0f);
			#else
			return float4(exp2(-c.rgb), 0.0f);
			#endif

		}
	ENDCG
}

}
Fallback Off
}
