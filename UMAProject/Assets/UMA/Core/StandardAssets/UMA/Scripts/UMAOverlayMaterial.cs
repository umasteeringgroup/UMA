using UnityEngine;
using System;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// UMA wrapper for Unity material.
	/// </summary>
	[CreateAssetMenu(menuName = "UMA/Overlay Material")]
	public class UMAOverlayMaterial : UMADestinationPropertyAsset
	{
		public MaterialChannel[] channels;
		public UMAClothProperties clothProperties;
		public UMAAtlasMaterial atlas;

		public bool RequireSeperateRenderer;
		public MaterialType materialType = MaterialType.Atlas;
		public enum MaterialType
		{
			Atlas = 1,
			NoAtlas = 2,
		}


		[Serializable]
		public class MaterialChannel
		{
			public UMATextureChannelCombiner combiner;
			public PropertyMapping[] properties = new PropertyMapping[0];
			public BasePieceProperty target;
		}
		
		public override void SetDestinationPropertyValue<T>(BasePieceProperty<T> property, T value)
		{
			throw new NotImplementedException();
		}
		
		private Dictionary<BasePieceProperty, int> _propertyToIndexLookup;
		public int GetPropertyIndex(BasePieceProperty property)
		{
			if (_propertyToIndexLookup == null)
			{
				_propertyToIndexLookup = new Dictionary<BasePieceProperty, int>();
				
				for (int i = 0; i < Properties.Length; i++) 
				{
					_propertyToIndexLookup.Add(Properties[i], i);
				}
			}
			int index;
			if (!_propertyToIndexLookup.TryGetValue(property, out index))
				return -1;
			return index;
		}


#if UNITY_EDITOR
		public override void UpdateDestinationProperties()
		{
			_propertyToIndexLookup = null;
			var count = (atlas!= null) ? atlas.GetPublicPropertyCount() : 0;
			DestinationProperties = new BasePieceProperty[count];
			if (count > 0)
				atlas.GetPublicProperties(DestinationProperties);

			if (channels == null)
				channels = new MaterialChannel[count];

			if (channels.Length != count)
			{
				Array.Resize(ref channels, count);
			}
			for (int i = 0; i < count; i++)
			{
				if (channels[i] == null)
				{
					channels[i] = new MaterialChannel();
				}
				channels[i].target = DestinationProperties[i];
			}
		}
#endif
	}
}
