using System;

namespace UMA
{
	public abstract class UMAMappedPropertyAsset : UMADestinationPropertyAsset
	{
		public PropertyMapping[] Mappings = new PropertyMapping[0];
#if UNITY_EDITOR

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
