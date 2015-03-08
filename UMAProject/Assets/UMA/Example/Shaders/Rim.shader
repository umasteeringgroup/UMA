Shader "UMA/Rim"{ 
    Properties {
      _MainTex ("Texture", 2D) = "white" {}
      _BumpTex ("Bumpmap", 2D) = "bump" {}
      _RimColor ("Rim Color", Color) = (0.26,0.19,0.16,0.0)
      _RimPower ("Rim Power", Range(0.5,8.0)) = 3.0
    }
    SubShader {
      Tags { "RenderType" = "Opaque" }
      CGPROGRAM
      #pragma surface surf Lambert
      struct Input {
          float2 uv_MainTex;
          float2 uv_BumpTex;
          float3 viewDir;
      };
      sampler2D _MainTex;
      sampler2D _BumpTex;
      float4 _RimColor;
      float _RimPower;
	  
      void surf (Input IN, inout SurfaceOutput o) {
          o.Albedo = tex2D (_MainTex, IN.uv_MainTex).rgb;
          fixed4 bump = tex2D(_BumpTex, IN.uv_BumpTex);
          fixed3 myNormal;
		  myNormal.xy = bump.wy * 2 - 1;
		  myNormal.z = sqrt(1 - myNormal.x*myNormal.x - myNormal.y * myNormal.y);
		  o.Normal = myNormal;
          half rim = 1.0 - saturate(dot (normalize(IN.viewDir), o.Normal));
          o.Emission = _RimColor.rgb * pow (rim, _RimPower);
      }
      ENDCG
    } 
    Fallback "Diffuse"
  }