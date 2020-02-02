// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'

//	============================================================
//	Name:		AtlasShader
//	Author: 	Joen Joensen (@UnLogick)
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

	GrabPass
    {
        "_PreviousNormal"
    }

	Pass
	{
		Tags{ "LightMode" = "Vertex" }
		Fog{ Mode Off }
		Blend Off
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
		sampler2D _PreviousNormal;

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

        // This atlas shader has Blend mode Off
        // It grabs from previous texture and outputs the merged normal map
		half4 frag(v2f i) : COLOR
		{
		    // Get previous normal map from grab pass,
			half4 previous = tex2D(_PreviousNormal, i.uv);
		    // Get current texture and mask textures
			half4 current = tex2D(_MainTex, i.uv);
			half4 extra = tex2D(_ExtraTex, i.uv);
            
            // Unpack previous and current textures with alpha from color/mask as strength
            half3 pn = half3(previous.wy * 2 - 1, 0);
            pn.z = sqrt(1 - saturate(dot(pn.xy, pn.xy)));
            half3 n = half3(current.wy * 2 - 1, 0);
            n.xy *= min(extra.a, _Color.a);
            n.z = sqrt(1 - saturate(dot(n.xy, n.xy)));
            
            // Blend them as current normal map being detail on previous one
            half3 blended = BlendNormals(pn, n);
            
            // Re-pack blended normal into texture and return.
#if defined(UNITY_NO_DXT5nm)
			return half4((blended.xyz + 1) / 2, 1);
#else
			half4 packednormal;
			packednormal.wy = (blended.xy + 1) / 2;
			packednormal.x = 1;
			packednormal.z = 1;
			return packednormal;
#endif
		}
		ENDCG
	}
}

Fallback "Transparent/VertexLit"
} 
