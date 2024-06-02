float fresnelDielectricCos(float cosi, float eta)
{
    //Blenders code for fresnel
    
    float c = abs(cosi);
    float g = eta * eta - 1.0 + c * c;
    float result;
    
    g = sqrt(g);
    float A = (g - c) / (g + c);
    float B = (c * (g + c) - 1.0) / (c * (g - c) + 1.0);
    result = 0.5 * A * A * (1.0 + B * B);
    
    return result;
}

float CustomFresnel(float3 viewDirection, float3 normal ,float ior)
{
    float eta = max(ior, 0.00001);
    float cosi = dot(viewDirection,normal);
    return fresnelDielectricCos(cosi, eta);
}

float fakeFresnel(float3 viewDirection,float3 normal, float power)
{
    float result = dot(viewDirection,normal);
    result = 1 - pow(result,power);
    return result;
}


float InvLerp(float a, float b, float v)
{
    return (v-a) / (b-a);
}

float Remap(float iMin, float iMax, float oMin , float oMax, float v)
{
    float t = InvLerp(iMin,iMax,v);
    return lerp(oMin,oMax,t);
}

float3 ShiftTangent(float3 normalWS, float4 tangentWS, float shift, float rotation01)
{
    float3 biTangent = cross(normalWS,tangentWS) * tangentWS.w;
    biTangent = normalize(biTangent);

    float scale = rotation01;
    scale *= 6.283;

    float3 avTB = (tangentWS * cos(scale)) + (biTangent * sin(scale));
    avTB = normalize(avTB);
    
    float3 shiftedT = avTB + shift * normalWS;

    return normalize(shiftedT);
}



//// === Hash Functions ====



float rand1(float seed)
{
    return frac(sin(seed) * 43758.5453123);
}

float rand2(float2 seedVec2){
    return frac(cos(dot(seedVec2, float2(12.9898, 78.233))) * 43758.5453123);
}

uint pcg_hash(uint seed)
{
    uint state = seed * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}


float randomHashFromCamera(float3 camPos, float3 viewDirec)
{
    
    float seed1 = camPos.x +camPos.y+camPos.z;
    float seed2 = viewDirec.x +viewDirec.y+viewDirec.z;
    
    float r = rand2(float2(seed1,seed2));
        
    return r;
}