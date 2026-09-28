Shader "Hidden/UMA/Hair Texture Map"
{
    // Keep the complete preview binding in Unity's material property sheet. Uniforms
    // alone can draw correctly after SetTexture/SetVector, but their values are lost
    // when the editor serializes/restores the material (leaving the whole map blue).
    Properties
    {
        [HideInInspector] _Map("Paint texels", 2D) = "black" {}
        [HideInInspector] _InvAtlasSize("Inverse paint atlas size", Float) = 1
        [HideInInspector] _Resolution("Texels per face edge", Float) = 16
        [HideInInspector] _Range("Map minimum and inverse range", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _Map;
            float _InvAtlasSize, _Resolution;
            float4 _Range;
            struct Input { float4 vertex : POSITION; float4 coords : TEXCOORD0; float2 field : TEXCOORD1; };
            struct Varying { float4 position : SV_POSITION; float4 coords : TEXCOORD0; float2 field : TEXCOORD1; };
            Varying vert(Input i) { Varying o; o.position = UnityObjectToClipPos(i.vertex); o.coords = i.coords; o.field = i.field; return o; }
            float Read(float2 p) { return tex2Dlod(_Map, float4((p + .5) * _InvAtlasSize, 0, 0)).r; }
            float4 frag(Varying i) : SV_Target
            {
                float value = i.field.x;
                if (i.field.y > .5)
                {
                    float2 uv = saturate(i.coords.xy); uv /= max(1, uv.x + uv.y);
                    float2 p = uv * _Resolution;
                    float2 cell = min(_Resolution - 1, floor(p));
                    if (cell.x + cell.y >= _Resolution) cell.x = max(0, _Resolution - cell.y - 1);
                    float2 f = p - cell, origin = cell + i.coords.zw;
                    if (f.x + f.y <= 1 || cell.x + cell.y == _Resolution - 1)
                        value = Read(origin) * max(0, 1 - f.x - f.y) + Read(origin + float2(1,0)) * f.x + Read(origin + float2(0,1)) * f.y;
                    else
                        value = Read(origin + 1) * (f.x + f.y - 1) + Read(origin + float2(0,1)) * (1 - f.x) + Read(origin + float2(1,0)) * (1 - f.y);
                }
                return lerp(float4(.02,.18,.85,.42), float4(1,.18,.015,.88), saturate((value - _Range.x) * _Range.y));
            }
            ENDHLSL
        }
    }
}
