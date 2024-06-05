Shader "Hair/Hair Mark 9"
{
    Properties
    {
		_Color("Color", Color) = (1,1,1,1)
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.4
		_CutoffOffset ("Alpha cutoff shadows", Range(0,1)) = 0.4
        _MipScale ("Mip Level Alpha Scale", Range(0,1)) = 0.25
		_MipScaleOffset ("Shadow Scale", Range(0,3)) = 1.5
		_ShadowScaleOffset ("Shadow Scale Offset", float) = -0.001
		_Spec("Spec Color", Color) = (1,1,1,1)
		_Gloss("Gloss", float) = 1.0
		_AnisoOffset("Aniso Offset", float) = 1.0
		_Flatness("Flatness", float) = 0.5
		_Smoother("Lighting Smoother", float) = 4
		_RimPower("Rim Lighting", Range(0,2)) = 0
		_RimAngle("Rim Angle", Range(0,20)) = 1
		_AnisoTwo("Aniso", float) = 1.0
		_AnisoThree("Strandiness", float) = 1.0
		_AnisoFour("Strand Highlight Cutoff", float) = 1.0
		_AnisoDir("Aniso Dir", Vector) = (0.5,0.5,1.0,1.0)
		_BlueNoiseCrossfade("Blue Noise Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderQueue"="AlphaTest" "RenderType"="TransparentCutout" }
        Cull Off
        
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            //AlphaToMask On
            ZWrite On 
			ColorMask RGB
			
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
			#pragma multi_compile_fwdbase
            
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
			#include "AutoLight.cginc"
            #include "DitherFunctions.cginc"
			
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                half3 normal : NORMAL;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 worldNormal : NORMAL;
				half3 viewDir : TEXCOORD1;
				float4 screenPos : TEXCOORD2;
				LIGHTING_COORDS(3,4)
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            
            fixed _Cutoff;
            half _MipScale;
            
			fixed _Gloss;
			fixed4 _Spec;
			half _AnisoOffset;
			half _Flatness;
			fixed4 _Color;
			float4 _AnisoDir;
			float _AnisoTwo;
			float _AnisoThree;
			float _AnisoFour;
			float _Smoother;
			
            float CalcMipLevel(float2 texture_coord)
            {
                float2 dx = ddx(texture_coord);
                float2 dy = ddy(texture_coord);
                float delta_max_sqr = max(dot(dx, dx), dot(dy, dy));
                
                return max(0.0, 0.5 * log2(delta_max_sqr));
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
				o.viewDir = ObjSpaceViewDir(v.vertex);
				o.screenPos = ComputeScreenPos(UnityObjectToClipPos(v.vertex));
				
				TRANSFER_VERTEX_TO_FRAGMENT(o);
				
                return o;
            }
			
			float _RimAngle;
			float _RimPower;
			
			inline fixed4 CalculateLighting(v2f i, fixed facing : VFACE, fixed4 albedo) {
				float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
				float3 worldNormal = i.worldNormal * facing;
				fixed3 h = normalize(normalize(lightDir) + normalize(i.viewDir));
				
				float d = max(dot(lightDir, worldNormal), dot(lightDir, worldNormal * -1));
				
				float NdotL = saturate(d * _Smoother + _Flatness);
				
				fixed HdotA = dot(normalize(worldNormal + _AnisoDir.rgb), h);
				float aniso = max(0, sin(radians((HdotA + _AnisoOffset) * 180)));

				float specX = saturate(dot(worldNormal, h));
				float rim = saturate(pow(max(dot(i.worldNormal * facing, i.viewDir), dot(i.worldNormal * facing, i.viewDir)), _RimAngle)) * _RimPower;
				
				float3 spec = saturate((pow(lerp(specX, aniso, _AnisoTwo), _Gloss * 128) + rim) * _Spec);
				
				float maxAlbedo = max(max(albedo.r, albedo.g), albedo.b);
				

				spec = lerp(spec, spec*saturate(((maxAlbedo - _AnisoFour)*(1.0 / _AnisoFour))), _AnisoThree);

				
				
				fixed4 c;
				half3 sh9 = ShadeSH9(float4(worldNormal.rgb,1.0));
				
				c.rgb = ((albedo * _LightColor0.rgb * NdotL * 0.5) + (_LightColor0.rgb * spec * maxAlbedo)) 
					* (LIGHT_ATTENUATION(i) * 2) 
					+ (sh9 * albedo);
				c.a = albedo.a;
				//clip(albedo.Alpha - _Cutoff);
				return c;
			}
            
            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                // rescale alpha by mip level (if not using preserved coverage mip maps)
                col.a *= 1 + max(0, CalcMipLevel(i.uv * _MainTex_TexelSize.zw)) * _MipScale;
                // rescale alpha by partial derivative
                col.a = (col.a - _Cutoff) / max(fwidth(col.a), 0.0001) + 0.5;
                
                half3 worldNormal = normalize(i.worldNormal * facing);
                
				col = CalculateLighting(i, facing, col);
                
            
				//col.rg = (i.screenPos.xy / i.screenPos.w);
			
				ditherClip(i.screenPos.xy / i.screenPos.w, col.a, _Cutoff);
				
				
                return col;
            }
            ENDCG
        }
		
		Pass
        {
            Tags { "LightMode"="ForwardBase" }
            AlphaToMask Off
            ZWrite Off 
			ZTest Less
			Cull Off
			ColorMask RGB
			Blend SrcAlpha OneMinusSrcAlpha
			
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
			#pragma multi_compile_fwdbase
            
            #include "UnityCG.cginc"
            #include "Lighting.cginc"
			#include "AutoLight.cginc"
            #include "DitherFunctions.cginc"
			
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                half3 normal : NORMAL;
            };
            
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half3 worldNormal : NORMAL;
				half3 viewDir : TEXCOORD1;
				float4 screenPos : TEXCOORD2;
				LIGHTING_COORDS(3,4)
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainTex_TexelSize;
            
            fixed _Cutoff;
            half _MipScale;
            
			fixed _Gloss;
			fixed4 _Spec;
			half _AnisoOffset;
			half _Flatness;
			fixed4 _Color;
			float4 _AnisoDir;
			float _AnisoTwo;
			float _AnisoThree;
			float _AnisoFour;
			float _Smoother;
			
            float CalcMipLevel(float2 texture_coord)
            {
                float2 dx = ddx(texture_coord);
                float2 dy = ddy(texture_coord);
                float delta_max_sqr = max(dot(dx, dx), dot(dy, dy));
                
                return max(0.0, 0.5 * log2(delta_max_sqr));
            }
            
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
				o.viewDir = ObjSpaceViewDir(v.vertex);
				o.screenPos = ComputeScreenPos(UnityObjectToClipPos(v.vertex));
				
				TRANSFER_VERTEX_TO_FRAGMENT(o);
				
                return o;
            }
			
			float _RimAngle;
			float _RimPower;
			
			inline fixed4 CalculateLighting(v2f i, fixed facing : VFACE, fixed4 albedo) {
				float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
				float3 worldNormal = i.worldNormal * facing;
				fixed3 h = normalize(normalize(lightDir) + normalize(i.viewDir));
				
				float d = max(dot(lightDir, worldNormal), dot(lightDir, worldNormal * -1));
				
				float NdotL = saturate(d * _Smoother + _Flatness);
				
				fixed HdotA = dot(normalize(worldNormal + _AnisoDir.rgb), h);
				float aniso = max(0, sin(radians((HdotA + _AnisoOffset) * 180)));

				float specX = saturate(dot(worldNormal, h));
				float rim = saturate(pow(max(dot(i.worldNormal * facing, i.viewDir), dot(i.worldNormal * facing, i.viewDir)), _RimAngle)) * _RimPower;
				
				float3 spec = saturate((pow(lerp(specX, aniso, _AnisoTwo), _Gloss * 128) + rim) * _Spec);
				
				float maxAlbedo = max(max(albedo.r, albedo.g), albedo.b);
				

				spec = lerp(spec, spec*saturate(((maxAlbedo - _AnisoFour)*(1.0 / _AnisoFour))), _AnisoThree);

				
				
				fixed4 c;
				half3 sh9 = ShadeSH9(float4(worldNormal.rgb,1.0));
				
				c.rgb = ((albedo * _LightColor0.rgb * NdotL * 0.5) + (_LightColor0.rgb * spec * maxAlbedo)) 
					* (LIGHT_ATTENUATION(i) * 2) 
					+ (sh9 * albedo);
				c.a = albedo.a;
				//clip(albedo.Alpha - _Cutoff);
				return c;
			}
            
            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * _Color;
                // rescale alpha by mip level (if not using preserved coverage mip maps)
                //col.a *= 1 + max(0, CalcMipLevel(i.uv * _MainTex_TexelSize.zw)) * _MipScale;
                // rescale alpha by partial derivative
                //col.a = (col.a - _Cutoff) / max(fwidth(col.a), 0.0001) + 0.5;
                
                half3 worldNormal = normalize(i.worldNormal * facing);
                
				col = CalculateLighting(i, facing, col);
                
				//ditherClip(i.screenPos.xy / i.screenPos.w, 1.0 - col.a, _Cutoff);
				
				col.a = saturate(col.a * (1/_Cutoff));
				
                return col;
            }
            ENDCG
        }
/*
		Pass {
			Name "CASTER"
			Tags { "LIGHTMODE"="SHADOWCASTER" "QUEUE"="AlphaTest" "IGNOREPROJECTOR"="true" "SHADOWSUPPORT"="true" "RenderType"="TransparentCutout" }
            //AlphaToMask On
			Cull Off
            ZWrite On 
			
			CGPROGRAM

			#include "HLSLSupport.cginc"
			#include "UnityShaderVariables.cginc"
			#include "UnityShaderUtilities.cginc"

			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0
			#pragma multi_compile_shadowcaster
			#pragma multi_compile_instancing // allow instanced shadow pass for most of the shaders
			#include "UnityCG.cginc"

			struct v2f {
				V2F_SHADOW_CASTER;
				float2  uv : TEXCOORD1;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			uniform float4 _MainTex_ST;
			
			float _ShadowScaleOffset;

			v2f vert( appdata_base v )
			{
				v2f o;
				v.vertex.xyz += v.normal.xyz * _ShadowScaleOffset;
				UNITY_SETUP_INSTANCE_ID(v);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
				TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
				o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
				return o;
			}
			

			uniform sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			uniform fixed _CutoffOffset;
			uniform fixed4 _Color;
			half _MipScaleOffset;
			
            float CalcMipLevel(float2 texture_coord)
            {
                float2 dx = ddx(texture_coord);
                float2 dy = ddy(texture_coord);
                float delta_max_sqr = max(dot(dx, dx), dot(dy, dy));
                
                return max(0.0, 0.5 * log2(delta_max_sqr));
            }

			float4 frag( v2f i ) : SV_Target
			{
				fixed4 col = tex2D( _MainTex, i.uv );
				
				//col.a *= 1 + max(0, CalcMipLevel(i.uv * _MainTex_TexelSize.zw)) * _MipScaleOffset;
				//col.a -= _CutoffOffset + 0.1;
				//col.a = (col.a - _CutoffOffset) / max(fwidth(col.a), 0.0) + 0.5;
				
				clip( col.a - _CutoffOffset);

				SHADOW_CASTER_FRAGMENT(i)
			}
			ENDCG
		}
*/
        Pass
        {
            Name "CASTER"
			Tags { "LIGHTMODE"="SHADOWCASTER" "QUEUE"="AlphaTest" "IGNOREPROJECTOR"="true" "SHADOWSUPPORT"="true" "RenderType"="TransparentCutout" }

            CGPROGRAM
            #include "UnityCG.cginc"
            #include "DitherFunctions.cginc"
            #pragma vertex vert
            #pragma fragment frag

            float4 _Color;
            float4 _MainTex_ST;         // For the Main Tex UV transform
            sampler2D _MainTex;         // Texture used for the line
            float _MipScaleOffset;
			float _CutoffOffset;
			
            struct v2f
            {
                float4 pos      : POSITION;
                float2 uv       : TEXCOORD0;
                float4 screenPos     : TEXCOORD1;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
				o.screenPos = ComputeScreenPos(UnityObjectToClipPos(v.vertex));
				
                return o;
            }

            float4 frag(v2f i) : COLOR
            {
                float4 col = _Color * tex2D(_MainTex, i.uv);
                ditherClip(i.screenPos.xy / i.screenPos.w, col.a * _MipScaleOffset, _CutoffOffset);

                return float4(0,0,0,0); 
            }

            ENDCG
        }

    }
	
	FallBack "Transparent/Cutout/Specular"
}