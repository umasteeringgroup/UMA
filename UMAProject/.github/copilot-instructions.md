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
- For SlotDataAsset strict validation: slots with meshData == null should be considered valid only when isUtilitySlot == true.