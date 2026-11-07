using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class AssetLogicalNameTests
    {
        [TestCase(false, "no-mesh")]
        [TestCase(true, "no-mesh")]
        [TestCase(false, "no-shapes")]
        [TestCase(true, "no-shapes")]
        [TestCase(false, "unrelated-shape")]
        [TestCase(true, "unrelated-shape")]
        [TestCase(false, "already-baked")]
        [TestCase(true, "already-baked")]
        [TestCase(false, "logical-name")]
        [TestCase(true, "logical-name")]
        [TestCase(false, "legacy-name")]
        [TestCase(true, "legacy-name")]
        public void RecipeBakedSlotResolutionPreservesOrdinarySlotsAndQuietlyFindsBodySlots(bool wardrobe, string scenario)
        {
            var singleton = typeof(UMAAssetIndexer).GetField("theIndexer", BindingFlags.Static | BindingFlags.NonPublic);
            object previousIndexer = singleton.GetValue(null);
            var indexer = ScriptableObject.CreateInstance<UMAAssetIndexer>();
            var source = ScriptableObject.CreateInstance<SlotDataAsset>();
            var baked = ScriptableObject.CreateInstance<SlotDataAsset>();
            var race = ScriptableObject.CreateInstance<RaceData>();
            var messages = new System.Collections.Generic.List<string>();
            Application.LogCallback capture = (message, stack, type) => messages.Add(message);
            try
            {
                singleton.SetValue(null, indexer);
                race.name = "Recipe Bake Test Race";
                race.PrebakedBlendshapes.Add(new SlotBurnOptions { BlendShape = "BodyShape", value = 0.5f });
                source.name = "Body_slot";
                source._oldSlotName = "Body";
                source.meshData = new UMAMeshData
                {
                    vertexCount = 1, vertices = new[] { Vector3.zero },
                    blendShapes = new[] { new UMABlendShape { shapeName = "BodyShape" } }
                };
                if (scenario == "no-mesh") source.meshData = null;
                if (scenario == "no-shapes") source.meshData.blendShapes = null;
                if (scenario == "unrelated-shape") source.meshData.blendShapes[0].shapeName = "Blink";
                if (scenario == "already-baked") source._oldSlotName = "Body_baked_" + race.raceName;
                bool useBaked = scenario == "logical-name" || scenario == "legacy-name";
                baked.name = (scenario == "legacy-name" ? source.name : source.slotName) + "_baked_" + race.raceName;
                indexer.SerializedItems.Clear();
                // Even a stale indexed counterpart must not replace a slot without bake targets.
                indexer.SerializedItems.Add(new AssetItem(typeof(SlotDataAsset), baked.slotName, "", baked));
                indexer.DoInitialDictionaryLoad();
                var expected = useBaked ? baked : source;
                Application.logMessageReceived += capture;
                for (int repaint = 0; repaint < 3; repaint++)
                {
                    if (wardrobe)
                    {
                        var method = typeof(RecipeEditor).GetMethod("BakeSlotForRaceIfNeeded", BindingFlags.Static | BindingFlags.NonPublic);
                        Assert.That(method.Invoke(null, new object[] { source, race }), Is.SameAs(expected));
                    }
                    else
                    {
                        var method = typeof(SlotMasterEditor).GetMethod("ResolveBaseSlotAssetForRace", BindingFlags.Static | BindingFlags.NonPublic);
                        object[] args = { new SlotData(source), race, null };
                        Assert.That(method.Invoke(null, args), Is.SameAs(expected));
                        Assert.That(args[2], Is.EqualTo(expected.slotName));
                    }
                }
                Assert.That(messages, Is.Empty, "Optional baked-slot lookup must not log missing candidates on each repaint.");
                Assert.That(indexer.SerializedItems.Count, Is.EqualTo(1), "Resolving these slots must not create extra baked assets.");
            }
            finally
            {
                Application.logMessageReceived -= capture;
                Object.DestroyImmediate(indexer);
                singleton.SetValue(null, previousIndexer);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(race);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DecalSerializationAndTagsUseLogicalOverlayName(bool useSentOverlay)
        {
            var slotProperty = typeof(DecalSlotBuilder).GetProperty("LastCreatedDecalSlot");
            var overlayProperty = typeof(DecalSlotBuilder).GetProperty("LastCreatedDecalOverlayAsset");
            var sentProperty = typeof(DecalSlotBuilder).GetProperty("LastDecalOverlaySent");
            var previousSlot = slotProperty.GetValue(null);
            var previousOverlay = overlayProperty.GetValue(null);
            var previousSent = sentProperty.GetValue(null);
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            var material = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                slot.name = "Decal_slot";
                slot._oldSlotName = "Decal";
                slot.meshData = new UMAMeshData { vertexCount = 1, vertices = new[] { Vector3.zero } };
                overlay.name = "Mark_overlay";
                overlay._oldOverlayName = "Mark";
                overlay.material = material;
                slotProperty.SetValue(null, slot);
                overlayProperty.SetValue(null, overlay);
                sentProperty.SetValue(null, useSentOverlay ? overlay : null);
                typeof(DecalSlotBuilder).GetMethod("EnsureOverlayTag", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { slot });
                var json = (string)typeof(DecalSlotBuilder).GetMethod("SerializeDecalSlotToJson",
                    BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { slot, false });
                var data = JsonUtility.FromJson<RuntimeSlotData>(json);
                Assert.That(data.slotName, Is.EqualTo("Decal"));
                Assert.That(data.overlayAssetName, Is.EqualTo("Mark"));
                CollectionAssert.Contains(data.tags, "DecalOverlay:Mark");
                CollectionAssert.DoesNotContain(data.tags, "DecalOverlay:Mark_overlay");
            }
            finally
            {
                slotProperty.SetValue(null, previousSlot);
                overlayProperty.SetValue(null, previousOverlay);
                sentProperty.SetValue(null, previousSent);
                Object.DestroyImmediate(slot);
                Object.DestroyImmediate(overlay);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void EditorNameReadsDoNotPopulateLegacyFields()
        {
            Assert.That(Application.isPlaying, Is.False);
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                slot.name = "Body";
                overlay.name = "Skin";
                Assert.That(slot.slotName, Is.EqualTo("Body"));
                Assert.That(overlay.overlayName, Is.EqualTo("Skin"));
                Assert.That(slot._oldSlotName, Is.Null.Or.Empty);
                Assert.That(overlay._oldOverlayName, Is.Null.Or.Empty);
                slot.name = "RenamedBody";
                overlay.name = "RenamedSkin";
                Assert.That(slot.slotName, Is.EqualTo("RenamedBody"));
                Assert.That(overlay.overlayName, Is.EqualTo("RenamedSkin"));
                Assert.That(slot._oldSlotName, Is.Null.Or.Empty);
                Assert.That(overlay._oldOverlayName, Is.Null.Or.Empty);
            }
            finally { Object.DestroyImmediate(slot); Object.DestroyImmediate(overlay); }
        }

        [Test]
        public void SourceAndBakedNamesUseLogicalSlotIdentity()
        {
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            var race = ScriptableObject.CreateInstance<RaceData>();
            try
            {
                slot.name = "Body_slot";
                slot._oldSlotName = "Body";
                race.name = "TestRace";
                Assert.That(slot.sourceSlot, Is.EqualTo("Body"));
                var method = typeof(SlotMasterEditor).GetMethod("GetBakedSlotAssetName",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(method.Invoke(null, new object[] { slot, race }), Is.EqualTo("Body_baked_TestRace"));
                var candidates = typeof(SlotMasterEditor).GetMethod("GetBakedSlotNameCandidates",
                    BindingFlags.Static | BindingFlags.NonPublic);
                CollectionAssert.AreEqual(new[] { "Body_baked_TestRace", "Body_slot_baked_TestRace" },
                    (System.Collections.IEnumerable)candidates.Invoke(null, new object[] { slot, race }));
            }
            finally { Object.DestroyImmediate(slot); Object.DestroyImmediate(race); }
        }

        [TestCase(null)]
        [TestCase("OriginalBody")]
        public void InMemoryCloneUsesRequestedLogicalName(string legacyName)
        {
            var source = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset clone = null;
            try
            {
                source.name = "OriginalBody_slot";
                source._oldSlotName = legacyName;
                clone = source.Clone("Copy_slot", "Copy", false);
                Assert.That(clone.name, Is.EqualTo("Copy_slot"));
                Assert.That(clone.slotName, Is.EqualTo("Copy"));
                Assert.That(clone.sourceSlot, Is.EqualTo("Copy"));
                Assert.That(clone.nameHash, Is.EqualTo(UMAUtils.StringToHash("Copy")));
                Assert.That(source._oldSlotName, Is.EqualTo(legacyName));
            }
            finally { Object.DestroyImmediate(clone); Object.DestroyImmediate(source); }
        }

        [TestCase(null)]
        [TestCase("Replacement")]
        public void AssignReplacesStaleNameHashAndSource(string legacyName)
        {
            var source = ScriptableObject.CreateInstance<SlotDataAsset>();
            var destination = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                destination.name = "Previous";
                destination.PrepareForAssetPath("Assets/Previous_slot.asset", "Previous");
                Assert.That(destination.nameHash, Is.EqualTo(UMAUtils.StringToHash("Previous")));
                source.name = "Replacement_slot";
                source._oldSlotName = legacyName;
                destination.Assign(source);
                string expected = legacyName ?? "Replacement_slot";
                Assert.That(destination.slotName, Is.EqualTo(expected));
                Assert.That(destination.sourceSlot, Is.EqualTo(expected));
                Assert.That(destination.nameHash, Is.EqualTo(UMAUtils.StringToHash(expected)));
                Assert.That(destination._oldSlotName, Is.EqualTo(legacyName));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(destination); }
        }
    }
}
