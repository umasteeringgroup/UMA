#ifndef UMA_PAINT_SURFACE_ANGLE_INCLUDED
#define UMA_PAINT_SURFACE_ANGLE_INCLUDED

// Shared by single stamps, batched stamps, blur, normal touchup, and ribbons.
float SurfaceAngleWeight(float facing, float cutoff, float fadeStart, bool backfaces)
{
    if (backfaces) facing = abs(facing);
    else if (facing <= 0.0) return 0.0;
    if (fadeStart - cutoff <= 1e-6) return facing >= cutoff ? 1.0 : 0.0;
    float t = saturate((facing - cutoff) / (fadeStart - cutoff));
    return t * t * (3.0 - 2.0 * t);
}

float ProjectedSurfaceAngleWeight(float2 uv, float4 facingPlane, float4 angle)
{
    if (facingPlane.w == 0.0) return 1.0;
    return SurfaceAngleWeight(dot(float3(uv, 1.0), facingPlane.xyz),
        angle.x, angle.y, angle.z != 0.0);
}

float ProjectedSurfaceDepthWeight(float2 uv, float4 depthPlane)
{
    if (depthPlane.w == 0.0) return 1.0;
    float depth = abs(dot(float3(uv, 1.0), depthPlane.xyz));
    float t = saturate((1.0 - depth) * 5.0);
    return t * t * (3.0 - 2.0 * t);
}

#endif
