
Shader "Sine Wave/Hair/Modern Hair V6 Part A" {
	Properties{
		_Color("Main Color", Color) = (1,1,1,1)
		_MainTex("Diffuse (RGB) Alpha (A)", 2D) = "gray" {}
		_Cutoff("Alpha Cut-Off Threshold", Range(0,1)) = 0.5
		_Glossiness("Smoothness", Range(0,1)) = 0.5
		_Metallic("Metallic", Range(0,1)) = 0.0
	}

		SubShader{
			Tags { "RenderType" = "Transparent" "Queue" = "Transparent+1"}
			
			Cull Off
			ZWrite Off

			CGPROGRAM
				#pragma surface surf Standard alpha:fade
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
					clip(-(c.a - _Cutoff));
					o.Albedo = c.rgb;// *float3(1, 0, 0);
					//o.Emission = float3(1, 0, 0);
					o.Metallic = _Metallic * (IN.facing);
					o.Smoothness = _Glossiness * (IN.facing);
					o.Alpha = c.a;
					o.Normal *= -1 * IN.facing;
				}
			ENDCG
		}
			FallBack "Transparent/Cutout/VertexLit"
}