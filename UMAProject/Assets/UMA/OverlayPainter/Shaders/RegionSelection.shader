Shader "Hidden/UMA/TexturePaint/RegionSelection"
{
 Properties
 {
  [HideInInspector] _MainTex ("Selection Gutter Source", 2D) = "black" {}
 }
 SubShader
 {
  CGINCLUDE
  #include "UnityCG.cginc"
  float4x4 _SelectionVP, _SelectionView;
  sampler2D _SelectionShape, _SelectionDepth;
  int _Through;
  float4 _ClipPlanes;
  struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
  struct V { float4 pos:SV_POSITION; float4 camera:TEXCOORD0; float depth:TEXCOORD1; };
  V Common(A v) { V o;float4 w=mul(unity_ObjectToWorld,v.vertex);o.camera=mul(_SelectionVP,w);o.depth=-mul(_SelectionView,w).z;o.pos=o.camera;return o; }
  V DepthVert(A v) { V o=Common(v);o.pos.z=0;
   #if UNITY_UV_STARTS_AT_TOP
   o.pos.y=-o.pos.y;
   #endif
   return o; }
  V UVVert(A v) {V o=Common(v);float2 p=v.uv*2-1;
   #if UNITY_UV_STARTS_AT_TOP
   p.y=-p.y;
   #endif
   o.pos=float4(p,0,1);return o;}
  float4 DepthFrag(V i):SV_Target {if(i.depth<_ClipPlanes.x || i.depth>_ClipPlanes.y)discard;return i.depth;}
  float4 SelectFrag(V i):SV_Target {
   // Alpha records UV ownership, independently of selection/visibility. Gutter padding
   // must never overwrite a real surface texel, including occluded or unselected ones.
   if(i.depth<_ClipPlanes.x || i.depth>_ClipPlanes.y || i.camera.w<=0) return float4(0,0,0,1);
   float2 uv=i.camera.xy/i.camera.w*0.5+0.5;
   if(any(uv<0)||any(uv>1))return float4(0,0,0,1);
   float value=tex2D(_SelectionShape,uv).r;
   float tolerance=max(0.0001,abs(ddx(i.depth))+abs(ddy(i.depth)))*2;
   if(_Through==0 && i.depth>tex2D(_SelectionDepth,uv).r+tolerance)return float4(0,0,0,1);
   return float4(value,value,value,1);
  }
  ENDCG
  Pass { Cull Off ZWrite Off ZTest Always BlendOp Min Blend One One
   CGPROGRAM
   #pragma vertex DepthVert
   #pragma fragment DepthFrag
   ENDCG
  }
  Pass { Cull Off ZWrite Off ZTest Always BlendOp Max Blend One One
   CGPROGRAM
   #pragma vertex UVVert
   #pragma fragment SelectFrag
   ENDCG
  }
  // Scene-only indication. Depth testing keeps rear selections behind the surface;
  // a small offset prevents flicker without changing the painted textures or depth.
  Pass { Cull Off ZWrite Off ZTest LEqual Offset -1, -1
   Blend SrcAlpha OneMinusSrcAlpha
   ColorMask RGB
   CGPROGRAM
   #pragma vertex PreviewVert
   #pragma fragment PreviewFrag
   struct PreviewV { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };
   PreviewV PreviewVert(A v) { PreviewV o;o.pos=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o; }
   float4 PreviewFrag(PreviewV i):SV_Target {
    float coverage=saturate(tex2D(_SelectionShape,i.uv).r);
    return float4(0,.9,1,.24*(1-coverage));
   }
   ENDCG
  }
  // Extend edge coverage into unused UV texels so bilinear filtering stays continuous
  // at island boundaries. Preserve all owned texels and the exact selection boundary.
  Pass { Cull Off ZWrite Off ZTest Always Blend One Zero
   CGPROGRAM
   #pragma target 4.5
   #pragma vertex vert_img
   #pragma fragment PadSelectionGutter
   sampler2D _MainTex;
   float4 _MainTex_TexelSize;
   float4 PadSelectionGutter(v2f_img i):SV_Target {
    float4 center=tex2D(_MainTex,i.uv);
    if(center.a>.5)return center;
    float nearest=1e20;
    float4 result=center;
    [unroll] for(int y=-2;y<=2;y++)
    [unroll] for(int x=-2;x<=2;x++) {
     float distance=x*x+y*y;
     float2 uv=i.uv+float2(x,y)*_MainTex_TexelSize.xy;
     if(distance<nearest && all(uv>=0) && all(uv<=1)) {
      float4 candidate=tex2D(_MainTex,uv);
      if(candidate.a>.5){nearest=distance;result=candidate;}
     }
    }
    return result;
   }
   ENDCG
  }
 }
}
