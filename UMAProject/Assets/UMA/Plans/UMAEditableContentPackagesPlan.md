# UMA Editable Content Packages Plan

## Implementation status

Completed and hardened on 2026-08-16. All five phases are implemented. The
release builder produces reproducible, revalidated content archives and uses a
verified, recoverable Core staging swap without raw SRP/UMA2/UMA3 trees. The
installer enforces compatible Core ranges and exact dependencies, preserves
content-root GUIDs, adopts matching trees, migrates legacy `Assets/UMA2`,
reports every local/upstream classification, defaults conflicts to abort, and
supports transactional replacement, retained backups, rollback, and reload
recovery. Generated archives omit empty leaf-folder records that Unity cannot
materialize, reject unsafe tar/path forms before extraction, and bind pending
transactions atomically to the exact selected archive and manifest so an
interrupted or altered import cannot be mistaken for a successful update.
Unreadable state and failed rollbacks preserve their recovery backups and block
a second replacement rather than deleting the only recoverable copy.

The release matrix covers Core-only UPM plus separate URP and HDRP layouts. Its
full-layout state machine uses the real installer for fresh imports, updates,
root-GUID preservation, local-conflict abort and backup replacement, legacy
migration, content removal, indexing, representative scenes, and player builds.
A separate legacy-package gate proves that content GUIDs still owned below
`Packages` are rejected before an import can redirect into read-only package
content.

## Goal

Keep UMA code and tools in the `com.umasteeringgroup.uma` UPM package, but ship the large character-content trees as separate, editable Unity asset packages:

| Distribution | Installed location | Required |
| --- | --- | --- |
| UMA Core (`com.umasteeringgroup.uma`) | `Packages/com.umasteeringgroup.uma` | Yes |
| UMA 3 Content (`UMA3Content.unitypackage`) | `Assets/UMA/UMA3` | Normal UMA setup |
| UMA 2 Legacy Content (`UMA2Content.unitypackage`) | `Assets/UMA/UMA2` | Optional |

The content distributions should be `.unitypackage` archives, not ordinary UPM packages. UPM packages are installed below `Packages` and remain read-only in the common registry/Git installation cases. Importing project-owned assets below `Assets` is the behavior needed for editing materials, textures, recipes, races, and wardrobe content.

## Current-state findings

- `Assets/UMA/UMA3` is already at its desired final asset path, but it is currently inside the UMA core package root.
- `Assets/UMA2` is a separate UPM-style package and must move to `Assets/UMA/UMA2`.
- UMA3 is approximately 939 MiB/3,256 files and UMA2 approximately 833 MiB/2,499 files. Do not embed either archive in the core UPM artifact; that would preserve the core package's current size and duplicate the content in package caches.
- UMA3 already has `UMA3.Samples` runtime/editor assembly definitions. UMA2 has five scripts but no assembly definition and currently falls into a consumer assembly.
- Serialized dependency audit results show the intended forward references, plus reverse references that must be removed before splitting:
  - UMA3 -> Core: 748 reference occurrences.
  - UMA2 -> Core: 461 reference occurrences.
  - UMA2 -> UMA3: 126 reference occurrences, so UMA2 is an add-on to UMA3 rather than a peer that can operate alone.
  - Core -> UMA3: 340 occurrences, concentrated in the Overlay Painter sample and SRP sample scenes.
  - Core -> UMA2: 25 occurrences, concentrated in a physics sample prefab and SRP materials that use UMA2 textures.
- At least 175 files contain the literal old/resolved UMA3 path and 12 contain the UMA2 path. GUID references survive moves, but serialized filename fields, documentation, tests, and editor utilities need a deliberate path migration.

## Package boundary and dependency rules

Use this logical dependency direction:

`Core -> selected UMA SRP support -> UMA3 Content -> UMA2 Legacy Content`

Asset references may point left in that chain. Core must compile and load without either content package. UMA3 must not require UMA2. UMA2 may reference UMA3 and Core.

The existing SRP sample scenes and UMA3 assets form a content-level cross-reference set. The onboarding flow should install both the selected SRP support and UMA3 before declaring the setup complete, then run validation after both imports. This is an installation-set relationship, not a reason to put UMA3 back in Core.

Before the split:

1. Move `OverlayPainter/Samples/Overlay Painter Document.asset` into the UMA3 payload, or replace all of its UMA3 dependencies with self-contained sample assets.
2. Keep the SRP sample scenes in the selected SRP payload and declare UMA3 as part of their required installation set.
3. Move `Core/Physics/CapsuleCollider/CapsuleColliderScriptPrefab.prefab` into UMA2, or move the referenced behavior into Core if it is genuinely a core feature.
4. Replace the UMA2 texture references in SRP materials with Core/SRP-owned neutral defaults. SRP must never depend on optional legacy content.
5. Add an `UMA2.Content` runtime asmdef referencing `UMA_Core`. Do not rename the existing UMA3 assemblies during this migration.

## Phase 1: Introduce content-aware paths

Add stable path APIs before moving anything:

- `UMA3ContentRoot = "Assets/UMA/UMA3"`
- `UMA2ContentRoot = "Assets/UMA/UMA2"`
- `ResolveUma3ContentPath(relativePath)`
- `ResolveUma2ContentPath(relativePath)`
- `IsUma3ContentInstalled` and `IsUma2ContentInstalled`

Do not resolve content through `UMAPathUtility.InstallAssetRoot`; that root points at the read-only Core package in a UPM installation. Keep `ResolveInstallAssetPath` for Core resources only.

Update release validators, Welcome scene discovery, Global Library/index generation, documentation links, test search roots, and repair tools to use the new content roots. Replace every stored `Assets/UMA2/...` filename with `Assets/UMA/UMA2/...` through Unity serialization APIs. Only edit a native Unity asset as text after positively validating its `%YAML` header; binary/native assets must be preserved or changed through Unity serialization.

## Phase 2: Move the authoring tree without changing GUIDs

In a clean branch and with Unity closed or through `AssetDatabase.MoveAsset`:

1. Move `Assets/UMA2` to `Assets/UMA/UMA2` together with every `.meta` file.
2. Retain `Assets/UMA/UMA3` and all of its existing `.meta` files in place.
3. Remove `Assets/UMA2/package.json` from the imported content payload. A content manifest described below replaces it; the legacy UPM dependency declaration no longer represents the installation model.
4. Open Unity, allow one complete import, and verify there are no GUID changes, duplicate GUIDs, missing scripts, or broken object references.
5. Re-run the dependency audit and require zero Core -> UMA2 references and no accidental Core -> UMA3 references outside explicitly approved SRP/onboarding metadata.

The repository can keep the editable authoring trees at their final `Assets/UMA/...` paths. Release packaging must use staging/allowlists so the Core UPM artifact excludes both raw content folders. `.npmignore` alone is insufficient for every Git-based UPM installation path, so publish Core from a clean staged directory or dedicated release branch containing only the Core allowlist.

## Phase 3: Build deterministic content archives

Create a release builder, modeled on `Build/Build-UMASrpPackages.ps1`, that produces two sibling release artifacts rather than placing them inside Core:

- `UMA3Content-<version>.unitypackage`
- `UMA2Content-<version>.unitypackage`

Build from explicit allowlists rooted at the final install paths. Preserve all existing asset GUIDs and importer metadata. Do not include the parent `Assets/UMA` folder record. Treat the `UMA3`/`UMA2` root folder record carefully: omit it when its GUID could still be registered to an older package install, matching the defensive SRP installer approach.

Each archive needs an owned-content manifest containing at least:

- format version and content ID;
- content version and compatible Core version range;
- exact install root;
- dependencies (`UMA3` requires Core/selected SRP; `UMA2` requires Core, selected SRP, and UMA3);
- every owned path, GUID, byte length, and SHA-256 hash;
- a small required-path set for fast installation checks.

The build must fail on paths outside the intended root, duplicate GUID ownership, missing `.meta` files, references to assets available only in a later/optional package, old `Assets/UMA2` strings, nested `.unitypackage` files, or version drift between artifacts.

## Phase 4: Add safe installation and upgrades

Extend the Welcome flow with separate status cards for UMA3 Content and UMA2 Legacy Content. Support selecting a downloaded archive first; a later release can add an explicitly confirmed download from the matching release URL and checksum catalog.

Use a transactional import flow similar to the SRP installer:

1. Validate archive identity, version compatibility, destination paths, GUID availability, and checksum before import.
2. Reject import while an older UPM package still owns any incoming GUID. For upgrades from the monolithic Core/UMA2 packages, first update/uninstall those packages, wait for Package Manager and AssetDatabase refresh, then import content.
3. Back up the existing destination outside `Assets` under `Library/UMA/ContentInstaller` before replacement so duplicate GUIDs are never imported.
4. Import, validate every required/owned asset, write an installed marker, refresh the Global Library, and roll back on failure.
5. Never silently overwrite project-edited content. Compare installed files with the previous manifest and classify them as unchanged, locally modified, added, deleted, or upstream changed. Default to aborting on conflicts; offer an explicit backup-and-overwrite action and report every affected path.

For projects that already contain `Assets/UMA/UMA3`, adopt the tree after validating its GUIDs rather than reimporting it. For projects containing `Assets/UMA2`, provide a one-time move to `Assets/UMA/UMA2`. Existing project content with unknown ownership must never be deleted merely because it is below a target folder.

## Phase 5: Update tests, documentation, and release automation

Add release gates for these layouts:

1. Core-only UPM install: compiles and opens Welcome with content reported missing.
2. Core + URP + UMA3 and Core + HDRP + UMA3: imports, indexes, opens representative scenes, and builds a player.
3. Either UMA3 layout + UMA2: legacy races/wardrobe load and the UMA2 scripts compile only in `UMA2.Content`.
4. Editable source-tree install at `Assets/UMA`, including both content folders.
5. Upgrade from the old monolithic UPM Core and old `com.umasteeringgroup.uma2` package without GUID redirection into `Packages`.
6. Upgrade with locally modified material/texture content: installer detects the conflict and leaves the project unchanged by default.
7. Content removal: Core still compiles, and tools show actionable missing-content states rather than broken references.

Inspect the staged Core tarball, not just the authoring tree. It must contain neither `UMA3` nor `UMA2` raw content. Inspect both `.unitypackage` archives and confirm every pathname begins with its exact `Assets/UMA/...` destination. Run the existing Package Readiness, release validation, EditMode, and representative PlayMode suites in Unity 6.3 or newer.

Update `PackageInstallation.md`, README/onboarding copy, and release notes with the new three-part install order, archive compatibility rules, editable-content update behavior, and recovery instructions.

## Suggested implementation sequence

Keep the work reviewable in five changesets:

1. Content path APIs and tests, with no asset moves.
2. Reverse-dependency cleanup, UMA2 asmdef, and the GUID-preserving UMA2 move.
3. Deterministic archive builder, manifests, and archive validation tests.
4. Welcome installer, adoption/migration flow, conflict-aware updates, and rollback.
5. Clean Core staging, full install/upgrade matrix, documentation, and release artifacts.

## Completion criteria

The migration is complete when a clean project can install Core through UPM, import UMA3 into `Assets/UMA/UMA3`, optionally import UMA2 into `Assets/UMA/UMA2`, edit their materials/textures directly, and update Core without moving or replacing those edits. Core-only projects must remain compilable, all shipped GUID references must resolve after the supported install sets complete, and content upgrades must never overwrite a locally modified asset without an explicit user decision.
