#ifndef LUX_PARALLAX_INCLUDED
#define LUX_PARALLAX_INCLUDED

//  Additional Inputs ------------------------------------------------------------------

half _ParallaxTiling;
half2 _UVRatio;

#ifndef LUX_STANDARD_CORE_INCLUDED
	float _Parallax;
	sampler2D _ParallaxMap;
    float4 _ParallaxMap_ST;
float4 _ParallaxMap_TexelSize;
    #if defined (EFFECT_BUMP)
        float _LinearSteps;
    #endif
#endif

//	Get parallax offset and handle mix mapping as well as setting height ---------------
void Lux_Parallax (
    inout half height,
    inout float2 offset,
    inout float4 i_tex,
    inout half2 mixmapValue,
    inout half puddleMaskValue,
    inout half3 viewDir
    )
{

    #if defined (LUX_STANDARD_CORE_INCLUDED) && (!defined(_PARALLAXMAP) || (SHADER_TARGET < 30))
        // SM20: instruction count limitation
        // SM20: no parallax
        i_tex = i_tex;
    
    #else
        viewDir = normalize(viewDir);
        // Regular Detail Blending
        #if !defined(GEOM_TYPE_BRANCH_DETAIL)
            half2 heightAndPuddleMask = tex2D (_ParallaxMap, i_tex.xy * _ParallaxTiling).gr;
            height = heightAndPuddleMask.x;
            #if !defined(TESSELLATION_ON)
                #if defined (_DETAIL_MULX2)
                    // As we might have to deal with two different tilings here, we have to calculate the ratio between base and detail texture tiling and use it when offsetting.
                    float2 BaseToDetailFactor = i_tex.zw/i_tex.xy;
                #else
                    float2 BaseToDetailFactor = 1;
                #endif
                    offset = ParallaxOffset1Step (height, _Parallax, viewDir);
                
            #if defined (LUX_STANDARD_CORE_INCLUDED)   
                    offset *= _MainTex_ST.xy;
            #endif
                    // Lux standard shader corrects this already
                    #ifndef LUX_STANDARD_CORE_INCLUDED
                //        offset /= _UVRatio.xy;
                    #endif


                    i_tex += float4(offset, offset * BaseToDetailFactor) / _ParallaxTiling;
                // Get final height
                #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                    heightAndPuddleMask = tex2D (_ParallaxMap, i_tex.xy * _ParallaxTiling).gr;
                    height = heightAndPuddleMask.x;
                #endif
            #endif
            puddleMaskValue = heightAndPuddleMask.y;
        // Mix Mapping
        #else
            half3 h;
            float4 uvScaled = i_tex * _ParallaxTiling;
            // Safe one texture lookup by using the already sampled height
            #ifdef FIRSTHEIGHT_READ
                h.x = height;
            #else
            // Read height, mask and puddle mask
                h = tex2D (_ParallaxMap, uvScaled.xy).gbr;
            #endif
            // If called from surface shader mixmapping is not set up
            #if !defined (LUX_STANDARD_CORE_INCLUDED)
                mixmapValue = half2(h.y, 1.0 - h.y);
            #endif
            h.y = tex2D (_ParallaxMap, uvScaled.zw).a;
            h.xy = saturate(h.xy + 0.001);
            // blend according to height and mixmapValue
            float2 blendValue = mixmapValue;
            mixmapValue *= float2( dot(h.x, mixmapValue.x), dot(h.y, mixmapValue.y));
            // sharpen mask
            mixmapValue *= mixmapValue;
            mixmapValue *= mixmapValue;
            mixmapValue = mixmapValue / dot(mixmapValue, 1);
            height = dot(mixmapValue, h.xy);
            #if !defined(TESSELLATION_ON)
                offset = ParallaxOffset1Step (height, _Parallax, viewDir);
                offset *= _MainTex_ST.xy;
                // Lux standard shader corrects this already
                #ifndef LUX_STANDARD_CORE_INCLUDED
                    offset /= _UVRatio.xy;
                #endif
                i_tex += float4(offset, offset) / _ParallaxTiling;
                // Get final height
                #if defined (_WETNESS_SIMPLE) || defined (_WETNESS_RIPPLES) || defined (_WETNESS_FLOW) || defined (_WETNESS_FULL)
                    half2 h1 = tex2D (_ParallaxMap, i_tex.xy * _ParallaxTiling).ga;
                    h1.y = tex2D (_ParallaxMap, i_tex.zw * _ParallaxTiling).a;
                    height = dot(mixmapValue, h1);
                #endif
            #endif
            puddleMaskValue = h.z;
        #endif
    #endif
}


//  Simple POM functions ---------------------------------------------------------------

//  Base function for just a single texture
void Lux_SimplePOM (
    inout half height,
    inout float2 offset,
    inout float4 uvIN,
    inout half puddleMaskValue,
    inout half3 viewDir,
    int POM_Linear_Steps,
    fixed detailBlendState,
    sampler2D heightmap )
{
    viewDir = normalize(viewDir);
    // Calculate the parallax offset vector max length.
    // This is equivalent to the tangent of the angle between the viewer position and the fragment location.
    float fParallaxLimit = -length( viewDir.xy ) / viewDir.z;
    // Scale the parallax limit according to heightmap scale.
    fParallaxLimit *= _Parallax * detailBlendState;

    POM_Linear_Steps = (detailBlendState == 0) ? 1 : POM_Linear_Steps;

    // Lux
    float slopeDamp = 1.0 - saturate (dot(viewDir, float3(0,0,1)));
    fParallaxLimit *= 1.0 - (slopeDamp * slopeDamp);

    // Calculate the parallax offset vector direction and maximum offset.
    float2 vOffsetDir = normalize( viewDir.xy );

    // Calculate how many samples should be taken along the view ray to find the surface intersection.
    // This is based on the angle between the surface normal and the view vector.
//  int nNumSamples = (int)lerp( nMaxSamples, nMinSamples, dot( E, N ) );
    
    // Specify the view ray step size. Each sample will shift the current view ray by this amount.
    float2 fStepSize = 1.0 / (float)POM_Linear_Steps; //(float)nNumSamples;


    float4 uvScaled = uvIN * _ParallaxTiling;

    // Calculate the texture coordinate partial derivatives in screen space for the tex2Dgrad texture sampling instruction.
    float2 dx = ddx( uvScaled.xy);
    float2 dy = ddy( uvScaled.xy);

    float2 vMaxOffset = vOffsetDir.xy * fParallaxLimit;

    // Initialize the starting view ray height and the texture offsets.
    float fCurrRayHeight = 1.0; 
    float2 vCurrOffset = 0.0;
    float2 vLastOffset = 0.0;
    
    float fLastSampledHeight = 1;
    float fCurrSampledHeight = 1;

    int nCurrSample = 0;

    float h0;
    float h1;

    // Lux
    // As we might have to deal with two different tilings here, we have to calculate the ratio between base and detail texture tiling and use it when offsetting.
    float2 BaseToDetailFactor = uvIN.zw/uvIN.xy;

    float2 finalStepSize = fStepSize * vMaxOffset 
    #if !defined(TESSELLATION_ON) 
        * _MainTex_ST.xy
    #endif
    ;

    bool hit = false;

    while ( nCurrSample < POM_Linear_Steps )
    {
        // Sample the heightmap at the current texcoord offset.
        fCurrSampledHeight = tex2Dgrad(heightmap, uvScaled.xy + vCurrOffset, dx.xy, dy.xy ).g;
        // Test if the view ray has intersected the surface.
        if ( fCurrSampledHeight > fCurrRayHeight )
        {
            // Find the relative height delta before and after the intersection.
            // This provides a measure of how close the intersection is to the final sample location.
            float delta1 = fCurrSampledHeight - fCurrRayHeight;
            float delta2 = (fCurrRayHeight + fStepSize) - fLastSampledHeight;
            float ratio = delta1/(delta1+delta2);
            // Interpolate between the final two segments to find the true intersection point offset.
            vCurrOffset = (ratio) * vLastOffset + (1.0-ratio) * vCurrOffset;
                // Lux: Calculate the final hight
                fLastSampledHeight = (ratio) * fLastSampledHeight + (1.0-ratio) * fCurrSampledHeight;
                hit = true;
            // Force the exit of the while loop
            nCurrSample = POM_Linear_Steps + 1;
        }
        else
        {
            // The intersection was not found.
            // Now set up the loop for the next iteration by incrementing the sample count,
            nCurrSample++;
            // take the next view ray height step,
            fCurrRayHeight -= fStepSize;
            // save the current texture coordinate offset and increment to the next sample location, 
            vLastOffset = vCurrOffset;
            vCurrOffset += finalStepSize; //fStepSize * vMaxOffset;
            // and finally save the current heightmap height.
            fLastSampledHeight = fCurrSampledHeight;
        }
    }
    if (!hit) {
        //fLastSampledHeight = 0.0f;
        vCurrOffset = vMaxOffset.xy;
    }
    // Calculate the final texture coordinate at the intersection point.
    uvIN.zw += vCurrOffset.xy * BaseToDetailFactor / _ParallaxTiling;
    uvIN.xy += vCurrOffset.xy / _ParallaxTiling;
    // Set height
    height = saturate(fLastSampledHeight);
    // Set offset
    offset = vCurrOffset.xy;    
}


//  ------------------------------------------------------------------
//  Mixing textures

//  Computes Parallax Occlusion Mapping texture offset and mixmapValue
//  inout heigh             needs no "real" input, outputs height
//  inout unIN              base uvs for texture1 and texture2
//  inout mixmapValue       needs no "real" input, outputs the final mixmapValue
//  inout viewDir           viewDir in tangent space
//  in POM_Linear_Steps     maximum number of samples in the height maps per pixel
//  in heightmap            combined heightmaps (GA) and mask (B)

#if defined (GEOM_TYPE_BRANCH_DETAIL)

void Lux_SimplePOM_MixMap (
    inout half height,
    inout float2 offset,
    inout float4 uvIN,
    inout half2 mixmapValue,
    inout half puddleMaskValue,
    inout half3 viewDir,
    int POM_Linear_Steps,
    fixed detailBlendState,
    sampler2D heightmap)
{

    // Calculate the parallax offset vector max length.
    // This is equivalent to the tangent of the angle between the viewer position and the fragment location.
    float fParallaxLimit = -length( viewDir.xy ) / viewDir.z;
    // Scale the parallax limit according to heightmap scale.
    fParallaxLimit *= _Parallax * detailBlendState;

    POM_Linear_Steps = (detailBlendState == 0) ? 1 : POM_Linear_Steps;

    // Lux
    float slopeDamp = 1.0 - saturate (dot(viewDir, float3(0,0,1)));
    fParallaxLimit *= 1.0 - (slopeDamp * slopeDamp);

    // Calculate the parallax offset vector direction and maximum offset.
    float2 vOffsetDir = normalize( viewDir.xy );

    // Calculate how many samples should be taken along the view ray to find the surface intersection.
    // This is based on the angle between the surface normal and the view vector.
//  int nNumSamples = (int)lerp( nMaxSamples, nMinSamples, dot( E, N ) );
    
    // Specify the view ray step size. Each sample will shift the current view ray by this amount.
    float fStepSize = 1.0 / (float)POM_Linear_Steps; //(float)nNumSamples;

    // Calculate the texture coordinate partial derivatives in screen space for the tex2Dgrad texture sampling instruction.

    float4 uvScaled = uvIN * _ParallaxTiling;

    float4 dx = ddx( uvScaled.xyzw );
    float4 dy = ddy( uvScaled.xyzw );

    float4 vMaxOffset = vOffsetDir.xyxy * fParallaxLimit;

    // Initialize the starting view ray height and the texture offsets.
    float fCurrRayHeight = 1.0; 
    float4 vCurrOffset = 0.0;
    float4 vLastOffset = 0.0;
    
    float fLastSampledHeight = 1.0;
    float fCurrSampledHeight = 1.0;

    int nCurrSample = 0;

    #if defined(GEOM_TYPE_LEAF)
        half3 heightAndMask;
    #endif
    float h0;
    float h1;

    // Lux
    // As we might have to deal with two different tilings here, we have to calculate the ratio between base and detail texture tiling and use it when offsetting.
    float2 BaseToDetailFactor = uvIN.zw/uvIN.xy;

    float4 finalStepSize = fStepSize * vMaxOffset 
    #if !defined(TESSELLATION_ON) 
        * _MainTex_ST.xyxy / _UVRatio.xyxy
    #endif
    * float4(1,1,BaseToDetailFactor);


    float2 finalHeights = float2(1.0, 1.0);

    bool hit = false;

    //while ( nCurrSample < POM_Linear_Steps )

// for should be faster than while

    for (int i = 0; i < POM_Linear_Steps; i++ )
    {

        // Using Mask texture
        #if defined(GEOM_TYPE_LEAF)
            // read height, mask and puddle mask
            heightAndMask = tex2Dgrad(heightmap, uvScaled.xy + vCurrOffset.xy, dx.xy, dy.xy).gbr;
            h0 = heightAndMask.x;
            h1 = tex2Dgrad(heightmap, uvScaled.zw + vCurrOffset.zw, dx.zw, dy.zw).a;
        // Using vertex colors
        #else
            h0 = tex2Dgrad(heightmap, uvScaled.xy + vCurrOffset.xy, dx.xy, dy.xy).g;
            h1 = tex2Dgrad(heightmap, uvScaled.zw + vCurrOffset.zw, dx.xy, dy.xy).a;
        #endif

        // Adjust the mixmapValue when using Mask texture
        #if defined(GEOM_TYPE_LEAF)
            mixmapValue = half2(heightAndMask.y, 1.0 - heightAndMask.y);
            mixmapValue = max( half2(0.0001, 0.0001), mixmapValue * float2(dot(h0, mixmapValue.x), dot(h1, mixmapValue.y)));
            // one might skip it in the loop and do it at the end
            mixmapValue *= mixmapValue;
            mixmapValue *= mixmapValue;
            mixmapValue = mixmapValue / dot(mixmapValue, half2(1.0, 1.0)); 
        #endif

        // Calculate height according to mixmapValue
        fCurrSampledHeight = lerp(h0, h1, mixmapValue.y);

        // Test if the view ray has intersected the surface.
        if ( fCurrSampledHeight > fCurrRayHeight )
        {
            // Find the relative height delta before and after the intersection.
            // This provides a measure of how close the intersection is to the final sample location.
            float delta1 = fCurrSampledHeight - fCurrRayHeight;
            float delta2 = (fCurrRayHeight + fStepSize) - fLastSampledHeight;
            float ratio = delta1/(delta1+delta2);
            // Interpolate between the final two segments to find the true intersection point offset.
            vCurrOffset = (ratio) * vLastOffset + (1.0-ratio) * vCurrOffset;
                // Lux: Calculate the final hight
                fLastSampledHeight = (ratio) * fLastSampledHeight + (1.0-ratio) * fCurrSampledHeight;
                hit = true;
                // Using vertex colors
                #if !defined(GEOM_TYPE_LEAF)
                    finalHeights = float2(h0, h1);
                #endif
            // Force the exit of the while loop
            // nCurrSample = POM_Linear_Steps + 1;
            i = POM_Linear_Steps;
        }
        else
        {
            // The intersection was not found. Now set up the loop for the next iteration by incrementing the sample count,
            nCurrSample++;
            // take the next view ray height step,
            fCurrRayHeight -= fStepSize;
            // save the current texture coordinate offset and increment to the next sample location, 
            vLastOffset = vCurrOffset;
            vCurrOffset += finalStepSize; //fStepSize * vMaxOffset;
            // and finally save the current heightmap height.
            fLastSampledHeight = fCurrSampledHeight;
        }
    }

    if (!hit) {
        //fLastSampledHeight = saturate(fLastSampledHeight);
        vCurrOffset = float4(vMaxOffset.xyxy);
    }

//  Calculate the final texture coordinate at the intersection point.
    uvIN += float4(vCurrOffset.xy, vCurrOffset.xy * BaseToDetailFactor) / _ParallaxTiling;

//  Adjust the mixmapValue when using vertex colors
    #if !defined(GEOM_TYPE_LEAF)
        float2 blendVal = max( 0.0001, float2 ( dot(finalHeights.x, mixmapValue.x), dot(finalHeights.y, mixmapValue.y))) ;
        blendVal *= blendVal;
        blendVal *= blendVal;
        blendVal = blendVal / dot(blendVal, 1.0);
        mixmapValue = lerp(mixmapValue, blendVal, detailBlendState);
/*    #else
        mixmapValue *= mixmapValue;
        mixmapValue *= mixmapValue;
        mixmapValue = mixmapValue / dot(mixmapValue, 1.0);*/
    #else
        puddleMaskValue = heightAndMask.z;
    #endif
    // Set height
    height = saturate(fLastSampledHeight);
    // Set offset
    offset = vCurrOffset.xy; 
}

//  ---------------------------------------------------

void Lux_SimplePOM_MixMapxxx(
    inout half height,
    inout float2 offset,
    inout float4 uvIN,
    inout half2 mixmapValue,
    inout half puddleMaskValue,
    inout half3 viewDir,
    int POM_Linear_Steps,
    fixed detailBlendState,
    sampler2D heightmap)
{

    // disable mixmapping for now
    mixmapValue = half2(1,0);

    viewDir = normalize(viewDir);

    // Calculate the texture coordinate partial derivatives in screen space for the tex2Dgrad texture sampling instruction.
    float4 uvScaled = uvIN * _ParallaxTiling;
    float4 dx = ddx( uvScaled.xyzw );
    float4 dy = ddy( uvScaled.xyzw );


//__________

float slopeDamp = 1.0 - saturate (dot(viewDir, float3(0,0,1)));

float3 Step = -viewDir.xyz;
Step /= length( viewDir.xy ) * viewDir.z;
Step *= float3(_Parallax.xx, 1) * (1.0 / (float)POM_Linear_Steps);
// Fade out POM according to slope and detailBlendState
Step.xy *= 1.0 - (slopeDamp * slopeDamp) * detailBlendState;

// As we might have to deal with two different tilings here, we have to calculate the ratio between base and detail texture tiling and use it when offsetting.
float2 BaseToDetailFactor = uvIN.zw/uvIN.xy;
float2 Step2nd = Step.xy * BaseToDetailFactor;

//___________



    float3 CurrentUVs_and_Depth = float3(uvIN.xy, 1.001);
    float2 Current2ndUVs = uvIN.zw;

    float LastOffset = 0.0;
    float LastSampledHeight = 1.001;
    float CurrentSampledHeight = 1.0;
    float CurrentSampledFinalHeight = 1.0;

    half3 heightAndMask;

    bool SampleIsBelowRay = false;
    float h0;
    float h1;


    for (int i = 0; i < POM_Linear_Steps; i++, CurrentUVs_and_Depth.xyz += Step, Current2ndUVs += Step2nd) {
        heightAndMask = tex2Dgrad(heightmap, CurrentUVs_and_Depth.xy, dx.xy, dy.xy).gbr;
        h0 = heightAndMask.x;
        h1 = tex2Dgrad(heightmap, Current2ndUVs, dx.zw, dy.zw).a;

        // Adjust the mixmapValue when using Mask texture
        #if defined(GEOM_TYPE_LEAF)
            mixmapValue = half2(heightAndMask.y, 1.0 - heightAndMask.y);
            mixmapValue = max( half2(0.0001, 0.0001), mixmapValue * float2(dot(h0, mixmapValue.x), dot(h1, mixmapValue.y)));
            // one might skip it in the loop and do it at the end
            mixmapValue *= mixmapValue;
            mixmapValue *= mixmapValue;
            mixmapValue = mixmapValue / dot(mixmapValue, half2(1.0, 1.0)); 
        #endif
        
        CurrentSampledHeight = lerp(h0, h1, mixmapValue.y);
         
        if(CurrentSampledHeight > CurrentUVs_and_Depth.z ) {
           SampleIsBelowRay = true;
           break;
        }
        LastSampledHeight = CurrentSampledHeight;
    }


    if (SampleIsBelowRay) {

        LastOffset = CurrentUVs_and_Depth.z - LastSampledHeight - Step.z;
        float slope = 1.0 - LastOffset / ((CurrentSampledHeight - LastSampledHeight) - Step.z);
        // Get new sampling position according to slope
        CurrentUVs_and_Depth.xyz -= Step * slope;
        Current2ndUVs -= Step2nd * slope;
        
        heightAndMask = tex2Dgrad(heightmap, CurrentUVs_and_Depth.xy, dx.xy, dy.xy).gbr;
        h0 = heightAndMask.x;
        h1 = tex2Dgrad(heightmap, Current2ndUVs, dx.zw, dy.zw).a;

        // Adjust the mixmapValue when using Mask texture
        #if defined(GEOM_TYPE_LEAF)
            mixmapValue = half2(heightAndMask.y, 1.0 - heightAndMask.y);
            mixmapValue = max( half2(0.0001, 0.0001), mixmapValue * float2(dot(h0, mixmapValue.x), dot(h1, mixmapValue.y)));
            mixmapValue *= mixmapValue;
            mixmapValue *= mixmapValue;
            mixmapValue = mixmapValue / dot(mixmapValue, half2(1.0, 1.0)); 
        #endif
        
        CurrentSampledFinalHeight = lerp(h0, h1, mixmapValue.y);

        // Step forwards
        if (CurrentSampledFinalHeight <= CurrentUVs_and_Depth.z) {
            Step *= slope;
            LastOffset = CurrentUVs_and_Depth.z - CurrentSampledFinalHeight;
            slope = LastOffset / ((CurrentSampledHeight - CurrentSampledFinalHeight) - Step.z);
            CurrentUVs_and_Depth += Step * slope;
        } 
        
        // Step backwards
        else {
            Step *= 1.0 - slope;
            slope = 1.0 - LastOffset / ((CurrentSampledFinalHeight - LastSampledHeight) - Step.z);
            CurrentUVs_and_Depth.xyz -= Step * slope;
        }
        
    }


//    else {
//       CurrentUVs_and_Depth.xy = vOffsetDir.xy * fParallaxLimit * _Parallax;  
//    }

    // Set offset
    uvIN = float4(CurrentUVs_and_Depth.xy, CurrentUVs_and_Depth.xy * BaseToDetailFactor); // Current2ndUVs); 


}



#endif


//	Surface shader Macro Definitions ---------------------------------------------------

//  POM // Meta Pass always uses simple parallax mapping
#if defined(EFFECT_BUMP) && !defined (UNITY_PASS_META)
    // Mixmapping
    #if defined (GEOM_TYPE_BRANCH_DETAIL)
        #define LUX_PARALLAX \
            Lux_SimplePOM_MixMap (lux.height, lux.offset, lux.extrudedUV, lux.mixmapValue, lux.puddleMaskValue, lux.eyeVecTangent, _LinearSteps, lux.detailBlendState, _ParallaxMap); \
            lux.finalUV = lux.extrudedUV;
    // Regular blending 
    #else
        #define LUX_PARALLAX \
            Lux_SimplePOM (lux.height, lux.offset, lux.extrudedUV, lux.puddleMaskValue, lux.eyeVecTangent, _LinearSteps, lux.detailBlendState, _ParallaxMap); \
            lux.finalUV = lux.extrudedUV;

         #define LUX_PARALLAX_SCALED \
            lux.finalUV = lux.finalUV;

    #endif
//  Simple Parallax
#else
    #define LUX_PARALLAX \
    	Lux_Parallax (lux.height, lux.offset, lux.extrudedUV, lux.mixmapValue, lux.puddleMaskValue, lux.eyeVecTangent); \
    	lux.finalUV = lux.extrudedUV;
#endif

#endif