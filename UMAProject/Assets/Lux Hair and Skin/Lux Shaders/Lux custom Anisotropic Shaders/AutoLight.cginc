// Upgrade NOTE: replaced '_LightMatrix0' with 'unity_WorldToLight'
// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'
// Upgrade NOTE: replaced 'unity_World2Shadow' with 'unity_WorldToShadow'

// Lux hacked AutoLight
// We use the "UNITY_LIGHT_ATTENUATION" macro to write out needed worldPosition (Area Lights) and worldTangent (Anisotropic Highlights)
// to our output structure o that gets sent to the lighting function.
// Only needed by forward lighting

#ifndef AUTOLIGHT_INCLUDED
#define AUTOLIGHT_INCLUDED

#include "HLSLSupport.cginc"
#include "UnityShadowLibrary.cginc"

#if (SHADER_TARGET < 30) || defined(SHADER_API_MOBILE)
	// We prefer performance over quality on SM2.0 and Mobiles
	// mobile or SM2.0: half precision for shadow coords
	#if defined (SHADOWS_NATIVE)
		#define unityShadowCoord half
		#define unityShadowCoord2 half2
		#define unityShadowCoord3 half3
	#else
		#define unityShadowCoord float
		#define unityShadowCoord2 float2
		#define unityShadowCoord3 float3
	#endif	
#if defined(SHADER_API_PSP2)
	#define unityShadowCoord4 float4	// Vita PCF only works when using float4 with tex2Dproj, doesn't work with half4.
#else
	#define unityShadowCoord4 half4
#endif
	#define unityShadowCoord4x4 half4x4
#else
	#define unityShadowCoord float
	#define unityShadowCoord2 float2
	#define unityShadowCoord3 float3
	#define unityShadowCoord4 float4
	#define unityShadowCoord4x4 float4x4
#endif


// ----------------
//  Shadow helpers
// ----------------

// ---- Screen space shadows
#if defined (SHADOWS_SCREEN)


#define SHADOW_COORDS(idx1) unityShadowCoord4 _ShadowCoord : TEXCOORD##idx1;

#if defined(UNITY_NO_SCREENSPACE_SHADOWS)

UNITY_DECLARE_SHADOWMAP(_ShadowMapTexture);
#define TRANSFER_SHADOW(a) a._ShadowCoord = mul( unity_WorldToShadow[0], mul( unity_ObjectToWorld, v.vertex ) );

inline fixed unitySampleShadow (unityShadowCoord4 shadowCoord)
{
	#if defined(SHADOWS_NATIVE)

	fixed shadow = UNITY_SAMPLE_SHADOW(_ShadowMapTexture, shadowCoord.xyz);
	shadow = _LightShadowData.r + shadow * (1-_LightShadowData.r);
	return shadow;

	#else

	unityShadowCoord dist = SAMPLE_DEPTH_TEXTURE_PROJ(_ShadowMapTexture, shadowCoord);

	// tegra is confused if we use _LightShadowData.x directly
	// with "ambiguous overloaded function reference max(mediump float, float)"
	half lightShadowDataX = _LightShadowData.x;
	return max(dist > (shadowCoord.z/shadowCoord.w), lightShadowDataX);

	#endif
}

#else // UNITY_NO_SCREENSPACE_SHADOWS

sampler2D _ShadowMapTexture;
#define TRANSFER_SHADOW(a) a._ShadowCoord = ComputeScreenPos(a.pos);

inline fixed unitySampleShadow (unityShadowCoord4 shadowCoord)
{
	fixed shadow = tex2Dproj( _ShadowMapTexture, UNITY_PROJ_COORD(shadowCoord) ).r;
	return shadow;
}

#endif

#define SHADOW_ATTENUATION(a) unitySampleShadow(a._ShadowCoord)

#endif


// ---- Spot light shadows
#if defined (SHADOWS_DEPTH) && defined (SPOT)
	#define SHADOW_COORDS(idx1) unityShadowCoord4 _ShadowCoord : TEXCOORD##idx1;
	#define TRANSFER_SHADOW(a) a._ShadowCoord = mul (unity_WorldToShadow[0], mul(unity_ObjectToWorld,v.vertex));
	#define SHADOW_ATTENUATION(a) UnitySampleShadowmap(a._ShadowCoord)
#endif


// ---- Point light shadows
#if defined (SHADOWS_CUBE)
	#define SHADOW_COORDS(idx1) unityShadowCoord3 _ShadowCoord : TEXCOORD##idx1;
	#define TRANSFER_SHADOW(a) a._ShadowCoord = mul(unity_ObjectToWorld, v.vertex).xyz - _LightPositionRange.xyz;
	#define SHADOW_ATTENUATION(a) UnitySampleShadowmap(a._ShadowCoord)
#endif

// ---- Shadows off
#if !defined (SHADOWS_SCREEN) && !defined (SHADOWS_DEPTH) && !defined (SHADOWS_CUBE)
	#define SHADOW_COORDS(idx1)
	#define TRANSFER_SHADOW(a)
	#define SHADOW_ATTENUATION(a) 1.0
#endif


// ------------------------------
//  Light helpers (5.0+ version)
// ------------------------------

// This version depends on having worldPos available in the fragment shader and using that to compute light coordinates.

// If none of the keywords are defined, assume directional?
#if !defined(POINT) && !defined(SPOT) && !defined(DIRECTIONAL) && !defined(POINT_COOKIE) && !defined(DIRECTIONAL_COOKIE)
#define DIRECTIONAL
#endif


#ifdef POINT
uniform sampler2D _LightTexture0;
uniform unityShadowCoord4x4 unity_WorldToLight;
#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) \
	unityShadowCoord3 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xyz; \
	fixed destName = (tex2D(_LightTexture0, dot(lightCoord, lightCoord).rr).UNITY_ATTEN_CHANNEL); \
						o.worldPosition = worldPos; \
						half3 t = half3(IN.tSpace0.x, IN.tSpace1.x, IN.tSpace2.x); \
						half3 b = half3(IN.tSpace0.y, IN.tSpace1.y, IN.tSpace2.y); \
						half3 n = half3(IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z); \
						half3x3 tangent2World = half3x3(t, b, n); \
						o.worldTangentDir = normalize( mul(half3(o.TangentDir.xy, 0.0), tangent2World)); \
						o.TangentDir = normalize( mul(o.TangentDir, tangent2World) ); \
						o.Shadow = SHADOW_ATTENUATION(input);
#endif

#ifdef SPOT
uniform sampler2D _LightTexture0;
uniform unityShadowCoord4x4 unity_WorldToLight;
uniform sampler2D _LightTextureB0;
inline fixed UnitySpotCookie(unityShadowCoord4 LightCoord)
{
	return tex2D(_LightTexture0, LightCoord.xy / LightCoord.w + 0.5).w;
}
inline fixed UnitySpotAttenuate(unityShadowCoord3 LightCoord)
{
	return tex2D(_LightTextureB0, dot(LightCoord, LightCoord).xx).UNITY_ATTEN_CHANNEL;
}
#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) \
	unityShadowCoord4 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)); \
	fixed destName = (lightCoord.z > 0) * UnitySpotCookie(lightCoord) * UnitySpotAttenuate(lightCoord.xyz); \
						o.worldPosition = worldPos; \
						half3 t = half3(IN.tSpace0.x, IN.tSpace1.x, IN.tSpace2.x); \
						half3 b = half3(IN.tSpace0.y, IN.tSpace1.y, IN.tSpace2.y); \
						half3 n = half3(IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z); \
						half3x3 tangent2World = half3x3(t, b, n); \
						o.worldTangentDir = normalize( mul(half3(o.TangentDir.xy, 0.0), tangent2World)); \
						o.TangentDir = normalize( mul(o.TangentDir, tangent2World) ); \
						o.Shadow = SHADOW_ATTENUATION(input);
#endif

#ifdef DIRECTIONAL
	#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) \
						fixed destName = 1.0; \
						o.worldPosition = worldPos; \
						half3 t = half3(IN.tSpace0.x, IN.tSpace1.x, IN.tSpace2.x); \
						half3 b = half3(IN.tSpace0.y, IN.tSpace1.y, IN.tSpace2.y); \
						half3 n = half3(IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z); \
						half3x3 tangent2World = half3x3(t, b, n); \
						o.worldTangentDir = normalize( mul(half3(o.TangentDir.xy, 0.0), tangent2World)); \
						o.TangentDir = normalize( mul(o.TangentDir, tangent2World) ); \
						o.Shadow = SHADOW_ATTENUATION(input);
#endif

// o.TangentDir = normalize( mul(o.TangentDir, tangent2World) ); \

#ifdef POINT_COOKIE
uniform samplerCUBE _LightTexture0;
uniform unityShadowCoord4x4 unity_WorldToLight;
uniform sampler2D _LightTextureB0;
#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) \
	unityShadowCoord3 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xyz; \
	fixed destName = tex2D(_LightTextureB0, dot(lightCoord, lightCoord).rr).UNITY_ATTEN_CHANNEL * texCUBE(_LightTexture0, lightCoord).w; \
						o.worldPosition = worldPos; \
						half3 t = half3(IN.tSpace0.x, IN.tSpace1.x, IN.tSpace2.x); \
						half3 b = half3(IN.tSpace0.y, IN.tSpace1.y, IN.tSpace2.y); \
						half3 n = half3(IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z); \
						half3x3 tangent2World = half3x3(t, b, n); \
						o.worldTangentDir = normalize( mul(half3(o.TangentDir.xy, 0.0), tangent2World)); \
						o.TangentDir = normalize( mul(o.TangentDir, tangent2World) ); \
						o.Shadow = SHADOW_ATTENUATION(input);
#endif

#ifdef DIRECTIONAL_COOKIE
uniform sampler2D _LightTexture0;
uniform unityShadowCoord4x4 unity_WorldToLight;
#define UNITY_LIGHT_ATTENUATION(destName, input, worldPos) \
	unityShadowCoord2 lightCoord = mul(unity_WorldToLight, unityShadowCoord4(worldPos, 1)).xy; \
	fixed destName = tex2D(_LightTexture0, lightCoord).w; \
						o.worldPosition = worldPos; \
						half3 t = half3(IN.tSpace0.x, IN.tSpace1.x, IN.tSpace2.x); \
						half3 b = half3(IN.tSpace0.y, IN.tSpace1.y, IN.tSpace2.y); \
						half3 n = half3(IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z); \
						half3x3 tangent2World = half3x3(t, b, n); \
						o.worldTangentDir = normalize( mul(half3(o.TangentDir.xy, 0.0), tangent2World)); \
						o.TangentDir = normalize( mul(o.TangentDir, tangent2World) ); \
						o.Shadow = SHADOW_ATTENUATION(input);
#endif


// -----------------------------
//  Light helpers (4.x version)
// -----------------------------

// This version computes light coordinates in the vertex shader and passes them to the fragment shader.

#ifdef POINT
#define LIGHTING_COORDS(idx1,idx2) unityShadowCoord3 _LightCoord : TEXCOORD##idx1; SHADOW_COORDS(idx2)
#define TRANSFER_VERTEX_TO_FRAGMENT(a) a._LightCoord = mul(unity_WorldToLight, mul(unity_ObjectToWorld, v.vertex)).xyz; TRANSFER_SHADOW(a)
#define LIGHT_ATTENUATION(a)	(tex2D(_LightTexture0, dot(a._LightCoord,a._LightCoord).rr).UNITY_ATTEN_CHANNEL * SHADOW_ATTENUATION(a))
#endif

#ifdef SPOT
#define LIGHTING_COORDS(idx1,idx2) unityShadowCoord4 _LightCoord : TEXCOORD##idx1; SHADOW_COORDS(idx2)
#define TRANSFER_VERTEX_TO_FRAGMENT(a) a._LightCoord = mul(unity_WorldToLight, mul(unity_ObjectToWorld, v.vertex)); TRANSFER_SHADOW(a)
#define LIGHT_ATTENUATION(a)	( (a._LightCoord.z > 0) * UnitySpotCookie(a._LightCoord) * UnitySpotAttenuate(a._LightCoord.xyz) * SHADOW_ATTENUATION(a) )
#endif

#ifdef DIRECTIONAL
	#define LIGHTING_COORDS(idx1,idx2) SHADOW_COORDS(idx1)
	#define TRANSFER_VERTEX_TO_FRAGMENT(a) TRANSFER_SHADOW(a)
	#define LIGHT_ATTENUATION(a)	SHADOW_ATTENUATION(a)
#endif

#ifdef POINT_COOKIE
#define LIGHTING_COORDS(idx1,idx2) unityShadowCoord3 _LightCoord : TEXCOORD##idx1; SHADOW_COORDS(idx2)
#define TRANSFER_VERTEX_TO_FRAGMENT(a) a._LightCoord = mul(unity_WorldToLight, mul(unity_ObjectToWorld, v.vertex)).xyz; TRANSFER_SHADOW(a)
#define LIGHT_ATTENUATION(a)	(tex2D(_LightTextureB0, dot(a._LightCoord,a._LightCoord).rr).UNITY_ATTEN_CHANNEL * texCUBE(_LightTexture0, a._LightCoord).w * SHADOW_ATTENUATION(a))
#endif

#ifdef DIRECTIONAL_COOKIE
#define LIGHTING_COORDS(idx1,idx2) unityShadowCoord2 _LightCoord : TEXCOORD##idx1; SHADOW_COORDS(idx2)
#define TRANSFER_VERTEX_TO_FRAGMENT(a) a._LightCoord = mul(unity_WorldToLight, mul(unity_ObjectToWorld, v.vertex)).xy; TRANSFER_SHADOW(a)
#define LIGHT_ATTENUATION(a)	(tex2D(_LightTexture0, a._LightCoord).w * SHADOW_ATTENUATION(a))
#endif


#endif
