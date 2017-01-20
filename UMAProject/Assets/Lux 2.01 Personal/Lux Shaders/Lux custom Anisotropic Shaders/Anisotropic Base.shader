// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Lux/Anisotropic Lighting/Base" {
	Properties {
		_Color ("Color", Color) = (1,1,1,1)
		[Header(Basic Inputs)]
		[Space(3)]
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		[NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}

		// Shader does not handle Metalic = 0.0 correctly. So we simply clamp the property.
		[Gamma] _Metallic("Metallic", Range(0.01, 1.0)) = 0.0
		// Smoothness must not go up to 1.0! So we simply clamp the property.
		_Glossiness ("Smoothness", Range(0,0.975)) = 0.5

		[Toggle(_METALLICGLOSSMAP)] _EnableMetallGlossMap("Enable Metallic Gloss Map", Float) = 0.0
		_MetallicGlossMap("Metallic (R) Occlusion (G) Smoothness (A)", 2D) = "white" {}
		

		[Lux_FloatToggleDrawer] _Translucency("Enable Translucent Lighting", Float) = 0.0

		[Header(Tangent Direction)]
		[Space(3)]
		[NoScaleOffset] _TangentDir ("Tangent (RG)", 2D) = "bump" {}
		_BaseTangentDir ("Base Tangent Direction (UV)", Vector) = (0.0,1.0,0.0,0.0)
		_TangentDirStrength ("Strength", Range(0,1)) = 1
		        
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		
		CGPROGRAM
		#pragma surface surf LuxAnisoMetallic fullforwardshadows vertex:vert 
		#pragma multi_compile __ LUX_AREALIGHTS
		#pragma shader_feature _METALLICGLOSSMAP

		#include "../Lux Core/Lux Config.cginc"
		#include "../Lux Core/Lux Lighting/LuxAnisoMetallicPBSLighting.cginc"
		#pragma target 3.0

		struct Input {
			float2 uv_MainTex;
			half3 viewDir;
			float3 worldNormal;
			INTERNAL_DATA

			fixed4 color : COLOR0;
			//#if defined (UNITY_PASS_DEFERRED) // does not get handled correctly by the compiler
				half3 worldTangent;
				half3 worldBinormal;
			//#endif
		};

		fixed4 _Color;
		sampler2D _MainTex;
		sampler2D _BumpMap;
		#if defined (_METALLICGLOSSMAP)
			sampler2D _MetallicGlossMap;
		#else
			half _Glossiness;
			half _Metallic;
		#endif
		
		void vert (inout appdata_full v, out Input o) {
			UNITY_INITIALIZE_OUTPUT(Input,o);

			// Unity's dynamic batching might break normals and tangents
			// v.normal = normalize(v.normal);
			// v.tangent.xyz = normalize(v.tangent.xyz);

			float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
			fixed3 worldNormal = UnityObjectToWorldNormal(v.normal);
			fixed3 worldTangent = UnityObjectToWorldDir(v.tangent.xyz);
			  
			//worldTangent = normalize( mul( _Object2World, float4( v.tangent.xyz, 0.0 ) ).xyz );
			#if defined (UNITY_PASS_DEFERRED)
				o.worldTangent = worldTangent;
				fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
		  		o.worldBinormal = cross(worldNormal, worldTangent) * tangentSign;
			#endif
		}



		void surf (Input IN, inout SurfaceOutputLuxAnisoMetallic o) {

			fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			o.Alpha = c.a;
			o.Albedo = c.rgb;

			#if defined (_METALLICGLOSSMAP)
				fixed4 metallicGloss = tex2D (_MetallicGlossMap, IN.uv_MainTex);
				o.Smoothness = metallicGloss.a;
				o.Metallic = metallicGloss.r; // That is how the standard shaders handles it...
				o.Occlusion = metallicGloss.g;
			#else
				o.Smoothness = _Glossiness;
				o.Metallic = _Metallic;
			#endif

			// Shader has to write to o.Normal as otherwise the needed tranformation matrix parameters will not get compiled out
			o.Normal = UnpackNormal( tex2D(_BumpMap, IN.uv_MainTex));

		//	Lux: Anisotropic features

			// We simply turn on or off translucency. So it is either 0 or 1. Mask is derived from metallic.
			o.Translucency = _Translucency;
			
			o.TangentDir = lerp( _BaseTangentDir, UnpackNormal( tex2D(_TangentDir, IN.uv_MainTex)), _TangentDirStrength);
			// tangent space basis -> tangent = (1, 0, 0), bitangent = (0, 1, 0) and normal = (0, 0, 1).

			#if defined (UNITY_PASS_DEFERRED)
				half3 n = ( WorldNormalVector(IN, half3(0,0,1) ) );
				half3x3 tangent2World = half3x3(IN.worldTangent.xyz, IN.worldBinormal.xyz, n);
				o.worldTangentDir = mul( o.TangentDir, (tangent2World));
			#endif

		}
		ENDCG
	} 
	FallBack "Diffuse"
}
