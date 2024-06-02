float3 _WorldSpaceLightPos0; 

void GetMainLight_float (out float3 lightDir) {
    #if (SHADERPASS == SHADERPASS_FORWARD)
        lightDir = _WorldSpaceLightPos0;
    #else
        lightDir = float3(0,1,0);
    #endif
}