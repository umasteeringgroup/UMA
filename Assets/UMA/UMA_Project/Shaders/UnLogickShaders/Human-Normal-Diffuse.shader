//	============================================================
//	Name:		UMA/Regular
//	Author: 	Joen Joensen (@UnLogick)
//	============================================================


Shader "UMA/Regular" 
{
Properties {
	_Color ("Main Color", Color) = (1,1,1,1)
	_Gloss ("Gloss", Range (0.03, 4)) = 1.0
	_Specular ("Specular", Range (0.03, 4)) = 1.0
	_MainTex ("Base (RGB) Mask (A)", 2D) = "white" {}
	_BumpTex ("Normal (GA) Specular (R) Gloss (B)", 2D) = "white" {}
	_BRDFTex ("NdotL NdotH (RGBA)", 2D) = "white" {}
}
SubShader { 
	Tags { "RenderType"="Opaque" }
	LOD 400
	
CGPROGRAM
#pragma surface surf PseudoBRDF
//BlinnPhong


sampler2D _BRDFTex; 
sampler2D _MainTex;
sampler2D _BumpTex;
fixed4 _Color;
half _Gloss;
half _Specular;

struct Input {
	float2 uv_MainTex;
	float2 uv_BumpTex;
};


inline half4 LightingPseudoBRDF (SurfaceOutput s, fixed3 lightDir, fixed3 viewDir, fixed atten)
{
	// Half vector
	fixed3 halfDir = normalize (lightDir + viewDir);
	
	// N.L
	fixed NdotL = dot (s.Normal, lightDir);
	// N.H
	fixed NdotH = dot (s.Normal, halfDir);
	
	// remap N.L from [-1..1] to [0..1]
	// this way we can shade pixels facing away from the light - helps to simulate bounce lights
	fixed biasNdotL = NdotL * 0.5 + 0.5;
	
	fixed4 l = tex2D (_BRDFTex, fixed2(biasNdotL, NdotH));

	fixed4 c;
	_LightColor0.rgb = _LightColor0.rgb * atten * 2;
	l.rgb = l.rgb * _LightColor0.rgb;

	c.rgb = s.Albedo * l.rgb * 2 + _SpecColor.rgb * s.Gloss * s.Specular * l.a * _LightColor0.rgb + l.rgb*s.Specular;
	c.rgb = s.Albedo * l.rgb * 2 + l.rgb;
	half gloss = clamp(1-l.a, 0, 1);
	gloss = (1 - gloss * gloss) * s.Gloss;

	//c.rgb += (1 - s.Albedo) * (gloss + l.a * s.Specular) * _LightColor0.rgb;
	//c.rgb += s.Albedo * (gloss + l.a * 4 * l.a * s.Specular) * _LightColor0.rgb;
	c.rgb += (1 - s.Albedo) * (gloss + l.a * 4 * l.a * s.Specular) * _LightColor0.rgb;
	//c.rgb += s.Albedo * s.Specular * l.a * _LightColor0.rgb;
	//c.rgb += l.rgb;
	c.a = 1;
	
	return c;
}


void surf (Input IN, inout SurfaceOutput o) {
	fixed4 tex = tex2D(_MainTex, IN.uv_MainTex);
	fixed4 bump = tex2D(_BumpTex, IN.uv_BumpTex);
	o.Albedo = tex.rgb * _Color.rgb;
	o.Gloss = bump.r * _Gloss;
	o.Specular = bump.b * _Specular;
	fixed3 myNormal;
    myNormal.xy = bump.wy * 2 - 1;
    myNormal.z = sqrt(1 - myNormal.x*myNormal.x - myNormal.y * myNormal.y);
	o.Normal = myNormal;
}
ENDCG
}

FallBack "UMA/Bumped Specular"
}
