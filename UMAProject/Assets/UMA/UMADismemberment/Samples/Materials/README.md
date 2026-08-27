# Cap materials

`SliceFill` is a dependency-free unlit material for Built-in, URP, and HDRP. Its texture scale is `1`; physical tiling is controlled first by **Cap UV Meters Per Tile** on `UmaDismemberment`.

For a pipeline-native lit cap, duplicate the material, select a shader supported by that pipeline, and add the exact `RenderPipelineAsset` and material to the component's override list. The fallback remains available to the Built-in pipeline and to pipelines without an explicit override.
