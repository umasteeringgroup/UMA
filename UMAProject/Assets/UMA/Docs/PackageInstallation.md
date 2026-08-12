# Installing UMA as a Unity Package

UMA supports both a traditional source-tree import and a Unity Package Manager installation. The natural package boundary is the `UMA` folder itself: its `package.json` is at the same level as `Core`, `UMA3`, `OverlayPainter`, `SRP`, and `Docs`.

## Supported layouts

- Source tree: `Assets/UMA`
- UPM package: `Packages/com.umasteeringgroup.uma`

Do not install the same UMA checkout in both locations. Duplicate scripts, GUIDs, Resources assets, shaders, and assembly definitions will produce ambiguous or duplicate imports.

For a local package, open **Window > Package Management > Package Manager**, choose **Add package from disk**, and select the `package.json` inside the `UMA` folder. A Git dependency may use the repository URL with the query fragment/path that selects the `UMA` package folder.

UMA supports Unity 6.3 and newer.

## Read-only package content

Content below `Packages` is treated as installed source, not project-owned authoring data. UMA resolves its installed assets dynamically, so shaders, defaults, documentation, samples, and editor resources work from either supported layout.

Anything UMA creates or modifies is stored below:

`Assets/UMAProjectData`

This includes the project settings override, Global Library index, generated slots and characters, converted assets, tasks, welcome-screen captures, Overlay Painter exports, recovery data, and test artifacts. Utilities that normally create an asset beside the selection redirect to `UMAProjectData` when the selection belongs to a read-only package.

The first package-based use creates project-owned copies of the shipped `UMASettings` and Global Library assets. Those copies are authoritative for that project; package defaults remain unchanged and can be updated safely with the package.

## Editing supplied content

Do not edit a package asset in place. Duplicate the race, recipe, slot, overlay, material, brush, preset, or sample into an appropriate folder below `Assets`, then edit the copy. Keep the package asset when your copy still references it.

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

URP and HDRP support consists of supplied pipeline-specific shaders, materials, and samples; UMA runtime assemblies do not directly reference URP or HDRP script assemblies. Install only the render pipeline used by the project. The dependency window lists both integrations independently. Built-in projects do not need to install both pipelines merely to compile UMA code.

Addressables, FBX Exporter, 2D Sprite, Test Framework, and performance-test tooling are optional integrations and are deliberately absent from UMA's required UPM dependency list.

## Package dependencies

The manifest contains only packages used directly by UMA runtime code, editor tooling, shaders, or samples. Addressables, FBX Exporter, 2D Sprite, Test Framework, and performance testing are optional and are not installed automatically.

UMA includes Built-in, URP, and HDRP material/shader content, so the relevant Unity 6.3 render-pipeline packages are declared. Projects may choose which supplied UMA material assets they actually use, but an installed UMA package remains importable as a complete distribution.

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
3. Remove the imported `Assets/UMA` copy only after confirming it is versioned or recoverable.
4. Install the same UMA version through Package Manager.
5. Open Unity and let it reimport/recompile.
6. Open **Edit > Project Settings > UMA** and verify the package install root and project data paths.
7. Rebuild or repair the Global Library project copy.
8. Validate representative races, wardrobe, builds, Overlay Painter save/export, and player builds.

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
