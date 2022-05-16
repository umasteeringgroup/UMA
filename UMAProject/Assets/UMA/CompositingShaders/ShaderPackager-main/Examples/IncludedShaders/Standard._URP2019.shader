Shader "BetterShaders/Standard"
{
   Properties
   {
         _AlbedoMap("Albedo", 2D) = "white" {}
	_Tint ("Tint", Color) = (1, 1, 1, 1)
   
   [Normal][NoScaleOffset]_NormalMap("Normal", 2D) = "bump" {}
   _NormalStrength("Normal Strength", Range(0,2)) = 1

   [Toggle(_MASKMAP)]
   _UseMaskMap ("Use Mask Map", Float) = 0
   [NoScaleOffset]_MaskMap("Mask Map", 2D) = "black" {}

   [Toggle(_EMISSION)]
   _UseEmission ("Use Emission Map", Float) = 0
   [NoScaleOffset]_EmissionMap("Emission Map", 2D) = "black" {}
   _EmissionStrength("Emission Strength", Range(0, 4)) = 1

   [Toggle(_DETAIL)]
   _UseDetail("Use Detail Map", Float) = 0
   _DetailMap("Detail Map", 2D) = "bump" {}
   _DetailAlbedoStrength("Detail Albedo Strength", Range(0, 2)) = 1
   _DetailNormalStrength("Detail Normal Strength", Range(0, 2)) = 1
   _DetailSmoothnessStrength("Detail Smoothness Strength", Range(0, 2)) = 1


   }
   SubShader
   {
      Tags { "RenderPipeline"="UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

      
        Pass
        {
            Name "Universal Forward"
            Tags 
            { 
                "LightMode" = "UniversalForward"
            }
            Blend One Zero, One Zero
Cull Back
ZTest LEqual
ZWrite On

            HLSLPROGRAM

               #pragma vertex Vert
   #pragma fragment Frag

            #pragma target 3.0

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
        
            // Keywords
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS _ADDITIONAL_OFF
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
            // GraphKeywords: <None>
            

                 
            // Includes
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.shadergraph/ShaderGraphLibrary/ShaderVariablesFunctions.hlsl"
        

            #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _URP 1


               #undef WorldNormalVector
      #define WorldNormalVector(data, normal) mul(normal, data.TBNMatrix)
      
      #define UnityObjectToWorldNormal(normal) mul(GetObjectToWorldMatrix(), normal)

      #define _WorldSpaceLightPos0 _MainLightPosition
      
      #define UNITY_DECLARE_TEX2D(name) TEXTURE2D(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2D_NOSAMPLER(name) TEXTURE2D(name);
      #define UNITY_DECLARE_TEX2DARRAY(name) TEXTURE2D_ARRAY(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(name) TEXTURE2D_ARRAY(name);

      #define UNITY_SAMPLE_TEX2DARRAY(tex,coord)            SAMPLE_TEXTURE2D_ARRAY(tex, sampler_##tex, coord.xy, coord.z)
      #define UNITY_SAMPLE_TEX2DARRAY_LOD(tex,coord,lod)    SAMPLE_TEXTURE2D_ARRAY_LOD(tex, sampler_##tex, coord.xy, coord.z, lod)
      #define UNITY_SAMPLE_TEX2D(tex, coord)                SAMPLE_TEXTURE2D(tex, sampler_##tex, coord)
      #define UNITY_SAMPLE_TEX2D_SAMPLER(tex, samp, coord)  SAMPLE_TEXTURE2D(tex, sampler_##samp, coord)

     
      #if defined(UNITY_COMPILER_HLSL)
         #define UNITY_INITIALIZE_OUTPUT(type,name) name = (type)0;
      #else
         #define UNITY_INITIALIZE_OUTPUT(type,name)
      #endif

      #define sampler2D_float sampler2D
      #define sampler2D_half sampler2D



      // data across stages, stripped like the above.
      struct VertexToPixel
      {
         float4 pos : SV_POSITION;
         float3 worldPos : TEXCOORD0;
         float3 worldNormal : TEXCOORD1;
         float4 worldTangent : TEXCOORD2;
          float4 texcoord0 : TEXCCOORD3;
         // float4 texcoord1 : TEXCCOORD4;
         // float4 texcoord2 : TEXCCOORD5;
         // float4 texcoord3 : TEXCCOORD6;
         // float4 screenPos : TEXCOORD7;
         // float4 color : COLOR;

         // float4 extraData0 : TEXCOORD12;
         // float4 extraData1 : TEXCOORD13;
         // float4 extraData2 : TEXCOORD14;
         // float4 extraData3 : TEXCOORD15;
            
         #if defined(LIGHTMAP_ON)
            float2 lightmapUV : TEXCOORD8;
         #endif
         #if !defined(LIGHTMAP_ON)
            float3 sh : TEXCOORD9;
         #endif
            float4 fogFactorAndVertexLight : TEXCOORD10;
            float4 shadowCoord : TEXCOORD11;
         #if UNITY_ANY_INSTANCING_ENABLED
            uint instanceID : CUSTOM_INSTANCE_ID;
         #endif
         #if (defined(UNITY_STEREO_INSTANCING_ENABLED))
            uint stereoTargetEyeIndexAsRTArrayIdx : SV_RenderTargetArrayIndex;
         #endif
         #if (defined(UNITY_STEREO_MULTIVIEW_ENABLED)) || (defined(UNITY_STEREO_INSTANCING_ENABLED) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)))
            uint stereoTargetEyeIndexAsBlendIdx0 : BLENDINDICES0;
         #endif
         #if defined(SHADER_STAGE_FRAGMENT) && defined(VARYINGS_NEED_CULLFACE)
            FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC;
         #endif
      };


         
            
            // data describing the user output of a pixel
            struct LightingInputs
            {
               half3 Albedo;
               half Height;
               half3 Normal;
               half Smoothness;
               half3 Emission;
               half Metallic;
               half3 Specular;
               half Occlusion;
               half Alpha;
               // HDRP Only
               half SpecularOcclusion;
               half SubsurfaceMask;
               half Thickness;
               half CoatMask;
               half Anisotropy;
               half iridescenceMask;
               half iridescenceThickness;
            };

            // data the user might need, this will grow to be big. But easy to strip
            struct ShaderData
            {
               float3 localSpacePosition;
               float3 localSpaceNormal;
               float3 localSpaceTangent;
        
               float3 worldSpacePosition;
               float3 worldSpaceNormal;
               float3 worldSpaceTangent;

               float3 worldSpaceViewDir;
               float3 tangentSpaceViewDir;

               float4 texcoord0;
               float4 texcoord1;
               float4 texcoord2;
               float4 texcoord3;

               float2 screenUV;
               float4 screenPos;

               float4 vertexColor;

               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;

               float3x3 TBNMatrix;
            };

            struct VertexData
            {
               float4 vertex : POSITION;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;
            
               UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct TessVertex 
            {
               float4 vertex : INTERNALTESSPOS;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;

               
               // float4 extraData0 : TEXCOORD4;
               // float4 extraData1 : TEXCOORD5;
               // float4 extraData2 : TEXCOORD6;
               // float4 extraData3 : TEXCOORD7;

               UNITY_VERTEX_INPUT_INSTANCE_ID
               UNITY_VERTEX_OUTPUT_STEREO
            };

            struct ExtraData
            {
               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;
            };


            float3 WorldToTangentSpace(ShaderData d, float3 normal)
            {
               return mul(d.TBNMatrix, normal);
            }

            float3 TangentToWorldSpace(ShaderData d, float3 normal)
            {
               return mul(normal, d.TBNMatrix);
            }

            // in this case, make standard more like SRPs, because we can't fix
            // GetWorldToObjectMatrix() in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(GetWorldToObjectMatrix(), p); };
               float3 TransformObjectToWorld(float3 p) { return mul(GetObjectToWorldMatrix(), p); };
               float4x4 GetWorldToObjectMatrix() { return GetWorldToObjectMatrix(); }
               float4x4 GetObjectToWorldMatrix() { return GetObjectToWorldMatrix(); }
            #endif


            
         CBUFFER_START(UnityPerMaterial)

            	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


         CBUFFER_END


         
   half3 BlendDetailNormal(half3 n1, half3 n2)
   {
      return normalize(half3(n1.xy + n2.xy, n1.z*n2.z));
   }

   // We share samplers with the albedo - which free's up more for stacking.

   UNITY_DECLARE_TEX2D(_AlbedoMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_NormalMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_MaskMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_EmissionMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_DetailMap);


	void SurfaceFunction(inout LightingInputs o, ShaderData d)
	{
      float2 uv = d.texcoord0.xy * _AlbedoMap_ST.xy + _AlbedoMap_ST.zw;

      half4 c = UNITY_SAMPLE_TEX2D(_AlbedoMap, uv);
      o.Albedo = c.rgb * _Tint.rgb;
		o.Normal = UnpackScaleNormal(UNITY_SAMPLE_TEX2D_SAMPLER(_NormalMap, _AlbedoMap, uv), _NormalStrength);

      half detailMask = 1;
      #if _MASKMAP
          // Unity mask map format (R) Metallic, (G) Occlusion, (B) Detail Mask (A) Smoothness
         half4 mask = UNITY_SAMPLE_TEX2D_SAMPLER(_MaskMap, _AlbedoMap, uv);
         o.Metallic = mask.r;
         o.Occlusion = mask.g;
         o.Smoothness = mask.a;
         detailMask = mask.b;
      #endif // separate maps


      half3 emission = 0;
      #if defined(_EMISSION)
         o.Emission = UNITY_SAMPLE_TEX2D_SAMPLER(_EmissionMap, _AlbedoMap, uv).rgb * _EmissionStrength;
      #endif

      #if defined(_DETAIL)
         float2 detailUV = uv * _DetailMap_ST.xy + _DetailMap_ST.zw;
         half4 detailSample = UNITY_SAMPLE_TEX2D_SAMPLER(_DetailMap, _AlbedoMap, detailUV);
         o.Normal = BlendDetailNormal(o.Normal, UnpackScaleNormal(detailSample, _DetailNormalStrength * detailMask));
         o.Albedo = lerp(o.Albedo, o.Albedo * 2 * detailSample.x,  detailMask * _DetailAlbedoStrength);
         o.Smoothness = lerp(o.Smoothness, o.Smoothness * 2 * detailSample.z, detailMask * _DetailSmoothnessStrength);
      #endif


		o.Alpha = c.a;
	}



        
            void ChainSurfaceFunction(inout LightingInputs l, ShaderData d)
            {
                   SurfaceFunction(l, d);
                 // SurfaceFunction_Ext1(l, d);
                 // SurfaceFunction_Ext2(l, d);
                 // SurfaceFunction_Ext3(l, d);
                 // SurfaceFunction_Ext4(l, d);
                 // SurfaceFunction_Ext5(l, d);
                 // SurfaceFunction_Ext6(l, d);
                 // SurfaceFunction_Ext7(l, d);
                 // SurfaceFunction_Ext8(l, d);
                 // SurfaceFunction_Ext9(l, d);
            }

            void ChainModifyVertex(inout VertexData v, inout VertexToPixel v2p)
            {
                 ExtraData d = (ExtraData)0;
                 //  ModifyVertex(v, d);
                 // ModifyVertex_Ext1(v, d);
                 // ModifyVertex_Ext2(v, d);
                 // ModifyVertex_Ext3(v, d);
                 // ModifyVertex_Ext4(v, d);
                 // ModifyVertex_Ext5(v, d);
                 // ModifyVertex_Ext6(v, d);
                 // ModifyVertex_Ext7(v, d);
                 // ModifyVertex_Ext8(v, d);
                 // ModifyVertex_Ext9(v, d);
                 // v2p.extraData0 = d.extraData0;
                 // v2p.extraData1 = d.extraData1;
                 // v2p.extraData2 = d.extraData2;
                 // v2p.extraData3 = d.extraData3;
            }

            void ChainModifyTessellatedVertex(inout VertexData v, inout VertexToPixel v2p)
            {
               ExtraData d = (ExtraData)0;
               //  ModifyTessellatedVertex(v, d);
               // ModifyTessellatedVertex_Ext1(v, d);
               // ModifyTessellatedVertex_Ext2(v, d);
               // ModifyTessellatedVertex_Ext3(v, d);
               // ModifyTessellatedVertex_Ext4(v, d);
               // ModifyTessellatedVertex_Ext5(v, d);
               // ModifyTessellatedVertex_Ext6(v, d);
               // ModifyTessellatedVertex_Ext7(v, d);
               // ModifyTessellatedVertex_Ext8(v, d);
               // ModifyTessellatedVertex_Ext9(v, d);
               // v2p.extraData0 = d.extraData0;
               // v2p.extraData1 = d.extraData1;
               // v2p.extraData2 = d.extraData2;
               // v2p.extraData3 = d.extraData3;
            }



         

         ShaderData CreateShaderData(VertexToPixel i)
         {
            ShaderData d = (ShaderData)0;
            d.worldSpacePosition = i.worldPos;

            d.worldSpaceNormal = i.worldNormal;
            d.worldSpaceTangent = i.worldTangent.xyz;
            float3 bitangent = cross(i.worldTangent.xyz, i.worldNormal) * i.worldTangent.w;
            

            d.TBNMatrix = float3x3(d.worldSpaceTangent, bitangent, d.worldSpaceNormal);
            d.worldSpaceViewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
            d.tangentSpaceViewDir = mul(d.TBNMatrix, d.worldSpaceViewDir);
             d.texcoord0 = i.texcoord0;
            // d.texcoord1 = i.texcoord1;
            // d.texcoord2 = i.texcoord2;
            // d.texcoord3 = i.texcoord3;
            // d.vertexColor = i.color;

            // these rarely get used, so we back transform them. Usually will be stripped.
            // d.localSpacePosition = mul(GetWorldToObjectMatrix(), i.worldPos);
            // d.localSpaceNormal = mul(GetWorldToObjectMatrix(), i.worldNormal);
            // d.localSpaceTangent = mul(GetWorldToObjectMatrix(), i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         

         #if defined(SHADERPASS_SHADOWCASTER)
            float3 _LightDirection;
         #endif

         
         #if defined(SHADERPASS_SHADOWCASTER)
            float3 _LightDirection;
         #endif

         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
           
           VertexToPixel o = (VertexToPixel)0;

           UNITY_SETUP_INSTANCE_ID(v);
           UNITY_TRANSFER_INSTANCE_ID(v, o);
           UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);


#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

            o.texcoord0 = v.texcoord0;
           // o.texcoord1 = v.texcoord1;
           // o.texcoord2 = v.texcoord2;
           // o.texcoord3 = v.texcoord3;
           // o.color = v.color;

           VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
           o.worldPos = TransformObjectToWorld(v.vertex.xyz);
           o.worldNormal = TransformObjectToWorldNormal(v.normal);
           o.worldTangent = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);


          #if defined(SHADERPASS_SHADOWCASTER)
              // Define shadow pass specific clip position for Universal
              o.pos = TransformWorldToHClip(ApplyShadowBias(o.worldPos, o.worldNormal, _LightDirection));
              #if UNITY_REVERSED_Z
                  o.pos.z = min(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #else
                  o.pos.z = max(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #endif
          #elif defined(SHADERPASS_META)
              o.pos = MetaVertexPosition(float4(v.vertex.xyz, 0), v.texcoord1, v.texcoord2, unity_LightmapST, unity_DynamicLightmapST);
          #else
              o.pos = TransformWorldToHClip(o.worldPos);
          #endif


              // o.screenPos = ComputeScreenPos(o.pos, _ProjectionParams.x);
          

          #if defined(SHADERPASS_FORWARD)
              OUTPUT_LIGHTMAP_UV(o.uv1, unity_LightmapST, output.lightmapUV);
              OUTPUT_SH(o.worldNormal, o.sh);
          #endif

          #ifdef VARYINGS_NEED_FOG_AND_VERTEX_LIGHT
              half3 vertexLight = VertexLighting(o.worldPos, o.worldNormal);
              half fogFactor = ComputeFogFactor(o.pos.z);
              o.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
          #endif

          #ifdef _MAIN_LIGHT_SHADOWS
              o.shadowCoord = GetShadowCoord(vertexInput);
          #endif

           return o;
         }


         

         // fragment shader
         half4 Frag (VertexToPixel IN) : SV_Target
         {
           UNITY_SETUP_INSTANCE_ID(IN);
           UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

           ShaderData d = CreateShaderData(IN);

           LightingInputs l = (LightingInputs)0;

           l.Albedo = half3(0.5, 0.5, 0.5);
           l.Normal = float3(0,0,1);
           l.Occlusion = 1;
           l.Alpha = 1;

           ChainSurfaceFunction(l, d);

           #ifdef _USESPECULAR
              float3 specular = l.Specular;
              float metallic = 1;
           #else   
              float3 specular = 0;
              float metallic = l.Metallic;
          #endif

          InputData inputData;

           inputData.positionWS = IN.worldPos;
           inputData.normalWS = mul(l.Normal, d.TBNMatrix);
           inputData.viewDirectionWS = d.worldSpaceViewDir;


          #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
              inputData.shadowCoord = TransformWorldToShadowCoord(IN.worldPos);
          #else
              inputData.shadowCoord = float4(0, 0, 0, 0);
          #endif

          inputData.fogCoord = IN.fogFactorAndVertexLight.x;
          inputData.vertexLighting = IN.fogFactorAndVertexLight.yzw;
          inputData.bakedGI = SAMPLE_GI(IN.lightmapUV, IN.sh, inputData.normalWS);


          half4 color = UniversalFragmentPBR(
            inputData,
            l.Albedo,
            metallic,
            specular,
            l.Smoothness,
            l.Occlusion,
            l.Emission,
            l.Alpha); 

          color.rgb = MixFog(color.rgb, inputData.fogCoord);

          // FinalColorForward(l, d, color);

          return color;

         }

         ENDHLSL

      }


      
        Pass
        {
            Name "ShadowCaster"
            Tags 
            { 
                "LightMode" = "ShadowCaster"
            }
           
            // Render State
            Blend One Zero, One Zero
            Cull Back
            ZTest LEqual
            ZWrite On
            // ColorMask: <None>

            HLSLPROGRAM

               #pragma vertex Vert
   #pragma fragment Frag

            #pragma target 3.0

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma multi_compile_instancing
        
            

                 
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.shadergraph/ShaderGraphLibrary/ShaderVariablesFunctions.hlsl"
        

               #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _URP 1


                  #undef WorldNormalVector
      #define WorldNormalVector(data, normal) mul(normal, data.TBNMatrix)
      
      #define UnityObjectToWorldNormal(normal) mul(GetObjectToWorldMatrix(), normal)

      #define _WorldSpaceLightPos0 _MainLightPosition
      
      #define UNITY_DECLARE_TEX2D(name) TEXTURE2D(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2D_NOSAMPLER(name) TEXTURE2D(name);
      #define UNITY_DECLARE_TEX2DARRAY(name) TEXTURE2D_ARRAY(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(name) TEXTURE2D_ARRAY(name);

      #define UNITY_SAMPLE_TEX2DARRAY(tex,coord)            SAMPLE_TEXTURE2D_ARRAY(tex, sampler_##tex, coord.xy, coord.z)
      #define UNITY_SAMPLE_TEX2DARRAY_LOD(tex,coord,lod)    SAMPLE_TEXTURE2D_ARRAY_LOD(tex, sampler_##tex, coord.xy, coord.z, lod)
      #define UNITY_SAMPLE_TEX2D(tex, coord)                SAMPLE_TEXTURE2D(tex, sampler_##tex, coord)
      #define UNITY_SAMPLE_TEX2D_SAMPLER(tex, samp, coord)  SAMPLE_TEXTURE2D(tex, sampler_##samp, coord)

     
      #if defined(UNITY_COMPILER_HLSL)
         #define UNITY_INITIALIZE_OUTPUT(type,name) name = (type)0;
      #else
         #define UNITY_INITIALIZE_OUTPUT(type,name)
      #endif

      #define sampler2D_float sampler2D
      #define sampler2D_half sampler2D



      // data across stages, stripped like the above.
      struct VertexToPixel
      {
         float4 pos : SV_POSITION;
         float3 worldPos : TEXCOORD0;
         float3 worldNormal : TEXCOORD1;
         float4 worldTangent : TEXCOORD2;
          float4 texcoord0 : TEXCCOORD3;
         // float4 texcoord1 : TEXCCOORD4;
         // float4 texcoord2 : TEXCCOORD5;
         // float4 texcoord3 : TEXCCOORD6;
         // float4 screenPos : TEXCOORD7;
         // float4 color : COLOR;

         // float4 extraData0 : TEXCOORD12;
         // float4 extraData1 : TEXCOORD13;
         // float4 extraData2 : TEXCOORD14;
         // float4 extraData3 : TEXCOORD15;
            
         #if defined(LIGHTMAP_ON)
            float2 lightmapUV : TEXCOORD8;
         #endif
         #if !defined(LIGHTMAP_ON)
            float3 sh : TEXCOORD9;
         #endif
            float4 fogFactorAndVertexLight : TEXCOORD10;
            float4 shadowCoord : TEXCOORD11;
         #if UNITY_ANY_INSTANCING_ENABLED
            uint instanceID : CUSTOM_INSTANCE_ID;
         #endif
         #if (defined(UNITY_STEREO_INSTANCING_ENABLED))
            uint stereoTargetEyeIndexAsRTArrayIdx : SV_RenderTargetArrayIndex;
         #endif
         #if (defined(UNITY_STEREO_MULTIVIEW_ENABLED)) || (defined(UNITY_STEREO_INSTANCING_ENABLED) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)))
            uint stereoTargetEyeIndexAsBlendIdx0 : BLENDINDICES0;
         #endif
         #if defined(SHADER_STAGE_FRAGMENT) && defined(VARYINGS_NEED_CULLFACE)
            FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC;
         #endif
      };

         
            
            
            // data describing the user output of a pixel
            struct LightingInputs
            {
               half3 Albedo;
               half Height;
               half3 Normal;
               half Smoothness;
               half3 Emission;
               half Metallic;
               half3 Specular;
               half Occlusion;
               half Alpha;
               // HDRP Only
               half SpecularOcclusion;
               half SubsurfaceMask;
               half Thickness;
               half CoatMask;
               half Anisotropy;
               half iridescenceMask;
               half iridescenceThickness;
            };

            // data the user might need, this will grow to be big. But easy to strip
            struct ShaderData
            {
               float3 localSpacePosition;
               float3 localSpaceNormal;
               float3 localSpaceTangent;
        
               float3 worldSpacePosition;
               float3 worldSpaceNormal;
               float3 worldSpaceTangent;

               float3 worldSpaceViewDir;
               float3 tangentSpaceViewDir;

               float4 texcoord0;
               float4 texcoord1;
               float4 texcoord2;
               float4 texcoord3;

               float2 screenUV;
               float4 screenPos;

               float4 vertexColor;

               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;

               float3x3 TBNMatrix;
            };

            struct VertexData
            {
               float4 vertex : POSITION;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;
            
               UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct TessVertex 
            {
               float4 vertex : INTERNALTESSPOS;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;

               
               // float4 extraData0 : TEXCOORD4;
               // float4 extraData1 : TEXCOORD5;
               // float4 extraData2 : TEXCOORD6;
               // float4 extraData3 : TEXCOORD7;

               UNITY_VERTEX_INPUT_INSTANCE_ID
               UNITY_VERTEX_OUTPUT_STEREO
            };

            struct ExtraData
            {
               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;
            };


            float3 WorldToTangentSpace(ShaderData d, float3 normal)
            {
               return mul(d.TBNMatrix, normal);
            }

            float3 TangentToWorldSpace(ShaderData d, float3 normal)
            {
               return mul(normal, d.TBNMatrix);
            }

            // in this case, make standard more like SRPs, because we can't fix
            // GetWorldToObjectMatrix() in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(GetWorldToObjectMatrix(), p); };
               float3 TransformObjectToWorld(float3 p) { return mul(GetObjectToWorldMatrix(), p); };
               float4x4 GetWorldToObjectMatrix() { return GetWorldToObjectMatrix(); }
               float4x4 GetObjectToWorldMatrix() { return GetObjectToWorldMatrix(); }
            #endif


            
            CBUFFER_START(UnityPerMaterial)

               	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


            CBUFFER_END

            
   half3 BlendDetailNormal(half3 n1, half3 n2)
   {
      return normalize(half3(n1.xy + n2.xy, n1.z*n2.z));
   }

   // We share samplers with the albedo - which free's up more for stacking.

   UNITY_DECLARE_TEX2D(_AlbedoMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_NormalMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_MaskMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_EmissionMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_DetailMap);


	void SurfaceFunction(inout LightingInputs o, ShaderData d)
	{
      float2 uv = d.texcoord0.xy * _AlbedoMap_ST.xy + _AlbedoMap_ST.zw;

      half4 c = UNITY_SAMPLE_TEX2D(_AlbedoMap, uv);
      o.Albedo = c.rgb * _Tint.rgb;
		o.Normal = UnpackScaleNormal(UNITY_SAMPLE_TEX2D_SAMPLER(_NormalMap, _AlbedoMap, uv), _NormalStrength);

      half detailMask = 1;
      #if _MASKMAP
          // Unity mask map format (R) Metallic, (G) Occlusion, (B) Detail Mask (A) Smoothness
         half4 mask = UNITY_SAMPLE_TEX2D_SAMPLER(_MaskMap, _AlbedoMap, uv);
         o.Metallic = mask.r;
         o.Occlusion = mask.g;
         o.Smoothness = mask.a;
         detailMask = mask.b;
      #endif // separate maps


      half3 emission = 0;
      #if defined(_EMISSION)
         o.Emission = UNITY_SAMPLE_TEX2D_SAMPLER(_EmissionMap, _AlbedoMap, uv).rgb * _EmissionStrength;
      #endif

      #if defined(_DETAIL)
         float2 detailUV = uv * _DetailMap_ST.xy + _DetailMap_ST.zw;
         half4 detailSample = UNITY_SAMPLE_TEX2D_SAMPLER(_DetailMap, _AlbedoMap, detailUV);
         o.Normal = BlendDetailNormal(o.Normal, UnpackScaleNormal(detailSample, _DetailNormalStrength * detailMask));
         o.Albedo = lerp(o.Albedo, o.Albedo * 2 * detailSample.x,  detailMask * _DetailAlbedoStrength);
         o.Smoothness = lerp(o.Smoothness, o.Smoothness * 2 * detailSample.z, detailMask * _DetailSmoothnessStrength);
      #endif


		o.Alpha = c.a;
	}



        
            void ChainSurfaceFunction(inout LightingInputs l, ShaderData d)
            {
                   SurfaceFunction(l, d);
                 // SurfaceFunction_Ext1(l, d);
                 // SurfaceFunction_Ext2(l, d);
                 // SurfaceFunction_Ext3(l, d);
                 // SurfaceFunction_Ext4(l, d);
                 // SurfaceFunction_Ext5(l, d);
                 // SurfaceFunction_Ext6(l, d);
                 // SurfaceFunction_Ext7(l, d);
                 // SurfaceFunction_Ext8(l, d);
                 // SurfaceFunction_Ext9(l, d);
            }

            void ChainModifyVertex(inout VertexData v, inout VertexToPixel v2p)
            {
                 ExtraData d = (ExtraData)0;
                 //  ModifyVertex(v, d);
                 // ModifyVertex_Ext1(v, d);
                 // ModifyVertex_Ext2(v, d);
                 // ModifyVertex_Ext3(v, d);
                 // ModifyVertex_Ext4(v, d);
                 // ModifyVertex_Ext5(v, d);
                 // ModifyVertex_Ext6(v, d);
                 // ModifyVertex_Ext7(v, d);
                 // ModifyVertex_Ext8(v, d);
                 // ModifyVertex_Ext9(v, d);
                 // v2p.extraData0 = d.extraData0;
                 // v2p.extraData1 = d.extraData1;
                 // v2p.extraData2 = d.extraData2;
                 // v2p.extraData3 = d.extraData3;
            }

            void ChainModifyTessellatedVertex(inout VertexData v, inout VertexToPixel v2p)
            {
               ExtraData d = (ExtraData)0;
               //  ModifyTessellatedVertex(v, d);
               // ModifyTessellatedVertex_Ext1(v, d);
               // ModifyTessellatedVertex_Ext2(v, d);
               // ModifyTessellatedVertex_Ext3(v, d);
               // ModifyTessellatedVertex_Ext4(v, d);
               // ModifyTessellatedVertex_Ext5(v, d);
               // ModifyTessellatedVertex_Ext6(v, d);
               // ModifyTessellatedVertex_Ext7(v, d);
               // ModifyTessellatedVertex_Ext8(v, d);
               // ModifyTessellatedVertex_Ext9(v, d);
               // v2p.extraData0 = d.extraData0;
               // v2p.extraData1 = d.extraData1;
               // v2p.extraData2 = d.extraData2;
               // v2p.extraData3 = d.extraData3;
            }



            

         ShaderData CreateShaderData(VertexToPixel i)
         {
            ShaderData d = (ShaderData)0;
            d.worldSpacePosition = i.worldPos;

            d.worldSpaceNormal = i.worldNormal;
            d.worldSpaceTangent = i.worldTangent.xyz;
            float3 bitangent = cross(i.worldTangent.xyz, i.worldNormal) * i.worldTangent.w;
            

            d.TBNMatrix = float3x3(d.worldSpaceTangent, bitangent, d.worldSpaceNormal);
            d.worldSpaceViewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
            d.tangentSpaceViewDir = mul(d.TBNMatrix, d.worldSpaceViewDir);
             d.texcoord0 = i.texcoord0;
            // d.texcoord1 = i.texcoord1;
            // d.texcoord2 = i.texcoord2;
            // d.texcoord3 = i.texcoord3;
            // d.vertexColor = i.color;

            // these rarely get used, so we back transform them. Usually will be stripped.
            // d.localSpacePosition = mul(GetWorldToObjectMatrix(), i.worldPos);
            // d.localSpaceNormal = mul(GetWorldToObjectMatrix(), i.worldNormal);
            // d.localSpaceTangent = mul(GetWorldToObjectMatrix(), i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         

            
         #if defined(SHADERPASS_SHADOWCASTER)
            float3 _LightDirection;
         #endif

         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
           
           VertexToPixel o = (VertexToPixel)0;

           UNITY_SETUP_INSTANCE_ID(v);
           UNITY_TRANSFER_INSTANCE_ID(v, o);
           UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);


#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

            o.texcoord0 = v.texcoord0;
           // o.texcoord1 = v.texcoord1;
           // o.texcoord2 = v.texcoord2;
           // o.texcoord3 = v.texcoord3;
           // o.color = v.color;

           VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
           o.worldPos = TransformObjectToWorld(v.vertex.xyz);
           o.worldNormal = TransformObjectToWorldNormal(v.normal);
           o.worldTangent = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);


          #if defined(SHADERPASS_SHADOWCASTER)
              // Define shadow pass specific clip position for Universal
              o.pos = TransformWorldToHClip(ApplyShadowBias(o.worldPos, o.worldNormal, _LightDirection));
              #if UNITY_REVERSED_Z
                  o.pos.z = min(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #else
                  o.pos.z = max(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #endif
          #elif defined(SHADERPASS_META)
              o.pos = MetaVertexPosition(float4(v.vertex.xyz, 0), v.texcoord1, v.texcoord2, unity_LightmapST, unity_DynamicLightmapST);
          #else
              o.pos = TransformWorldToHClip(o.worldPos);
          #endif


              // o.screenPos = ComputeScreenPos(o.pos, _ProjectionParams.x);
          

          #if defined(SHADERPASS_FORWARD)
              OUTPUT_LIGHTMAP_UV(o.uv1, unity_LightmapST, output.lightmapUV);
              OUTPUT_SH(o.worldNormal, o.sh);
          #endif

          #ifdef VARYINGS_NEED_FOG_AND_VERTEX_LIGHT
              half3 vertexLight = VertexLighting(o.worldPos, o.worldNormal);
              half fogFactor = ComputeFogFactor(o.pos.z);
              o.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
          #endif

          #ifdef _MAIN_LIGHT_SHADOWS
              o.shadowCoord = GetShadowCoord(vertexInput);
          #endif

           return o;
         }


            

            // fragment shader
            half4 Frag (VertexToPixel IN) : SV_Target
            {
               UNITY_SETUP_INSTANCE_ID(IN);

               ShaderData d = CreateShaderData(IN);

               LightingInputs l = (LightingInputs)0;

               l.Albedo = half3(0.5, 0.5, 0.5);
               l.Normal = float3(0,0,1);
               l.Occlusion = 1;
               l.Alpha = 1;

               ChainSurfaceFunction(l, d);

             return 0;

            }

         ENDHLSL

      }


      
        Pass
        {
            Name "DepthOnly"
            Tags 
            { 
                "LightMode" = "DepthOnly"
            }
           
            // Render State
            Blend One Zero, One Zero
            Cull Back
            ZTest LEqual
            ZWrite On
            ColorMask 0
            

            HLSLPROGRAM

               #pragma vertex Vert
   #pragma fragment Frag

            #define SHADERPASS_DEPTHONLY

            #pragma target 3.0

                 
            // Includes
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.shadergraph/ShaderGraphLibrary/ShaderVariablesFunctions.hlsl"
        

               #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _URP 1


                  #undef WorldNormalVector
      #define WorldNormalVector(data, normal) mul(normal, data.TBNMatrix)
      
      #define UnityObjectToWorldNormal(normal) mul(GetObjectToWorldMatrix(), normal)

      #define _WorldSpaceLightPos0 _MainLightPosition
      
      #define UNITY_DECLARE_TEX2D(name) TEXTURE2D(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2D_NOSAMPLER(name) TEXTURE2D(name);
      #define UNITY_DECLARE_TEX2DARRAY(name) TEXTURE2D_ARRAY(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(name) TEXTURE2D_ARRAY(name);

      #define UNITY_SAMPLE_TEX2DARRAY(tex,coord)            SAMPLE_TEXTURE2D_ARRAY(tex, sampler_##tex, coord.xy, coord.z)
      #define UNITY_SAMPLE_TEX2DARRAY_LOD(tex,coord,lod)    SAMPLE_TEXTURE2D_ARRAY_LOD(tex, sampler_##tex, coord.xy, coord.z, lod)
      #define UNITY_SAMPLE_TEX2D(tex, coord)                SAMPLE_TEXTURE2D(tex, sampler_##tex, coord)
      #define UNITY_SAMPLE_TEX2D_SAMPLER(tex, samp, coord)  SAMPLE_TEXTURE2D(tex, sampler_##samp, coord)

     
      #if defined(UNITY_COMPILER_HLSL)
         #define UNITY_INITIALIZE_OUTPUT(type,name) name = (type)0;
      #else
         #define UNITY_INITIALIZE_OUTPUT(type,name)
      #endif

      #define sampler2D_float sampler2D
      #define sampler2D_half sampler2D



      // data across stages, stripped like the above.
      struct VertexToPixel
      {
         float4 pos : SV_POSITION;
         float3 worldPos : TEXCOORD0;
         float3 worldNormal : TEXCOORD1;
         float4 worldTangent : TEXCOORD2;
          float4 texcoord0 : TEXCCOORD3;
         // float4 texcoord1 : TEXCCOORD4;
         // float4 texcoord2 : TEXCCOORD5;
         // float4 texcoord3 : TEXCCOORD6;
         // float4 screenPos : TEXCOORD7;
         // float4 color : COLOR;

         // float4 extraData0 : TEXCOORD12;
         // float4 extraData1 : TEXCOORD13;
         // float4 extraData2 : TEXCOORD14;
         // float4 extraData3 : TEXCOORD15;
            
         #if defined(LIGHTMAP_ON)
            float2 lightmapUV : TEXCOORD8;
         #endif
         #if !defined(LIGHTMAP_ON)
            float3 sh : TEXCOORD9;
         #endif
            float4 fogFactorAndVertexLight : TEXCOORD10;
            float4 shadowCoord : TEXCOORD11;
         #if UNITY_ANY_INSTANCING_ENABLED
            uint instanceID : CUSTOM_INSTANCE_ID;
         #endif
         #if (defined(UNITY_STEREO_INSTANCING_ENABLED))
            uint stereoTargetEyeIndexAsRTArrayIdx : SV_RenderTargetArrayIndex;
         #endif
         #if (defined(UNITY_STEREO_MULTIVIEW_ENABLED)) || (defined(UNITY_STEREO_INSTANCING_ENABLED) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)))
            uint stereoTargetEyeIndexAsBlendIdx0 : BLENDINDICES0;
         #endif
         #if defined(SHADER_STAGE_FRAGMENT) && defined(VARYINGS_NEED_CULLFACE)
            FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC;
         #endif
      };

         
            
            
            // data describing the user output of a pixel
            struct LightingInputs
            {
               half3 Albedo;
               half Height;
               half3 Normal;
               half Smoothness;
               half3 Emission;
               half Metallic;
               half3 Specular;
               half Occlusion;
               half Alpha;
               // HDRP Only
               half SpecularOcclusion;
               half SubsurfaceMask;
               half Thickness;
               half CoatMask;
               half Anisotropy;
               half iridescenceMask;
               half iridescenceThickness;
            };

            // data the user might need, this will grow to be big. But easy to strip
            struct ShaderData
            {
               float3 localSpacePosition;
               float3 localSpaceNormal;
               float3 localSpaceTangent;
        
               float3 worldSpacePosition;
               float3 worldSpaceNormal;
               float3 worldSpaceTangent;

               float3 worldSpaceViewDir;
               float3 tangentSpaceViewDir;

               float4 texcoord0;
               float4 texcoord1;
               float4 texcoord2;
               float4 texcoord3;

               float2 screenUV;
               float4 screenPos;

               float4 vertexColor;

               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;

               float3x3 TBNMatrix;
            };

            struct VertexData
            {
               float4 vertex : POSITION;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;
            
               UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct TessVertex 
            {
               float4 vertex : INTERNALTESSPOS;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;

               
               // float4 extraData0 : TEXCOORD4;
               // float4 extraData1 : TEXCOORD5;
               // float4 extraData2 : TEXCOORD6;
               // float4 extraData3 : TEXCOORD7;

               UNITY_VERTEX_INPUT_INSTANCE_ID
               UNITY_VERTEX_OUTPUT_STEREO
            };

            struct ExtraData
            {
               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;
            };


            float3 WorldToTangentSpace(ShaderData d, float3 normal)
            {
               return mul(d.TBNMatrix, normal);
            }

            float3 TangentToWorldSpace(ShaderData d, float3 normal)
            {
               return mul(normal, d.TBNMatrix);
            }

            // in this case, make standard more like SRPs, because we can't fix
            // GetWorldToObjectMatrix() in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(GetWorldToObjectMatrix(), p); };
               float3 TransformObjectToWorld(float3 p) { return mul(GetObjectToWorldMatrix(), p); };
               float4x4 GetWorldToObjectMatrix() { return GetWorldToObjectMatrix(); }
               float4x4 GetObjectToWorldMatrix() { return GetObjectToWorldMatrix(); }
            #endif


            
            CBUFFER_START(UnityPerMaterial)

               	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


            CBUFFER_END

            
   half3 BlendDetailNormal(half3 n1, half3 n2)
   {
      return normalize(half3(n1.xy + n2.xy, n1.z*n2.z));
   }

   // We share samplers with the albedo - which free's up more for stacking.

   UNITY_DECLARE_TEX2D(_AlbedoMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_NormalMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_MaskMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_EmissionMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_DetailMap);


	void SurfaceFunction(inout LightingInputs o, ShaderData d)
	{
      float2 uv = d.texcoord0.xy * _AlbedoMap_ST.xy + _AlbedoMap_ST.zw;

      half4 c = UNITY_SAMPLE_TEX2D(_AlbedoMap, uv);
      o.Albedo = c.rgb * _Tint.rgb;
		o.Normal = UnpackScaleNormal(UNITY_SAMPLE_TEX2D_SAMPLER(_NormalMap, _AlbedoMap, uv), _NormalStrength);

      half detailMask = 1;
      #if _MASKMAP
          // Unity mask map format (R) Metallic, (G) Occlusion, (B) Detail Mask (A) Smoothness
         half4 mask = UNITY_SAMPLE_TEX2D_SAMPLER(_MaskMap, _AlbedoMap, uv);
         o.Metallic = mask.r;
         o.Occlusion = mask.g;
         o.Smoothness = mask.a;
         detailMask = mask.b;
      #endif // separate maps


      half3 emission = 0;
      #if defined(_EMISSION)
         o.Emission = UNITY_SAMPLE_TEX2D_SAMPLER(_EmissionMap, _AlbedoMap, uv).rgb * _EmissionStrength;
      #endif

      #if defined(_DETAIL)
         float2 detailUV = uv * _DetailMap_ST.xy + _DetailMap_ST.zw;
         half4 detailSample = UNITY_SAMPLE_TEX2D_SAMPLER(_DetailMap, _AlbedoMap, detailUV);
         o.Normal = BlendDetailNormal(o.Normal, UnpackScaleNormal(detailSample, _DetailNormalStrength * detailMask));
         o.Albedo = lerp(o.Albedo, o.Albedo * 2 * detailSample.x,  detailMask * _DetailAlbedoStrength);
         o.Smoothness = lerp(o.Smoothness, o.Smoothness * 2 * detailSample.z, detailMask * _DetailSmoothnessStrength);
      #endif


		o.Alpha = c.a;
	}



        
            void ChainSurfaceFunction(inout LightingInputs l, ShaderData d)
            {
                   SurfaceFunction(l, d);
                 // SurfaceFunction_Ext1(l, d);
                 // SurfaceFunction_Ext2(l, d);
                 // SurfaceFunction_Ext3(l, d);
                 // SurfaceFunction_Ext4(l, d);
                 // SurfaceFunction_Ext5(l, d);
                 // SurfaceFunction_Ext6(l, d);
                 // SurfaceFunction_Ext7(l, d);
                 // SurfaceFunction_Ext8(l, d);
                 // SurfaceFunction_Ext9(l, d);
            }

            void ChainModifyVertex(inout VertexData v, inout VertexToPixel v2p)
            {
                 ExtraData d = (ExtraData)0;
                 //  ModifyVertex(v, d);
                 // ModifyVertex_Ext1(v, d);
                 // ModifyVertex_Ext2(v, d);
                 // ModifyVertex_Ext3(v, d);
                 // ModifyVertex_Ext4(v, d);
                 // ModifyVertex_Ext5(v, d);
                 // ModifyVertex_Ext6(v, d);
                 // ModifyVertex_Ext7(v, d);
                 // ModifyVertex_Ext8(v, d);
                 // ModifyVertex_Ext9(v, d);
                 // v2p.extraData0 = d.extraData0;
                 // v2p.extraData1 = d.extraData1;
                 // v2p.extraData2 = d.extraData2;
                 // v2p.extraData3 = d.extraData3;
            }

            void ChainModifyTessellatedVertex(inout VertexData v, inout VertexToPixel v2p)
            {
               ExtraData d = (ExtraData)0;
               //  ModifyTessellatedVertex(v, d);
               // ModifyTessellatedVertex_Ext1(v, d);
               // ModifyTessellatedVertex_Ext2(v, d);
               // ModifyTessellatedVertex_Ext3(v, d);
               // ModifyTessellatedVertex_Ext4(v, d);
               // ModifyTessellatedVertex_Ext5(v, d);
               // ModifyTessellatedVertex_Ext6(v, d);
               // ModifyTessellatedVertex_Ext7(v, d);
               // ModifyTessellatedVertex_Ext8(v, d);
               // ModifyTessellatedVertex_Ext9(v, d);
               // v2p.extraData0 = d.extraData0;
               // v2p.extraData1 = d.extraData1;
               // v2p.extraData2 = d.extraData2;
               // v2p.extraData3 = d.extraData3;
            }



            

         ShaderData CreateShaderData(VertexToPixel i)
         {
            ShaderData d = (ShaderData)0;
            d.worldSpacePosition = i.worldPos;

            d.worldSpaceNormal = i.worldNormal;
            d.worldSpaceTangent = i.worldTangent.xyz;
            float3 bitangent = cross(i.worldTangent.xyz, i.worldNormal) * i.worldTangent.w;
            

            d.TBNMatrix = float3x3(d.worldSpaceTangent, bitangent, d.worldSpaceNormal);
            d.worldSpaceViewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
            d.tangentSpaceViewDir = mul(d.TBNMatrix, d.worldSpaceViewDir);
             d.texcoord0 = i.texcoord0;
            // d.texcoord1 = i.texcoord1;
            // d.texcoord2 = i.texcoord2;
            // d.texcoord3 = i.texcoord3;
            // d.vertexColor = i.color;

            // these rarely get used, so we back transform them. Usually will be stripped.
            // d.localSpacePosition = mul(GetWorldToObjectMatrix(), i.worldPos);
            // d.localSpaceNormal = mul(GetWorldToObjectMatrix(), i.worldNormal);
            // d.localSpaceTangent = mul(GetWorldToObjectMatrix(), i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         

            
         #if defined(SHADERPASS_SHADOWCASTER)
            float3 _LightDirection;
         #endif

         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
           
           VertexToPixel o = (VertexToPixel)0;

           UNITY_SETUP_INSTANCE_ID(v);
           UNITY_TRANSFER_INSTANCE_ID(v, o);
           UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);


#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

            o.texcoord0 = v.texcoord0;
           // o.texcoord1 = v.texcoord1;
           // o.texcoord2 = v.texcoord2;
           // o.texcoord3 = v.texcoord3;
           // o.color = v.color;

           VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
           o.worldPos = TransformObjectToWorld(v.vertex.xyz);
           o.worldNormal = TransformObjectToWorldNormal(v.normal);
           o.worldTangent = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);


          #if defined(SHADERPASS_SHADOWCASTER)
              // Define shadow pass specific clip position for Universal
              o.pos = TransformWorldToHClip(ApplyShadowBias(o.worldPos, o.worldNormal, _LightDirection));
              #if UNITY_REVERSED_Z
                  o.pos.z = min(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #else
                  o.pos.z = max(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #endif
          #elif defined(SHADERPASS_META)
              o.pos = MetaVertexPosition(float4(v.vertex.xyz, 0), v.texcoord1, v.texcoord2, unity_LightmapST, unity_DynamicLightmapST);
          #else
              o.pos = TransformWorldToHClip(o.worldPos);
          #endif


              // o.screenPos = ComputeScreenPos(o.pos, _ProjectionParams.x);
          

          #if defined(SHADERPASS_FORWARD)
              OUTPUT_LIGHTMAP_UV(o.uv1, unity_LightmapST, output.lightmapUV);
              OUTPUT_SH(o.worldNormal, o.sh);
          #endif

          #ifdef VARYINGS_NEED_FOG_AND_VERTEX_LIGHT
              half3 vertexLight = VertexLighting(o.worldPos, o.worldNormal);
              half fogFactor = ComputeFogFactor(o.pos.z);
              o.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
          #endif

          #ifdef _MAIN_LIGHT_SHADOWS
              o.shadowCoord = GetShadowCoord(vertexInput);
          #endif

           return o;
         }


            

            // fragment shader
            half4 Frag (VertexToPixel IN) : SV_Target
            {
               UNITY_SETUP_INSTANCE_ID(IN);
               UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

               ShaderData d = CreateShaderData(IN);

               LightingInputs l = (LightingInputs)0;


               l.Albedo = half3(0.5, 0.5, 0.5);
               l.Normal = float3(0,0,1);
               l.Occlusion = 1;
               l.Alpha = 1;

               ChainSurfaceFunction(l, d);

               return 0;

            }

         ENDHLSL

      }


      
        Pass
        {
            Name "Meta"
            Tags 
            { 
                "LightMode" = "Meta"
            }

             // Render State
            Blend One Zero, One Zero
            Cull Back
            ZTest LEqual
            ZWrite On
            // ColorMask: <None>

            HLSLPROGRAM

               #pragma vertex Vert
   #pragma fragment Frag

            #pragma target 3.0

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
        
            #define SHADERPASS_META

                 
            // Includes
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"
            #include "Packages/com.unity.shadergraph/ShaderGraphLibrary/ShaderVariablesFunctions.hlsl"
        

               #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _URP 1


                  #undef WorldNormalVector
      #define WorldNormalVector(data, normal) mul(normal, data.TBNMatrix)
      
      #define UnityObjectToWorldNormal(normal) mul(GetObjectToWorldMatrix(), normal)

      #define _WorldSpaceLightPos0 _MainLightPosition
      
      #define UNITY_DECLARE_TEX2D(name) TEXTURE2D(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2D_NOSAMPLER(name) TEXTURE2D(name);
      #define UNITY_DECLARE_TEX2DARRAY(name) TEXTURE2D_ARRAY(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(name) TEXTURE2D_ARRAY(name);

      #define UNITY_SAMPLE_TEX2DARRAY(tex,coord)            SAMPLE_TEXTURE2D_ARRAY(tex, sampler_##tex, coord.xy, coord.z)
      #define UNITY_SAMPLE_TEX2DARRAY_LOD(tex,coord,lod)    SAMPLE_TEXTURE2D_ARRAY_LOD(tex, sampler_##tex, coord.xy, coord.z, lod)
      #define UNITY_SAMPLE_TEX2D(tex, coord)                SAMPLE_TEXTURE2D(tex, sampler_##tex, coord)
      #define UNITY_SAMPLE_TEX2D_SAMPLER(tex, samp, coord)  SAMPLE_TEXTURE2D(tex, sampler_##samp, coord)

     
      #if defined(UNITY_COMPILER_HLSL)
         #define UNITY_INITIALIZE_OUTPUT(type,name) name = (type)0;
      #else
         #define UNITY_INITIALIZE_OUTPUT(type,name)
      #endif

      #define sampler2D_float sampler2D
      #define sampler2D_half sampler2D



      // data across stages, stripped like the above.
      struct VertexToPixel
      {
         float4 pos : SV_POSITION;
         float3 worldPos : TEXCOORD0;
         float3 worldNormal : TEXCOORD1;
         float4 worldTangent : TEXCOORD2;
          float4 texcoord0 : TEXCCOORD3;
         // float4 texcoord1 : TEXCCOORD4;
         // float4 texcoord2 : TEXCCOORD5;
         // float4 texcoord3 : TEXCCOORD6;
         // float4 screenPos : TEXCOORD7;
         // float4 color : COLOR;

         // float4 extraData0 : TEXCOORD12;
         // float4 extraData1 : TEXCOORD13;
         // float4 extraData2 : TEXCOORD14;
         // float4 extraData3 : TEXCOORD15;
            
         #if defined(LIGHTMAP_ON)
            float2 lightmapUV : TEXCOORD8;
         #endif
         #if !defined(LIGHTMAP_ON)
            float3 sh : TEXCOORD9;
         #endif
            float4 fogFactorAndVertexLight : TEXCOORD10;
            float4 shadowCoord : TEXCOORD11;
         #if UNITY_ANY_INSTANCING_ENABLED
            uint instanceID : CUSTOM_INSTANCE_ID;
         #endif
         #if (defined(UNITY_STEREO_INSTANCING_ENABLED))
            uint stereoTargetEyeIndexAsRTArrayIdx : SV_RenderTargetArrayIndex;
         #endif
         #if (defined(UNITY_STEREO_MULTIVIEW_ENABLED)) || (defined(UNITY_STEREO_INSTANCING_ENABLED) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)))
            uint stereoTargetEyeIndexAsBlendIdx0 : BLENDINDICES0;
         #endif
         #if defined(SHADER_STAGE_FRAGMENT) && defined(VARYINGS_NEED_CULLFACE)
            FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC;
         #endif
      };


            
            
            // data describing the user output of a pixel
            struct LightingInputs
            {
               half3 Albedo;
               half Height;
               half3 Normal;
               half Smoothness;
               half3 Emission;
               half Metallic;
               half3 Specular;
               half Occlusion;
               half Alpha;
               // HDRP Only
               half SpecularOcclusion;
               half SubsurfaceMask;
               half Thickness;
               half CoatMask;
               half Anisotropy;
               half iridescenceMask;
               half iridescenceThickness;
            };

            // data the user might need, this will grow to be big. But easy to strip
            struct ShaderData
            {
               float3 localSpacePosition;
               float3 localSpaceNormal;
               float3 localSpaceTangent;
        
               float3 worldSpacePosition;
               float3 worldSpaceNormal;
               float3 worldSpaceTangent;

               float3 worldSpaceViewDir;
               float3 tangentSpaceViewDir;

               float4 texcoord0;
               float4 texcoord1;
               float4 texcoord2;
               float4 texcoord3;

               float2 screenUV;
               float4 screenPos;

               float4 vertexColor;

               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;

               float3x3 TBNMatrix;
            };

            struct VertexData
            {
               float4 vertex : POSITION;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;
            
               UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct TessVertex 
            {
               float4 vertex : INTERNALTESSPOS;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;

               
               // float4 extraData0 : TEXCOORD4;
               // float4 extraData1 : TEXCOORD5;
               // float4 extraData2 : TEXCOORD6;
               // float4 extraData3 : TEXCOORD7;

               UNITY_VERTEX_INPUT_INSTANCE_ID
               UNITY_VERTEX_OUTPUT_STEREO
            };

            struct ExtraData
            {
               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;
            };


            float3 WorldToTangentSpace(ShaderData d, float3 normal)
            {
               return mul(d.TBNMatrix, normal);
            }

            float3 TangentToWorldSpace(ShaderData d, float3 normal)
            {
               return mul(normal, d.TBNMatrix);
            }

            // in this case, make standard more like SRPs, because we can't fix
            // GetWorldToObjectMatrix() in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(GetWorldToObjectMatrix(), p); };
               float3 TransformObjectToWorld(float3 p) { return mul(GetObjectToWorldMatrix(), p); };
               float4x4 GetWorldToObjectMatrix() { return GetWorldToObjectMatrix(); }
               float4x4 GetObjectToWorldMatrix() { return GetObjectToWorldMatrix(); }
            #endif


            
            CBUFFER_START(UnityPerMaterial)

               	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


            CBUFFER_END

            
   half3 BlendDetailNormal(half3 n1, half3 n2)
   {
      return normalize(half3(n1.xy + n2.xy, n1.z*n2.z));
   }

   // We share samplers with the albedo - which free's up more for stacking.

   UNITY_DECLARE_TEX2D(_AlbedoMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_NormalMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_MaskMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_EmissionMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_DetailMap);


	void SurfaceFunction(inout LightingInputs o, ShaderData d)
	{
      float2 uv = d.texcoord0.xy * _AlbedoMap_ST.xy + _AlbedoMap_ST.zw;

      half4 c = UNITY_SAMPLE_TEX2D(_AlbedoMap, uv);
      o.Albedo = c.rgb * _Tint.rgb;
		o.Normal = UnpackScaleNormal(UNITY_SAMPLE_TEX2D_SAMPLER(_NormalMap, _AlbedoMap, uv), _NormalStrength);

      half detailMask = 1;
      #if _MASKMAP
          // Unity mask map format (R) Metallic, (G) Occlusion, (B) Detail Mask (A) Smoothness
         half4 mask = UNITY_SAMPLE_TEX2D_SAMPLER(_MaskMap, _AlbedoMap, uv);
         o.Metallic = mask.r;
         o.Occlusion = mask.g;
         o.Smoothness = mask.a;
         detailMask = mask.b;
      #endif // separate maps


      half3 emission = 0;
      #if defined(_EMISSION)
         o.Emission = UNITY_SAMPLE_TEX2D_SAMPLER(_EmissionMap, _AlbedoMap, uv).rgb * _EmissionStrength;
      #endif

      #if defined(_DETAIL)
         float2 detailUV = uv * _DetailMap_ST.xy + _DetailMap_ST.zw;
         half4 detailSample = UNITY_SAMPLE_TEX2D_SAMPLER(_DetailMap, _AlbedoMap, detailUV);
         o.Normal = BlendDetailNormal(o.Normal, UnpackScaleNormal(detailSample, _DetailNormalStrength * detailMask));
         o.Albedo = lerp(o.Albedo, o.Albedo * 2 * detailSample.x,  detailMask * _DetailAlbedoStrength);
         o.Smoothness = lerp(o.Smoothness, o.Smoothness * 2 * detailSample.z, detailMask * _DetailSmoothnessStrength);
      #endif


		o.Alpha = c.a;
	}



        
            void ChainSurfaceFunction(inout LightingInputs l, ShaderData d)
            {
                   SurfaceFunction(l, d);
                 // SurfaceFunction_Ext1(l, d);
                 // SurfaceFunction_Ext2(l, d);
                 // SurfaceFunction_Ext3(l, d);
                 // SurfaceFunction_Ext4(l, d);
                 // SurfaceFunction_Ext5(l, d);
                 // SurfaceFunction_Ext6(l, d);
                 // SurfaceFunction_Ext7(l, d);
                 // SurfaceFunction_Ext8(l, d);
                 // SurfaceFunction_Ext9(l, d);
            }

            void ChainModifyVertex(inout VertexData v, inout VertexToPixel v2p)
            {
                 ExtraData d = (ExtraData)0;
                 //  ModifyVertex(v, d);
                 // ModifyVertex_Ext1(v, d);
                 // ModifyVertex_Ext2(v, d);
                 // ModifyVertex_Ext3(v, d);
                 // ModifyVertex_Ext4(v, d);
                 // ModifyVertex_Ext5(v, d);
                 // ModifyVertex_Ext6(v, d);
                 // ModifyVertex_Ext7(v, d);
                 // ModifyVertex_Ext8(v, d);
                 // ModifyVertex_Ext9(v, d);
                 // v2p.extraData0 = d.extraData0;
                 // v2p.extraData1 = d.extraData1;
                 // v2p.extraData2 = d.extraData2;
                 // v2p.extraData3 = d.extraData3;
            }

            void ChainModifyTessellatedVertex(inout VertexData v, inout VertexToPixel v2p)
            {
               ExtraData d = (ExtraData)0;
               //  ModifyTessellatedVertex(v, d);
               // ModifyTessellatedVertex_Ext1(v, d);
               // ModifyTessellatedVertex_Ext2(v, d);
               // ModifyTessellatedVertex_Ext3(v, d);
               // ModifyTessellatedVertex_Ext4(v, d);
               // ModifyTessellatedVertex_Ext5(v, d);
               // ModifyTessellatedVertex_Ext6(v, d);
               // ModifyTessellatedVertex_Ext7(v, d);
               // ModifyTessellatedVertex_Ext8(v, d);
               // ModifyTessellatedVertex_Ext9(v, d);
               // v2p.extraData0 = d.extraData0;
               // v2p.extraData1 = d.extraData1;
               // v2p.extraData2 = d.extraData2;
               // v2p.extraData3 = d.extraData3;
            }



            

         ShaderData CreateShaderData(VertexToPixel i)
         {
            ShaderData d = (ShaderData)0;
            d.worldSpacePosition = i.worldPos;

            d.worldSpaceNormal = i.worldNormal;
            d.worldSpaceTangent = i.worldTangent.xyz;
            float3 bitangent = cross(i.worldTangent.xyz, i.worldNormal) * i.worldTangent.w;
            

            d.TBNMatrix = float3x3(d.worldSpaceTangent, bitangent, d.worldSpaceNormal);
            d.worldSpaceViewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
            d.tangentSpaceViewDir = mul(d.TBNMatrix, d.worldSpaceViewDir);
             d.texcoord0 = i.texcoord0;
            // d.texcoord1 = i.texcoord1;
            // d.texcoord2 = i.texcoord2;
            // d.texcoord3 = i.texcoord3;
            // d.vertexColor = i.color;

            // these rarely get used, so we back transform them. Usually will be stripped.
            // d.localSpacePosition = mul(GetWorldToObjectMatrix(), i.worldPos);
            // d.localSpaceNormal = mul(GetWorldToObjectMatrix(), i.worldNormal);
            // d.localSpaceTangent = mul(GetWorldToObjectMatrix(), i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         

            
         #if defined(SHADERPASS_SHADOWCASTER)
            float3 _LightDirection;
         #endif

         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
           
           VertexToPixel o = (VertexToPixel)0;

           UNITY_SETUP_INSTANCE_ID(v);
           UNITY_TRANSFER_INSTANCE_ID(v, o);
           UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);


#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

            o.texcoord0 = v.texcoord0;
           // o.texcoord1 = v.texcoord1;
           // o.texcoord2 = v.texcoord2;
           // o.texcoord3 = v.texcoord3;
           // o.color = v.color;

           VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
           o.worldPos = TransformObjectToWorld(v.vertex.xyz);
           o.worldNormal = TransformObjectToWorldNormal(v.normal);
           o.worldTangent = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);


          #if defined(SHADERPASS_SHADOWCASTER)
              // Define shadow pass specific clip position for Universal
              o.pos = TransformWorldToHClip(ApplyShadowBias(o.worldPos, o.worldNormal, _LightDirection));
              #if UNITY_REVERSED_Z
                  o.pos.z = min(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #else
                  o.pos.z = max(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #endif
          #elif defined(SHADERPASS_META)
              o.pos = MetaVertexPosition(float4(v.vertex.xyz, 0), v.texcoord1, v.texcoord2, unity_LightmapST, unity_DynamicLightmapST);
          #else
              o.pos = TransformWorldToHClip(o.worldPos);
          #endif


              // o.screenPos = ComputeScreenPos(o.pos, _ProjectionParams.x);
          

          #if defined(SHADERPASS_FORWARD)
              OUTPUT_LIGHTMAP_UV(o.uv1, unity_LightmapST, output.lightmapUV);
              OUTPUT_SH(o.worldNormal, o.sh);
          #endif

          #ifdef VARYINGS_NEED_FOG_AND_VERTEX_LIGHT
              half3 vertexLight = VertexLighting(o.worldPos, o.worldNormal);
              half fogFactor = ComputeFogFactor(o.pos.z);
              o.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
          #endif

          #ifdef _MAIN_LIGHT_SHADOWS
              o.shadowCoord = GetShadowCoord(vertexInput);
          #endif

           return o;
         }


            

            // fragment shader
            half4 Frag (VertexToPixel IN) : SV_Target
            {
               UNITY_SETUP_INSTANCE_ID(IN);

               ShaderData d = CreateShaderData(IN);

               LightingInputs l = (LightingInputs)0;

               l.Albedo = half3(0.5, 0.5, 0.5);
               l.Normal = float3(0,0,1);
               l.Occlusion = 1;
               l.Alpha = 1;

               ChainSurfaceFunction(l, d);

               MetaInput metaInput = (MetaInput)0;
               metaInput.Albedo = l.Albedo;
               metaInput.Emission = l.Emission;

               return MetaFragment(metaInput);

            }

         ENDHLSL

      }


      
        Pass
        {
            // Name: <None>
            Tags 
            { 
                "LightMode" = "Universal2D"
            }
           
            // Render State
            Blend One Zero, One Zero
            Cull Back
            ZTest LEqual
            ZWrite On
            // ColorMask: <None>

            HLSLPROGRAM

               #pragma vertex Vert
   #pragma fragment Frag

            #pragma target 3.0

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma multi_compile_instancing
        
            #define SHADERPASS_2D


            // Includes
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderGraphFunctions.hlsl"
            #include "Packages/com.unity.shadergraph/ShaderGraphLibrary/ShaderVariablesFunctions.hlsl"
        

               #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _URP 1


                  #undef WorldNormalVector
      #define WorldNormalVector(data, normal) mul(normal, data.TBNMatrix)
      
      #define UnityObjectToWorldNormal(normal) mul(GetObjectToWorldMatrix(), normal)

      #define _WorldSpaceLightPos0 _MainLightPosition
      
      #define UNITY_DECLARE_TEX2D(name) TEXTURE2D(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2D_NOSAMPLER(name) TEXTURE2D(name);
      #define UNITY_DECLARE_TEX2DARRAY(name) TEXTURE2D_ARRAY(name); SAMPLER(sampler_##name);
      #define UNITY_DECLARE_TEX2DARRAY_NOSAMPLER(name) TEXTURE2D_ARRAY(name);

      #define UNITY_SAMPLE_TEX2DARRAY(tex,coord)            SAMPLE_TEXTURE2D_ARRAY(tex, sampler_##tex, coord.xy, coord.z)
      #define UNITY_SAMPLE_TEX2DARRAY_LOD(tex,coord,lod)    SAMPLE_TEXTURE2D_ARRAY_LOD(tex, sampler_##tex, coord.xy, coord.z, lod)
      #define UNITY_SAMPLE_TEX2D(tex, coord)                SAMPLE_TEXTURE2D(tex, sampler_##tex, coord)
      #define UNITY_SAMPLE_TEX2D_SAMPLER(tex, samp, coord)  SAMPLE_TEXTURE2D(tex, sampler_##samp, coord)

     
      #if defined(UNITY_COMPILER_HLSL)
         #define UNITY_INITIALIZE_OUTPUT(type,name) name = (type)0;
      #else
         #define UNITY_INITIALIZE_OUTPUT(type,name)
      #endif

      #define sampler2D_float sampler2D
      #define sampler2D_half sampler2D



      // data across stages, stripped like the above.
      struct VertexToPixel
      {
         float4 pos : SV_POSITION;
         float3 worldPos : TEXCOORD0;
         float3 worldNormal : TEXCOORD1;
         float4 worldTangent : TEXCOORD2;
          float4 texcoord0 : TEXCCOORD3;
         // float4 texcoord1 : TEXCCOORD4;
         // float4 texcoord2 : TEXCCOORD5;
         // float4 texcoord3 : TEXCCOORD6;
         // float4 screenPos : TEXCOORD7;
         // float4 color : COLOR;

         // float4 extraData0 : TEXCOORD12;
         // float4 extraData1 : TEXCOORD13;
         // float4 extraData2 : TEXCOORD14;
         // float4 extraData3 : TEXCOORD15;
            
         #if defined(LIGHTMAP_ON)
            float2 lightmapUV : TEXCOORD8;
         #endif
         #if !defined(LIGHTMAP_ON)
            float3 sh : TEXCOORD9;
         #endif
            float4 fogFactorAndVertexLight : TEXCOORD10;
            float4 shadowCoord : TEXCOORD11;
         #if UNITY_ANY_INSTANCING_ENABLED
            uint instanceID : CUSTOM_INSTANCE_ID;
         #endif
         #if (defined(UNITY_STEREO_INSTANCING_ENABLED))
            uint stereoTargetEyeIndexAsRTArrayIdx : SV_RenderTargetArrayIndex;
         #endif
         #if (defined(UNITY_STEREO_MULTIVIEW_ENABLED)) || (defined(UNITY_STEREO_INSTANCING_ENABLED) && (defined(SHADER_API_GLES3) || defined(SHADER_API_GLCORE)))
            uint stereoTargetEyeIndexAsBlendIdx0 : BLENDINDICES0;
         #endif
         #if defined(SHADER_STAGE_FRAGMENT) && defined(VARYINGS_NEED_CULLFACE)
            FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC;
         #endif
      };



            
            
            // data describing the user output of a pixel
            struct LightingInputs
            {
               half3 Albedo;
               half Height;
               half3 Normal;
               half Smoothness;
               half3 Emission;
               half Metallic;
               half3 Specular;
               half Occlusion;
               half Alpha;
               // HDRP Only
               half SpecularOcclusion;
               half SubsurfaceMask;
               half Thickness;
               half CoatMask;
               half Anisotropy;
               half iridescenceMask;
               half iridescenceThickness;
            };

            // data the user might need, this will grow to be big. But easy to strip
            struct ShaderData
            {
               float3 localSpacePosition;
               float3 localSpaceNormal;
               float3 localSpaceTangent;
        
               float3 worldSpacePosition;
               float3 worldSpaceNormal;
               float3 worldSpaceTangent;

               float3 worldSpaceViewDir;
               float3 tangentSpaceViewDir;

               float4 texcoord0;
               float4 texcoord1;
               float4 texcoord2;
               float4 texcoord3;

               float2 screenUV;
               float4 screenPos;

               float4 vertexColor;

               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;

               float3x3 TBNMatrix;
            };

            struct VertexData
            {
               float4 vertex : POSITION;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;
            
               UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct TessVertex 
            {
               float4 vertex : INTERNALTESSPOS;
               float3 normal : NORMAL;
               float4 tangent : TANGENT;
               float4 texcoord0 : TEXCOORD0;
               float4 texcoord1 : TEXCOORD1;
               float4 texcoord2 : TEXCOORD2;
               // float4 texcoord3 : TEXCOORD3;
               // float4 vertexColor : COLOR;

               
               // float4 extraData0 : TEXCOORD4;
               // float4 extraData1 : TEXCOORD5;
               // float4 extraData2 : TEXCOORD6;
               // float4 extraData3 : TEXCOORD7;

               UNITY_VERTEX_INPUT_INSTANCE_ID
               UNITY_VERTEX_OUTPUT_STEREO
            };

            struct ExtraData
            {
               float4 extraData0;
               float4 extraData1;
               float4 extraData2;
               float4 extraData3;
            };


            float3 WorldToTangentSpace(ShaderData d, float3 normal)
            {
               return mul(d.TBNMatrix, normal);
            }

            float3 TangentToWorldSpace(ShaderData d, float3 normal)
            {
               return mul(normal, d.TBNMatrix);
            }

            // in this case, make standard more like SRPs, because we can't fix
            // GetWorldToObjectMatrix() in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(GetWorldToObjectMatrix(), p); };
               float3 TransformObjectToWorld(float3 p) { return mul(GetObjectToWorldMatrix(), p); };
               float4x4 GetWorldToObjectMatrix() { return GetWorldToObjectMatrix(); }
               float4x4 GetObjectToWorldMatrix() { return GetObjectToWorldMatrix(); }
            #endif


            
            CBUFFER_START(UnityPerMaterial)

               	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


            CBUFFER_END

            
   half3 BlendDetailNormal(half3 n1, half3 n2)
   {
      return normalize(half3(n1.xy + n2.xy, n1.z*n2.z));
   }

   // We share samplers with the albedo - which free's up more for stacking.

   UNITY_DECLARE_TEX2D(_AlbedoMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_NormalMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_MaskMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_EmissionMap);
   UNITY_DECLARE_TEX2D_NOSAMPLER(_DetailMap);


	void SurfaceFunction(inout LightingInputs o, ShaderData d)
	{
      float2 uv = d.texcoord0.xy * _AlbedoMap_ST.xy + _AlbedoMap_ST.zw;

      half4 c = UNITY_SAMPLE_TEX2D(_AlbedoMap, uv);
      o.Albedo = c.rgb * _Tint.rgb;
		o.Normal = UnpackScaleNormal(UNITY_SAMPLE_TEX2D_SAMPLER(_NormalMap, _AlbedoMap, uv), _NormalStrength);

      half detailMask = 1;
      #if _MASKMAP
          // Unity mask map format (R) Metallic, (G) Occlusion, (B) Detail Mask (A) Smoothness
         half4 mask = UNITY_SAMPLE_TEX2D_SAMPLER(_MaskMap, _AlbedoMap, uv);
         o.Metallic = mask.r;
         o.Occlusion = mask.g;
         o.Smoothness = mask.a;
         detailMask = mask.b;
      #endif // separate maps


      half3 emission = 0;
      #if defined(_EMISSION)
         o.Emission = UNITY_SAMPLE_TEX2D_SAMPLER(_EmissionMap, _AlbedoMap, uv).rgb * _EmissionStrength;
      #endif

      #if defined(_DETAIL)
         float2 detailUV = uv * _DetailMap_ST.xy + _DetailMap_ST.zw;
         half4 detailSample = UNITY_SAMPLE_TEX2D_SAMPLER(_DetailMap, _AlbedoMap, detailUV);
         o.Normal = BlendDetailNormal(o.Normal, UnpackScaleNormal(detailSample, _DetailNormalStrength * detailMask));
         o.Albedo = lerp(o.Albedo, o.Albedo * 2 * detailSample.x,  detailMask * _DetailAlbedoStrength);
         o.Smoothness = lerp(o.Smoothness, o.Smoothness * 2 * detailSample.z, detailMask * _DetailSmoothnessStrength);
      #endif


		o.Alpha = c.a;
	}



        
            void ChainSurfaceFunction(inout LightingInputs l, ShaderData d)
            {
                   SurfaceFunction(l, d);
                 // SurfaceFunction_Ext1(l, d);
                 // SurfaceFunction_Ext2(l, d);
                 // SurfaceFunction_Ext3(l, d);
                 // SurfaceFunction_Ext4(l, d);
                 // SurfaceFunction_Ext5(l, d);
                 // SurfaceFunction_Ext6(l, d);
                 // SurfaceFunction_Ext7(l, d);
                 // SurfaceFunction_Ext8(l, d);
                 // SurfaceFunction_Ext9(l, d);
            }

            void ChainModifyVertex(inout VertexData v, inout VertexToPixel v2p)
            {
                 ExtraData d = (ExtraData)0;
                 //  ModifyVertex(v, d);
                 // ModifyVertex_Ext1(v, d);
                 // ModifyVertex_Ext2(v, d);
                 // ModifyVertex_Ext3(v, d);
                 // ModifyVertex_Ext4(v, d);
                 // ModifyVertex_Ext5(v, d);
                 // ModifyVertex_Ext6(v, d);
                 // ModifyVertex_Ext7(v, d);
                 // ModifyVertex_Ext8(v, d);
                 // ModifyVertex_Ext9(v, d);
                 // v2p.extraData0 = d.extraData0;
                 // v2p.extraData1 = d.extraData1;
                 // v2p.extraData2 = d.extraData2;
                 // v2p.extraData3 = d.extraData3;
            }

            void ChainModifyTessellatedVertex(inout VertexData v, inout VertexToPixel v2p)
            {
               ExtraData d = (ExtraData)0;
               //  ModifyTessellatedVertex(v, d);
               // ModifyTessellatedVertex_Ext1(v, d);
               // ModifyTessellatedVertex_Ext2(v, d);
               // ModifyTessellatedVertex_Ext3(v, d);
               // ModifyTessellatedVertex_Ext4(v, d);
               // ModifyTessellatedVertex_Ext5(v, d);
               // ModifyTessellatedVertex_Ext6(v, d);
               // ModifyTessellatedVertex_Ext7(v, d);
               // ModifyTessellatedVertex_Ext8(v, d);
               // ModifyTessellatedVertex_Ext9(v, d);
               // v2p.extraData0 = d.extraData0;
               // v2p.extraData1 = d.extraData1;
               // v2p.extraData2 = d.extraData2;
               // v2p.extraData3 = d.extraData3;
            }



            

         ShaderData CreateShaderData(VertexToPixel i)
         {
            ShaderData d = (ShaderData)0;
            d.worldSpacePosition = i.worldPos;

            d.worldSpaceNormal = i.worldNormal;
            d.worldSpaceTangent = i.worldTangent.xyz;
            float3 bitangent = cross(i.worldTangent.xyz, i.worldNormal) * i.worldTangent.w;
            

            d.TBNMatrix = float3x3(d.worldSpaceTangent, bitangent, d.worldSpaceNormal);
            d.worldSpaceViewDir = normalize(_WorldSpaceCameraPos - i.worldPos);
            d.tangentSpaceViewDir = mul(d.TBNMatrix, d.worldSpaceViewDir);
             d.texcoord0 = i.texcoord0;
            // d.texcoord1 = i.texcoord1;
            // d.texcoord2 = i.texcoord2;
            // d.texcoord3 = i.texcoord3;
            // d.vertexColor = i.color;

            // these rarely get used, so we back transform them. Usually will be stripped.
            // d.localSpacePosition = mul(GetWorldToObjectMatrix(), i.worldPos);
            // d.localSpaceNormal = mul(GetWorldToObjectMatrix(), i.worldNormal);
            // d.localSpaceTangent = mul(GetWorldToObjectMatrix(), i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         

            
         #if defined(SHADERPASS_SHADOWCASTER)
            float3 _LightDirection;
         #endif

         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
           
           VertexToPixel o = (VertexToPixel)0;

           UNITY_SETUP_INSTANCE_ID(v);
           UNITY_TRANSFER_INSTANCE_ID(v, o);
           UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);


#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

            o.texcoord0 = v.texcoord0;
           // o.texcoord1 = v.texcoord1;
           // o.texcoord2 = v.texcoord2;
           // o.texcoord3 = v.texcoord3;
           // o.color = v.color;

           VertexPositionInputs vertexInput = GetVertexPositionInputs(v.vertex.xyz);
           o.worldPos = TransformObjectToWorld(v.vertex.xyz);
           o.worldNormal = TransformObjectToWorldNormal(v.normal);
           o.worldTangent = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);


          #if defined(SHADERPASS_SHADOWCASTER)
              // Define shadow pass specific clip position for Universal
              o.pos = TransformWorldToHClip(ApplyShadowBias(o.worldPos, o.worldNormal, _LightDirection));
              #if UNITY_REVERSED_Z
                  o.pos.z = min(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #else
                  o.pos.z = max(o.pos.z, o.pos.w * UNITY_NEAR_CLIP_VALUE);
              #endif
          #elif defined(SHADERPASS_META)
              o.pos = MetaVertexPosition(float4(v.vertex.xyz, 0), v.texcoord1, v.texcoord2, unity_LightmapST, unity_DynamicLightmapST);
          #else
              o.pos = TransformWorldToHClip(o.worldPos);
          #endif


              // o.screenPos = ComputeScreenPos(o.pos, _ProjectionParams.x);
          

          #if defined(SHADERPASS_FORWARD)
              OUTPUT_LIGHTMAP_UV(o.uv1, unity_LightmapST, output.lightmapUV);
              OUTPUT_SH(o.worldNormal, o.sh);
          #endif

          #ifdef VARYINGS_NEED_FOG_AND_VERTEX_LIGHT
              half3 vertexLight = VertexLighting(o.worldPos, o.worldNormal);
              half fogFactor = ComputeFogFactor(o.pos.z);
              o.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
          #endif

          #ifdef _MAIN_LIGHT_SHADOWS
              o.shadowCoord = GetShadowCoord(vertexInput);
          #endif

           return o;
         }


            

            // fragment shader
            half4 Frag (VertexToPixel IN) : SV_Target
            {
               UNITY_SETUP_INSTANCE_ID(IN);
               UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);


               ShaderData d = CreateShaderData(IN);

               LightingInputs l = (LightingInputs)0;

               l.Albedo = half3(0.5, 0.5, 0.5);
               l.Normal = float3(0,0,1);
               l.Occlusion = 1;
               l.Alpha = 1;

               ChainSurfaceFunction(l, d);

               
               half4 color = half4(l.Albedo, l.Alpha);

               return color;

            }

         ENDHLSL

      }



   }
   
   
}
