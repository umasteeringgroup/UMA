# UMA editor plugins

Launcher API version 1 lives in the editor-only `UMA_Editor` assembly. Overlay Painter, Hair Card Editor and Dismemberment are optional packages. Overlay Painter's generator/filter Plugin API v2 remains a separate API.

Every plugin distributes **Examples** and **Tests** as separate companion packages. The welcome
page lists compact, single-line rows with those companions indented immediately below their
parent. Install the parent first. Remove its companions before removing the parent; normal
plugin use does not require examples or Unity Test Framework.

## Overlay Painter installation and use

Open **UMA > Welcome to UMA**, then select **Plugins**, the second navigation button. The table lists one row for every plugin in UMA's package catalog, its installation status and version, and **Install** or **Reinstall** and **Remove** actions. Click **?** for a description and dependencies. Installation searches `Build/Plugins`, the last used package location, the project `Plugins` folder, and the UMA install root's `Plugins` folder. It chooses the highest compatible validated version; if none is found, a file picker asks for the matching `.unitypackage`. Status refreshes automatically after package operations. If removal is unavailable, hover over its status or Remove button to see why; dependent packages must be removed first, and unmanaged source files require adoption before managed removal.

Select a scene avatar or a slot with mesh data, then use **Editors > Plugins > Overlay Painter** above the inspector controls. Existing painter menus remain available. Avatar launch is disabled for prefab assets and Prefab Mode.

Settings are under **Project Settings > UMA > Overlay Painter**. First access copies the five legacy UMA settings to `Assets/UMAProjectData/OverlayPainter/Settings.asset`; this asset becomes authoritative. Deliberate settings changes also update the legacy scalar fields for compatibility. Settings, documents, exports, and recovery live outside the installed plugin folder.

Use **Install** or **Reinstall** on **Welcome to UMA > Plugins** to install or update an archive. Managed installation validates paths, GUIDs, hashes, API version, core compatibility, and dependencies before import. Modified files use the existing backup-and-replace workflow. Unowned files survive updates. Backups and interrupted-import records live under `Library/UMA/ContentInstaller`.

The corresponding **Remove** command removes unchanged manifest-owned files while preserving modified and unowned files. Remove examples and tests first. Removal recovery and its last report live under `Library/UMA/PluginRemoval`. A source checkout without an installed manifest must first be adopted using an archive built from the same files.

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

## Build plugin packages

Choose **UMA > Build > Build Plugin Packages**. Unity builds all installed known plugin sources into `Build/Plugins/<Plugin>-<version>.unitypackage` and reveals the output folder. No PowerShell installation is needed. Each archive includes a manifest with GUIDs, hashes, API requirements and dependencies, and is validated before replacing an existing output. Plugin payloads and installed ownership manifests are not modified by building. Missing plugins are skipped; missing required files stop that plugin's build with an error.

Both builders read the saved, installed **UMASettings** asset and synchronize Core's `package.json` before building. The actual release label is stored as `umaVersion` in package metadata and each archive manifest; the installer displays this label. For example, `UMA NextGen 3.1f2` produces display version `3.1f2` and semantic package version `3.1.2`. Alpha and beta labels preserve their stage, such as `3.2b3` becoming `3.2.0-beta.3`. A missing, transient or unsupported settings version stops the build rather than falling back to an older version. The PowerShell builder validates the settings asset's Unity YAML header; use the Unity builder if settings are stored as a native asset.

For repository release builds, `tools/Packaging/Build-UMAPluginPackages.ps1` also builds Hair Cards, validates package ownership, and stages Core with all optional plugins excluded. Use `-SkipCore` to build only the plugin archives.

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
