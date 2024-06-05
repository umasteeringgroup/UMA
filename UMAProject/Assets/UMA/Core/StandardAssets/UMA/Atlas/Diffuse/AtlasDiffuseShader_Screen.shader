// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//	============================================================
//	Name:		AtlasShader
//	Author: 	Joen Joensen (@UnLogick)
//	============================================================

Shader "UMA/Atlas/AtlasDiffuseShader_Screen" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_AdditiveColor ("Additive Color", Color) = (0,0,0,0)
	_MainTex ("Base Texture", 2D) = "white" {}
	_ExtraTex ("mask", 2D) = "white" {}
	_BaseTex ("Base Texture", 2D) = "white" {}
}

SubShader {
	Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}

	Pass 
	{
		Tags { "LightMode" = "Vertex" }
   		Fog { Mode Off }
		BlendOp Add, Add
		Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
		Lighting Off
CGPROGRAM
#pragma vertex vert
#pragma fragment frag

#include "UnityCG.cginc"

float4 _Color;
float4 _AdditiveColor;
sampler2D _MainTex;
sampler2D _ExtraTex;
sampler2D _BaseTex;

struct v2f {
    float4  pos : SV_POSITION;
    float2  uv : TEXCOORD0;
};

float4 _MainTex_ST;

v2f vert (appdata_base v)
{
    v2f o;
    o.pos = UnityObjectToClipPos (v.vertex);
    o.uv = TRANSFORM_TEX (v.texcoord, _MainTex);
    return o;
}

float3 BlendMode_Screen(float3 base, float3 blend)
{
	return base + blend - base*blend;
}

		float4 frag (v2f i) : COLOR
		{
			float4 texcol = tex2D(_MainTex, i.uv); // get the color from the overlay
			float4 basecol = tex2D(_BaseTex, i.uv); // get the color from the previous pass
			texcol.rgb = BlendMode_Screen(basecol.rgb, texcol.rgb); // subtract the overlay from the previous pass
			half4 mask = tex2D(_ExtraTex, i.uv);
			return float4(texcol.rgb, mask.a)*_Color+_AdditiveColor;
		}
ENDCG
	}
}

Fallback "Transparent/VertexLit"
} 