Shader "UMA/Bumped Specular Rim" {
Properties {
	_SpecColor ("Specular Color", Color) = (0.5, 0.5, 0.5, 1)
	_MainTex ("Base (RGB) Gloss (A)", 2D) = "white" {}
	_BumpTex ("Normalmap", 2D) = "bump" {}
	_Gloss ("Gloss", Range (0.03, 4)) = 1.0
	_Specular ("Specular", Range (0.03, 4)) = 1.0
	_RimColor ("Rim Color", Color) = (0.26,0.19,0.16,0.0)
    _RimPower ("Rim Power", Range(0.5,8.0)) = 3.0
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
float4 _RimColor;
float _RimPower;

struct Input {
	float2 uv_MainTex;
	float2 uv_BumpTex;
	float3 viewDir;
};

void surf (Input IN, inout SurfaceOutput o) {
	fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
	o.Albedo = c.rgb;

	fixed4 bump = tex2D(_BumpTex, IN.uv_BumpTex);	
	
	//Looks like Gloss and Specular are inverted, based one standard specular and gloss definition.
	o.Gloss = bump.r * _Specular;
	o.Specular = bump.b * _Gloss * 2;
	fixed3 myNormal;
    myNormal.xy = bump.wy * 2 - 1;
    myNormal.z = sqrt(1 - myNormal.x*myNormal.x - myNormal.y * myNormal.y);
	o.Normal = myNormal;
	
	
	half rim = 1.0 - saturate(dot(normalize(IN.viewDir), o.Normal));
	o.Emission = _RimColor.rgb * pow (rim, _RimPower);
}
ENDCG
}

FallBack "Diffuse"
}
