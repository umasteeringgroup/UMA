- - - - - - - - - - - - - - - -
LUX SKIN SHADER

The Lux skin shader is based on the work of Eric Penner (http://www.ericpenner.net/) and his pre-integrated skin shader.
The base of the code Lux uses was ported to unity by farfarer (http://www.farfarer.com/blog/).
Also the BRDF Lookup texture was provided by farfarer.
I also added translucency as developed by Dice: http://dice.se/wp-content/uploads/Colin_BarreBrisebois_Programming_ApproximatingTranslucency.pdf


- - - - - - - - - - - - - - - -
IMPORTANT INFO
As the skin shader uses its own lighting function it is forward only – unless you own Lux Pro :-)
It is based upon preintegrated skin lighting which means that it uses a special BRDF look up texture to calculate diffuse lighting in order to simulate small scale subsurface light scattering.
The shader also supports large scale subsurface light transportation aka translucency or transmission which is driven by texture input.

Please note: In order to reduce the amount of shading artifacts only lights that cast shadows contribute to large scale sub surface light transportation. 


- - - - - - - - - - - - - - - -
THE SHADER INPUTS

- Albedo (RGB) Smoothness (A): Diffuse or albedo texture in rgb, alpha stores smoothness.
- Translucency (G) AO (A):
  - Translucency determines the amount of deep subsurface scattering. It is also known as depth or thickness map: So apply bright values for areas like the ears, nose and fingers which would usually have more subsurface scattering.
  - Ambient Occlusion ist stored in the alpha channel.
- Normal Map: A regular normal map. Make sure that you only bake rather small details into it.

- Enable Micro Bumps: In case you have cinematics and very close shoots you might enable micro bumps which will add a high frequent bump texture for small pores added on top of the regular bum map.
- Micro Bump Map: A normal map which contains the micro bumps for pores. Please have a look at the provided demo content.
- Micro Bump Tiling: Tiling of the micro bump according to the base textures.
- Micro Bump Scale: Lets you weaken or strengthen the micro bumps.

- Diffuse Normal Map Blur Bias: Determines which mip level of the applied normal map is sampled and used when calculating curvature and diffuse lighting. So higher values will give you softer direct and ambient diffuse lighting. Best to keep this between 2 and 3.
- Blur Strength: As sampling lower mip levels will blur the normal in rather large steps you may fine adjust the final diffuse normal by lerping between regular normal and the one sampled using the manually adjusted Diffuse Normal Map Blur Bias.

- Curvature Influence: If set to 0.0 the shader will not calculate any curvature at all but simply use the Translucency when doing the lookup in the BRDF texture. If set to 1.0 only the calculated curvature will be used.
- Curvature Scale: Lets you control the amount of small scale subsurface scattering. You should use rather small values here: 0.02 – 0.002. Please check your settings under varying lighting conditions!
- Bias: Lets you raise the small scale sub surface light scattering by adding a constant.


As you might have noticed: Skin does not let you define a dedicated specular color.
Instead the shader sets it to the original spec value of skin which is 0.028.


- - - - - - - - - - - - - - - -
GLOBAL INPUTS

As Lux Pro supports deferred skin lighting not all parameters can be set up per material bust must be declared globally using the "Lux_Setup" script.

- BRDF Texture: Look up Texture for diffuse skin lighting.
- Subsurface Color: Color of the large scale subsurface light scattering
- Power: Power value for direct translucency or large scale subsurface scattering, view dependent: "While we usually want view-independent effect, this power is very useful because it will break the continuity in a view-dependent way, which will break uniformity between back and front lighting/translucency, and make your results much more organic." Quoted from dice's paper
- Distortion: Shifts the surface normals, view dependent, fresnel like: "This factor will simulate subsurface light transport distortion, improving the results and making everything even more organic, and almost Fresnel-like at times" Quoted from dice's paper
- Scale: Scales deep subsurface light transportation or translucency.

- Enable Skin Lighting Fade: When checked skin lighting lerps towards standard lighting based on the distance to the camera.
This lets you swap out the shader at lower LODs so you might use the simple "Lux/Human/Skin Standard Lighting" shader or even create a unified material for your characters using the (Lux) standard shader.
- Skin Lighting Distance: Distance at which the shader shall not do any skin related lighting.
- Skin Lighting Fade Range: Range starting from "Skin Lighting Distance" in which the shader performs the blending.
