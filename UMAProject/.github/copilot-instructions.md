# Copilot Instructions

## General Guidelines
- Avoid kludgy fixes; prefer root-cause fixes based on observed event flow and resource readiness.
- For UI/UX refactors in UMA editor tools, clarify requirements with questions before implementation and ensure clear mode separation to prevent overlapping functions.
- In UMA editor tools, keep EditorModifiers behind UNITY_EDITOR and avoid including them in builds for memory reasons.

## Code Style
- Use specific formatting rules
- Follow naming conventions
- Ensure shader parameter name is exactly `_Color` (case sensitive); use `_Color` in code and repo instructions.

## Project-Specific Rules
- Custom requirement A
- Custom requirement B
- CreateDecal 'ClearAllStamps' is invoked only from the CreateDecal OnGUI UI, not from inspectors.
- For SlotDataAsset strict validation: slots with meshData == null should be considered valid only when isUtilitySlot == true. Additionally, `submeshes`/`subMeshIndex` on `SlotDataAsset` are only valid for legacy (UMA 2) slots; new slots always use submesh 0.
- `DynamicCharacterAvatar` derives from `UMAData`, allowing valid casting from `UMAData` to `DynamicCharacterAvatar` when the instance is a DCA.
- In this repo, SRP cross compatible shaders are "UMA/Diffuse_Alpha" for alpha blending (set `_Color`, case sensitive) and "UMA/Diffuse" for opaque.
