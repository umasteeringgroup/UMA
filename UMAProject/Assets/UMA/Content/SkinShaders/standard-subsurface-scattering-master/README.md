# Standard Subsurface Scattering

A simple subsurface scattering shader for Unity/VRchat that implements an approximation of a subsurface scattering effect, while keeping most of the options present in the Standard shader.

# [Can't find the Download link? Click here!](https://gitlab.com/s-ilent/standard-subsurface-scattering/-/archive/master/standard-subsurface-scattering-master.zip)

![Preview](https://gitlab.com/s-ilent/standard-subsurface-scattering/-/wikis/uploads/90b71889afe1414c2b8a02dea44fcef2/SSSS_DemoScene_8K_16.12.2020_21-39-53.jpg)

## Installation

Download the repository. Then place the Shader/ folder with the shader into your Assets/ directory.

## Usage

This shader features most (but not all) of the features available in the Unity Standard shader. You can drop in most kinds of texture map (if you set them in Standard, they'll be transferred over for you automatically.)
The main difference is in the addition of the thickness map. You can create this map in a few ways.
- Invert the normals of your model in Blender and bake ambient occlusion. This will create a true "thickness" map.
- If you use Substance Painter, you can set it up to generate a thickness map. Please see the Substance Painter documentation for more info.
- If you're lazy, like me, you can use the "Invert Thickness Map" option to use a preexisting texture. For example, you can get a nice effect on character models by using a metalness map as your thickness, with "Invert Thickness Map". This will make light pass through everything but metal. Note that for best appearance, you'll want to boost the Power variable.

If your thickness map doesn't define a colour for the scattering, you should also set "Tint Scattering by Albedo" so that the SSS effect isn't pure white. 

From there, you can tweak the values as you see fit. I recommend **leaving Intensity at 1**, as SSS is normally a subtle effect, and Intensity is a straight multiplier.

## Something I want is missing!

Request it and it will be fixed later.

## The default settings are weird!

Play around and see what works!

I recommend keeping intensity at 1. 

## License?

This work is licensed under MIT license.
