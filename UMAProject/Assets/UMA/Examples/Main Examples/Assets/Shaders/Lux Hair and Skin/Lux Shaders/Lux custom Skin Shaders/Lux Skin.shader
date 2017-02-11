// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

Shader "Lux/Human/Skin" {
	Properties {
		_Color ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB) Smoothness (A)", 2D) = "white" {}
		[NoScaleOffset] _SpecTex ("Translucency (G) AO (A)", 2D) = "gray" {}
		[NoScaleOffset] _BumpMap ("Bump Map", 2D) = "bump" {}
		
		[Space(4)]
		[Toggle(_LUX_SKINMICROBUMPS)] _UseMicroBumps ("Enable Micro Bumps", Float) = 0.0
		[NoScaleOffset] _MicroBumpMap ("Micro Bump Map", 2D) = "bump" {}
		_MicroBumpMapTiling ("Micro Bump Tiling", Range (5,50)) = 10.0
		_MicroBumpScale ("Micro Bump Scale", Range (0.1,2)) = 1

		[Header(Diffuse Bump Settings)]
		[Space(4)]
		_BumpBias ("Diffuse Normal Map Blur Bias", Float) = 2.0
		_BlurStrength ("Blur Strength", Range (0,1)) = 1.0

		[Header(Preintegrated Skin Lighting)]
		[Space(4)]
		_CurvatureInfluence ("Curvature Influence", Range (0,1)) = 0.5
		_CurvatureScale ("Curvature Scale", Float) = 0.02
		_Bias ("Bias", Range (0,1)) = 0.0

	}

	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		
		CGPROGRAM
		#pragma surface surf LuxSkinSpecular fullforwardshadows vertex:vert
		#pragma target 3.0

		#pragma multi_compile __ LUX_AREALIGHTS
		#pragma multi_compile __ LUX_LIGHTINGFADE
		#pragma shader_feature _LUX_SKINMICROBUMPS
		
		#include "../Lux Core/Lux Config.cginc"
		#include "../Lux Core/Lux Lighting/LuxSkinPBSLighting.cginc"
		#include "UnityStandardUtils.cginc"

		struct Input {
			float2 uv_MainTex;
			#if defined (LUX_LIGHTINGFADE)
				float blendState;
			#endif
			float3 worldNormal;
			float3 worldPos;
			INTERNAL_DATA
		};

		sampler2D _MainTex;
		sampler2D _BumpMap;
		#if defined (_LUX_SKINMICROBUMPS)
			sampler2D _MicroBumpMap;
			half _MicroBumpMapTiling;
			half _MicroBumpScale;
		#endif
		sampler2D _SpecTex;
		fixed4 _Color;
		float _BumpBias;
		float _BlurStrength;
		fixed _CurvatureScale;
		fixed _CurvatureInfluence;
		half _Bias;

		
		
		//float2 _Lux_Skin_DistanceRange;

		void vert (inout appdata_full v, out Input o) {
			UNITY_INITIALIZE_OUTPUT(Input,o);
			// Store blendState
			#if defined (LUX_LIGHTINGFADE)
				float3 worldPosition = mul(unity_ObjectToWorld, v.vertex);
				o.blendState = distance(_WorldSpaceCameraPos, worldPosition);
				o.blendState = saturate( (_Lux_Skin_DistanceRange.x - o.blendState) / _Lux_Skin_DistanceRange.y);
			#endif
		}


		void surf (Input IN, inout SurfaceOutputLuxSkinSpecular o) {
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			o.Smoothness = c.a;
			o.Alpha = 1;

			o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));

			#if defined (_LUX_SKINMICROBUMPS)
				half3 MicroBump = UnpackScaleNormal(tex2D(_MicroBumpMap, IN.uv_MainTex * _MicroBumpMapTiling ), _MicroBumpScale);
				o.Normal = normalize(half3(o.Normal.xy + MicroBump.xy, o.Normal.z * MicroBump.z));
			#endif

			fixed3 blurredWorldNormal = UnpackNormal( tex2Dlod ( _BumpMap, float4 ( IN.uv_MainTex, _BumpBias, _BumpBias ) ) );
			blurredWorldNormal = normalize( lerp(o.Normal, blurredWorldNormal, _BlurStrength
			#if defined (LUX_LIGHTINGFADE)
				* IN.blendState ) );
			#else
				) );
			#endif

			blurredWorldNormal = WorldNormalVector( IN, blurredWorldNormal );
			o.BlurredNormal = blurredWorldNormal;

			// Sample combined translucency / ao map
			fixed4 combinedMap = tex2D(_SpecTex, IN.uv_MainTex);
			o.Translucency = combinedMap.g
			#if defined (LUX_LIGHTINGFADE)
				* IN.blendState;
			#else
				;
			#endif
			o.Occlusion = combinedMap.a;


			// Set specular to the original spec value of skin which is 0.028
			o.Specular = unity_ColorSpaceDielectricSpec.rgb * 0.7;	
			
			fixed Curvature = 0;
			//	Calculate the curvature of the model dynamically
			if (_CurvatureInfluence > 0) {
			//	Get the scale of the derivatives of the blurred world normal and the world position.
				#if (SHADER_TARGET > 40) //SHADER_API_D3D11
	            // In DX11, ddx_fine should give nicer results.
	            	float deltaWorldNormal = length( abs(ddx_fine(blurredWorldNormal)) + abs(ddy_fine(blurredWorldNormal)) );
	            	float deltaWorldPosition = length( abs(ddx_fine(IN.worldPos)) + abs(ddy_fine(IN.worldPos)) );
				#else
					float deltaWorldNormal = length( fwidth( blurredWorldNormal ) );
					float deltaWorldPosition = length( max(1e-5f, fwidth ( IN.worldPos ) ) );
					deltaWorldPosition = (deltaWorldPosition == 0.0) ? 1e-5f : deltaWorldPosition;
				#endif		
				Curvature = (deltaWorldNormal / deltaWorldPosition) * _CurvatureScale;
				Curvature = lerp(combinedMap.b, Curvature, _CurvatureInfluence);
			}
			else {
				Curvature = combinedMap.b;	
			}
			o.Curvature = saturate(Curvature + _Bias)
			#if defined (LUX_LIGHTINGFADE)
				* IN.blendState;
			#else
				;
			#endif
			//#if defined(LUX_AREALIGHTS)
				o.worldPosition = IN.worldPos;
			//#endif
		}
		ENDCG
	} 
	FallBack "Diffuse"
}

