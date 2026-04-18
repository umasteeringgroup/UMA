# Copilot Instructions

## General Guidelines
- Avoid kludgy fixes; prefer root-cause fixes based on observed event flow and resource readiness.
- Prioritize root-cause event-flow fixes and caching in UMA editor stages.
- For UI/UX refactors in UMA editor tools, clarify requirements with questions before implementation and ensure clear mode separation to prevent overlapping functions.
- UMA editor tools should avoid kludgy fixes; prefer root-cause event-flow fixes and caching, and clarify UX requirements before refactors.
- Do not replace popup preview with a bolted-on TextureMerge.DrawAllRects path when a direct transform-path fix is feasible; prefer direct root-cause fixes instead.
- In UMA editor tools, keep EditorModifiers behind `#if UNITY_EDITOR` to avoid including them in builds for memory reasons.
- For FaceEditorStage and UMA editor tooling changes: panel height redistribution should be even among expanded panels; allow panels to extend offscreen rather than shrink below minimums; reserve an 8% vertical buffer due to Unity usable-rect inaccuracies.
- Always implement requested code changes directly unless the user explicitly asks not to. When the user says a requested editor UI change was not applied, inspect the actual target file immediately, edit it, and verify instead of relying on prior intent. If the user corrects a UMA editor layout change, inspect the actual target file immediately, patch it directly, and verify; for UmaExamineOverlaysWindow, the details panel should be fixed-width while the list uses the remainder.
- Always set a `_rectChanged` flag and ensure overlay rect changes are saved and trigger a rebuild on Update/Close in Overlay Positioner.
- Do not use reflection to access TextureMerge fields/methods in OverlayEditor; use direct TextureMerge API from settings instead.
- When scene objects cause editor-state issues, avoid ObjectField persistence and prefer InstanceID-backed drag-drop handling instead.

## Development Environment
- User environment uses Visual Studio Community 2026 (18.4).
- User prefers using PowerShell as the terminal shell in this workspace.
- Repository branch in use is NextGen at E:\UMAURP\UMA.

## Code Style
- Use specific formatting rules.
- Follow naming conventions.
- Ensure shader parameter name is exactly `_Color` (case sensitive); use `_Color` in code and repo instructions.

## Project-Specific Rules
- Custom requirement A.
- Custom requirement B.
- CreateDecal 'ClearAllStamps' is invoked only from the CreateDecal OnGUI UI, not from inspectors.
- For SlotDataAsset strict validation: slots with meshData == null should be considered valid only when isUtilitySlot == true. Additionally, `submeshes`/`subMeshIndex` on `SlotDataAsset` are only valid for legacy (UMA 2) slots; new slots always use submesh 0.
- `DynamicCharacterAvatar` derives from `UMAData`, allowing valid casting from `UMAData` to `DynamicCharacterAvatar` when the instance is a DCA.
- In this repo, SRP cross compatible shaders are "UMA/Diffuse_Alpha" for alpha blending (set `_Color`, case sensitive) and "UMA/Diffuse" for opaque.
- Map normal map textures to `UMAMaterial.ChannelType.NormalMap`; map all other texture channels to `UMAMaterial.ChannelType.Texture` in `UMAMaterial` creation.
- When creating UMAMaterial channels from shader texture properties, skip any texture properties whose names start with "unity".
- For scene asset consolidation tooling, do not process generic 'everything else' dependencies; only copy explicitly allowed asset types (materials, textures, audioclips, models, prefabs, SlotDataAsset, OverlayDataAsset).
- MeshModifierEditor and all MeshModifier matching/build logic should always use `SlotDataAsset.sourceSlot` for creation and matching; MeshModifier SlotName should be matched only against `SlotDataAsset.sourceSlot`.
