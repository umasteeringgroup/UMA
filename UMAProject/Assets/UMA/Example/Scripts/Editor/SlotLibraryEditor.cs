using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UMA;


[CustomEditor(typeof(SlotLibrary))]
[CanEditMultipleObjects]
public class SlotLibraryEditor : Editor
{
	private SerializedObject m_Object;
	private SlotLibrary slotLibrary;
	private SerializedProperty m_SlotDataCount;

	private const string kArraySizePath = "slotElementList.Array.size";
	private const string kArrayData = "slotElementList.Array.data[{0}]";

	private bool canUpdate;
	private bool isDirty;

	public void OnEnable()
	{
		m_Object = new SerializedObject(target);
		slotLibrary = m_Object.targetObject as SlotLibrary;
		m_SlotDataCount = m_Object.FindProperty(kArraySizePath);
	}


	private SlotData[] GetSlotDataArray()
	{

		int arrayCount = m_SlotDataCount.intValue;
		SlotData[] SlotDataArray = new SlotData[arrayCount];

		for (int i = 0; i < arrayCount; i++)
		{

			SlotDataArray[i] = m_Object.FindProperty(string.Format(kArrayData, i)).objectReferenceValue as SlotData;

		}
		return SlotDataArray;

	}

	private void SetSlotData(int index, SlotData slotElement)
	{
		m_Object.FindProperty(string.Format(kArrayData, index)).objectReferenceValue = slotElement;
		isDirty = true;
	}

	private SlotData GetSlotDataAtIndex(int index)
	{
		return m_Object.FindProperty(string.Format(kArrayData, index)).objectReferenceValue as SlotData;
	}

	private void AddSlotData(SlotData slotElement)
	{
		m_SlotDataCount.intValue++;
		SetSlotData(m_SlotDataCount.intValue - 1, slotElement);
	}


	private void RemoveSlotDataAtIndex(int index)
	{

		for (int i = index; i < m_SlotDataCount.intValue - 1; i++)
		{

			SetSlotData(i, GetSlotDataAtIndex(i + 1));
		}

		m_SlotDataCount.intValue--;

	}

	private void DropAreaGUI(Rect dropArea)
	{

		var evt = Event.current;

		if (evt.type == EventType.DragUpdated)
		{
			if (dropArea.Contains(evt.mousePosition))
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
			}
		}

		if (evt.type == EventType.DragPerform)
		{
			if (dropArea.Contains(evt.mousePosition))
			{
				DragAndDrop.AcceptDrag();

				UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
				for (int i = 0; i < draggedObjects.Length; i++)
				{
					if (draggedObjects[i])
					{
						SlotData tempSlotData = draggedObjects[i] as SlotData;
						if (tempSlotData)
						{
							AddSlotData(tempSlotData);
							continue;
						}

						var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
						if (System.IO.Directory.Exists(path))
						{
							var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
							foreach (var assetFile in assetFiles)
							{
								tempSlotData = AssetDatabase.LoadAssetAtPath(assetFile, typeof(SlotData)) as SlotData;
								if (tempSlotData)
								{
									AddSlotData(tempSlotData);
								}
							}
						}
					}
				}
			}
		}
	}

	public override void OnInspectorGUI()
	{
		m_Object.Update();

		GUILayout.Label("slotElementList", EditorStyles.boldLabel);

		SlotData[] slotElementList = GetSlotDataArray();

		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Order by Name"))
		{
			canUpdate = false;

			List<SlotData> SlotDataTemp = slotElementList.ToList();

			//Make sure there's no invalid data
			for (int i = 0; i < SlotDataTemp.Count; i++)
			{
				if (SlotDataTemp[i] == null)
				{
					SlotDataTemp.RemoveAt(i);
					i--;
				}
			}

			SlotDataTemp.Sort((x, y) => x.name.CompareTo(y.name));

			for (int i = 0; i < SlotDataTemp.Count; i++)
			{
				SetSlotData(i, SlotDataTemp[i]);
			}

		}

		if (GUILayout.Button("Update List"))
		{
			isDirty = true;
			canUpdate = false;
		}


		GUILayout.EndHorizontal();

		GUILayout.Space(20);
		Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
		GUI.Box(dropArea, "Drag Slots here");
		GUILayout.Space(20);


		for (int i = 0; i < m_SlotDataCount.intValue; i++)
		{
			GUILayout.BeginHorizontal();

			SlotData result = EditorGUILayout.ObjectField(slotElementList[i], typeof(SlotData), true) as SlotData;

			if (GUI.changed && canUpdate)
			{
				SetSlotData(i, result);
			}

			if (GUILayout.Button("-", GUILayout.Width(20.0f)))
			{
				canUpdate = false;
				RemoveSlotDataAtIndex(i);
			}

			GUILayout.EndHorizontal();

			if (i == m_SlotDataCount.intValue - 1)
			{
				canUpdate = true;

				if (isDirty)
				{
					slotLibrary.UpdateDictionary();
					isDirty = false;
				}
			}
		}

		DropAreaGUI(dropArea);

		if (GUILayout.Button("Add SlotData"))
		{
			AddSlotData(null);
		}

		if (GUILayout.Button("Clear List"))
		{
			m_SlotDataCount.intValue = 0;
		}

		if (GUILayout.Button("Remove Invalid Slot Data"))
		{
			RemoveInvalidSlotData(slotElementList);
		}

		if (GUILayout.Button("Validate Texture Name Lists"))
		{
			ValidateSlotDataTextureNameLists(slotElementList);
		}

		m_Object.ApplyModifiedProperties();

	}

	private void ValidateSlotDataTextureNameLists(SlotData[] slotElementList)
	{
		var uma = GameObject.Find("UMA");
		if (uma == null) return;
		var generators = uma.GetComponentsInChildren<UMAGeneratorBase>();
		if (generators.Length == 0) return;
		string[] defaultTextureNameList = generators[0].textureNameList;

		for (int i = m_SlotDataCount.intValue - 1; i >= 0; i--)
		{
			if (slotElementList[i])
			{
				if (slotElementList[i].textureNameList == null || slotElementList[i].textureNameList.Length == 0)
				{
					ValidateSlotDataTextureNameList(defaultTextureNameList, slotElementList[i]);
				}
			}
		}
	}

	private void ValidateSlotDataTextureNameList(string[] defaultTextureNameList, SlotData slotData)
	{
		bool valid = true;
		for(int i = 0; i < defaultTextureNameList.Length; i++ )
		{
			valid = valid && slotData.materialSample.HasProperty(defaultTextureNameList[i]);
		}
		if( !valid )
		{
			var properties = ShaderUtil.GetPropertyCount(slotData.materialSample.shader);
			var newTextureNameList = new List<string>(16);
			for(int i = 0; i < properties; i++ )
			{
				if( ShaderUtil.GetPropertyType(slotData.materialSample.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv )
				{
					newTextureNameList.Add(ShaderUtil.GetPropertyName(slotData.materialSample.shader, i));
				}
			}
			Debug.LogWarning("Fixed SlotData: " + slotData.slotName);
			slotData.textureNameList = newTextureNameList.ToArray();
			EditorUtility.SetDirty(slotData);
		}
	}

	private void RemoveInvalidSlotData(SlotData[] slotElementList)
	{
		for (int i = m_SlotDataCount.intValue - 1; i >= 0; i--)
		{
			if (slotElementList[i])
			{
				if (slotElementList[i].meshRenderer == null || slotElementList[i].materialSample == null)
				{
					Debug.LogWarning("Removed SlotData: " + slotElementList[i].slotName);
					RemoveSlotDataAtIndex(i);
				}
			}
		}
	}
}
