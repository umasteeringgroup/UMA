# UMA Addressables

This document explains UMA's Addressables generation path and how assets are labeled and grouped.

## Overview
- UMA can export recipes and dependencies to Addressables for streaming.
- The editor plugin `SingleGroupGenerator` aggregates dependencies and labels per recipe and writes them to a shared Addressables group packed separately.

## Key Script: SingleGroupGenerator
Location: `Assets/UMA/Core/Editor/Scripts/AddressablePlugins/SingleGroupGenerator.cs`

Responsibilities
- Collect assets referenced by selected recipes (UMATextRecipe, UMAWardrobeRecipe, UMAWardrobeCollection)
- Build label lists: default label + recipe label + extra labels from wardrobe collections
- Add recipes, slots, overlays, materials, etc. to the Shared group
- Strip materials and/or overlay textures when configured via `UMAEditorUtilities`
  - When stripping textures, textures inherit the parent overlay labels and are added/marked as addressable in the UMA index

Group Settings
- Shared Group Name: UMA_SharedItems
- Bundle mode: PackSeparately

## Labels
- Default: `UMASettings.AddrDefaultLabel`
- Recipe label: `recipe.AssignedLabel`
- Extra labels from `UMAWardrobeCollection`

## Stripping Behavior
- `StripUMAMaterials`: clears references on slots/overlays and stores names for runtime restore
- `StripTextures`: for overlays, adds each texture to Addressables with the same labels/group, sets UMA index fields (`IsAddressable`, `AddressableGroup`, `AddressableAddress`, `AddressableLabels`), and nulls the texture reference while storing names in `textureNames`

## Runtime
- UMA loads by label lists (`UMAAssetIndexer.Preload`) and restores stripped references (material shader names, overlay textures by stored `textureNames`)
