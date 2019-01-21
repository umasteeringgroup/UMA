// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//	============================================================
//	Name:		NormalSwizzleShader
//	Author: 	Kenan Chabuk (@Kenamis)
//	This works in conjunction with the AtlasNormalShader that swizzles the alpha to red channel of the packed normal and blends them.
//	Then this post effect re-swizzles the normal into the packed format for use with standard shaders.
//	============================================================

Shader "UMA/NormalSwizzleShader" {
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
				half4 r = tex2D(_MainTex, i.uv);

				//G and A are the important ones. 
#if defined(UNITY_NO_DXT5nm)
				return half4(r.r, r.g, r.b, 0);
#else
				return half4(1, r.y, 1, r.x);
#endif
			}
			ENDCG
		}
	}

Fallback "Transparent/VertexLit"
}
