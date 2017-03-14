#ifndef LUX_CONFIG_INCLUDED
#define LUX_CONFIG_INCLUDED

	#if defined (LUX_STANDARDSHADER)
		// Define FogMode (Forward only)
		// #define FOG_LINEAR
		// #define FOG_EXP
		#define FOG_EXP2
	#endif

	// Enable Lazarov Environmental BRDF / Set it to 0 in case you want to use Unity's built in one
	#ifndef LUX_LAZAROV_ENVIRONMENTAL_BRDF
		#define LUX_LAZAROV_ENVIRONMENTAL_BRDF 1
	#endif

#endif // LUX_CONFIG_INCLUDED