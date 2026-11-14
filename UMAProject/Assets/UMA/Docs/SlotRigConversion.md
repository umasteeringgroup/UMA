# Slot Rig Conversion

Slot Rig Conversion makes a slot authored for one race's rig wearable by another race. Its first use is moving UMA 2 clothing onto the UMA 3 rigs, whose meshes were exported from Blender with different axis settings and whose bodies have slightly different proportions. It has been tested on HumanMale to HumanMale30. The female rigs (HumanFemale to HumanFemale30) use the same code but have not been tested yet.

The converter is runtime code in `UMA_Core` (`UMASlotRigConverter`, `UMARigRestPose`, `UMASlotRigConversionProfile` under `Assets/UMA/Core/Scripts/RigConversion`). It works from data alone: slot mesh data and the two races' rest skeletons. No generated avatar, scene Transform, or editor API is involved. An editor window saves the results as new assets.

## Why earlier approaches failed

A slot's bind poses record where each bone was when the mesh was skinned. Clothing is often exported with the skeleton in a different pose from the body, so one slot carries several different "mesh-to-root" transforms, one per bone, and the same bone can even have two bind poses in one slot. Any converter that picks a single transform for the whole slot (from one bone, or from the first bind pose per bone) puts every other bone's vertices in the wrong place. The Bard jacket in `UMA RPG Starter 1` has 21 distinct bone transforms; the Bard belt has 5.

## What the converter does

1. **Bake to avatar space.** Every vertex is skinned by its own influences, each through that influence's own bind pose, using the source race's rest skeleton placed as the generator places it (`Root` × `Global` × bone hierarchy). This is the character standing at the origin in its rest pose (see [What "exact" means](#what-exact-means)). Normals, tangents and blend-shape deltas follow the same per-vertex map (see [Normals](#normals)).
2. **Correct per bone (optional, default on).** For each target bone the converter builds a rigid correction from the two rest skeletons: rotate about the source joint by the change in bone direction (measured from the joint to its children), then move the joint onto the target joint. Leaf and helper bones inherit their parent's rotation. Each vertex gets the weight-blended correction of its bones. With `UMARigCorrectionMode.None` this step is skipped and the result is exact: the geometry lands where the source rig put it.
3. **Remap weights.** Bone weights move by bone-name hash. A bone the target rig lacks sends its weight to the nearest ancestor the target does have (then to the rig root), and weights are merged per bone, sorted and renormalised. Bones the slot defines itself (for example prop bones under a hand) are carried over, re-parented under their nearest target ancestor with their avatar-space frame preserved.
4. **Rebind.** New bind poses are computed against the target rest skeleton in the target rig's canonical mesh space, which is read from the target race's body slots: it is the space most of their bones agree on, so one oddly bound bone cannot decide it. Where a body slot's bind pose for a bone agrees with the computed one, the body slot's matrix is reused verbatim so the mesh combiner merges the bone instead of duplicating it. The result has one bind pose per bone, `umaBones` from the target race, and the target root bone. It shares no arrays with the source slot.

Everything the converter decided is in a `UMASlotRigConversionReport`: influence and bone counts, weights moved to ancestors or the root, carried bones, the largest joint delta among the joints the slot follows, unweighted and renormalised vertices, whether handedness was flipped, and warnings.

### What "exact" means

With `UMARigCorrectionMode.None` the converted slot is drawn exactly where the source slot is drawn when both skeletons hold the pose stored in their base slots (`umaBones`). Unity's own skinning confirms this for the Bard outfit to within a few micrometres.

A generated character does not stand in that pose. The generator puts every bone into the race **T-pose**, and the two races' T-poses differ from their slot rest poses in different ways (HumanMale turns the arms 43° and the feet 25°; HumanMale30 turns the arms 42° and moves the shoulders). From there the converted slot follows the target skeleton through its bone weights, exactly as the target's own body does. So "exact" describes the bind pose, not a pixel match between a posed UMA 2 character and a posed UMA 3 character.

### Normals

`UMASlotRigConversionOptions.NormalMode` chooses how normals and blend-shape normal deltas are carried over.

- `MatchSourceSkinning` (default) transforms them with the blended skinning matrix, which is what Unity's skinning and UMA's mesh combiner do. The converted slot is lit exactly as the source slot is on its own rig.
- `InverseTranspose` uses the inverse-transpose of that matrix. Where a vertex blends bones that sit far apart in the bind pose (the Bard jacket, belt and pants around `LowerBackAdjust`), this follows the baked surface slightly more closely, but the shading differs from the source slot by up to 44°.

### Weights

Unity does not normalise bone weights when it skins. A vertex whose weights sum to within `WeightSumTolerance` (0.001) of 1 is therefore baked with the weights as stored, which is where Unity draws it. Further off than that, the weights are treated as damaged and normalised first; the report counts those vertices. A vertex with no weights at all, which Unity would draw at the character's origin, is kept in the slot's mesh space and bound to the rig root; the report counts those too. Output weights are always normalised.

### Mirrored rigs

When one rig's root is mirrored (negative scale) and the other's is not, triangle winding and tangent handedness are reversed, because Unity reverses culling and the bitangent sign for a renderer under a mirrored root. A mirrored mesh space on its own changes neither.

## Using it in the editor

`UMA > Tools > Slot Tools > Convert Slots To Another Rig` opens the converter window.

1. Set **Source race** (the race the slots were made for) and **Target race**.
2. Choose **Correction**: *Joint anchored* follows the target body's proportions; *None* is an exact bake and rebind.
3. Choose **Normals** (see [Normals](#normals)) and optionally assign a **Profile** with per-bone overrides (see below).
4. Add the slots (drag them in, or select them in the Project window and press **Add selected**).
5. Set the **Name suffix** (appended to both the asset file name and the slot name) and, if wanted, an **Output folder**. Without one, each new asset is saved next to its source.
6. Press **Convert**. New `SlotDataAsset` files are written and, with **Add to UMA index** on, registered in the asset index. The source assets are never modified. The window shows each slot's report and how many slots were converted, skipped and failed.

The converted slots keep their material, overlays, tags and other settings, and have `isLegacySlot` cleared. Recipes and wardrobe items still reference the originals; point them at the converted slots.

Converting a slot again, with **Replace earlier conversions** on (the default), writes into the asset the last conversion made. The asset keeps its GUID, so recipes that already use it keep working. With it off, each run creates another asset, which leaves two slots with the same name; the report warns about that.

Slots that are already bound to the target race's rig are skipped: their bind poses already sit in the target's mesh space, and converting them as if they came from the source rig would damage them. Slots that are not skinned, or whose bone weights are incomplete, are skipped with the reason.

## Automatic conversion

A race can convert legacy slots by itself while a character is built, so UMA 2 clothing can be put on a UMA 3 character without converting assets first.

**How it is switched on.** A race names the older rig it replaces in its **Legacy Rig** field (`RaceData.legacyRig`, shown in the race inspector above *Cross Compatibility Settings*). The field takes a *Rig Rest Pose* asset: the older rig's bone hierarchy and root placement, baked from that rig's race. HumanMale30 ships with `UMA3/Races/LegacyRigs/HumanMale UMA2 Rig Rest Pose`, baked from the UMA 2 HumanMale race. Because the rest pose is its own asset, the UMA 2 race and its body slots do not need to be in the project or the build. A race with no legacy rig converts nothing.

**Which slots are converted.** When the generator builds a character, a slot in its recipe is converted when all of these hold:

1. The character's race has a legacy rig.
2. The slot is still flagged `isLegacySlot`.
3. The slot's own bone table (`umaBones`, the skeleton it was built from) matches the legacy rig and does not match the race's own rig. At least 80% of its bones must match the legacy rig and fewer than 50% the race's rig; a bone matches when its local position is within 1 mm and its rotation within 1°.
4. The converter accepts it (it is skinned and its weights are complete).

The flag is only the opt-in. Many slots that are native to the UMA 3 rigs still carry it, including the HumanMale30 body, so the bone table decides. In this project that separates cleanly: every male UMA 2 clothing slot matches the UMA 2 rig, every HumanMale30 slot matches HumanMale30, and female slots match neither and are left alone.

**What happens to the slot.** The slot asset is never changed. The character's recipe gets a converted copy (Joint anchored, normals matched to the source, with the race's **Legacy Rig Profile** if one is set). The copy is made once per slot and race and reused by every character; it is destroyed when no recipe uses it any more, the same way UMA handles slots it bakes at runtime. The Bard outfit's 12 slots convert in about 24 ms in total.

**What it does not do.** It does not decide which recipes a race may wear. A UMA 3 character accepts a UMA 2 wardrobe recipe only if the recipe lists the UMA 3 race, or the UMA 3 race lists the UMA 2 race under *Cross Compatibility Settings*. [Converting a folder of legacy recipes](#converting-a-folder-of-legacy-recipes) adds the UMA 3 races to the recipes for you. Slots added to a recipe directly are converted regardless.

**Controls.**

- Clear a race's **Legacy Rig** to turn it off for that race, or set `UMALegacySlotConverter.Enabled = false` to turn it off everywhere.
- To bake a rest pose for another race, select the race and choose `UMA > Tools > Slot Tools > Bake Rig Rest Pose From Selected Race`. The asset is written next to the race; baking again updates it in place.
- Clear `isLegacySlot` on a slot to keep it from ever being converted.
- `UMALegacySlotConverter.ClearCache()` drops the converted copies after you change a slot, rest pose or profile from code. In the editor this happens by itself when an asset is saved.

Only HumanMale30 has a legacy rig assigned. The other UMA 3 races, including HumanFemale30, have none until a rest pose is baked and tested for them.

## Converting a folder of legacy recipes

UMA 2 wardrobe recipes hide parts of the body by naming the legacy race's body slots (`MaleTorso`, `FemaleFeet`…). The UMA 3 body is made of other slots, so those hides do nothing there. **UMA > Tools > Slot Tools > Convert Legacy Content** (or right-click a folder and choose **UMA > Convert Legacy Content In Folder**) brings a whole folder of UMA 2 wardrobe recipes to the UMA 3 races in one step:

1. **Mesh hides for the legacy body slots.** For each legacy race it makes one set of Mesh Hide Assets on the new race's body: one asset per legacy body slot and new body slot it covers, so `MaleTorso` becomes the torso and arms of `UMA30_Body_UDIM1002`, `UDIM1003` and `UDIM1005`. The legacy body slots are converted to the new rig (with the new race's **Legacy Rig** and **Legacy Rig Profile** when the Legacy Rig was baked from that legacy race, otherwise from the legacy race's own skeleton), and every triangle of the new body takes the legacy slot whose surface lies nearest. A majority filter (**Seam smoothing**) then removes stray triangles along the seams. The assets are saved in `<content folder>/MeshHides/<legacy race>/`, so they travel with the recipes, and are named `Legacy<race>_<legacy slot>_<new slot>`. Running again updates them in place; a hide whose **Locked** box is ticked is left as it is.
2. **Hides on the recipes.** Each recipe compatible with the legacy race gets the hides for the legacy slots in its *Hides* list. Hidden slots that are not body slots (eyebrows, slots of other races) are listed in the report.
3. **Races.** The new race is added to the recipe's compatible races, and the races listed under **Races to remove** (by default `<legacy race>DCS`, such as `HumanMaleDCS`) are taken off. The UMA index's race-to-recipe table is rebuilt afterwards.

Only recipes compatible with a listed legacy race are touched; a recipe for both HumanMale and HumanFemale is handled once for each. The window starts with HumanMale → HumanMale30 and HumanFemale → HumanFemale30. Under **Body slots** each race pair shows the slots of both base recipes; eyes, mouth, lashes, brows and hair are left out by default because they are separate slots on both generations. **Preview** lists every change without writing anything, and the report warns about recipes whose wardrobe slot the new race does not have (UMA 2's *Cape*, for example), since those cannot be worn there.

A hide only acts on a race whose base recipe has the slot it names, so the new hides do nothing on the UMA 2 races. From code, fill a `UMALegacyContentConversionSettings` (`UMALegacyContentConverter.DefaultMappings()` gives the two human races) and call `UMALegacyContentConverter.Convert(settings, preview)`.

The fit is the same as an editor conversion with the same settings: see [Limits](#limits). For clothing that must sit closely, convert in the editor and adjust the result.

## Using it from code

```csharp
// From races (loads both races' base recipe slots and T-poses):
var report = new UMASlotRigConversionReport();
UMAMeshData converted = UMASlotRigConverter.ConvertSlot(slot, humanMaleUma2, humanMale30, UMASlotRigConversionOptions.Default, report);
Debug.Log(report.Summary());

// From rest poses, for example when the same pair of rigs is reused for many slots:
UMARigRestPose source = UMARigRestPose.FromRace(humanMaleUma2);
UMARigRestPose target = UMARigRestPose.FromRace(humanMale30);
UMAMeshData exact = UMASlotRigConverter.Convert(slot.meshData, source, target, UMASlotRigConversionOptions.Exact);
```

`UMARigRestPose.FromSlots` and `FromBones` build rest poses from explicit data for rigs that are not UMA races.

`Convert` throws `ArgumentException` for mesh data it cannot convert. For unattended use, where one unusable slot must not stop the rest, use the non-throwing forms:

```csharp
if (!UMARigRestPose.TryFromRace(humanMale30, out UMARigRestPose target, out string reason)) { Debug.LogWarning(reason); return; }

// Already native to the target? Then leave it alone.
if (UMASlotRigConverter.IsBoundToRigMeshSpace(slot.meshData, target, 1e-4f, out _)) return;

if (!UMASlotRigConverter.TryConvert(slot.meshData, source, target, null, report, out UMAMeshData converted, out reason))
{
    Debug.LogWarning($"{slot.slotName} was not converted: {reason}");
}
```

`CanConvert` runs the same data checks without converting. The converter never modifies the slot it is given; put the result on a copy of the slot (`Instantiate`), not on the shared asset.

## Per-bone overrides

A **Rig Conversion Profile** (`Create > UMA > Misc > Rig Conversion Profile`) holds a default mode and a list of bone entries. Each entry names a bone and sets:

- **Mode**: `Full` (translate and rotate, the default), `TranslateOnly`, `InheritParent`, or `None`.
- **Position offset** in metres, avatar space.
- **Rotation offset** in degrees about the target joint.
- **Scale offset** about the target joint, along the bone's own axes. Use it to narrow or widen what a bone carries, for example a sleeve that stands off the arm.
- **Carry to child bones** (on by default): the entry's offsets also apply to every bone below it, the way moving a real bone carries its children. A parent's offset acts after its child's. Turn it off to adjust only the vertices weighted to that one bone.

Offsets are applied after the computed correction, so a profile can nudge a sleeve or hat without changing anything else. Assign the profile in the converter window, in `UMASlotRigConversionOptions.Profile`, or as a race's **Legacy Rig Profile** for automatic conversion.

### Entries for one slot

One set of entries usually fits a whole wardrobe, because the entries describe how the two bodies differ rather than how any one garment is cut. Now and then a garment needs something else: a stiff collar, a pauldron that sits wrong, a glove from a different artist. For those the profile also holds **entries for single slots**.

A slot's entry for a bone **replaces** the general entry for that bone, mode and offsets together, for that slot only. Every bone the slot has no entry of its own for keeps following the general entries, and every other slot is unaffected. A new slot entry starts as a copy of the general one, so making it changes nothing until it is edited. Remove it and the bone follows the general entry again.

Slots are matched by slot name. Automatic conversion, the converter window, the Rig Profile Editor's preview and the automatic mesh hides all look the entries up by the name of the slot being converted. From code, set `UMASlotRigConversionOptions.ProfileSlotName`; when it is empty the mesh data's own `SlotName` is used. The profile's methods take the slot name as an extra argument (`GetOrAddCorrection(bone, slot)`, `TryGetCorrection(bone, slot, out entry)`, `RemoveCorrection(bone, slot)`, `RemoveSlotOverride(slot)`); a null or empty slot name means the general entries.

The entries are per slot rather than per recipe on purpose: a slot converts once and is cached, and the same slot worn through two recipes should not fit two ways.

### Effectors

A bone entry moves whatever a bone carries. An **effector** moves a place: every vertex of a converted slot that falls inside it, whatever it is weighted to. Use one when a garment needs reshaping in a spot that no bone owns on its own, for example the seat of a pair of pants, which is usually shared between the lower back, the upper legs and the gluteus bones.

An effector is a **sphere** or a **capsule**, placed from a bone of the target rig (so it sits in a meaningful place on any character of the race) with a position offset and, for a capsule, a rotation. It acts at full strength inside its core and fades smoothly to nothing at its radius; **Falloff** sets how much of the radius the fade takes. Nothing outside the radius is touched: those vertices come out of the conversion exactly as they would without the effector.

A capsule also has a **curve along its length**: strength from one end to the other. Leave it flat for an even effect, or shape it to fade the effect in and out along the capsule, for example full in the middle of a thigh and gone toward the knee and the hip.

Each effector does one of four things:

- **Push out**: away from the sphere's centre, or from the capsule's centre line, by a distance. A negative distance pulls in.
- **Move**: shift by an offset.
- **Scale**: about the effector's centre, along its own axes.
- **Clear the body**: bring cloth that lies under the skin, or closer to it than the **Clearance**, out along the skin's normal. This is the one that fits a garment to a body it was not made for, because it moves each vertex only as far as that vertex needs: cloth already clear of the skin stays where it is, so an effector around the seat leaves the front of the pants alone without being told to. **Reach** is how far under the skin cloth is still brought out; anything deeper is taken to be inside on purpose and is left. **Smoothing** spreads the push into the neighbouring cloth so the moved part blends into the rest. The surface between the vertices is tested as well: a coarse garment can have every vertex over the skin and still cut through a rounded body with the flat of its triangles, and those triangles are lifted until they clear.

**Mirror** applies the mirror image of an effector on the other side of the character as well, so one effector serves both thighs or both shoulders.

Effectors run after the bone entries, in order, each on what the one before left. Normals and tangents are turned by the same amount the surface under them turned, so authored hard edges and seams keep their look. Blend shapes are carried unchanged.

Each effector says which slots it acts on. **All slots** is the default: the effector reshapes every converted slot that passes through it, whatever the character is wearing, which is what makes a profile reusable across a wardrobe (the seat is brought out for any pair of pants, and the belt and the hem of a jacket hanging over the same spot move with it). An effector can instead be kept to **one slot**, for a fix that belongs to one garment; it then leaves every other slot alone, even where another slot passes straight through it. In the asset, the ones for all slots are `profile.effectors` and the ones kept to a slot are that slot's `slotOverrides[].effectors`; a slot gets the ones for all slots first and then its own. From code, `SetEffectorSlot(effector, slotName)` moves an effector between the two (a null or empty name means all slots) and `TryGetEffectorSlot` reads where it is.

"Clear the body" needs the body. In the editor tools (the Rig Profile Editor's preview, the converter window, the automatic mesh hides) it is the race's body at rest. Automatic conversion builds it the first time it is needed, from the race's base recipe slots as the character being built wears them (baked copies included), never from clothing, so the converted slot is the same for every character of the race and stays shared. From code, pass it in `UMASlotRigConversionOptions.BodySurface` (`UMARigBodySurface.FromSlots`); without it those effectors are skipped and the report says so.

### The Rig Profile Editor

Typing bone names and numbers into the asset works, but the **Rig Profile Editor** shows the result while you edit. Open it with the **Open Rig Profile Editor** button on a race (under *Legacy Rig Profile*) or on a profile asset, with the **Rig Profile** button beside **Edit TPose** in a Dynamic Character Avatar's inspector (Advanced View, for races using the new DNA system; it is greyed out until the race has a Legacy Rig Profile), or from `UMA > Tools > Slot Tools > Rig Profile Editor`.

1. **Race and profile.** The window edits the race's Legacy Rig Profile. **New** creates one next to the race and assigns it. You can edit any other profile too; the window says when it is not the one the race uses and offers to assign it.
2. **Preview character.** Add wardrobe recipes or slots built for the legacy rig (drag them in, or select them in the Project window and press the add button), then **Build preview**. A temporary character of the race appears in the Scene view wearing those slots, converted with the profile. It is not saved with the scene, and what it wears is remembered per race. A slot added without a recipe borrows an overlay from its own folder. The box in front of each recipe switches it on or off without taking it out of the list (**All** and **None** switch them all, **Clear** empties the list): a switched-off recipe is not worn by the preview and gets no hides from **Create mesh hides**. **Inspect** opens a recipe in a popup Inspector. With **Live** on, the preview is redressed at once and the camera stays where it is.

   To go through a lot of content quickly, drag a folder into **Recipe folders > Root folder**. Every folder under it that holds wardrobe recipes is listed by its whole path below the root (`Wardrobe/HighElf/Male (6)`), sorted so subfolders stay together. A folder's **Edit** button replaces the recipe list above with that folder's recipes, all switched on, so single pieces can then be switched off or inspected there; the folder loaded last is shown in bold, and **Previous** / **Next** load the folder before or after it. **Filter** narrows the list by path. **Only recipes for** keeps the list to recipes compatible with the race, a race it is cross-compatible with, or the race its Legacy Rig was baked from (HumanMale30 lists both Human Male 3.0 and HumanMale recipes). The root folder is remembered per race. **Rescan** looks again; the list also refreshes when the project changes.
3. **Find a bone.** The bone list is the race's skeleton as a tree. **Filter** narrows it by name, the quick buttons fill in common names, and the tabs show **All**, **Favorites**, **Edited** (bones with an entry) or **Used by preview** (bones the previewed slots are weighted to). The star on each row marks a favourite; favourites are stored in the profile, so they stay with it. Clothing on these rigs is mostly weighted to the `...Adjust` bones, so *Used by preview* is the quickest way to the bones that matter. With a filter or search active, the listed bones also appear as dots in the Scene view and can be clicked.
4. **Adjust it.** With a bone selected, set its mode and offsets in the window, or drag the **Move**, **Rotate** or **Scale** handle in the Scene view (**W**, **E** and **R** switch handles). **O** resets one channel, **Reset** all three, and **Remove entry** returns the bone to the default mode.
   **Edit bone entries for** chooses what the bone edits apply to (effectors do not follow this switch; each has its own **Affects** setting, see step 7). **All slots** is the general profile. Pick one of the previewed slots (or any slot the profile already has entries for) to give that slot entries of its own: the bone panel then shows the entry that applies to the slot, says whether it is the slot's own or the one for all slots, and the first change makes the slot its own entry. In the bone list, bold marks the slot's own entries and *(all slots)* the bones that only follow the general ones. **Remove slot entry** puts one bone back on the general entry, and **Clear** does so for the whole slot. Mirroring, handles and undo work the same in either scope.
5. **Mirror it.** With **Mirror to …** ticked (it is on by default and remembered), every change to a left or right bone is applied, mirrored, to the bone on the other side: the position offset is reflected across the character's centre line, the rotation turns the other way, and the scale is carried to the matching axes of the opposite bone. Mode and *Carry to child bones* are copied, and resetting or removing an entry does the same to its opposite. The opposite bone is marked in the Scene view while you work. Opposite bones are found by name (`Left…`/`Right…`, or `…_L`/`…_R`); bones on the centre line are not affected. **Copy across** mirrors the selected bone once when the mode is off, and **Mirror all: Left to Right / Right to Left** makes one whole side the mirror image of the other.
6. **Watch it.** Every change converts the previewed slots again and rebuilds the character's mesh. The status line shows how long that took. Turn **Live** off to rebuild only when you press the button.

7. **Reshape a spot.** Under **Effectors**, **Add sphere** or **Add capsule** puts an effector at the selected bone. The list shows every effector of the profile, under what it acts on. A new effector acts on **all slots**; its **Affects** setting keeps it to one slot instead, and changes it back. Under Affects the editor says which of the previewed slots have vertices inside the effector, and warns when the slot it is kept to has none: an effector that sits on the jacket but is kept to the pants does nothing, and this is where that shows. **Mark those vertices in the Scene view** (on by default) puts a dot on every vertex the selected effector holds: brighter where it is stronger, grey on a slot it leaves alone, dim where the character hides it. An effector only moves the vertices inside it, and its outline does not show depth: a capsule that looks as if it covers the back from behind can be running through the body with the cloth just outside its radius. A stretch of cloth with no dots is outside the effector. Select an effector in the list to edit it: its outline appears in the Scene view, with the core drawn inside the outer radius and the mirror image dimmer, and **Move**, **Rotate** and **Size** switch its handle (**W**, **E**, **R**). Size drags the radius, and for a capsule the cones at its ends set the length. For a garment whose seat, chest or thigh sits under the skin, choose **Clear the body**, place the effector over the spot, and raise the radius until the skin is covered. Selecting a bone in the list gives the bone's handle back.

8. **Hide what is covered.** Under **Mesh hides**, **Create mesh hides for the preview recipes** works out which body triangles each recipe's clothing covers, saves a Mesh Hide Asset per body slot next to the recipe, and adds them to the recipe. **Show in preview** applies them to the preview character. Hidden triangles are marked in red on the character, and a small brush corrects them in place; what you paint is kept when the hides are created again. See [Creating Mesh Hides Automatically](MeshHideAssets.md#creating-mesh-hides-automatically). Do this once the fit is settled: the hides are computed from the clothing as the profile places it.

The preview stands in the **rest pose**, the pose stored in the race's base slots, because that is the pose the offsets are defined in; the handles work there. **T-pose** shows the pose a generated character starts from, for a look at the result as it will appear. Edits are undoable and are saved with the profile asset.

## Limits

- The converter changes skinning, not fit. Clothing shaped around one body keeps that shape; the Joint anchored correction follows the target's joints, not its surface. Measured on the Bard outfit at the bind pose, the worn slots sit a median 7 mm off the HumanMale skin, 34 mm off the HumanMale30 skin after an exact conversion, and 28 mm after a Joint anchored one. Gloves gain most from Joint anchored (0.3 mm, then 24 mm, then 4 mm). Use the Clothing Conformer for a surface fit.
- Bone directions are measured from a joint to its children, summed when there are several. The correction does not estimate twist about the bone, and a vertex that blends several bones gets the weighted average of their corrections, which is not quite rigid. A bone whose children all sit on the joint (zero-length helpers) inherits its parent's rotation.
- Bone entries adjust per bone: they move, turn and scale what each bone carries. Effectors reshape a region, and "Clear the body" brings cloth out of the skin there, but neither remodels a garment: a crease or a seam that is part of the mesh is carried along, and cloth is only ever pushed out to the skin, never drawn in to it.
- Effectors are placed against the rest pose and applied once, when the slot is converted. They do not follow DNA: a character whose body is made larger by blend shapes after conversion can still show through.
- Automatic conversion needs the slot's bone table to match the legacy rig. A UMA 2 slot whose bones were edited, or that was built from a modified skeleton, is left alone; convert it in the editor instead. A legacy slot whose `isLegacySlot` flag has been cleared is also left alone.
- Mesh hide assets keep working, because conversion does not change a slot's triangles. Hides authored against the UMA 2 body do not apply to the UMA 3 body.
- Bones that exist on neither rig and are not defined by the slot lose their weights to the nearest ancestor; the report lists each such bone.
- UMA 2 and UMA 3 body proportions differ by up to about 7.5 cm per joint for the human males. Props far from their bone move further: a sword about 1 m from the hand moves about 10 cm when the hand's direction changes by 6 degrees.

## Tests

- `UMASlotRigConverterTests` (runtime test assembly) checks the algorithm on synthetic rigs: exact round trip, two bind poses for one bone, blend shapes, both normal modes, mirrored rigs (winding and tangent handedness), weight remapping, carried and uncarried slot bones, the joint correction and profile overrides, entries for single slots (they replace the general entry bone by bone, for that slot only), effectors applied where the converted slot stands, unweighted and unnormalised weights, the non-throwing entry points, and that the result shares nothing with its source. One test skins the original and the converted mesh with a real `SkinnedMeshRenderer` and compares what Unity produces.
- `UMARigEffectorTests` (runtime test assembly) checks effectors on hand-made geometry: how strength falls off in a sphere and along and around a capsule, the curve along a capsule's length, what each action does, mirroring, that normals and tangents turn with the surface, the nearest-skin search, "clear the body" against a flat skin (buried cloth comes out to the clearance, cloth deeper than the reach or already clear is left, smoothing only ever adds), and which slots an effector acts on (all slots or one, and moving it between the two).
- `UMARigEffectorBardTests` (editor test assembly) puts a "clear the body" effector on the UMA 2 Bard pants on HumanMale30: every vertex and every triangle middle it reaches at full strength ends up outside the skin, nothing outside its radius moves at all, and automatic conversion produces the same result from the body slots the character wears.
- `UMASlotRigConverterBardTests` (editor test assembly) converts the UMA 2 Bard slots to HumanMale30. It generates a HumanMale and a HumanMale30 character, checks their skeletons against the rest-pose model, and has Unity skin each original slot on the first and each converted slot on the second: positions, normals and tangents must agree. It also saves, reloads and re-converts an asset, refuses slots that are already on the target rig, and generates a HumanMale30 character wearing the converted jacket, checking that the mesh combiner kept every jacket vertex in place. For automatic conversion it builds a HumanMale30 character wearing the unconverted jacket and checks that the recipe got a converted copy, the project asset and the body slots were left alone, and the jacket sits where an explicit conversion puts it; and it checks that the baked UMA 2 rest pose still matches the UMA 2 race.
