# Eye, wrap and embroidery examples

These original examples belong to the optional **Overlay Painter Examples** companion package. They require Overlay Painter, UMA 3 content and UMA render pipeline support.

- **UMA Eyes Blue / Hazel Project**: open these Overlay Painter documents to edit a procedural eye on the actual `UMA30_Eyes_slot`. The project uses a PBR authoring material for Albedo, Roughness and Normal Control. It does not replace the character's specialized eye/cornea shader. Both eyes share the stock slot's overlapping UVs.
- **Blue, Brown, Green, Hazel and Gray Iris**: editable material presets for the Eye/Iris plugin.
- **LeatherWrap, ClothBandage, GripTape, GauzeWrap, AdhesiveDressing**: native garment construction presets. Assign to a generated path or projection; size the ribbon/patch to the surface and adjust Feature Spacing for strip width.
- **Gold Satin Crest / Colored Tatami Crest**: editable embroidery presets using the included original Crest Motif and RGB direction map. Add a fabric layer beneath them.

The Examples menu can recreate missing presets and eye projects without replacing existing ones. Full controls and limitations are documented in Overlay Painter's **Eyes, wraps and advanced embroidery** guide.

## Visual reference

`UMA-Eye-Slot.png` is a render of the generated blue material on the installed UMA eye mesh. `Eyes-Gallery.png` shows the five eye colors. `Wraps-Gallery.png` shows the five wrap constructions with height-based preview lighting. `Embroidery-Satin.png` and `Embroidery-Tatami.png` show the same crest with different stitch construction; the checkerboard is a viewing aid.

## Validation

Validated in `C:/GitHub/UMA/UMAProject`, installed UMA **3.1f3**, Unity **6000.3.18f1**, on an Intel Core i7-12700K and NVIDIA RTX 5070 Ti (128 GB RAM).

- 13 focused generator/host checks passed, covering determinism, coverage, iris relief isolation, independent/shared eye UVs, cancellation, undo/redo and path/projection alignment.
- 96 existing garment/quilt regression cases passed, including every native garment preset in both path and projection modes.
- Tests were invoked directly through their NUnit methods in the live Editor, including setup/teardown. The full Unity Test Runner was not used because it required replacing the user's unsaved scene.
- Eye material at 512 square: 41–46 ms for the three shared CPU outputs. Native wrap projection at 512 square: 36–37 ms warm / 81 ms first invocation, including GPU readback. Embroidery contour preparation for the 256-square crest: 90–92 ms. These are stage timings, not end-to-end interactive latency guarantees.

The contour field is capped at 1024 square; tiny contour features may need a larger motif or narrower border. Wraps are texture relief, not extra geometry. Shared eye UVs cannot hold independent left/right textures. Captures are surface/slot demonstrations, not a claim of physically refractive cornea rendering.
