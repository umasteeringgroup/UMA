using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace UMA.Editors
{
	public class UmaSlotBuilderWindow : EditorWindow 
	{
		public string slotName;
		public string RootBone = "Global";
		public UnityEngine.Object slotFolder;
		public UnityEngine.Object relativeFolder;
		public SkinnedMeshRenderer normalReferenceMesh;
		public SkinnedMeshRenderer slotMesh;
		public UMAMaterial slotMaterial;
		public bool createOverlay;
		public bool createRecipe;
		public bool addToGlobalLibrary;
		public bool addToLocalLibrary;
		public bool binarySerialization;

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
			normalReferenceMesh = EditorGUILayout.ObjectField("Seams Mesh (Optional)  ", normalReferenceMesh, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;
			slotMesh = EditorGUILayout.ObjectField("Slot Mesh  ", slotMesh, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;
			slotMaterial = EditorGUILayout.ObjectField("UMAMaterial	 ", slotMaterial, typeof(UMAMaterial), false) as UMAMaterial;
			slotFolder = EditorGUILayout.ObjectField("Slot Destination Folder"	, slotFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
			EnforceFolder(ref slotFolder);
			RootBone = EditorGUILayout.TextField("Root Bone (ex:'Global')", RootBone);
			slotName = EditorGUILayout.TextField("Slot Name", slotName);
			binarySerialization = EditorGUILayout.Toggle(new GUIContent("Binary Serialization", "Forces the created Mesh object to be serialized as binary. Recommended for large meshes and blendshapes."), binarySerialization);

			EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
			EditorGUILayout.BeginHorizontal();
			createOverlay = EditorGUILayout.Toggle("Create Overlay", createOverlay);
			EditorGUILayout.LabelField(slotName + "_Overlay");
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.BeginHorizontal();
			createRecipe = EditorGUILayout.Toggle("Create Wardrobe Recipe ", createRecipe);
			EditorGUILayout.LabelField(slotName + "_Recipe");
			EditorGUILayout.EndHorizontal();
			addToGlobalLibrary = EditorGUILayout.Toggle("Add To Global Library", addToGlobalLibrary);
			if (UMAContext.Instance != null)
			{
				if (UMAContext.Instance.slotLibrary != null)
				{
					addToLocalLibrary = EditorGUILayout.Toggle("Add to Scene Library", addToLocalLibrary);
				}
			}

			if (GUILayout.Button("Create Slot"))
			{
				Debug.Log("Processing...");
				SlotDataAsset sd = CreateSlot();
				if (sd != null)
				{
					Debug.Log("Success.");
					string AssetPath = AssetDatabase.GetAssetPath(sd.GetInstanceID());
					if (addToGlobalLibrary)
					{
						UMAAssetIndexer.Instance.EvilAddAsset(typeof(SlotDataAsset), sd);
					}
					if (addToLocalLibrary && UMAContext.Instance != null)
					{
						if (UMAContext.Instance.slotLibrary != null)
						{
							UMAContext.Instance.slotLibrary.AddSlotAsset(sd);
						}
					}
					OverlayDataAsset od = null;
					if (createOverlay)
					{
						od = CreateOverlay(AssetPath.Replace(sd.name, sd.slotName + "_Overlay"), sd);
					}
					if (createRecipe)
					{
						CreateRecipe(AssetPath.Replace(sd.name, sd.slotName + "_Recipe"), sd, od);
					}
				}
			}


			if (slotMesh != null )
			{
				if( slotMesh.localBounds.size.x > 10.0f || slotMesh.localBounds.size.y > 10.0f || slotMesh.localBounds.size.z > 10.0f)
					EditorGUILayout.HelpBox ("This slot's size is very large. It's import scale may be incorrect!", MessageType.Warning);

				if( slotMesh.localBounds.size.x < 0.01f || slotMesh.localBounds.size.y < 0.01f || slotMesh.localBounds.size.z < 0.01f)
					EditorGUILayout.HelpBox ("This slot's size is very small. It's import scale may be incorrect!", MessageType.Warning);

                if (slotName == null || slotName == "")
                {
                    slotName = slotMesh.name;
                }
                if (RootBone == null || RootBone == "")
                {
                    RootBone = "Global";
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

			SlotDataAsset sd = CreateSlot_Internal();

			return sd;
		}

		private OverlayDataAsset CreateOverlay(string path, SlotDataAsset sd)
		{
			OverlayDataAsset asset = ScriptableObject.CreateInstance<OverlayDataAsset>();
			asset.overlayName = slotName + "_Overlay";
			asset.material = sd.material;
			AssetDatabase.CreateAsset(asset, path);
			AssetDatabase.SaveAssets();
			if (addToGlobalLibrary)
			{
				UMAAssetIndexer.Instance.EvilAddAsset(typeof(OverlayDataAsset), asset);
			}
			if (addToLocalLibrary && UMAContext.Instance != null)
			{
				if (UMAContext.Instance.overlayLibrary != null)
				{
					UMAContext.Instance.overlayLibrary.AddOverlayAsset(asset);
				}
			}
			return asset;
		}

		private void CreateRecipe(string path, SlotDataAsset sd, OverlayDataAsset od)
		{
			UMAEditorUtilities.CreateRecipe(path, sd, od, sd.name, addToGlobalLibrary);
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
			SlotDataAsset slot = UMASlotProcessingUtil.CreateSlotData(AssetDatabase.GetAssetPath(slotFolder), GetAssetFolder(), GetAssetName(), slotMesh, material, normalReferenceMesh,RootBone, binarySerialization);
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

					SlotDataAsset sd = null;
					float current = 1f;
					float total = (float)meshes.Count;

					foreach(var mesh in meshes)
					{
						EditorUtility.DisplayProgressBar(string.Format("Creating Slots {0} of {1}", current, total), string.Format("Slot: {0}", mesh.name), (current / total));
						slotMesh = mesh;
						GetMaterialName(mesh.name, mesh);
						sd = CreateSlot();
						if (sd != null)
						{
							Debug.Log("Batch importer processed mesh: " + slotName);
							string AssetPath = AssetDatabase.GetAssetPath(sd.GetInstanceID());
							if (createOverlay)
							{
								CreateOverlay(AssetPath.Replace(sd.name, sd.slotName + "_Overlay"), sd);
							}
							if (createRecipe)
							{
								CreateRecipe(AssetPath.Replace(sd.name, sd.slotName + "_Recipe"), sd, null);
							}
						}
						current++;
					}
					EditorUtility.ClearProgressBar();
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
			window.titleContent.text = "Slot Builder";
		}
	}
}