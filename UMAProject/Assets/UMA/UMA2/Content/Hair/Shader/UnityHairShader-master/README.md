# UnityHairShader Version 9
This version uses blue noise and alpha to mask dithering to produce significantly better rendering when layering lots of hair strips on top of each other.
Be aware however, while this is temporally stable, it is not view angle stable -- it must be used with some kind of Temporal Anti Aliasing or Motion Blur.

The results are much, much better though.

# UnityHairShader Version 6
A hair shader originally built for the sine.space virtual world, designed and built for Unity3D's PBR rendering system.

![Preview image](https://raw.githubusercontent.com/AdamFrisby/UnityHairShader/master/Version6/Anisohair.jpg)

Leverages the Anisotropic Rendering released by Matt Dean availible from https://github.com/Kink3d/AnisotropicStandardShader - and incorporates features from Alan Zucconi's implementation of EA's Frostbite Fast Translucency algorithm (GDC 2011).

Quick Summary:
* Anisotropic highlights
* PBR-ish. Has option to tweak/remove unrealistic fresnel highlights.
* Optional variant with Normal Map support
* Double Sided (optional - delete the first pass if you dont need it)
* Works with Shader Model 3.0 (Normal Map variant requires SM 4.5, but will fallback to SM 3.0 variant)

Notes:
* Is going to be kinda heavy. Performance seems OK here, but could probably do with optimisation.
* We render the lower pass using alpha testing versus blending; this fixes a host of issues with ordering; it's a little smelly, but it works better over all.

Shows up as 'Sine Wave/Modern/Adam's Hair Shader 1.0' (Plain variant) and 'Sine Wave/Modern/Adam's Hair Shader 1.1' (Normal variant)
