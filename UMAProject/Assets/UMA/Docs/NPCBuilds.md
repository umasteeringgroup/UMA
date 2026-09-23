# Completed NPC builds

`BuildCharacter()` remains the editable, general-purpose path with normal mesh and atlas caching. `BuildNPC()` is a runtime shortcut for **explicitly repeated appearances**, not an automatic similarity search. A handle represents the source appearance, including wardrobe, DNA, colors, mesh settings and baked/included blendshapes. Passing it intentionally selects that appearance; unrelated recipient appearance edits are not merged.

## Random crowds

On UMARandomAvatar set **Maximum Unique Characters** to a nonzero pool size and enable **Use Completed NPC Builds**. The equivalent DCA option on the character prefab also opts this spawner in. Enable **Reuse Identical Meshes** and **Reuse Identical Atlases** (or the monitor's reuse ON policy). Existing scenes stay opted out until you enable the new option.

The first character for each setup builds normally. Pending replicas retain the same request. Once ready, replicas instantiate only the generated hierarchy and renderers, without merging recipes, building an Animator avatar, or entering the mesh/atlas generation queues. Gameplay components still come from the original character prefab, never from another live character. The shortcut queue has an 8 ms/frame work budget, independent of the generator iteration limit; a single instantiation cannot be interrupted midway.

## Explicit API and ownership

```csharp
// Configure a fresh source DCA before its initial build.
source.BuildCharacterEnabled = false;
source.reuseGeneratedMeshes = source.reuseGeneratedTextures = true;
UMANPCBuildHandle borrowed = source.BuildNPC();
UMANPCBuildHandle pool = borrowed?.Retain();

// Configure/create a fresh recipient from your normal character prefab.
recipient.BuildCharacterEnabled = false;
recipient.reuseGeneratedMeshes = recipient.reuseGeneratedTextures = true;
recipient.BuildNPC(pool); // also accepts a pending pool request

// Dispose your retained handle when the pool ends.
pool?.Dispose();
```

The new template returned by `BuildNPC()` is owned by its source DCA. Retain it before storing it beyond that source's lifetime. Each retained handle must be disposed. A caller must not pass a disposed handle. Null means no template is available; check before requesting replicas. `IsPending`, `IsReady` and `FailureReason` describe the handle; `NPCBuildStatus` explains each DCA's shortcut or fallback.

Each live instance owns independent skeleton transforms, Animator/Avatar objects, DNA, recipe data and blendshape weights. Built-in mesh modifiers are copied directly without JSON serialization; custom modifier implementations fall back to normal generation. Generated mesh/material/atlas leases remain shared. Removing the source or pool does not destroy resources still used by replicas. Inactive template renderer leases are released explicitly, including at session/assembly shutdown.

Use MaterialPropertyBlock for instance-only shader parameters, or `UMAResourceLeaseOwner.MakeMaterialsUnique(renderer)` before changing a shared material. Use `MakeMeshUnique`/`MakeTextureUnique` before directly modifying shared generated buffers. This does not require disabling general texture reuse.

## Editing, invalidation and fallback

Call `BuildCharacter()` on an instance to make ordinary wardrobe/DNA/color edits. It cancels any pending NPC request and uses the existing generator/cache path. `CancelNPCBuild()` cancels a queued shortcut; it does not cancel an ordinary build already submitted to the generator.

Mesh/texture input invalidation and registered dependency revisions invalidate templates for future instances; existing instances remain unchanged. UMARandomAvatar replaces invalidated pool handles automatically on its next request. Generator output-quality mismatches also use the normal path. For direct runtime edits to race, DNA, source materials or other dependencies outside those APIs, call `UMANPCBuildHandle.InvalidateAll()` before requesting more instances, then replace manually managed pool handles. A retained template is a frozen appearance, not a live link to its source DCA.

Unsupported paths conservatively build normally: external/FBX-route or legacy rigs, custom avatar subclasses, addressable content, runtime DNA providers, direct source overrides, build-stage callbacks, embedded bone physics, custom rig/renderer components, private generated materials, pending atlas conversions, and externally retained Animator avatars. Created/updated and slot-completed callbacks run per instance. A failed or invalidated template does not leave waiters stuck; they build normally with the captured appearance. A caller that wants a replacement template must start a new pool request.

## Measurements

The resource monitor and saved JSON include **NPC shortcut instances**, **NPC instantiation work**, and the configured opt-in. Shortcut work includes native instantiation, private recipe copies, skeleton binding and completion callbacks. It is outside the generator timer: compare wall time and generator **plus** NPC work, not the reduced generator time alone. Output-memory totals count meshes and atlases, not template skeletons, private recipes, materials, Animators, or total application RAM. Re-run with the same random seed, pool, quality and spawn budget for a fair comparison.
