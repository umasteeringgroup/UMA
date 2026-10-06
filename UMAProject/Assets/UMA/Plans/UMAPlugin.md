# UMAPlugin implementation plan

Status: API version 1 implemented. See [implementation and usage](../Docs/UMAPlugins.md).
Date: 2026-09-24.
Fitness review: 2026-10-04; revised for the [Plugin Migration Plan](Plugin%20Migration.MD).
Reviewed checkout: `C:\GitHub\UMA\UMAProject`, UMA NextGen 3.1f1, Unity 6000.3.18f1.
Supported baseline: Unity 6.3 and newer.
Original review method: source and assembly inspection plus Unity 6.3 API documentation. Implementation followed the revised contract on 2026-10-04. The specification below records the design; current validation results are recorded in the migration plan.

## Goal

Provide a small editor extension system that lets a plugin register one or more buttons for the type of item being edited. Matching buttons appear in a **Plugins** group inside an **Editors** area at the top of the editor, in both Standard View and Advanced View. Omit the complete area, including its spacing, when there are no applicable plugins.

Registration must work for any inspected object type, including types from another package, rather than maintaining a fixed list of UMA asset types. Plugin authors retain normal access to UMA and Unity APIs. The system provides discovery, target matching, and button placement; plugin tools use Unity's existing windows, drawing, updates, serialization, Undo, and events.

Interpretation of the requested layout: **Editors** is the outer area and **Plugins** is the group containing extension buttons. It sits below Unity's mandatory object/component header, above the view selector and ordinary editable content. Existing built-in editing commands remain usable when no plugins are installed; hiding the new area must not hide those commands.

## Fitness verdict

The attributed action design is suitable for optional tool launchers. It can replace the hardcoded OverlayPainter launches and support HairCards, ClothingConformer, Dismemberment authoring, Timeline utilities, and advanced mesh tools without dependencies from core to those packages. It needs the host and compatibility contracts specified in this revision before implementation.

| Migration requirement | Review outcome |
| --- | --- |
| Install a compatible package and discover its tools automatically | Keep TypeCache discovery in the editor-only foundation; no per-project registration asset or manual scripting define is required. |
| Remove an optional package while ordinary UMA continues working | Keep dependencies directed from plugins to the foundation. Installation, file ownership, dependency checks, and removal belong to PackageSupport, as planned in the migration document. |
| Preserve painting workflows on avatars and slots | The static action and validator can express the existing entry points, including scene/Prefab Mode and mesh restrictions. Validators remain plugin-owned. |
| Protect unsaved inspector/window state and subsequent plugin edits | Add a small host synchronization interface with an explicit window-adapter hook. Generic SerializedObject apply/repaint cannot synchronize detached working recipes. |
| Support default inspectors, both UMA views, and embedded editors | Retain the header hook and explicit renderer; define a disposable explicit-placement registration to prevent duplicate rendering. Prove actual coverage in Unity 6.3. |
| Ship add-ons separately from core | Publish launcher API version 1 and record required API/core versions in package metadata. Compatibility must be checked before importing incompatible source. |
| Preserve existing recipe extensions | Keep the recipe-specific legacy bridge. Do not convert arbitrary inline UI into launcher buttons automatically. |

The API is an editor launcher. Runtime components, generator/filter APIs, settings panels, package installation, and tool-window lifecycle continue using their existing systems. In particular, OverlayPainter Plugin API v2 remains unchanged by this design.

## Findings in the current source

| Source | Relevant behavior |
| --- | --- |
| `Core/Editor/Extensions/DynamicCharacterSystem/IUMARecipePlugin.cs` | Provides a foldout label/state and forwarded enable, destroy, and inspector drawing callbacks. No target-type registration. |
| `Core/Editor/Extensions/DynamicCharacterSystem/RecipeEditor.cs` | Scans loaded assemblies and owns legacy plugin instances. Loads a separate `_recipe` working copy; `DoUpdate()` saves it back to the inspected asset. |
| `Core/Editor/Scripts/CharacterBaseEditor.cs` | Shared by recipe-related inspectors, but not every UMA editor. `Rebuild()` recreates controls from the existing working recipe; it does not reload the asset. |
| `Core/Editor/Extensions/DynamicCharacterSystem/UMAWardrobeRecipeEditor.StandardView.cs` | Standard View queues edits and detects changes to `recipeString`. Its commit/reload paths provide useful host integration points but do not establish a contract for all recipe inspectors or Advanced View. |
| `Core/Editor/Scripts/UMAWardrobeRecipeGraphEditorWindow.cs` | The window owns `_recipe`, `_needsSave`, and a separate SerializedObject, independently of its embedded `_legacyInspector`. An Editor-only synchronization contract would miss the window's state. This source remains subject to the migration plan's Unity 6.4+ support audit. |
| `Core/Editor/Scripts/UMAInspectorView.cs` | Shared Standard/Advanced selector and section styling. It does not own the inspected Editor or all UMA editor entry points. |
| `Core/Editor/Extensions/DynamicCharacterSystem/DynamicCharacterAvatarEditor.cs` | Has its own inspector lifecycle and view branching. A recipe-only integration cannot cover it. |
| `Editor/General/UMA_Editor.asmdef` | Editor-only foundation referencing UMA_Core. UMA_Core_Editor already references it. Suitable location for the independent registration API. |

Paths in this table are relative to `Assets/UMA/`. The existing DNA converter and Overlay Painter plugin systems serve different purposes and remain independent.

## Proposed public API

Use a repeatable `UMAPluginAttribute` on static action methods. One registered method contributes one button; a plugin class can contain several methods. This avoids requiring a provider instance, custom lifecycle, or central plugin asset for a button that simply opens an existing tool.

Place the public types in namespace `UMA.Editors`, assembly `UMA_Editor`. Seal the attribute and declare `AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)`. Resolve nonpublic static methods as well as public ones. Reject instance methods, generic methods, methods on open generic types, `async void` actions, and signatures with anything other than exactly one Editor parameter and the specified return type. Windows opened by an action can own asynchronous work through their normal Unity lifecycle.

Proposed signature:

```csharp
[UMAPlugin(typeof(SlotDataAsset), "Open Slot Tool", Order = 100)]
private static void OpenSlotTool(UnityEditor.Editor editor)
{
    // Existing Unity API and tool implementation.
    // Use editor.target or editor.targets, not Selection.activeObject.
    MySlotToolWindow.Open((SlotDataAsset)editor.target);
}
```

`MySlotToolWindow` represents a plugin-owned implementation.

Attribute options:

| Member | Contract |
| --- | --- |
| Target type | Required inspected item type, not the custom Editor class. Accept Unity object types and interfaces implemented by inspected objects. |
| Label | Required nonempty button text. |
| Tooltip | Optional explanatory text. |
| Order | Optional integer, default zero; lower values draw first. |
| IncludeDerived | Default true; applies to each target registration separately. False requires an exact concrete type. Interface registrations always use assignability; reject IncludeDerived=false on an interface with a diagnostic. |
| SupportsMultipleTargets | Default false. Matching actions remain visible but disabled for multiple selection unless explicitly enabled. |
| ValidateMethod | Optional name of a nongeneric static `bool Method(Editor editor)` declared on the same class; allow nonpublic methods. Resolve by this exact signature and reject missing/ambiguous validators. Controls enabled state using current context; has no side effects and never applies or saves edits. |

Execution signature is `static void Method(Editor editor)`. Unity's Editor already supplies `target`, `targets`, `serializedObject`, and `Repaint()`, so no replacement context/service wrapper is needed. Helpers can be called from an ordinary static class or a plugin's existing EditorWindow class. Unity only calls lifecycle messages on appropriate Unity objects: adding an `OnGUI` method to an ordinary registration class does not make it a Unity window.

Multiple target attributes on the same method make the same button available for several types. Deduplicate by method identity when registrations overlap. Require identical Label, Tooltip, Order, SupportsMultipleTargets, and ValidateMethod across that method's attributes. Target type and IncludeDerived may differ. Reject conflicting action metadata as one invalid action, with a diagnostic. Multiple buttons use separate action methods. Two different methods with the same label remain separate actions.

The remaining public surface is deliberately small:

```csharp
public static class UMAPluginApi
{
    public static int Version => 1;
}

public static class UMAPluginGUI
{
    public static void Draw(UnityEditor.Editor editor);
    public static System.IDisposable RegisterExplicitHost(
        UnityEditor.Editor editor, IUMAPluginHost host = null);
}

// Implemented by inspectors or window adapters with buffered working data.
public interface IUMAPluginHost
{
    bool CanRunPluginActions { get; }
    bool TryPreparePluginAction();
    void RefreshAfterPluginAction();
}
```

This is the implemented public contract, shown without method bodies. The synchronization interface is optional for ordinary inspectors using only their SerializedObject, and required for shipped UMA hosts with detached or deferred edits. A window can implement it itself or provide an adapter through RegisterExplicitHost; it need not replace its cached Editor's type. Plugins still receive the original Editor; they do not call the host interface or depend on a specific core inspector class.

No new `OnGUI`, `Update`, `OnEnable`, `OnDisable`, `OnDestroy`, event bus, scheduler, window manager, Undo wrapper, access facade, runtime plugin loader, or dependency injection API is needed. Plugins open normal windows and subscribe/unsubscribe to Unity events themselves.

### Version and package compatibility

`UMAPluginApi.Version` identifies the launcher contract, independently of the installed UMA release and independently of OverlayPainter Plugin API v2. Use a property rather than an inlined public constant. Publish version 1 only with the implemented and validated surface above. Additive changes preserve existing behavior; breaking changes need a new supported contract and a migration policy.

Separately distributed add-ons declare their required launcher API and supported UMA versions in the package metadata specified by the migration plan. PackageSupport checks those requirements before managed import. Direct `.unitypackage` import is supported for a documented compatible UMA release; document and enforce the release pairing in packaging/release checks. An attribute discovered after compilation cannot prevent a compilation failure caused by importing source that references an API absent from an older UMA installation.

Do not add an installer, package manifest reader, runtime plugin registry, or global scripting define to the launcher. A plugin-owned editor assembly references UMA_Editor and the necessary core assemblies; runtime assemblies must not reference UMA_Editor. Core and the foundation must compile with every optional package absent.

## Discovery and matching

1. Discover attributed methods through `TypeCache.GetMethodsWithAttribute<UMAPluginAttribute>()` once per assembly reload. Cache validated descriptors and delegates, not a process-wide collection of live editors or target assets.
2. Validate method signature, target types, attribute metadata, and optional validator independently. A bad registration must not prevent valid plugins or the host inspector from loading. Log enough information to locate the declaring assembly, class, and method.
3. Cache candidate matching by concrete target type. Evaluate current target validity, multiple selection, and validators against the actual host at draw/click time.
4. Match by exact type or `registeredType.IsAssignableFrom(actualType)`. For a multiple selection, show only actions whose registration set covers every target. Do not silently operate on a subset.
5. Sort deterministically by Order, label using ordinal comparison, then declaring assembly/type/method. Never depend on TypeCache enumeration order.
6. Build button visibility/layout on Layout and keep it stable for that GUI event cycle. Revalidate before executing; a target may have been deleted or the editor may have changed since Layout.
7. Cache only static metadata across editors. Keep any host state scoped to the actual Editor/window using object references or weak ownership. Do not use `GetInstanceID()`.

A disabled applicable action still counts as a plugin and keeps the area visible. An unrelated registration does not. Reloading scripts must replace cached registrations and hook subscriptions without duplicating buttons. Entering Play Mode with domain reload disabled must not accumulate registrations.

Target types must be closed UnityEngine.Object types or closed interfaces. Reject null types, unrelated CLR classes, open generic targets, and empty/whitespace labels. Cache immutable registrations and typed delegates after validation. Multiple selection matches when the method's registration set covers every target, even if different attributes cover different targets; SupportsMultipleTargets then determines whether the action is enabled. An unready IUMAPluginHost disables otherwise applicable actions without removing their layout.

## Host integration and editor coverage

### Normal Unity inspectors

Use `Editor.finishedDefaultHeaderGUI` as the initial general insertion point. It receives the actual Editor and places the area above its inspector body, independently of Standard/Advanced branching. Subscribe once through editor initialization, filter to matching target registrations before allocating any layout, and restore GUI state afterward.

This approach allows matching buttons in existing UMA inspectors, default inspectors, and third-party inspectors without replacing their CustomEditor or forcing a common base class. It also avoids adding a conflicting catch-all `CustomEditor(typeof(Object), true)`.

The documented hook follows the default header; it is not proof that every custom host draws that header. The first implementation milestone must exercise asset and component inspectors, IMGUI and UI Toolkit bodies, locked inspectors, and embedded editors in Unity 6.3. Record where the hook actually runs. A gap requires the explicit host integration below, not a claim of universal automatic injection.

### Custom headers, embedded editors, and editor windows

Provide a small shared `UMAPluginGUI.Draw(Editor editor)` entry point for hosts that bypass the normal header. A window editing an item uses its existing Editor, or a cached Editor created and destroyed through Unity's normal APIs, and passes that Editor to the same renderer. Plugins continue to register the edited item type.

Each host chooses one route: automatic header placement or explicit placement. `RegisterExplicitHost(editor)` returns a disposable lease that suppresses automatic rendering for that Editor only. Register in the host's earliest enable/setup path, before its first header draw, and retain the lease until disable/disposal. RecipeEditor must register in OnEnable, not its deferred InitializeEditor callback. A window owns the lease alongside its cached Editor and disposes it before destroying that Editor. A retained UI host releases its lease when the host detaches or is destroyed.

Multiple leases for the same Editor reference share one suppression entry with reference counting; disposing a lease twice is harmless. Storage must not keep an abandoned Editor alive. Two locked inspectors remain independent. A suppressed host must call Draw exactly once at its chosen top placement on each GUI pass, before the view selector and all early returns. Never use a global last-drawn-frame flag. Registering inside Draw is too late to suppress a header already rendered.

The optional host argument supplies synchronization for window-owned working data. Resolve the effective synchronization host in this order: an explicitly supplied adapter, the Editor implementing IUMAPluginHost, then the SerializedObject default. A null-host lease only controls placement, so it can coexist with a wrapping window's adapter lease. Repeated nonnull registrations must refer to the same adapter; reject a competing adapter before changing the active registration. Do not let the last window silently take over another window's pending edits. Removing or replacing the effective adapter invalidates queued clicks even if another placement lease remains.

The window owns its adapter and lease; static storage uses weak ownership. If both the cached Editor and the window have buffered edits, the supplied adapter must coordinate both preparation and refresh explicitly. Do not run two independent preparation flows automatically. A wrapping window may supply an adapter for its embedded editor's existing Draw call; it must not add a second Plugins area for the same Editor. Windows editing different items use different cached Editors and leases.

Inventory all shipped UMA custom inspectors and item-editing windows. Start with recipes and their derived editors, DynamicCharacterAvatar, SlotDataAsset, OverlayDataAsset, RaceData, UMAMaterial, DNA/converter tools, and optional-package editors. Track which route each host uses and verify that none depend on entering Advanced View or calling a particular base inspector method.

An arbitrary third-party EditorWindow does not expose a universal top-of-window insertion point. Such a window participates through the explicit call. Windows without an edited item have no matching item context and do not acquire buttons merely because a different object is selected elsewhere. Property drawers do not each draw another Editors area; their owning editor supplies the area for its item.

Use IMGUI for integration with the current inspectors and header callback. A retained UI host can embed the common renderer in an IMGUIContainer at its top when explicit placement is needed. This feature does not require rewriting existing editor interfaces.

## Host synchronization

The launcher can apply a normal inspector's SerializedObject, but it cannot infer or serialize private working data. RecipeEditor is the concrete reason this interface is necessary: saving an old `_recipe` after a plugin changes the asset can erase the plugin's changes. Calling Rebuild or Repaint alone does not fix that.

| Host member | Required behavior |
| --- | --- |
| CanRunPluginActions | Cheap, side-effect-free readiness check. False while initializing, loading, in an invalid target state, or unable to prepare safely. Evaluated during draw and dispatch. |
| TryPreparePluginAction | Commit the host's pending serialized and buffered edits using its existing Undo/save/update policy. Return true only when the inspected objects reflect those edits and the working state is synchronized. Return false on a conflict or unavailable data; cancel the action without discarding pending edits. |
| RefreshAfterPluginAction | Refresh SerializedObject and reload/invalidate detached data from the current targets after an action starts, including after an exception. Rebuild controls at the host's normal safe boundary and repaint. Do not save the old working buffer over the action's changes. |

Implement adapters in the host assembly, leaving UMA_Editor independent of RecipeEditor, UMAInspectorView, and optional tools. Audit recipes and every derived Standard/Advanced path, DynamicCharacterAvatar, embedded UMAData editors, and item-editing windows for their actual buffering behavior. Share existing commit/load routines where appropriate, but do not assume that the base Rebuild method reloads an asset. Hosts using the interface own all preparation, including their SerializedObject apply; the launcher must not apply it again behind the host's back. Readiness, preparation, and refresh use the same effective adapter throughout a dispatch.

For an ordinary Editor without the interface, apply pending SerializedObject properties before execution and update/repaint afterward. Never call SerializedObject.Update before applying pending changes: Update discards unapplied local modifications. This default only covers Unity serialized-property editing. Document that a third-party inspector with private buffers must implement the host interface to guarantee synchronization; automatic header placement alone cannot supply that guarantee.

Opening a window returns before its later edits occur. Buffered hosts must also detect subsequent external target changes and Undo/redo through appropriate Unity events or a revision comparison at a safe layout boundary. The wardrobe Standard View's recipeString comparison is one existing example. When an external revision and local pending edits conflict, preserve the local edits and use the host's conflict handling; do not automatically flush stale data or silently replace it. Include two inspectors on the same recipe in validation. There is no cross-plugin save/rebuild bus or automatic SaveAssets call.

Before flushing detached edits, compare the target's current revision with the baseline from which the working buffer was loaded. If only the external revision changed, reload it; if both the target and local buffer changed, refuse preparation and let the owner resolve the conflict. Do not assume that existing recipe editors already implement this check. Preparation and refresh run outside OnGUI: they may update data, mark controls for rebuilding, and request repaint, but must not draw GUILayout controls or require Event.current. Initialization and any queued host edits must finish before readiness becomes true.

## Button layout and execution

- Draw one Editors area containing a Plugins label and the matching buttons. Use readable text, tooltips where supplied, and a vertical or wrapping layout that remains usable in a narrow Inspector. Multiple actions from a plugin must remain individually discoverable.
- Keep the same actions and order in both view modes. The shared view selector remains a presentation preference and does not control registration.
- Draw no empty labels, separators, foldouts, or padding. Preserve existing GUI enabled state, indentation, color, and change tracking around launcher controls; opening a tool must not mark the inspected asset dirty. Scope restoration of GUI.changed to the launch buttons. Preserve genuine changes from the legacy inline drawing contribution so the owning recipe inspector can process them normally.
- Use the Editor passed by the host, including its locked selection. A callback runs once with the complete targets array when it explicitly supports multiple targets; the host does not repeat it separately for every object.
- Queue clicks through EditorApplication.delayCall after ending the area's layout scopes. This gives the inspector body a chance to finish its ordinary edit/apply flow and keeps tool invocation outside header GUILayout traversal. It is a single deferred dispatch, not a plugin scheduler.
- Capture the initiating Editor weakly, its exact ordered target references, the action descriptor, registry generation, explicit-host lifetime generation where registered, and effective synchronization adapter identity/generation. Allow one pending action per host; disable its action buttons until dispatched or canceled. Never redirect to global Selection, reacquire a different Editor, or fall back to a different synchronization adapter after the click.
- At dispatch, cancel if the Editor was destroyed, any target was destroyed, its target list changed, the registration generation changed, or Unity is compiling, importing, or transitioning Play Mode. Also cancel if the captured adapter is gone, destroyed, or no longer the effective host. Release queued state immediately; do not retry later on a new context. Hook teardown/reload and play transitions so domain reload disabled is safe. An automatic host uses weak ownership and live Editor/target checks. An explicit host also cancels when its final placement lease ends or its lifetime generation changes; a reinitialized host must release the old lease before registering again.
- Recheck matching and host readiness. Prepare through IUMAPluginHost or the SerializedObject default above, then recheck the captured target context and rerun validation against the committed state. Invoke once only if all checks still pass. Preparing normal pending edits may succeed even when the final validator disables execution; those edits retain the host's normal policy.
- After an action starts, refresh the surviving host in finally, even if the action partially changes an asset and throws. If preparation or validation cancels execution, repaint as needed without discarding working data. Check host/target lifetime again before refreshing. An action that opens a window must copy the target references it needs into that window; it must not rely on the clicked Editor remaining alive.
- The action is responsible for Undo, prefab overrides, intentional asset changes, and any required UMA rebuild. Apply any SerializedObject changes made by the action before returning; subsequent host refresh must not discard unapplied action writes. Launcher actions open tools or perform synchronous data operations; inline drawing and Event.current-dependent interactions belong in the tool's normal GUI callbacks. The launcher cannot infer which objects the action modifies. Do not automatically save all assets or rebuild every avatar.
- Isolate registration, validation, preparation, action, and refresh exceptions with a useful plugin/host/method diagnostic. A refresh failure must not conceal the original action failure. Preserve Unity's ExitGUIException behavior rather than treating it as an ordinary plugin failure; unwrap reflected exceptions where necessary. Restore GUI state in finally and do not repeatedly log the same validator failure on every repaint.

## Existing recipe plugins

Keep `IUMARecipePlugin` source compatibility in the initial release. It supports arbitrary inline UI, so translating every existing plugin into a button would change behavior.

Retain the existing per-recipe-editor instances, foldout state, SerializedObject context, and enable/destroy pairing. Move their rendering into the common top Plugins area through a recipe-specific host bridge. That host uses explicit placement and suppresses automatic placement, so new action buttons and legacy foldouts appear once in the same area. Either legacy content or new actions makes the area visible.

Keep this bridge in UMA_Core_Editor, where the legacy interface already lives. The foundation API must not reference RecipeEditor or introduce an assembly cycle. Provide an internal Draw overload that accepts legacy-content presence and its drawing delegate, exposed to UMA_Core_Editor through an explicit InternalsVisibleTo declaration in UMA_Editor. This lets the bridge use the common area without adding a public inline-extension API. Legacy content may make the area visible even when there are no action registrations. The bridge retains its existing lifecycle ownership; the renderer only hosts drawing.

Verify all derived recipe inspectors, including their Standard View early returns. Preserve deferred initialization and dispose each legacy instance once. Isolate failures so one legacy plugin cannot prevent the others from initializing or cleaning up. An author migrating a tool removes its old interface registration when adding the new action registration to avoid presenting the same tool twice.

No automatic migration of DNA converter plugins or Overlay Painter generators is proposed. Those tools may separately register launcher buttons when appropriate.

## Implementation phases

### 1. Confirm host coverage and placement

- Inventory the inspector and window hosts described above, including optional assemblies.
- Prototype the default-header hook against Unity 6.3 in the current checkout.
- Record automatic versus explicit placement for each UMA host and confirm top placement in both views.
- Settle recipe working-data refresh boundaries and legacy bridge placement before changing plugin execution.
- Record readiness, pending-edit ownership, and external-change detection for each buffered host. Prove commit/reload behavior in both views; do not substitute Rebuild for reloading a recipe.
- Include a window-owned buffer separate from its cached Editor in the prototype. Exercise adapter binding and conflict detection without relying on the deferred Wardrobe Graph's unverified Unity baseline.

### 2. Implement the minimal registration foundation

- Add the attribute, API version property, host interface, immutable descriptor, discovery/matching registry, and common button renderer under `Assets/UMA/Editor/General/Plugins/` in UMA_Editor.
- Add idempotent initialization and deterministic ordering.
- Implement deferred dispatch, cancellation, failure isolation, and disposable explicit-host leases. Keep editor/target lifetime state out of the static metadata caches.
- Keep the foundation independent of optional plugin assemblies. Plugin assemblies reference the foundation; the foundation discovers them without referencing them.
- Keep installation and compatibility metadata handling in PackageSupport. Avoid a separate launcher manager UI or configuration assets.

### 3. Integrate hosts and legacy compatibility

- Enable automatic header integration and add explicit placement to uncovered shipped editors/windows.
- Integrate the recipe bridge without duplicating buttons, lifecycle calls, or view selectors.
- Implement IUMAPluginHost where the inventory finds buffered data. Preserve pending edits, reload after direct action edits, and reconcile later window/Undo changes safely.
- Reuse current visual conventions where assembly boundaries allow. Do not make UMA_Editor depend on UMA_Core_Editor merely to access UMAInspectorView styling.
- Verify selection, reload, and editor disposal behavior before enabling sample actions broadly.

### 4. Examples and author documentation

- Supply a minimal single-button example, a plugin with two actions, multiple target registrations, and an explicit multiple-selection action.
- Demonstrate opening an existing EditorWindow with the clicked item and using its ordinary OnGUI/Update/lifecycle methods.
- Document optional validation, target inheritance, locked inspectors, API/package version requirements, assembly placement, explicit host lease ownership, buffered host integration, normal Undo responsibilities, and legacy migration.
- Include a window-adapter example with pending edits, preparation outside OnGUI, and cleanup. State that actions must apply their own serialized writes before returning.
- Keep examples separate from normal production registrations so installing UMA does not add irrelevant buttons everywhere.

### 5. Acceptance checks

- Matching buttons appear at the top in Standard and Advanced View for recipe, avatar, slot, overlay, race, and material editors, plus a representative default and external custom inspector.
- An unrelated target and an empty registry produce no Editors/Plugins area or blank spacing.
- Exact, derived, and interface matching work; overlapping registrations do not duplicate an action. Ordering remains stable across reloads.
- A multi-button plugin presents each action once. Multiple selection is disabled unless supported, and mixed selections never partially execute.
- Two locked inspectors invoke actions on their own targets even when global selection changes. Deleting targets before deferred execution cancels safely.
- Destroying the Editor, changing its target array, closing/reinitializing an explicit host, releasing its final explicit lease, script reload, import/compile, and Play Mode transitions cancel queued actions. A canceled click never later executes on another item.
- Generated UI does not dirty assets. Representative edit actions preserve Undo/redo and prefab overrides, and recipe edits survive the next host repaint/save.
- Edit a recipe's buffered slots/colors, immediately launch an action that edits that recipe, and verify both changes survive in Standard and Advanced View. Repeat with late edits from an open window, Undo/redo, and two locked inspectors on the same asset. Readiness/conflict failure must preserve local data and prevent execution.
- A window with a working buffer separate from its cached Editor commits through its supplied adapter, refreshes after direct action edits, and preserves later window edits. Verify adapter removal/rebinding cancels queued actions, competing adapters are rejected, null-host placement leases coexist, and nested hosting still draws one area.
- Legacy plugin foldouts remain usable in both views with exactly one initialization/cleanup cycle per owner. No duplicate area appears through inheritance or embedded inspectors.
- A legacy inline plugin's real data edits still reach the host's change/apply flow, while clicking a launcher alone leaves GUI change tracking and asset dirtiness unchanged.
- Bad registrations, failing validators, and failing actions do not break other plugins or the owning inspector. ExitGUI remains functional.
- Cover invalid/generic/async signatures, conflicting repeated metadata, interface options, preparation failure, partial action failure, and refresh failure. Verify exact deduplication, final validation after commit, balanced GUI state, and bounded diagnostics.
- Opening/closing windows and recompiling scripts release host references and do not accumulate event subscriptions. Entering Play Mode without domain reload does not duplicate buttons.
- Compile a player to confirm the new API, examples, and discovery stay in editor-only assemblies. Before Unity validation, recheck and record the installed UMASettings version and current Unity version; use this checkout rather than an unsynchronized test project.
- In a clean compatible project, import OverlayPainter and another independent example add-on and verify automatic discovery without manual defines/configuration. Compile core-only after their managed removal. Reject incompatible metadata before managed import, keep unrelated files/project data intact, and confirm core has no optional-assembly references. These package checks follow the migration plan, not a new launcher installer.

Done means the coverage inventory has no unexplained gaps for shipped UMA item editors, the documented external-host integration and synchronization contracts work, and compatible optional packages are discovered/removed without affecting normal UMA. Plugin author examples use ordinary Unity tool lifecycles. The launcher cannot be called implemented or ready for migration until these acceptance checks pass on the current project.

## Unity API references

Unity documents the default-header event as an insertion point after the standard Inspector header. The proposal uses it for automatic placement and explicitly validates its coverage in the supported Unity version: [Editor.finishedDefaultHeaderGUI](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Editor-finishedDefaultHeaderGUI.html).

Attributed method discovery is available through TypeCache; its returned order is undefined, which is why the registry sorts descriptors explicitly: [TypeCache.GetMethodsWithAttribute](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/TypeCache.GetMethodsWithAttribute.html).

Unity calls delayCall once after inspectors update, which provides the deferred dispatch boundary: [EditorApplication.delayCall](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/EditorApplication-delayCall.html).

Applying SerializedObject properties writes them to the target and records Undo. Updating SerializedObject discards unapplied local changes. Neither operation automatically handles a detached UMARecipe: [SerializedObject.ApplyModifiedProperties](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializedObject.ApplyModifiedProperties.html), [SerializedObject.Update](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SerializedObject.Update.html).
