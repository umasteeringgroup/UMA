Shader "Hidden/UMA/DecalRTStamp"
{
	Properties
	{
		// Unity common/standard properties to satisfy Material.color and Material.mainTexture accessors
		_Color("Color", Color) = (1,1,1,1)
		[NoScaleOffset]_MainTex("MainTex", 2D) = "white" {}

		// Expose your actual inputs so they serialize/bind cleanly on all backends
		[NoScaleOffset]_OverlayTex("Overlay", 2D) = "white" {}
		[NoScaleOffset]_MaskTex("Mask", 2D) = "white" {}
		_Fudge("Fudge", Float) = 0
		_ForceLinear("Force Linear", Float) = 0
		_UVRect("UVRect (xMin,yMin,xMax,yMax)", Vector) = (0,0,1,1)
		_UseUVRect("Use UVRect", Float) = 1
		_UseMask("Use Mask", Float) = 0
		_UseFixedLOD("Use Fixed LOD", Float) = 0
		_FixedLOD("Fixed LOD", Float) = 0
		_FlipY("Flip Y", Float) = 0
	}

	SubShader
	{
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		ZTest Always
		Cull Off
		ZWrite Off
		Blend SrcAlpha OneMinusSrcAlpha

		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#include "UnityCG.cginc"

			UNITY_DECLARE_TEX2D(_OverlayTex);
			UNITY_DECLARE_TEX2D(_MaskTex);

			float _Fudge;
			float _ForceLinear;
			float4 _UVRect;     // x=minx, y=miny, z=maxx, w=maxy
			float _UseUVRect;   // 0/1 toggle
			float _UseMask;     // 0/1 toggle
			float _UseFixedLOD; // 0/1 toggle
			float _FixedLOD;    // LOD level when _UseFixedLOD==1
			float _FlipY;       // 0/1 toggle to flip uv + clip

			struct appdata
			{
				float4 vertex : POSITION;   // clip-space quad verts provided by caller
				float2 uv     : TEXCOORD0;  // base UV0 (atlas space)
				float2 uv1    : TEXCOORD1;  // overlay planar UV
				fixed4 color  : COLOR;      // unused
			};

			struct v2f
			{
				float4 pos       : SV_POSITION;
				float2 overlayUV : TEXCOORD0;
				float2 baseUV    : TEXCOORD1;
			};

			v2f vert(appdata v)
			{
				v2f o;
				float2 pos = v.vertex.xy;
				float2 baseUV = v.uv;
				float2 overlayUV = v.uv1;

				// WebGL/OpenGL render targets can appear vertically inverted.
				// Apply a deterministic flip here so it can be controlled from C#.
				if (_FlipY > 0.5)
				{
					pos.y = -pos.y;
					baseUV.y = 1.0 - baseUV.y;
					overlayUV.y = 1.0 - overlayUV.y;
				}

				o.pos = float4(pos.xy, 0.0, 1.0);
				o.overlayUV = overlayUV;
				o.baseUV = baseUV;
				return o;
			}

			inline fixed4 SampleOverlay(float2 uv)
			{
				uv = saturate(uv);
				#if defined(SHADER_API_D3D11) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL)
				if (_UseFixedLOD > 0.5)
				{
					return UNITY_SAMPLE_TEX2D_LOD(_OverlayTex, uv, _FixedLOD);
				}
				#endif
				return UNITY_SAMPLE_TEX2D(_OverlayTex, uv);
			}

			inline fixed SampleMask(float2 uv)
			{
				uv = saturate(uv);
				#if defined(SHADER_API_D3D11) || defined(SHADER_API_GLCORE) || defined(SHADER_API_GLES3) || defined(SHADER_API_METAL)
				if (_UseFixedLOD > 0.5)
				{
					return UNITY_SAMPLE_TEX2D_LOD(_MaskTex, uv, _FixedLOD).a;
				}
				#endif
				return UNITY_SAMPLE_TEX2D(_MaskTex, uv).a;
			}

			fixed4 frag(v2f i) : SV_Target
			{
				// Optional UV clipping to the provided rect (atlas region)
				if (_UseUVRect > 0.5)
				{
					if (i.baseUV.x < _UVRect.x || i.baseUV.x > _UVRect.z || i.baseUV.y < _UVRect.y || i.baseUV.y > _UVRect.w)
						discard;
				}

				float2 uv = clamp(i.overlayUV, 0.0, 1.0);
				fixed4 c = SampleOverlay(uv);

				// Apply global coverage mask (from overlay.textureList[0] alpha or explicit mask)
				c.a *= (_UseMask > 0.5) ? SampleMask(uv) : 1.0f;

				// If needed, enable linearization:
				// if (_ForceLinear > 0.5) c.rgb = GammaToLinearSpace(c.rgb);

				return c;
			}
			ENDHLSL
		}
	}
	Fallback Off
}