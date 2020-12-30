// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//	============================================================
//	Name:		AtlasDetailNormalPostShader
//	Author: 	Umut Ozkan
//	This works in conjunction with the AtlasDetailNormalShader.
//	============================================================

Shader "UMA/AtlasDetailNormalPostShader" {
	Properties{
		_MainTex("Normalmap", 2D) = "bump" {}
	}

	SubShader
	{
		Pass
		{
			Tags{ "LightMode" = "Vertex" }
			Fog{ Mode Off }
			Blend off
			Lighting Off
			Cull Off
			ZWrite Off
			ZTest Always
			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment frag

			#include "UnityCG.cginc"

			uniform sampler2D _MainTex;

		    inline float4 PACK_INTERNAL(half3 unpacked) {
				#if defined(UNITY_NO_DXT5nm)
				    half4 packednormal;
				    packednormal.xyz = (unpacked.xyz + 1) / 2;
				    packednormal.w = 1;
	                return packednormal;
	            #else
	                half4 packednormal;
	                packednormal.wy = (unpacked.xy + 1) / 2;
	                packednormal.x = 1;
	                packednormal.z = 1;
	                return packednormal;
	            #endif
		    }

			half4 frag(v2f_img i) : COLOR
			{
				half4 normal = tex2D(_MainTex, i.uv);
				normal.z = sqrt(1 - saturate(dot(normal.xy, normal.xy)));
				half3 normalized = normalize(normal.xyz);
				return PACK_INTERNAL(normalized);
			}
			ENDCG
		}
	}

Fallback "Transparent/VertexLit"
}
