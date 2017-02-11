Shader "Lux/Human/Skin Standard Lighting" {
	Properties {
		_Color ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		[NoScaleOffset] _SpecTex ("Translucency (G) AO (A)", 2D) = "gray" {}
		[NoScaleOffset] _BumpMap ("Bump Map", 2D) = "bump" {}
	}
	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200
		
		CGPROGRAM
		// Physically based Standard lighting model, and enable shadows on all light types
		#pragma surface surf LuxStandardSpecular fullforwardshadows
		#include "../Lux Core/Lux Config.cginc"
		#include "../Lux Core/Lux Lighting/LuxStandardPBSLighting.cginc"

		// Use shader model 3.0 target, to get nicer looking lighting
		#pragma target 3.0

		sampler2D _MainTex;
		sampler2D _SpecTex;
		sampler2D _BumpMap;

		struct Input {
			float2 uv_MainTex;
		};

		half _Glossiness;
		half _Metallic;
		fixed4 _Color;

		void surf (Input IN, inout SurfaceOutputLuxStandardSpecular o) {
			// Albedo comes from a texture tinted by color
			fixed4 c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			o.Albedo = c.rgb;
			o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
			// Set specular to the original spec value of skin which is 0.028
			o.Specular = unity_ColorSpaceDielectricSpec.rgb * 0.7;
			o.Smoothness = c.a;
			fixed4 combinedMap = tex2D(_SpecTex, IN.uv_MainTex);
			o.Occlusion = combinedMap.a;
			o.Alpha = 1;
		}
		ENDCG
	}
	FallBack "Diffuse"
}
