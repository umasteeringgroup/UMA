// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//	============================================================
//	Name:		AtlasShader
//	Author: 	Joen Joensen (@UnLogick)
//	============================================================

Shader "UMA/AtlasShaderNormal" {
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_AdditiveColor ("Additive Color", Color) = (0,0,0,0)
	_MainTex ("Normalmap", 2D) = "bump" {}
	_ExtraTex ("mask", 2D) = "white" {}
}

SubShader 
{
	Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}

	GrabPass {}
	Pass 
	{
		Tags { "LightMode" = "Vertex" }
   		Fog { Mode Off }
		Blend off
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
		sampler2D _GrabTexture;

		struct v2f {
		    float4  pos : SV_POSITION;
		    float2  uv : TEXCOORD0;
		    float4  grabuv : TEXCOORD1;
		};

		float4 _MainTex_ST;

		//http://blog.selfshadow.com/publications/blending-in-detail/

		float3 linearBlend(float3 n1, float3 n2, float t)
		{
			return normalize(lerp(n2, n1, t));
		}

		float3 pdBlend(float3 n1, float3 n2, float t)
		{
			float2 p1 = (n1.xy/n1.z);
			float2 p2 = (n2.xy/n2.z);

			float2 pd = lerp(n2, n1, t);

			return normalize(float3(pd,1));
		}

		v2f vert (appdata_base v)
		{
		    v2f o;
		    o.pos = UnityObjectToClipPos (v.vertex);
		    o.uv = TRANSFORM_TEX (v.texcoord, _MainTex);
		    o.grabuv = ComputeGrabScreenPos(o.pos);
		    return o;
		}

		half4 frag (v2f i) : COLOR
		{
			half3 n1 = UnpackNormal(tex2D(_MainTex, i.uv));
			half3 n2 = UnpackNormal(tex2D(_GrabTexture, i.grabuv));

			half4 extra = tex2D(_ExtraTex, i.uv);

			n1 = clamp(n1, -1, 1);
			n2 = clamp(n2, -1, 1);

			half t = min(extra.a, _Color.a);

			//Add alpha check early out?

			//float3 r = linearBlend(n1,n2);
			half3 r = pdBlend(n1, n2, t);

			//Bring normal back into packed range.
			r = saturate(r * 0.5 + 0.5);

			//G and A are the important ones. 
			//Setting green to red and blue just so it looks greyscale in the inspector.
			//return half4(r.y,r.y,r.y,r.x);

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
