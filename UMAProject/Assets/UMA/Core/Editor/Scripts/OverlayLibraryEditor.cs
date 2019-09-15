using UnityEngine;
using UnityEditor;
//using System.Linq;
using System.Collections.Generic;

namespace UMA.Editors
{
	[CustomEditor(typeof(OverlayLibrary))]
	[CanEditMultipleObjects]
	public class OverlayLibraryEditor : Editor
	{	
		private SerializedObject m_Object;
		private OverlayLibrary overlayLibrary;
		private SerializedProperty m_OverlayDataCount;
		
		private const string kArraySizePath = "overlayElementList.Array.size";
		private const string kArrayData = "overlayElementList.Array.data[{0}]";
		
		private bool canUpdate;
		private bool isDirty;
		
			
		public SerializedProperty scaleAdjust;
		public SerializedProperty readWrite;
		public SerializedProperty compress;
		
		public void OnEnable(){
			
			m_Object = new SerializedObject(target);
			overlayLibrary = m_Object.targetObject as OverlayLibrary;	
			m_OverlayDataCount = m_Object.FindProperty(kArraySizePath);
			scaleAdjust = serializedObject.FindProperty ("scaleAdjust");
			readWrite = serializedObject.FindProperty ("readWrite");
			compress = serializedObject.FindProperty ("compress");
		}


		private OverlayDataAsset[] GetOverlayDataArray()
		{
		
			int arrayCount = m_OverlayDataCount.intValue;
			OverlayDataAsset[] OverlayDataArray = new OverlayDataAsset[arrayCount];
			
			for(int i = 0; i < arrayCount; i++){
			
				OverlayDataArray[i] = m_Object.FindProperty(string.Format(kArrayData,i)).objectReferenceValue as OverlayDataAsset;
				
			}
			return OverlayDataArray;
			
		}

		private void SetOverlayData(int index, OverlayDataAsset overlayElement)
		{
			m_Object.FindProperty(string.Format(kArrayData,index)).objectReferenceValue = overlayElement;
			isDirty = true;
		}

		private OverlayDataAsset GetOverlayDataAtIndex(int index)
		{
			return m_Object.FindProperty(string.Format(kArrayData, index)).objectReferenceValue as OverlayDataAsset;
		}

		private void AddOverlayData(OverlayDataAsset overlayElement)
		{
			m_OverlayDataCount.intValue ++;
			SetOverlayData(m_OverlayDataCount.intValue - 1, overlayElement);
		}	
			
		
		private void RemoveOverlayDataAtIndex(int index){
			
			for(int i = index; i < m_OverlayDataCount.intValue - 1; i++){	
			
				SetOverlayData(i, GetOverlayDataAtIndex(i + 1));
			}

			m_OverlayDataCount.intValue --;
			
		}
		
		private void ScaleDownTextures(){

			OverlayDataAsset[] overlayElementList = GetOverlayDataArray();
			string path;
			
			
			for(int i = 0; i < overlayElementList.Length; i++){
				if(overlayElementList[i] != null){
					Rect tempRect = overlayElementList[i].rect;
					overlayElementList[i].rect = new Rect(tempRect.x*0.5f,tempRect.y*0.5f,tempRect.width*0.5f,tempRect.height*0.5f);				
					
					EditorUtility.SetDirty(overlayElementList[i]);
					
					for(int textureID = 0; textureID < overlayElementList[i].textureList.Length; textureID++){
						if(overlayElementList[i].textureList[textureID]){
							path = AssetDatabase.GetAssetPath(overlayElementList[i].textureList[textureID]);
							TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
							
							textureImporter.maxTextureSize = (int)(textureImporter.maxTextureSize*0.5f);
													
							AssetDatabase.WriteImportSettingsIfDirty (path);
							AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
						}
					}
				}
			}
			
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
		
		private void ScaleUpTextures(){

			OverlayDataAsset[] overlayElementList = GetOverlayDataArray();
			string path;
			
			
			for(int i = 0; i < overlayElementList.Length; i++){
				if(overlayElementList[i] != null){
			
					Rect tempRect = overlayElementList[i].rect;
					overlayElementList[i].rect = new Rect(tempRect.x*2,tempRect.y*2,tempRect.width*2,tempRect.height*2);
					
					EditorUtility.SetDirty(overlayElementList[i]);
					
					for(int textureID = 0; textureID < overlayElementList[i].textureList.Length; textureID++){
						if(overlayElementList[i].textureList[textureID]){
							path = AssetDatabase.GetAssetPath(overlayElementList[i].textureList[textureID]);
							TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
							
							textureImporter.maxTextureSize = (int)(textureImporter.maxTextureSize*2);
							
							AssetDatabase.WriteImportSettingsIfDirty (path);
							AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
						}
					}
				}
			}
			
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
		
		
		private void ConfigureTextures(){

			OverlayDataAsset[] overlayElementList = GetOverlayDataArray();
			string path;
			
			
			for(int i = 0; i < overlayElementList.Length; i++){
				if(overlayElementList[i] != null){
			
					for(int textureID = 0; textureID < overlayElementList[i].textureList.Length; textureID++){
						if(overlayElementList[i].textureList[textureID]){
							path = AssetDatabase.GetAssetPath(overlayElementList[i].textureList[textureID]);
							TextureImporter textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
							
							textureImporter.isReadable = readWrite.boolValue;
							
							if(compress.boolValue){
								textureImporter.textureCompression = TextureImporterCompression.CompressedHQ;
								textureImporter.compressionQuality = (int)TextureCompressionQuality.Best;                               
							}else{
								textureImporter.textureCompression = TextureImporterCompression.Uncompressed;                             
							}
							
							AssetDatabase.WriteImportSettingsIfDirty (path);
							AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
							Debug.Log(overlayElementList[i].textureList[textureID].name + " isReadable set to " + readWrite.boolValue + " and compression set to " + compress.boolValue);
						}
					}
				}
			}
			
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
		
		private void DropAreaGUI(Rect dropArea){
			
			var evt = Event.current;

			if(evt.type == EventType.DragUpdated){
				if(dropArea.Contains(evt.mousePosition)){
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				}
			}
			
			if(evt.type == EventType.DragPerform){
				if(dropArea.Contains(evt.mousePosition)){			
					DragAndDrop.AcceptDrag();
					UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences;
					for(int i = 0; i < draggedObjects.Length; i++){
						if (draggedObjects[i])
						{
							OverlayDataAsset tempOverlayData = draggedObjects[i] as OverlayDataAsset;
							if (tempOverlayData)
							{
								AddOverlayData(tempOverlayData);
								continue;
							}
							var path = AssetDatabase.GetAssetPath(draggedObjects[i]);
							if (System.IO.Directory.Exists(path))
							{
								RecursiveScanFoldersForAssets(path);
							}
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
				var tempOverlayData = AssetDatabase.LoadAssetAtPath(assetFile, typeof(OverlayDataAsset)) as OverlayDataAsset;
				if (tempOverlayData)
				{
					AddOverlayData(tempOverlayData);
				}
			}
			foreach (var subFolder in System.IO.Directory.GetDirectories(path))
			{
				RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'));
			}
		}
		
		public override void OnInspectorGUI(){	
			m_Object.Update();
			serializedObject.Update();
			
			GUILayout.Label ("overlayList", EditorStyles.boldLabel);


			OverlayDataAsset[] overlayElementList = GetOverlayDataArray();
			GUILayout.Space(30);
			GUILayout.Label ("Overlays reduced " + scaleAdjust.intValue +" time(s)");
			GUILayout.BeginHorizontal();
				
				if(scaleAdjust.intValue > 0){
					if(GUILayout.Button("Resolution +")){
						ScaleUpTextures();
					
						isDirty = true;
						canUpdate = false;
						scaleAdjust.intValue --;
					}
					
				}
			
				if(GUILayout.Button("Resolution -")){
					ScaleDownTextures();
				
					isDirty = true;
					canUpdate = false;
					scaleAdjust.intValue ++;
				}
				

			GUILayout.EndHorizontal();
			
			GUILayout.Space(20);
			
			
			GUILayout.BeginHorizontal();
				compress.boolValue = GUILayout.Toggle (compress.boolValue ? true : false," Compress Textures");

				readWrite.boolValue = GUILayout.Toggle (readWrite.boolValue ? true : false," Read/Write");

				if(GUILayout.Button(" Apply")){
					ConfigureTextures();
					
					isDirty = true;
					canUpdate = false;
				}

			GUILayout.EndHorizontal();
			
			GUILayout.Space(20);
			
			
			GUILayout.BeginHorizontal();
				if(GUILayout.Button("Order by Name")){
					canUpdate = false;

                List<OverlayDataAsset> OverlayDataTemp = new List<OverlayDataAsset>();
                OverlayDataTemp.AddRange(overlayElementList); 
				
					//Make sure there's no invalid data
					for(int i = 0; i < OverlayDataTemp.Count; i++){
						if(OverlayDataTemp[i] == null){
							OverlayDataTemp.RemoveAt(i);
							i--;
						}
					}
				
					OverlayDataTemp.Sort((x,y) => x.name.CompareTo(y.name));

					for(int i = 0; i < OverlayDataTemp.Count; i++){
						SetOverlayData(i,OverlayDataTemp[i]);
					}
				
				}
				
				if(GUILayout.Button("Update List")){
					isDirty = true;
					canUpdate = false;
				}
				if (GUILayout.Button("Remove Duplicates"))
				{
					HashSet<OverlayDataAsset> Overlays = new HashSet<OverlayDataAsset>();
					
					foreach(OverlayDataAsset oda in overlayElementList)
					{
					Overlays.Add(oda);
					}

					m_OverlayDataCount.intValue = Overlays.Count;

                    List<OverlayDataAsset> od = new List<OverlayDataAsset>();
                    od.AddRange(Overlays);

					for(int i=0;i<od.Count;i++)
					{
						SetOverlayData(i,od[i]);
					}
					isDirty = true;
					canUpdate = false;
				}
			GUILayout.EndHorizontal();
			
			GUILayout.Space(20);
				Rect dropArea = GUILayoutUtility.GetRect(0.0f,50.0f, GUILayout.ExpandWidth(true));
				GUI.Box(dropArea,"Drag Overlays here");
			GUILayout.Space(20);
			

			for(int i = 0; i < m_OverlayDataCount.intValue; i ++){
				GUILayout.BeginHorizontal();

				var result = EditorGUILayout.ObjectField(overlayElementList[i], typeof(OverlayDataAsset), true) as OverlayDataAsset;
					
					if(GUI.changed && canUpdate){
						SetOverlayData(i,result);
					}
					
					if(GUILayout.Button("-", GUILayout.Width(20.0f))){
						canUpdate = false;
						RemoveOverlayDataAtIndex(i);					
					}

				GUILayout.EndHorizontal();
				
				if(i == m_OverlayDataCount.intValue -1){
					canUpdate = true;	
					
					if(isDirty){
						overlayLibrary.UpdateDictionary();
						isDirty = false;
					}
				}
			}
			
			DropAreaGUI(dropArea);
			
			if(GUILayout.Button("Add OverlayData")){
				AddOverlayData(null);
			}
			
			if(GUILayout.Button("Clear List")){
				m_OverlayDataCount.intValue = 0;
			}
			
			
			m_Object.ApplyModifiedProperties();
			serializedObject.ApplyModifiedProperties();
		}
	}
}
