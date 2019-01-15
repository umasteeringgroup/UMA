using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace UMA
{
	public class DNAConverterListTester : MonoBehaviour
	{
		public DNAConverterList dnaConverterList = new DNAConverterList();

		[Tooltip("Some DNA field")]
		public DNAConverterField dnaConverterField;

		public bool UpgradeFromLegacy(DnaConverterBehaviour oldConverter, DynamicDNAConverterController newConverter)
		{
			if (dnaConverterList.Contains(oldConverter))
			{
				if (dnaConverterList.Replace(oldConverter, newConverter))
					return true;
			}
			return false;
		}
	}

	[CustomEditor(typeof(DNAConverterListTester))]
	public class DNAConverterListTesterInspector: Editor
	{
		DNAConverterList _DCLtarget;
		DNAConverterField _DCField;
		UnityEngine.Object _objectToAdd;
		UnityEngine.Object _objectToRemove;
		UnityEngine.Object _containsObject;
		UnityEngine.Object _indexOfObject;
		int _indexToCheck = -1;
		DnaConverterBehaviour _objectToReplace;
		DynamicDNAConverterController _objectToReplaceWith;

		public override void OnInspectorGUI()
		{
			base.OnInspectorGUI();

			_DCLtarget = (target as DNAConverterListTester).dnaConverterList;
			_DCField = (target as DNAConverterListTester).dnaConverterField;

			if (GUILayout.Button("Log Each"))
			{
				for(int i = 0; i < _DCLtarget.Count; i++)
				{
					Debug.Log(_DCLtarget[i]);
				}
			}
			if (GUILayout.Button("Log Array"))
			{
				Debug.Log(_DCLtarget.ToArray().Length);
				Debug.Log(_DCLtarget.ToArray());
			}

			//add object
			EditorGUILayout.BeginHorizontal();
			_objectToAdd = EditorGUILayout.ObjectField(_objectToAdd, typeof(UnityEngine.Object), false);
			if (GUILayout.Button("Add Object"))
			{
				_DCLtarget.Add(_objectToAdd);
			}
			EditorGUILayout.EndHorizontal();

			//remove object
			EditorGUILayout.BeginHorizontal();
			_objectToRemove = EditorGUILayout.ObjectField(_objectToRemove, typeof(UnityEngine.Object), false);
			if (GUILayout.Button("Remove Object"))
			{
				_DCLtarget.Remove(_objectToRemove);
			}
			EditorGUILayout.EndHorizontal();

			//Contains
			EditorGUILayout.BeginHorizontal();
			_containsObject = EditorGUILayout.ObjectField(_containsObject, typeof(UnityEngine.Object), false);
			if (GUILayout.Button("Contains"))
			{
				var contained = _DCLtarget.Contains(_containsObject);
				Debug.Log("_DCLtarget.Contains(" + _containsObject + ") was " + contained);
			}
			EditorGUILayout.EndHorizontal();

			//IndexOf
			EditorGUILayout.BeginHorizontal();
			_indexOfObject = EditorGUILayout.ObjectField(_indexOfObject, typeof(UnityEngine.Object), false);
			if (GUILayout.Button("IndexOf"))
			{
				var indexOf = _DCLtarget.IndexOf(_indexOfObject);
				Debug.Log("_DCLtarget.IndexOf(" + _indexOfObject + ") was " + indexOf);
			}
			EditorGUILayout.EndHorizontal();

			//element at Index
			EditorGUILayout.BeginHorizontal();
			_indexToCheck = EditorGUILayout.IntField("IndexToCheck", _indexToCheck);
			if (GUILayout.Button("IndexToCheck"))
			{
				Debug.Log(_DCLtarget[_indexToCheck]);
			}
			EditorGUILayout.EndHorizontal();

			//replace legacy
			EditorGUILayout.BeginHorizontal();
			_objectToReplace = (DnaConverterBehaviour)EditorGUILayout.ObjectField(_objectToReplace, typeof(DnaConverterBehaviour), false);
			_objectToReplaceWith = (DynamicDNAConverterController)EditorGUILayout.ObjectField(_objectToReplaceWith, typeof(DynamicDNAConverterController), false);
			EditorGUILayout.EndHorizontal();
			if (GUILayout.Button("Replace"))
			{
				Debug.Log("DCLTarget Contains _objectToReplace? " + _DCLtarget.Contains(_objectToReplace));
				if (_DCLtarget.Contains(_objectToReplace))
				{
					var replaced = _DCLtarget.Replace(_objectToReplace, _objectToReplaceWith);
					Debug.Log("DCLTarget Replaced =  " + replaced);
				}
			}
			if (GUILayout.Button("Replace Field"))
			{
				Debug.Log("_DCField is _objectToReplace? " + (_DCField.Value as Object == _objectToReplace));
				if (_DCField.Value as Object == _objectToReplace)
				{
					_DCField.Value = _objectToReplaceWith;
				}
			}

		}
	}
}
