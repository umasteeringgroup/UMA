using System;

namespace UMA
{
	public abstract class UMADestinationPropertyAsset : UMAPropertyAsset
	{
		public BasePieceProperty[] DestinationProperties = new BasePieceProperty[0];

		public BasePieceProperty GetDestinationProperty(string name)
		{
			for (int i = 0; i < DestinationProperties.Length; i++)
			{
				if (DestinationProperties[i].propertyName == name)
				{
					return DestinationProperties[i];
				}
			}
			return null;
		}

		public abstract void SetDestinationPropertyValue<T>(BasePieceProperty<T> property, T value)
			where T : BaseProperty, new();

#if UNITY_EDITOR
		public abstract void UpdateDestinationProperties();
#endif
	}
}
