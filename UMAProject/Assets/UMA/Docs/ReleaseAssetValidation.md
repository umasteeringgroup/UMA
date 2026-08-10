# Release Asset Validation

UMA Release Asset Validation checks whether the optional UMA3 and UMA2 content folders can be exported as independent packages without missing or forbidden project references. It writes a structured JSON report and provides controlled repair actions.

Open it from `UMA > Testing > Release Asset Validation...`.

## Package Rules

The validator applies these boundaries:

- Assets under `Assets/UMA/UMA3` may reference assets under `Assets/UMA`.
- Assets under `Assets/UMA2` may reference assets under `Assets/UMA` or `Assets/UMA2`.
- Unity built-in resources and Package Manager assets are treated as external prerequisites rather than exportable project content.

The release scan includes T-poses, races, slots, overlays, textures, expression sets and groups, bone poses, and UMA DNA-related assets. It checks Unity dependency closure, serialized GUIDs, meta-file references, and loaded serialized object references. Missing scripts and unresolved GUIDs are also reported when their folder context identifies them as release data.

## Run and Review

- `Run Asset Validation` starts the `UMA Release Tests/Asset Validation` EditMode test.
- `Reload Report` reloads the most recent JSON without running the test.
- `Reveal JSON` opens the report location in the operating system.

The report is written to `Temp/UMA/LastReleaseTest.json` at the project root. It is generated even when validation fails unexpectedly, when possible.

The JSON schema contains:

- Overall pass state, issue count, Unity version, project path, and generation time.
- Each release scope and its allowed folders.
- Discovered release assets with name, type, GUID, category, and location.
- Recorded references with serialized field/property paths, source line numbers, source and target assets, status, and validity.
- Repair issues with suggested actions.

The lower grid lists issues. Click a row to populate the detail pane. Use `Highlight Source` and `Highlight Referenced` before repairing anything.

For a missing GUID, the detail pane shows the raw serialized field and YAML source line that owns the reference. A field that appears in the report but not in either Inspector mode is normally stale serialized data from a field that was removed or renamed after the asset was saved.

After a successful item-level repair, the selected issue is removed from the grid immediately. The window then reruns validation in the background; that fresh report is authoritative and will restore the issue if the underlying problem still exists. Bulk `Auto` repairs keep the existing grid visible until their validation pass completes because one operation can have mixed results across many issues.

The toolbar action `Remove all Non-Applicable shader properties from all Materials` finds every unique material that owns one or more issues in the current report. It previews the affected materials and property count, processes each material exactly once, saves the results, removes those materials' current rows from the grid, and reruns validation. Any unrelated issues that remain on a cleaned material return in the fresh report.

## Repair Actions

### Move

Moves the referenced asset into a type-appropriate subfolder under the owner asset's UMA2 or UMA3 scope. Unity preserves its GUID, so all existing project references continue to follow it.

### Copy

Copies the referenced asset into the appropriate scope and retargets recorded writable references from that scope to the new copy. The original remains unchanged.

Copy is transactional where possible. If no writable reference is found or any reference cannot be updated, the utility attempts to roll references back and deletes the new copy.

### Universal

Moves the referenced asset under `Assets/UMA/Universal` in a type-appropriate category. Its GUID is preserved. Use this when UMA2 and UMA3 intentionally share the same project asset.

### Delete Source

Moves the source or owner asset to the operating system recycle bin. It does not delete the referenced dependency. Use this only when the reported source asset should not be part of the release at all.

### Remove All Non-Applicable Shader Properties

When the selected issue belongs to a material, this action removes saved material-property entries whose internal property names are not declared by the material's current shader. It covers saved textures, integers, floats and ranges, colors, and vectors.

The comparison uses the shader's actual property names, such as `_BaseMap`, rather than Inspector display labels such as `Base Map`. Every saved property whose internal name is declared by the current shader is preserved. The action is disabled when there is nothing to remove or when the material's shader is missing and cannot be inspected safely.

The confirmation dialog lists the material, current shader, and properties that will be removed. The material is registered with Unity Undo and saved. Validation then runs again because removing one stale texture entry can resolve both a loaded-object issue and a raw serialized-GUID issue for the same reference.

Use the report-wide toolbar version when several rows or materials have this problem. It applies the same shader-name safety check to all affected materials and deduplicates repeated issues so the same material is never cleaned or saved twice during one operation.

### Reserialize Source Asset

This action is available only when a missing GUID belongs to a raw YAML field that is not present on the asset's current serialized type. After confirmation, Unity force-reserializes that source asset, which removes stale data left by a removed or renamed field. The repair verifies that the reported GUID is gone before removing the issue and rerunning validation.

The action stays disabled for live fields because reserialization preserves genuine missing references. Restore, remap, or clear those fields instead. It is also disabled for saved material properties that the current shader does not use; use `Remove All Non-Applicable Shader Properties` for those. A forced YAML rewrite is not registered with Unity Undo, so review the change in source control.

### Auto

Builds a preview of safe material cleanup and unambiguous moves, then asks for confirmation. It removes non-applicable shader properties from affected source materials before moving assets. An asset is eligible to move only when all known release referrers belong to exactly one destination scope, UMA2 or UMA3.

Dependencies that exist only through a non-applicable material property are excluded from move planning because material cleanup removes those references. Auto never copies or deletes assets, and it never removes a property supported by the material's current shader. Assets referenced from both scopes, referenced from an unknown location, or lacking a clear referrer are left unchanged.

## Destination Categories

Repair actions retain the filename and place assets under category folders chosen from their type or source extension. Unity generates a unique destination pathname rather than overwriting an existing asset.

After any repair, rerun validation. A successful move can reveal a deeper dependency that was previously outside the scanned closure.

## Safe Release Workflow

1. Commit or back up the project.
2. Run validation without repairing.
3. Review missing references before location errors.
4. Use Auto for non-applicable material properties and the unambiguous move set.
5. Resolve shared dependencies with `Universal` or intentional copies.
6. Rerun the test until it passes.
7. Review `LastReleaseTest.json` and changed assets in source control.
8. Export and import each package into a clean test project.

## Important Limitations

- The report describes the project state at the time it was generated. Reloading an old report does not rescan assets.
- Copy can retarget only references the repair utility knows how to write safely.
- Moving a dependency is project-wide because the GUID is preserved.
- Validation proves package-boundary consistency, not that every asset generates a visually correct character.
- Auto deliberately favors doing nothing when ownership is ambiguous.

## Troubleshooting

### The Window Has No Report

Click `Run Asset Validation`. If the test assembly is still compiling, wait for compilation to finish and run it again.

### Copy Rolled Back

The utility could not update every recorded reference safely. Move the dependency with GUID preservation, or duplicate and retarget the references manually.

### An Expected Shared Asset Is Reported

Move it to the Universal area if both packages should share the same GUID, or make intentional per-package copies and update references.

### Validation Passes but the Exported Package Is Incomplete

Confirm that the package export includes the permitted shared UMA root and any required packages. Then perform a clean-project import and generation test.
