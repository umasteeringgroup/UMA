Shader "Hidden/UMA/TexturePaint/Stencil"
{
 SubShader
 {
  Pass
  {
   Cull Off ZWrite Off ZTest Always BlendOp Max Blend One One
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _Stencil;
   float4x4 _StencilVP;
   float4 _StencilTransform;
   float _StencilRotation, _StencilAspect;
   int _StencilChannel, _StencilInvert;
   struct A { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
   struct V { float4 position:SV_POSITION; float4 camera:TEXCOORD0; };
   V vert(A v)
   {
    V o; o.camera=mul(_StencilVP,mul(unity_ObjectToWorld,v.vertex));
    float2 p=v.uv*2-1;
    #if UNITY_UV_STARTS_AT_TOP
    p.y=-p.y;
    #endif
    o.position=float4(p,0,1);return o;
   }
   float4 frag(V i):SV_Target
   {
    if(i.camera.w<=0)discard;
    float2 screen=i.camera.xy/i.camera.w*.5+.5;
    float2 p=screen-_StencilTransform.xy;
    // Rotation happens in screen pixels, before scaling into source UVs.
    // Correct viewport aspect so a displayed circle remains a circle while rotating.
    p.x *= _StencilAspect;
    float s,c;sincos(_StencilRotation,s,c);
    p=float2(c*p.x-s*p.y,s*p.x+c*p.y);
    p.x /= _StencilAspect;
    float2 uv=p/_StencilTransform.zw+.5;
    if(any(uv<0)||any(uv>1))discard;
    float4 sample=tex2D(_Stencil,uv);
    float value=dot(sample.rgb,float3(.2126,.7152,.0722));
    if(_StencilChannel==1)value=sample.r;
    else if(_StencilChannel==2)value=sample.g;
    else if(_StencilChannel==3)value=sample.b;
    else if(_StencilChannel==4)value=sample.a;
    if(_StencilInvert!=0)value=1-value;
    if(_StencilChannel!=4)value*=sample.a;
    return float4(value,value,value,1);
   }
   ENDCG
  }
 }
}
