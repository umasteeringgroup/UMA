using System;
using UnityEngine;
using UnityEngine.Events;

namespace UMA
{
    [Serializable]
    public class UMADataEvent : UnityEvent<UMAData>
    {
        public UMADataEvent()
        {
        }
    }

    [Serializable]
    public class UMADataSlotEvent : UnityEvent<UMAData, SlotData>
    {
        public UMADataSlotEvent()
        {
        }
    }

    [Serializable]
    public class UMADataSlotMaterialRectEvent : UnityEvent<UMAData, SlotData, Material, Rect>
    {
        public UMADataSlotMaterialRectEvent()
        {
        }
    }
}
