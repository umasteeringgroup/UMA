Shader "Hidden/UMA/TexturePaint/SourceExtract"
{
    Properties
    {
        [HideInInspector] _MainTex ("Source", 2D) = "white" {}
        [HideInInspector] _ScaleOffset ("Scale Offset", Vector) = (1,1,0,0)
        [HideInInspector] _SourceIsNormalMap ("Unity Normal Map", Int) = 0
        [HideInInspector] _SourceIsSRGB ("Source Is sRGB", Int) = 0
        [HideInInspector] _InvertGreen ("Invert Green", Int) = 0
        [HideInInspector] _InvertChannels ("Invert Channels", Int) = 0
        [HideInInspector] _Grayscale ("Grayscale", Int) = 0
        [HideInInspector] _SourceComponent ("Source Component", Int) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        // Both passes use source-pixel offsets. Associated color avoids dark fringes from
        // arbitrary RGB in transparent texels; alpha itself receives the same Gaussian.
        float4 BlurSource(sampler2D source, float2 uv, float2 step, int radius, bool associate)
        {
            float sigma = max(0.5, radius * 0.5);
            float4 sum = 0;
            float total = 0;
            for (int tap = -radius; tap <= radius; tap++)
            {
                float weight = exp(-0.5 * tap * tap / (sigma * sigma));
                float4 value = tex2D(source, uv + step * tap);
                if (associate) value.rgb *= value.a;
                sum += value * weight;
                total += weight;
            }
            return sum / total;
        }
        ENDCG

        // Ordinary sprite extraction.
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _ScaleOffset;
            int _InvertChannels;
            int _Grayscale;

            float4 frag(v2f_img input) : SV_Target
            {
                float4 source = tex2D(_MainTex, input.uv * _ScaleOffset.xy + _ScaleOffset.zw);
                if (_InvertChannels != 0) source.rgb = 1.0 - source.rgb;
                if (_Grayscale != 0)
                {
                    float value = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
                    source.rgb = value.xxx;
                }
                return source;
            }
            ENDCG
        }

        // Extract one scalar component from a packed material texture and replicate it for the
        // ordinary grayscale painting path.
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _ScaleOffset;
            int _SourceComponent;
            int _InvertChannels;

            float4 frag(v2f_img input) : SV_Target
            {
                float4 source = tex2D(_MainTex, input.uv * _ScaleOffset.xy + _ScaleOffset.zw);
                float value = _SourceComponent == 0 ? source.r :
                    _SourceComponent == 1 ? source.g :
                    _SourceComponent == 2 ? source.b : source.a;
                if (_InvertChannels != 0) value = 1.0 - value;
                return float4(value, value, value, 1.0);
            }
            ENDCG
        }

        // Convert either raw RGB normal data or a Unity Normal Map asset to the painter's
        // canonical linear OpenGL-style (X,Y,Z) * 0.5 + 0.5 representation.
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _ScaleOffset;
            int _SourceIsNormalMap;
            int _SourceIsSRGB;
            int _InvertGreen;
            int _InvertChannels;

            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv = input.uv * _ScaleOffset.xy + _ScaleOffset.zw;
                float4 source = tex2D(_MainTex, uv);
                float3 normal;
                float alpha = source.a;
                if (_SourceIsNormalMap != 0)
                {
                    normal = UnpackNormal(source);
                    // Normal-map compression may store X in alpha; it is not paint coverage.
                    alpha = 1.0;
                }
                else
                {
                    float3 encoded = source.rgb;
                    #if !defined(UNITY_COLORSPACE_GAMMA)
                        // Sprite assets cannot use Unity's Normal Map importer. If their sheet is
                        // imported as sRGB, recover the authored encoded RGB after hardware decode.
                        if (_SourceIsSRGB != 0) encoded = LinearToGammaSpace(encoded);
                    #endif
                    normal = encoded * 2.0 - 1.0;
                    float lengthSquared = dot(normal, normal);
                    normal = lengthSquared > 1e-8 ? normal * rsqrt(lengthSquared) : float3(0, 0, 1);
                }
                if (_InvertGreen != 0) normal.y = -normal.y;
                float3 encodedNormal = normal * 0.5 + 0.5;
                if (_InvertChannels != 0) encodedNormal = 1.0 - encodedNormal;
                return float4(encodedNormal, alpha);
            }
            ENDCG
        }
        // Horizontal: straight source to associated linear RGBA.
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            int _BlurRadius;
            float4 frag(v2f_img input) : SV_Target
            {
                return BlurSource(_MainTex, input.uv, float2(_MainTex_TexelSize.x, 0), _BlurRadius, true);
            }
            ENDCG
        }
        // Vertical: recover straight RGB and normalize averaged normal vectors.
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            int _BlurRadius, _BlurNormal;
            float4 frag(v2f_img input) : SV_Target
            {
                float4 value = BlurSource(_MainTex, input.uv, float2(0, _MainTex_TexelSize.y), _BlurRadius, false);
                value.rgb = value.a > 1e-6 ? value.rgb / value.a : 0;
                if (_BlurNormal != 0)
                {
                    float3 n = value.rgb * 2 - 1;
                    n = value.a > 1e-6 && dot(n, n) > 1e-8 ? normalize(n) : float3(0, 0, 1);
                    value.rgb = n * 0.5 + 0.5;
                }
                return value;
            }
            ENDCG
        }
    }
    Fallback Off
}
