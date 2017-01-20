// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Lux/Anisotropic Lighting/Hair" {
	Properties {
		_Color ("Color", Color) = (1,1,1,1)
		[Header(Basic Inputs)]
		[Space(3)]
		_MainTex ("Albedo (RGB)", 2D) = "white" {}

		[Space(3)]
		_Cutoff ("Cutoff", Range(0,1)) = 0.5
		[Toggle(_SPECGLOSSMAP)] _EnableDitheredOpacity("Enable dithered Opacity", Float) = 0.0

		[Space(3)]
		[NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}

		// Shader does not handle Metalic = 0.0 correctly. So we simply clamp the property.
		[Gamma] _Metallic("Metallic", Range(0.02, 1.0)) = 0.0
		// Smoothness must not go up to 1.0! So we simply clamp the property.
		_Glossiness ("Smoothness", Range(0,0.975)) = 0.5
		
		[Space(3)]
		[Toggle(_METALLICGLOSSMAP)] _EnableMetallGlossMap("Enable Metallic Occlusion Gloss Map", Float) = 0.0
		[NoScaleOffset] _MetallicGlossMap("Metallic (R) Occlusion (G) Smoothness (A)", 2D) = "white" {}
		[Space(3)]
        _VertexOcclusionStrength ("Vertex Color Occlusion", Range(0,1)) = 0.5	
		
		// Lux anisotropic Lighting properties
		[Header(Tangent Direction)]
		[Space(3)]
		[NoScaleOffset] _TangentDir ("Tangent (RG)", 2D) = "bump" {}
		_BaseTangentDir ("Base Tangent Direction (XYZ)", Vector) = (0.0,1.0,0.0,0.0)
		_TangentDirStrength ("Strength", Range(0,1)) = 1
	
		[Header(Hair specific lighting)]
		[Space(3)]
		// Lets you enable/disable translucent lighting
		[Lux_FloatToggleDrawer] _Translucency("Enable Translucent Lighting", Float) = 0.0
		[Space(3)]
		// Lux diffuse Scattering properties
        _DiffuseScatteringCol("Diffuse Scattering Color", Color) = (0,0,0,1)
        _DiffuseScatteringBias("Scatter Bias", Range(0.0, 0.5)) = 0.0
        _DiffuseScatteringContraction("Scatter Contraction", Range(1.0, 10.0)) = 8.0
        
        
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		Cull Off
		
		CGPROGRAM
		// We have to use "addshadow" due to vface
		#pragma surface surf LuxAnisoMetallic fullforwardshadows addshadow vertex:vert
		// exclude_path:forward
		#pragma multi_compile __ LUX_AREALIGHTS
		#pragma shader_feature _SPECGLOSSMAP
		#pragma shader_feature _METALLICGLOSSMAP

		#include "../Lux Core/Lux Config.cginc"
		#include "../Lux Core/Lux Lighting/LuxAnisoMetallicPBSLighting.cginc"
		#include "../Lux Core/Lux Features/LuxDiffuseScattering.cginc"
		#pragma target 3.0

		struct Input {
			float2 uv_MainTex;
			half3 viewDir;
			//float4 screenPos;			// Would need 11 texture interpolators in forward...
			float3 worldNormal;
			INTERNAL_DATA

			fixed4 color : COLOR0;
			//#if defined (UNITY_PASS_DEFERRED) // does not get handled correctly by the compiler / and we need it in forward too due to the packing
				half4 worldTangent_screenPosX;
				half4 worldBinormal_screenPosY;
			//#endif
			float FacingSign : FACE;	// Needed to correctly lit single sided geometry
		};

		fixed4 _Color;
		sampler2D _MainTex;
		sampler2D _BumpMap;
	//	#if defined (_METALLICGLOSSMAP)
			sampler2D _MetallicGlossMap;
	//	#else
			half _Glossiness;
			half _Metallic;
	//	#endif

		half _Cutoff;
		
		fixed _VertexOcclusionStrength;

		void vert (inout appdata_full v, out Input o) {
			UNITY_INITIALIZE_OUTPUT(Input,o);
			// Unity's dynamic batching might break normals and tangents
			v.normal = normalize(v.normal);
			v.tangent.xyz = normalize(v.tangent.xyz);

			float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
			fixed3 worldNormal = UnityObjectToWorldNormal(v.normal);
			fixed3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
		  
			#if defined (UNITY_PASS_DEFERRED)
				o.worldTangent_screenPosX.xyz = worldTangent;
				fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
		  		o.worldBinormal_screenPosY.xyz = cross(worldNormal, worldTangent) * tangentSign;
			#endif

			float4 pos = mul (UNITY_MATRIX_MVP, v.vertex);
			float4 screenPos = ComputeScreenPos (pos);
			screenPos.xy /= screenPos.w;
			o.worldTangent_screenPosX.w = screenPos.x;
			o.worldBinormal_screenPosY.w = screenPos.y;
		}

		void surf (Input IN, inout SurfaceOutputLuxAnisoMetallic o) {

			fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			o.Alpha = c.a;

		//	Dithered Opacity
			#if defined (_SPECGLOSSMAP)
				#if !defined(UNITY_PASS_SHADOWCASTER)
					//float2 screenPos = floor(( float2(IN.screenPos.xy / IN.screenPos.w) * _ScreenParams.xy);
					float2 screenPos = floor( float2(IN.worldTangent_screenPosX.w, IN.worldBinormal_screenPosY.w ) * _ScreenParams.xy);
					// Interleaved Gradient Noise from http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare (slide 122)
					half3 magic = float3(0.06711056, 0.00583715, 52.9829189);
					half gradient = frac(magic.z * frac(dot(screenPos.xy, magic.xy)));
					clip(c.a + gradient - 1.0);
				#else
					// We have to distinguish between depth and shadow pass (forward rendering) / unity_LightShadowBias is (0,0,0,0) when rendering depth in forward
					float2 screenPos = floor( float2(IN.worldTangent_screenPosX.w, IN.worldBinormal_screenPosY.w ) * _ScreenParams.xy);
					// Interleaved Gradient Noise from http://www.iryoku.com/next-generation-post-processing-in-call-of-duty-advanced-warfare (slide 122)
					half3 magic = float3(0.06711056, 0.00583715, 52.9829189);
					half gradient = frac(magic.z * frac(dot(screenPos.xy, magic.xy)));
					half clipVal = (dot(unity_LightShadowBias, 1.0) == 0.0 ) ? c.a + gradient - 1.0 : c.a - _Cutoff;
					clip (clipVal);
				#endif
		//	Regular Alpha Testing
			#else
				clip(c.a - _Cutoff);
			#endif

			// In order to get a more lively shading we add vertex color green not only to occlusion but also to the albedo
			// That gives us some kind of double occlsion but looks fine here.
			o.Albedo = c.rgb * IN.color.g;

			#if defined (_METALLICGLOSSMAP)
				fixed4 metallicGloss = tex2D (_MetallicGlossMap, IN.uv_MainTex);
				o.Smoothness = metallicGloss.a * _Glossiness;
				o.Metallic = metallicGloss.r * _Metallic; // That is how the standard shaders handles it...
				o.Occlusion = metallicGloss.g;
			#else
				o.Smoothness = _Glossiness;
				o.Metallic = _Metallic;
			#endif


			// Shader has to write to o.Normal as otherwise the needed tranformation matrix parameters will not get compiled out
			// !!!! SINGLE SIDED GEOMETRY: We have to multiply Normal by flipFacing.
			o.Normal = UnpackNormal( tex2D(_BumpMap, IN.uv_MainTex)) * float3(1.0, 1.0, IN.FacingSign);

			// Add occlusion from texture as well as occlusion from vertex colors
			o.Occlusion *= lerp(1.0, IN.color.g, _VertexOcclusionStrength);

		//	Lux: Anisotropic features
			// We simply turn on or off translucency. So it is either 0 or 1. Mask is derived from metallic.
			o.Translucency = _Translucency;
			
			// tangent space basis -> tangent = (1, 0, 0), bitangent = (0, 1, 0) and normal = (0, 0, 1).
			// !!!! SINGLE SIDED GEOMETRY: We have to multiply TangentDir by flipFacing.
			o.TangentDir = lerp( _BaseTangentDir, UnpackNormal( tex2D(_TangentDir, IN.uv_MainTex)), _TangentDirStrength) * float3(IN.FacingSign, 1, 1);
			
			#if defined (UNITY_PASS_DEFERRED)
				half3 n = WorldNormalVector(IN, half3(0,0,1));
				half3x3 tangent2World = half3x3(IN.worldTangent_screenPosX.xyz, IN.worldBinormal_screenPosY.xyz, n);
				o.worldTangentDir = mul( o.TangentDir, (tangent2World));
			#endif

			// We have to use LUX_DIFFUSESCATTERING_METALLIC as the shader always uses the metallic workflow
			// Please note: Adding diffuse scattering when using the metallic workflow is not really correct as it will change the finally calculated specular color as well.
			LUX_DIFFUSESCATTERING_METALLIC(o.Albedo, o.Normal, IN.viewDir, o.Metallic)
		
		}
		ENDCG
	} 
	FallBack "Legacy Shaders/Transparent/Cutout/VertexLit"
}
