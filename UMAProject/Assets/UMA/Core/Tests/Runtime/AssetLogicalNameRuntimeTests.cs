using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class AssetLogicalNameRuntimeTests
    {
        [Test]
        public void RuntimeNameReadsCacheBeforeSubsequentManagedAccess()
        {
            Assert.That(Application.isPlaying, Is.True);
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                slot.name = "RuntimeBody";
                overlay.name = "RuntimeSkin";
                Assert.That(slot.slotName, Is.EqualTo("RuntimeBody"));
                Assert.That(overlay.overlayName, Is.EqualTo("RuntimeSkin"));
                Assert.That(slot._oldSlotName, Is.EqualTo("RuntimeBody"));
                Assert.That(overlay._oldOverlayName, Is.EqualTo("RuntimeSkin"));
                // Object.name is a main-thread-only Unity call. Cached getters must not call it.
                var names = Task.Run(() => new[] { slot.slotName, overlay.overlayName }).GetAwaiter().GetResult();
                CollectionAssert.AreEqual(new[] { "RuntimeBody", "RuntimeSkin" }, names);
            }
            finally { Object.DestroyImmediate(slot); Object.DestroyImmediate(overlay); }
        }

        [Test]
        public void RuntimeReadsPreserveExplicitLogicalNames()
        {
            Assert.That(Application.isPlaying, Is.True);
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                slot.name = "Body_slot";
                slot._oldSlotName = "Body";
                overlay.name = "Skin_overlay";
                overlay._oldOverlayName = "Skin";
                Assert.That(slot.slotName, Is.EqualTo("Body"));
                Assert.That(overlay.overlayName, Is.EqualTo("Skin"));
                Assert.That(slot._oldSlotName, Is.EqualTo("Body"));
                Assert.That(overlay._oldOverlayName, Is.EqualTo("Skin"));
                Assert.That(slot.sourceSlot, Is.EqualTo("Body"));
            }
            finally { Object.DestroyImmediate(slot); Object.DestroyImmediate(overlay); }
        }
    }
}
