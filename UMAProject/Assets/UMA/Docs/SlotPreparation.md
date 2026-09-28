# Automatic slot preparation

Slot preparation accelerates repeated source validation, blendshape baking, and
the initial mesh-reuse fingerprint. It does not replace UMA's mesh-building
backend or change avatar-specific skinning, modifiers, LOD selection, or UV remapping.

## Existing and new slots

- Existing SlotDataAssets continue to load. Missing, outdated, or damaged preparation
  is rebuilt automatically on first use. No migration dialog or manual upgrade is required.
- The converted metadata stays on the slot. In edit mode, a persistent converted slot
  in an editable Assets folder is marked dirty so the next save writes the new format, even without another edit.
  Conversion itself does not write to disk. Play-mode/player conversion does not mark assets dirty.
  Read-only/package slots can still convert in memory without attempting to write their source files.
- Normal editor saves and slot builders persist current preparation. On reload, saved
  metadata is verified against the source once; valid metadata is reused without conversion.
  Save Asset If Dirty also preserves metadata already converted in memory.
- A stale save from an external editor remains safe: it is verified and automatically
  repaired on use. Invalid source geometry is not silently repaired; it retains the
  existing combiner's validation/fallback behavior.
- Optional maintenance commands are under **UMA > Slot Preparation**. They are not
  necessary for compatibility. Batch upgrades support cancellation and skip non-editable assets.

Source geometry, names, weights, and blendshape positions remain authoritative.
Explicit preparation and normal saves remove only exactly-zero optional blendshape
normal/tangent arrays. Tiny nonzero deltas are preserved. Automatic first-use conversion
does not strip shared source channels while avatars are building.

## What is prepared

Versioned 128-bit source fingerprints cover all vertex streams (including four UV sets),
all topology/LOD ranges, bind poses, legacy and variable bone weights, bone hashes and
hierarchy transforms, root identifiers, cloth data, and every blendshape name, frame
weight and delta channel. Metadata has its own integrity fingerprint.

Validated bounds/counts and per-frame normal/tangent flags accompany sparse affected-vertex
lists. Dense frames do not store a redundant full index list. Multi-frame sparse baking
uses the union of both frames, including normal-only/tangent-only and previous-frame-only
changes. Included (unbaked) blendshapes retain their names, frames and values.

Fingerprints are an optimization, not proof of reuse equality: the generated-resource
cache retains its exact input comparisons and avatar-specific request parameters.
Runtime lookup tables use weak keys and own no native buffers or avatar objects.

## Editing and invalidation

UMA's UV mirror, source extraction, bone update/copy, normal/blendshape copy, triangle/LOD
setters and hierarchy assignment paths invalidate affected preparation. New generated
output buffers do not invalidate source caches. Editor dirty counts, OnValidate,
Undo/Redo, and imports also invalidate cached source verification. Builder preparation
runs after final geometry changes, including UDIM welding and LOD generation.

Custom runtime code that writes UMA's public source arrays directly must notify UMA:

```csharp
slot.meshData.vertices[index] = newPosition;
slot.meshData.InvalidateBuildData();
```

Call once after the edit batch, before rebuilding. `UMAResourceReuse.InvalidateMeshSource`
is equivalent. Shared/aliased source buffers require invalidation too. Arbitrary in-place
array writes cannot be detected in constant time without this notification. Editor
tools should also mark edited SlotDataAssets dirty. Replacing the complete mesh data
object naturally creates a new preparation entry. Existing generated meshes are not
mutated; changes become visible when the avatar rebuilds.

## Storage benchmark decision

Tested in the current **UMA NextGen 3.0f5 / Unity 6000.3.18f1** checkout, not a copied
validation project. Representative results from `UMASlotPreparationBenchmarkTests`:

| UMA30 body tile | Vertices | Current pack stage | Prototype native copy | Extra packed bytes | Extra topology bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1001 | 3,183 | 0.0527 ms | 0.0026 ms | 152,784 | 67,308 |
| 1002 | 2,803 | 0.0384 ms | 0.0023 ms | 134,544 | 64,176 |
| 1003 | 793 | 0.0158 ms | 0.0009 ms | 38,064 | 16,308 |

The current packing timings are measured inside 10 actual jobified builds, with reuse
off. Prototype copies are allocation-free microbenchmarks (median of seven batches of
50). They are **not** measured end-to-end speedups; additional validation, loading,
modified-vertex handling, stream selection and atlas remapping still apply. Topology
prototype construction measured 0.0046–0.0191 ms, versus 0.0005–0.0018 ms to copy it;
UMA already keeps source index buffers, so those comparisons do not establish a need
for another stored copy.

**Decision: do not serialize duplicate packed vertex streams or triangle records yet.**
These three tiles alone would add 325,392 bytes of vertex streams and 147,792 bytes of
topology. The small measured stage costs do not establish an end-to-end benefit large
enough to justify that storage and invalidation complexity. The benchmark remains
available for future platform-specific testing.

A synthetic 24k-vertex/two-frame slot took about 18.7 ms for first-use conversion,
versus 0.17 ms to verify its saved preparation. These are one-time costs per source
revision, not per-avatar overhead. Ten thousand warm metadata checks allocated zero
managed bytes. Full synthetic builds were about 3.3–3.5 ms, including avatar setup and
disposal; those numbers are not a pre-change performance comparison.

## Regression coverage

`UMAMeshPreparationTests` covers exact zero detection, Unity mesh extraction, sparse
multi-frame baking through incremental/jobified paths, serialization, automatic
upgrade and save/unload/reload, malformed/stale metadata, fingerprints across source
channels, editor and alias invalidation, exact mesh reuse, and weak-cache lifetimes.
Run it alongside the existing slot-builder, mesh-combiner and generated-resource reuse suites.
