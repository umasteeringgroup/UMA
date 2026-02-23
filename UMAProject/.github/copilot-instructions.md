# Copilot Instructions

## General Guidelines
- Avoid kludgy fixes; prefer root-cause fixes based on observed event flow and resource readiness.

## Code Style
- Use specific formatting rules
- Follow naming conventions

## Project-Specific Rules
- Custom requirement A
- Custom requirement B
- CreateDecal 'ClearAllStamps' is invoked only from the CreateDecal OnGUI UI, not from inspectors.
- For SlotDataAsset strict validation: slots with meshData == null should be considered valid only when isUtilitySlot == true. Additionally, `submeshes`/`subMeshIndex` on `SlotDataAsset` are only valid for legacy (UMA 2) slots; new slots always use submesh 0.
- `DynamicCharacterAvatar` derives from `UMAData`, allowing valid casting from `UMAData` to `DynamicCharacterAvatar` when the instance is a DCA.