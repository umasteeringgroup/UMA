# UMA Addressables

UMA can use Unity Addressables to load character recipes and their dependencies on demand. This is useful for a large wardrobe library, downloadable content, branded character sets, or any project that should not keep all UMA content in memory at startup.

UMA Addressables are driven by the **UMA Global Library** (`UMAAssetIndexer`). UMA labels each recipe and the assets it needs, then `DynamicCharacterAvatar` (DCA) loads those labels before building the character.

> Addressables are optional. A normal UMA project can keep using Resources and direct asset references. Enable them when there is a real content-loading or memory-management reason to do so.

## How the UMA workflow works

The standard UMA generator is `SingleGroupGenerator`. It creates or updates an Addressables group called `UMA_SharedItems`, configured to **Pack Separately**, and writes the corresponding group, address, and label information into the UMA Global Library.

An asset can have more than one label. That is expected: a shared body slot or overlay can be needed by many wardrobe recipes and must load for each of them.

The generator applies these labels:

| Label | Source | Purpose |
| --- | --- | --- |
| Recipe label | The recipe's **Alt Addressable Label**, or its asset name when that field is blank | Loads the recipe and its dependencies together. |
| Default label | **Addr Default Label** in Project Settings > UMA; `UMA_Default` by default | Marks normal shared UMA dependencies. |
| Wardrobe collection label | The `UMAWardrobeCollection` label | Lets a collection request all of its member recipes. |
| `UMA_Recipes` | Generated when **Addr Include Recipes** is enabled | Lets projects request recipe assets as a set. |

When **Addr Include Other** is enabled, indexed `RaceData`, `RuntimeAnimatorController`, `TextAsset`, and `DynamicUMADnaAsset` assets also receive the default label.

## Before you start

Addressables generation changes Unity Addressables settings and UMA Global Library metadata. Final-build preparation can also temporarily modify UMA source assets to avoid duplicate materials, shaders, and texture references in bundles.

Before first generation:

1. Commit or back up the project, including `Assets/AddressableAssetsData` if it exists, UMA settings, and the Global Library.
2. Confirm the Global Library contains every race, recipe, slot, overlay, material, DNA asset, and controller needed by your characters.
3. Give every independently loaded recipe a stable, unique **Alt Addressable Label**. Prefer a production identifier such as `HeroKnight` over a temporary authoring name.
4. Decide which content belongs in Resources and which belongs in Addressables. Mixing both is supported, but each asset should have a clear ownership path.

Do not run the cleanup commands until you have generated groups and inspected the results. See [Safe cleanup](#safe-cleanup).

## Enable UMA Addressables

### 1. Install and initialize Unity Addressables

Install Unity's **Addressables** package with the Package Manager. If the project does not have Addressables settings yet, open Unity's Addressables Groups window and create the settings when prompted. UMA cannot generate groups until Unity has an `AddressableAssetSettings` asset.

Set up the Addressables profiles, build path, and load path for the project. UMA creates entries and labels, but Unity Addressables controls whether bundles are local, remote, cached, or downloaded from a CDN.

### 2. Enable the UMA compile option

Open **Edit > Project Settings > UMA** and enable **Use Addressables** under **Project Build Options**. UMA adds the `UMA_ADDRESSABLES` scripting define and Unity recompiles the project.

You can also open **UMA > Asset Index > Global Library**, open its **Addressables** menu, and choose **Enable Addressables (Package must be installed first)**. It performs the same define-symbol change.

Wait for compilation to finish before rebuilding the library. The Global Library's Addressables menu is unavailable until UMA has compiled with `UMA_ADDRESSABLES`.

### 3. Configure UMA's options

In **Project Settings > UMA > UMA Addressables Options**, review these settings before generating groups:

| Setting | What it does | Recommendation |
| --- | --- | --- |
| **Addr Default Label** | The label applied to normal shared dependencies. | Keep it stable. Default: `UMA_Default`. |
| **Addr Include Recipes** | Adds recipe assets themselves to Addressables with `UMA_Recipes`, default, and recipe labels. | Enable when recipes must be discovered or loaded entirely from Addressables. |
| **Addr Include Other** | Adds indexed races, animator controllers, text assets, and dynamic DNA assets. | Enable unless another deliberate system supplies them. |
| **Addr Strip Materials** | Removes direct slot/overlay material references for the build while preserving their names for runtime restoration. | Use for final builds after the workflow is validated. |
| **Addr Strip Textures** | Moves overlay textures to Addressables and replaces texture references with stored names. | Final-build optimization only; see [Texture and shader stripping](#texture-and-shader-stripping). |
| **Addr Strip UV Attached Shaders** | Temporarily strips shaders from UV-attached-item prefab materials for the build. | Enable only when original shaders are included in the player. |

**Always Get Addressables** is in the same Project Settings page. It allows the editor to resolve local assets even when bundles are unavailable. That is useful while authoring, but it can hide missing labels, groups, and bundles. Turn it off for final validation and test a built player.

## Prepare content for generation

### Rebuild and inspect the Global Library

Open **UMA > Asset Index > Global Library**. Rebuild or refresh the library after importing, moving, renaming, or deleting content.

The generator only knows about dependencies that UMA can find through indexed recipe data. An asset absent from the Global Library cannot receive UMA Addressables metadata. After generation, use the library to verify the asset is marked Addressable and has the expected group, address, and labels.

### Set recipe labels

Select a base recipe, wardrobe recipe, or other `UMATextRecipe`-derived asset and set **Alt Addressable Label** if its asset name is not the label you want. This label is what DCA uses to preload the base race recipe, wardrobe recipes, additive recipes, and additional recipes.

When a recipe label changes, you must:

1. Regenerate UMA Addressables groups.
2. Rebuild Addressables content.
3. Update saved avatar definitions, remote catalogs, or code that requests the previous label.

### Resources Only

Recipes marked **Resources Only** are skipped by `SingleGroupGenerator`. Their content stays on the Resources/direct-reference path instead of being assigned by this generator. The inspector notes that this can create duplicate assets if the same dependencies are also included through Addressables.

Use Resources Only only for content that is genuinely local and always available. Do not mark a recipe Resources Only and then expect its label to be downloadable through UMA Addressables.

### Label Local Files

**Label Local Files** is for a deliberate override workflow. UMA searches the recipe's local folder for matching assets and labels those local assets with the recipe label instead of applying the normal shared-label behavior.

Use it for a branding or local-content substitution pack. Do not enable it for ordinary recipes: it can leave a dependency out of the usual shared-label set.

### Force Keep

**Force Keep** marks content as always loaded instead of allowing normal Addressables cleanup to release it. It is appropriate for a small set of startup-critical assets, but it trades memory for convenience. Use it sparingly and profile the result.

## Generate UMA groups

Open **UMA > Asset Index > Global Library**, then use the **Addressables** drop-down in its toolbar.

| Command | Intended use |
| --- | --- |
| **Generators > Generate Single group (fast)** | Runs `SingleGroupGenerator`, honoring the current UMA Addressables settings. Use this while iterating on labels and content. |
| **Generators > Generate Single Group (Final Build Only)** | Clears old UMA groups, then generates a single group with material clearing enabled. Use on a clean build workspace. |
| **Generators > Generate Groups (optimized)** | Uses UMA's alternate multi-group generation path. Use it only if the project relies on and has profiled that strategy. |
| **Generators > Prepare Build** | Enables texture and UV-attached shader stripping, clears materials, regenerates UMA Addressables, prepares the index, and cleans orphaned slot/overlay entries. It is a build-pipeline command, not a casual editor command. |
| **Generators > Postbuild Material Fixup** | Restores UMA material references and material shader assignments in the source project after bundles have been built. |

Generation updates Addressables entries and UMA's index metadata. It does **not** build Addressables bundles. After any recipe, dependency, label, or relevant setting change, regenerate groups and build Addressables content again.

### What SingleGroupGenerator includes

For each non-Resources-Only packed recipe, `SingleGroupGenerator` collects indexed dependencies from the recipe. Depending on the Project Settings, it can also add:

- the recipe asset itself;
- wardrobe collection labels;
- races, animation controllers, text assets, and dynamic DNA assets;
- overlay textures and alpha masks when texture stripping is enabled.

All generated assets go into `UMA_SharedItems`, packed separately. Shared dependencies receive every relevant recipe label so a character can request one recipe label and get its required assets.

## Build Addressables and the player

The normal order is:

1. Generate UMA Addressables groups.
2. Build Addressables content with Unity's Addressables build command or a build script.
3. Build the player after the Addressables content build succeeds.
4. If material stripping was used, run **Postbuild Material Fixup**. If UV-attached shaders were stripped, also run **Reset stripped shaders**. Treat texture stripping as a build-copy-only operation; see [Texture and shader stripping](#texture-and-shader-stripping).
5. Test using the same Addressables profile, catalog, and load path intended for release.

For CI or custom build automation, UMA provides pre- and post-build helpers. Call the pre-step before Unity builds Addressables content and the post-step after it completes:

```csharp
using UnityEditor.AddressableAssets.Settings;
using UMA;

UMAAddressablesSupport.Instance.AddressablesBuildPreStep();
AddressableAssetSettings.BuildPlayerContent(out var result);

if (!string.IsNullOrEmpty(result.Error))
{
    throw new System.Exception(result.Error);
}

UMAAddressablesSupport.Instance.AddressablesBuildPostStep();
```

The pre-step prepares/rebuilds the UMA index, runs `SingleGroupGenerator` with material clearing, and adds references for UMA items that remain non-addressable. The post-step performs material fixup. See `UMAAddressablesBuildSample.cs` for a complete example.

> If a build fails after stripping has started, run **Generators > Postbuild Material Fixup** and, when applicable, **Reset stripped shaders** before returning to normal authoring. Do not commit accidentally stripped source assets. Texture stripping is not undone by these commands, so restore those assets from a clean checkout/build copy when necessary.

## Runtime behavior with DynamicCharacterAvatar

With Addressables enabled, a normal DCA build is asynchronous:

1. DCA collects the active race's base recipe, worn wardrobe recipes, additive recipes, and additional recipes.
2. `UMAAssetIndexer.Preload(this)` requests their labels through a union Addressables load.
3. The indexer registers and post-processes loaded assets.
4. DCA resumes `LoadCharacter` and builds the UMA.

Do not assume the character is ready immediately after requesting a build. Use the normal DCA completion events/callbacks before reading generated renderers, skeletons, or DNA-dependent results.

Keep **Bundle Check** enabled on the DCA. In a player, it lets DCA wait for required Addressables content before the final build. Editor generation can skip bundle lookup to support local authoring; that is not a substitute for testing a player build.

`DynamicCharacterAvatar.GenerateNow()` is not supported with Addressables because it is synchronous. Use the normal DCA build/load flow instead.

### Loading and unloading

DCA tracks its Addressables preload operations. After a later build succeeds, it releases older operations; **Delay Unload** controls the short grace period before release (two seconds by default), avoiding churn during immediate rebuilds.

If your own code calls `UMAAssetIndexer.Preload`, `LoadLabel`, or Unity Addressables APIs directly, it owns the returned operation handle and must release it when it is no longer needed. Avoid mixing ad-hoc loads with DCA's normal lifecycle unless ownership is explicit.

## Texture and shader stripping

Stripping is a final-build optimization. It prevents bundles from carrying duplicate material, shader, and template-texture references, but it intentionally changes source assets while the build is being prepared. Use a clean, disposable build workspace or source-control workflow for it.

### Stripped overlay textures

With **Addr Strip Textures** enabled, UMA adds overlay textures to Addressables, stores their names in `OverlayDataAsset.textureNames`, and clears the matching entries in `textureList`. At runtime, `UMAAssetIndexer` restores the textures by looking up those stored names after the overlay loads. The post-build material fixup command does not restore those serialized texture-list references, so do not enable this on the only editable copy of your content unless the stripped form is intentionally committed.

For reliable restoration:

- Ensure `Texture2D` assets are indexed before the build. UMA's settings UI explicitly requires this, and the generator also attempts to add the type when stripping is enabled.
- Use unique names for independently used textures. Restoration is name based, so duplicate texture names are ambiguous.
- Regenerate groups and rebuild Addressables whenever overlay textures change.

### Stripped materials and shaders

With material clearing enabled, slots and overlays retain material names but lose direct material references. UMA restores the materials through the Global Library at runtime. **Postbuild Material Fixup** restores these material references and `UMAMaterial` shader assignments in the editor after the content build.

With **Addr Strip UV Attached Shaders** enabled, UMA replaces shaders on UV-attached-item prefab materials and records the original shader name in a material tag. Use **Reset stripped shaders** after the bundle build to restore those prefab-material shaders in the editor.

Make sure required shader variants are included in the player. The Prepare Build path expects projects to retain shader references, commonly through a shader-reference prefab such as the UMA initialization scene's `ForceIncludeShaders` object, so Unity does not strip the needed variants.

If a character is pink, uses `Hidden/InternalErrorShader`, or an attached item has missing materials:

1. Confirm the original shader is included in the player.
2. Confirm the material and `UMAMaterial` are indexed and generated into the correct Addressables content.
3. Run **Postbuild Material Fixup** and, for UV-attached items, **Reset stripped shaders** after building. Restore stripped overlay texture references from a clean build copy/source-control revision if needed.

## Validate before shipping

Test a development player, not only the editor:

- The Addressables Groups window contains `UMA_SharedItems` with the expected entries, using **Pack Separately**.
- Tested recipes have their assigned labels and shared dependencies have all expected labels.
- The Global Library shows the same assets as Addressable with a group, address, and labels.
- The Addressables content build completed after the most recent UMA generation.
- The player uses the correct profile, catalog, and local/remote load path.
- DCA can build its base race, change wardrobe, apply additive recipes, and rebuild without missing slots, overlays, materials, animator controllers, or DNA assets.
- **Always Get Addressables** is disabled where practical so local editor references cannot conceal a missing bundle.
- Material/shader source assets have been restored with **Postbuild Material Fixup** and, when needed, **Reset stripped shaders**. Texture stripping was performed in a disposable build copy or intentionally retained.

## Troubleshooting

### The Global Library has no Addressables menu

The Unity Addressables package may not be installed, or UMA has not compiled with `UMA_ADDRESSABLES`. Install the package, enable **Use Addressables** in Project Settings > UMA, wait for compilation, and create Unity Addressables settings if the project has none.

### "Resources for the following recipes cannot be loaded from the Addressables System"

UMA reports this when Unity Addressables cannot resolve one or more requested labels. Check the exact label in the error, then:

1. Verify the recipe's **Alt Addressable Label** or asset-name fallback.
2. Regenerate UMA groups.
3. Confirm the Unity Addressables entry has that label.
4. Rebuild content and deploy the new catalog/bundles.
5. Confirm the DCA's race, wardrobe, additive, and additional recipes are all indexed.

### A race or wardrobe item works in the editor but is missing in a player

The editor probably resolved a local reference absent from the player. Disable **Always Get Addressables** for validation, then check that:

- the asset is indexed;
- its recipe is not unintentionally **Resources Only**;
- labels were regenerated after the last edit;
- Addressables content was rebuilt and the correct profile/catalog is deployed;
- **Addr Include Other** is enabled when races, animators, text assets, or dynamic DNA are not supplied elsewhere.

### Textures are blank or wrong after stripping

Check that the texture is indexed as `Texture2D`, addressable, and has the same labels as its parent overlay. Check for duplicate texture names, then regenerate and rebuild. Use normal, unstripped generation while diagnosing so the original references remain visible.

### The project remains stripped after a build

Run **Addressables > Generators > Postbuild Material Fixup** from the Global Library to restore UMA material references and `UMAMaterial` shaders. Run **Reset stripped shaders** for UV-attached prefab materials. Neither command restores the serialized `OverlayDataAsset.textureList` references cleared by texture stripping; restore those from a clean build copy/source-control revision, then disable texture stripping for normal authoring. If a shader cannot be restored, make sure it still exists in the project and its recorded name is valid.

### The library reports orphaned slots or overlays

An orphan is an index entry that is neither Addressable, in Resources, nor marked always loaded. Start with **Select Orphaned Slots** or **Select Orphaned Overlays** and inspect the results. Mark deliberately retained content as Keep/always loaded or ensure it belongs to a generated recipe path. The remove commands remove entries from the Global Library; use them only after generation and validation.

### I edited UMA Addressables entries by hand and generation removed them

UMA generation is authoritative for UMA-owned groups and Global Library metadata. Keep unrelated/manual Addressables content in separate groups, and make UMA inclusion or label changes through UMA settings and recipe inspectors before regenerating.

## Safe cleanup

The Global Library's Addressables menu includes **Remove Addressables**, **Delete Empty Groups**, and orphan cleanup commands. Treat them as maintenance tools:

- **Remove Addressables** removes UMA-managed groups and can clear UMA Addressables flags. Do not use UMA-managed group prefixes for unrelated content.
- **Delete Empty Groups** removes only empty UMA-managed groups.
- Start with **Select Orphaned**; inspect before using **Remove Orphaned**.
- **Prepare Build** also performs orphan cleanup for slots and overlays. Run it only in a controlled build workflow.

When in doubt, commit first, regenerate from the Global Library, rebuild Addressables content, and validate a player build. That gives UMA a repeatable source of truth instead of relying on hand-edited group state.
