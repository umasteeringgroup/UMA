#ifndef LUX_DYNAMICWEATHER_INCLUDED
#define LUX_DYNAMICWEATHER_INCLUDED


//  Additional Inputs ------------------------------------------------------------------

#if defined (_SNOW) || defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
    half3           _Lux_RainfallRainSnowIntensity;
    half2           _Lux_WaterToSnow;
    half2           _Lux_SnowMelt;          // x: SnowMelt / y: SnowMelt 2^(-10 * (x))
    sampler2D       _Lux_SnowWaterBump;     // Shared by Snow and water flow
    sampler2D       _Lux_SnowMask;

    // Wetness - Inputs from script
    half4           _Lux_WaterFloodlevel; // x: cracks / y: puddles / z: wetness darkening / w: wetness smoothness / 
#endif

#if defined (_SNOW)
    //  Snow - per material
    half            _SnowSlopeDamp;
    half2           _SnowAccumulation;      // x: Material Constant / y: Global Factor
    //half            _OcclusionInfluence;
    //half            _HeightMapInfluence;
    half2           _SnowTiling;
    half            _SnowNormalStrength;
    half2           _SnowMaskTiling;
    half2           _SnowDetailTiling;
    half            _SnowDetailStrength;
    half            _SnowOpacity;

    //  Snow - Inputs from script
    float4          _Lux_SnowWaterBump_TexelSize;

    float2          _Lux_SnowHeightParams;  // x: start height / y: blend zone
    half            _Lux_SnowAmount;
    fixed4          _Lux_SnowColor;
    fixed3          _Lux_SnowSpecColor;
    fixed4          _Lux_SnowScatterColor;
    half            _Lux_SnowScatteringContraction;
    half            _Lux_SnowScatteringBias;
#endif

#if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
    //  Wetness - per material
    half            _WaterSlopeDamp;
    half4           _WaterAccumulationCracksPuddles = half4(0,1,0,1);   // Cracks: x: Constant for Material, y: Multiplier for Script Input, etc
    // mix mapping
    #if defined (GEOM_TYPE_BRANCH_DETAIL)
        half4       _WaterAccumulationCracksPuddles2 = half4(0,1,0,1);
    #endif
    half4           _WaterColor;
    half4           _WaterColor2;

    #if defined (GEOM_TYPE_MESH) || !defined(LUX_STANDARD_CORE_INCLUDED)
        half        _PuddleMaskTiling;
    #endif

    // Properties only needed by flow enabled wetness shaders
    #if defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
        half        _Lux_FlowNormalTiling;
        half        _Lux_FlowSpeed;
        half        _Lux_FlowInterval;
        half        _Lux_FlowNormalStrength;
        half        _Lux_FlowRefraction;
    #endif

    // Properties only needed by ripple enabled wetness shaders
    #if defined (_WETNESS_RIPPLES) || defined (_WETNESS_FULL)
        sampler2D   _Lux_RainRipples;
        float4      _Lux_RainRipples_ST;
        float4      _Lux_RainRipples_TexelSize;
        half        _Lux_RippleAnimSpeed;
        half        _Lux_RippleTiling;
        half        _Lux_RippleRefraction;
    #endif
#endif





//  //////////////////////////////////////////
//  Water

//  Based on the work of Sébastien Lagarde: http://seblagarde.wordpress.com/2013/04/14/water-drop-3b-physically-based-wet-surfaces/

#if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
    //  Accumulate water in cracks(x) and puddles(y)
    inline half2 ComputeWaterAccumulation(LuxFragment lux)
    {
        half2 AccumulatedWaters;
        // Tweak script based accumulation according to the defined factors and add constant accumulated water from material
        // mix mapping
        #if defined (GEOM_TYPE_BRANCH_DETAIL)
            half2 WaterFloodlevel = ( _Lux_WaterFloodlevel.xy * (_WaterAccumulationCracksPuddles.yw * lux.mixmapValue.x + _WaterAccumulationCracksPuddles2.yw * lux.mixmapValue.y)
                                             + lux.localWater );
        #else
            half2 WaterFloodlevel = ( _Lux_WaterFloodlevel.xy * _WaterAccumulationCracksPuddles.yw + lux.localWater );
        #endif
        // Get the "size" of the accumulated water in cracks
        AccumulatedWaters.x = min(WaterFloodlevel.x, WaterFloodlevel.x - lux.height );
        // Get the size of the accumlated water in puddles taking into account the marging (0.4 constant here).
        // First we shrink the Puddle by dampedWorldNormal / In order to improve the edges we also take the Heightmap into account.
        // Damp WaterAccumulation according to the worldNormal.y Component
        half dampedWorldNormal = saturate(lux.worldNormalFace.y + (1.0 - _WaterSlopeDamp));  
        //AccumulatedWaters.y = saturate((WaterFloodlevel.y * lux.worldNormalFace.y - (1.0 - lux.puddleMaskValue) - lux.height) / 0.4);
        AccumulatedWaters.y = saturate((WaterFloodlevel.y * dampedWorldNormal - (1.0 - lux.puddleMaskValue) - lux.height) / 0.4);
        AccumulatedWaters = float2(max(AccumulatedWaters.x, AccumulatedWaters.y), max(AccumulatedWaters.x, AccumulatedWaters.y * 2) * 0.5) * dampedWorldNormal;
        return saturate(AccumulatedWaters.xy * half2(1.0, AccumulatedWaters.y));
    }
#endif

#if defined (_WETNESS_RIPPLES) || defined (_WETNESS_FULL)
    //  Samples and returns animated ripple normals / Called by: AddWaterRipples 
    inline half2 ComputeRipple(float2 UV, float2 dx, float2 dy, float CurrentTime, half Weight)
    {
        float4 Ripple = tex2Dgrad(_Lux_RainRipples, UV, dx, dy);
        Ripple.yz = Ripple.yz * 2 - 1; // Decompress Normal
        half DropFrac = frac(Ripple.w + CurrentTime); // Apply time shift
        half TimeFrac = DropFrac - 1.0f + Ripple.x;
        half DropFactor = saturate(0.2f + Weight * 0.8f - DropFrac);
        half FinalFactor = DropFactor * Ripple.x * sin(clamp(TimeFrac * 9.0f, 0.0f, 3.0f) * UNITY_PI);
        return Ripple.yz * FinalFactor;
    }

    //  Add animated Ripples to areas where the Water Accumulation is high enough
    //  Returns the tweaked and adjusted Ripple Normal
    inline half3 AddWaterRipples(LuxFragment lux, float2 dx, float2 dy)
    {
        half2 Weights = _Lux_RainfallRainSnowIntensity.y - float2(0, 0.25);
        float animSpeed =  _Time.y * _Lux_RippleAnimSpeed;

        dx *= _Lux_RippleTiling; // * 3;
        dy *= _Lux_RippleTiling; // * 3;

        // dx = length( dx ) / (_Lux_RainRipples_TexelSize.z / _Lux_RippleTiling * 1.0010); //     TexelsPerMeterInfo
        // dy = length( dy ) / (_Lux_RainRipples_TexelSize.w / _Lux_RippleTiling * 1.0010); //     TexelsPerMeterInfo

        half2 Ripple1 = ComputeRipple( (lux.worldPos.xz + lux.offset) * _Lux_RippleTiling , dx, dy, animSpeed, Weights.x);
        half2 Ripple2 = ComputeRipple( (lux.worldPos.xz + lux.offset + float2(0.55f, 0.37f)) * _Lux_RippleTiling , dx, dy, animSpeed * 0.85f, Weights.y); 
        half3 rippleNormal = half3( Weights.x * Ripple1.xy + Weights.y * Ripple2.xy, 1.0);
        // Blend and fade out Ripples
        // float reduction = saturate(abs(dx * 100) - 0.35);
        return lerp( half3(0,0,1), rippleNormal, lux.waterAmount.y * saturate(lux.worldNormalFace.y) ); // * (1.0 - reduction) ); // * (1.0 - _Lux_WaterToSnow.y) );
    }
#endif

// Add water flow based on slope
// http://www.heathershrewsbury.com/dreu2010/wp-content/uploads/2010/07/FlowVisualizationUsingMovingTextures.pdf
// http://www.roxlu.com/downloads/scholar/002.fluid.water_technology_of_uncharted_gdc_2012.pdf
#if defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
    inline half3 AddWaterFlow(LuxFragment lux, float2 dx, float2 dy)
    {
        float2 flowUV = lux.extrudedUV * _Lux_FlowNormalTiling;
        // float2 flowDirection = lux.flowDirection * _Lux_FlowSpeed * objectScale_TextureScaleRatio * (1.0 - _Lux_WaterToSnow.y * saturate(_WaterAccumulationCracksPuddles.w) );
        float speedDampByWaterToSnow = saturate(1.0 - _Lux_WaterToSnow.y + lux.localWater.y );
        float2 flowDirection = lux.waterFlowDir * _Lux_FlowSpeed * speedDampByWaterToSnow;
        // Time
        float  timeInt = (_Time.y ) / (_Lux_FlowInterval);
        float2 fTime = frac( float2(timeInt, timeInt + 0.5) );
        half fade = abs( (2.0 * frac(timeInt) ) - 1.0);

        half4 flowBump = tex2Dgrad(_Lux_SnowWaterBump, flowUV + fTime.xx * flowDirection, dx, dy);
        flowBump = lerp(flowBump, tex2Dgrad(_Lux_SnowWaterBump, flowUV + fTime.yy * flowDirection, dx, dy), fade);
        // Unpack scaled normal
        half3 finalflowBump = Lux_UnpackScaleNormal(flowBump.ag, _Lux_FlowNormalStrength);
        // Mask water flow according to overall wetness and scale down flow normal according to speed (worldNormalFace.y)
        half worldNormalDamp = saturate(lux.worldNormalFace.y);
        return lerp(half3(0,0,1), finalflowBump, (1 - worldNormalDamp * worldNormalDamp) * lux.waterAmount.x );
    }
#endif

#if defined (_SNOW)
//  //////////////////////////////////////////
//  Snow

//  Accumulates snow only based on different masks to make snow independent from material's input like height or occlusion
//  Returns the snow mask (x) and a slightly bigger mask for melted snow (y)
    inline half2 ComputeSnowAccumulation (LuxFragment lux)
    {

        half baseSnowAmount = saturate(_Lux_SnowAmount * _SnowAccumulation.y + _SnowAccumulation.x);

    //  Sample the Mask Texture with different tiling factors
        half4 snowMask = tex2D(_Lux_SnowMask, (lux.extrudedUV.xy * _SnowMaskTiling ));
        half4 detailSnowMask = tex2D (_Lux_SnowMask, lux.extrudedUV.xy * _SnowDetailTiling);

    //  Combine the given snow amount, world normal and snow height fade
        #if !defined (UNITY_PASS_META)
        //&& !defined(TESSELLATION_ON)
            half2 snowAmount = saturate ( baseSnowAmount - _SnowSlopeDamp * saturate(1.0 - lux.worldNormal.y - baseSnowAmount * 0.25) ); // was: 0.75
        #else
            half2 snowAmount = saturate ( baseSnowAmount - _SnowSlopeDamp * saturate(1.0 - lux.worldNormalFace.y - baseSnowAmount * 0.25) ); // was: 0.75
        #endif

        baseSnowAmount = snowAmount.x * lux.snowHeightFadeState;
    
    //  Add custom snowmask 
        snowAmount *= lux.uniqueSnowMaskValue;
    //  Sharpen snow (due to slope damp and unique mask)
        snowAmount = saturate(snowAmount * 4.0 * (2.0 - baseSnowAmount));

        half2 snowVal = 1.0 - 1.0 * baseSnowAmount; // * lux.snowHeightFadeState;
        half2 finalSnowMask = snowMask.bb * detailSnowMask.bb; //snowMask.gg * detailSnowMask.gg;
    
    //  We have to calculate 2 snow values: one for the actual snow (x) and one for the melted one (y)
        finalSnowMask = smoothstep(
            // lower bound
            snowVal - baseSnowAmount * 
                half2(
                    1.0 - baseSnowAmount,
                    4.0 - baseSnowAmount             // widen the mask, might be exposed to script
            ),
            // upper bound
            snowVal + half2(0.01, 0.1),
            // param
            half2 ( 
                finalSnowMask.x,
                finalSnowMask.x * (1.0 + _Lux_SnowMelt.x * _SnowAccumulation.y)                                // sharpen
            )
        ); 

        snowAmount = finalSnowMask * snowAmount * lerp(detailSnowMask.bb, 1.0, baseSnowAmount);   
        return snowAmount;
    }


    inline half ComputeSnowAccumulationVertex (float2 texCoords, float3 normal) {
        half baseSnowAmount = saturate(_Lux_SnowAmount * _SnowAccumulation.y + _SnowAccumulation.x);
        //  Sample the Mask Texture with different tiling factors
        half4 snowMask = tex2Dlod(_Lux_SnowMask, float4(texCoords * _SnowMaskTiling, 0, 0) );
        half4 detailSnowMask = tex2Dlod(_Lux_SnowMask, float4(texCoords * _SnowDetailTiling, 0, 0) );

        //  Combine the given snow amount, world normal and snow height fade
        half snowAmount = saturate ( baseSnowAmount - _SnowSlopeDamp * (1.0 - UnityObjectToWorldNormal(normal).y - baseSnowAmount * 0.75) );
        //  Sharpen snow (due to slope damp and unique mask)
        snowAmount = saturate(snowAmount * 4.0 * (2.0 - baseSnowAmount));

        half2 snowVal = 1.0 - 1.0 * baseSnowAmount;
        half finalSnowMask = snowMask.b * detailSnowMask.b;
    
    //  We have to calculate 2 snow values: one for the actual snow (x) and one for the melted one (y)
        finalSnowMask = smoothstep(
            // lower bound
            snowVal - baseSnowAmount * (1.0 - baseSnowAmount),
            // upper bound
            snowVal + 0.01,
            // param
            finalSnowMask.x
        ); 
        snowAmount = finalSnowMask * snowAmount * lerp(detailSnowMask.b, 1.0, baseSnowAmount); 
        return snowAmount;
    }
#endif


//  /////////////////////////////////////////////
//  Main entry point for dynamic weather Part 1

void Lux_DynamicWeather ( inout LuxFragment lux)
{
//  Wetness
    #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
        
        // Calculate the constant water per material x: cracks / y: puddles / might be greater than 1!
        // mix mapping
        #if defined (GEOM_TYPE_BRANCH_DETAIL)
            lux.localWater = _WaterAccumulationCracksPuddles.xz * lux.mixmapValue.xx + _WaterAccumulationCracksPuddles2.xz * lux.mixmapValue.yy;
        #else
            lux.localWater = _WaterAccumulationCracksPuddles.xz;
        #endif 

        // Calculate water distribuition / returns x: water in cracks and puddles / y: water in puddles only
        lux.waterAmount = ComputeWaterAccumulation (lux);
        lux.waterNormal = half3(0,0,1);
        #if !defined (UNITY_PASS_META)
            half3 rippleNormal = half3(0,0,1);
            half3 flowNormal = rippleNormal;
            float2 dx = ddx(lux.worldPos.xz);
            float2 dy = ddy(lux.worldPos.xz);

            #if defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                float2 dx_flow = dx * _Lux_FlowNormalTiling;
                float2 dy_flow = dy * _Lux_FlowNormalTiling;
            #endif

            // No branching when using tessellation as otherwise it breaks
            #if !defined(TESSELLATION_ON)
                UNITY_BRANCH
                if (lux.detailBlendState > 0.001 ) {
            #endif
                //  Compute ripple normal
                    #if defined (_WETNESS_RIPPLES) || defined (_WETNESS_FULL)
                        UNITY_BRANCH
                        if (_Lux_RainfallRainSnowIntensity.y > 0.0) {
                            // Sample the animated ripple normal
                            rippleNormal = AddWaterRipples (lux, dx, dy);
                        }
                    #endif
                //  Compute flow normal
                    #if defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                        // No branching when using tessellation as otherwise it breaks
                        #if !defined(TESSELLATION_ON)
                            // Ignores material multiplier AND material constant set to 0 – but in this case it would be easier to disable water flow... 
                            UNITY_BRANCH
                            if (_Lux_WaterFloodlevel.y + lux.localWater.y > 0.0) {
                                // Sample the animated flow normal
                                flowNormal = AddWaterFlow (lux, dx_flow, dy_flow);
                            }
                        #else
                            if (_Lux_WaterFloodlevel.y + lux.localWater.y > 0.0) {
                                // Sample the animated flow normal
                                flowNormal = AddWaterFlow (lux, dx_flow, dy_flow);
                            }
                        #endif
                    #endif
                //  Combine and fade out waterNormal
                    lux.waterNormal = lerp(lux.waterNormal, normalize(half3(rippleNormal.xy + flowNormal.xy, rippleNormal.z * flowNormal.z)), lux.detailBlendState);
            #if !defined(TESSELLATION_ON)
                }
            #endif
        #endif
    #endif 

//  Snow
    #if defined (_SNOW)
        // Calculate snow distribution based on the given height
        lux.snowHeightFadeState = saturate((lux.worldPos.y - _Lux_SnowHeightParams.x) / _Lux_SnowHeightParams.y);
        lux.snowHeightFadeState = sqrt(lux.snowHeightFadeState);
        // Calculate the accumulated snow / Returns x: mask or amount for snow / y: mask for melted snow
        lux.snowAmount = ComputeSnowAccumulation (lux);
        // Skip detail calculations in the meta pass
        #if !defined (UNITY_PASS_META) && !defined (DO_NOT_REFRACT_UVS)
            // Add melted snow to wetnessMask
            // Do not let melted snow flood the surface (* 0.75)
            // lux.waterAmount.x = saturate( lux.waterAmount.x + saturate (2.25 * lux.snowAmount.y * _Lux_SnowMelt.y) * _Lux_SnowAmount * _SnowAccumulation.y);
            lux.waterAmount.x = saturate( lux.waterAmount.x + saturate (1.75 * lux.snowAmount.y * _Lux_SnowMelt.y) * 0.75 ); // * _Lux_SnowAmount * _SnowAccumulation.y);
            // Add refraction from waterNormal to the main uvs
            #if defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                lux.finalUV += (
                    #if defined (_WETNESS_RIPPLES) || defined (_WETNESS_FULL)
                        rippleNormal.xyxy * _Lux_RippleRefraction
                    #endif
                    #if defined (_WETNESS_FULL)
                        +
                    #endif
                    #if defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                        flowNormal.xyxy * _Lux_FlowRefraction
                    #endif
                        ) * lux.waterAmount.y * (1 - saturate(2 * lux.snowAmount.x)) * saturate(1 - _Lux_WaterToSnow.x + lux.localWater.y ) * lux.detailBlendState;
            #endif
        #endif
    // If snow is disabled simply add refraction from waterNormal to the main uvs
    #else
        #if !defined (UNITY_PASS_META) && !defined (DO_NOT_REFRACT_UVS)
            // Add refraction from waterNormal to the final uvs
            #if defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                lux.finalUV  += (
                    #if defined (_WETNESS_RIPPLES) || defined (_WETNESS_FULL)
                        rippleNormal.xyxy * _Lux_RippleRefraction
                    #endif
                    #if defined (_WETNESS_FULL)
                        +
                    #endif
                    #if defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                        flowNormal.xyxy * _Lux_FlowRefraction
                    #endif
                    ) * lux.waterAmount.y * lux.detailBlendState;
            #endif
        #endif
    #endif
}

//  /////////////////////////////////////////////
//  Dynamic weather Part 2

void ApplySnowAndWetness (
    inout LuxFragment lux,
    inout half3 diffColor,
    inout half3 specColor,
    inout half oneMinusRoughness,
    inout half occlusion,
    inout half3 emission
    #if defined (LOD_FADE_PERCENTAGE)
        , inout half translucency
    #endif
)
{
    half suppressionFactor = 1;

//  Wetness
    #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL) || defined (_SNOW)

        // For whatever reason dx9 does not like diffColor to be calculated inside the if (although there are no texture lookup there)
        // So we do it less effective and up front
        #if defined (SHADER_API_D3D9)
            half porosity = saturate((((1 - oneMinusRoughness) - 0.5)) / 0.4);
            half metalness = saturate((dot(specColor, 0.33) * 1000 - 500) );
            half porosityFactor = lerp(1, 0.3, saturate((1 - metalness) * porosity));

            // Get thin layer of Wetness
            half3 wetness = _Lux_WaterFloodlevel.zww;
            #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                wetness *= saturate(lux.worldNormal.y + (1.0 - _WaterSlopeDamp));
            #endif
            // wetness.yz control smoothness/normal – so they get influenced by porosity
            wetness.yz *= (1-porosityFactor);
            wetness.z *= 0.5;
            wetness = max(lux.waterAmount.xxx, wetness);

            diffColor *= lerp(1.0, porosityFactor, lux.waterAmount.x);
        #endif  
    
        #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
//  TODO no mix mapping?
        UNITY_BRANCH
        if (_Lux_WaterFloodlevel.x + _Lux_WaterFloodlevel.y + _Lux_WaterFloodlevel.z + lux.localWater.x + lux.localWater.y > 0.0 || _Lux_WaterToSnow.x < 1.0) {
        #else
        UNITY_BRANCH
        if (_Lux_WaterToSnow.x < 1.0) {
        #endif

            // All other apis do not have any problems, so they can do it only if really needed. See above.
            #if !defined (SHADER_API_D3D9)
                half porosity = saturate((((1 - oneMinusRoughness) - 0.5)) / 0.4);
                half metalness = saturate((dot(specColor, 0.33) * 1000 - 500) );
                half porosityFactor = lerp(1, 0.2, saturate((1 - metalness) * porosity));

                // Get thin layer of Wetness
                half3 wetness = _Lux_WaterFloodlevel.zww;
                #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                    wetness *= saturate(lux.worldNormal.y + (1.0 - _WaterSlopeDamp));
                #endif
                // wetness.yz control smoothness/normal – so they get influenced by porosity
                wetness.yz *= (1-porosityFactor);
                wetness.z *= 0.5;
                wetness = max(lux.waterAmount.xxx, wetness);

                // Lerp all outputs towards water
                diffColor *= lerp(1.0, porosityFactor, wetness.x); //lux.waterAmount.x);
            #endif
            
            // Water color is only available if wetness is enabled
            #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
            // Add Water color (thin layer wetness does not influence water color)
                // mix mapping
                #if defined(GEOM_TYPE_BRANCH_DETAIL)
                    lux.waterColor = half4(_WaterColor.rgb, 1) * _WaterColor.a * lux.mixmapValue.x + half4(_WaterColor2.rgb, 1) * _WaterColor2.a * lux.mixmapValue.y;
                #else
                    lux.waterColor = _WaterColor;
                #endif
                diffColor = lerp(diffColor, lux.waterColor.rgb, lux.waterColor.a * lux.waterAmount.x);
                occlusion = lerp(occlusion, 1, lux.waterColor.a * lux.waterAmount.x);
                suppressionFactor = 1.0 - lux.waterColor.a * lux.waterAmount.x;
            #endif
            lux.tangentNormal = lerp(lux.tangentNormal, lux.waterNormal, wetness.z );

            half wetSmoothness = lerp(oneMinusRoughness, lerp(0.85, 0.60, porosityFactor), wetness.y ); //lux.waterAmount.x);
            oneMinusRoughness = lerp(wetSmoothness, 0.9, wetness.y ); //lux.waterAmount.x);
            // spec color of water is pretty low
            specColor = lerp(specColor, half3(0.02, 0.02, 0.02), wetness.y ); //lux.waterAmount.x);    // fixed for ps4
        }
    #endif

//  Snow
    #if defined (_SNOW)
        diffColor = lerp(diffColor, _Lux_SnowColor.rgb, lux.snowAmount.x); // fixed for ps4
        // Tweak occlusion and emission according to snowAmount
        occlusion = lerp(occlusion, 1, lux.snowAmount.x);
        suppressionFactor = lerp(suppressionFactor, 1.0 - lux.snowAmount.x * _SnowOpacity, lux.snowAmount.x);
        
        #if !defined (UNITY_PASS_META)
            #if defined (_PARALLAXMAP)
                float4 i_tex_snow = lerp(lux.extrudedUV.xyxy, lux.extrudedUV.xyxy - lux.offset.xyxy / float4(_SnowTiling, _SnowDetailTiling), lux.snowAmount.x * 0.5) * float4(_SnowTiling, _SnowDetailTiling);
            #else
                float4 i_tex_snow = lux.extrudedUV.xyxy * float4(_SnowTiling, _SnowDetailTiling);
            #endif
            // We better use the base uvs here to calculate dx and dy in order to get less discontinueties.
            float2 i_tex_snow_dx = lux.baseUV.xy * _SnowTiling;
            float2 dx = ddx( i_tex_snow_dx.xy);
            float2 dy = ddy( i_tex_snow_dx.xy);
            // Smooth the normalBlendFactor by taking the outer bounds or smooth snowmask into account
            half normalBlendFactor = (lux.snowAmount.x * lux.snowAmount.y);
            half4 combinedNormalSmoothness = tex2Dgrad(_Lux_SnowWaterBump, i_tex_snow.xy , dx.xy, dy.xy );
            lux.snowNormal = Lux_UnpackScaleNormal (combinedNormalSmoothness.ag, normalBlendFactor * saturate(_Lux_SnowAmount * _Lux_WaterToSnow * _SnowAccumulation.y + _SnowAccumulation.x) * _SnowNormalStrength ); // * _SnowNormalStrength );

            UNITY_BRANCH
            if (lux.detailBlendState > 0.001 ) {
                half3 snowdetailNormal = Lux_UnpackScaleNormal (  tex2Dgrad(_Lux_SnowMask, i_tex_snow.zw, dx.xy * _SnowDetailTiling , dy.xy * _SnowDetailTiling ).ag, _SnowDetailStrength * lux.detailBlendState  );
                lux.snowNormal = Lux_BlendNormals(lux.snowNormal, snowdetailNormal );   
            }
            lux.tangentNormal = lerp (lux.tangentNormal, lux.snowNormal, normalBlendFactor );
            // We skip snow smoothness for meta pass currently
            oneMinusRoughness = lerp(oneMinusRoughness, combinedNormalSmoothness.b, lux.snowAmount.x);
            specColor = lerp(specColor, _Lux_SnowSpecColor.rgb, lux.snowAmount.x); // fixed for ps4
        #endif
    #endif
    
    emission *= suppressionFactor;
    #if defined (LOD_FADE_PERCENTAGE)
        translucency *= suppressionFactor;
    #endif
}


// -------------------------------------------------------------------------------------

// Macro Definitions for custom surface shaders

#if !defined(LUX_STANDARD_CORE_INCLUDED)

    #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)

        // Single sided geometry
        #if defined (EFFECT_HUE_VARIATION)

            #if defined (_SNOW)
                #define LUX_INIT_DYNAMICWEATHER_SINGLESIDED(puddleMaskVal, snowMaskVal, perPixelTangentNormal, flipFacingVal) \
                    lux.worldNormalFace = WorldNormalVector (IN, half3(0,0,1) * flipFacingVal); \
                    lux.worldNormal = WorldNormalVector (IN, lerp(perPixelTangentNormal, half3(0,0,1), saturate ((_Lux_SnowAmount * _SnowAccumulation.y + _SnowAccumulation.x) * 0.5) ) * flipFacingVal ); \
                    lux.puddleMaskValue = puddleMaskVal; \
                    lux.uniqueSnowMaskValue = snowMaskVal; \
                    Lux_DynamicWeather(lux);
            #else
                #define LUX_INIT_DYNAMICWEATHER_SINGLESIDED(puddleMaskVal, snowMaskVal, perPixelTangentNormal, flipFacingVal) \
                    lux.worldNormalFace = WorldNormalVector (IN, half3(0,0,1) * flipFacingVal); \
                    lux.puddleMaskValue = puddleMaskVal; \
                    Lux_DynamicWeather(lux);
            #endif  

        // Regular geometry
        #else
            #if defined (_SNOW)
                #define LUX_INIT_DYNAMICWEATHER(puddleMaskVal, snowMaskVal, perPixelTangentNormal) \
                    lux.worldNormalFace = WorldNormalVector (IN, half3(0,0,1)); \
                    lux.worldNormal = WorldNormalVector (IN, lerp(perPixelTangentNormal, half3(0,0,1), saturate ((_Lux_SnowAmount * _SnowAccumulation.y + _SnowAccumulation.x) * 0.5) ) ); \
                    lux.puddleMaskValue = puddleMaskVal; \
                    lux.uniqueSnowMaskValue = snowMaskVal; \
                    Lux_DynamicWeather(lux);
            #else
                #define LUX_INIT_DYNAMICWEATHER(puddleMaskVal, snowMaskVal, perPixelTangentNormal) \
                    lux.worldNormalFace = WorldNormalVector (IN, half3(0,0,1)); \
                    lux.puddleMaskValue = puddleMaskVal; \
                    Lux_DynamicWeather(lux);
            #endif 
        #endif

    #elif defined (_SNOW)
        // Single sided geometry
        #if defined (EFFECT_HUE_VARIATION)
            #define LUX_INIT_DYNAMICWEATHER_SINGLESIDED(puddleMaskVal, snowMaskVal, perPixelTangentNormal, flipFacingVal) \
                lux.worldNormalFace = WorldNormalVector (IN, half3(0,0,1) * flipFacingVal); \
                lux.worldNormal = WorldNormalVector (IN, lerp(perPixelTangentNormal, half3(0,0,1), saturate ((_Lux_SnowAmount * _SnowAccumulation.y + _SnowAccumulation.x) * 0.5) ) * flipFacingVal ); \
                lux.uniqueSnowMaskValue = snowMaskVal; \
                Lux_DynamicWeather(lux);
        // Regular geometry
        #else
            #define LUX_INIT_DYNAMICWEATHER(puddleMaskVal, snowMaskVal, perPixelTangentNormal) \
                lux.worldNormalFace = WorldNormalVector (IN, half3(0,0,1)); \
                lux.worldNormal = WorldNormalVector (IN, lerp(perPixelTangentNormal, half3(0,0,1), saturate ((_Lux_SnowAmount * _SnowAccumulation.y + _SnowAccumulation.x) * 0.5) ) ); \
                lux.uniqueSnowMaskValue = snowMaskVal; \
                Lux_DynamicWeather(lux);
        #endif
    #else
        #error You have to at least enable either snow or wetness
    #endif


    #if !defined (LOD_FADE_PERCENTAGE)
        #define LUX_APPLY_DYNAMICWEATHER \
            lux.tangentNormal = o.Normal; \
            ApplySnowAndWetness(lux, o.Albedo, o.Specular, o.Smoothness, o.Occlusion, o.Emission); \
            o.Normal = lux.tangentNormal;
    #else
        #define LUX_APPLY_DYNAMICWEATHER \
            lux.tangentNormal = o.Normal; \
            ApplySnowAndWetness(lux, o.Albedo, o.Specular, o.Smoothness, o.Occlusion, o.Emission, o.Translucency); \
            o.Normal = lux.tangentNormal;
    #endif

#endif


#endif