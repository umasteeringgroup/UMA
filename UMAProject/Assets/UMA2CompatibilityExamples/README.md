# UMA2Compatibility examples

This optional package contains two base recipe examples and the legacy UMA 2 Wearables tree: clothing, hair, tattoos and utility-slot samples. Install UMA2Compatibility, UMA 3 Content and the selected UMA render-pipeline support first. The parent provides all resources needed by its base characters and works without this package.

1. Open or create a scene with the normal UMA context and generator setup.
2. Rebuild the Global Library if newly imported legacy assets are not listed.
3. Add a DynamicCharacterAvatar and select the legacy **Human Male** or **Human Female** race (internal identifiers `HumanMale` and `HumanFemale`), rather than the UMA 3 race.
4. Open **UMA2 Male Example** or **UMA2 Female Example** in the recipe inspector to examine the matching body slots, overlays and colors. These are example recipe asset names, not renamed races. The recipes reference **Human Male** and **Human Female**, respectively, and can be loaded through the normal avatar recipe workflow.
5. Add wardrobe recipes that declare compatibility with the selected legacy race, then build the character.

Wearable recipes and their assets live under `Assets/UMA2CompatibilityExamples/Wearables`. Utility-slot examples live under `Wearables/Example/AdditionalSlots`. Their original GUIDs are retained, and `UMA2.Content.asmref` preserves the scripts' original assembly identity. Base-character resources shared with the main races are retained under `Assets/UMA2/Races/HumanShared/BaseResources` rather than duplicated here.

Copy an example into your own project folder before customizing it. Removing this companion leaves the parent races and base characters usable, but removes its optional clothing, hair, tattoo and utility-slot samples. The parent's **Remove All** action removes companions first.
