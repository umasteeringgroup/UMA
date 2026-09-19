using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.Events;

namespace UMA
{
    public class TextureEventParms
        {
        public RenderTexture renderTexture;
        public OverlayData overlayData;
        public SlotData slotData;
        public UMAData umaData;
        public int propertyIndex = 0;


        public string overlayName
        {
            get
            {
                if (overlayData == null)
                    return null;
                return overlayData.overlayName;
            }
        }   

        public string materialPropertyName
        {
            get
            {
                if (slotData == null || propertyIndex < 0)
                    return null;

                UMAMaterial mat = slotData.material;
                if (mat == null || mat.channels == null || propertyIndex >= mat.channels.Length)
                    return null;

                return mat.channels[propertyIndex].materialPropertyName;
            }
        }

        public UMAMaterial UMAMaterial 
        {
            get
            {
                if (slotData == null)
                    return null;
                return slotData.material;
            }
        }

        public TextureEventParms(RenderTexture rt, OverlayData od, SlotData sd, UMAData ud, int pi)
        {
            renderTexture = rt;
            overlayData = od;
            slotData = sd;
            umaData = ud;
            propertyIndex = pi;
        }
    }

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

    // New event: UMATextureEvent following the pattern of UMASlotsEvent
    [Serializable]
    public class UMATextureEvent : UnityEvent<UMAData, TextureEventParms>
    {
        private static readonly Func<UnityEventBase, int> getCallCount = CreateCallCountReader();

        /// <summary>
        /// Whether this event may invoke a callback. An allocated but empty event is not
        /// a texture writer. Counts runtime registrations too, including subscriptions
        /// made through the UnityEvent base type. Unknown engine layouts fail closed.
        /// </summary>
        public bool HasListeners => GetPersistentEventCount() != 0 ||
            getCallCount == null || getCallCount(this) != 0;

        private static Func<UnityEventBase, int> CreateCallCountReader()
        {
            // Unity exposes persistent listener counts only. Its internal count includes
            // runtime AddListener/AddAction calls without invoking them. Resolve once;
            // the hot path is an allocation-free delegate call, not reflection/serialization.
            // link.xml preserves this method in stripped players. Do not replace this with
            // shadowed AddListener methods: base-typed callers would bypass that tracking.
            try
            {
                var method = typeof(UnityEventBase).GetMethod("GetCallsCount",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                return method == null ? null :
                    (Func<UnityEventBase, int>)method.CreateDelegate(typeof(Func<UnityEventBase, int>));
            }
            catch (Exception)
            {
                // If a future Unity version/backend cannot expose the count, retain the
                // conservative bypass instead of risking mutation of a shared atlas.
                return null;
            }
        }

        public UMATextureEvent()
        {
        }

        public UMATextureEvent(UMATextureEvent source)
        {
            for (int i = 0; i < source.GetPersistentEventCount(); i++)
            {
                var target = source.GetPersistentTarget(i);
                AddListener(target, UnityEventBase.GetValidMethodInfo(
                    target,
                    source.GetPersistentMethodName(i),
                    new Type[] { typeof(UMAData), typeof(TextureEventParms)  }
                ));
            }
        }

        public void AddAction(Action<UMAData, TextureEventParms> action)
        {
            this.AddListener(action.Target, action.Method);
        }

        public void RemoveAction(Action<UMAData, TextureEventParms> action)
        {
            this.RemoveListener(action.Target, action.Method);
        }
    }
}
