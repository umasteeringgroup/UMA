Shader "Hidden/UMA/TexturePaint/Projection"
{
    Properties { [HideInInspector] _MainTex ("Padding source", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One Zero
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            #include "Garment.hlsl"
            struct PatchTriangle { float4 a, b, c, uv; };
            StructuredBuffer<PatchTriangle> _Patch;
            int _CyclicU;
            int _PatchCount, _Fade, _FrontOnly, _FirstSurface, _NormalChannel, _DepthFromSurface;
            float3 _BoundsMin, _BoundsMax;
            float _HalfDepth, _DepthMin, _DepthMax, _DepthFade, _EdgeWidth, _AngleStart, _AngleEnd, _VisibilityTolerance;
            float2 _Flip;
            sampler2D _Source, _Coverage, _Visibility, _Outline, _Curve;
            struct Attributes { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 position : SV_POSITION; float3 world : TEXCOORD0; float3 normal : TEXCOORD1; float2 uv : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings o; float2 clip = input.uv * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                o.position = float4(clip, 0, 1); o.world = mul(unity_ObjectToWorld, input.vertex).xyz;
                o.normal = UnityObjectToWorldNormal(input.normal); o.uv = input.uv; return o;
            }
            float Curve(float value) { return tex2Dlod(_Curve, float4(saturate(value), 0.5, 0, 0)).r; }
            float3 AlignNormal(float3 value, float3 from, float3 to)
            {
                float3 axis = cross(from, to); float cosine = dot(from, to);
                return value * cosine + cross(axis, value) + axis * dot(axis, value) / max(1 + cosine, 1e-5);
            }
            float4 Frag(Varyings input) : SV_Target
            {
                // Derivatives must be evaluated before divergent projection/visibility branches.
                float3 dx = ddx(input.world), dy = ddy(input.world);
                float2 ux = ddx(input.uv), uy = ddy(input.uv);
                // Unaffected faces can share UVs with the projected surface. Discard them:
                // writing transparent black would erase an earlier valid projection fragment.
                if (any(input.world < _BoundsMin) || any(input.world > _BoundsMax)) discard;
                float best = 1e20, signedDepth = 0, frontDepth = _DepthMax; float2 projectedUV = 0;
                float3 patchNormal = 0, patchU = 0, patchV = 0;
                [loop] for (int i = 0; i < _PatchCount; i++)
                {
                    PatchTriangle p = _Patch[i]; float3 e1 = p.b.xyz - p.a.xyz, e2 = p.c.xyz - p.a.xyz;
                    float3 n = cross(e1, e2); float area = length(n); if (area < 1e-12) continue; n /= area;
                    float3 v = input.world - p.a.xyz; float d = dot(v, n); if (d < _DepthMin || d > _DepthMax + p.a.w || abs(d) >= best) continue;
                    float aa = dot(e1, e1), ab = dot(e1, e2), bb = dot(e2, e2);
                    float det = aa * bb - ab * ab; if (abs(det) < 1e-20) continue;
                    float b = (bb * dot(v, e1) - ab * dot(v, e2)) / det;
                    float c = (aa * dot(v, e2) - ab * dot(v, e1)) / det;
                    if (b < -0.00001 || c < -0.00001 || b + c > 1.00001) continue;
                    best = abs(d); signedDepth = d; frontDepth = _DepthMax + p.a.w; patchNormal = n;
                    float2 localUV = p.uv.w < 0.5 ? float2(b + c, c) : float2(b, b + c);
                    projectedUV = p.uv.xy + localUV * p.uv.z;
                    patchU = normalize(p.uv.w < 0.5 ? e1 : e1 - e2);
                    patchV = normalize(cross(n, patchU));
                }
                if (best > 1e19) discard;
                float3 normal = normalize(input.normal);
                float facing = dot(normal, patchNormal);
                if (_FrontOnly != 0 && facing <= 0) discard;
                float alignment = _FrontOnly != 0 ? facing : abs(facing);
                float alpha = _AngleStart > _AngleEnd + 1e-6 ?
                    saturate((alignment - _AngleEnd) / (_AngleStart - _AngleEnd)) : step(_AngleEnd, alignment);
                if (_DepthFade > 0)
                {
                    float distanceToBoundary = min(signedDepth - _DepthMin, frontDepth - signedDepth);
                    float fadeRange = _DepthFromSurface != 0 ? -_DepthMin : _HalfDepth;
                    alpha *= Curve(distanceToBoundary / max(fadeRange * _DepthFade, 1e-6));
                }
                if (_FirstSurface != 0)
                {
                    float first = tex2Dlod(_Visibility, float4(projectedUV, 0, 0)).r;
                    float slope = abs(dot(dx, patchNormal)) + abs(dot(dy, patchNormal));
                    if (abs(first - signedDepth) > _VisibilityTolerance + slope * 2) discard;
                }
                float2 uv = lerp(projectedUV, 1 - projectedUV, _Flip);
                float distance = 1;
                if (_Fade == 1) distance = (1 - length(projectedUV * 2 - 1)) * 0.5;
                if (_Fade == 2) distance = _CyclicU != 0 ? min(projectedUV.y, 1 - projectedUV.y) :
                    min(min(projectedUV.x, 1 - projectedUV.x), min(projectedUV.y, 1 - projectedUV.y));
                if (_Fade == 3) distance = tex2Dlod(_Outline, float4(uv, 0, 0)).r;
                if (_Fade != 0) alpha *= _EdgeWidth > 0 ? Curve(distance / _EdgeWidth) : step(0, distance);
                float4 source = tex2Dlod(_Source, float4(uv, 0, 0));
                if (_GarmentEnabled != 0)
                    source=ShadeGarment(uv,input.uv,max(length(float2(ddx(uv.x),ddy(uv.x))),.0001),
                        false,input.world,input.normal,float4(0,0,0,1));
                else if (_NormalChannel != 0)
                {
                    float3 encoded = source.xyz * 2 - 1;
                    encoded.xy *= 1 - 2 * _Flip;
                    float3 worldNormal = patchU * encoded.x + patchV * encoded.y + patchNormal * encoded.z;
                    if (facing < 0) { patchNormal = -patchNormal; worldNormal = -worldNormal; }
                    worldNormal = normalize(AlignNormal(worldNormal, patchNormal, normal));
                    float determinant = ux.x * uy.y - ux.y * uy.x;
                    float3 tangent = abs(determinant) > 1e-10 ? (dx * uy.y - dy * ux.y) / determinant : patchU;
                    tangent = normalize(tangent - normal * dot(tangent, normal));
                    float3 bitangent = abs(determinant) > 1e-10 ? (-dx * uy.x + dy * ux.x) / determinant : patchV;
                    float signV = dot(cross(normal, tangent), bitangent) < 0 ? -1 : 1;
                    bitangent = cross(normal, tangent) * signV;
                    source.rgb = float3(dot(worldNormal, tangent), dot(worldNormal, bitangent), dot(worldNormal, normal)) * 0.5 + 0.5;
                }
                source.a = (_GarmentEnabled != 0 ? source.a : tex2Dlod(_Coverage, float4(uv, 0, 0)).a) * alpha;
                if (source.a <= 0) discard;
                return source;
            }
            ENDCG
        }
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One Zero
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Pad
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_TexelSize;
            float4 Pad(v2f_img input) : SV_Target
            {
                float4 center = tex2D(_MainTex, input.uv); if (center.a > 1e-5) return center;
                float4 best = center;
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                { float4 p = tex2D(_MainTex, input.uv + float2(x, y) * _MainTex_TexelSize.xy); if (p.a > best.a) best = p; }
                return best;
            }
            ENDCG
        }
        // First-surface depth in projection UV space, independent of the model's texture UVs.
        Pass
        {
            Cull Off ZWrite Off ZTest Always Blend One One BlendOp Max
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex VisibilityVert
            #pragma fragment VisibilityFrag
            #include "UnityCG.cginc"
            struct PatchTriangle { float4 a, b, c, uv; };
            StructuredBuffer<PatchTriangle> _Patch;
            int _VisibilityPatch, _FrontOnly;
            float _DepthMin, _DepthMax, _VisibilitySize;
            struct VisibilityVaryings
            {
                float4 position : SV_POSITION;
                float3 barycentricDepth : TEXCOORD0;
            };
            VisibilityVaryings VisibilityVert(float4 vertex : POSITION)
            {
                PatchTriangle p = _Patch[_VisibilityPatch];
                float3 e1 = p.b.xyz - p.a.xyz, e2 = p.c.xyz - p.a.xyz;
                float3 v = mul(unity_ObjectToWorld, vertex).xyz - p.a.xyz;
                float aa = dot(e1, e1), ab = dot(e1, e2), bb = dot(e2, e2);
                float determinant = max(aa * bb - ab * ab, 1e-30);
                float b = (bb * dot(v, e1) - ab * dot(v, e2)) / determinant;
                float c = (aa * dot(v, e2) - ab * dot(v, e1)) / determinant;
                float2 uv = p.uv.xy + (p.uv.w < .5 ? float2(b + c, c) : float2(b, b + c)) * p.uv.z;
                float2 clip = uv * 2 - 1;
                #if UNITY_UV_STARTS_AT_TOP
                    clip.y = -clip.y;
                #endif
                VisibilityVaryings o;
                o.position = float4(clip, 0, 1);
                o.barycentricDepth = float3(b, c, dot(v, normalize(cross(e1, e2))));
                return o;
            }
            float VisibilityFrag(VisibilityVaryings input, float facing : VFACE) : SV_Target
            {
                // The projector maps its outward cross-product normal toward positive Z.
                // In this UV rasterization frame those triangles have Unity's back winding.
                if (_FrontOnly != 0 && facing > 0) discard;
                PatchTriangle p = _Patch[_VisibilityPatch];
                // Choose exactly one patch at a texel center, as the CPU reference does.
                // Interpolated barycentrics can disagree at shared diagonals, leaving holes
                // or choosing the neighbouring curved patch's different depth frame.
                float2 local = (input.position.xy / _VisibilitySize - p.uv.xy) / p.uv.z;
                if (any(local < 0) || any(local >= 1)) discard;
                if (p.uv.w < .5 ? local.x < local.y : local.x >= local.y) discard;
                float3 value = input.barycentricDepth;
                if (value.z < _DepthMin || value.z > _DepthMax + p.a.w) discard;
                return value.z;
            }
            ENDCG
        }
    }
}
