Shader "Hidden/UMA/TextureUtilitiesRawNormalPreview"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BumpMap ("Raw Normal Map", 2D) = "bump" {}
        _LightDir ("Light Direction", Vector) = (0.35, 0.45, 0.82, 0)
        _LightContrast ("Light Contrast", Range(0, 3)) = 1.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _BaseMap;
            sampler2D _BumpMap;
            float4 _BaseMap_ST;
            float4 _BumpMap_ST;
            float4 _LightDir;
            float _LightContrast;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 albedo = tex2D(_BaseMap, TRANSFORM_TEX(i.uv, _BaseMap));
                fixed3 rawNormal = tex2D(_BumpMap, TRANSFORM_TEX(i.uv, _BumpMap)).rgb;
                fixed3 normal = normalize((rawNormal * 2.0) - 1.0);
                fixed3 lightDir = normalize(_LightDir.xyz);
                fixed diffuse = saturate(dot(normal, lightDir));
                fixed rim = saturate(normal.z) * 0.18;
                fixed contrast = max(_LightContrast, 0.0);
                fixed shade = saturate(0.55 + ((diffuse - 0.5) * contrast) + (rim * contrast));
                return fixed4(albedo.rgb * shade, albedo.a);
            }
            ENDCG
        }
    }

    FallBack Off
}
