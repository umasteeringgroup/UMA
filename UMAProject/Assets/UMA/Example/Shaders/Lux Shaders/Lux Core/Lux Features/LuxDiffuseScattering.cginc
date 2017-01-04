#ifndef LUX_DIFFUSESCATTERING_INCLUDED
#define LUX_DIFFUSESCATTERING_INCLUDED


// Additional Inputs ------------------------------------------------------------------

fixed4 _DiffuseScatteringCol;
half _DiffuseScatteringBias;
half _DiffuseScatteringContraction;

// Mix Mapping
#if defined(GEOM_TYPE_BRANCH_DETAIL)
	fixed4 _DiffuseScatteringCol2;
    half _DiffuseScatteringBias2;
    half _DiffuseScatteringContraction2;
#endif

// Snow Scatter inputs already declared in "dynamic weather"


// ------------------------------------------------------------------


void Lux_DiffuseScattering(
	inout half3 albedo,
	half3 tangentNormal,
	half3 viewDir,
	half metallic
	#if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL) || defined (_SNOW)
		, inout LuxFragment lux
	#endif
	)
{
	half NdotV = dot(tangentNormal, viewDir);
	NdotV *= NdotV;
	half3 diffuseScatter = 0;
	// Mix Mapping
	#if defined(GEOM_TYPE_BRANCH_DETAIL)
		// Enable/disable scattering based on alpha values
		if(_DiffuseScatteringCol.a + _DiffuseScatteringCol2.a > 0.0) {
			fixed3 scatterColor = lerp(_DiffuseScatteringCol, _DiffuseScatteringCol2, lux.mixmapValue.y);
			half2 scatterBias_Contraction = lerp( half2(_DiffuseScatteringBias, _DiffuseScatteringContraction), half2(_DiffuseScatteringBias2, _DiffuseScatteringContraction2), lux.mixmapValue.y);
			diffuseScatter = scatterColor * (exp2(-(NdotV * scatterBias_Contraction.y)) + scatterBias_Contraction.x);
		}
	#else
	// Regular Detail Blending
		if (_DiffuseScatteringCol.a > 0.0) {
			diffuseScatter = _DiffuseScatteringCol * (exp2(-(NdotV * _DiffuseScatteringContraction)) + _DiffuseScatteringBias);
		}
	#endif
	// Snow Scattering
	#if defined (_SNOW)
		half3 snowScatter = _Lux_SnowScatterColor * (exp2(-(NdotV * _Lux_SnowScatteringContraction)) + _Lux_SnowScatteringBias);
		diffuseScatter = lerp(diffuseScatter, snowScatter, lux.snowAmount.x );
	#endif

	#if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL) || defined (_SNOW)
		diffuseScatter *= 1.0 - lux.waterColor.a * lux.waterAmount.x;
	#endif
	
	albedo += diffuseScatter * (1.0 - metallic);
}

#if defined (_SNOW)
void Lux_DiffuseSnowScattering(
	inout half3 albedo,
	half3 tangentNormal,
	half3 viewDir,
	inout LuxFragment lux
	)
{
	half NdotV = dot(tangentNormal, viewDir);
	NdotV *= NdotV;
	half3 snowScatter = _Lux_SnowScatterColor * (exp2(-(NdotV * _Lux_SnowScatteringContraction)) + _Lux_SnowScatteringBias);
	half3 diffuseScatter = snowScatter * lux.snowAmount.xxx;
	#if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
		diffuseScatter *= 1.0 - lux.waterColor.a * lux.waterAmount.x;
	#endif
	
	albedo += diffuseScatter;
}
#endif

//	------------------------------------------------------------------
//	Surface shader Macro Definitions

#if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL) || defined (_SNOW)
	#define LUX_DIFFUSESCATTERING(albedo, normal, viewDir) \
		Lux_DiffuseScattering(albedo, normal, viewDir, 0.0, lux);
	#define LUX_DIFFUSESCATTERING_METALLIC(albedo, normal, viewDir, metallic) \
		Lux_DiffuseScattering(albedo, normal, viewDir, metallic, lux);
	#define LUX_DIFFUSESNOWSCATTERING(albedo, normal, viewDir) \
		Lux_DiffuseSnowScattering(albedo, normal, viewDir, lux);
#else
// Simple version that does not need the lux surface structure
	#define LUX_DIFFUSESCATTERING(albedo, normal, viewDir) \
		Lux_DiffuseScattering(albedo, normal, viewDir, 0.0);
	#define LUX_DIFFUSESCATTERING_METALLIC(albedo, normal, viewDir, metallic) \
		Lux_DiffuseScattering(albedo, normal, viewDir, metallic);
#endif

#endif