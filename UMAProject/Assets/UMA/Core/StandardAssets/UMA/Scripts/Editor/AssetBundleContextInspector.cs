using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA.Editors
{
	[CustomEditor(typeof(UMAAssetBundleContext))]
	public class AssetBundleContextInspector : Editor
	{	
		private UMAAssetBundleContext context;
		private SerializedObject serializedContext;

		public void OnEnable()
		{
			context = target as UMAAssetBundleContext;
			serializedContext = new SerializedObject(context);
		}

		private bool AddAssetToDictionaries(Object asset)
		{
			if (asset is RaceDataAsset)
			{
				context.AddRace(asset as RaceDataAsset);
				return true;
			}
			if (asset is SlotDataAsset)
			{
				context.AddSlotAsset(asset as SlotDataAsset);
				return true;
			}
			if (asset is OverlayDataAsset)
			{
				context.AddOverlayAsset(asset as OverlayDataAsset);
				return true;
			}
			if (asset is DNADataAsset)
			{
				context.AddDNAAsset(asset as DNADataAsset);
				return true;
			}
			if (asset is OcclusionDataAsset)
			{
				context.AddOcclusionAsset(asset as OcclusionDataAsset);
				return true;
			}

			return false;
		}

		private void DropAreaGUI(Rect dropArea)
		{
			Event evt = Event.current;

			if (evt.type == EventType.DragUpdated)
			{
				if(dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				}
			}
			
			if (evt.type == EventType.DragPerform)
			{
				if (dropArea.Contains(evt.mousePosition))
				{
					DragAndDrop.AcceptDrag();
					Object[] draggedObjects = DragAndDrop.objectReferences;

					Undo.RecordObject(context, "Add Assets");
					foreach (Object asset in draggedObjects)
					{
						string assetPath = AssetDatabase.GetAssetPath(asset);
						if (System.IO.Directory.Exists(assetPath))
						{
							RecursiveScanFoldersForAssets(assetPath);
						}
						else
						{
							AddAssetToDictionaries(asset);
						}
					}
				}
			}
		}

		private void RecursiveScanFoldersForAssets(string path)
		{
			var assetFiles = System.IO.Directory.GetFiles(path, "*.asset");
			foreach (var assetFile in assetFiles)
			{
				Object fileObject = AssetDatabase.LoadMainAssetAtPath(assetFile);
				if (fileObject != null)
				{
					AddAssetToDictionaries(fileObject);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'));
			}
		}
		
		public override void OnInspectorGUI()
		{
			serializedContext.Update();

			GUILayout.Space(EditorGUIUtility.standardVerticalSpacing);

			Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
			GUI.Box(dropArea, "Drag UMA assets here to add");

			SerializedProperty raceDictionary = serializedContext.FindProperty("raceDictionary");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(raceDictionary);
			if (EditorGUI.EndChangeCheck())
			{
				serializedContext.ApplyModifiedProperties();
			}

			SerializedProperty slotDictionary = serializedContext.FindProperty("slotDictionary");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(slotDictionary);
			if (EditorGUI.EndChangeCheck())
			{
				serializedContext.ApplyModifiedProperties();
			}

			SerializedProperty overlayDictionary = serializedContext.FindProperty("overlayDictionary");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(overlayDictionary);
			if (EditorGUI.EndChangeCheck())
			{
				serializedContext.ApplyModifiedProperties();
			}

			SerializedProperty dnaDictionary = serializedContext.FindProperty("dnaDictionary");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(dnaDictionary);
			if (EditorGUI.EndChangeCheck())
			{
				serializedContext.ApplyModifiedProperties();
			}

			SerializedProperty occlusionDictionary = serializedContext.FindProperty("occlusionDictionary");
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(occlusionDictionary);
			if (EditorGUI.EndChangeCheck())
			{
				serializedContext.ApplyModifiedProperties();
			}

			DropAreaGUI(dropArea);
		}
	}
}
