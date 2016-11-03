UMA 2.1 DCS Readme
-----------------------------------

DynamicCharacterSystem is under UMA\Extensions\DynamicCharacterSystem
Scenes are under the Example/Scenes folder.

The Dynamic Character System adds a wardrobe system to UMA. Wardrobe pieces are simply recipes
that are set to Recipe Type "Wardrobe". Wardrobe Recipes can contain any number of slots and overlays,
and are assigned to a specific "Wardrobe Slot" that is race specific - these are defined on 
the RaceData. Races are preconfigured with the most common wardrobe slots.

Adding a recipe to a slot removes whatever was previously at the slot. (So putting on a helmet,
for example, removes the previous helmet.) Wardrobe Recipes can also be set to suppress another
slot (for example, putting on the helmet can also suppress the Hair.) 

A race has a base recipe that define what the default avatar of that race looks like - this contains
the slots and overlays for the body and skin.

Wardrobe recipes can also Hide parts of the base recipe. For example, putting gloves on the hands slot
can hide the base models hands. This solves the "poke through" issues that can occur, and lowers 
resource usage.

The system manages this all by merging recipes when the avatar is built. A base slot can
appear in any number of recipes. for example, you might have a "face" recipe that contains the 
head slot, and a "beard" recipe that also contains the head slot. The merging process will
correctly apply the beard after the face during the build process.

Scenes:

1-DynamicCharacterSystem - Simple Setup. This shows how to create a DynamicCharacterAvatar,
and add wardrobe pieces, set colors, and adjust DNA. It's probably all you need for a single-player game.

2-DynamicCharacterSystem - Advanced with Asset Bundles. This is a more powerful demo
that has the ability to download asset bundles, and add races and clothing on the fly.

3-DynamicCharacterSystem - Dna Converter Behaviour Customizer. This demo allows you to edit the
DNA Converter Behaviours. 

