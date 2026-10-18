using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class AssetLogicalNameTests
    {
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
