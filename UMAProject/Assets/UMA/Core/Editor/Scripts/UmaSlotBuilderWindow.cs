using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditorInternal;

namespace UMA.Editors
{
	public class UmaSlotBuilderWindow : EditorWindow 
	{
		/// <summary>
		/// This class is pretty dumb. It exists solely because "string" has no default constructor, so can't be created using reflection.
		/// </summary>
		public class BoneName : Object
		{
			public string strValue;
			public static implicit operator BoneName(string value)
			{
				return new BoneName(value);
			}

			public static BoneName operator +(BoneName first, BoneName second)
			{
				return new BoneName(first.strValue + second.strValue);
			}

			public BoneName()
            {
				strValue = "";
            }

            public override string ToString()
            {
                return strValue;
            }

            public BoneName(string val)
            {
				strValue = val;
            }
		}

		public string slotName;
		public string RootBone = "Global";
		public UnityEngine.Object slotFolder;
		public UnityEngine.Object relativeFolder;
		public SkinnedMeshRenderer normalReferenceMesh;
		public SkinnedMeshRenderer slotMesh;
		public GameObject AllSlots;
		public UMAMaterial slotMaterial;
		public bool createOverlay;
		public bool createRecipe;
		public bool addToGlobalLibrary;
		public bool binarySerialization;
		public bool calcTangents=true;
		public bool udimAdjustment = true;
        public string errmsg = "";
		public List<string> Tags = new List<string>();
		public bool showTags;
		public bool nameAfterMaterial=false;
		public List<BoneName> KeepBones = new List<BoneName>();
		private ReorderableList boneList;
		private bool boneListInitialized;
		public string BoneStripper;
		private bool useRootFolder=false;
		public bool keepAllBones = false;
		public bool rotationEnabled = false;
        public Quaternion rotation = Quaternion.identity;
		public bool invertX;
        public bool invertY;
        public bool invertZ;

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

		string GetSlotName(SkinnedMeshRenderer smr)
		{
			if (nameAfterMaterial)
			{
				return smr.sharedMaterial.name.ToTitleCase();
			}
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

		private void InitBoneList()
		{
			boneList = new ReorderableList(KeepBones,typeof(BoneName), true, true, true, true);
            boneList.drawHeaderCallback = (Rect rect) =>
            {
				EditorGUI.LabelField(rect, "Keep Bones Containing");
			};
            boneList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
				rect.y += 2;
				KeepBones[index].strValue = EditorGUI.TextField(new Rect(rect.x + 10, rect.y, rect.width - 10, EditorGUIUtility.singleLineHeight), KeepBones[index].strValue);
			};
			boneListInitialized = true;
		}

		void OnGUI()
        {
			if (!boneListInitialized || boneList == null)
			{
				InitBoneList();
			}
			GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
			GUILayout.Label("Common Parameters", EditorStyles.boldLabel);
            normalReferenceMesh = EditorGUILayout.ObjectField("Seams Mesh (Optional)  ", normalReferenceMesh, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;

            slotMaterial = EditorGUILayout.ObjectField("UMAMaterial	 ", slotMaterial, typeof(UMAMaterial), false) as UMAMaterial;
            slotFolder = EditorGUILayout.ObjectField("Slot Destination Folder", slotFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;

			//EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
			EditorGUILayout.BeginHorizontal();
			createOverlay = EditorGUILayout.Toggle("Create Overlay", createOverlay);
			createRecipe = EditorGUILayout.Toggle("Create Wardrobe Recipe ", createRecipe);
			EditorGUILayout.EndHorizontal();
			//EditorGUILayout.LabelField(slotName + "_Overlay");
			//EditorGUILayout.EndHorizontal();
			//EditorGUILayout.BeginHorizontal();
			//EditorGUILayout.LabelField(slotName + "_Recipe");
			//EditorGUILayout.EndHorizontal();
			EditorGUILayout.BeginHorizontal();
			binarySerialization = EditorGUILayout.Toggle(new GUIContent("Binary Serialization", "Forces the created Mesh object to be serialized as binary. Recommended for large meshes and blendshapes."), binarySerialization);
			addToGlobalLibrary = EditorGUILayout.Toggle("Add To Global Library", addToGlobalLibrary);
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.BeginHorizontal();
            calcTangents = EditorGUILayout.Toggle("Calculate Tangents", calcTangents);
            udimAdjustment = EditorGUILayout.Toggle("Adjust for UDIM", udimAdjustment);
            EditorGUILayout.EndHorizontal();
			EditorGUILayout.BeginHorizontal();
            useRootFolder = EditorGUILayout.Toggle("Write to Root Folder", useRootFolder);
            keepAllBones = EditorGUILayout.Toggle("Keep All Bones", keepAllBones);
            EditorGUILayout.EndHorizontal();
            BoneStripper = EditorGUILayout.TextField("Strip from Bones:", BoneStripper);
            rotationEnabled = EditorGUILayout.Toggle("Enable Rotation", rotationEnabled);
            rotation = Quaternion.Euler(EditorGUILayout.Vector3Field("Rotation", rotation.eulerAngles));
			invertX = EditorGUILayout.Toggle("Invert X", invertX);
            invertY = EditorGUILayout.Toggle("Invert Y", invertY);
            invertZ = EditorGUILayout.Toggle("Invert Z", invertZ);
            if (rotationEnabled)
            {
                EditorGUILayout.HelpBox("Rotation is enabled. This will rotate the slot mesh by the specified amount. This is useful for correcting the orientation of the slot mesh.", MessageType.Info);
            }

            boneList.DoLayoutList();
			GUIHelper.EndVerticalPadded(10);


			DoDragDrop();

			EnforceFolder(ref slotFolder);
			//
			// For now, we will disable this option.
			// It doesn't work in most cases.
			// RootBone = EditorGUILayout.TextField("Root Bone (ex:'Global')", RootBone);
			// 
			GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.85f, 1f),EditorStyles.helpBox);
			GUILayout.Label("Single Slot Processing", EditorStyles.boldLabel);

			var newslotMesh = EditorGUILayout.ObjectField("Slot Mesh  ", slotMesh, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;
            if (newslotMesh != slotMesh)
            {
                errmsg = "";
                slotMesh = newslotMesh;
                slotName = newslotMesh.name;
            }

            slotName = EditorGUILayout.TextField("Slot Name", slotName);



			if (GUILayout.Button("Verify Slot"))
			{
				if (slotMesh == null)
				{
					errmsg = "Slot is null.";
				}
				else
				{
					Vector2[] uv = slotMesh.sharedMesh.uv;
					foreach (Vector2 v in uv)
					{
						if (v.x > 1.0f || v.x < 0.0f || v.y > 1.0f || v.y < 0.0f)
						{
							errmsg = "UV Coordinates are out of range and will likely have issues with atlassed materials. Textures should not be tiled unless using non-atlassed materials. If this slot is using UDIMs, please check the box to adjust for UDIM in the slot options.";
							break;
						}
					}
					if (string.IsNullOrEmpty(errmsg))
					{
						errmsg = "No errors found";
					}
				}
			}

			if (!string.IsNullOrEmpty(errmsg))
			{
				EditorGUILayout.HelpBox(errmsg, MessageType.Warning);
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

			GUIHelper.EndVerticalPadded(10);

            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            GUILayout.Space(10);
            showTags = EditorGUILayout.Foldout(showTags, "Tags");
			GUILayout.EndHorizontal();
            if (showTags)
            {
                GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f),EditorStyles.helpBox);
                // Draw the button area
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Add Tag", GUILayout.Width(80)))
                {
                    Tags.Add("");
                    Repaint();
                }

                GUILayout.Label(Tags.Count + " Tags defined");
                GUILayout.EndHorizontal();

                if (Tags.Count == 0)
                {
                    GUILayout.Label("No tags defined", EditorStyles.helpBox);
                }
                else
                {
                    int del = -1;

                    for (int i = 0; i < Tags.Count; i++)
                    {
                        GUILayout.BeginHorizontal();
                        Tags[i] = GUILayout.TextField(Tags[i]);
                        if (GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        {
                            del = i;
                        }
                        GUILayout.EndHorizontal();
                    }
                    if (del >= 0)
                    {
                        Tags.RemoveAt(del);
                        Repaint();
                    }
                }
                // Draw the tags (or "No tags defined");
                GUIHelper.EndVerticalPadded(10);
            }


            if (slotMesh != null)
            {
                if (slotMesh.localBounds.size.x > 10.0f || slotMesh.localBounds.size.y > 10.0f || slotMesh.localBounds.size.z > 10.0f)
                {
                    EditorGUILayout.HelpBox("This slot's size is very large. It's import scale may be incorrect!", MessageType.Warning);
                }

                if (slotMesh.localBounds.size.x < 0.01f || slotMesh.localBounds.size.y < 0.01f || slotMesh.localBounds.size.z < 0.01f)
                {
                    EditorGUILayout.HelpBox("This slot's size is very small. It's import scale may be incorrect!", MessageType.Warning);
                }

                if (slotName == null || slotName == "")
                {
                    slotName = slotMesh.name;
                }
                if (RootBone == null || RootBone == "")
                {
                    RootBone = "Global";
                }
            }

        }

        private void DoDragDrop()
        {
			GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
			GUILayout.Label("Automatic Drag and Drop processing", EditorStyles.boldLabel);
			Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
			nameAfterMaterial = GUILayout.Toggle(nameAfterMaterial, "Name slot by material");
			Color save = GUI.color;
			GUI.color = Color.white;
            GUI.Box(dropArea, "Drag FBX GameObject or meshes here to generate all slots and overlays for the GameObject");
            relativeFolder = EditorGUILayout.ObjectField("Relative Folder", relativeFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
            EnforceFolder(ref relativeFolder);

            DropAreaGUI(dropArea);			
			GUI.color = save;
			GUIHelper.EndVerticalPadded(10);
        }

        private SlotDataAsset CreateSlot()
		{
            if (slotName == null || slotName == "")
            {
				Debug.LogError("slotName must be specified.");
				return null;
			}

			SlotDataAsset sd = CreateSlot_Internal();
            if (sd == null)
            {
                return null;
            }

			UMAUpdateProcessor.UpdateSlot(sd);
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
				Debug.LogError("No UMAMaterial specified! You must specify an UMAMaterial to build a slot.");
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

			List<string> KeepList = new List<string>();
			foreach(BoneName b in KeepBones)
            {
				KeepList.Add(b.strValue);
            }
			if (!string.IsNullOrEmpty(BoneStripper))
			{
				int stripCount = 0;
				foreach (Transform t in slotMesh.bones)
				{
					if (t.name.Contains(BoneStripper))
					{
						t.name = t.name.Replace(BoneStripper, "");
						stripCount++;
					}
				}
				Debug.Log("Stripped " + stripCount + " Bones");
			}

            SlotBuilderParameters sbp = new SlotBuilderParameters();
            sbp.calculateTangents = calcTangents;
            sbp.binarySerialization = binarySerialization;
            sbp.nameByMaterial = nameAfterMaterial;
            sbp.stripBones = BoneStripper;
            sbp.rootBone = RootBone;
            sbp.assetName = GetAssetName();
            sbp.slotName = GetSlotName(slotMesh);
            sbp.assetFolder = GetAssetFolder();
            sbp.slotFolder = AssetDatabase.GetAssetPath(slotFolder);
            sbp.keepList = KeepList;
            sbp.slotMesh = slotMesh;
            sbp.seamsMesh = normalReferenceMesh;
            sbp.material = material;
            sbp.udimAdjustment = udimAdjustment;
            sbp.useRootFolder = false;
			sbp.keepAllBones = keepAllBones;
			sbp.rotation = rotation;
			sbp.rotationEnabled = rotationEnabled;
            sbp.invertX = invertX;
            sbp.invertY = invertY;
            sbp.invertZ = invertZ;


            SlotDataAsset slot = UMASlotProcessingUtil.CreateSlotData(sbp);
			if (slot == null)
            {
                Debug.LogError("Failed to create SlotDataAsset");
                return null;
            }
            slot.tags = Tags.ToArray();
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

        private string AsciiName(string name)
        {
			return name.ToTitleCase();
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

		[MenuItem("UMA/Slot Builder", priority = 20)]
		public static void OpenUmaTexturePrepareWindow()
		{
			UmaSlotBuilderWindow window = (UmaSlotBuilderWindow)EditorWindow.GetWindow(typeof(UmaSlotBuilderWindow));
			window.titleContent.text = "Slot Builder";
		}
	}
}
