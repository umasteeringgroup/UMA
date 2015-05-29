Shader "UMA/Bumped Specular" {
Properties {
	_SpecColor ("Specular Color", Color) = (0.5, 0.5, 0.5, 1)
	_MainTex ("Base (RGB)", 2D) = "white" {}
	_BumpTex ("Normalmap(GA), Specular (R), Gloss (B)", 2D) = "bump" {}
	_Gloss ("Gloss", Range (0.03, 4)) = 1.0
	_Specular ("Specular", Range (0.03, 4)) = 1.0
}
SubShader { 
	Tags { "RenderType"="Opaque" }
	LOD 400
	
CGPROGRAM
#pragma surface surf BlinnPhong
#pragma exclude_renderers flash

sampler2D _MainTex;
sampler2D _BumpTex;
half _Gloss;
half _Specular;

struct Input {
	float2 uv_MainTex;
	float2 uv_BumpTex;
};

void surf (Input IN, inout SurfaceOutput o) {
	o.Albedo = tex2D(_MainTex, IN.uv_MainTex);
	fixed4 bump = tex2D(_BumpTex, IN.uv_BumpTex);	
	
	//Looks like Gloss and Specular are inverted, based one standard specular and gloss definition.
	o.Gloss = bump.r * _Specular;
	o.Specular = bump.b * _Gloss;
	
	fixed3 myNormal;
    myNormal.xy = bump.wy * 2 - 1;
    myNormal.z = sqrt(1 - myNormal.x*myNormal.x - myNormal.y * myNormal.y);
	o.Normal = myNormal;
}
ENDCG
}

FallBack "Diffuse"
}
