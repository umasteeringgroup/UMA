// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//	============================================================
//	Name:		NormalPackingShader
//	Author: 	Kenan Chabuk (@Kenamis)
//	This works in conjunction with the AtlasNormalShader that unpacks the normal and blends them.
//	Then this post effect repacks the normal into the packed range for use with standard shaders.
//	============================================================

Shader "UMA/NormalPackingShader" {
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

			half4 frag(v2f_img i) : COLOR
			{
				half3 r = tex2D(_MainTex, i.uv);

				//Bring normal back into packed range.
				r = saturate(r * 0.5 + 0.5);

				//G and A are the important ones. 
#if defined(UNITY_NO_DXT5nm)
				return half4(r.r, r.g, r.b, 0);
#else
				return half4(0, r.y, 0, r.x);
#endif
			}
			ENDCG
		}
	}

Fallback "Transparent/VertexLit"
}
