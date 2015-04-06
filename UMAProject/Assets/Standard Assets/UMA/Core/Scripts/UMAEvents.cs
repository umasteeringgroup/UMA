using System;
using UnityEngine;
using UnityEngine.Events;

namespace UMA
{
	/// <summary>
	/// UMA event occuring on UMA data.
	/// </summary>
    [Serializable]
    public class UMADataEvent : UnityEvent<UMAData>
    {
        public UMADataEvent()
        {
        }

        public UMADataEvent(UMADataEvent source)
		{
			for (int i = 0; i < source.GetPersistentEventCount(); i++)
			{
				var target = source.GetPersistentTarget(i);
				AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(UMAData) }));
			}
		}
	}

	/// <summary>
	/// UMA event occuring on slot.
	/// </summary>
	[Serializable]
    public class UMADataSlotEvent : UnityEvent<UMAData, SlotData>
    {
        public UMADataSlotEvent()
        {
        }
		public UMADataSlotEvent(UMADataSlotEvent source)
		{
			for (int i = 0; i < source.GetPersistentEventCount(); i++)
			{
				var target = source.GetPersistentTarget(i);
				AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(UMAData), typeof(SlotData) }));
			}
		}
    }

	/// <summary>
	/// UMA event occuring on material.
	/// </summary>
	[Serializable]
    public class UMADataSlotMaterialRectEvent : UnityEvent<UMAData, SlotData, Material, Rect>
    {
        public UMADataSlotMaterialRectEvent()
        {
        }
		public UMADataSlotMaterialRectEvent(UMADataSlotMaterialRectEvent source)
		{
			for (int i = 0; i < source.GetPersistentEventCount(); i++)
			{
				var target = source.GetPersistentTarget(i);
				AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(UMAData), typeof(SlotData), typeof(Material), typeof(Rect) }));
			}
		}
    }
}
