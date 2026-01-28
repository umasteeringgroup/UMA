
Shader "Sine Wave/Hair/Modern Hair V6 Part B" {
	Properties{
		_Color("Main Color", Color) = (1,1,1,1)
		_MainTex("Diffuse (RGB) Alpha (A)", 2D) = "gray" {}
		_Cutoff("Alpha Cut-Off Threshold", Range(0,1)) = 0.5
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0
	}

		SubShader{
			Tags { "RenderType" = "TransparentCutout" }

			Cull Off
			

		Pass {
				ZWrite On
				Cull Off
				ColorMask 0

				CGPROGRAM
				#pragma vertex vert
				#pragma fragment frag

				// vertex shader inputs
				struct appdata
				{
					float4 vertex : POSITION; // vertex position
					float2 uv : TEXCOORD0; // texture coordinate
				};

			// vertex shader outputs ("vertex to fragment")
			struct v2f
			{
				float2 uv : TEXCOORD0; // texture coordinate
				float4 vertex : SV_POSITION; // clip space position
			};

			float _Cutoff;

			// vertex shader
			v2f vert(appdata v)
			{
				v2f o;
				// transform position to clip space
				// (multiply with model*view*projection matrix)
				o.vertex = UnityObjectToClipPos(v.vertex);
				// just pass the texture coordinate
				o.uv = v.uv;
				return o;
			}

			// texture we will sample
			sampler2D _MainTex;

			// pixel shader; returns low precision ("fixed4" type)
			// color ("SV_Target" semantic)
			fixed4 frag(v2f i) : SV_Target
			{
				// sample texture and return it
				fixed4 col = tex2D(_MainTex, i.uv);
				clip(col.a - _Cutoff);
				return col;
			}
			ENDCG
		}

				ColorMask ARGB


			CGPROGRAM
				#pragma surface surf Standard
				#pragma target 3.0

				struct Input
				{
					float2 uv_MainTex;
					float facing : VFACE;
				};

				sampler2D _MainTex;
				float _Cutoff;
				fixed4 _Color;
				half _Glossiness;
				half _Metallic;

				void surf(Input IN, inout SurfaceOutputStandard o)
				{
					fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
					clip(c.a - _Cutoff);
					o.Albedo = c;
					o.Metallic = _Metallic * (IN.facing);
					o.Smoothness = _Glossiness * (IN.facing);
					o.Alpha = c.a;
					o.Normal *= -1 * IN.facing;
				}
			ENDCG
		}
			FallBack "Transparent/Cutout/VertexLit"
}