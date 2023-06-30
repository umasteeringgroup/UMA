# ShaderPackager
   Shader Packager is system for combining multiple shaders, written for specific render pipelines or unity versions, and making them act as a single shader in the project. The goal is to help fix on of the #SRPLife problems, which is that users install your shaders and see nothing but pink shaders, because the versions that shipped with your asset are for the wrong pipeline. The usual workaround for this is to provide various zip files and have the user unpack the right versions for the SRP and Unity version they are using- however, this is a horrible experience for new users. 

   So what this system does is take a list of shaders, with requirements about SRP type and Unity version, and pack them all into a single file. At import time, it detects which SRP is installed, and imports the correct shader from the package into the project. To the user, it looks like any other shader, but just works on the first install regardless of which SRP they are installed into (Assuming you have provided support for that version).

# Using in your asset store asset
   To use this in an Asset Store context, you will need to do the following:
   - Copy the files into your project somewhere
   - Change the namespace on the files to your own namespace, so they do not conflict with someone else using the same system
   - In ShaderPackagerImporter, change the k_FileExtension to something unique to your project.
   - Note that you don't need to ship the ShaderPackagerImportEditor.cs file to your users, it is only required for the packing UI.

# ShaderPackager
   To pack your shaders, generate your shaders in whatever program you'd like. My asset, Better Shaders, will let you write a single shader that compiles for any SRP, and can export shaders for each pipeline for you - or you can hand write them, or use something like ASE to generate them. Then create a new Shader Package in the project from the right click menu.

   Add some entries, and set the SRP Target, Min and Max Unity versions for each, and set the shader. When you are done, press the Pack button, it will copy the source code for each shader into the text block of each entry. Then press Apply, and you will notice that your object now becomes a shader for the current project.

# Current Issues
   The render pipeline detection is based on installed pipeline, not installed and active pipeline. So if a user switches has multiple SRPs installed, or one installed and not active, they will get the wrong shader. I'll have to fix this at some point soon.
