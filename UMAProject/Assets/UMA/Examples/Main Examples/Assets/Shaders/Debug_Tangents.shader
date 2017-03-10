Shader "!Debug/Tangents" {
SubShader {
    Pass {
        Fog { Mode Off }
CGPROGRAM
#pragma vertex vert
#pragma fragment frag

// vertex input: position, tangent
struct appdata {
    float4 vertex : POSITION;
    float4 tangent : TANGENT;
};

struct v2f {
    float4    pos : SV_POSITION;
    fixed4    color : COLOR;
};
v2f vert (appdata v) {
    v2f o;
    o.pos = mul( UNITY_MATRIX_MVP, v.vertex );
    o.color = v.tangent * 0.5 + 0.5;
    return o;
}
fixed4 frag (v2f i) : COLOR0 { return i.color; }
ENDCG
    }
}
}