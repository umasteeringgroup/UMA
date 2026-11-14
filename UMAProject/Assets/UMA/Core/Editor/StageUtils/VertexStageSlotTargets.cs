using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Which slots the vertex editor stage may edit. A single list drives every
    /// tool (selection, raycast, sculpt, vertex paint), replacing the separate
    /// per-tool slot pickers.
    ///
    /// The model stores the slots the user has <em>locked</em> rather than the
    /// ones they enabled, so slots added to the recipe later (or shown again
    /// after being hidden) are editable by default. A slot is editable only when
    /// the stage says it can be edited at all (visible, has mesh data, not a
    /// utility slot) and it is not locked. Hiding a slot therefore makes it
    /// uneditable without forgetting the user's choice.
    /// </summary>
    [Serializable]
    public sealed class VertexStageSlotTargets
    {
        [SerializeField] private List<string> lockedSlotNames = new List<string>();

        /// <summary>Incremented on every change, so tools can tell when to refresh.</summary>
        public int Version { get; private set; }

        public bool IsLocked(string slotName)
        {
            return !string.IsNullOrEmpty(slotName) && lockedSlotNames.Contains(slotName);
        }

        /// <summary>True when the slot can be edited and the user has not locked it.</summary>
        public bool IsEditable(SlotData slot, Func<SlotData, bool> canEdit)
        {
            return slot != null && canEdit != null && canEdit(slot) && !IsLocked(slot.slotName);
        }

        /// <summary>Editable slots, in recipe order.</summary>
        public List<SlotData> GetEditable(IEnumerable<SlotData> slots, Func<SlotData, bool> canEdit)
        {
            var result = new List<SlotData>();
            if (slots == null) return result;
            foreach (SlotData slot in slots)
            {
                if (IsEditable(slot, canEdit)) result.Add(slot);
            }
            return result;
        }

        public void SetEditable(string slotName, bool editable)
        {
            if (string.IsNullOrEmpty(slotName)) return;
            bool changed = editable ? lockedSlotNames.Remove(slotName) : AddLock(slotName);
            if (changed) Version++;
        }

        /// <summary>Unlock every slot.</summary>
        public void EditAll()
        {
            if (lockedSlotNames.Count == 0) return;
            lockedSlotNames.Clear();
            Version++;
        }

        /// <summary>Lock every slot in the recipe.</summary>
        public void EditNone(IEnumerable<SlotData> slots)
        {
            bool changed = false;
            foreach (SlotData slot in Named(slots)) changed |= AddLock(slot.slotName);
            if (changed) Version++;
        }

        /// <summary>Flip editable/locked for every slot that can be edited.</summary>
        public void Invert(IEnumerable<SlotData> slots, Func<SlotData, bool> canEdit)
        {
            bool changed = false;
            foreach (SlotData slot in Named(slots))
            {
                if (canEdit == null || !canEdit(slot)) continue;
                if (!lockedSlotNames.Remove(slot.slotName)) AddLock(slot.slotName);
                changed = true;
            }
            if (changed) Version++;
        }

        /// <summary>Make only <paramref name="slotName"/> editable.</summary>
        public void Solo(string slotName, IEnumerable<SlotData> slots)
        {
            if (string.IsNullOrEmpty(slotName)) return;
            bool changed = lockedSlotNames.Remove(slotName);
            foreach (SlotData slot in Named(slots))
            {
                if (slot.slotName != slotName) changed |= AddLock(slot.slotName);
            }
            if (changed) Version++;
        }

        /// <summary>Make editable exactly the slots matching <paramref name="include"/>.</summary>
        public void EditOnly(IEnumerable<SlotData> slots, Func<SlotData, bool> include)
        {
            bool changed = false;
            foreach (SlotData slot in Named(slots))
            {
                changed |= include != null && include(slot)
                    ? lockedSlotNames.Remove(slot.slotName)
                    : AddLock(slot.slotName);
            }
            if (changed) Version++;
        }

        private bool AddLock(string slotName)
        {
            if (lockedSlotNames.Contains(slotName)) return false;
            lockedSlotNames.Add(slotName);
            return true;
        }

        private static IEnumerable<SlotData> Named(IEnumerable<SlotData> slots)
        {
            if (slots == null) yield break;
            foreach (SlotData slot in slots)
            {
                if (slot != null && !string.IsNullOrEmpty(slot.slotName)) yield return slot;
            }
        }
    }
}
