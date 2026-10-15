Shader "Hidden/UMA/MakeupExampleComposite"
{
    Properties{_MainTex("Skin",2D)="white"{} _Makeup("Makeup",2D)="black"{} _Mask("Packed mask",Float)=0}
    SubShader{Cull Off ZWrite Off ZTest Always Pass{
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex,_Makeup;float _Mask;
        float4 frag(v2f_img i):SV_Target
        {float4 skin=tex2D(_MainTex,i.uv),paint=tex2D(_Makeup,i.uv);if(_Mask>.5)skin.a=lerp(skin.a,1-paint.r,paint.a);else skin.rgb=lerp(skin.rgb,paint.rgb,paint.a);return skin;}
        ENDCG
    }}
}
