# Cap materials

`SliceFill` is a dependency-free unlit material for Built-in, URP, and HDRP. Its texture scale is `1`; physical tiling in **Meter Scaled Tiled** mode is controlled first by **Cap UV Meters Per Tile** on `UmaDismemberment`.

For a cross-section texture with a localized center feature, select **Centered Fit** on the individual sliceable-bone row. Each disconnected cap loop is centered at UV `(0.5, 0.5)`, preserves its aspect ratio, and fits inside **Centered UV Padding**. Use Clamp wrap mode and keep the bone or other focal detail near the texture center. **Cap UV Meters Per Tile** does not control this mode.

For a pipeline-native lit cap, duplicate the material, select a shader supported by that pipeline, and add the exact `RenderPipelineAsset` and material to the component's override list. The fallback remains available to the Built-in pipeline and to pipelines without an explicit override.
