using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Reflection;


namespace UMA.Editors
{
	/// <summary>
	/// A reorderable list for use in property drawers and other places where you dont want to initialize it in OnGUI every frame. Call CachedReorderableList.GetListDrawer to use
	/// </summary>
	public sealed class CachedReorderableList : ReorderableList
	{

		public GUIContent Label;

		private CachedReorderableList(SerializedObject serializedObj, SerializedProperty property) : base(serializedObj, property)
		{
		}

		#region Methods

		private static FieldInfo _m_SerializedObject;
		private void ReInit(SerializedObject obj, SerializedProperty prop)
		{
			try
			{
				if (_m_SerializedObject == null)
                {
                    _m_SerializedObject = typeof(ReorderableList).GetField("m_SerializedObject", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }

                _m_SerializedObject.SetValue(this, obj);
			}
			catch
			{
				UnityEngine.Debug.LogWarning("This version of Spacepuppy Framework does not support the version of Unity it's being used with (CachedReorderableCollection).");
			}

			this.serializedProperty = prop;
		}

		#endregion


		#region Static Factory

		private static Dictionary<int, CachedReorderableList> _lstCache = new Dictionary<int, CachedReorderableList>();

		public static CachedReorderableList GetListDrawer(SerializedProperty property, ReorderableList.HeaderCallbackDelegate drawHeaderCallback, ReorderableList.ElementHeightCallbackDelegate getElementHeightCallback, ReorderableList.ElementCallbackDelegate drawElementCallback, 
														  ReorderableList.FooterCallbackDelegate drawFooterCallback = null,
														  ReorderableList.AddCallbackDelegate onAddCallback = null, ReorderableList.RemoveCallbackDelegate onRemoveCallback = null, ReorderableList.SelectCallbackDelegate onSelectCallback = null,
														  ReorderableList.ChangedCallbackDelegate onChangedCallback = null, ReorderableList.ReorderCallbackDelegate onReorderCallback = null, ReorderableList.CanRemoveCallbackDelegate onCanRemoveCallback = null,
														  ReorderableList.AddDropdownCallbackDelegate onAddDropdownCallback = null)
		{
			if (property == null)
            {
                throw new System.ArgumentNullException("property");
            }

            if (!property.isArray)
            {
                throw new System.ArgumentException("SerializedProperty must be a property for an Array or List", "property");
            }

            int hash = GetPropertyHash(property);
			CachedReorderableList lst;
			if (_lstCache.TryGetValue(hash, out lst))
			{
				lst.ReInit(property.serializedObject, property);
			}
			else
			{
				lst = new CachedReorderableList(property.serializedObject, property);
				_lstCache[hash] = lst;
			}
			lst.drawHeaderCallback = drawHeaderCallback;
			lst.elementHeightCallback = getElementHeightCallback;
			lst.drawElementCallback = drawElementCallback;
			lst.drawFooterCallback = drawFooterCallback;
			lst.onAddCallback = onAddCallback;
			lst.onRemoveCallback = onRemoveCallback;
			lst.onSelectCallback = onSelectCallback;
			lst.onChangedCallback = onChangedCallback;
			lst.onReorderCallback = onReorderCallback;
			lst.onCanRemoveCallback = onCanRemoveCallback;
			lst.onAddDropdownCallback = onAddDropdownCallback;

			return lst;
		}

		public static int GetPropertyHash(SerializedProperty property)
		{
			if (property == null)
            {
                throw new System.ArgumentNullException("property");
            }

            if (property.serializedObject.targetObject == null)
            {
                return 0;
            }

            var spath = property.propertyPath;

			int num = property.serializedObject.targetObject.GetInstanceID() ^ spath.GetHashCode();
			if (property.propertyType == SerializedPropertyType.ObjectReference)
            {
                num ^= property.objectReferenceInstanceIDValue;
            }

            return num;
		}

		#endregion

	}
}
