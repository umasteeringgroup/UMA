using UnityEngine;
using UnityEditor;
//using System.Linq;

namespace UMA.Editors
{
    [CustomEditor(typeof(UMAMountedItem))]
	[CanEditMultipleObjects]
	public class UMAMountedItemEditor : Editor
	{
		private SerializedObject m_Object;
		private UMAMountedItem mountedItem;
		private SerializedProperty m_SlotDataAssetCount;

		public void OnEnable()
		{
			m_Object = new SerializedObject(target);
			mountedItem = m_Object.targetObject as UMAMountedItem;
		}

		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();
			if (GUILayout.Button("Reset Mount Point"))
			{
				mountedItem.ResetMountPoint();
			}
			if (GUILayout.Button("Set mount item transform"))
			{
				var mp = mountedItem.EditorFindOrCreateMountpoint();

				mountedItem.Position = mp.localPosition;
				mountedItem.Orientation = mp.localRotation;
			}
		}
	}
}
