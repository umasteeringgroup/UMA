# Installing UMA as a Unity Package

UMA Core supports both a traditional source-tree import and a Unity Package Manager installation. Large character-content trees are separate editable asset packages so artists can change supplied materials, textures, recipes, races, and wardrobe directly in the project.

## Supported layouts

- Source tree: `Assets/UMA`
- UPM package: `Packages/com.umasteeringgroup.uma`
- Editable UMA3 content: `Assets/UMA/UMA3`
- Editable UMA2 legacy content: `Assets/UMA/UMA2`

Do not install Core in both Core locations. Duplicate scripts, GUIDs, Resources assets, shaders, and assembly definitions will produce ambiguous imports. The editable UMA3/UMA2 folders are expected to coexist with a UPM Core.

For a local Core package, open **Window > Package Management > Package Manager**, choose **Add package from disk**, and select `package.json` in the staged Core folder. Release automation creates this folder without raw UMA3/UMA2 content; do not use the combined authoring tree as a Git UPM dependency.

UMA supports Unity 6.3 and newer.

## Read-only package content

Content below `Packages` is treated as installed source, not project-owned authoring data. UMA resolves its installed defaults, documentation, samples, and editor resources dynamically. The selected render-pipeline support is a project-owned override at `Assets/UMA/SRP`, because Unity package content is read-only.

Anything UMA creates or modifies is stored below:

`Assets/UMAProjectData`

This includes the project settings override, Global Library index, generated slots and characters, converted assets, tasks, welcome-screen captures, Overlay Painter exports, recovery data, and test artifacts. Utilities that normally create an asset beside the selection redirect to `UMAProjectData` when the selection belongs to a read-only package.

The first package-based use creates project-owned copies of the shipped `UMASettings` and Global Library assets. Those copies are authoritative for that project; package defaults remain unchanged and can be updated safely with the package.

## Editable content packages

Open **UMA > Welcome to UMA > Install / Update UMA Packages**. The same page provides the bundled **Install/Switch UMA URP Support** and **Install/Switch UMA HDRP Support** actions plus file selectors for `UMA3Content-<version>.unitypackage` and optional `UMA2Content-<version>.unitypackage`. UMA3 imports to `Assets/UMA/UMA3`; UMA2 imports to `Assets/UMA/UMA2` after a matching UMA3 version is installed. Both trees are ordinary project assets and can be edited in place.

The installer validates archive identity, exact destination paths, GUID ownership, the declared Core compatibility range, exact package dependencies, and the owned-file manifest before importing. Existing matching source trees can be adopted without replacement. Updates classify every owned path as unchanged, locally modified/deleted/added, and upstream unchanged/changed/added/removed in `Library/UMA/ContentInstaller/<content>/LastChangeReport.txt`. A conflict defaults to cancellation; only the explicit **Back Up and Replace** action proceeds. The previous tree and its root `.meta` are retained below `Library/UMA/ContentInstaller`, and the live content-root GUID is preserved across successful updates and rollbacks.

Archive member names and tar checksums are validated before import. Empty leaf folders are omitted from the release manifest because Unity does not materialize them from a `.unitypackage`; every folder that owns packaged descendants retains its original `.meta` GUID. Pending transaction records are written atomically and bound to both the exact selected archive and manifest hashes, so a restart or changed transaction file cannot accidentally mark an older tree as the new installation. A rollback reason is retained in `Library/UMA/ContentInstaller/<content>/LastError.txt`. If a transaction record is unreadable or a rollback itself cannot finish, UMA blocks another replacement and leaves the transaction backup untouched for manual recovery.

Install order is Core, the selected UMA URP/HDRP support, UMA3 Content, then optional UMA2 Content. The Welcome window reports each state and rebuilds the Global Library after a completed content import.

Core package assets remain read-only. Duplicate a Core-owned default, brush, or preset into an appropriate folder below `Assets` before editing it.

If a tool asks for an output folder, choose an `Assets/...` location. UMA rejects or redirects writable output paths below `Packages`.

## Optional Addressables support

Addressables is intentionally not a package dependency. Projects that do not install Addressables compile without it.

To use UMA Addressables:

1. Install Unity Addressables in the consuming project.
2. Create the project's Addressables settings.
3. Enable **Use Addressables** in **Edit > Project Settings > UMA**. This adds `UMA_ADDRESSABLES`.
4. Allow Unity to recompile, then generate UMA groups from the Global Library.

Addressables runtime, editor, generator, build-window, and sample code live in assemblies constrained by `UMA_ADDRESSABLES`. UMA's normal runtime and editor assemblies contain only dependency-neutral bridges, so the optional package does not leak into `UMA_Core`.

See [Addressables.md](Addressables.md) for the complete workflow.

## Optional FBX Exporter support

FBX export from UMA's prefab saver is also optional. Install Unity's FBX Exporter package and add `UMA_FBX_EXPORT` to the consuming project's scripting define symbols. The exporter implementation lives in the constrained `UMA_FBX_Editor` assembly; `UMA_Core_Editor` calls it through a dependency-neutral bridge. Projects without FBX Exporter therefore compile without installing it.

## Package dependencies for an Assets/UMA installation

A traditional `.unitypackage` cannot acquire Unity Registry dependencies automatically. Before importing UMA into a minimal project, install the required packages listed below through **Window > Package Management > Package Manager**. After UMA imports, **UMA > Package Dependencies** shows their current status and offers explicit, confirmed install actions.

Required by the current full UMA distribution:

- Burst, Collections, Jobs, and Mathematics for UMA's jobified mesh pipeline.
- Input System for the supplied input actions and character controllers.
- Timeline for UMA's race, wardrobe, color, and DNA tracks.
- Unity UI for UMA runtime UI and supplied samples.

The dependency window never changes the project silently. Pressing an install button explains which package will be added to `Packages/manifest.json` and asks for confirmation first.

### Optional 2D Sprite integration

Unity's **2D Sprite** package is optional. Overlay Painter's normal painting, sprite use, and sprite-set consumption do not require it. Only **Set Sprite Grid Options** needs the package to create or read Unity sprite rectangles.

The package-specific implementation lives in `UMA.TexturePaint.Sprite.Editor`, which is activated automatically by an assembly `versionDefines` check for `com.unity.2d.sprite`. The main Overlay Painter assembly contains only a dependency-neutral bridge. Without 2D Sprite, UMA still compiles and the Sprite Grid window provides an **Install 2D Sprite** action instead of exposing unusable controls. No scripting define needs to be maintained manually.

### Optional Test Framework integration

Unity's **Test Framework** is optional for consumers. Race smoke-test logic, validation reports, and repair tools remain compiled without it; only launching EditMode tests and running Asset Validation from its window require the package. The `UMA.TestRunner.Editor` backend and every UMA test assembly are enabled automatically through a `com.unity.test-framework` version define. If it is absent, testing commands open the dependency window instead of causing missing-assembly errors.

Some compatible Collections releases declare Test Framework and Performance Testing as their own transitive dependencies. Unity may therefore install them even though UMA does not request them directly. They remain isolated from UMA production assemblies and can be removed only when the selected Collections version no longer requires them.

### Render-pipeline content

UMA ships `UMAURP.unitypackage` and `UMAHDRP.unitypackage` in its `SRP` folder. On first installation, the Welcome window requires one of them to be installed. The selected archive replaces or creates `Assets/UMA/SRP`:

- An Asset Store import or copied `Assets/UMA` folder keeps its selected support inside that UMA folder.
- A UPM/package.json installation keeps core UMA read-only below `Packages` and uses `Assets/UMA/SRP` as its writable project override.

The installer verifies that the corresponding Unity URP or HDRP package is present and that the selection matches the active Render Pipeline Asset. It validates the bundled archive before replacing any files. Source-tree installations preserve both bundled installer archives, while UPM installations continue reading them from the package under `Packages`.

Each installed support folder contains a content manifest. UMA validates every owned path, writes a pipeline-specific installation marker, and compares that marker with the bundled archive so later UMA updates can offer a one-click SRP update. A manually copied pre-manifest URP or HDRP folder is recognized from several pipeline-specific files and can be enrolled in update detection by reinstalling the matching support package once.

All shipped UMA 3 sample scenes are owned by both SRP archives and are installed below `Assets/UMA/SRP/Samples/Scenes` together with the matching `U3Environment` prefab. Core UMA therefore contains no active scene that can reference a not-yet-installed pipeline asset. Before a pipeline is selected, the Welcome window shows the scenes but keeps their Open buttons disabled; installing URP or HDRP makes the complete matching scene set available atomically. The archive build assigns deterministic install-only GUIDs to the moved sample payload and rewrites its internal references. This prevents Unity from redirecting an import to a stale `Packages/.../UMA3/Scenes` GUID mapping when a local UPM package is upgraded in place. Rebuilding the Global Library after selecting a pipeline indexes the newly installed sample content using those final project paths and GUIDs.

Installation transactions and rollback data are stored below `Library/UMA/SrpInstaller`, so an interrupted import can recover after an editor restart. Transaction paths and archive hashes are revalidated before use. A failed rollback retains `CurrentBackup` and the pending record instead of deleting the only recovery copy; the immediately previous `SRP` folder is retained as `PreviousBackup` after a successful replacement.

Only one UMA render-pipeline support package is required. UMA core does not directly depend on either Unity URP or Unity HDRP: install the Unity pipeline package selected by the project, then install its matching UMA support. URP-only projects do not need HDRP, and HDRP-only projects do not need URP.

Addressables, FBX Exporter, 2D Sprite, Test Framework, and performance-test tooling are optional integrations and are deliberately absent from UMA's required UPM dependency list.

## Package dependencies

The manifest contains only packages used directly by UMA runtime code, editor tooling, shaders, or samples. Addressables, FBX Exporter, 2D Sprite, Test Framework, and performance testing are optional and are not installed automatically.

UMA core contains the bootstrap archives rather than an active SRP content tree. The Welcome window installs the selected content into `Assets/UMA/SRP`; UMA's shader refresh and legacy SRP path resolution use that override in both source-tree and UPM layouts.

Maintainers rebuild both archives with `Build/Build-UMASrpPackages.ps1`. The build injects the shared scene payload from `Build/SRPScenePayload`, deterministically migrates its former core GUIDs, normalizes GUIDs shared by URP and HDRP, removes opposite-pipeline material references and the historical orphaned URP hair-pass material from HDRP, writes the content manifests, and rejects missing scenes, nested installers, path leakage, GUID drift, and references that exist only in the other archive. Unity assets are treated as text only after their `%YAML` header is validated. Native binary scenes and assets receive only byte-level GUID substitutions, retain their original serialization format and length, and are compared byte-for-byte with the expected transformed payload before either archive is accepted. Every Unity package `pathname` record is written as the exact UTF-8 asset path without a trailing line ending; surrounding whitespace and control characters are rejected because Unity can otherwise retain a Windows carriage return as part of the destination filename. The archives deliberately omit the `Assets/UMA/SRP` folder record itself: including its shipping-folder GUID would make Unity redirect a UPM import back into the read-only package instead of creating the project-owned override.

Maintainers build UMA3/UMA2 content and the publishable Core staging tree with `Build/Build-UMAContentPackages.ps1`. By default, the versioned UMA3/UMA2 archives and `ReleaseManifest.json` are published to `Build/Content`, while the publishable Core tree is staged at `Build/CorePackage`. It preserves source asset bytes and GUIDs, hashes assets and importer metadata, writes versioned ownership manifests, rejects missing `.meta` files, non-canonical archive paths, ASCII-serialized reverse dependencies, cross-package GUID ownership, stale archives/source trees, stale `Assets/UMA2` paths, dependency/version drift, nested installers, and pipeline-specific serialized references in Core or either content tree, and validates both generated archives by expanding them again. The required disposable Unity validation then resolves each asset's direct dependencies through Unity serialization, covering native binary references that cannot safely be decoded or rewritten as text. UMA3 and UMA2 materials may reference only SRP assets whose paths and GUIDs are shared by both the URP and HDRP support archives; pipeline-only material metadata, renderer components, and project render-pipeline settings must remain outside Core or in the matching SRP archive. The shipped Core Global Library is intentionally empty and is rebuilt after content installation. Both content manifests therefore declare the selected SRP support as an exact dependency. Archive paths, timestamps, ownership, and ordering are normalized so identical inputs produce byte-identical archives. Core staging is built and hash-verified in a sibling temporary directory, then atomically swapped into place; an existing UMA Core staging tree is retained as a timestamped backup rather than deleted. Core must be published from the generated staging directory; `.npmignore` is an additional safeguard, not the Git-package boundary.

Obsolete UMA2 HDRP demos, old getting-started scenes, and unreferenced generated prefabs with deleted source dependencies are preserved below `Assets/UMA/Temp/LegacyUMA2Content` for source history and manual recovery. That authoring-only tree is excluded from Core and from the UMA2 content archive. The retained `Assets/UMA/UMA2` payload is the supported legacy-content boundary and must pass the complete serialized-GUID audit in both URP and HDRP validation layouts.

Run `Build/Validate-UMAContentMatrix.ps1` after building the artifacts. It creates separate disposable Unity 6.3 Core-only, legacy-package-ownership, URP, and HDRP projects with staged Core embedded below `Packages`. The ownership gate proves that a GUID still registered to an older package is rejected. The full layouts drive the real content installer through fresh UMA3/UMA2 imports, same-version updates, root-GUID checks, conflict abort, backup-and-replace, legacy `Assets/UMA2` migration, Global Library race indexing, UMA3/UMA2 wardrobe discovery, UMA2 assembly compilation, complete SRP serialized-GUID resolution, representative scene opening and player builds, and content removal. `Build/Validate-UMAContentInstall.ps1` remains the single-layout entry point. Validation projects are restricted to the repository `tmp` folder, reject reparse-point targets, and preflight archive members before the validation helper extracts selected SRP content. For a same-pipeline rerun after a content-only correction, `-ReuseLibrary` retains the disposable project's Unity import/shader cache while deleting all prior UMA installer and smoke-test state, so the lifecycle still begins with a fresh install.

The validation timeout defaults to 240 minutes per layout. Unity 6.3 can spend more than two hours preparing shader variants during the first representative URP or HDRP player build even when later stripping removes most variants; a timeout should therefore be treated as an incomplete build gate, not as a successful validation.

Every packaged `UMAMaterial` must resolve its active Unity material to content in the same archive. HDRP keeps the default `_material` and its dependency when `_HDRPMaterial` is empty, matching UMA's runtime fallback behavior. The archive build and the editor-side preflight validator both reject an UMA material with no usable active-pipeline material.

## Running UMA package tests

Unity does not expose tests contained in an installed package by default. To run UMA's tests from Test Runner or batch mode, add the package name to the consuming project's `Packages/manifest.json`:

```json
"testables": [
  "com.umasteeringgroup.uma"
]
```

This is needed only for development and release validation; normal UMA consumers should omit it.

## Moving an existing project to UPM

1. Commit or back up the project.
2. Close Unity.
3. Copy `Assets/UMA/UMA3` and `Assets/UMA/UMA2` (including `.meta` files) to a safe temporary location outside `Assets`.
4. Remove the remaining imported `Assets/UMA` Core only after confirming it is versioned or recoverable.
5. Install the same Core version through Package Manager and let Unity finish refreshing.
6. Restore the two content folders to their original `Assets/UMA/...` paths, or import the matching content archives. If upgrading from `com.umasteeringgroup.uma2`, uninstall that old UPM package and wait for its GUIDs to unregister first.
7. Use **Content Packages** in the Welcome window with the matching archives to adopt restored trees and establish their ownership manifests.
8. Open **Edit > Project Settings > UMA**, verify the package and project-data paths, then rebuild the Global Library.
9. Validate representative races, wardrobe, builds, Overlay Painter save/export, and player builds.

Existing project-authored UMA content elsewhere below `Assets` remains project content. Do not move it into `Packages`.

## Troubleshooting

### A tool tries to write under Packages

Use an `Assets/...` destination. Built-in UMA defaults should redirect to `Assets/UMAProjectData`; report any remaining tool that attempts to modify package content.

### Scripts appear twice or types are ambiguous

Check that only one UMA install exists. Remove either the `Assets/UMA` import or the UPM installation, then reimport.

### Settings or the Global Library reset after an update

Verify the project-owned assets exist in `Assets/UMAProjectData/Resources`. UMA loads those overrides before the package defaults.

### Addressables menus are unavailable

Install Addressables, create its settings, enable `UMA_ADDRESSABLES` through UMA Project Settings, and wait for compilation. Addressables is deliberately absent from a default UMA install.
