// Simple 2 Pass Hair Shader 

// This shader blends a solid cutout and fade shader to try and produce reasonable looking hair.

// Made with components from the: Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "UMA/Hair Fade2 Cutout Emission"
{
	Properties
	{
		[HideInInspector] __dirty( "", Int ) = 1
		_MainTex("Diffuse/Alpha Map", 2D) = "white" {}
		_MaskClipValue( "Cotout Clip Value", Range( 0 , 1) ) = 0.7
		_BumpMap("Normal Map", 2D) = "bump" {}
		_BumpStrength("Bump Strength", Range( 0 , 1)) = 0.4
		_MetallicStrength("Metallic Strength", Range (0,1) ) = 0.5
		_MetallicAdd("Metallic Add", Range (0,1) ) = 0.0
		_SmoothnessStrength("Smoothness Strength", Range (0,1)) = 0.5
		_SmoothnessAdd("Smoothness Add",Range(0,1)) = 0.0
		_MetallicGlossMap("Metallic Gloss Map", 2D) = "white" {}
        _Emission("Emission", 2D) = "black" {}
		[HDR] _EmissionColor("Color", Color) = (0,0,0)
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
	}

	SubShader
	{
		Tags{ "RenderType" = "TransparentCutout"  "Queue" = "AlphaTest+0" }
		Cull Back
		CGINCLUDE
		#include "UnityStandardUtils.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0
		struct Input
		{
			float2 uv_texcoord;
		};

	    uniform float _MetallicAdd;
	    uniform float _MetallicStrength;
		uniform float _SmoothnessStrength;
		uniform float _SmoothnessAdd;
		uniform float _BumpStrength;
		uniform sampler2D _BumpMap;
		uniform float4 _BumpMap_ST;
		uniform sampler2D _MainTex;
		uniform float4 _MainTex_ST;
		uniform sampler2D _MetallicGlossMap;
		uniform float4 _MetallicGlossMap_ST;
		uniform float _MaskClipValue = 0.5;
		uniform sampler2D _Emission;
		uniform half4 _EmissionColor;

		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float2 uv_BumpMap = i.uv_texcoord;// *_BumpMap_ST.xy + _BumpMap_ST.zw;
			o.Normal = UnpackScaleNormal( tex2D( _BumpMap,uv_BumpMap) ,_BumpStrength );
			float2 uv_MainTex = i.uv_texcoord;// *_MainTex_ST.xy + _MainTex_ST.zw;
			float4 tex2DNode1 = tex2D( _MainTex,uv_MainTex);
			half4 emiss = tex2D(_Emission,uv_MainTex) * _EmissionColor;
			o.Albedo = tex2DNode1.xyz;
			float2 uv_MetallicGlossMap = i.uv_texcoord;// *_MetallicGlossMap_ST.xy + _MetallicGlossMap_ST.zw;
		    o.Metallic = _MetallicAdd + (tex2D( _MetallicGlossMap,uv_MetallicGlossMap).x * _MetallicStrength);
			// o.Metallic = _MetallicAdd + (tex2D(_MetallicGlossMap, uv_MetallicGlossMap).x * _MetallicStrength);
			o.Smoothness = _SmoothnessAdd + (tex2D(_MetallicGlossMap, uv_MetallicGlossMap).a * _SmoothnessStrength);
			o.Alpha = tex2DNode1.a;
			o.Emission = emiss;
			clip( tex2DNode1.a - _MaskClipValue );
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Standard addshadow 

		ENDCG
		
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IgnoreProjector" = "True" }
		Cull Back
		CGINCLUDE
		#include "UnityStandardUtils.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0

		void surf2( Input i , inout SurfaceOutputStandard o )
		{
			float2 uv_BumpMap = i.uv_texcoord;// *_BumpMap_ST.xy + _BumpMap_ST.zw;
			o.Normal = UnpackScaleNormal( tex2D( _BumpMap,uv_BumpMap) ,_BumpStrength );
			float2 uv_MainTex = i.uv_texcoord;// *_MainTex_ST.xy + _MainTex_ST.zw;
			float4 tex2DNode1 = tex2D( _MainTex,uv_MainTex);
			half4 emiss = tex2D(_Emission,uv_MainTex) * _EmissionColor;
			o.Albedo = tex2DNode1.xyz;
			float2 uv_MetallicGlossMap = i.uv_texcoord;// *_MetallicGlossMap_ST.xy + _MetallicGlossMap_ST.zw;
			o.Metallic = _MetallicAdd + (tex2D(_MetallicGlossMap, uv_MetallicGlossMap).x * _MetallicStrength);
			o.Smoothness = _SmoothnessAdd + (tex2D(_MetallicGlossMap, uv_MetallicGlossMap).a * _SmoothnessStrength);
		
			o.Emission = emiss * tex2DNode1.a;
			o.Alpha = tex2DNode1.a;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf2 Standard alpha:fade keepalpha addshadow

		ENDCG		
	}
	FallBack "Legacy Shaders/Transparent/Cutout/Bumped Diffuse"
}
