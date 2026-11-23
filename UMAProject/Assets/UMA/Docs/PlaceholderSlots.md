# Using placeholder slots to paint differently named meshes with one overlay

A tattoo painted for `Torso_A` can also fit `Torso_B` if both meshes put the same patch of skin in the same place on the texture. A **placeholder slot** lets you ship that tattoo as one wardrobe recipe. Each character keeps its own torso mesh and base skin. When the character builds, UMA adds the tattoo to the torso it finds by tag.

Use this for details such as freckles, makeup, scars or fabric prints that should follow whichever compatible mesh the character wears. The placeholder holds the overlays and their matching rules. The race or another equipped item supplies the mesh. **The tag decides which surfaces receive the art. The UV layout decides where the art lands.** UMA does not inspect the UVs or move the artwork to make it fit.

## Prepare the receiving slots

Suppose you want a blue swallow tattoo on the left shoulder of two torso variants. The names below are examples you can replace with your project's names.

| Slot on the character | What you see on its UV template | Tag to give the slot |
| --- | --- | --- |
| `Torso_A` | The tattoo falls on the left shoulder | `Skin_TorsoUV1` |
| `Torso_B` | The left shoulder occupies the same pixels, with the same orientation | `Skin_TorsoUV1` |
| `Torso_C` | Those pixels cover the upper back | Give this layout its own tag if you paint a version for it |

1. **Check the artwork against both UV templates.** Compare the shoulder outline, seam and left/right orientation on the UV set the meshes actually use. An asymmetrical mark makes mirroring easy to spot. Matching texture dimensions alone tell you nothing about placement.
2. **Tag each receiving mesh.** Select its **SlotDataAsset** in the Project window. In **Standard View**, open **Slot & organization → Tags** and add `Skin_TorsoUV1`. Keep **Apply Overlays by Tag (Wildcard)** off on the receiving slot so it continues to supply the mesh. These are UMA slot tags. Unity GameObject tags and the tattoo overlay's own **Tags** field serve other purposes.
3. **Check slots already saved in recipes.** A recipe keeps its own copy of a slot's tags. Open the base or wardrobe recipe that actually supplies the torso, expand the torso row, then open **Matching Criteria → Edit tags for this slot**. Add the tag there if it is missing and save the recipe. Editing this row also lets you opt in just one recipe that uses a widely shared slot asset.
4. **Prepare the tattoo overlay.** If needed, use **Assets → Create → UMA → Core → Overlay Asset**. Paint the tattoo on a transparent background, assign its textures to the channels expected by the torso's UMA Material, and use a compatible material on both variants. Give the OverlayDataAsset a unique UMA name and make sure the UMA Global Library indexes it. Leave the base skin on each torso so its own skin tone and details show through around the ink.

## Add the placeholder in the Wardrobe Recipe Editor

1. In the Project window, choose **Assets → Create → UMA → Wardrobe → Recipe**, name the recipe `BlueSwallowTattoo`, and select it. In the Inspector, choose **Advanced View**.
2. Open **Race Settings** and add the `RaceData` assets for the characters that can equip the tattoo. Under **Wardrobe Settings → Wardrobe Region**, choose an existing region for body art on those races. The region controls how the item is equipped. Choosing a region already used by an ordinary wardrobe item can displace that item.
3. Scroll to the slot controls below the recipe's drop area. Click **Add Placeholder Slot**, beside **Add Slot**. Expand the new **(Placeholder Wildcard)** row. In **Placeholder Name**, enter `BlueSwallow_Torso` and press Enter. Give independently equipped effects distinct placeholder names, even across recipes. UMA merges same-named placeholders when it combines recipes.
4. Inside that row, open **Matching Criteria → Match Tags**. Use **+** to make a row, type `Skin_TorsoUV1`, and press Enter. If your project already offers the tag in the **Select** dropdown, choose it and click **Add**. The new placeholder starts with an empty tag list, which the Inspector highlights in red.
5. Leave **Only add for these Races** empty to allow any active race that can equip this recipe. To narrow just this placeholder, choose a race and click **Add Race** for each permitted race. Keep **Disable in recipe** and **This is a swap slot** off for this effect.
6. In the **same placeholder row**, find **Add Overlay**. Drag the tattoo's **OverlayDataAsset** onto **drop here**, or click **Select indexed asset…** and pick it by name. Confirm the tattoo appears under `BlueSwallow_Torso`. Add further painted layers in the order you want them applied. For a tattoo recipe, carry only the ink and its related details. An opaque skin overlay here can cover the receiving character's skin.
7. If **Automatic Updates** is off, click **Save Recipe**. Reopen the recipe and check the placeholder name, its match tag and the overlay underneath it.

**Starting from an existing overlay recipe?** Use **Save As** if you want a separate wearable. Expand the slot that holds the art and click **Convert slot to placeholder**, just below **Disable in recipe**. This keeps the name, overlays, tags and race criteria on that recipe row and removes its mesh reference. Give the placeholder a distinctive name, review the layers it inherited, and make sure the avatar gets the receiving mesh and base skin from another recipe.

**Using the node graph on Unity 6.4 or newer?** Right-click the wardrobe recipe in the Project window and choose **Edit in Node Graph**. Right-click the canvas and choose **Add → Placeholder Slot**. Select the node to edit **Placeholder Name**, **Matching Criteria** and **Add Overlay** in its inspector. Use the graph toolbar's **Save** if **Auto Save** is off.

## How the tags match

During a Dynamic Character Avatar build, UMA checks each eligible mesh slot against the placeholder. **One exact tag in common is enough.** Capitalization and spaces count. A placeholder tagged `Skin_TorsoUV1` reaches both example torsos if both are present. It also reaches an accidentally tagged shirt. A hidden or disabled torso cannot receive the tattoo through this match. With no matching mesh, the placeholder contributes no visible art.

Multiple **Match Tags** are alternatives. Adding separate tags `Skin` and `Torso` would also select a face tagged only `Skin` or a jacket tagged only `Torso`. Use one specific tag such as `Skin_TorsoUV1` when both the surface and its layout matter. For a different unwrap, paint a second overlay and put it on a separately named placeholder with that layout's own tag. The placeholder name, overlay name and mesh names can all differ.

## Check the result on a character

1. Select a test **Dynamic Character Avatar** with `Torso_A`. In **Standard View → Default wardrobe → Wardrobe items**, click the body-art region and choose `BlueSwallowTattoo`. Use **Regenerate Avatar**. The swallow should sit on the left shoulder, with the original skin visible around it.
2. Switch to a character or outfit that supplies `Torso_B`, equip the same recipe and regenerate. Check the same shoulder landmark and seam. Try `Torso_C` with the tattoo still equipped. Its different layout should receive no ink. Finally, remove the tattoo on a matching character and regenerate. The original torso and skin should still be there.

| What you see | What to check |
| --- | --- |
| The tattoo is missing from the wardrobe picker | Check the recipe is indexed, includes the active race and uses a wardrobe region that race provides. |
| The recipe equips, but the skin stays bare | Open the recipe supplying the visible torso. Compare its saved slot tag character for character with **Match Tags**. Check the placeholder's race list, **Disable in recipe**, and whether clothing hides the torso or removes the overlay through **Tags to Hide**. |
| The tattoo appears on the back, mirrored, or across a seam | The overlay reached a mesh. Compare its UV set and island orientation with the painting template, then check the overlay's crop and placement. |
| The tattoo looks right alone but moves when another cosmetic is equipped | Compare both recipes' **Placeholder Name** fields. Give effects intended for different surfaces distinct names and keep each one's match tags specific. |
| Ink appears on another piece, or looks twice as dark | Check every visible slot carrying the tag. Also check whether those pieces share one overlay list. A layer added to a shared list can show on a second piece or be added twice when both pieces match. |

For more on the individual assets and controls, see the [Wardrobe Recipe Editor](../Assets/UMA/Docs/WardrobeRecipeEditor.md), [Slot Data Asset](../Assets/UMA/Docs/SlotDataAsset.md) and [Overlay Data Asset](../Assets/UMA/Docs/OverlayDataAsset.md) guides.

This guide follows the editor and build code in UMAProject, UMA 3.1.6 with Unity 6000.3.26f1.
