Shader "Hidden/TonemapperLog" {
	Properties {
		_MainTex ("", 2D) = "black" {}
		_SmallTex ("", 2D) = "grey" {}
		_Curve ("", 2D) = "black" {}
	}
	
	CGINCLUDE
	
	#include "UnityCG.cginc"
	 
	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
	};
	
	sampler2D _MainTex;
	sampler2D _SmallTex;
	sampler2D _Curve;

	sampler3D _ClutTex;

	float _Scale;
	float _Offset;

	float _HdrParams;
	float2 intensity;
	float4 _MainTex_TexelSize;
	float _AdaptionSpeed;
	float _RangeScale;

	float _AdaptionEnabled;
	float _AdaptiveMin;
	float _AdaptiveMax;

	float _LogMid;
	float _LinearMid;
	float _DynamicRange;

	v2f vert( appdata_img v ) 
	{
		v2f o;
		o.pos = mul(UNITY_MATRIX_MVP, v.vertex);
		o.uv = v.texcoord.xy;
		return o;
	} 

	float LinToPerceptual(float3 color)
	{
		const float DELTA = 0.001f;
	
		float lum = log(Luminance(color));

 		return (lum);
	}

	float PerceptualToLin(float f)
	{
		return exp(f);
	}


	float4 fragLog(v2f i) : SV_Target 
	{
		float fLogLumSum = 0.0f;
 
		fLogLumSum += LinToPerceptual(tex2D(_MainTex, i.uv + _MainTex_TexelSize.xy * float2(-1,-1)).rgb);		
		fLogLumSum += LinToPerceptual(tex2D(_MainTex, i.uv + _MainTex_TexelSize.xy * float2(1,1)).rgb);		
		fLogLumSum += LinToPerceptual(tex2D(_MainTex, i.uv + _MainTex_TexelSize.xy * float2(-1,1)).rgb);		
		fLogLumSum += LinToPerceptual(tex2D(_MainTex, i.uv + _MainTex_TexelSize.xy * float2(1,-1)).rgb);		

		float avg = fLogLumSum / 4.0;
		return float4(avg, avg, avg, avg);
	}

	float4 fragExp(v2f i) : SV_Target 
	{
		float lum = 0.0f;
		
		lum += tex2D(_MainTex, i.uv  + _MainTex_TexelSize.xy * float2(-1,-1)).x;	
		lum += tex2D(_MainTex, i.uv  + _MainTex_TexelSize.xy * float2(1,1)).x;	
		lum += tex2D(_MainTex, i.uv + _MainTex_TexelSize.xy * float2(1,-1)).x;	
		lum += tex2D(_MainTex, i.uv  + _MainTex_TexelSize.xy * float2(-1,1)).x;	

		lum = PerceptualToLin(lum / 4.0f);

		return float4(lum, lum, lum, saturate(0.0125 * _AdaptionSpeed));
	}
			
	// NOTE/OPTIMIZATION: we're not going the extra CIE detour anymore, but
	// scale with the OUT/IN luminance ratio,this is sooooo much faster 
	
	float4 fragAdaptive(v2f i) : SV_Target 
	{
		float avgLum = tex2D(_SmallTex, i.uv).x;
		float4 color = tex2D (_MainTex, i.uv);
		
		float ratio = _HdrParams / avgLum;
		ratio = max(_AdaptiveMin,min(_AdaptiveMax,ratio));
		ratio = _AdaptionEnabled * ratio + 1.0f*(1.0f-_AdaptionEnabled);

		float3 x = color.rgb * ratio;

		// log color is effectively in gamma space
        color.rgb = _LogMid + log2(x / _LinearMid)  / _DynamicRange;
	
		// apply curve in gamma space
		color.r	= tex2D(_Curve, float2(color.r, 0.5)).r;
		color.g	= tex2D(_Curve, float2(color.g, 0.5)).g;
		color.b	= tex2D(_Curve, float2(color.b, 0.5)).b;

		// apply secondary lookup in gamma space
		color.rgb = tex3D(_ClutTex, color.rgb * _Scale + _Offset).rgb;

		// convert back to linear space
		color.rgb = color.rgb * color.rgb;

		return color;
	}
	
	float4 fragDebug(v2f i) : SV_Target 
	{
		float avgLum = tex2D(_SmallTex, i.uv).x;
		float4 color = tex2D (_MainTex, i.uv);
		
		float ratio = _HdrParams / avgLum;
		ratio = max(_AdaptiveMin,min(_AdaptiveMax,ratio));
		ratio = _AdaptionEnabled * ratio + 1.0f*(1.0f-_AdaptionEnabled);

		float3 x = color.rgb * ratio;

        color.rgb = _LogMid + log2(x / _LinearMid)  / _DynamicRange;

		float minV = min(color.r,min(color.g,color.b));
		float maxV = max(color.r,max(color.g,color.b));

		if (minV < 0)
			color.rgb = float3(0,0,1);

		if (maxV > 1.0)
			color.rgb = float3(1,0,0);

		return color;
	}
	
	ENDCG 
	
Subshader {
 // adaptive reinhhard apply
 Pass {
	  ZTest Always Cull Off ZWrite Off

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment fragAdaptive
      ENDCG
  }

  // 1
 Pass {
	  ZTest Always Cull Off ZWrite Off

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment fragLog
      ENDCG
  }  
  // 2
 Pass {
	  ZTest Always Cull Off ZWrite Off
	  Blend SrcAlpha OneMinusSrcAlpha

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment fragExp
      ENDCG
  }  
  // 3 
 Pass {
	  ZTest Always Cull Off ZWrite Off

	  Blend Off   

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment fragExp
      ENDCG
  }  
  // 4 - debugging
 Pass {
	  ZTest Always Cull Off ZWrite Off

      CGPROGRAM
      #pragma vertex vert
      #pragma fragment fragDebug
      ENDCG
  }
}

Fallback off
	
} // shader
