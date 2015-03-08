Shader "UMA/Diffuse Texture" {
    Properties {
	  _MainColor ("Main Color", Color) = (1,1,1,1)
      _MainTex ("Texture", 2D) = "white" {}
      _ExtraTex ("Texture", 2D) = "white" {}
      _MaskTex ("Texture", 2D) = "white" {}
    }
    SubShader {
       Tags { "RenderType" = "Opaque" }
       
      CGPROGRAM
      #pragma surface surf SimpleLambert

      half4 LightingSimpleLambert (SurfaceOutput s, half3 lightDir, half atten) {
          half4 c;
          c.rgb = s.Albedo;
          c.a = s.Alpha;
          return c;
      }

      struct Input {
          float2 uv_MainTex;
      };
      
      float4 _MainColor;
      sampler2D _MainTex;
      sampler2D _ExtraTex;
      sampler2D _MaskTex;
      
      void surf (Input IN, inout SurfaceOutput o) {
         fixed4 c = tex2D (_MainTex, IN.uv_MainTex);
         fixed4 e = tex2D (_ExtraTex, IN.uv_MainTex);
         fixed4 m = tex2D (_MaskTex, IN.uv_MainTex);
         
          o.Emission.rgb = lerp (c, e, m.a);
          o.Emission.rgb = lerp (o.Emission, o.Emission * _MainColor, m.a);
          
         o.Alpha = lerp (c, e, m.a).a;
         o.Alpha = lerp (o.Alpha,o.Alpha * _MainColor.a,m.a);
      }
      ENDCG
    }
    Fallback "Diffuse"
  }