// Put together with Amplify's help.
Shader "Custom/Subsurface Scattering"
{
	Properties
	{
		[Header(Standard)]
		_Color ("Color", Color) = (1,1,1,1)
		_MainTex("Albedo", 2D) = "white" {}
		[NoScaleOffset][Normal]_BumpMap("Normal Map", 2D) = "bump" {}
		_BumpScale ("Normal Scale", Float) = 1
		[NoScaleOffset]_MetallicGlossMap("Metallic", 2D) = "black" {}
		_GlossMapScale("Smoothness", Range(0, 1)) = 1.0
		[Toggle(_)]_SmoothnessFromAlbedo("Smoothness stored in Albedo alpha", Float) = 0.0
		[NoScaleOffset]_OcclusionMap("Occlusion", 2D) = "white" {}
		_OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1.0
		[Enum(UV1, 0, UV2, 1)] _OcclusionUVSource("Occlusion UV Source", Float) = 0

		[Header(Detail)]
		[NoScaleOffset]_OverlayMap("Overlay", 2D) = "black" {}
		_OverlayColor ("Overlay Color", Color) = (1,1,1,1)
		[NoScaleOffset]_DetailMask("Detail Mask", 2D) = "white" {}
		[Normal]_DetailBumpMap("Detail Normal Map", 2D) = "bump" {}
		_DetailMetallicGlossMap("Detail Metallic", 2D) = "white" {}
		_DetailBumpMapScale ("Detail Scale", Float) = 1
		[Enum(UV1, 0, UV2, 1)] _SecondUVSource("Secondary UV Source", Float) = 0

		[Header(SSS)]
		[NoScaleOffset]_ThicknessMap("Thickness Map", 2D) = "black" {}
		[Toggle(_)]_ThicknessMapInvert("Invert Thickness", Float) = 0.0
		_ThicknessMapPower ("Thickness Map Power", Range(0.01, 10)) = 1
		[Enum(UV1, 0, UV2, 1)] _ThicknessUVSource("Thickness UV Source", Float) = 0
		[Toggle(_)]_ScatteringByAlbedo("Tint Scattering with Albedo", Float) = 0.0
		_SSSCol ("Scattering Color", Color) = (1,1,1,1)
		_SSSIntensity ("Scattering Intensity", Range(0, 10)) = 1
		_SSSPow ("Scattering Power", Range(0.01, 10)) = 1
		_SSSDist ("Scattering Distance", Range(0, 10)) = 1
		_SSSAmbient ("Scattering Ambient Intensity", Range(0, 1)) = 0.1

		[Header(System)]
		[Enum(UnityEngine.Rendering.CullMode)] _CullMode("Cull Mode", Float) = 2
		[ToggleOff(_SPECULARHIGHLIGHTS_OFF)]_SpecularHighlights ("Specular Highlights", Float) = 1.0
		[ToggleOff(_GLOSSYREFLECTIONS_OFF)]_GlossyReflections ("Glossy Reflections", Float) = 1.0
		[HideInInspector] _texcoord( "", 2D ) = "white" {}
		[HideInInspector] _texcoord2( "", 2D ) = "white" {}
		[HideInInspector] __dirty( "", Int ) = 1
	}

	SubShader
	{
		Tags{ "RenderType" = "Opaque"  "Queue" = "Geometry+0" }
           Cull[_CullMode]
		CGINCLUDE
		#include "UnityPBSLighting.cginc"
		#include "Lighting.cginc"
		#pragma target 3.0

		#pragma shader_feature _ _SPECULARHIGHLIGHTS_OFF
		#pragma shader_feature _ _GLOSSYREFLECTIONS_OFF		
		
		#ifdef UNITY_PASS_SHADOWCASTER
			#undef INTERNAL_DATA
			#undef WorldReflectionVector
			#undef WorldNormalVector
			#define INTERNAL_DATA half3 internalSurfaceTtoW0; half3 internalSurfaceTtoW1; half3 internalSurfaceTtoW2;
			#define WorldReflectionVector(data,normal) reflect (data.worldRefl, half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal)))
			#define WorldNormalVector(data,normal) half3(dot(data.internalSurfaceTtoW0,normal), dot(data.internalSurfaceTtoW1,normal), dot(data.internalSurfaceTtoW2,normal))
		#endif
		struct Input
		{
			float3 worldNormal;
			INTERNAL_DATA
			float2 uv_texcoord;
			float2 uv2_texcoord2;
		};

		struct SurfaceOutputCustomLightingCustom
		{
			half3 Albedo;
			half3 Normal;
			half3 Emission;
			half3 Specular;
			half Smoothness;
			half Occlusion;
			half Alpha;
			Input SurfInput;
			UnityGIInput GIData;
		};

		uniform sampler2D _MainTex; uniform float4 _MainTex_ST;
		uniform sampler2D _BumpMap;
		uniform sampler2D _MetallicGlossMap;
		uniform sampler2D _OcclusionMap;
		uniform sampler2D _ThicknessMap;
		uniform sampler2D _OverlayMap;
		uniform sampler2D _DetailMask;
		uniform sampler2D _DetailBumpMap; uniform float4 _DetailBumpMap_ST;
		uniform sampler2D _DetailMetallicGlossMap; uniform float4 _DetailMetallicGlossMap_ST;

		uniform float _ThicknessMapPower;
		uniform float _ThicknessMapInvert;
		uniform float3 _SSSCol;
		uniform float _SSSAmbient;
		uniform float _SSSIntensity;
		uniform float _SSSPow;
		uniform float _SSSDist; 
		uniform float3 _Color; 
		uniform float3 _OverlayColor; 

		uniform float _DetailBumpMapScale; 
		uniform float _ScatteringByAlbedo; 
		uniform float _SmoothnessFromAlbedo; 
		uniform float _BumpScale; 
		uniform float _OcclusionStrength; 
		uniform float _GlossMapScale; 

		uniform float _SecondUVSource; 
		uniform float _OcclusionUVSource; 
		uniform float _ThicknessUVSource; 

		#include "SSS_Utils.cginc"

		inline half4 LightingStandardCustomLighting( inout SurfaceOutputCustomLightingCustom s, half3 viewDir, UnityGI gi )
		{
			UnityGIInput data = s.GIData;
			Input i = s.SurfInput;
			half4 c = 0;
			SurfaceOutputStandard s1 = (SurfaceOutputStandard ) 0;
			float2 scaledUV = TRANSFORM_TEX(i.uv_texcoord, _MainTex);

			float4 _MainTex_var = tex2D( _MainTex, scaledUV );
			float2 texcoord2 = _SecondUVSource? scaledUV : i.uv2_texcoord2;
			float detailMask = tex2D( _DetailMask, scaledUV ).a;

			s1.Albedo = lerp(_MainTex_var.rgb * _Color, _OverlayColor, tex2D( _OverlayMap, texcoord2 ).rgb * detailMask);
			s1.Normal = WorldNormalVector( i , NormalInTangentSpace(scaledUV, texcoord2, detailMask) );
			s1.Normal = normalize(s1.Normal);
			s1.Emission = float3( 0,0,0 );
			float4 _MetallicGlossMap_var = tex2D( _MetallicGlossMap, scaledUV );
			float4 detailMetallicGlossMap_var = tex2D (_DetailMetallicGlossMap, TRANSFORM_TEX(texcoord2, _DetailMetallicGlossMap));
			_MetallicGlossMap_var *= lerp(1.0, detailMetallicGlossMap_var, detailMask);
			s1.Metallic = _MetallicGlossMap_var.r;
			s1.Smoothness = _SmoothnessFromAlbedo? _MainTex_var.a : _MetallicGlossMap_var.a;
			s1.Smoothness *= _GlossMapScale;
			s1.Smoothness = GeometricNormalFiltering(s1.Smoothness, i.worldNormal, 0.25, 0.5);
			float2 occlusionUV = _OcclusionUVSource? scaledUV : i.uv2_texcoord2;
			s1.Occlusion = LerpOneTo(tex2D( _OcclusionMap, occlusionUV ).g, _OcclusionStrength);

			data.light = gi.light;

			UnityGI gi1 = gi;
			#ifdef UNITY_PASS_FORWARDBASE
			Unity_GlossyEnvironmentData g1 = UnityGlossyEnvironmentSetup( s1.Smoothness, data.worldViewDir, s1.Normal, float3(0,0,0));
			gi1 = UnityGlobalIllumination( data, s1.Occlusion, s1.Normal, g1 );
			#endif

			#ifdef UNITY_PASS_FORWARDBASE
			float ase_lightAtten = data.atten;
			if( _LightColor0.a == 0)
			ase_lightAtten = 0;
			#else
			float3 ase_lightAttenRGB = gi.light.color / ( ( _LightColor0.rgb ) + 0.000001 );
			float ase_lightAtten = max( max( ase_lightAttenRGB.r, ase_lightAttenRGB.g ), ase_lightAttenRGB.b );
			#endif
			#if defined(HANDLE_SHADOWS_BLENDING_IN_GI)
			half bakedAtten = UnitySampleBakedOcclusion(data.lightmapUV.xy, data.worldPos);
			float zDist = dot(_WorldSpaceCameraPos - data.worldPos, UNITY_MATRIX_V[2].xyz);
			float fadeDist = UnityComputeShadowFadeDistance(data.worldPos, zDist);
			ase_lightAtten = UnityMixRealtimeAndBakedShadows(data.atten, bakedAtten, UnityComputeShadowFade(fadeDist));
			#endif

			float2 thicknessUV = _ThicknessUVSource? scaledUV : i.uv2_texcoord2;
			float3 thicknessMap_var = tex2D( _ThicknessMap, thicknessUV ).rgb;
			float3 lightDirection = Unity_SafeNormalize(_WorldSpaceLightPos0.xyz);

			float3 subsurfaceColour = _ScatteringByAlbedo? _SSSCol*s1.Albedo : _SSSCol;

			float3 finalResult = LightingStandard ( s1, viewDir, gi1 ).rgb;
			finalResult += getSubsurfaceScatteringLight(gi.light.color, gi.light.dir, s1.Normal, data.worldViewDir,
				ase_lightAtten, thicknessMap_var, gi1.indirect.diffuse, subsurfaceColour );
			finalResult += s1.Emission;

			c.rgb = finalResult;
			c.a = 1;
			return c;
		}

		inline void LightingStandardCustomLighting_GI( inout SurfaceOutputCustomLightingCustom s, UnityGIInput data, inout UnityGI gi )
		{
			s.GIData = data;
		}

		void surf( Input i , inout SurfaceOutputCustomLightingCustom o )
		{
			o.SurfInput = i;
			o.Normal = float3(0,0,1);
		}

		ENDCG
		CGPROGRAM
		#pragma surface surf StandardCustomLighting keepalpha fullforwardshadows 

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
			#include "HLSLSupport.cginc"
			#if ( SHADER_API_D3D11 || SHADER_API_GLCORE || SHADER_API_GLES3 || SHADER_API_METAL || SHADER_API_VULKAN )
				#define CAN_SKIP_VPOS
			#endif
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "UnityPBSLighting.cginc"
			struct v2f
			{
				V2F_SHADOW_CASTER;
				float2 customPack1 : TEXCOORD1;
				float4 tSpace0 : TEXCOORD2;
				float4 tSpace1 : TEXCOORD3;
				float4 tSpace2 : TEXCOORD4;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};
			v2f vert( appdata_full v )
			{
				v2f o;
				UNITY_SETUP_INSTANCE_ID( v );
				UNITY_INITIALIZE_OUTPUT( v2f, o );
				UNITY_TRANSFER_INSTANCE_ID( v, o );
				Input customInputData;
				float3 worldPos = mul( unity_ObjectToWorld, v.vertex ).xyz;
				half3 worldNormal = UnityObjectToWorldNormal( v.normal );
				half3 worldTangent = UnityObjectToWorldDir( v.tangent.xyz );
				half tangentSign = v.tangent.w * unity_WorldTransformParams.w;
				half3 worldBinormal = cross( worldNormal, worldTangent ) * tangentSign;
				o.tSpace0 = float4( worldTangent.x, worldBinormal.x, worldNormal.x, worldPos.x );
				o.tSpace1 = float4( worldTangent.y, worldBinormal.y, worldNormal.y, worldPos.y );
				o.tSpace2 = float4( worldTangent.z, worldBinormal.z, worldNormal.z, worldPos.z );
				o.customPack1.xy = customInputData.uv_texcoord;
				o.customPack1.xy = v.texcoord;
				TRANSFER_SHADOW_CASTER_NORMALOFFSET( o )
				return o;
			}
			half4 frag( v2f IN
			#if !defined( CAN_SKIP_VPOS )
			, UNITY_VPOS_TYPE vpos : VPOS
			#endif
			) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID( IN );
				Input surfIN;
				UNITY_INITIALIZE_OUTPUT( Input, surfIN );
				surfIN.uv_texcoord = IN.customPack1.xy;
				float3 worldPos = float3( IN.tSpace0.w, IN.tSpace1.w, IN.tSpace2.w );
				half3 worldViewDir = normalize( UnityWorldSpaceViewDir( worldPos ) );
				surfIN.worldNormal = float3( IN.tSpace0.z, IN.tSpace1.z, IN.tSpace2.z );
				surfIN.internalSurfaceTtoW0 = IN.tSpace0.xyz;
				surfIN.internalSurfaceTtoW1 = IN.tSpace1.xyz;
				surfIN.internalSurfaceTtoW2 = IN.tSpace2.xyz;
				SurfaceOutputCustomLightingCustom o;
				UNITY_INITIALIZE_OUTPUT( SurfaceOutputCustomLightingCustom, o )
				surf( surfIN, o );
				#if defined( CAN_SKIP_VPOS )
				float2 vpos = IN.pos;
				#endif
				SHADOW_CASTER_FRAGMENT( IN )
			}
			ENDCG
		}
	}
	Fallback "Diffuse"
}
