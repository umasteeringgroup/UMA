// Upgrade NOTE: replaced 'defined FOG_COMBINED_WITH_WORLD_POS' with 'defined (FOG_COMBINED_WITH_WORLD_POS)'

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
      Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

      
      Pass
      {
		   Name "FORWARD"
		   Tags { "LightMode" = "ForwardBase" }
         

         CGPROGRAM
         // compile directives
            #pragma vertex Vert
   #pragma fragment Frag

         #pragma target 3.0
         #pragma multi_compile_instancing
         #pragma multi_compile_fog
         #pragma multi_compile_fwdbase
         #include "HLSLSupport.cginc"
         #define UNITY_INSTANCED_LOD_FADE
         #define UNITY_INSTANCED_SH
         #define UNITY_INSTANCED_LIGHTMAPSTS

         #include "UnityShaderVariables.cginc"
         #include "UnityShaderUtilities.cginc"
         // -------- variant for: <when no other keywords are defined>

         #include "UnityCG.cginc"
         #include "Lighting.cginc"
         #include "UnityPBSLighting.cginc"
         #include "AutoLight.cginc"

            #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _STANDARD 1



         

         // data across stages, stripped like the above.
         struct VertexToPixel
         {
            UNITY_POSITION(pos);
            float3 worldPos : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float4 worldTangent : TEXCOORD2;
             float4 texcoord0 : TEXCCOORD3;
            // float4 texcoord1 : TEXCCOORD4;
            // float4 texcoord2 : TEXCCOORD5;
            // float4 texcoord3 : TEXCCOORD6;
            // float4 screenPos : TEXCOORD7;
            // float4 color : COLOR;
            float4 lmap : TEXCOORD8;
            #if UNITY_SHOULD_SAMPLE_SH
               half3 sh : TEXCOORD9; // SH
            #endif
            #ifdef LIGHTMAP_ON
               UNITY_LIGHTING_COORDS(10,11)
            #else
               UNITY_FOG_COORDS(10)
               UNITY_SHADOW_COORDS(11)
            #endif

            // float4 extraData0 : TEXCOORD12;
            // float4 extraData1 : TEXCOORD13;
            // float4 extraData2 : TEXCOORD14;
            // float4 extraData3 : TEXCOORD15;


            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
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
            // unity_WorldToObject in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(unity_WorldToObject, p); };
               float3 TransformObjectToWorld(float3 p) { return mul(unity_ObjectToWorld, p); };
               float4x4 GetWorldToObjectMatrix() { return unity_WorldToObject; }
               float4x4 GetObjectToWorldMatrix() { return unity_ObjectToWorld; }
            #endif


            
         	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


         
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
            // d.localSpacePosition = mul(unity_WorldToObject, i.worldPos);
            // d.localSpaceNormal = mul(unity_WorldToObject, i.worldNormal);
            // d.localSpaceTangent = mul(unity_WorldToObject, i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         

         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
           UNITY_SETUP_INSTANCE_ID(v);
           VertexToPixel o;
           UNITY_INITIALIZE_OUTPUT(VertexToPixel,o);
           UNITY_TRANSFER_INSTANCE_ID(v,o);
           UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

           o.pos = UnityObjectToClipPos(v.vertex);
            o.texcoord0 = v.texcoord0;
           // o.texcoord1 = v.texcoord1;
           // o.texcoord2 = v.texcoord2;
           // o.texcoord3 = v.texcoord3;
           // o.color = v.color;
           // o.screenPos = ComputeScreenPos(o.pos);
           o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
           o.worldNormal = UnityObjectToWorldNormal(v.normal);
           o.worldTangent.xyz = UnityObjectToWorldDir(v.tangent.xyz);
           fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
           o.worldTangent.w = tangentSign;

           #ifdef DYNAMICLIGHTMAP_ON
           o.lmap.zw = v.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
           #endif
           #ifdef LIGHTMAP_ON
           o.lmap.xy = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
           #endif

           // SH/ambient and vertex lights
           #ifndef LIGHTMAP_ON
             #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
               o.sh = 0;
               // Approximated illumination from non-important point lights
               #ifdef VERTEXLIGHT_ON
                 o.sh += Shade4PointLights (
                   unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
                   unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
                   unity_4LightAtten0, o.worldPos, o.worldNormal);
               #endif
               o.sh = ShadeSHPerVertex (o.worldNormal, o.sh);
             #endif
           #endif // !LIGHTMAP_ON

           UNITY_TRANSFER_LIGHTING(o,v.texcoord1.xy); // pass shadow and, possibly, light cookie coordinates to pixel shader
           #ifdef FOG_COMBINED_WITH_TSPACE
             UNITY_TRANSFER_FOG_COMBINED_WITH_TSPACE(o,o.pos); // pass fog coordinates to pixel shader
           #elif defined (FOG_COMBINED_WITH_WORLD_POS)
             UNITY_TRANSFER_FOG_COMBINED_WITH_WORLD_POS(o,o.pos); // pass fog coordinates to pixel shader
           #else
             UNITY_TRANSFER_FOG(o,o.pos); // pass fog coordinates to pixel shader
           #endif

           return o;
         }

         

         // fragment shader
         fixed4 Frag (VertexToPixel IN) : SV_Target
         {
           UNITY_SETUP_INSTANCE_ID(IN);
           // prepare and unpack data
           #ifdef FOG_COMBINED_WITH_TSPACE
             UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
           #elif defined (FOG_COMBINED_WITH_WORLD_POS)
             UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
           #else
             UNITY_EXTRACT_FOG(IN);
           #endif

           ShaderData d = CreateShaderData(IN);

           LightingInputs l = (LightingInputs)0;

           l.Albedo = half3(0.5, 0.5, 0.5);
           l.Normal = float3(0,0,1);
           l.Occlusion = 1;
           l.Alpha = 1;

           ChainSurfaceFunction(l, d);


           #ifndef USING_DIRECTIONAL_LIGHT
             fixed3 lightDir = normalize(UnityWorldSpaceLightDir(d.worldSpacePosition));
           #else
             fixed3 lightDir = _WorldSpaceLightPos0.xyz;
           #endif
           float3 worldViewDir = normalize(UnityWorldSpaceViewDir(d.worldSpacePosition));

           // compute lighting & shadowing factor
           UNITY_LIGHT_ATTENUATION(atten, IN, d.worldSpacePosition)

           #if _USESPECULAR
              #ifdef UNITY_COMPILER_HLSL
              SurfaceOutputStandardSpecular o = (SurfaceOutputStandardSpecular)0;
              #else
              SurfaceOutputStandardSpecular o;
              #endif
              o.Specular = l.Specular;
           #else
              #ifdef UNITY_COMPILER_HLSL
              SurfaceOutputStandard o = (SurfaceOutputStandard)0;
              #else
              SurfaceOutputStandard o;
              #endif
              o.Metallic = l.Metallic;
           #endif

           o.Albedo = l.Albedo;
           o.Emission = l.Emission;
           o.Alpha = l.Alpha;
           o.Occlusion = l.Occlusion;

           
           o.Normal = normalize(TangentToWorldSpace(d, l.Normal));

           fixed4 c = 0;
           // Setup lighting environment
           UnityGI gi;
           UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
           gi.indirect.diffuse = 0;
           gi.indirect.specular = 0;
           gi.light.color = _LightColor0.rgb;
           gi.light.dir = lightDir;
           // Call GI (lightmaps/SH/reflections) lighting function
           UnityGIInput giInput;
           UNITY_INITIALIZE_OUTPUT(UnityGIInput, giInput);
           giInput.light = gi.light;
           giInput.worldPos = d.worldSpacePosition;
           giInput.worldViewDir = worldViewDir;
           giInput.atten = atten;
           #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
             giInput.lightmapUV = IN.lmap;
           #else
             giInput.lightmapUV = 0.0;
           #endif
           #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
             giInput.ambient = IN.sh;
           #else
             giInput.ambient.rgb = 0.0;
           #endif
           giInput.probeHDR[0] = unity_SpecCube0_HDR;
           giInput.probeHDR[1] = unity_SpecCube1_HDR;
           #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
             giInput.boxMin[0] = unity_SpecCube0_BoxMin; // .w holds lerp value for blending
           #endif
           #ifdef UNITY_SPECCUBE_BOX_PROJECTION
             giInput.boxMax[0] = unity_SpecCube0_BoxMax;
             giInput.probePosition[0] = unity_SpecCube0_ProbePosition;
             giInput.boxMax[1] = unity_SpecCube1_BoxMax;
             giInput.boxMin[1] = unity_SpecCube1_BoxMin;
             giInput.probePosition[1] = unity_SpecCube1_ProbePosition;
           #endif
           

           #if _USESPECULAR
              LightingStandardSpecular_GI(o, giInput, gi);
              c += LightingStandardSpecular (o, worldViewDir, gi);
           #else
              LightingStandard_GI(o, giInput, gi);
              c += LightingStandard (o, worldViewDir, gi);
           #endif

           c.rgb += o.Emission;

           UNITY_APPLY_FOG(_unity_fogCoord, c); // apply fog
           
           #if !_ALPHABLEND_ON
              UNITY_OPAQUE_ALPHA(c.a);
           #endif
           
           return c;
         }

         ENDCG

      }

	   // ---- forward rendering additive lights pass:
	   Pass
      {
		   Name "FORWARD"
		   Tags { "LightMode" = "ForwardAdd" }
		   ZWrite Off Blend One One
         
		
         CGPROGRAM

            #pragma vertex Vert
   #pragma fragment Frag

         // compile directives
         #pragma target 3.0
         #pragma multi_compile_instancing
         #pragma multi_compile_fog
         #pragma skip_variants INSTANCING_ON
         #pragma multi_compile_fwdadd_fullshadows
         #include "HLSLSupport.cginc"
         #define UNITY_INSTANCED_LOD_FADE
         #define UNITY_INSTANCED_SH
         #define UNITY_INSTANCED_LIGHTMAPSTS
         #include "UnityShaderVariables.cginc"
         #include "UnityShaderUtilities.cginc"


         #include "UnityCG.cginc"
         #include "Lighting.cginc"
         #include "UnityPBSLighting.cginc"
         #include "AutoLight.cginc"

            #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _STANDARD 1


         // data across stages, stripped like the above.
         struct VertexToPixel
         {
            UNITY_POSITION(pos);       // must be named pos because Unity does stupid macro stuff
            float3 worldPos : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float4 worldTangent : TEXCOORD2;
             float4 texcoord0 : TEXCCOORD3;
            // float4 texcoord1 : TEXCCOORD4;
            // float4 texcoord2 : TEXCCOORD5;
            // float4 texcoord3 : TEXCCOORD6;
            // float4 screenPos : TEXCOORD7;
            // float4 color : COLOR;

            float4 lmap : TEXCOORD8;
            #if UNITY_SHOULD_SAMPLE_SH
               half3 sh : TEXCOORD9; // SH
            #endif

            UNITY_LIGHTING_COORDS(10,11)
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO

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
            // unity_WorldToObject in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(unity_WorldToObject, p); };
               float3 TransformObjectToWorld(float3 p) { return mul(unity_ObjectToWorld, p); };
               float4x4 GetWorldToObjectMatrix() { return unity_WorldToObject; }
               float4x4 GetObjectToWorldMatrix() { return unity_ObjectToWorld; }
            #endif


            
         	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


         
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
            // d.localSpacePosition = mul(unity_WorldToObject, i.worldPos);
            // d.localSpaceNormal = mul(unity_WorldToObject, i.worldNormal);
            // d.localSpaceTangent = mul(unity_WorldToObject, i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         

         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
           UNITY_SETUP_INSTANCE_ID(v);
           VertexToPixel o;
           UNITY_INITIALIZE_OUTPUT(VertexToPixel,o);
           UNITY_TRANSFER_INSTANCE_ID(v,o);
           UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

           o.pos = UnityObjectToClipPos(v.vertex);
            o.texcoord0 = v.texcoord0;
           // o.texcoord1 = v.texcoord1;
           // o.texcoord2 = v.texcoord2;
           // o.texcoord3 = v.texcoord3;
           // o.color = v.color;
           // o.screenPos = ComputeScreenPos(o.pos);
           o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
           o.worldNormal = UnityObjectToWorldNormal(v.normal);
           o.worldTangent.xyz = UnityObjectToWorldDir(v.tangent.xyz);
           fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
           o.worldTangent.w = tangentSign;

           UNITY_TRANSFER_LIGHTING(o, v.texcoord1.xy); // pass shadow and, possibly, light cookie coordinates to pixel shader
           UNITY_TRANSFER_FOG(o,o.pos); // pass fog coordinates to pixel shader

           return o;
         }

         

         // fragment shader
         fixed4 Frag (VertexToPixel IN) : SV_Target
         {
           UNITY_SETUP_INSTANCE_ID(IN);
           // prepare and unpack data

           #ifdef FOG_COMBINED_WITH_TSPACE
             UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
           #elif defined (FOG_COMBINED_WITH_WORLD_POS)
             UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
           #else
             UNITY_EXTRACT_FOG(IN);
           #endif



           ShaderData d = CreateShaderData(IN);

           LightingInputs l = (LightingInputs)0;

           l.Albedo = half3(0.5, 0.5, 0.5);
           l.Normal = float3(0,0,1);
           l.Occlusion = 1;
           l.Alpha = 1;

           ChainSurfaceFunction(l, d);


           #ifndef USING_DIRECTIONAL_LIGHT
             fixed3 lightDir = normalize(UnityWorldSpaceLightDir(d.worldSpacePosition));
           #else
             fixed3 lightDir = _WorldSpaceLightPos0.xyz;
           #endif
           float3 worldViewDir = normalize(UnityWorldSpaceViewDir(d.worldSpacePosition));

           #if _USESPECULAR
              #ifdef UNITY_COMPILER_HLSL
              SurfaceOutputStandardSpecular o = (SurfaceOutputStandardSpecular)0;
              #else
              SurfaceOutputStandardSpecular o;
              #endif
              o.Specular = l.Specular;
           #else
              #ifdef UNITY_COMPILER_HLSL
              SurfaceOutputStandard o = (SurfaceOutputStandard)0;
              #else
              SurfaceOutputStandard o;
              #endif
              o.Metallic = l.Metallic;
           #endif

   

           o.Albedo = l.Albedo;
           o.Emission = l.Emission;
           o.Alpha = l.Alpha;
           o.Occlusion = l.Occlusion;
           o.Normal = normalize(TangentToWorldSpace(d, l.Normal));

           UNITY_LIGHT_ATTENUATION(atten, IN, d.worldSpacePosition)
           fixed4 c = 0;

           // Setup lighting environment
           UnityGI gi;
           UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
           gi.indirect.diffuse = 0;
           gi.indirect.specular = 0;
           gi.light.color = _LightColor0.rgb;
           gi.light.dir = lightDir;
           gi.light.color *= atten;


           #if _USESPECULAR
              c += LightingStandardSpecular (o, worldViewDir, gi);
           #else
              c += LightingStandard (o, worldViewDir, gi);
           #endif

           UNITY_APPLY_FOG(_unity_fogCoord, c); // apply fog

           // FinalColorForward(l, d, c);

           #if !_ALPHABLEND_ON
              UNITY_OPAQUE_ALPHA(c.a);
           #endif
           
           return c;
         }

         ENDCG

      }

      
	   // ---- deferred shading pass:
	   Pass
      {
		   Name "DEFERRED"
		   Tags { "LightMode" = "Deferred" }

         CGPROGRAM

            #pragma vertex Vert
   #pragma fragment Frag

         // compile directives
         #pragma target 3.0
         #pragma multi_compile_instancing
         #pragma exclude_renderers nomrt
         #pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
         #pragma multi_compile_prepassfinal
         #include "HLSLSupport.cginc"
         #define UNITY_INSTANCED_LOD_FADE
         #define UNITY_INSTANCED_SH
         #define UNITY_INSTANCED_LIGHTMAPSTS
         #include "UnityShaderVariables.cginc"
         #include "UnityShaderUtilities.cginc"
         #include "UnityCG.cginc"
         #include "Lighting.cginc"
         #include "UnityPBSLighting.cginc"

            #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _STANDARD 1

         

         // data across stages, stripped like the above.
         struct VertexToPixel
         {
            UNITY_POSITION(pos);       // must be named pos because Unity does stupid macro stuff
            float3 worldPos : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float4 worldTangent : TEXCOORD2;
             float4 texcoord0 : TEXCCOORD3;
            // float4 texcoord1 : TEXCCOORD4;
            // float4 texcoord2 : TEXCCOORD5;
            // float4 texcoord3 : TEXCCOORD6;
            // float4 screenPos : TEXCOORD7;
            // float4 color : COLOR;

            #ifndef DIRLIGHTMAP_OFF
              float3 viewDir : TEXCOORD8;
            #endif
            float4 lmap : TEXCOORD9;
            #ifndef LIGHTMAP_ON
              #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
                half3 sh : TEXCOORD10; // SH
              #endif
            #else
              #ifdef DIRLIGHTMAP_OFF
                float4 lmapFadePos : TEXCOORD11;
              #endif
            #endif

            // float4 extraData0 : TEXCOORD12;
            // float4 extraData1 : TEXCOORD13;
            // float4 extraData2 : TEXCOORD14;
            // float4 extraData3 : TEXCOORD15;

            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
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
            // unity_WorldToObject in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(unity_WorldToObject, p); };
               float3 TransformObjectToWorld(float3 p) { return mul(unity_ObjectToWorld, p); };
               float4x4 GetWorldToObjectMatrix() { return unity_WorldToObject; }
               float4x4 GetObjectToWorldMatrix() { return unity_ObjectToWorld; }
            #endif


            
         	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


         
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
            // d.localSpacePosition = mul(unity_WorldToObject, i.worldPos);
            // d.localSpaceNormal = mul(unity_WorldToObject, i.worldNormal);
            // d.localSpaceTangent = mul(unity_WorldToObject, i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         


         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
            UNITY_SETUP_INSTANCE_ID(v);
            VertexToPixel o;
            UNITY_INITIALIZE_OUTPUT(VertexToPixel,o);
            UNITY_TRANSFER_INSTANCE_ID(v,o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

            o.pos = UnityObjectToClipPos(v.vertex);
             o.texcoord0 = v.texcoord0;
            // o.texcoord1 = v.texcoord1;
            // o.texcoord2 = v.texcoord2;
            // o.texcoord3 = v.texcoord3;
            // o.color = v.color;
            // o.screenPos = ComputeScreenPos(o.pos);
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            o.worldTangent.xyz = UnityObjectToWorldDir(v.tangent.xyz);
            fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
            float3 worldBinormal = cross(o.worldNormal, o.worldTangent.xyz) * tangentSign;
            o.worldTangent.w = tangentSign;

            float3 viewDirForLight = UnityWorldSpaceViewDir(o.worldPos);
            #ifndef DIRLIGHTMAP_OFF
               o.viewDir.x = dot(viewDirForLight, o.worldTangent.xyz);
               o.viewDir.y = dot(viewDirForLight, worldBinormal);
               o.viewDir.z = dot(viewDirForLight, o.worldNormal);
            #endif
            #ifdef DYNAMICLIGHTMAP_ON
               o.lmap.zw = v.texcoord2.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
            #else
               o.lmap.zw = 0;
            #endif
            #ifdef LIGHTMAP_ON
               o.lmap.xy = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
               #ifdef DIRLIGHTMAP_OFF
                  o.lmapFadePos.xyz = (mul(unity_ObjectToWorld, v.vertex).xyz - unity_ShadowFadeCenterAndType.xyz) * unity_ShadowFadeCenterAndType.w;
                  o.lmapFadePos.w = (-UnityObjectToViewPos(v.vertex).z) * (1.0 - unity_ShadowFadeCenterAndType.w);
               #endif
            #else
               o.lmap.xy = 0;
               #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
                  o.sh = 0;
                  o.sh = ShadeSHPerVertex (o.worldNormal, o.sh);
               #endif
            #endif

            return o;
         }

         

         #ifdef LIGHTMAP_ON
         float4 unity_LightmapFade;
         #endif
         fixed4 unity_Ambient;

         

         // fragment shader
         void Frag (VertexToPixel IN,
             out half4 outGBuffer0 : SV_Target0,
             out half4 outGBuffer1 : SV_Target1,
             out half4 outGBuffer2 : SV_Target2,
             out half4 outEmission : SV_Target3
         #if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
             , out half4 outShadowMask : SV_Target4
         #endif
         )
         {
           UNITY_SETUP_INSTANCE_ID(IN);
           // prepare and unpack data

           #ifdef FOG_COMBINED_WITH_TSPACE
             UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
           #elif defined (FOG_COMBINED_WITH_WORLD_POS)
             UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
           #else
             UNITY_EXTRACT_FOG(IN);
           #endif


           ShaderData d = CreateShaderData(IN);

           LightingInputs l = (LightingInputs)0;

           l.Albedo = half3(0.5, 0.5, 0.5);
           l.Normal = float3(0,0,1);
           l.Occlusion = 1;
           l.Alpha = 1;

           ChainSurfaceFunction(l, d);



           #ifndef USING_DIRECTIONAL_LIGHT
             fixed3 lightDir = normalize(UnityWorldSpaceLightDir(d.worldSpacePosition));
           #else
             fixed3 lightDir = _WorldSpaceLightPos0.xyz;
           #endif
           float3 worldViewDir = normalize(UnityWorldSpaceViewDir(d.worldSpacePosition));

           #if _USESPECULAR
              #ifdef UNITY_COMPILER_HLSL
              SurfaceOutputStandardSpecular o = (SurfaceOutputStandardSpecular)0;
              #else
              SurfaceOutputStandardSpecular o;
              #endif
              o.Specular = l.Specular;
           #else
              #ifdef UNITY_COMPILER_HLSL
              SurfaceOutputStandard o = (SurfaceOutputStandard)0;
              #else
              SurfaceOutputStandard o;
              #endif
              o.Metallic = l.Metallic;
           #endif


           o.Albedo = l.Albedo;
           o.Normal = normalize(TangentToWorldSpace(d, l.Normal));
           o.Occlusion = l.Occlusion;
           o.Smoothness = l.Smoothness;
           o.Alpha = l.Alpha;


           half atten = 1;

           // Setup lighting environment
           UnityGI gi;
           UNITY_INITIALIZE_OUTPUT(UnityGI, gi);
           gi.indirect.diffuse = 0;
           gi.indirect.specular = 0;
           gi.light.color = 0;
           gi.light.dir = half3(0,1,0);
           // Call GI (lightmaps/SH/reflections) lighting function
           UnityGIInput giInput;
           UNITY_INITIALIZE_OUTPUT(UnityGIInput, giInput);
           giInput.light = gi.light;
           giInput.worldPos = d.worldSpacePosition;
           giInput.worldViewDir = worldViewDir;
           giInput.atten = atten;
           #if defined(LIGHTMAP_ON) || defined(DYNAMICLIGHTMAP_ON)
             giInput.lightmapUV = IN.lmap;
           #else
             giInput.lightmapUV = 0.0;
           #endif
           #if UNITY_SHOULD_SAMPLE_SH && !UNITY_SAMPLE_FULL_SH_PER_PIXEL
             giInput.ambient = IN.sh;
           #else
             giInput.ambient.rgb = 0.0;
           #endif
           giInput.probeHDR[0] = unity_SpecCube0_HDR;
           giInput.probeHDR[1] = unity_SpecCube1_HDR;
           #if defined(UNITY_SPECCUBE_BLENDING) || defined(UNITY_SPECCUBE_BOX_PROJECTION)
             giInput.boxMin[0] = unity_SpecCube0_BoxMin; // .w holds lerp value for blending
           #endif
           #ifdef UNITY_SPECCUBE_BOX_PROJECTION
             giInput.boxMax[0] = unity_SpecCube0_BoxMax;
             giInput.probePosition[0] = unity_SpecCube0_ProbePosition;
             giInput.boxMax[1] = unity_SpecCube1_BoxMax;
             giInput.boxMin[1] = unity_SpecCube1_BoxMin;
             giInput.probePosition[1] = unity_SpecCube1_ProbePosition;
           #endif

           #if _USESPECULAR
              LightingStandardSpecular_GI(o, giInput, gi);

              // call lighting function to output g-buffer
              outEmission = LightingStandardSpecular_Deferred (o, worldViewDir, gi, outGBuffer0, outGBuffer1, outGBuffer2);
              #if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
                outShadowMask = UnityGetRawBakedOcclusions (IN.lmap.xy, worldPos);
              #endif
              #ifndef UNITY_HDR_ON
              outEmission.rgb = exp2(-outEmission.rgb);
              #endif
           #else
              LightingStandard_GI(o, giInput, gi);

              // call lighting function to output g-buffer
              outEmission = LightingStandard_Deferred (o, worldViewDir, gi, outGBuffer0, outGBuffer1, outGBuffer2);
              #if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
                outShadowMask = UnityGetRawBakedOcclusions (IN.lmap.xy, d.worldSpacePosition);
              #endif
              #ifndef UNITY_HDR_ON
              outEmission.rgb = exp2(-outEmission.rgb);
              #endif
           #endif
            
           #if !defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT <= 4)
             half4 outShadowMask = 0;
           #endif

           // FinalGBufferStandard(o, d, GBuffer0, GBuffer1, GBuffer2, outEmission, outShadowMask);
         }




         ENDCG

      }


      
	   Pass {
		   Name "ShadowCaster"
		   Tags { "LightMode" = "ShadowCaster" }
		   ZWrite On ZTest LEqual

         CGPROGRAM

            #pragma vertex Vert
   #pragma fragment Frag
         // compile directives
         #pragma target 3.0
         #pragma multi_compile_instancing
         #pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
         #pragma multi_compile_shadowcaster
         #include "HLSLSupport.cginc"
         #define UNITY_INSTANCED_LOD_FADE
         #define UNITY_INSTANCED_SH
         #define UNITY_INSTANCED_LIGHTMAPSTS
         #include "UnityShaderVariables.cginc"
         #include "UnityShaderUtilities.cginc"

         #include "UnityCG.cginc"
         #include "Lighting.cginc"
         #include "UnityPBSLighting.cginc"

            #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _STANDARD 1



         // data across stages, stripped like the above.
         struct VertexToPixel
         {
            V2F_SHADOW_CASTER;
            float3 worldPos : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float4 worldTangent : TEXCOORD2;
             float4 texcoord0 : TEXCCOORD3;
            // float4 texcoord1 : TEXCCOORD4;
            // float4 texcoord2 : TEXCCOORD5;
            // float4 texcoord3 : TEXCCOORD6;
            // float4 screenPos : TEXCOORD7;
            // float4 color : COLOR;

            // float4 extraData0 : TEXCOORD8;
            // float4 extraData1 : TEXCOORD9;
            // float4 extraData2 : TEXCOORD10;
            // float4 extraData3 : TEXCOORD11;

            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
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
            // unity_WorldToObject in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(unity_WorldToObject, p); };
               float3 TransformObjectToWorld(float3 p) { return mul(unity_ObjectToWorld, p); };
               float4x4 GetWorldToObjectMatrix() { return unity_WorldToObject; }
               float4x4 GetObjectToWorldMatrix() { return unity_ObjectToWorld; }
            #endif


            
         	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


         
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
            // d.localSpacePosition = mul(unity_WorldToObject, i.worldPos);
            // d.localSpaceNormal = mul(unity_WorldToObject, i.worldNormal);
            // d.localSpaceTangent = mul(unity_WorldToObject, i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         


         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
            UNITY_SETUP_INSTANCE_ID(v);
            VertexToPixel o;
            UNITY_INITIALIZE_OUTPUT(VertexToPixel,o);
            UNITY_TRANSFER_INSTANCE_ID(v,o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

#if !_TESSELLATION_ON
           // ModifyVertex(v);
           // ModifyVertex_Ext1(v);
           // ModifyVertex_Ext2(v);
           // ModifyVertex_Ext3(v);
           // ModifyVertex_Ext4(v);
           // ModifyVertex_Ext5(v);
           // ModifyVertex_Ext6(v);
           // ModifyVertex_Ext7(v);
           // ModifyVertex_Ext8(v);
           // ModifyVertex_Ext9(v);
#endif

             o.texcoord0 = v.texcoord0;
            // o.texcoord1 = v.texcoord1;
            // o.texcoord2 = v.texcoord2;
            // o.texcoord3 = v.texcoord3;
            // o.color = v.color;
            // o.screenPos = ComputeScreenPos(o.pos);
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            o.worldTangent.xyz = UnityObjectToWorldDir(v.tangent.xyz);
            fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
            float3 worldBinormal = cross(o.worldNormal, o.worldTangent.xyz) * tangentSign;
            o.worldTangent.w = tangentSign;

            TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
            return o;
         }

         

         // fragment shader
         fixed4 Frag (VertexToPixel IN) : SV_Target
         {
           UNITY_SETUP_INSTANCE_ID(IN);
           // prepare and unpack data

           #ifdef FOG_COMBINED_WITH_TSPACE
             UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
           #elif defined (FOG_COMBINED_WITH_WORLD_POS)
             UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
           #else
             UNITY_EXTRACT_FOG(IN);
           #endif

           #ifndef USING_DIRECTIONAL_LIGHT
             fixed3 lightDir = normalize(UnityWorldSpaceLightDir(IN.worldPos));
           #else
             fixed3 lightDir = _WorldSpaceLightPos0.xyz;
           #endif

           ShaderData d = CreateShaderData(IN);

           LightingInputs l = (LightingInputs)0;

           l.Albedo = half3(0.5, 0.5, 0.5);
           l.Normal = float3(0,0,1);
           l.Occlusion = 1;
           l.Alpha = 1;

           ChainSurfaceFunction(l, d);

           SHADOW_CASTER_FRAGMENT(IN)
         }


         ENDCG

      }

      
	   // ---- meta information extraction pass:
	   Pass
      {
		   Name "Meta"
		   Tags { "LightMode" = "Meta" }
		   Cull Off

         CGPROGRAM

            #pragma vertex Vert
   #pragma fragment Frag

         // compile directives
         #pragma target 3.0
         #pragma multi_compile_instancing
         #pragma skip_variants FOG_LINEAR FOG_EXP FOG_EXP2
         #pragma shader_feature EDITOR_VISUALIZATION

         #include "HLSLSupport.cginc"
         #define UNITY_INSTANCED_LOD_FADE
         #define UNITY_INSTANCED_SH
         #define UNITY_INSTANCED_LIGHTMAPSTS
         #include "UnityShaderVariables.cginc"
         #include "UnityShaderUtilities.cginc"

         #include "UnityCG.cginc"
         #include "Lighting.cginc"
         #include "UnityPBSLighting.cginc"
         #include "UnityMetaPass.cginc"

            #pragma shader_feature_local _ _MASKMAP
   #pragma shader_feature_local _ _DETAIL
   #pragma shader_feature_local _ _EMISSION

   #define _STANDARD 1


         // data across stages, stripped like the above.
         struct VertexToPixel
         {
            UNITY_POSITION(pos);
            float3 worldPos : TEXCOORD0;
            float3 worldNormal : TEXCOORD1;
            float4 worldTangent : TEXCOORD2;
             float4 texcoord0 : TEXCCOORD3;
            // float4 texcoord1 : TEXCCOORD4;
            // float4 texcoord2 : TEXCCOORD5;
            // float4 texcoord3 : TEXCCOORD6;
            // float4 screenPos : TEXCOORD7;
            // float4 color : COLOR;
            #ifdef EDITOR_VISUALIZATION
              float2 vizUV : TEXCOORD8;
              float4 lightCoord : TEXCOORD9;
            #endif

            // float4 extraData0 : TEXCOORD10;
            // float4 extraData1 : TEXCOORD11;
            // float4 extraData2 : TEXCOORD12;
            // float4 extraData3 : TEXCOORD13;


            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
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
            // unity_WorldToObject in HDRP, since it already does macro-fu there

            #if _STANDARD
               float3 TransformWorldToObject(float3 p) { return mul(unity_WorldToObject, p); };
               float3 TransformObjectToWorld(float3 p) { return mul(unity_ObjectToWorld, p); };
               float4x4 GetWorldToObjectMatrix() { return unity_WorldToObject; }
               float4x4 GetObjectToWorldMatrix() { return unity_ObjectToWorld; }
            #endif


            
         	half4 _Tint;
   float4 _AlbedoMap_ST;
   float4 _DetailMap_ST;
   half _NormalStrength;
   half _EmissionStrength;
   half _DetailAlbedoStrength;
   half _DetailNormalStrength;
   half _DetailSmoothnessStrength;


         
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
            // d.localSpacePosition = mul(unity_WorldToObject, i.worldPos);
            // d.localSpaceNormal = mul(unity_WorldToObject, i.worldNormal);
            // d.localSpaceTangent = mul(unity_WorldToObject, i.worldTangent.xyz);

            // d.screenPos = i.screenPos;
            // d.screenUV = i.screenPos.xy / i.screenPos.w;

            // d.extraData0 = i.extraData0;
            // d.extraData1 = i.extraData1;
            // d.extraData2 = i.extraData2;
            // d.extraData3 = i.extraData3;

            return d;
         }
         

         // vertex shader
         VertexToPixel Vert (VertexData v)
         {
            UNITY_SETUP_INSTANCE_ID(v);
            VertexToPixel o;
            UNITY_INITIALIZE_OUTPUT(VertexToPixel,o);
            UNITY_TRANSFER_INSTANCE_ID(v,o);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

#if !_TESSELLATION_ON
           ChainModifyVertex(v, o);
#endif

            o.pos = UnityMetaVertexPosition(v.vertex, v.texcoord1.xy, v.texcoord2.xy, unity_LightmapST, unity_DynamicLightmapST);
            #ifdef EDITOR_VISUALIZATION
               o.vizUV = 0;
               o.lightCoord = 0;
               if (unity_VisualizationMode == EDITORVIZ_TEXTURE)
                  o.vizUV = UnityMetaVizUV(unity_EditorViz_UVIndex, v.texcoord.xy, v.texcoord1.xy, v.texcoord2.xy, unity_EditorViz_Texture_ST);
               else if (unity_VisualizationMode == EDITORVIZ_SHOWLIGHTMASK)
               {
                  o.vizUV = v.texcoord1.xy * unity_LightmapST.xy + unity_LightmapST.zw;
                  o.lightCoord = mul(unity_EditorViz_WorldToLight, mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1)));
               }
            #endif


             o.texcoord0 = v.texcoord0;
            // o.texcoord1 = v.texcoord1;
            // o.texcoord2 = v.texcoord2;
            // o.texcoord3 = v.texcoord3;
            // o.color = v.color;
            // o.screenPos = ComputeScreenPos(o.pos);
            o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
            o.worldNormal = UnityObjectToWorldNormal(v.normal);
            o.worldTangent.xyz = UnityObjectToWorldDir(v.tangent.xyz);
            fixed tangentSign = v.tangent.w * unity_WorldTransformParams.w;
            o.worldTangent.w = tangentSign;

            return o;
         }

         

         // fragment shader
         fixed4 Frag (VertexToPixel IN) : SV_Target
         {
            UNITY_SETUP_INSTANCE_ID(IN);

            #ifdef FOG_COMBINED_WITH_TSPACE
               UNITY_EXTRACT_FOG_FROM_TSPACE(IN);
            #elif defined (FOG_COMBINED_WITH_WORLD_POS)
               UNITY_EXTRACT_FOG_FROM_WORLD_POS(IN);
            #else
               UNITY_EXTRACT_FOG(IN);
            #endif

            ShaderData d = CreateShaderData(IN);

            LightingInputs l = (LightingInputs)0;

            l.Albedo = half3(0.5, 0.5, 0.5);
            l.Normal = float3(0,0,1);
            l.Occlusion = 1;
            l.Alpha = 1;

            
            ChainSurfaceFunction(l, d);

            UnityMetaInput metaIN;
            UNITY_INITIALIZE_OUTPUT(UnityMetaInput, metaIN);
            metaIN.Albedo = l.Albedo;
            metaIN.Emission = l.Emission;

            #if _USESPECULAR
               metaIN.SpecularColor = l.Specular;
            #endif

            #ifdef EDITOR_VISUALIZATION
              metaIN.VizUV = IN.vizUV;
              metaIN.LightCoord = IN.lightCoord;
            #endif
            return UnityMetaFragment(metaIN);
         }
         ENDCG

      }


   }
   
   
}
