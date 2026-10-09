# UMA2Compatibility

UMA2Compatibility supplies the legacy UMA 2 character content for current UMA on Unity 6.3 and newer. Install it when an existing character, wardrobe recipe or project needs the older races and assets. A project using only UMA 3 content does not need this plugin.

## What is included

The main package contains legacy races and base recipes, DNA converters and bone poses, expressions, animations, random sets, body slots, overlays and the original documentation at `Assets/UMA2`. Base characters work without the optional Examples package. Existing asset and script GUIDs and the `UMA2.Content` assembly name are retained.

The legacy Wearables tree is supplied by the separate Examples package at `Assets/UMA2CompatibilityExamples/Wearables`, including clothing, hair, tattoos and the utility-slot prefabs under `Wearables/Example/AdditionalSlots`. Resources required by the parent races and base recipes, such as eyelash, eyebrow and hair-cap assets, live in `Assets/UMA2/Races/HumanShared/BaseResources` so removing Examples does not break base characters. All moved assets retain their original GUIDs.

This package supplies content for the current UMA runtime. It does not install the old UMA 2 engine, provide compatibility with old Unity versions, or automatically convert UMA 2 wardrobes to UMA 3 races. Legacy race-specific DNA, UVs, skeletons and wardrobe compatibility still apply. The original cloth-upgrade PDF in this folder describes historical assets; current UMA and Unity documentation governs runtime setup.

## Install and update

1. Install UMA Core, select UMA URP or HDRP support, and install UMA 3 Content. Some legacy assets depend on those shared resources.
2. Open **UMA > Welcome to UMA > Plugins** and install **UMA2Compatibility**.
3. UMA searches for `UMA2Compatibility-<version>.unitypackage` automatically. If it cannot locate a compatible archive, choose the file when prompted.
4. Install the optional **Examples** or **Tests** companions if needed. Normal character generation requires neither.

The install location is `Assets/UMA2`, outside the read-only Core package. The stable manifest ID is `uma2`, retaining the package identity previously used for legacy content. Do not import an older `UMA2Content` archive alongside it. Projects with the earlier nested location `Assets/UMA/UMA2` can move that tree to `Assets/UMA2` through the installer, preserving GUIDs and local edits. If both locations exist, consolidate them before installing; the installer does not choose between competing copies.

Source checkouts already containing this folder can adopt a matching package through **Reinstall** without replacing their assets. Updates and removal use the same manifest ownership, conflict reports and backup workflow as other official plugins. Package versions are taken from the installed UMASettings release.

## Use legacy characters

Use the Global Library to find the supplied legacy `RaceData`, base recipes and compatible wardrobe recipes. Select a legacy race on a DynamicCharacterAvatar, then assign wardrobe recipes that declare compatibility with that race. Follow the normal UMA character-build workflow. If recently imported content is missing from selectors, rebuild the Global Library with its existing tools.

The legacy race assets retain their original names: **Human Male** and **Human Female**, with internal race identifiers `HumanMale` and `HumanFemale`. The compatibility plugin does not rename these races.

The optional Examples package contains base recipe assets named **UMA2 Male Example** and **UMA2 Female Example**, with a guide for configuring a DynamicCharacterAvatar. These are example recipe names, not new race names: the recipes reference **Human Male** and **Human Female**, respectively, using the existing legacy meshes and textures.

Legacy textures and material wrappers still need the appropriate shaders for your render pipeline. Where required, install the existing **UMA 2.X Shader Packages** from Welcome's shader-package page. Installing compatibility content does not make every historical shader work in every pipeline.

## Documentation and removal

Select **UMA2Compatibility** in the Documentation Browser's top dropdown to read this guide separately from the main UMA documentation. The descriptor registers on import and installation; managed removal unregisters it.

Use **Remove** on Examples or Tests to remove a companion independently, or **Remove All** on the parent to remove its companions first. Base characters remain usable without Examples; characters using its optional wearables or utility slots require Examples reinstalled. Unchanged package-owned assets are removed. Locally modified and unowned assets are preserved, with a removal report under `Library/UMA/PluginRemoval`. Characters and project assets that reference the removed races, slots or scripts need the plugin reinstalled before they can be used again. Copy customized content into project-owned folders and resolve its dependencies before removing the source package.

## Build packages

Choose **UMA > Build > Build Plugin Packages**. The main archive and its separate companions are written under `Build/Plugins` with the current UMA version. The PowerShell release builder, `tools/Packaging/Build-UMAPluginPackages.ps1`, supports the same three packages. Asset payloads and import metadata are copied byte-for-byte; dependencies are declared rather than bundled.
