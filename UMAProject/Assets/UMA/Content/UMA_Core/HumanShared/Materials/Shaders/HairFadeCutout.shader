// Upgrade NOTE: replaced '_Object2World' with 'unity_ObjectToWorld'

// Simple 2 Pass Hair Shader 

// This shader blends a solid cutout and fade shader to try and produce reasonable looking hair.

// Made with components from the: Amplify Shader Editor
// Available at the Unity Asset Store - http://u3d.as/y3X 
Shader "UMA/Hair Fade Cutout"
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

		void surf( Input i , inout SurfaceOutputStandard o )
		{
			float2 uv_BumpMap = i.uv_texcoord * _BumpMap_ST.xy + _BumpMap_ST.zw;
			o.Normal = UnpackScaleNormal( tex2D( _BumpMap,uv_BumpMap) ,_BumpStrength );
			float2 uv_MainTex = i.uv_texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
			float4 tex2DNode1 = tex2D( _MainTex,uv_MainTex);
			o.Albedo = tex2DNode1.xyz;
			float2 uv_MetallicGlossMap = i.uv_texcoord * _MetallicGlossMap_ST.xy + _MetallicGlossMap_ST.zw;
			o.Metallic = _MetallicAdd + (tex2D( _MetallicGlossMap,uv_MetallicGlossMap).x * _MetallicStrength);
			o.Smoothness = _SmoothnessAdd + (tex2D(_MetallicGlossMap, uv_MetallicGlossMap).a * _SmoothnessStrength);
			o.Alpha = tex2DNode1.a;
			clip( tex2DNode1.a - _MaskClipValue );
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf Standard keepalpha 

		ENDCG
		
		Tags{ "RenderType" = "Transparent"  "Queue" = "Transparent+0" "IgnoreProjector" = "True" }
		Cull Off
		CGINCLUDE
		#include "UnityStandardUtils.cginc"
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0

		void surf2( Input i , inout SurfaceOutputStandard o )
		{
			float2 uv_BumpMap = i.uv_texcoord * _BumpMap_ST.xy + _BumpMap_ST.zw;
			o.Normal = UnpackScaleNormal( tex2D( _BumpMap,uv_BumpMap) ,_BumpStrength );
			float2 uv_MainTex = i.uv_texcoord * _MainTex_ST.xy + _MainTex_ST.zw;
			float4 tex2DNode1 = tex2D( _MainTex,uv_MainTex);
			o.Albedo = tex2DNode1.xyz;
			float2 uv_MetallicGlossMap = i.uv_texcoord * _MetallicGlossMap_ST.xy + _MetallicGlossMap_ST.zw;
			// o.Metallic = tex2D( _MetallicGlossMap,uv_MetallicGlossMap).x * _MetallicStrength;
			o.Metallic = _MetallicAdd + (tex2D(_MetallicGlossMap, uv_MetallicGlossMap).x * _MetallicStrength);
			o.Smoothness = _SmoothnessAdd + (tex2D(_MetallicGlossMap, uv_MetallicGlossMap).a * _SmoothnessStrength);
			o.Alpha = tex2DNode1.a;
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf2 Standard alpha:fade keepalpha 

		ENDCG
		
		Pass
		{
			Name "ShadowCaster"
			Tags{ "LightMode" = "ShadowCaster" }
			ZWrite On
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_shadowcaster
			#pragma multi_compile UNITY_PASS_SHADOWCASTER
			#pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
			# include "HLSLSupport.cginc"
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			sampler3D _DitherMaskLOD;
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float3 worldPos : TEXCOORD6;
				float4 tSpace0 : TEXCOORD1;
				float4 tSpace1 : TEXCOORD2;
				float4 tSpace2 : TEXCOORD3;
				float4 texcoords01 : TEXCOORD4;
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				fixed3 worldTangent = UnityObjectToWorldDir( v.tangent.xyz );
				fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
				fixed3 worldBinormal = cross( worldNormal, worldTangent ) * tangentSign;
				o.tSpace0 = float4( worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x );
				o.tSpace1 = float4( worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y );
				o.tSpace2 = float4( worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z );
				o.texcoords01 = float4( v.texcoord.xy, v.texcoord1.xy );
				o.worldPos = worldPos;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				return o;
			}
			fixed4 frag( v2f IN ) : SV_Target
			{
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				surfIN.uv_texcoord = IN.texcoords01.xy;
				float3 worldPos = IN.worldPos;
				fixed3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				SurfaceOutputStandard o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputStandard, o )
				surf( surfIN, o );
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	FallBack "Legacy Shaders/Transparent/Cutout/Bumped Diffuse"
}
