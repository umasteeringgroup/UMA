# Hair Cards Manual QA

Run this matrix on Unity 6.3 or newer before a release.

## Core workflow

- Open from a readable Mesh, generated DynamicCharacterAvatar, and existing HairGroomAsset.
- Open from an avatar whose root or skeleton is rotated/translated away from source-mesh orientation; confirm the painted surface, generated/authored guides, children, helpers, card preview, handles, and groom brushes all remain registered to the baked character.
- Cancel new-groom save and confirm no source object or asset changes.
- Move through every workflow tab and confirm its scoped Scene tool activates: Growth cannot keep painting in Guides, Groom, Cards, Optimize, or Validate & Bake.
- Paint and erase Growth Area with mouse and pen; confirm Alt navigation remains available.
- Enable Growth **Mirror X** from both the workspace and Scene toolbar, then toggle it with `M`. Confirm paint and erase affect both sides across source-local `X = 0`, the mirrored cursor follows a rotated/posed avatar, hidden slots remain excluded, and centerline vertices do not receive double strength.
- Confirm Growth and Groom brushes match Overlay Painter: the inner hardness ring receives full strength, the edge falls off linearly, Shift+right-drag changes radius horizontally and hardness vertically, brackets change radius, and Shift+brackets change hardness.
- Confirm the translucent blue-to-orange Growth overlay updates over the posed authoring surface while the character materials remain visible beneath it.
- Select triangles, add/subtract selection, grow, shrink, invert, clear, and convert between selection and map.
- Preview the same guide seed twice and confirm root positions match.
- Confirm preview guides are dashed and temporary, then accept, replace generated-only, and cancel generation without deleting hand-authored guides.
- Place a guide, move its control points, delete it, undo, and redo.
- Acquire the brush at guide control points and between them, apply every essential brush on a visible sculpt layer, and verify locked layers/groups do not change.
- Create curve and collision helpers, move them, add constraints, break a helper reference, and verify validation reports it.
- Preview ribbon and 3-, 6-, and 12-sided tapered tube profiles.
- Create numbered UV areas, select a non-contiguous subset such as 2, 3, and 7 for one group, and confirm every generated card stays inside those rectangles.
- Rebuild the same groom repeatedly and confirm weighted UV-area assignment is deterministic; change the child seed and confirm the distribution changes.
- Rename and reorder atlas areas and confirm group assignments survive by stable area ID. Delete every selected area and confirm validation reports `MissingAtlasRegion`.
- Switch every LOD repeatedly and confirm deterministic counts and stable material order.

## Persistence and safety

- Save, close the stage, reopen, and verify groups, active data, maps, guides, layers, modifiers, helpers, constraints, profiles, LODs, and bake settings.
- Force a script recompile while the stage is open and confirm it exits safely.
- Enter Play mode while the stage is open and confirm it exits safely.
- Restore the autosave recovery snapshot and verify one undo step restores the prior groom.
- Change source triangle order and verify baking is blocked by topology validation.
- Make an output asset read-only or force an invalid output folder and verify the prior baked output remains usable.

## Runtime and UMA

- Generate with `HairGroomRuntimeAPI` in Play mode without network access or provider configuration.
- Regenerate and dispose repeatedly; verify generated meshes do not accumulate.
- Exercise all LOD levels through `HairGroomRuntimeComponent.SetLodLevel`.
- Bake a Unity mesh, SlotDataAsset, existing-overlay recipe, generated-overlay recipe, and all LOD meshes.
- Confirm updating a bake preserves existing asset references.
- Equip the recipe on every declared compatible race and inspect head, neck, and facial animation.
- Inspect UVs, atlas flips, alpha, tangent-space normals, material count, bounds, and backface behavior.

## Scale and accessibility

- Test 100%, 150%, and 200% editor scaling at narrow and wide dock widths.
- Complete the core workflow with mouse only and pen plus visible controls.
- Navigate task controls with keyboard focus and remap all registered shortcuts.
- Verify information is not conveyed by group color alone: selected guides must also be wider/outlined and validation must include text.

## Performance captures

Benchmark at minimum:

- 250, 1,000, and 5,000 authored guides.
- 1, 4, 8, and 16 children per guide.
- 8, 12, and 24 samples per card.
- Ribbon and 3-, 6-, and 12-sided tubes.
- 50k, 100k, and 250k source vertices while painting.

Record stage open, brush p50/p95, preview rebuild, validation, LOD switch, bake duration, managed allocations, generated vertex/triangle count, material count, and peak memory.
