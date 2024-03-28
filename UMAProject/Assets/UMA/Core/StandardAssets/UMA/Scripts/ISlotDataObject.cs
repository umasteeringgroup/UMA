using UnityEngine;

namespace UMA
{
    public interface ISlotDataObject
    {
        void CharacterBegun(UMAData umaData);
        void CharacterCompleted(UMAData umaData);
        void DNAApplied(UMAData umaData);
        void SlotAtlassed(UMAData umaData, SlotData slotData, Material material, Rect rect);
    }
}
