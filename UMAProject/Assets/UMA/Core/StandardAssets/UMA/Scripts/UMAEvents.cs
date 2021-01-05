using System;
using UMA.CharacterSystem;
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
		public void AddAction(Action<UMAData> action)
		{
			this.AddListener(action.Target, action.Method);
		}
		public void RemoveAction(Action<UMAData> action)
		{
			this.RemoveListener(action.Target, action.Method);
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

	[Serializable]
	public class UMADataWardrobeEvent : UnityEvent<UMAData, UMAWardrobeRecipe>
	{
		public UMADataWardrobeEvent()
		{
		}
		public UMADataWardrobeEvent(UMADataWardrobeEvent source)
		{
			for (int i = 0; i < source.GetPersistentEventCount(); i++)
			{
				var target = source.GetPersistentTarget(i);
				AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(UMAData), typeof(UMAWardrobeRecipe) }));
			}
		}
	}

	[Serializable]
	public class UMAExpressionEvent: UnityEvent<UMAData, string, float>
    {
		public UMAExpressionEvent()
		{
		}
		public UMAExpressionEvent(UMAExpressionEvent source)
		{
			for (int i = 0; i < source.GetPersistentEventCount(); i++)
			{
				var target = source.GetPersistentTarget(i);
				AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(UMAData), typeof(string), typeof(float) }));
			}
		}
	}

	[Serializable]
	public class UMARandomAvatarEvent: UnityEvent<GameObject, GameObject>
    {
		public UMARandomAvatarEvent()
		{
		}
		public UMARandomAvatarEvent(UMARandomAvatarEvent source)
		{
			for (int i = 0; i < source.GetPersistentEventCount(); i++)
			{
				var target = source.GetPersistentTarget(i);
				AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(GameObject), typeof(GameObject) }));
			}
		}
	}
}
