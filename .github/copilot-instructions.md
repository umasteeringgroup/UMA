# UMA (Unity Multipurpose Avatar) — Short Dev Guide

Scope
- Unity package (no standalone builds). Validate in Play Mode.
- Unity: 6000.2.4f1 (exact). .NET Framework 4.7.1, C# 9.

Open
- Open in Unity Hub with 6000.2.4f1. First import/compile can take minutes.

Validate (Play Mode)
1) Scene Loader
   - Assets/UMA/Examples/SceneLoader/SceneLoader.unity
   - Play → menu visible
2) DCS Demo (primary)
   - Assets/UMA/Examples/DynamicCharacterSystem Examples/UMA DCS Demo - Simple Setup.unity
   - In Play: race switch, DNA sliders, wardrobe apply; no pink/errors

Optional checks (as needed)
- UMA DCS Demo - Random Characters.unity
- UMA Core Demo - Crowd.unity
- Blendshape Example.unity
- AddressablesScene.unity
- UMA Timeline Example.unity

Done Criteria
- No console errors on open.
- Scene Loader menu OK.
- DCS Demo: race/DNA/wardrobe OK, no UMA warnings/errors.

Troubleshooting
- Pink/missing: check shaders/RP and Console.
- Generation fails: overlays/slots exist; try UMA > Race Updater.
- Assembly errors: verify asmdefs and manifest deps.

Assistant Rules (GPT-5)
- Keep answers short; use bullets. Avoid heavy markup.
- File edits: use exact paths; minimal diffs; preserve style.
- Code blocks MUST include: 
  - language and target path header:
    ```<language> <relative file path>
    <code>
    ```
- Editor code:
  - Wrap in `#if UNITY_EDITOR`.
  - Use `SerializedObject/SerializedProperty`; call `ApplyModifiedProperties`.
  - Call `Repaint()` and `EditorUtility.SetDirty(target)` when UI changes.
  - Support Undo: `Undo.RecordObject(target, "Change")` before mutations.
- Runtime code:
  - Avoid new packages/deps; respect existing asmdefs.
  - Prefer existing UMA APIs (e.g., `UMAAssetIndexer`, `DynamicCharacterAvatar`).
  - Keep allocations low in per-frame paths; avoid LINQ in hot loops.
- Compatibility:
  - Use APIs available in Unity 6000.2.4f1, .NET 4.7.1, C# 9.
  - Editor-only API guarded; no `AssetDatabase` in runtime.
- Behavior:
  - Don’t rename public APIs or break serialization.
  - Ask for missing context only when necessary (list exact files/lines needed).
  - Prefer incremental changes; avoid large refactors.
- Diagrams:
  - Use mermaid; follow workspace rules for escaping and quoting.

Key Folders
- Core: Assets/UMA/Core/
- Examples: Assets/UMA/Examples/
- Content: Assets/UMA/Content/
- Editor tools: Assets/UMA/Core/Editor/