using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace UMA.Tests
{
    /// <summary>
    /// The vertex editor stage's Slots panel: one Show/Edit list drives every tool.
    /// </summary>
    public class VertexStageSlotTargetsTests
    {
        private static SlotData Slot(string name) => SlotData.CreatePlaceholder(name, Array.Empty<string>());

        private static List<SlotData> Slots(params string[] names) => names.Select(Slot).ToList();

        private static string[] Names(IEnumerable<SlotData> slots) => slots.Select(slot => slot.slotName).ToArray();

        private static readonly Func<SlotData, bool> AnySlot = slot => true;

        [Test]
        public void EverySlotThatCanBeEditedStartsEditable()
        {
            var targets = new VertexStageSlotTargets();
            var slots = Slots("Body", "Shirt", "Pants");
            Assert.That(Names(targets.GetEditable(slots, AnySlot)), Is.EqualTo(new[] { "Body", "Shirt", "Pants" }));
        }

        [Test]
        public void HiddenUtilityOrEmptySlotsAreNeverEditable()
        {
            var targets = new VertexStageSlotTargets();
            var slots = Slots("Body", "HiddenShirt", "Utility");
            // The stage's rule: hidden, utility and empty-mesh slots cannot be edited.
            Func<SlotData, bool> canEdit = slot => slot.slotName == "Body";
            Assert.That(Names(targets.GetEditable(slots, canEdit)), Is.EqualTo(new[] { "Body" }));
            Assert.That(targets.IsEditable(slots[1], canEdit), Is.False);
            Assert.That(targets.IsEditable(null, AnySlot), Is.False);
        }

        [Test]
        public void HidingAndShowingASlotRemembersWhetherItWasLocked()
        {
            var targets = new VertexStageSlotTargets();
            var slots = Slots("Body", "Shirt");
            targets.SetEditable("Body", false);
            bool shirtVisible = false;
            Func<SlotData, bool> canEdit = slot => slot.slotName != "Shirt" || shirtVisible;

            Assert.That(Names(targets.GetEditable(slots, canEdit)), Is.Empty, "A hidden slot is not editable.");
            shirtVisible = true;
            Assert.That(Names(targets.GetEditable(slots, canEdit)), Is.EqualTo(new[] { "Shirt" }),
                "Shown again, an unlocked slot is editable and a locked one stays locked.");
        }

        [Test]
        public void SlotsAddedLaterAreEditableByDefault()
        {
            var targets = new VertexStageSlotTargets();
            targets.Solo("Body", Slots("Body", "Shirt"));
            var withNewSlot = Slots("Body", "Shirt", "Hat");
            Assert.That(Names(targets.GetEditable(withNewSlot, AnySlot)), Is.EqualTo(new[] { "Body", "Hat" }));
        }

        [Test]
        public void SoloMakesOneSlotEditableAndAllRestoresTheRest()
        {
            var targets = new VertexStageSlotTargets();
            var slots = Slots("Body", "Shirt", "Pants");
            targets.Solo("Shirt", slots);
            Assert.That(Names(targets.GetEditable(slots, AnySlot)), Is.EqualTo(new[] { "Shirt" }));
            targets.EditAll();
            Assert.That(Names(targets.GetEditable(slots, AnySlot)), Is.EqualTo(new[] { "Body", "Shirt", "Pants" }));
        }

        [Test]
        public void NoneLocksEverySlotAndInvertSkipsSlotsThatCannotBeEdited()
        {
            var targets = new VertexStageSlotTargets();
            var slots = Slots("Body", "Shirt", "Hidden");
            targets.EditNone(slots);
            Assert.That(Names(targets.GetEditable(slots, AnySlot)), Is.Empty);

            Func<SlotData, bool> canEdit = slot => slot.slotName != "Hidden";
            targets.Invert(slots, canEdit);
            Assert.That(Names(targets.GetEditable(slots, canEdit)), Is.EqualTo(new[] { "Body", "Shirt" }));
            Assert.That(targets.IsLocked("Hidden"), Is.True, "Invert must not change slots that cannot be edited.");
        }

        [Test]
        public void EditOnlySelectsExactlyTheMatchingSlots()
        {
            var targets = new VertexStageSlotTargets();
            var slots = Slots("Body", "Eyes", "Shirt", "Pants");
            var baseSlots = new HashSet<string> { "Body", "Eyes" };
            targets.EditOnly(slots, slot => !baseSlots.Contains(slot.slotName));
            Assert.That(Names(targets.GetEditable(slots, AnySlot)), Is.EqualTo(new[] { "Shirt", "Pants" }));
        }

        [Test]
        public void VersionChangesOnlyWhenTheEditableSetChanges()
        {
            var targets = new VertexStageSlotTargets();
            var slots = Slots("Body", "Shirt");
            int version = targets.Version;

            targets.SetEditable("Body", true);
            targets.EditAll();
            Assert.That(targets.Version, Is.EqualTo(version), "No-op changes must not refresh the tools.");

            targets.SetEditable("Body", false);
            Assert.That(targets.Version, Is.EqualTo(version + 1));
            targets.SetEditable("Body", false);
            Assert.That(targets.Version, Is.EqualTo(version + 1));
            targets.Solo("Shirt", slots);
            Assert.That(targets.Version, Is.EqualTo(version + 1), "Solo on an already-solo slot is a no-op.");
            targets.EditAll();
            Assert.That(targets.Version, Is.EqualTo(version + 2));
        }

        [Test]
        public void EmptyNamesAndNullSlotsAreIgnored()
        {
            var targets = new VertexStageSlotTargets();
            int version = targets.Version;
            targets.SetEditable(null, false);
            targets.SetEditable(string.Empty, false);
            targets.Solo(null, Slots("Body"));
            targets.EditNone(new SlotData[] { null });
            Assert.That(targets.Version, Is.EqualTo(version));
            Assert.That(targets.GetEditable(null, AnySlot), Is.Empty);
        }
    }
}
