using System;

namespace UMA
{
	public abstract class UMAMappedPropertyAsset : UMAPropertyAsset
	{
		public PropertyMapping[] Mappings = new PropertyMapping[0];
		public BasePieceProperty[] DestinationProperties = new BasePieceProperty[0];

		public BasePieceProperty GetDestinationProperty(string name)
		{
			for (int i = 0; i < DestinationProperties.Length; i++)
			{
				if (DestinationProperties[i].name == name)
				{
					return DestinationProperties[i];
				}
			}
			return null;
		}

		public abstract void SetDestinationPropertyValue<T>(BasePieceProperty<T> property, T value)
			where T : BaseProperty, new();

#if UNITY_EDITOR
		public abstract int GetDestinationPropertyCount();
		public abstract Type GetDestinationPropertyType(int index);
		public abstract string GetDestinationPropertyName(int index);

		protected virtual void UpdateDestinationProperties()
		{
			var count = GetDestinationPropertyCount();
			var newDestinationProperties = new BasePieceProperty[count]; // editor only potential garbage allocation
			int newProperties = 0;
			for (int i = 0; i < count; i++)
			{
				var name = GetDestinationPropertyName(i);
				var propertyType = GetDestinationPropertyType(i);
				var destinationProperty = GetDestinationProperty(name);
				if (destinationProperty != null && destinationProperty.GetType() != BaseProperty.GetPiecePropertyTypeFromPropertyType(propertyType))
				{
					DestroyImmediate(destinationProperty, true);
				}
				if (destinationProperty == null)
				{
					destinationProperty = BasePieceProperty.CreateProperty(propertyType);
					destinationProperty.name = name;
					AddScriptableObjectToAsset(destinationProperty);
					newProperties++;
				}
				newDestinationProperties[i] = destinationProperty;
			}
			if (newProperties != 0 || DestinationProperties.Length != newDestinationProperties.Length)
			{
				DestinationProperties = newDestinationProperties;
				UnityEditor.EditorUtility.SetDirty(this);
			}
		}

		public virtual void UpdateMappedProperties()
		{
			UpdateDestinationProperties();

			for (int i = Mappings.Length - 1; i >= 0; i--)
			{
				if (!object.ReferenceEquals(Mappings[i].Source, null) && Mappings[i].Source == null)
				{
					Mappings[i].Source = null;
					UnityEditor.EditorUtility.SetDirty(this);
				}
				if (!object.ReferenceEquals(Mappings[i].Dest, null) && Mappings[i].Dest == null)
				{
					Mappings[i].Dest = null;
					UnityEditor.EditorUtility.SetDirty(this);
				}
				if (Mappings[i].Source != null && Mappings[i].Dest != null && !Mappings[i].Dest.GetValue().CanSetValueFrom(Mappings[i].Source.GetValue()))
				{
					Mappings[i].Dest = null;
					UnityEditor.EditorUtility.SetDirty(this);
				}
			}
		}
#endif
	}
}
