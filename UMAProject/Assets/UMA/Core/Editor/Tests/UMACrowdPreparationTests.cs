#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    public class UMACrowdPreparationTests
    {
        private UMATextRecipe recipe;
        private static UMAPackedRecipeBase.UMAPackRecipe Prepared(UMATextRecipe source) =>
            (UMAPackedRecipeBase.UMAPackRecipe)typeof(UMATextRecipe).GetMethod("PackedLoadForUnpack", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(source, null);

        [SetUp]
        public void Setup()
        {
            recipe = ScriptableObject.CreateInstance<UMATextRecipe>();
            recipe.PackedSave(new UMAPackedRecipeBase.UMAPackRecipe
            {
                version = 3, race = "Prepared recipe test", isWardrobe = true,
                slotsV3 = new[] { new UMAPackedRecipeBase.PackedSlotDataV3
                {
                    id = "Carrier", isPlaceholderSlot = true, copyIdx = -1,
                    Tags = new[] { "Original" }, Races = new[] { "Original race" }
                } },
                sharedColorCount = 1,
                fColors = new[] { new UMAPackedRecipeBase.PackedOverlayColorDataV3(new OverlayColorData(1) { name = "Skin", color = Color.red }) }
            });
        }

        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(recipe);

        [Test]
        public void PrivatePreparationReusesOnlyParsedInputAndPublicPackedLoadStaysEditable()
        {
            var first = Prepared(recipe);
            var second = Prepared(recipe);
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.slotsV3, Is.SameAs(second.slotsV3));
            first.isWardrobe = false;
            Assert.That(Prepared(recipe).isWardrobe, Is.True);
            recipe.PackedLoad().slotsV3[0].id = "Changed public copy";
            Assert.That(Prepared(recipe).slotsV3[0].id, Is.EqualTo("Carrier"));
        }

        [Test]
        public void PreparedDnaValuesArePrivateAndSerializedEditsInvalidateThem()
        {
            var collection = new DNAInstanceCollection();
            collection.dnaInstances.Add(new DNAInstance("height", .4f, null) { enabled = false });
            var packed = new UMAPackedRecipeBase.UMAPackedDna { dnaType = nameof(UMADnaInstance),
                packedDna = UMADnaInstance.SaveInstance(new UMADnaInstance(collection)) };
            var list = new System.Collections.Generic.List<UMAPackedRecipeBase.UMAPackedDna> { packed };
            var a = (UMADnaInstance)UMAPackedRecipeBase.UnPackDNA(list)[0];
            var b = (UMADnaInstance)UMAPackedRecipeBase.UnPackDNA(list)[0];
            Assert.That(a.DNAInstances, Is.Not.SameAs(b.DNAInstances));
            a.DNAInstances.dnaInstances[0].Value = .9f;
            Assert.That(b.GetValue(0), Is.EqualTo(.4f));
            Assert.That(b.DNAInstances.dnaInstances[0].enabled, Is.False);
            collection.dnaInstances[0].Value = .7f;
            packed.packedDna = UMADnaInstance.SaveInstance(new UMADnaInstance(collection));
            Assert.That(UMAPackedRecipeBase.UnPackDNA(list)[0].GetValue(0), Is.EqualTo(.7f));
        }

        [Test]
        public void DirectStringEditsAndSetBytesInvalidatePreparation()
        {
            var old = Prepared(recipe);
            var edit = recipe.PackedLoad(); edit.slotsV3[0].id = "Edited";
            recipe.recipeString = JsonUtility.ToJson(edit);
            Assert.That(Prepared(recipe).slotsV3[0].id, Is.EqualTo("Edited"));
            Assert.That(Prepared(recipe).slotsV3, Is.Not.SameAs(old.slotsV3));
            edit.slotsV3[0].id = "Bytes";
            recipe.SetBytes(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(edit)));
            Assert.That(Prepared(recipe).slotsV3[0].id, Is.EqualTo("Bytes"));
        }

        public sealed class CustomTextRecipe : UMATextRecipe
        {
            public int LoadCalls;
            public override UMAPackRecipe PackedLoad() { LoadCalls++; return base.PackedLoad(); }
        }

        [Test]
        public void CustomPackedLoadOverrideIsNotBypassedByPreparation()
        {
            var custom = ScriptableObject.CreateInstance<CustomTextRecipe>();
            try
            {
                custom.recipeString = recipe.recipeString;
                var first = Prepared(custom); var second = Prepared(custom);
                Assert.That(custom.LoadCalls, Is.EqualTo(2));
                Assert.That(first.slotsV3, Is.Not.SameAs(second.slotsV3));
            }
            finally { UnityEngine.Object.DestroyImmediate(custom); }
        }

        [Test]
        public void LegacyPackedDataRetainsIndependentLoadBehavior()
        {
            var legacy = recipe.PackedLoad(); legacy.version = 2; recipe.PackedSave(legacy);
            Assert.That(Prepared(recipe).slotsV3, Is.Not.SameAs(Prepared(recipe).slotsV3));
        }

        [Test]
        public void UnpackedCharactersDoNotShareMutableSlotTagsRacesOrColors()
        {
            var first = new UMAData.UMARecipe(); recipe.Load(first);
            first.slotDataList[0].tags[0] = "Changed";
            first.slotDataList[0].Races[0] = "Changed";
            first.sharedColors[0].color = Color.blue;
            var second = new UMAData.UMARecipe(); recipe.Load(second);
            Assert.That(second.slotDataList[0].tags[0], Is.EqualTo("Original"));
            Assert.That(second.slotDataList[0].Races[0], Is.EqualTo("Original race"));
            Assert.That(second.sharedColors[0].color, Is.EqualTo(Color.red));
            Assert.That(second.slotDataList[0], Is.Not.SameAs(first.slotDataList[0]));
        }

        [Test]
        public void PreparedShaderPropertiesArePrivateAndDetectInPlaceStringArrayEdits()
        {
            var color = new OverlayColorData(1);
            color.SetColorProperty("_Tint", Color.red);
            var packed = new UMAPackedRecipeBase.PackedOverlayColorDataV3(color);
            var first = new OverlayColorData(); packed.SetOverlayColorData(first);
            ((UMAColorProperty)first.PropertyBlock.shaderProperties[0]).Value = Color.blue;
            var second = new OverlayColorData(); packed.SetOverlayColorData(second);
            Assert.That(((UMAColorProperty)second.PropertyBlock.shaderProperties[0]).Value, Is.EqualTo(Color.red));
            packed.ShaderParms[0] = new UMAColorProperty { name = "_Tint", Value = Color.green }.ToString();
            packed.SetOverlayColorData(second);
            Assert.That(((UMAColorProperty)second.PropertyBlock.shaderProperties[0]).Value, Is.EqualTo(Color.green));
        }

        [Test]
        public void RuntimeOnlyPropertyWithNoArrayKeepsLegacyUnpackBehavior()
        {
            var packed = new UMAPackedRecipeBase.PackedOverlayColorDataV3(new OverlayColorData(1));
            packed.ShaderParms = new[] { "VectorArray;unused;_Array" };
            var color = new OverlayColorData();
            Assert.DoesNotThrow(() => packed.SetOverlayColorData(color));
            Assert.That(color.PropertyBlock.shaderProperties[0], Is.TypeOf<UMAVectorArrayProperty>());
        }

        private sealed class SpawnProbe : System.Collections.IEnumerator, IDisposable
        {
            internal float Value;
            internal bool Disposed, Throw;
            public object Current => null;
            public bool MoveNext()
            {
                if (Throw) throw new InvalidOperationException("test failure");
                Value = UnityEngine.Random.value;
                return true;
            }
            public void Reset() => throw new NotSupportedException();
            public void Dispose() => Disposed = true;
        }

        [TestCase(false)] [TestCase(true)]
        public void SlicedSpawningIsolatesRandomStateAndDisposesOnCancelOrFailure(bool fail)
        {
            var savedRandom = UnityEngine.Random.state;
            var go = new GameObject("Spawn scheduling test"); go.SetActive(false);
            try
            {
                var crowd = go.AddComponent<UMARandomAvatar>(); crowd.SpawnBudgetMilliseconds = float.Epsilon;
                var probe = new SpawnProbe();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                UnityEngine.Random.InitState(13);
                typeof(UMARandomAvatar).GetField("spawnRandomState", flags).SetValue(crowd, UnityEngine.Random.state);
                float expectedFirst = UnityEngine.Random.value;
                float expectedSecond = UnityEngine.Random.value;
                typeof(UMARandomAvatar).GetField("spawnSequence", flags).SetValue(crowd, probe);
                var advance = typeof(UMARandomAvatar).GetMethod("AdvanceSpawning", flags);
                UnityEngine.Random.InitState(79);
                var callerState = UnityEngine.Random.state;
                advance.Invoke(crowd, new object[] { true });
                Assert.That(probe.Value, Is.EqualTo(expectedFirst));
                Assert.That(UnityEngine.Random.state, Is.EqualTo(callerState));
                UnityEngine.Random.InitState(321); // Unrelated game code between frames.
                callerState = UnityEngine.Random.state;
                probe.Throw = fail;
                if (fail) Assert.Throws<TargetInvocationException>(() => advance.Invoke(crowd, new object[] { true }));
                else
                {
                    advance.Invoke(crowd, new object[] { true });
                    Assert.That(probe.Value, Is.EqualTo(expectedSecond));
                    crowd.CancelSpawning();
                }
                Assert.That(UnityEngine.Random.state, Is.EqualTo(callerState));
                Assert.That(crowd.IsGenerating, Is.False);
                Assert.That(probe.Disposed, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Random.state = savedRandom; }
        }
    }
}
#endif
