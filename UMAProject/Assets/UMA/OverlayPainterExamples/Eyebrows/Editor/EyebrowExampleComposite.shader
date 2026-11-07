Shader "Hidden/UMA/EyebrowExampleComposite"
{
    Properties { _MainTex("Source",2D)="white"{} _Brow("Eyebrow",2D)="black"{} _Mask("Mask packing",Float)=0 _Normal("Merge relief",Float)=0 }
    SubShader { Cull Off ZWrite Off ZTest Always Pass {
        CGPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _MainTex,_Brow;float _Mask,_Normal;float4 _Brow_TexelSize;
        float Height(float2 uv){float4 b=tex2D(_Brow,uv);return lerp(.5,b.r,b.a);}
        float4 frag(v2f_img i):SV_Target
        {
            float4 source=tex2D(_MainTex,i.uv),brow=tex2D(_Brow,i.uv);
            if(_Normal>.5)
            {
                float2 gradient=float2(Height(i.uv+float2(_Brow_TexelSize.x,0))-Height(i.uv-float2(_Brow_TexelSize.x,0)),
                    Height(i.uv+float2(0,_Brow_TexelSize.y))-Height(i.uv-float2(0,_Brow_TexelSize.y)));
                float3 detail=normalize(float3(-gradient,1)),base=UnpackNormal(source);
                float3 t=base+float3(0,0,1),u=detail*float3(-1,-1,1);
                float3 n=normalize(t*dot(t,u)/max(t.z,1e-5)-u);
                return float4(1,n.y*.5+.5,1,n.x*.5+.5);
            }
            if(_Mask>.5)source.a=lerp(source.a,1-brow.r,brow.a);
            else source.rgb=lerp(source.rgb,brow.rgb,brow.a);
            return source;
        }
        ENDCG
    }}
}
