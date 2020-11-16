// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//	============================================================
//	Name:		AtlasShader
//	Author: 	Umut Ozkan
//	============================================================

Shader "UMA/AtlasDetailShaderNormal" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_AdditiveColor ("Additive Color", Color) = (0,0,0,0)
	_MainTex ("Normalmap", 2D) = "bump" {}
	_ExtraTex ("mask", 2D) = "white" {}
}

SubShader 
{
	Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}

	Pass
	{
		Tags{ "LightMode" = "Vertex" }
		Fog{ Mode Off }
		Blend One One
		Lighting Off
		CGPROGRAM
		#pragma vertex vert
		#pragma fragment frag

		#include "UnityCG.cginc"
		#include "UnityStandardUtils.cginc"

		float4 _Color;
		float4 _AdditiveColor;
		sampler2D _MainTex;
		sampler2D _ExtraTex;

		struct v2f {
			float4  pos : SV_POSITION;
			float2  uv : TEXCOORD0;
		};

		float4 _MainTex_ST;

		v2f vert(appdata_base v)
		{
			v2f o;
			o.pos = UnityObjectToClipPos(v.vertex);
			o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
			return o;
		}
		
		half2 UNPACK_WITH_WEIGHT_INTERNAL(half4 packednormal, half bumpScale)
        {
            #if defined(UNITY_NO_DXT5nm)
                half2 normal = half2(packednormal.xy * 2 - 1);
                normal.xy *= bumpScale;
                return normal;
            #else
                half2 result = half2(packednormal.wy * 2 - 1);
                result.xy *= bumpScale;
                return result;
            #endif
        }

        // This atlas shader has Blend mode add
        // It adds adds unpacked values of normal textures. The post process normalize and packs them
		half4 frag(v2f i) : COLOR
		{
			half4 current = tex2D(_MainTex, i.uv);
			half4 extra = tex2D(_ExtraTex, i.uv);
            half2 n = UNPACK_WITH_WEIGHT_INTERNAL(current, min(extra.a, _Color.a));
			// We only need 2 values to calculate normal. Post process will calculate the 3rd vector component
            return half4(n.x, n.y, 0, 0);
		}
		ENDCG
	}
}

Fallback "Transparent/VertexLit"
} 
