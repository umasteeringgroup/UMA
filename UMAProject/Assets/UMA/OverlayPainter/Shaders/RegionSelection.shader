Shader "Hidden/UMA/TexturePaint/RegionSelection"
{
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
   if(i.depth<_ClipPlanes.x || i.depth>_ClipPlanes.y || i.camera.w<=0) discard;
   float2 uv=i.camera.xy/i.camera.w*0.5+0.5;
   if(any(uv<0)||any(uv>1))discard;
   float value=tex2D(_SelectionShape,uv).r;
   float tolerance=max(0.0001,abs(ddx(i.depth))+abs(ddy(i.depth)))*2;
   if(_Through==0 && i.depth>tex2D(_SelectionDepth,uv).r+tolerance)discard;
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
 }
}
