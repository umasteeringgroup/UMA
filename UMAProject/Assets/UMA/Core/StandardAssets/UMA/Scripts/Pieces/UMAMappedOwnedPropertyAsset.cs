using System;

namespace UMA
{
	public abstract class UMAMappedOwnedPropertyAsset : UMAMappedPropertyAsset
	{
#if UNITY_EDITOR
		public abstract int GetDestinationPropertyCount();
		public abstract Type GetDestinationPropertyType(int index);
		public abstract string GetDestinationPropertyName(int index);

		public override void UpdateDestinationProperties()
		{
			var count = GetDestinationPropertyCount();
			var newDestinationProperties = new BasePieceProperty[count]; // editor only potential garbage allocation
			int newProperties = 0;
			for (int i = 0; i < count; i++)
			{
				var destinationPropertyName = GetDestinationPropertyName(i);
				var destinationPropertyType = GetDestinationPropertyType(i);
				var destinationProperty = GetDestinationProperty(destinationPropertyName);
				if (destinationProperty != null && destinationProperty.GetPropertyType() != destinationPropertyType)
				{
					UnityEngine.Debug.LogErrorFormat("Destroying {0} ({1}) since it's {2} and not {3} {4}", destinationPropertyName, destinationPropertyType, destinationProperty.GetPropertyType(), destinationPropertyType, destinationProperty.GetPropertyType() == destinationPropertyType);
					destinationProperty.DestroyImmediate();
				}
				if (destinationProperty == null)
				{
					destinationProperty = BasePieceProperty.CreateProperty(destinationPropertyType, this);
					destinationProperty.propertyName = destinationPropertyName;
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
#endif
	}
}
