#if UNITY_EDITOR
using System;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    public sealed class UMAResourceUsageSampleSceneTests
    {
        [Test]
        public void NPCRecipeCopyKeepsInternalAliasesButNotMutableSourceData()
        {
            var overlay = (OverlayData)Activator.CreateInstance(typeof(OverlayData), true);
            overlay.colorData = new OverlayColorData(1);
            var first = new SlotData(); var second = new SlotData();
            var overlays = new System.Collections.Generic.List<OverlayData> { overlay };
            first.SetOverlayList(overlays); second.SetOverlayList(overlays);
            var adjustments = new VertexDeltaAdjustmentCollection();
            adjustments.Add(new VertexDeltaAdjustment { vertexIndex = 3, delta = Vector3.up });
            first.meshModifiers.Add(new MeshModifier.Modifier { SlotName = "test", Scale = .5f, adjustments = adjustments });
            var recipe = new UMAData.UMARecipe { slotDataList = new[] { first, second }, sharedColors = new[] { overlay.colorData } };
            var method = typeof(UMAData).Assembly.GetType("UMA.UMANPCSnapshot").GetMethod("CloneRecipe",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var clone = (UMAData.UMARecipe)method.Invoke(null, new object[] { recipe });
            Assert.That(clone.slotDataList[0].GetOverlayList(), Is.SameAs(clone.slotDataList[1].GetOverlayList()));
            Assert.That(clone.slotDataList[0].GetOverlayList(), Is.Not.SameAs(overlays));
            Assert.That(clone.slotDataList[0].GetOverlay(0).colorData, Is.SameAs(clone.sharedColors[0]));
            Assert.That(clone.sharedColors[0], Is.Not.SameAs(overlay.colorData));
            var modifier = clone.slotDataList[0].meshModifiers[0];
            modifier.Scale = 1;
            ((VertexDeltaAdjustment)modifier.adjustments.vertexAdjustments[0]).delta = Vector3.left;
            Assert.That(first.meshModifiers[0].Scale, Is.EqualTo(.5f));
            Assert.That(((VertexDeltaAdjustment)adjustments.vertexAdjustments[0]).delta, Is.EqualTo(Vector3.up));
        }
    }
}
#endif
