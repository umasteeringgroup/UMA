using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The Vertex Editor's "Save as Mesh Modifier" writes one Mesh Modifier per wardrobe recipe whose slots
    /// were edited (and one for slots no worn recipe has), finds a slot's recipe by the key modifiers store,
    /// and while the editor is open a recipe it added a modifier to does not apply that modifier a second time.
    /// </summary>
    public class MeshModifierRecipeSaveTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
            created.Clear();
        }

        private UMAWardrobeRecipe Recipe(string name)
        {
            var recipe = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
            recipe.name = name;
            created.Add(recipe);
            return recipe;
        }

        private static MeshModifier.Modifier Modifier(string slot, string name = "Sculpt")
        {
            return new MeshModifier.Modifier { SlotName = slot, ModifierName = name };
        }

        [Test]
        public void EditsOnTwoRecipesSlotsBecomeOneModifierEachPlusOneForTheBody()
        {
            UMAWardrobeRecipe shirt = Recipe("Shirt");
            UMAWardrobeRecipe pants = Recipe("Pants");
            var owners = new Dictionary<string, UMAWardrobeRecipe> { { "ShirtSlot", shirt }, { "ShirtCollar", shirt }, { "PantsSlot", pants } };
            var mods = new List<MeshModifier.Modifier> { Modifier("ShirtSlot"), Modifier("PantsSlot"), Modifier("Body"), Modifier("ShirtCollar") };

            List<MeshModifierRecipeSavePlanner.Plan> plans = MeshModifierRecipeSavePlanner.PlanSaves(
                mods, key => owners.TryGetValue(key, out var r) ? r : null, "Assets/Edits/Outfit.asset");

            Assert.AreEqual(3, plans.Count);
            Assert.AreSame(shirt, plans[0].recipe);
            Assert.AreEqual("Assets/Edits/Outfit_Shirt.asset", plans[0].path);
            CollectionAssert.AreEqual(new[] { mods[0], mods[3] }, plans[0].modifiers, "both shirt slots, in session order");
            Assert.AreSame(pants, plans[1].recipe);
            Assert.AreEqual("Assets/Edits/Outfit_Pants.asset", plans[1].path);
            Assert.IsNull(plans[2].recipe, "the body is in no wardrobe recipe");
            Assert.AreEqual("Assets/Edits/Outfit.asset", plans[2].path, "it keeps the chosen file");
        }

        [Test]
        public void EditsOnOneRecipeKeepTheChosenFile()
        {
            UMAWardrobeRecipe shirt = Recipe("Shirt");
            var plans = MeshModifierRecipeSavePlanner.PlanSaves(
                new List<MeshModifier.Modifier> { Modifier("ShirtSlot"), Modifier("ShirtSlot", "Paint") },
                key => shirt, "Assets/Edits/Shirt_MM.asset");
            Assert.AreEqual(1, plans.Count);
            Assert.AreEqual("Assets/Edits/Shirt_MM.asset", plans[0].path);
            Assert.AreSame(shirt, plans[0].recipe);

            // No recipe at all: one file, as before.
            plans = MeshModifierRecipeSavePlanner.PlanSaves(new List<MeshModifier.Modifier> { Modifier("Body") }, key => null, "Assets/Edits/Body.asset");
            Assert.AreEqual(1, plans.Count);
            Assert.IsNull(plans[0].recipe);
            Assert.AreEqual("Assets/Edits/Body.asset", plans[0].path);
        }

        [Test]
        public void ASlotIsFoundInTheWardrobeRecipeThatHoldsIt()
        {
            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            SlotDataAsset slot = null;
            if (indexer != null)
            {
                foreach (SlotDataAsset candidate in indexer.GetAllAssets<SlotDataAsset>())
                {
                    if (candidate != null && !candidate.isWildCardSlot && !candidate.isUtilitySlot && !string.IsNullOrEmpty(candidate.slotName)
                        && candidate.meshData != null && candidate.meshData.vertexCount > 0)
                    {
                        slot = candidate;
                        break;
                    }
                }
            }
            if (slot == null)
            {
                Assert.Ignore("The Global Library holds no slot with a mesh.");
            }

            UMAWardrobeRecipe wardrobe = Recipe("Wearing");
            wardrobe.recipeType = "Wardrobe";
            wardrobe.Save(new UMAData.UMARecipe { slotDataList = new[] { new SlotData(slot) } });
            UMAWardrobeRecipe other = Recipe("Other");
            other.recipeType = "Wardrobe";
            other.Save(new UMAData.UMARecipe { slotDataList = new SlotData[0] });

            var ownerOf = MeshModifierRecipeSavePlanner.SlotOwnerLookup(new UMATextRecipe[] { other, wardrobe });
            string key = string.IsNullOrEmpty(slot.sourceSlot) ? slot.slotName : slot.sourceSlot;
            Assert.AreSame(wardrobe, ownerOf(key), "by the logical slot name modifiers store");
            Assert.AreSame(wardrobe, ownerOf(key.ToUpperInvariant()), "names compare without case, as modifiers do");
            Assert.AreSame(wardrobe, ownerOf(slot.name), "by the asset name older modifiers stored");
            Assert.IsNull(ownerOf("UMATest_NoSuchSlot_4e21"));
        }

        [Test]
        public void ARecipeDoesNotApplyTheEditorSessionsOwnModifierAgain()
        {
            var avatarObject = new GameObject("MeshModifierRecipeSaveTests");
            created.Add(avatarObject);
            UMAData data = avatarObject.AddComponent<UMAData>();
            var saved = ScriptableObject.CreateInstance<MeshModifier>();
            created.Add(saved);
            saved.RuntimeModifiers = new List<MeshModifier.Modifier> { Modifier("ShirtSlot") };
            var other = ScriptableObject.CreateInstance<MeshModifier>();
            created.Add(other);
            other.RuntimeModifiers = new List<MeshModifier.Modifier> { Modifier("ShirtSlot", "Older") };
            UMAWardrobeRecipe shirt = Recipe("Shirt");
            shirt.MeshModifiers = new List<MeshModifier> { other, saved };

            data.EditorSessionMeshModifiers.Add(saved);
            data.AddMeshModifiers(shirt);
            Assert.AreEqual(1, data.Modifiers["ShirtSlot"].Count, "the session already applies the saved modifier");
            Assert.AreEqual("Older", data.Modifiers["ShirtSlot"][0].ModifierName);

            // After the editor closes, the recipe applies it as usual.
            data.EditorSessionMeshModifiers.Clear();
            data.Modifiers.Clear();
            data.AddMeshModifiers(shirt);
            Assert.AreEqual(2, data.Modifiers["ShirtSlot"].Count);
        }
    }
}
