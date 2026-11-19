using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// A DNA change on a loaded character is applied by the build types its effects report:
    /// a pass can be limited to those effects, and DNA mesh modifiers can be replaced on their own.
    /// </summary>
    public class DNABuildTypeTests
    {
        private class TypedEffect : DNAEffect
        {
            public DNAInstanceCollection.DNABuildType area;
            public int afterRecipeCalls;
            public int postApplyCalls;

            public override DNAInstanceCollection.DNABuildType AreaEffect => area;

            public override void AfterRecipeGenerated(UMAData avatar, DNA dna, float value)
            {
                afterRecipeCalls++;
            }

            public override void PostApply(UMAData avatar, DNA dna, float value)
            {
                postApplyCalls++;
            }
        }

        private DNA dna;
        private GameObject avatarObject;

        [SetUp]
        public void SetUp()
        {
            dna = ScriptableObject.CreateInstance<DNA>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(dna);
            if (avatarObject != null)
            {
                Object.DestroyImmediate(avatarObject);
            }
        }

        private static TypedEffect Effect(DNAInstanceCollection.DNABuildType area)
        {
            return new TypedEffect { area = area };
        }

        private static MeshModifier.Modifier Modifier(string slot)
        {
            return new MeshModifier.Modifier { SlotName = slot };
        }

        [Test]
        public void AfterRecipePassCanBeLimitedToBuildTypes()
        {
            TypedEffect color = Effect(DNAInstanceCollection.DNABuildType.Texture | DNAInstanceCollection.DNABuildType.SharedColors);
            TypedEffect modifier = Effect(DNAInstanceCollection.DNABuildType.MeshModifiers);
            TypedEffect untyped = Effect(DNAInstanceCollection.DNABuildType.None);
            dna.effects.Add(color);
            dna.effects.Add(modifier);
            dna.effects.Add(untyped);

            DNAInstanceCollection.DNABuildType flags = dna.AfterRecipeGeneration(
                null, 0.75f, false, DNAInstanceCollection.DNABuildType.SharedColors);

            Assert.AreEqual(1, color.afterRecipeCalls);
            Assert.AreEqual(0, modifier.afterRecipeCalls, "A mesh modifier effect must not run again for a color change.");
            Assert.AreEqual(0, untyped.afterRecipeCalls);
            Assert.AreEqual(color.area, flags);
        }

        [Test]
        public void UnlimitedPassRunsEveryEffect()
        {
            TypedEffect color = Effect(DNAInstanceCollection.DNABuildType.SharedColors);
            TypedEffect untyped = Effect(DNAInstanceCollection.DNABuildType.None);
            dna.effects.Add(color);
            dna.effects.Add(untyped);

            dna.AfterRecipeGeneration(null, 0.75f);
            dna.PostApply(null, 0.75f);

            Assert.AreEqual(1, color.afterRecipeCalls);
            Assert.AreEqual(1, untyped.afterRecipeCalls, "Effects that report no build type still run in a normal pass.");
            Assert.AreEqual(1, color.postApplyCalls);
            Assert.AreEqual(1, untyped.postApplyCalls);
        }

        [Test]
        public void PostApplyPassCanBeLimitedToBuildTypes()
        {
            TypedEffect blendShape = Effect(DNAInstanceCollection.DNABuildType.BlendShape);
            TypedEffect rig = Effect(DNAInstanceCollection.DNABuildType.Rig);
            dna.effects.Add(blendShape);
            dna.effects.Add(rig);

            dna.PostApply(null, 0.5f, DNAInstanceCollection.DNABuildType.BlendShape);

            Assert.AreEqual(1, blendShape.postApplyCalls);
            Assert.AreEqual(0, rig.postApplyCalls);
        }

        [Test]
        public void EffectsReportTheBuildTypeTheyNeed()
        {
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.MeshModifiers, new DNAEffect_MeshModifier().AreaEffect);
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.BlendShape, new DNAEffect_BlendShape().AreaEffect);
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.Rig, new DNAEffect_BonePose().AreaEffect);
            Assert.AreEqual(DNAInstanceCollection.DNABuildType.MaterialProperty, new DNAEffect_RuntimeMaterialProperty().AreaEffect);
        }

        [Test]
        public void RendererOnlyPassRunsBlendShapeAndMaterialPropertyEffectsOnly()
        {
            TypedEffect blendShape = Effect(DNAInstanceCollection.DNABuildType.BlendShape);
            TypedEffect material = Effect(DNAInstanceCollection.DNABuildType.MaterialProperty);
            TypedEffect rig = Effect(DNAInstanceCollection.DNABuildType.Rig);
            TypedEffect color = Effect(DNAInstanceCollection.DNABuildType.Texture | DNAInstanceCollection.DNABuildType.SharedColors);
            dna.effects.Add(blendShape);
            dna.effects.Add(material);
            dna.effects.Add(rig);
            dna.effects.Add(color);

            dna.PostApply(null, 0.5f, DNAInstanceCollection.DNABuildType.RendererOnly);

            Assert.AreEqual(1, blendShape.postApplyCalls);
            Assert.AreEqual(1, material.postApplyCalls);
            Assert.AreEqual(0, rig.postApplyCalls);
            Assert.AreEqual(0, color.postApplyCalls);
        }

        [Test]
        public void TextureEffectsRunInTheSameAfterRecipePassAsColorEffects()
        {
            // An overlay transform reports Texture only. A DNA change must still run it again,
            // in the pass a color effect takes part in.
            const DNAInstanceCollection.DNABuildType drawn =
                DNAInstanceCollection.DNABuildType.Texture | DNAInstanceCollection.DNABuildType.SharedColors;
            TypedEffect overlay = Effect(DNAInstanceCollection.DNABuildType.Texture);
            TypedEffect color = Effect(drawn);
            TypedEffect modifier = Effect(DNAInstanceCollection.DNABuildType.MeshModifiers);
            dna.effects.Add(overlay);
            dna.effects.Add(color);
            dna.effects.Add(modifier);

            dna.AfterRecipeGeneration(null, 0.75f, false, drawn);

            Assert.AreEqual(1, overlay.afterRecipeCalls);
            Assert.AreEqual(1, color.afterRecipeCalls);
            Assert.AreEqual(0, modifier.afterRecipeCalls);
            Assert.AreNotEqual(0, (int)(new DNAEffect_OverlayUVTransform().AreaEffect & drawn));
        }

        [Test]
        public void DNAMeshModifiersAreReplacedWithoutTouchingRecipeModifiers()
        {
            avatarObject = new GameObject("DNABuildTypeTests");
            UMAData data = avatarObject.AddComponent<UMAData>();
            MeshModifier.Modifier replaced = Modifier("Body");
            MeshModifier.Modifier firstDna = Modifier("Body");
            MeshModifier.Modifier otherSlotDna = Modifier("Head");
            MeshModifier.Modifier recipe = Modifier("Body");

            // The order a character load uses: replaced items, DNA effects, then the recipes.
            data.AddMeshModifiers(new List<MeshModifier.Modifier> { replaced });
            data.AddDNAMeshModifiers(new List<MeshModifier.Modifier> { firstDna, otherSlotDna });
            data.AddMeshModifiers(new List<MeshModifier.Modifier> { recipe });

            data.RemoveDNAMeshModifiers();

            CollectionAssert.AreEqual(new[] { replaced, recipe }, data.Modifiers["Body"]);
            Assert.AreEqual(0, data.Modifiers["Head"].Count);

            MeshModifier.Modifier secondDna = Modifier("Body");
            MeshModifier.Modifier thirdDna = Modifier("Body");
            data.AddDNAMeshModifiers(new List<MeshModifier.Modifier> { secondDna, thirdDna });

            CollectionAssert.AreEqual(new[] { replaced, secondDna, thirdDna, recipe }, data.Modifiers["Body"],
                "Replacement DNA modifiers must go back where the old ones were.");

            // A second change replaces the new ones again, and leaves no copies behind.
            data.RemoveDNAMeshModifiers();
            MeshModifier.Modifier fourthDna = Modifier("Body");
            data.AddDNAMeshModifiers(new List<MeshModifier.Modifier> { fourthDna });

            CollectionAssert.AreEqual(new[] { replaced, fourthDna, recipe }, data.Modifiers["Body"]);
        }

        [Test]
        public void ClearingModifiersForgetsDNAModifiers()
        {
            avatarObject = new GameObject("DNABuildTypeTests");
            UMAData data = avatarObject.AddComponent<UMAData>();
            data.AddDNAMeshModifiers(new List<MeshModifier.Modifier> { Modifier("Body") });

            data.ClearModifiers();
            MeshModifier.Modifier recipe = Modifier("Body");
            data.AddMeshModifiers(new List<MeshModifier.Modifier> { recipe });
            data.RemoveDNAMeshModifiers();

            CollectionAssert.AreEqual(new[] { recipe }, data.Modifiers["Body"]);
        }
    }
}
