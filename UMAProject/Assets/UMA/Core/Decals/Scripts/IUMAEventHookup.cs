using UnityEngine;

namespace UMA
{
    public interface IUMAEventHookup
    {
        void HookupEvents(SlotDataAsset slot);
        void Begun(UMAData umaData);
        void Completed(UMAData umaData,GameObject slotObject);
    }
}
