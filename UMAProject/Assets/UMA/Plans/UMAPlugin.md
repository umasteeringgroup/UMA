# UMAPlugin implementation plan

Status: Proposed; implementation has not started.
Date: 2026-09-24.
Reviewed checkout: `C:\GitHub\UMA\UMAProject`, UMA NextGen 3.1f1, Unity 6000.3.18f1.
Supported baseline: Unity 6.3 and newer.

## Goal

Provide a small editor extension system that lets a plugin register one or more buttons for the type of item being edited. Matching buttons appear in a **Plugins** group inside an **Editors** area at the top of the editor, in both Standard View and Advanced View. Omit the complete area, including its spacing, when there are no applicable plugins.

Registration must work for any inspected object type, including types from another package, rather than maintaining a fixed list of UMA asset types. Plugin authors retain normal access to UMA and Unity APIs. The system provides discovery, target matching, and button placement; plugin tools use Unity's existing windows, drawing, updates, serialization, Undo, and events.

Interpretation of the requested layout: **Editors** is the outer area and **Plugins** is the group containing extension buttons. It sits below Unity's mandatory object/component header, above the view selector and ordinary editable content. Existing built-in editing commands remain usable when no plugins are installed; hiding the new area must not hide those commands.

## Findings in the current source

| Source | Relevant behavior |
| --- | --- |
| `Core/Editor/Extensions/DynamicCharacterSystem/IUMARecipePlugin.cs` | Provides a foldout label/state and forwarded enable, destroy, and inspector drawing callbacks. No target-type registration. |
| `Core/Editor/Extensions/DynamicCharacterSystem/RecipeEditor.cs` | Scans loaded assemblies, creates recipe plugin instances, and draws their foldouts before the base inspector. Owns their initialization and cleanup. |
| `Core/Editor/Scripts/CharacterBaseEditor.cs` | Shared by recipe-related inspectors, but not every UMA editor. |
| `Core/Editor/Scripts/UMAInspectorView.cs` | Shared Standard/Advanced selector and section styling. It does not own the inspected Editor or all UMA editor entry points. |
| `Core/Editor/Extensions/DynamicCharacterSystem/DynamicCharacterAvatarEditor.cs` | Has its own inspector lifecycle and view branching. A recipe-only integration cannot cover it. |
| `Editor/General/UMA_Editor.asmdef` | Editor-only foundation referencing UMA_Core. UMA_Core_Editor already references it. Suitable location for the independent registration API. |

Paths in this table are relative to `Assets/UMA/`. The existing DNA converter and Overlay Painter plugin systems serve different purposes and remain independent.

## Proposed public API

Use a repeatable `UMAPluginAttribute` on static action methods. One registered method contributes one button; a plugin class can contain several methods. This avoids requiring a provider instance, custom lifecycle, or central plugin asset for a button that simply opens an existing tool.

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

The example is a proposed API sketch; `MySlotToolWindow` represents a plugin-owned implementation.

Attribute options:

| Member | Contract |
| --- | --- |
| Target type | Required inspected item type, not the custom Editor class. Accept Unity object types and interfaces implemented by inspected objects. |
| Label | Required nonempty button text. |
| Tooltip | Optional explanatory text. |
| Order | Optional integer, default zero; lower values draw first. |
| IncludeDerived | Default true. Exact matching is available for concrete types; interface registrations use assignability. |
| SupportsMultipleTargets | Default false. Matching actions remain visible but disabled for multiple selection unless explicitly enabled. |
| ValidateMethod | Optional name of a static `bool Method(Editor editor)` on the same declaring class. Controls enabled state using current context; has no side effects. |

Execution signature is `static void Method(Editor editor)`. Unity's Editor already supplies `target`, `targets`, `serializedObject`, and `Repaint()`, so no replacement context/service wrapper is needed. Helpers can be called from an ordinary static class or a plugin's existing EditorWindow class. Unity only calls lifecycle messages on appropriate Unity objects: adding an `OnGUI` method to an ordinary registration class does not make it a Unity window.

Multiple target attributes on the same method make the same button available for several types. Deduplicate the method when more than one attribute matches a target. Require identical label/options for repeated registrations of that method; reject conflicting metadata with a clear diagnostic. Multiple buttons use separate action methods.

No new `OnGUI`, `Update`, `OnEnable`, `OnDisable`, `OnDestroy`, event bus, scheduler, window manager, Undo wrapper, access facade, runtime plugin loader, or dependency injection API is needed. Plugins open normal windows and subscribe/unsubscribe to Unity events themselves.

## Discovery and matching

1. Discover attributed methods through `TypeCache.GetMethodsWithAttribute<UMAPluginAttribute>()` once per assembly reload. Cache validated descriptors and delegates, not a process-wide collection of live editors or target assets.
2. Validate method signature, target types, attribute metadata, and optional validator independently. A bad registration must not prevent valid plugins or the host inspector from loading. Log enough information to locate the declaring assembly, class, and method.
3. Cache candidate matching by concrete target type. Evaluate current target validity, multiple selection, and validators against the actual host at draw/click time.
4. Match by exact type or `registeredType.IsAssignableFrom(actualType)`. For a multiple selection, show only actions whose registration set covers every target. Do not silently operate on a subset.
5. Sort deterministically by Order, label using ordinal comparison, then declaring assembly/type/method. Never depend on TypeCache enumeration order.
6. Build button visibility/layout on Layout and keep it stable for that GUI event cycle. Revalidate before executing; a target may have been deleted or the editor may have changed since Layout.
7. Cache only static metadata across editors. Keep any host state scoped to the actual Editor/window using object references or weak ownership. Do not use `GetInstanceID()`.

A disabled applicable action still counts as a plugin and keeps the area visible. An unrelated registration does not. Reloading scripts must replace cached registrations and hook subscriptions without duplicating buttons. Entering Play Mode with domain reload disabled must not accumulate registrations.

## Host integration and editor coverage

### Normal Unity inspectors

Use `Editor.finishedDefaultHeaderGUI` as the initial general insertion point. It receives the actual Editor and places the area above its inspector body, independently of Standard/Advanced branching. Subscribe once through editor initialization, filter to matching target registrations before allocating any layout, and restore GUI state afterward.

This approach allows matching buttons in existing UMA inspectors, default inspectors, and third-party inspectors without replacing their CustomEditor or forcing a common base class. It also avoids adding a conflicting catch-all `CustomEditor(typeof(Object), true)`.

The documented hook follows the default header; it is not proof that every custom host draws that header. The first implementation milestone must exercise asset and component inspectors, IMGUI and UI Toolkit bodies, locked inspectors, and embedded editors in Unity 6.3. Record where the hook actually runs. A gap requires the explicit host integration below, not a claim of universal automatic injection.

### Custom headers, embedded editors, and editor windows

Provide a small shared `UMAPluginGUI.Draw(Editor editor)` entry point for hosts that bypass the normal header. A window editing an item uses its existing Editor, or a cached Editor created and destroyed through Unity's normal APIs, and passes that Editor to the same renderer. Plugins continue to register the edited item type.

Each host chooses one route: automatic header placement or explicit placement. An explicit host registers that placement before header drawing so the automatic callback can skip it. Suppression is scoped to the host instance/lifetime and must work independently in two locked inspectors; never use a global last-drawn-frame flag.

Inventory all shipped UMA custom inspectors and item-editing windows. Start with recipes and their derived editors, DynamicCharacterAvatar, SlotDataAsset, OverlayDataAsset, RaceData, UMAMaterial, DNA/converter tools, and optional-package editors. Track which route each host uses and verify that none depend on entering Advanced View or calling a particular base inspector method.

An arbitrary third-party EditorWindow does not expose a universal top-of-window insertion point. Such a window participates through the explicit call. Windows without an edited item have no matching item context and do not acquire buttons merely because a different object is selected elsewhere. Property drawers do not each draw another Editors area; their owning editor supplies the area for its item.

Use IMGUI for integration with the current inspectors and header callback. A retained UI host can embed the common renderer in an IMGUIContainer at its top when explicit placement is needed. This feature does not require rewriting existing editor interfaces.

## Button layout and execution

- Draw one Editors area containing a Plugins label and the matching buttons. Use readable text, tooltips where supplied, and a vertical or wrapping layout that remains usable in a narrow Inspector. Multiple actions from a plugin must remain individually discoverable.
- Keep the same actions and order in both view modes. The shared view selector remains a presentation preference and does not control registration.
- Draw no empty labels, separators, foldouts, or padding. Preserve existing GUI enabled state, indentation, color, and change tracking; opening a tool must not mark the inspected asset dirty.
- Use the Editor passed by the host, including its locked selection. A callback runs once with the complete targets array when it explicitly supports multiple targets; the host does not repeat it separately for every object.
- Invoke actions at a safe host boundary after ending the area's layout scopes. If dispatch must be deferred, retain the initiating context briefly, cancel when the host/targets are invalid, and never redirect to the current global selection.
- Commit pending serialized edits through the owning inspector's existing apply flow before invocation where needed. After an action, refresh/repaint using normal Unity APIs. Audit recipe editors with separate working recipe data so stale local state cannot overwrite plugin edits. Avoid a new general save/rebuild callback framework; use the existing host refresh path.
- The action is responsible for Undo, prefab overrides, intentional asset changes, and any required UMA rebuild. The launcher cannot infer which objects the action modifies. Do not automatically save all assets or rebuild every avatar.
- Isolate registration, validation, and action exceptions with a useful plugin/method diagnostic. Preserve Unity's ExitGUIException behavior and balanced layout scopes. Do not repeatedly log the same validator failure on every repaint.

## Existing recipe plugins

Keep `IUMARecipePlugin` source compatibility in the initial release. It supports arbitrary inline UI, so translating every existing plugin into a button would change behavior.

Retain the existing per-recipe-editor instances, foldout state, SerializedObject context, and enable/destroy pairing. Move their rendering into the common top Plugins area through a recipe-specific host bridge. That host uses explicit placement and suppresses automatic placement, so new action buttons and legacy foldouts appear once in the same area. Either legacy content or new actions makes the area visible.

Keep this bridge in UMA_Core_Editor, where the legacy interface already lives. The foundation API must not reference RecipeEditor or introduce an assembly cycle. The common renderer can receive an optional legacy-content drawing contribution from the host; that is an internal integration detail, not a second public lifecycle API.

Verify all derived recipe inspectors, including their Standard View early returns. Preserve deferred initialization and dispose each legacy instance once. Isolate failures so one legacy plugin cannot prevent the others from initializing or cleaning up. An author migrating a tool removes its old interface registration when adding the new action registration to avoid presenting the same tool twice.

No automatic migration of DNA converter plugins or Overlay Painter generators is proposed. Those tools may separately register launcher buttons when appropriate.

## Implementation phases

### 1. Confirm host coverage and placement

- Inventory the inspector and window hosts described above, including optional assemblies.
- Prototype the default-header hook against Unity 6.3 in the current checkout.
- Record automatic versus explicit placement for each UMA host and confirm top placement in both views.
- Settle recipe working-data refresh boundaries and legacy bridge placement before changing plugin execution.

### 2. Implement the minimal registration foundation

- Add the attribute, immutable descriptor, discovery/matching registry, and common button renderer under `Assets/UMA/Editor/General/Plugins/` in UMA_Editor.
- Add idempotent initialization and deterministic ordering.
- Keep the foundation independent of optional plugin assemblies. Plugin assemblies reference the foundation; the foundation discovers them without referencing them.
- Add only the explicit-host placement support required by phase 1. Avoid a separate plugin manager UI or configuration assets.

### 3. Integrate hosts and legacy compatibility

- Enable automatic header integration and add explicit placement to uncovered shipped editors/windows.
- Integrate the recipe bridge without duplicating buttons, lifecycle calls, or view selectors.
- Reuse current visual conventions where assembly boundaries allow. Do not make UMA_Editor depend on UMA_Core_Editor merely to access UMAInspectorView styling.
- Verify selection, reload, and editor disposal behavior before enabling sample actions broadly.

### 4. Examples and author documentation

- Supply a minimal single-button example, a plugin with two actions, multiple target registrations, and an explicit multiple-selection action.
- Demonstrate opening an existing EditorWindow with the clicked item and using its ordinary OnGUI/Update/lifecycle methods.
- Document optional validation, target inheritance, locked inspectors, assembly placement, explicit window hosting, normal Undo responsibilities, and legacy migration.
- Keep examples separate from normal production registrations so installing UMA does not add irrelevant buttons everywhere.

### 5. Acceptance checks

- Matching buttons appear at the top in Standard and Advanced View for recipe, avatar, slot, overlay, race, and material editors, plus a representative default and external custom inspector.
- An unrelated target and an empty registry produce no Editors/Plugins area or blank spacing.
- Exact, derived, and interface matching work; overlapping registrations do not duplicate an action. Ordering remains stable across reloads.
- A multi-button plugin presents each action once. Multiple selection is disabled unless supported, and mixed selections never partially execute.
- Two locked inspectors invoke actions on their own targets even when global selection changes. Deleting targets before deferred execution cancels safely.
- Generated UI does not dirty assets. Representative edit actions preserve Undo/redo and prefab overrides, and recipe edits survive the next host repaint/save.
- Legacy plugin foldouts remain usable in both views with exactly one initialization/cleanup cycle per owner. No duplicate area appears through inheritance or embedded inspectors.
- Bad registrations, failing validators, and failing actions do not break other plugins or the owning inspector. ExitGUI remains functional.
- Opening/closing windows and recompiling scripts release host references and do not accumulate event subscriptions. Entering Play Mode without domain reload does not duplicate buttons.
- Compile a player to confirm the new API, examples, and discovery stay in editor-only assemblies. Before Unity validation, recheck and record the installed UMASettings version and current Unity version; use this checkout rather than an unsynchronized test project.

Done means the coverage inventory has no unexplained gaps for shipped UMA item editors, the documented external-host integration works, and the author examples require no custom lifecycle machinery.

## Unity API references

Unity documents the default-header event as an insertion point after the standard Inspector header. The proposal uses it for automatic placement and explicitly validates its coverage in the supported Unity version: [Editor.finishedDefaultHeaderGUI](https://docs.unity3d.com/ja/6000.0/ScriptReference/Editor-finishedDefaultHeaderGUI.html).

Attributed method discovery is available through TypeCache; its returned order is undefined, which is why the registry sorts descriptors explicitly: [TypeCache.GetMethodsWithAttribute](https://docs.unity3d.com/cn/6000.0/ScriptReference/TypeCache.GetMethodsWithAttribute.html).
