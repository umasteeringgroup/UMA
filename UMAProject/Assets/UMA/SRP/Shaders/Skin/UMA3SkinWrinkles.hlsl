#ifndef UMA_REALISTIC_SKIN_WRINKLES
#define UMA_REALISTIC_SKIN_WRINKLES
// Expression wrinkles for "UMA/URP/Realistic Skin SSS Wrinkles". Included by UMA3SkinSSSInput.hlsl only when
// UMA_SKIN_WRINKLES is defined, so the other skin shaders compile exactly as before.
//
// Two textures, both linear data and both atlased by UMA like any other channel:
//
//   _WrinkleControlMap  R  zone id of layer A, stored as id * 17 / 255 (ids 0..15, 0 = no zone)
//                       G  mask of layer A
//                       B  zone id of layer B
//                       A  mask of layer B
//   _WrinkleMap         RG wrinkle normal X / Y in tangent space, 0.5 = flat. A delta on top of the skin normal map.
//                       B  crease depth, 0 = none, 1 = deepest (darkens ambient light in the crease)
//                       A  flush, where compressed skin turns redder
//
// Up to two zones can share a texel. Zone ids only change where both masks are zero (the baker spreads each id into
// the empty space around its zone), so bilinear filtering, mipmaps and atlas scaling never mix ids where it shows.
// The weight of each zone is a material float, _WrinkleZone01.._WrinkleZone15, in -1..1. Positive values deepen
// the wrinkles; negative values stretch the skin, which smooths the normal map and pores in that zone.

TEXTURE2D(_WrinkleMap); SAMPLER(sampler_WrinkleMap);
TEXTURE2D(_WrinkleControlMap); SAMPLER(sampler_WrinkleControlMap);

#define SKIN_WRINKLE_ZONE_LEVELS 15.0
// Wrinkle normals are combined as slopes. A slope this steep (about 76 degrees) is the most a texel can carry.
#define SKIN_WRINKLE_MAX_SLOPE 4.0

// Amount of compression (x) and stretch (y) at this pixel, set by SkinApplyWrinkles and read by the diffuse
// normal reads in the lighting pass of the same pixel.
static half2 skinWrinkleAmount = 0;

half SkinWrinkleZoneWeight(half encodedId)
{
    half id = round(encodedId * SKIN_WRINKLE_ZONE_LEVELS);
    half4 w0 = half4(0, _WrinkleZone01, _WrinkleZone02, _WrinkleZone03);
    half4 w1 = half4(_WrinkleZone04, _WrinkleZone05, _WrinkleZone06, _WrinkleZone07);
    half4 w2 = half4(_WrinkleZone08, _WrinkleZone09, _WrinkleZone10, _WrinkleZone11);
    half4 w3 = half4(_WrinkleZone12, _WrinkleZone13, _WrinkleZone14, _WrinkleZone15);
    half4 lane = half4(0, 1, 2, 3);
    // One-hot selection instead of an indexed array: no indexable temporaries.
    return dot(w0, half4(abs(id - lane) < 0.5))
        + dot(w1, half4(abs(id - 4 - lane) < 0.5))
        + dot(w2, half4(abs(id - 8 - lane) < 0.5))
        + dot(w3, half4(abs(id - 12 - lane) < 0.5));
}

// True when any zone has a weight, so a face at rest skips every wrinkle texture read.
bool SkinWrinklesActive()
{
    half4 a = abs(half4(_WrinkleZone01, _WrinkleZone02, _WrinkleZone03, _WrinkleZone04))
        + abs(half4(_WrinkleZone05, _WrinkleZone06, _WrinkleZone07, _WrinkleZone08))
        + abs(half4(_WrinkleZone09, _WrinkleZone10, _WrinkleZone11, _WrinkleZone12))
        + abs(half4(_WrinkleZone13, _WrinkleZone14, _WrinkleZone15, 0));
    return dot(a, 1) > 0.0001;
}

// x: compression 0..1, y: stretch 0..1.
half2 SkinWrinkleAmountAt(float2 uv)
{
    half4 control = SAMPLE_TEXTURE2D(_WrinkleControlMap, sampler_WrinkleControlMap, uv);
    half a = control.g * SkinWrinkleZoneWeight(control.r);
    half b = control.a * SkinWrinkleZoneWeight(control.b);
    return saturate(half2(max(a, 0) + max(b, 0), max(-a, 0) + max(-b, 0)));
}

half2 SkinNormalToSlope(half3 n)
{
    return clamp(n.xy / max(n.z, 0.05), -SKIN_WRINKLE_MAX_SLOPE, SKIN_WRINKLE_MAX_SLOPE);
}

half2 SkinWrinkleSlope(half4 wrinkle)
{
    half2 xy = wrinkle.rg * 2 - 1;
    return SkinNormalToSlope(half3(xy, sqrt(saturate(1 - dot(xy, xy)))));
}

// Skin normal with the wrinkles added: slopes add, so wrinkles stack on any face normal map (Jimenez et al. 2011).
half3 SkinCombineWrinkleNormal(half3 skinNormal, half2 wrinkleSlope, half2 amount)
{
    half2 slope = SkinNormalToSlope(skinNormal) * (1 - amount.y * _WrinkleStretchFlatten)
        + wrinkleSlope * (amount.x * _WrinkleNormalStrength);
    return normalize(half3(slope, 1));
}

void SkinApplyWrinkles(float2 uv, inout SkinData s)
{
    skinWrinkleAmount = 0;
    UNITY_BRANCH if (!SkinWrinklesActive()) return;
    half2 amount = SkinWrinkleAmountAt(uv);
    skinWrinkleAmount = amount;
    UNITY_BRANCH if (amount.x + amount.y <= 0.001) return;
    half4 wrinkle = SAMPLE_TEXTURE2D(_WrinkleMap, sampler_WrinkleMap, uv);
    s.macroNormalTS = SkinCombineWrinkleNormal(s.macroNormalTS, SkinWrinkleSlope(wrinkle), amount);
    // Stretched skin also shows its pores less.
    s.detailNormalXY *= 1 - amount.y * _WrinkleStretchFlatten;
    half3 detailNormal = half3(s.detailNormalXY, sqrt(saturate(1 - dot(s.detailNormalXY, s.detailNormalXY))));
    s.surface.normalTS = normalize(BlendNormalRNM(s.macroNormalTS, detailNormal));
    half compression = amount.x;
    s.surface.occlusion *= 1 - saturate(wrinkle.b * compression * _WrinkleCavityStrength);
    s.surface.albedo = lerp(s.surface.albedo, s.surface.albedo * _WrinkleFlushColor.rgb,
        saturate(wrinkle.a * compression * _WrinkleFlushStrength));
}

// A fold is real geometry a millimetre deep: light scattering under the skin softens its shading far less than it
// softens pores and fine texture. The diffuse read of the wrinkle map therefore uses only this part of the extra
// footprint the skin normal gets; the full footprint erased creases from the red channel and left red lines.
#define SKIN_WRINKLE_DIFFUSE_FOOTPRINT 0.35

// The diffuse-lighting normal of one color channel with the wrinkles added.
half3 SkinWrinkleDiffuseNormal(half3 skinNormal, float2 uv, float2 uvDx, float2 uvDy)
{
    half3 result = skinNormal;
    // Derivatives outside the branch: never narrower than one screen pixel.
    float2 pixelDx = ddx(uv), pixelDy = ddy(uv);
    float2 gradX = lerp(pixelDx, uvDx, SKIN_WRINKLE_DIFFUSE_FOOTPRINT), gradY = lerp(pixelDy, uvDy, SKIN_WRINKLE_DIFFUSE_FOOTPRINT);
    UNITY_BRANCH if (skinWrinkleAmount.x + skinWrinkleAmount.y > 0.001)
    {
        half4 wrinkle = SAMPLE_TEXTURE2D_GRAD(_WrinkleMap, sampler_trilinear_repeat, uv, gradX, gradY);
        result = SkinCombineWrinkleNormal(skinNormal, SkinWrinkleSlope(wrinkle), skinWrinkleAmount);
    }
    return result;
}

// Values of the "Wrinkle View" property.
#define SKIN_WRINKLE_DEBUG_ZONES 1
#define SKIN_WRINKLE_DEBUG_AMOUNT 2
#define SKIN_WRINKLE_DEBUG_NORMAL 3
#define SKIN_WRINKLE_DEBUG_CREASE 4

half3 SkinWrinkleZoneColor(half encodedId)
{
    half id = round(encodedId * SKIN_WRINKLE_ZONE_LEVELS);
    // Spread hues so neighbouring ids differ clearly.
    half hue = frac(id * 0.381966);
    half3 rgb = saturate(abs(frac(hue + half3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6 - 3) - 1);
    return id < 0.5 ? half3(0, 0, 0) : rgb;
}

half3 SkinWrinkleDebugColor(float2 uv)
{
    half4 control = SAMPLE_TEXTURE2D(_WrinkleControlMap, sampler_WrinkleControlMap, uv);
    half4 wrinkle = SAMPLE_TEXTURE2D(_WrinkleMap, sampler_WrinkleMap, uv);
    half3 color = 0;
    if (_WrinkleDebug < 1.5)
        color = SkinWrinkleZoneColor(control.r) * control.g + SkinWrinkleZoneColor(control.b) * control.a * 0.6;
    else if (_WrinkleDebug < 2.5)
        color = half3(SkinWrinkleAmountAt(uv).x, 0, SkinWrinkleAmountAt(uv).y);
    else if (_WrinkleDebug < 3.5)
        color = half3(wrinkle.rg, 1);
    else
        color = wrinkle.bbb;
    #ifdef UNITY_COLORSPACE_GAMMA
    return saturate(color);
    #else
    return SRGBToLinear(saturate(color));
    #endif
}

// Replaces the lit color with a wrinkle diagnostic when "Wrinkle View" is not Lit.
half3 SkinWrinkleDebug(float2 uv, half3 litColor)
{
    return _WrinkleDebug < 0.5 ? litColor : SkinWrinkleDebugColor(uv);
}

#endif
