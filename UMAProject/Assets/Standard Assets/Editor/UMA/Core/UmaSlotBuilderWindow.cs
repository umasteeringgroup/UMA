using UnityEngine;
using UnityEditor;
using System.Text;
using System.IO;
using UMA;
using System.Collections.Generic;

namespace UMAEditor
{
	public class UmaSlotBuilderWindow : EditorWindow 
	{
	    public string slotName;
	    public UnityEngine.Object slotFolder;
	    public UnityEngine.Object relativeFolder;
	    public SkinnedMeshRenderer normalReferenceMesh;
	    public SkinnedMeshRenderer slotMesh;
	    public UMAMaterial slotMaterial;

	    string GetAssetFolder()
	    {
	        int index = slotName.LastIndexOf('/');
	        if( index > 0 )
	        {
	            return slotName.Substring(0, index+1);
	        }
	        return "";
	    }

	    string GetAssetName()
	    {
	        int index = slotName.LastIndexOf('/');
	        if (index > 0)
	        {
	            return slotName.Substring(index + 1);
	        }
	        return slotName;
	    }

	    public void EnforceFolder(ref UnityEngine.Object folderObject)
	    {
	        if (folderObject != null)
	        {
	            string destpath = AssetDatabase.GetAssetPath(folderObject);
	            if (string.IsNullOrEmpty(destpath))
	            {
	                folderObject = null;
	            }
	            else if (!System.IO.Directory.Exists(destpath))
	            {
	                destpath = destpath.Substring(0, destpath.LastIndexOf('/'));
	                folderObject = AssetDatabase.LoadMainAssetAtPath(destpath);
	            }
	        }
	    }


	    void OnGUI()
	    {
			GUILayout.Label("UMA Slot Builder");
			GUILayout.Space(20);
			normalReferenceMesh = EditorGUILayout.ObjectField("Seams Mesh (Optional)", normalReferenceMesh, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;
	        slotMesh = EditorGUILayout.ObjectField("Slot Mesh", slotMesh, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;
			slotMaterial = EditorGUILayout.ObjectField("UMAMaterial", slotMaterial, typeof(UMAMaterial), false) as UMAMaterial;
	        slotFolder = EditorGUILayout.ObjectField("Slot Destination Folder", slotFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
			EnforceFolder(ref slotFolder);
			slotName = EditorGUILayout.TextField("Element Name", slotName);
			
	        if (GUILayout.Button("Create Slot"))
	        {
	            Debug.Log("Processing...");
	            if (CreateSlot() != null)
	            {
	                Debug.Log("Success.");
	            }
	        }
	      
	        GUILayout.Label("", EditorStyles.boldLabel);
	        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
	        GUI.Box(dropArea, "Drag meshes here");
	        GUILayout.Label("Automatic Drag and Drop processing", EditorStyles.boldLabel);
	        relativeFolder = EditorGUILayout.ObjectField("Relative Folder", relativeFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
	        EnforceFolder(ref relativeFolder);

	        DropAreaGUI(dropArea);
	    }

	    private SlotDataAsset CreateSlot()
	    {
			if(slotName == null || slotName == ""){
				Debug.LogError("slotName must be specified.");
	            return null;
			}
			
	        return CreateSlot_Internal();
	    }

	    private SlotDataAsset CreateSlot_Internal()
	    {
			var material = slotMaterial;
			if (slotName == null || slotName == "")
			{
				Debug.LogError("slotName must be specified.");
	            return null;
			}
			
			if (material == null)
	        {
	            Debug.LogWarning("No UMAMaterial specified, you need to specify that later.");
	            return null;
	        }

	        if (slotFolder == null)
	        {
	            Debug.LogError("Slot folder not supplied");
	            return null;
	        }

	        if (slotMesh == null)
	        {
	            Debug.LogError("Slot Mesh not supplied.");
	            return null;
	        }
            Debug.Log("Slot Mesh: " + slotMesh.name, slotMesh.gameObject);
			SlotDataAsset slot = UMASlotProcessingUtil.CreateSlotData(AssetDatabase.GetAssetPath(slotFolder), GetAssetFolder(), GetAssetName(), slotMesh, material, normalReferenceMesh);
	        return slot;
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
					var meshes = new HashSet<SkinnedMeshRenderer>();
	                for (int i = 0; i < draggedObjects.Length; i++)
	                {
						RecurseObject(draggedObjects[i], meshes);
					}

					foreach(var mesh in meshes)
					{
						slotMesh = mesh;
						GetMaterialName(mesh.name, mesh);
						if (CreateSlot() != null)
						{
							Debug.Log("Batch importer processed mesh: " + slotName);
						}
					}
	            }
	        }
	    }

		private void RecurseObject(Object obj, HashSet<SkinnedMeshRenderer> meshes)
		{
			GameObject go = obj as GameObject;
			if (go != null)
			{
				foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				{
					meshes.Add(smr);
				}
				return;
			}
			var path = AssetDatabase.GetAssetPath(obj);
			if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
			{
				foreach (var guid in AssetDatabase.FindAssets("t:GameObject", new string[] {path}))
				{
					RecurseObject(AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), typeof(GameObject)), meshes);
				}
			}
		}

	    private string ProcessTextureTypeAndName(Texture2D tex)
	    {
	        var suffixes = new string[] { "_dif", "_spec", "_nor" };
	       
	        int index = 0;
	        foreach( var suffix in suffixes )
	        {
	            index = tex.name.IndexOf(suffix, System.StringComparison.InvariantCultureIgnoreCase);
	            if( index > 0 )
	            {
	                string name = tex.name.Substring(0,index);
	                GetMaterialName(name, tex);
	                return suffix;
	            }
	        }
	        return "";
	    }

	    private void GetMaterialName(string name, UnityEngine.Object obj)
	    {
	        if (relativeFolder != null)
	        {
	            var relativeLocation = AssetDatabase.GetAssetPath(relativeFolder);
	            var assetLocation = AssetDatabase.GetAssetPath(obj);
	            if (assetLocation.StartsWith(relativeLocation, System.StringComparison.InvariantCultureIgnoreCase))
	            {
	                string temp = assetLocation.Substring(relativeLocation.Length + 1); // remove the prefix
	                temp = temp.Substring(0, temp.LastIndexOf('/') + 1); // remove the asset name
	                slotName = temp + name; // add the cleaned name
	            }
	        }
	        else
	        {
	            slotName = name;
	        }
	    }

	    [MenuItem("UMA/Slot Builder")]
	    public static void OpenUmaTexturePrepareWindow()
	    {
			UmaSlotBuilderWindow window = (UmaSlotBuilderWindow)EditorWindow.GetWindow(typeof(UmaSlotBuilderWindow));

#if !UNITY_4_6 && !UNITY_5_0
			window.titleContent.text = "Slot Builder";
#else
			window.title = "Slot Builder";
#endif
        }

		[MenuItem("UMA/Optimize Slot Meshes")]
		public static void OptimizeSlotMeshes()
		{
#if UMA2_LEAN_AND_CLEAN 
			Debug.LogError("MenuItem - UMA/OptimizeSlotMeshes does not work with the define UMA2_LEAN_AND_CLEAN, we need all legacy fields available.");
#else
			foreach (var obj in Selection.objects)
			{
				var SlotDataAsset = obj as SlotDataAsset;
				if (SlotDataAsset != null)
				{
#pragma warning disable 618
					if (SlotDataAsset.meshRenderer != null)
					{
						UMASlotProcessingUtil.OptimizeSlotDataMesh(SlotDataAsset.meshRenderer);
						SlotDataAsset.UpdateMeshData(SlotDataAsset.meshRenderer);
						SlotDataAsset.meshRenderer = null;
						EditorUtility.SetDirty(SlotDataAsset);
					}
					else
					{
						if (SlotDataAsset.meshData != null)
						{
							SlotDataAsset.UpdateMeshData();
						}
						else
						{
							if (SlotDataAsset.meshData.vertices != null)
							{
								SlotDataAsset.UpdateMeshData();
							}
						}
					}
#pragma warning restore 618
				}
			}
			AssetDatabase.SaveAssets();
#endif
		}
	}
}