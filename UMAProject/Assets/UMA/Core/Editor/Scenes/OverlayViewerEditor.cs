using System;
using System.Collections;
using System.Collections.Generic;
using UMA.Editors;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace UMA
{ 
	/// <summary>
	/// This editor is used for the UI in the inspector to edit overlays. It uses the OverlayViewer component.
	/// </summary>
	[CustomEditor(typeof(OverlayViewer))]
	public class OverlayViewerEditor : Editor
	{
		private ReorderableList overlayDataList;
		private SerializedProperty baseOverlayProperty;
		private SerializedProperty overlaysProperty;
		private SerializedProperty currentOverlayProperty;
		private SerializedProperty texMergeProperty;
		private OverlayViewer overlayViewer;
		private RawImage rawImage;
		private OverlayEditor overlayEditor = null;

		private UMAData TempUMAData;
		private SlotData TempSlot;
		private UMAGeneratorStub ugb;
		private OverlayData TempOverlay;
		private UMAGeneratorCoroutine activeGeneratorCoroutine;
		private OverlayData BaseOverlay = null;
		private Dictionary<int, OverlayData> AdditionalOverlays = new Dictionary<int, OverlayData>();


		private void OnEnable()
		{
			Initialize();
			ProcessCurrentOverlays();
		}

		private void Initialize(bool retry = true)
		{
			overlayViewer = serializedObject.targetObject as OverlayViewer;
			TempUMAData = overlayViewer.gameObject.GetComponent<UMAData>();
			ugb = overlayViewer.gameObject.GetComponent<UMAGeneratorStub>();
			TempSlot = new SlotData(overlayViewer.SlotDataAsset);
			rawImage = overlayViewer.ImageViewer;
			activeGeneratorCoroutine = new UMAGeneratorCoroutine();

			SetupGenerator();

			TempUMAData.Initialize(ugb);
			TempUMAData.SetSlot(0, TempSlot);

			baseOverlayProperty = serializedObject.FindProperty("BaseOverlay");
			overlaysProperty = serializedObject.FindProperty("Overlays");
			texMergeProperty = serializedObject.FindProperty("TextureMergePrefab");

			overlayDataList = new ReorderableList(serializedObject, overlaysProperty, true, true, false, false);
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
			};


			overlayDataList.onSelectCallback = (ReorderableList list) =>
			{
				SelectNewOverlay(list.index);
			};


			overlayDataList.onChangedCallback = (ReorderableList list) =>
			{
				SelectNewOverlay(list.index);
				ProcessCurrentOverlays(retry);
			};
		}

		private void SetupGenerator()
		{
			ugb.convertMipMaps = false;
			ugb.atlasResolution = 2048;
			ugb.convertRenderTexture = false;
			ugb.fitAtlas = true;
			ugb.textureMerge = overlayViewer.TextureMergePrefab;
			ugb.textureMerge.RefreshMaterials();
		}

		private void SelectNewOverlay(int index)
		{
			if (index < 0 || index >= overlayDataList.serializedProperty.arraySize)
			{
				currentOverlayProperty = null;
				return;
			}
			Debug.Log("Index is " + index);
			var element = overlayDataList.serializedProperty.GetArrayElementAtIndex(overlayDataList.index);
			if (element.objectReferenceValue == null)
			{
				currentOverlayProperty = null;
				return;
			}
			if (element != currentOverlayProperty)
			{
				currentOverlayProperty = element;
				OverlayDataAsset asset = currentOverlayProperty.objectReferenceValue as OverlayDataAsset;

				if (!AdditionalOverlays.ContainsKey(asset.GetInstanceID()))
				{
					AdditionalOverlays.Add(asset.GetInstanceID(), new OverlayData(asset));
				}
				TempOverlay = AdditionalOverlays[asset.GetInstanceID()];
				overlayEditor = new OverlayEditor(TempUMAData.umaRecipe, TempSlot, TempOverlay, baseOverlayProperty.objectReferenceValue as OverlayDataAsset);
			}
		}


		public void ProcessCurrentOverlays(bool retry = true)
		{

			if (baseOverlayProperty == null)
				return;


			if (BaseOverlay == null || (BaseOverlay.asset.GetInstanceID() != (baseOverlayProperty.objectReferenceValue as OverlayDataAsset).GetInstanceID()))
			{
				OverlayDataAsset overlayDataAsset = baseOverlayProperty.objectReferenceValue as OverlayDataAsset;
				BaseOverlay = new OverlayData(overlayDataAsset);
			}

			OverlayViewer viewerobj = serializedObject.targetObject as OverlayViewer;

			List<OverlayData> od = new List<OverlayData>();
			od.Add(BaseOverlay);

			foreach (OverlayDataAsset o in viewerobj.Overlays)
			{
				if (o != null)
				{
					if (!AdditionalOverlays.ContainsKey(o.GetInstanceID()))
					{
						AdditionalOverlays.Add(o.GetInstanceID(), new OverlayData(o));
					}
					od.Add(AdditionalOverlays[o.GetInstanceID()]);
				}
			}

			TempSlot.asset.material = od[0].asset.material;
			TempSlot.SetOverlayList(od);
			SlotData[] slot = new SlotData[1];
			slot[0] = TempSlot;
			TempUMAData.SetSlots(slot);

			TextureProcessBaseCoroutine textureProcessCoroutine;
			textureProcessCoroutine = new TextureProcessPROCoroutine();
			textureProcessCoroutine.Prepare(TempUMAData, TempUMAData.umaGenerator);
			activeGeneratorCoroutine.Prepare(TempUMAData.umaGenerator, TempUMAData, textureProcessCoroutine, false, 1);
			try
			{
				bool workDone = activeGeneratorCoroutine.Work();
				//Debug.Log("Workdone is " + workDone);
				rawImage.material = TempUMAData.generatedMaterials.materials[0].material;
			}
			catch(Exception ex)
			{
				Debug.Log("Something has gone wrong. Reinitializing. Text of error was: "+ex.Message);
				if (retry)
					Initialize(false);
			}
		}

	public override void OnInspectorGUI()
		{
			OverlayDataAsset SelectedOverlay = null;

			if (currentOverlayProperty != null)
			{
				SelectedOverlay = currentOverlayProperty.objectReferenceValue as OverlayDataAsset;
			}

			if (overlayViewer.AnnoyingPanel.activeSelf)
			{
				if (GUILayout.Button("Begin"))
				{
					overlayViewer.AnnoyingPanel.SetActive(false);
				}
				EditorGUILayout.LabelField("Press the begin button to hide the annoying panel and start editing", EditorStyles.helpBox);
			}


			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(baseOverlayProperty);

			if (SelectedOverlay == null)
			{
				EditorGUILayout.LabelField("Selected overlay: <None Selected>");
			}
			else
			{
				EditorGUILayout.LabelField("Selected overlay: "+SelectedOverlay.overlayName);
			}

			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Add"))
			{
				SerializedObject obj = overlaysProperty.serializedObject;
				var ov = obj.targetObject as OverlayViewer;
				ov.Overlays.Add(null);
			}

			if (GUILayout.Button("Remove"))
			{
				currentOverlayProperty = null;

				if (overlayDataList.index >= 0)
				{
					int count = overlayDataList.count;

					SerializedObject obj = overlaysProperty.serializedObject;
					var ov = obj.targetObject as OverlayViewer;
					ov.Overlays.RemoveAt(overlayDataList.index);
					if (overlayDataList.count == 1)
					{
						overlayDataList.index = -1;
					}
					else if (overlayDataList.index > 0)
					{
						overlayDataList.index--;
					}
				}
			}

			GUILayout.EndHorizontal();

			overlayDataList.DoLayoutList();
			if (EditorGUI.EndChangeCheck())
			{
				serializedObject.ApplyModifiedProperties();
				SelectNewOverlay(overlayDataList.index);
				ProcessCurrentOverlays();
			}

			EditorGUILayout.Separator();
			if (currentOverlayProperty == null)
			{
				EditorGUILayout.LabelField("Select an overlay in the list to edit the overlay positions.");
			}
			else
			{
				OverlayDataAsset ovl = currentOverlayProperty.objectReferenceValue as OverlayDataAsset;
				EditorGUILayout.LabelField("Selected Overlay: " + ovl.overlayName);
				if (overlayEditor.OnGUI())
				{
					ProcessCurrentOverlays();
				}
				EditorGUILayout.LabelField("Press Save or edits will be lost!", EditorStyles.helpBox);
				if (GUILayout.Button("Save"))
				{
					ovl.rect = overlayEditor.Overlay.rect;
					EditorUtility.SetDirty(ovl);
					AssetDatabase.SaveAssets();
					EditorUtility.DisplayDialog("Message", "Overlay '" + ovl.overlayName + "' Saved", "OK");
				}
			}
		}
	}
}
 