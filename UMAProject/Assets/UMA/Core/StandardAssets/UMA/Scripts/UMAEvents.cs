using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.Events;

namespace UMA
{
    /// <summary>
    /// UMALabelsEvent is a UnityEvent that happens when a list of labels is processed
    /// </summary>
    [Serializable]
    public class UMALabelsEvent : UnityEvent<List<string>>
    {
        public UMALabelsEvent()
        {
        }
        public UMALabelsEvent(UMALabelsEvent source)
        {
            for (int i = 0; i < source.GetPersistentEventCount(); i++)
            {
                var target = source.GetPersistentTarget(i);
                AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(List<string>) }));
            }
        }
        public void AddAction(Action<List<string>> action)
        {
            this.AddListener(action.Target, action.Method);
        }

        public void RemoveAction(Action<List<string>> action)
        {
            this.RemoveListener(action.Target, action.Method);
        }
    }

    /// <summary>
    /// An event that happens when a generic UMACharacterAvatar is processed
    /// </summary>
    [Serializable]
    public class UMACharacterEvent : UnityEvent<DynamicCharacterAvatar>
    {
        public UMACharacterEvent()
        {
        }

        public UMACharacterEvent(UMACharacterEvent source)
        {
            for (int i = 0; i < source.GetPersistentEventCount(); i++)
            {
                var target = source.GetPersistentTarget(i);
                AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(DynamicCharacterAvatar) }));
            }
        }

        public void AddAction(Action<DynamicCharacterAvatar> action)
        {
            this.AddListener(action.Target, action.Method);
        }

        public void RemoveAction(Action<DynamicCharacterAvatar> action)
        {
            this.RemoveListener(action.Target, action.Method);
        }
    }

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

    [Serializable]
    public class UMARecipesEvent : UnityEvent<List<UMATextRecipe>>
    {
        public UMARecipesEvent()
        {
        }

        public UMARecipesEvent(UMARecipesEvent source)
        {
            for (int i = 0; i < source.GetPersistentEventCount(); i++)
            {
                var target = source.GetPersistentTarget(i);
                AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(UMAData) }));
            }
        }
        public void AddAction(Action<List<UMATextRecipe>> action)
        {
            this.AddListener(action.Target, action.Method);
        }
        public void RemoveAction(Action<List<UMATextRecipe>> action)
        {
            this.RemoveListener(action.Target, action.Method);
        }
    }

    [Serializable]
    public class UMASlotsEvent : UnityEvent<List<SlotData>>
    {
        public UMASlotsEvent()
        {
        }

        public UMASlotsEvent(UMASlotsEvent source)
        {
            for (int i = 0; i < source.GetPersistentEventCount(); i++)
            {
                var target = source.GetPersistentTarget(i);
                AddListener(target, UnityEventBase.GetValidMethodInfo(target, source.GetPersistentMethodName(i), new Type[] { typeof(UMAData) }));
            }
        }
        public void AddAction(Action<List<SlotData>> action)
        {
            this.AddListener(action.Target, action.Method);
        }
        public void RemoveAction(Action<List<SlotData>> action)
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
	/// UMAEvent that happens when a slot is processed
	/// </summary>
    [Serializable]
    public class UMADataSlotProcessedEvent: UnityEvent<UMAData, SlotData>
	{
		public UMADataSlotProcessedEvent()
		{

		}
        public UMADataSlotProcessedEvent(UMADataSlotProcessedEvent source)
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
