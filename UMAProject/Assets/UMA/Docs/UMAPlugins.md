# UMA official plugins

Launcher API version 1 lives in the editor-only `UMA_Editor` assembly. UMA2Compatibility, Overlay Painter, Hair Card Editor and Dismemberment are official optional packages. Overlay Painter's generator/filter Plugin API v2 remains a separate API.

Every plugin distributes **Examples** and **Tests** as separate companion packages. The welcome
page lists compact, single-line rows with those companions indented immediately below their
parent. Install the parent first. **Remove All** on the parent removes its installed companions first, then the parent; normal
plugin use does not require examples or Unity Test Framework.

**Install All Plugins** asks once for the entire batch, including Examples and Tests.
It skips packages already installed, registers matching unmanaged content, and backs up
content that needs replacement while preserving extra files and separately installed companions.
The approval survives script reloads and downloaded-package callbacks. Individual Install/Reinstall
actions retain their normal confirmations. A final dialog lists every package as installed,
already installed, failed/cancelled, or not attempted. A failed or cancelled installation stops
the queue and keeps successful earlier installations. The report is also saved to
`Library/UMA/ContentInstaller/LastPluginInstallReport.txt`.

## Overlay Painter installation and use

Open **UMA > Welcome to UMA**, then select **Plugins**, the second navigation button. The table lists one row for every plugin in UMA's package catalog, its installation status and version, and **Install** or **Reinstall** and **Remove** actions. Click **?** for a description and dependencies. Installation searches `Build/Plugins`, the current release's download cache, the last used package location, the project `Plugins` folder, and the UMA install root's `Plugins` folder. It chooses the highest compatible validated local version; if none is found, it downloads the package from the matching UMA GitHub release. Status refreshes automatically after package operations. If removal is unavailable, hover over its status or Remove button to see why; dependent packages must be removed first, and unmanaged source files require adoption before managed removal.

The download rule is `https://github.com/umasteeringgroup/UMA/releases/download/V{UMAVersion}/{unitypackagename}`.
For UMA **3.1f2**, Dismemberment downloads from release **V3.1f2** using the filename
**Dismemberment-3.1.2.unitypackage**. The release comes from the installed UMASettings asset;
the filename uses the same version conversion as the package builder. This applies to
all official plugins and their separate Examples and Tests packages.

Install and Reinstall immediately open a progress dialog while UMA searches and
validates local packages. It identifies the current operation, including unpacking
and hashing, checking installed files for changes, and backing up existing content.
The bar describes the current operation, not an estimate of total installation time.
Unity displays its own progress during package import. Large local packages can
still take time to validate even when no download is needed.

Downloads show progress and can be cancelled. Files are streamed to
`Library/UMA/PluginDownloads/V{UMAVersion}` outside `Assets`. A temporary download is
never imported or offered as a local package: only a completed archive matching the
requested plugin and release, with valid manifest hashes, becomes a cached package.
The usual dependency checks, installation confirmation, and local-change protection
still apply. Cancellation or an assembly reload discards the partial download.

If the network is unavailable, the release file is missing, or validation fails, the
Plugins page explains the failure and displays the release download page and expected
filename. Use **Retry**, **Open Download Page**, or **Locate Local Package** to continue,
or **Dismiss**. A compatible local package remains usable offline.

Select a scene avatar or a slot with mesh data, then use **Editors > Plugins > Overlay Painter** above the inspector controls. Existing painter menus remain available. Avatar launch is disabled for prefab assets and Prefab Mode.

Settings are under **Project Settings > UMA > Overlay Painter**. First access copies the five legacy UMA settings to `Assets/UMAProjectData/OverlayPainter/Settings.asset`; this asset becomes authoritative. Deliberate settings changes also update the legacy scalar fields for compatibility. Settings, documents, exports, and recovery live outside the installed plugin folder.

Use **Install** or **Reinstall** on **Welcome to UMA > Plugins** to install or update an archive. Managed installation validates paths, GUIDs, hashes, API version, core compatibility, and dependencies before import. Modified files use the existing backup-and-replace workflow. By default, unowned files survive updates. The conflict dialog also offers **Replace everything** with an optional **Also remove extra files from this package's folder** checkbox. Leave it off to preserve extras; enable it for a clean replacement within the selected package folder. Separately installed companions stay intact in both modes. **Back Up and Replace** always preserves extras, and both replacement choices keep a recovery backup. **Review Report** opens the report without closing the dialog. Backups and interrupted-import records live under `Library/UMA/ContentInstaller`.
Each build generates a fresh manifest inside the package, including the current asset
and importer hashes. Building does not overwrite the project's installed manifest:
that remains the baseline from the last installation. Reinstall compares local files
with both that baseline and the incoming package, so local edits already included in
your rebuild do not trigger a conflict warning. Different local edits remain protected.
Empty folders are omitted from packages because Unity does not reliably import empty
leaf folders. Reinstall retains unowned empty folders and their metadata without
reporting them as content conflicts.

The parent's **Remove All** command asks for one confirmation and removes installed Examples and Tests before the main plugin. Companion rows retain **Remove** for individual removal. All selected manifests are validated before removal starts. The confirmation offers **Continue**, **Cancel**, and **Continue - Remove All**. Continue removes only unchanged package-owned files. Continue - Remove All also deletes modified package-owned files and their metadata, applying to every listed companion and parent. It cleans up empty folders from the deepest children upward, including parents and the package root when they become empty. Unowned files and user settings, documents, recovery, and exports remain protected with either choice. The selected policy is saved for interrupted-session recovery. The complete sequence is saved under `Library/UMA/PluginRemoval` and resumes after an interrupted Editor session. A source checkout without an installed manifest must first be adopted using an archive built from the same files.

Direct UnityPackage import requires UMA with launcher API 1 already installed. Archive versions are derived from the installed UMASettings release; see the build instructions below for the display and semantic version formats. The supported Unity baseline is 6.3.

## Hair Card Editor installation and use

**UMA > Welcome to UMA > Plugins** includes **Hair Card Editor**. Its package installs to `Assets/UMA/HairCards`; that folder and its metadata are excluded from UMA Core. Existing script and asset GUIDs are preserved. Core contains no Hair Cards type references or dedicated launcher.

Select a scene avatar and use **Editors > Plugins > Hair Card Editor**. The existing **UMA > Hair Cards** menus and mesh/groom asset workflows remain available. Prefab assets and Prefab Mode cannot launch avatar authoring. Hair Cards includes its core data, runtime support, editor and shaders. The existing UMA render pipeline package supplies the hair atlas textures and optional URP hair shaders.

**Hair Card Examples** contains the authored content under `Assets/UMAProjectData/HairCards/Examples`.
**Hair Card Tests** owns `Assets/UMA/HairCards/Editor/Tests` and requires Unity Test Framework.
The main Hair Cards archive excludes both. Reinstalling an older parent that bundled tests
preserves the existing tests during the transition to companion ownership.

The Documentation Browser's **Hair Card Editor** selection contains the quick start and hairstyle guides. Installing registers its descriptor; removing the plugin removes that documentation registration. User grooms, baked assets and recovery under `Assets/UMAProjectData/HairCards`, and preferences under `UserSettings`, survive removal. The separately installed Examples package owns only its listed example assets; modified examples are preserved during removal. Editable grooms and runtime groom components require the plugin's types to load; reinstalling restores those types. Baked ordinary Unity/UMA assets do not require the authoring editor.

## Dismemberment installation and use

Install **Dismemberment** from the Plugins page. Its runtime, editor, shared cap shader and
documentation live under `Assets/UMA/UMADismemberment`, excluded from UMA Core. The existing
GUIDs and assembly names are retained. **Editors > Plugins > Dismemberment** on a scene avatar
adds or selects its configuration. Its documentation appears separately in the Documentation Browser.

**Dismemberment Examples** owns `Samples`; **Dismemberment Tests** owns `Tests`. The parent
has no dependency on either. UMA core has no dismemberment assembly dependency and compiles
without the plugin. Project-authored scripts or scenes that use its components still require it.
The `U3-GoreExample` scene and its lighting assets are now in the Examples package, rather than
SRP's general samples. Examples requires UMA3 Content and SRP Support; Tests requires Unity
Test Framework. The cap shader is part of the main plugin, so runtime cuts work without Examples.

## UMA2Compatibility installation and use

Install **UMA2Compatibility** from **UMA > Welcome to UMA > Plugins** when a project needs the
legacy UMA 2 races, DNA, expressions, animations and random sets.
It packages the main `Assets/UMA2` content with existing asset GUIDs and the `UMA2.Content`
assembly preserved. It supplies legacy content for current UMA on Unity 6.3+, rather than the
old UMA 2 engine. Projects using only UMA 3 content do not require it.

The parent requires Core, UMA 3 Content and the selected render-pipeline support. Select a
legacy race on an avatar and use wardrobe recipes compatible with that race. Some historical
materials also require the **UMA 2.X Shader Packages** offered by Welcome's shader page.
Installing compatibility content does not automatically convert older clothing to UMA 3.

**UMA2Compatibility Examples** installs legacy clothing, hair, tattoos, utility-slot samples, male and female sample base recipes and a setup guide
to `Assets/UMA2CompatibilityExamples`. **UMA2Compatibility Tests** installs optional archive,
ownership, path-resolution and documentation tests to `Assets/UMA2CompatibilityTests`. Both
are separate packages, shown directly beneath the parent. The parent works without Examples: resources required by its base characters live in `Assets/UMA2/Races/HumanShared/BaseResources`. Example utility scripts retain their original assembly through an assembly reference.

The Documentation Browser lists **UMA2Compatibility** separately; its guides and the original
cloth-upgrade PDF live in `Assets/UMA2/UMA2Docs`. Installation registers the guide descriptor
and removal unregisters it. The browser displays Markdown; the PDF can be opened as a project
asset. See the [compatibility guide](../../UMA2/UMA2Docs/UMA2Compatibility.md).

The stable package ID remains `uma2`. The new archive is
`Build/Plugins/UMA2Compatibility-<version>.unitypackage`. An existing source tree can be adopted
by reinstalling a matching archive. Previous `Assets/UMA/UMA2` installations can be moved by the
installer with their GUIDs preserved; competing trees require consolidation first. Remove
Examples and Tests before the parent. Modified and unowned files are preserved, but characters
referencing removed legacy assets require reinstalling the plugin.

## Build plugin packages

Choose **UMA > Build > Build Plugin Packages**. Unity builds all installed known plugin sources into `Build/Plugins/<Plugin>-<version>.unitypackage` and reveals the output folder. No PowerShell installation is needed. Each archive includes a manifest with GUIDs, hashes, API requirements and dependencies, and is validated before replacing an existing output. Plugin payloads and installed ownership manifests are not modified by building. Missing plugins are skipped; missing required files stop that plugin's build with an error.

Both builders read the saved, installed **UMASettings** asset and synchronize Core's `package.json` before building. The actual release label is stored as `umaVersion` in package metadata and each archive manifest; the installer displays this label. For example, `UMA NextGen 3.1f2` produces display version `3.1f2` and semantic package version `3.1.2`. Alpha and beta labels preserve their stage, such as `3.2b3` becoming `3.2.0-beta.3`. A missing, transient or unsupported settings version stops the build rather than falling back to an older version. The PowerShell builder validates the settings asset's Unity YAML header; use the Unity builder if settings are stored as a native asset.

For repository release builds, `tools/Packaging/Build-UMAPluginPackages.ps1` builds all installed official plugins and their separate companions, validates package ownership, and stages Core with all optional plugins excluded. Use `-SkipCore` to build only the plugin archives.

To build only the compatibility plugin and its companions:

```powershell
./tools/Packaging/Build-UMAPluginPackages.ps1 -SkipCore -PluginIds @('uma2', 'uma2-compatibility-examples', 'uma2-compatibility-tests')
```

Selected builds write `packages-selected.json`; full builds retain their `packages.json` report.

## Register an inspector action

Reference `UMA_Editor` and the required runtime assemblies from an editor-only asmdef.

```csharp
using UMA;
using UMA.Editors;
using UnityEditor;

internal static class MySlotActions
{
    [UMAPlugin(typeof(SlotDataAsset), "Inspect slot", Order = 100,
        ValidateMethod = nameof(CanInspect))]
    private static void Inspect(Editor editor)
    {
        // Open your normal EditorWindow using editor.target.
        EditorGUIUtility.PingObject(editor.target);
    }

    private static bool CanInspect(Editor editor) => editor.target != null;
}
```

Actions are synchronous, nongeneric `static void Method(Editor)`. Validators are side-effect-free `static bool Method(Editor)` methods on the same class, checked again before invocation. Use `editor.target` / `editor.targets`, including in locked inspectors, rather than global Selection.

`IncludeDerived` defaults to true. `SupportsMultipleTargets` defaults to false. Every selected target must match a registration. Repeat the attribute to cover several types with one action, keeping label, tooltip, order, validator, and multiple-target metadata identical. Ordering is deterministic; invalid registrations are isolated and logged.

The dispatcher commits serialized edits, invokes once on a subsequent editor callback, and refreshes the inspector. Stale targets, host lifetimes, registry generations, and play-mode transitions cancel queued work. Plugin code owns Undo recording, asset saving, window lifecycle, and event cleanup.

## Custom hosts and buffered data

Normal inspectors use Unity's default header hook. Hosts that omit the header register `UMAPluginGUI.RegisterExplicitHost(editor)` during setup, retain its disposable lease, call `UMAPluginGUI.Draw(editor)` once before the inspector body, and dispose the lease during teardown. This prevents duplicate automatic placement.

Buffered inspectors implement `IUMAPluginHost`: `CanRunPluginActions`, `TryPreparePluginAction()`, and `RefreshAfterPluginAction()`. Windows can supply an adapter as the registration's second argument. The adapter coordinates all buffers it owns. A competing adapter for the same Editor is rejected.

UMA's recipe, wardrobe, DCA, and embedded UMAData inspectors have explicit integration. Recipe hosts commit pending edits before launch and reload after action edits. External revisions conflicting with local edits disable launch until resolved. Closing a conflicting inspector preserves its working copy as JSON under `Library/UMA/PluginRecovery` instead of overwriting the external revision. Existing `IUMARecipePlugin` inline extensions retain their compatibility bridge.

## Register plugin documentation

The Documentation Browser starts with **UMA** selected. Its top dropdown lists installed plugins with registered Markdown documentation; selecting a plugin shows only that plugin's guides. Favorites and document ordering remain available. The Plans browser remains separate.

Ship a `UMAPluginDocumentation.json` file in the plugin's root, alongside its `.meta` file, for example:

```json
{
  "pluginId": "overlay-painter",
  "displayName": "OverlayPainter",
  "documentationFolder": "Documentation"
}
```

Use the same stable plugin ID as the package manifest. The documentation folder is relative to this descriptor and must be inside the plugin. Importing the descriptor registers it automatically. The project registry is saved at `ProjectSettings/UMAPluginDocumentation.asset` and tracks descriptor and folder GUIDs, so folder moves retain the registration. Existing installations can register explicitly with `UMAPluginDocumentationRegistry.RegisterDescriptor(assetPath)` from `UMA_Editor`.

UMA's package installer registers documentation after installation; its uninstaller calls `UMAPluginDocumentationRegistry.Unregister(pluginId)` even when modified plugin files are preserved. Custom uninstallers should make the same call. Deleting the descriptor or its documentation folder also prunes the registry. Removing the selected plugin switches the browser back to UMA. Registration is driven by imports, so intentionally unregistered, preserved files are not rediscovered on every editor restart; importing the descriptor again registers a reinstall.

## Build and validate

Run from the Unity project root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Packaging/Build-UMAPluginPackages.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Packaging/Validate-UMAPlugins.ps1
```

The builder writes `Build/Plugins/OverlayPainter-<version>.unitypackage`, separate examples/tests archives, and a hash report. Core staging at `Build/CorePackage` excludes all three optional roots. Archive ownership is validated and shared core/plugin GUIDs are rejected. Native assets are copied byte-for-byte. Painter resources follow a plugin-owned script GUID independently of the core installation location.

The validator synchronizes the current staged core into an isolated project under `tmp`, verifies Unity and the actual UMASettings version, and checks absence, installation, discovery, resource relocation, update, removal, and reinstall. It checks modified/unowned file preservation and authored-data GUIDs across the cycle. Scripts are versioned under `tools/Packaging`; build artifacts stay outside source control.

Documentation registry validation (2026-10-05): tested directly in `C:\GitHub\UMA\UMAProject`, Unity `6000.3.18f1`, installed UMASettings `UMA NextGen 3.1f1`. All eight `UMAPluginDocumentationTests` passed: automatic import registration, duplicate registration, explicit unregister with document preservation, deleted-descriptor cleanup, folder relocation, invalid paths, default UMA selection, separate plugin contents (including a plugin nested inside Docs), and selection fallback. Compilation reported zero errors/warnings. OverlayPainter remains installed; its real uninstall is reserved for the user's later check.
