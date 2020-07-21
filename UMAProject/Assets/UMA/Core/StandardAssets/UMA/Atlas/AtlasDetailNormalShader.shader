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

	GrabPass { }

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
		sampler2D _GrabTexture;

		struct v2f {
			float4  pos : SV_POSITION;
			float2  uv : TEXCOORD0;
			float4  grabPos : TEXCOORD1;
		};

		float4 _MainTex_ST;

		v2f vert(appdata_base v)
		{
			v2f o;
			o.pos = UnityObjectToClipPos(v.vertex);
			o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
			o.grabPos = ComputeGrabScreenPos(o.pos);
			return o;
		}
		
		half3 UNPACK_WITH_WEIGHT_INTERNAL(half4 packednormal, half bumpScale)
        {
            #if defined(UNITY_NO_DXT5nm)
                half3 normal = half3(packednormal.xy * 2 - 1, 1);
                normal.xy *= bumpScale;
                normal.z = sqrt(1 - saturate(dot(normal.xy, normal.xy)));
                return normalize(normal);
            #else
                half3 result = half3(packednormal.wy * 2 - 1, 1);
                result.xy *= bumpScale;
                result.z = sqrt(1 - saturate(dot(result.xy, result.xy)));
                return normalize(result);
            #endif
        }
		
		inline half3 UNPACK_INTERNAL(half4 tobeUnpacked) {
		    return UNPACK_WITH_WEIGHT_INTERNAL(tobeUnpacked, 1);
	    }
	    
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

        // This atlas shader has Blend mode Off
        // It grabs from previous texture and outputs the detail merged normal map
		half4 frag(v2f i) : COLOR
		{
		    // Get previous normal map from grab pass,
			half4 previous = tex2Dproj(_GrabTexture, i.grabPos);
		    // Get current texture and mask textures
			half4 current = tex2D(_MainTex, i.uv);
			half4 extra = tex2D(_ExtraTex, i.uv);
            
            // Unpack "previous texture" and "current texture with alpha from color/mask as strength"
            half3 pn = UNPACK_INTERNAL(previous);
            half3 n = UNPACK_WITH_WEIGHT_INTERNAL(current, min(extra.a, _Color.a));
            
            // Blend them as current normal map being detail on previous one
            half3 blended = BlendNormals(pn, n);
            
            // Re-pack blended normal into texture and return.
            return PACK_INTERNAL(blended);
		}
		ENDCG
	}
}

Fallback "Transparent/VertexLit"
} 
