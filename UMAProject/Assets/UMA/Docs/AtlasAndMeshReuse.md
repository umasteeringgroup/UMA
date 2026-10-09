# Atlas and mesh reuse

This guide explains how to share generated character resources in UMA NextGen 3.1f1 on Unity 6.3 and newer. Start here for setup and practical examples; see [Generated resource reuse](GeneratedResourceReuse.md) for cache internals, extension APIs, and detailed profiling notes.

## What you gain

When several characters need the same generated mesh or atlas, UMA can build that output once and let matching characters reference it. This can reduce generation work and the memory occupied by duplicate meshes and textures. Each character keeps its own skeleton, renderer, Animator, pose, and live expression weights, so sharing does not make characters animate together.

Mesh reuse and atlas reuse are independent. You can enable either one or both. Reuse is most useful for crowds, repeated outfits, and NPC variants with common source assets. A character with completely unique output may have nothing to share.

An atlas is the generated texture that combines the character's overlays for a material channel. Reuse matches those generated outputs; you do not need to create a separate Unity Sprite Atlas or manually assign one character's textures to another.

## Get started in the Inspector

1. Select a character with a **Dynamic Character Avatar** component, or edit the avatar prefab you will use to spawn NPCs.
2. In **Standard View**, open **Generation & resource sharing**.
3. Leave **Inherit Generator Reuse Policy** off when choosing reuse directly on this avatar.
4. Enable **Reuse Identical Meshes**, **Reuse Identical Atlases**, or both.
5. Build or rebuild the character. For spawned characters, configure the prefab before generation begins.
6. Build a second character from the same race, wardrobe, DNA, colors, and output settings. Keep the first character alive while the second builds so its shared output remains available.

In **Advanced View**, the equivalent controls are under **Advanced Options > Cache and Reuse (NPCs)**:

| Standard View | Advanced View | C# field |
| --- | --- | --- |
| Reuse Identical Meshes | Share generated meshes | `reuseGeneratedMeshes` |
| Reuse Identical Atlases | Share atlases / matching materials | `reuseGeneratedTextures` |

Both reuse flags default to off. Set them on every participating character. Changes take effect on the next build; switching a flag off does not detach an already displayed shared output until it is rebuilt.

### Enable reuse from code

Call this with an initialized avatar, after choosing its appearance and before requesting its build:

```csharp
using UMA.CharacterSystem;

public static class SharedAvatarBuild
{
    public static void Build(DynamicCharacterAvatar avatar)
    {
        avatar.inheritGeneratorReusePolicy = false;
        avatar.reuseGeneratedMeshes = true;
        avatar.reuseGeneratedTextures = true;
        avatar.BuildCharacter();
    }
}
```

These fields also exist on `UMAData` for callers that do not use `DynamicCharacterAvatar`. The generator handles matching and ownership automatically; no cache registration is needed for ordinary UMA content.

### Control a crowd with quality profiles

If reuse should follow a generator quality profile, enable **Inherit Generator Reuse Policy** on participating avatars. In the profile, enable the overrides for **Reuse Meshes** and **Reuse Textures**, then set their values. Apply the profile through a **Generator Quality Controller** or `UMAGeneratorOverride`.

An avatar's local flags are authoritative by default. With inheritance enabled, the effective profile can override them. Profile rebuild mode determines when existing characters update: **New Builds Only**, **Gradual**, or **Immediate**. See [Generator quality profiles](GeneratorQuality.md) for the full setup and precedence rules.

## Design characters that can share

UMA compares the inputs that determine the generated result. Giving two outfits the same name is not enough; their geometry, texture inputs, layout, and relevant settings must agree.

| Character variation | Expected sharing opportunity |
| --- | --- |
| Identical appearance with different animation or bone pose | Meshes and atlases can share; pose remains per character. |
| Same generated geometry and atlas UV layout, different overlay colors | Meshes can share. Atlas channels whose compositing inputs change need different outputs. |
| Different geometry, same atlas inputs and layout | Atlases can share even when meshes cannot. |
| Different surface shader parameters with the same atlas inputs | Atlases can share; differing material parameters keep separate materials. |
| Different baked blendshape values, mesh modifiers, mesh-hide masks, or mesh LOD | Changed mesh outputs cannot share. |
| Different atlas resolution, layout, source textures, or composited colors | Changed atlas outputs cannot share. A layout change can also prevent mesh sharing because atlas UVs are part of the mesh. |

For a practical crowd, begin with a small set of repeated appearance variants. Reuse source slot and texture assets, keep compatible atlas and LOD settings, and vary animation freely. Add appearance variations incrementally while watching cache hits and output memory. Continuous random colors and unique baked shapes can greatly reduce matching opportunities.

DNA does not have one universal effect on reuse: a change that alters generated geometry, bind poses, or other mesh inputs can prevent sharing. Live, non-baked facial expression weights remain renderer-local and do not prevent mesh sharing.

## How it works

1. UMA resolves the character's recipe and prepares its output requirements.
2. It describes the relevant mesh or atlas inputs and looks for a compatible shared result. Hashes help locate candidates; exact comparison decides whether sharing is safe.
3. On a hit, the renderer receives a reference to the existing output. On a miss, UMA generates an output and publishes it for matching characters.
4. Matching incremental mesh requests can wait for one in-progress build. Matching atlas requests can also share a pending GPU-to-Texture2D conversion.
5. Each renderer retains ownership of its outputs. Replacing or destroying one character releases its references without freeing resources still used by others.

Atlas matching happens independently for each channel. Materials can also be shared when their effective settings and bound textures match. Different material settings do not automatically prevent texture sharing.

The cache retains outputs while owners need them; it is not a permanent library of every character ever built. Disabled or pooled avatars still own their resources. Destroying the last owner releases its output, so building a warm-up character and immediately destroying it does not guarantee a future cache hit.

The standard/default, jobified, and incremental mesh combiners support reuse. Custom subclasses that retain custom combining behavior take their normal private path. Recipe resolution, skeleton setup, renderer binding, and some preparation still happen even on a hit. Sharing does not remove animation, skinning, or draw-call costs.

## Make individual changes safely

For persistent appearance changes, edit the avatar's DNA, wardrobe, overlays, or shared colors and rebuild. UMA then finds or creates the appropriate output.

For renderer-specific shading, prefer a `MaterialPropertyBlock`. If custom code needs to modify a generated mesh, material, or texture directly, make that resource unique first:

```csharp
using UMA;
using UnityEngine;

// Run after the avatar has finished building.
var renderer = avatar.umaData.GetRenderer(0);
Mesh privateMesh = UMAResourceLeaseOwner.MakeMeshUnique(renderer);
UMAResourceLeaseOwner.MakeMaterialsUnique(renderer);
Texture privateTexture = UMAResourceLeaseOwner.MakeTextureUnique(
    renderer, 0, "_BaseMap");
```

Use the texture property name required by your shader; `_BaseMap` is an example. These helpers copy shared generated resources, not source asset textures. Direct output edits are replaced by a later avatar rebuild. Do not mutate or destroy a shared generated resource directly, because other characters may still use it. Making a texture unique can wait for a pending GPU readback.

Runtime code that edits source mesh arrays or writes texture contents must also notify UMA of those changes. See the invalidation and dependency-revision examples in [Generated resource reuse](GeneratedResourceReuse.md#runtime-inputs-and-extension-code).

## Check that reuse is helping

Start with two identical characters before measuring a large randomized crowd. In the DCA's **Advanced Options > Cache and Reuse (NPCs)**, use **Log Cache Diagnostics** to inspect hits, misses, pending joins, and published outputs. The status line explains cache bypasses. Reference counts include build and resource ownership, so they are not a character count.

For an OFF/ON comparison, open the **U3-Generating Random Characters** sample, select **GeneratorParms**, and enter Play mode. Its **UMA Resource Usage Monitor** can restart the crowd with reuse off or on, replay the same initial random sequence, and compare generation timings, unique resources, and memory avoided through live sharing. Wait for **Run complete** before comparing results. See [the monitor walkthrough](GeneratedResourceReuse.md#compare-random-crowd-runs-in-the-inspector) for measurement limits.

| Symptom | What to check |
| --- | --- |
| No hits on the first character | A first build normally has no matching live output yet. Build another matching character while the first remains alive. |
| Identical-looking characters do not share | Check both avatars' flags, inherited policy, source asset identities, baked shapes, atlas layout, output sizes, LOD, and material/compositor settings. |
| Mesh hits but few atlas hits | Look for randomized colors, different overlays or texture inputs, and differing atlas settings. |
| Atlas reuse is bypassed | Check the status reason. Registered `AtlasUpdated` callbacks can modify pixels and require private outputs; runtime textures without dependable revisions and unsupported custom compositors can also bypass reuse. |
| Disabling reuse appears to do nothing | Rebuild the affected character; existing output remains shared until replaced. |
| Memory remains allocated after pooling | Inactive renderers still own their outputs. Destroying an avatar releases its references; remaining owners keep shared resources alive. |

Measure memory and generation time in a player build on your target hardware. Cache hit counts alone do not predict a frame-rate improvement.

## Related features

**Use Completed NPC Builds** is a separate opt-in path that caches a completed NPC template and restores it for a matching character. Atlas and mesh reuse share individual generated resources around the normal builders. See [Completed NPC builds](NPCBuilds.md) before enabling that additional option.

For more detail, continue with [Generated resource reuse](GeneratedResourceReuse.md), [Mesh combiners](MeshCombiners.md), and [Generator quality profiles](GeneratorQuality.md).
