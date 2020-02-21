using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UMA
{
	[CustomEditor(typeof(OverlayViewer))]
	public class OverlayViewerEditor : Editor
	{
		private ReorderableList overlayDataList;
		private bool overlayDataListInitialized = false;
		private SerializedProperty baseOverlay;
		private SerializedProperty overlays;
		private SerializedProperty currentOverlay;
		private OverlayViewer overlayViewer;
		private UMAData TempUMAData = new UMAData();
		private SlotData TempSlot = new SlotData();
		private UMAGeneratorCoroutine activeGeneratorCoroutine = new UMAGeneratorCoroutine();
		private void OnEnable()
		{
			TempUMAData.SetSlot(0, new SlotData(new SlotDataAsset()));
			overlayViewer = serializedObject.targetObject as OverlayViewer;

			baseOverlay = serializedObject.FindProperty("BaseOverlay");
			overlays = serializedObject.FindProperty("Overlays");

			overlayDataList = new ReorderableList(serializedObject, overlays, true, true, true, true);
			overlayDataList.drawHeaderCallback = (Rect rect) =>
			{
				EditorGUI.LabelField(rect, "Overlays");
			};
			overlayDataList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
			{
				var element = overlayDataList.serializedProperty.GetArrayElementAtIndex(index);
				rect.y += 2;
				OverlayDataAsset o = element.objectReferenceValue as OverlayDataAsset;
				string name = "Not Set";
				if (o != null)
				{
					name = o.overlayName;
				}
				EditorGUI.PropertyField(new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight), element);
				// EditorGUI.LabelField(new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight), name);
			};


			overlayDataList.onSelectCallback = (ReorderableList list) =>
			{
				SelectNewOverlay(list.index);
			};


			overlayDataList.onChangedCallback = (ReorderableList list) =>
			{
				SelectNewOverlay(list.index);
				ProcessCurrentOverlays();
			};
			overlayDataListInitialized = true;
		}

		private void SelectNewOverlay(int index)
		{
			var element = overlayDataList.serializedProperty.GetArrayElementAtIndex(overlayDataList.index);
			if (element != currentOverlay)
			{
				currentOverlay = element;
			}
		}

		public void ProcessCurrentOverlays()
		{
			if (baseOverlay == null)
				return;

			OverlayViewer viewerobj = serializedObject.targetObject as OverlayViewer;

			List<OverlayData> od = new List<OverlayData>();
			od.Add(new OverlayData(baseOverlay.objectReferenceValue as OverlayDataAsset));

			foreach (OverlayDataAsset o in viewerobj.Overlays)
			{
				if (o != null)
				{
					od.Add(new OverlayData(o));
				}
			}

			TempSlot.SetOverlayList(od);
			SlotData[] slot = new SlotData[1];
			slot[0] = TempSlot;
			TempUMAData.SetSlots(slot);

			UMAGeneratorStub ugb = new UMAGeneratorStub();
			TextureProcessBaseCoroutine textureProcessCoroutine;
			textureProcessCoroutine = new TextureProcessPROCoroutine();
			textureProcessCoroutine.Prepare(TempUMAData, ugb);
			activeGeneratorCoroutine.Prepare(ugb, TempUMAData, textureProcessCoroutine, false, 1);
			bool workDone = activeGeneratorCoroutine.Work();
		}

	public override void OnInspectorGUI()
		{
			OverlayDataAsset SelectedOverlay = currentOverlay.objectReferenceValue as OverlayDataAsset;

			EditorGUILayout.PropertyField(baseOverlay);
		    
			if (SelectedOverlay == null)
			{
				EditorGUILayout.LabelField("Selected overlay: <None Selected>");

			}
			else
			{
				EditorGUILayout.LabelField("Selected overlay: "+SelectedOverlay.overlayName);
			}

			if (GUILayout.Button("Add"))
			{
				SerializedObject obj = overlays.serializedObject;
				var ov = obj.targetObject as OverlayViewer;
				ov.Overlays.Add(null);
			}

			EditorGUI.BeginChangeCheck();
			overlayDataList.DoLayoutList();
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
			}
		}
	}
}
 