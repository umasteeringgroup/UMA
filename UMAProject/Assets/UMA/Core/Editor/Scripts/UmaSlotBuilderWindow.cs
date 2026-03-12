using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditorInternal;
using System.IO;
using System.Xml.Serialization;
using System.Threading; // Added for JSON save/load

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

        private string EnsureWardrobeFolder()
        {
            if (slotFolder == null)
            {
                return null;
            }

            string basePath = AssetDatabase.GetAssetPath(slotFolder);
            if (string.IsNullOrEmpty(basePath))
            {
                return null;
            }

            string wardrobePath = basePath.TrimEnd('/') + "/Wardrobe";
            if (AssetDatabase.IsValidFolder(wardrobePath))
            {
                return wardrobePath;
            }

            string guid = AssetDatabase.CreateFolder(basePath, "Wardrobe");
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogWarning("[SlotBuilder] Could not create Wardrobe folder under: " + basePath);
                return basePath;
            }

            return wardrobePath;
        }

        private void CreateWardrobeRecipesForSlots(UMASlotProcessingUtil.SlotBuildResult result, string wardrobeFolderPath)
        {
            if (result == null || result.Slots == null || result.Slots.Count == 0)
            {
                return;
            }

            if (string.IsNullOrEmpty(wardrobeFolderPath))
            {
                wardrobeFolderPath = EnsureWardrobeFolder();
            }

            if (string.IsNullOrEmpty(wardrobeFolderPath))
            {
                return;
            }

            for (int i = 0; i < result.Slots.Count; i++)
            {
                var sda = result.Slots[i];
                if (sda == null)
                {
                    continue;
                }

                string slotAssetName = sda.slotName;
                if (string.IsNullOrEmpty(slotAssetName))
                {
                    slotAssetName = sda.name;
                }

                EditorUtility.DisplayProgressBar("Creating Wardrobe Recipes", "Recipe for: " + slotAssetName, 1f);
                Thread.Sleep(0);

                var recipe = new UMA.UMAData.UMARecipe();
                recipe.ClearDna();

                var sd = new SlotData(sda);
                if (result.SlotToOverlay != null && result.SlotToOverlay.TryGetValue(sda, out var oda) && oda != null)
                {
                    var od = new OverlayData(oda);
                    sd.AddOverlay(od);
                }
                recipe.SetSlot(0, sd);

                string recipeBaseName = slotAssetName + "_Recipe";
                string recipePath = Path.Combine(wardrobeFolderPath, recipeBaseName + ".asset").Replace('\\', '/');
                recipePath = AssetDatabase.GenerateUniqueAssetPath(recipePath);

                var uwr = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
                uwr.name = recipeBaseName;
                uwr.Save(recipe);
                AssetDatabase.CreateAsset(uwr, recipePath);
                EditorUtility.SetDirty(uwr);

                if (addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(UMAWardrobeRecipe), uwr);
                }
            }

            AssetDatabase.SaveAssets();
        }

        // Persisted state for window parameters
        [System.Serializable]
        private class SlotBuilderWindowState
        {
            public string slotName;
            public string RootBone;
            public bool createOverlay;
            public bool createRecipe;
            public bool isBaseRaceRecipe;
            public bool addToGlobalLibrary;
            public bool binarySerialization;
            public bool calcTangents;
            public bool clearNormals;
            public bool clearTangents;
            public bool udimAdjustment;
            public int udimTilesU;
            public int udimTilesV;
            public bool nameAfterMaterial;
            public List<string> KeepBones = new List<string>();
            public string BoneStripper;
            public bool useRootFolder;
            public bool keepAllBones;
            public bool rotationEnabled;
            public Vector3 rotationEuler;
            public bool invertX;
            public bool invertY;
            public bool invertZ;
            public bool alwaysRecreateSlots;
            public List<string> Tags = new List<string>();
            // New: persist material and folder by asset path
            public string slotMaterialPath;
            public string slotFolderPath;

            // Slot LOD generation
            public bool generateSlotLods;
            public bool useUnityLodGenerator;
            public int slotLodMaxLevels;
            public int slotLodMinTriangles;
            public float slotLodTargetReductionPerLevel;
            public bool slotLodPreserveBoundaryEdges;
            public float slotLodBoundaryWeight;
            public Vector2 scrollPos = Vector2.zero;

            // Drag & drop batch filters
            public bool filterIgnoreMeshesEnabled;
            public string filterIgnoreMeshesContaining;
            public bool filterOnlyIncludeMeshesEnabled;
            public string filterOnlyIncludeMeshesContaining;

            public bool pendingSmrsCompactView;
            public bool pendingSmrsFilterEnabled;
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
        public bool appendTypeToName = true;
        public bool addUDIMTileNumbers = true;
        public bool isBaseRaceRecipe = false;
        public bool addToGlobalLibrary;
        public bool binarySerialization;
        public bool calcTangents=true;
        public bool clearNormals=false;
        public bool clearTangents=false;
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
        public bool alwaysRecreateSlots = false;
        public Vector2 scrollPos = Vector2.zero;

        [System.Serializable]
        private class PendingSmrEntry
        {
            public SkinnedMeshRenderer smr;
            public bool selected = true;
        }

        private List<PendingSmrEntry> _pendingSmrs = new List<PendingSmrEntry>();
        private Vector2 _pendingSmrsScroll;
        private bool _pendingSmrsCompactView = true;
        private bool _pendingSmrsFilterEnabled = false;

        // Drag & drop batch filters
        private bool _filterIgnoreMeshesEnabled;
        private string _filterIgnoreMeshesContaining = string.Empty;
        private bool _filterOnlyIncludeMeshesEnabled;
        private string _filterOnlyIncludeMeshesContaining = string.Empty;

        // Slot LOD generation
        public bool generateSlotLods = false;
        public bool useUnityLodGenerator = false;
        public int slotLodMaxLevels = 8;
        public int slotLodMinTriangles = 256;
        public float slotLodTargetReductionPerLevel = 0.5f;
        public bool slotLodPreserveBoundaryEdges = true;
        public float slotLodBoundaryWeight = 10f;

        // UDIM grid size (default 10x10)
        public int udimTilesU = 10;
        public int udimTilesV = 10;

        // File path in UMA root folder for persistence
        private static string GetPersistFilePath()
        {
#if UNITY_EDITOR
            string umaRoot = UMA.UMASettings.FindUMAFullPath(); // e.g., "Assets/UMA"
            if (string.IsNullOrEmpty(umaRoot)) umaRoot = "Assets/UMA";
            // Convert to absolute path
            string rel = umaRoot.StartsWith("Assets/") ? umaRoot.Substring("Assets/".Length) : umaRoot.TrimStart('/');
            string absRoot = Path.Combine(Application.dataPath, rel.Replace('/', Path.DirectorySeparatorChar));
            return Path.Combine(absRoot, "SlotBuilderParameters.json");
#else
            return string.Empty;
#endif
        }

        private void SaveStateToJson()
        {
            try
            {
                var state = new SlotBuilderWindowState();
                state.slotName = slotName;
                state.RootBone = RootBone;
                state.createOverlay = createOverlay;
                state.createRecipe = createRecipe;
                state.isBaseRaceRecipe = isBaseRaceRecipe;
                state.addToGlobalLibrary = addToGlobalLibrary;
                state.binarySerialization = binarySerialization;
                state.calcTangents = calcTangents;
                state.clearNormals = clearNormals;
                state.clearTangents = clearTangents;
                state.udimAdjustment = udimAdjustment;
                state.udimTilesU = udimTilesU;
                state.udimTilesV = udimTilesV;
                state.nameAfterMaterial = nameAfterMaterial;
                state.BoneStripper = BoneStripper;
                state.useRootFolder = useRootFolder;
                state.keepAllBones = keepAllBones;
                state.rotationEnabled = rotationEnabled;
                state.rotationEuler = rotation.eulerAngles;
                state.invertX = invertX;
                state.invertY = invertY;
                state.invertZ = invertZ;
                state.alwaysRecreateSlots = alwaysRecreateSlots;
                state.scrollPos = scrollPos;
                // Copy KeepBones strings
                state.KeepBones = new List<string>(KeepBones != null ? KeepBones.Count : 0);
                if (KeepBones != null)
                {
                    for (int i = 0; i < KeepBones.Count; i++)
                    {
                        state.KeepBones.Add(KeepBones[i]?.strValue ?? string.Empty);
                    }
                }
                // Tags
                state.Tags = new List<string>(Tags ?? new List<string>());
                // New: persist paths for material and folder
                state.slotMaterialPath = slotMaterial != null ? AssetDatabase.GetAssetPath(slotMaterial) : string.Empty;
                state.slotFolderPath = slotFolder != null ? AssetDatabase.GetAssetPath(slotFolder) : string.Empty;

                state.generateSlotLods = generateSlotLods;
                state.useUnityLodGenerator = useUnityLodGenerator;
                state.slotLodMaxLevels = slotLodMaxLevels;
                state.slotLodMinTriangles = slotLodMinTriangles;
                state.slotLodTargetReductionPerLevel = slotLodTargetReductionPerLevel;
                state.slotLodPreserveBoundaryEdges = slotLodPreserveBoundaryEdges;
                state.slotLodBoundaryWeight = slotLodBoundaryWeight;

                state.filterIgnoreMeshesEnabled = _filterIgnoreMeshesEnabled;
                state.filterIgnoreMeshesContaining = _filterIgnoreMeshesContaining;
                state.filterOnlyIncludeMeshesEnabled = _filterOnlyIncludeMeshesEnabled;
                state.filterOnlyIncludeMeshesContaining = _filterOnlyIncludeMeshesContaining;

                state.pendingSmrsCompactView = _pendingSmrsCompactView;
                state.pendingSmrsFilterEnabled = _pendingSmrsFilterEnabled;

                string json = JsonUtility.ToJson(state, true);
                string fp = GetPersistFilePath();
                string dir = Path.GetDirectoryName(fp);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(fp, json);
#if UNITY_EDITOR
                AssetDatabase.Refresh();
#endif
            }
            catch { }
        }

        private void LoadStateFromJson()
        {
            try
            {
                string fp = GetPersistFilePath();
                if (string.IsNullOrEmpty(fp) || !File.Exists(fp)) return;
                string json = File.ReadAllText(fp);
                if (string.IsNullOrEmpty(json)) return;
                var state = JsonUtility.FromJson<SlotBuilderWindowState>(json);
                if (state == null) return;

                slotName = state.slotName;
                RootBone = string.IsNullOrEmpty(state.RootBone) ? RootBone : state.RootBone;
                createOverlay = state.createOverlay;
                createRecipe = state.createRecipe;
                isBaseRaceRecipe = state.isBaseRaceRecipe;
                addToGlobalLibrary = state.addToGlobalLibrary;
                binarySerialization = state.binarySerialization;
                calcTangents = state.calcTangents;
                clearNormals = state.clearNormals;
                clearTangents = state.clearTangents;
                udimAdjustment = state.udimAdjustment;
                udimTilesU = state.udimTilesU;
                udimTilesV = state.udimTilesV;
                nameAfterMaterial = state.nameAfterMaterial;
                BoneStripper = state.BoneStripper;
                useRootFolder = state.useRootFolder;
                keepAllBones = state.keepAllBones;
                rotationEnabled = state.rotationEnabled;
                rotation = Quaternion.Euler(state.rotationEuler);
                invertX = state.invertX;
                invertY = state.invertY;
                invertZ = state.invertZ;
                alwaysRecreateSlots = state.alwaysRecreateSlots;

                generateSlotLods = state.generateSlotLods;
                useUnityLodGenerator = state.useUnityLodGenerator;
                slotLodMaxLevels = state.slotLodMaxLevels;
                slotLodMinTriangles = state.slotLodMinTriangles;
                slotLodTargetReductionPerLevel = state.slotLodTargetReductionPerLevel;
                slotLodPreserveBoundaryEdges = state.slotLodPreserveBoundaryEdges;
                slotLodBoundaryWeight = state.slotLodBoundaryWeight;
                scrollPos = state.scrollPos;

                // When upgrading from older JSON, these fields may be missing;
                // default to disabled unless clearly enabled by non-empty text.
                _filterIgnoreMeshesContaining = state.filterIgnoreMeshesContaining ?? string.Empty;
                _filterOnlyIncludeMeshesContaining = state.filterOnlyIncludeMeshesContaining ?? string.Empty;

                _filterIgnoreMeshesEnabled = state.filterIgnoreMeshesEnabled;
                if (!_filterIgnoreMeshesEnabled && !string.IsNullOrEmpty(_filterIgnoreMeshesContaining))
                {
                    // keep disabled
                }

                _filterOnlyIncludeMeshesEnabled = state.filterOnlyIncludeMeshesEnabled;
                if (!_filterOnlyIncludeMeshesEnabled && !string.IsNullOrEmpty(_filterOnlyIncludeMeshesContaining))
                {
                    // keep disabled
                }

                _pendingSmrsCompactView = state.pendingSmrsCompactView;
                _pendingSmrsFilterEnabled = state.pendingSmrsFilterEnabled;

                // Restore KeepBones list
                KeepBones = new List<BoneName>();
                if (state.KeepBones != null)
                {
                    for (int i = 0; i < state.KeepBones.Count; i++)
                    {
                        KeepBones.Add(new BoneName(state.KeepBones[i] ?? string.Empty));
                    }
                }
                // Restore Tags
                Tags = state.Tags ?? new List<string>();
                // New: restore slotMaterial and slotFolder from paths
                if (!string.IsNullOrEmpty(state.slotMaterialPath))
                {
                    var mat = AssetDatabase.LoadAssetAtPath<UMAMaterial>(state.slotMaterialPath);
                    if (mat != null) slotMaterial = mat;
                }
                if (!string.IsNullOrEmpty(state.slotFolderPath))
                {
                    var folderObj = AssetDatabase.LoadMainAssetAtPath(state.slotFolderPath);
                    if (folderObj != null) slotFolder = folderObj;
                }
            }
            catch { }
        }

        private void OnEnable()
        {
            LoadStateFromJson();
        }

        private void OnDisable()
        {
            SaveStateToJson();
        }

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

        private static bool singleInstanceOpen = false;
        void OnGUI()
        {
            if (!boneListInitialized || boneList == null)
            {
                InitBoneList();
            }
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            GUILayout.Label("Common Parameters", EditorStyles.boldLabel);
            normalReferenceMesh = EditorGUILayout.ObjectField("Seams Mesh (Optional)  ", normalReferenceMesh, typeof(SkinnedMeshRenderer), false) as SkinnedMeshRenderer;

            slotMaterial = EditorGUILayout.ObjectField("UMAMaterial\t ", slotMaterial, typeof(UMAMaterial), false) as UMAMaterial;
            slotFolder = EditorGUILayout.ObjectField("Slot Destination Folder", slotFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;

            alwaysRecreateSlots = EditorGUILayout.Toggle(new GUIContent("Always recreate slots", "If enabled, any existing SlotDataAsset at the target path will be deleted and recreated instead of updated."), alwaysRecreateSlots);
            //EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.BeginHorizontal();
            isBaseRaceRecipe = EditorGUILayout.Toggle("Is Base Race Recipe", isBaseRaceRecipe);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            createOverlay = EditorGUILayout.Toggle("Create Overlay", createOverlay);
            if (!isBaseRaceRecipe)
            {
                createRecipe = EditorGUILayout.Toggle("Create Wardrobe Recipe ", createRecipe);
            }
            else
            {
                createRecipe = EditorGUILayout.Toggle("Create Base Recipe ", createRecipe);
            }

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
            // appendTypeToName = EditorGUILayout.Toggle("Append Type To Name", appendTypeToName);
            addUDIMTileNumbers = EditorGUILayout.Toggle("Add UDIM Tile Numbers", addUDIMTileNumbers);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            calcTangents = EditorGUILayout.Toggle("Calculate Tangents", calcTangents);
            EditorGUILayout.EndHorizontal();


            EditorGUILayout.BeginHorizontal();
            clearNormals = EditorGUILayout.Toggle("Clear Blendshape Normals", clearNormals);
            clearTangents = EditorGUILayout.Toggle("Clear Blendshape Tangents", clearTangents);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            useRootFolder = EditorGUILayout.Toggle("Write to Root Folder", useRootFolder);

            keepAllBones = EditorGUILayout.Toggle("Keep All Bones", keepAllBones);
            EditorGUILayout.EndHorizontal();
            BoneStripper = EditorGUILayout.TextField("Strip from Bones:", BoneStripper);

            rotationEnabled = EditorGUILayout.Toggle("Enable Rotation", rotationEnabled);
            if (rotationEnabled)
            {
                rotation = Quaternion.Euler(EditorGUILayout.Vector3Field("Rotation", rotation.eulerAngles));
                invertX = EditorGUILayout.Toggle("Invert X", invertX);
                invertY = EditorGUILayout.Toggle("Invert Y", invertY);
                invertZ = EditorGUILayout.Toggle("Invert Z", invertZ);
                EditorGUILayout.HelpBox("Rotation is enabled. This will rotate the slot mesh by the specified amount. This is useful for correcting the orientation of the slot mesh.", MessageType.Info);
            }

            boneList.DoLayoutList();
            GUIHelper.EndVerticalPadded(10);

            // UDIM tile dimensions
            DoUDIMGUI();

            DoLODGUI();

            DoDragDrop();

            DoSingleSlotGUI();

            GUIHelper.BeginVerticalPadded(2, new Color(0.75f, 0.875f, 1f), EditorStyles.helpBox);
            showTags = EditorGUILayout.Foldout(showTags, "Tags");
            if (showTags)
            {
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




            }
            GUIHelper.EndVerticalPadded(2);


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
            EditorGUILayout.EndScrollView();
        }

        private bool showUDIMGUI;
        private void DoUDIMGUI()
        {
            GUIHelper.BeginVerticalPadded(2, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            showUDIMGUI = EditorGUILayout.Foldout(showUDIMGUI, "UDIM Settings", true);
            if (showUDIMGUI)
            {
                udimAdjustment = EditorGUILayout.Toggle("Adjust for UDIM", udimAdjustment);
                using (new EditorGUI.DisabledScope(!udimAdjustment))
                {
                    udimTilesU = EditorGUILayout.IntField(new GUIContent("UDIM Tiles U", "Number of tiles horizontally (e.g., 10)"), Mathf.Max(0, udimTilesU));
                    udimTilesV = EditorGUILayout.IntField(new GUIContent("UDIM Tiles V", "Number of tiles vertically (e.g., 10)"), Mathf.Max(0, udimTilesV));
                }
            }
            GUIHelper.EndVerticalPadded(2);
        }

        private void DoSingleSlotGUI()
        {
            EnforceFolder(ref slotFolder);
            GUIHelper.BeginVerticalPadded(2, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            singleInstanceOpen = EditorGUILayout.Foldout(singleInstanceOpen, "Single Slot Processing", true);
            if (singleInstanceOpen)
            {
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
                    // Save current parameters before creating
                    SaveStateToJson();

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
                        // Overlay and Recipe creation moved to UMASlotProcessingUtil via SlotBuilderParameters flags
                    }
                }

            }
            GUIHelper.EndVerticalPadded(2);
        }

        private bool ShouldProcessMeshByFilters(SkinnedMeshRenderer smr)
        {
            if (smr == null)
            {
                return false;
            }

            string meshName = smr.name;
            if (meshName == null)
            {
                meshName = string.Empty;
            }

            if (_filterIgnoreMeshesEnabled)
            {
                string ignore = _filterIgnoreMeshesContaining;
                if (!string.IsNullOrEmpty(ignore))
                {
                    if (meshName.IndexOf(ignore, System.StringComparison.InvariantCultureIgnoreCase) >= 0)
                    {
                        Debug.Log(string.Format("[SlotBuilder] Skipping mesh '{0}' due to Ignore filter ('{1}')", meshName, ignore));
                        return false;
                    }
                }
            }

            if (_filterOnlyIncludeMeshesEnabled)
            {
                string include = _filterOnlyIncludeMeshesContaining;
                if (!string.IsNullOrEmpty(include))
                {
                    if (meshName.IndexOf(include, System.StringComparison.InvariantCultureIgnoreCase) < 0)
                    {
                        Debug.Log(string.Format("[SlotBuilder] Skipping mesh '{0}' due to Only-Include filter ('{1}')", meshName, include));
                        return false;
                    }
                }
            }

            return true;
        }


        private bool showLodSettings = false;
        private void DoLODGUI()
        {
            GUIHelper.BeginVerticalPadded(2, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            showLodSettings = EditorGUILayout.Foldout(showLodSettings, "Slot LOD Generation Settings", true);
            if (showLodSettings)
            {
                EditorGUILayout.BeginHorizontal();
                generateSlotLods = EditorGUILayout.Toggle(new GUIContent("Generate Slot LODs", "Generate internal per-slot LOD triangle buffers/ranges for UMASimpleLOD. When enabled, Unity 6 mesh LOD copying is not used."), generateSlotLods);
                EditorGUILayout.EndHorizontal();
                using (new EditorGUI.DisabledScope(!generateSlotLods))
                {
#if UNITY_6000_2_OR_NEWER
                    useUnityLodGenerator = EditorGUILayout.Toggle(new GUIContent("Use Unity LOD Generator", "Use Unity's built-in mesh LOD generator instead of UMA's custom generator."), useUnityLodGenerator);
#endif
                    slotLodMaxLevels = EditorGUILayout.IntSlider(new GUIContent("Max LOD Levels", "Maximum internal LOD levels to generate (including LOD0)."), Mathf.Clamp(slotLodMaxLevels, 1, 8), 1, 8);
                 using (new EditorGUI.DisabledScope(useUnityLodGenerator))
                    {
                        slotLodMinTriangles = EditorGUILayout.IntField(new GUIContent("Min Triangles", "Stop generating when a LOD reaches this triangle count (approx)."), Mathf.Max(0, slotLodMinTriangles));
                        slotLodTargetReductionPerLevel = EditorGUILayout.Slider(new GUIContent("Reduction per Level", "Target triangle reduction ratio per LOD step."), Mathf.Clamp01(slotLodTargetReductionPerLevel), 0.1f, 0.9f);
                        slotLodPreserveBoundaryEdges = EditorGUILayout.Toggle(new GUIContent("Preserve Boundary Edges", "Attempt to preserve open edges to reduce seams when combined with other slots."), slotLodPreserveBoundaryEdges);
                        slotLodBoundaryWeight = EditorGUILayout.FloatField(new GUIContent(
                            "Boundary Weight",
                            "表tDefault / good starting point: 10\n" +
                            "表tIf you see borders \u201Cchewing in\u201D or seams drifting: 20\u201350\n" +
                            "表tIf you see it refusing to reduce enough or leaving too much geometry near borders: 5\u201310\n" +
                            "表tFor slots where border alignment is critical (hands, sleeves, boots, neck, waist): 25\u201375\n" +
                            "表tFor mostly-closed pieces (props, buttons, inner mouth) where border preservation is less important: 0\u201310"),
                            Mathf.Max(0f, slotLodBoundaryWeight));
                    }
                }
            }
            GUIHelper.EndVerticalPadded(2);
        }

        private static bool dragDropOpen = false;
        private void DoDragDrop()
        {
            GUIHelper.BeginVerticalPadded(2, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);
            //dragDropOpen = GUIHelper.FoldoutBar(dragDropOpen, "Drag and Drop Slot Builder");
            dragDropOpen = EditorGUILayout.Foldout(dragDropOpen, "Drag and Drop Slot Builder", true);
            if (dragDropOpen)
            {
                //GUIHelper.BeginVerticalPadded(2, new Color(0.75f, 0.85f, 1f), EditorStyles.helpBox);

                GUILayout.Label("Automatic Drag and Drop processing", EditorStyles.boldLabel);
                Rect dropArea = GUILayoutUtility.GetRect(0.0f, 50.0f, GUILayout.ExpandWidth(true));
                nameAfterMaterial = GUILayout.Toggle(nameAfterMaterial, "Name slot by material");
                Color save = GUI.color;
                GUI.color = Color.white;
                GUI.Box(dropArea, "Drag FBX GameObject or meshes here to generate all slots and overlays for the GameObject");
                relativeFolder = EditorGUILayout.ObjectField("Relative Folder", relativeFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;
                EnforceFolder(ref relativeFolder);

                EditorGUILayout.Space(3);
                EditorGUILayout.LabelField("Batch Filters", EditorStyles.boldLabel);

                EditorGUILayout.BeginHorizontal();
                _filterIgnoreMeshesEnabled = EditorGUILayout.Toggle(_filterIgnoreMeshesEnabled, GUILayout.Width(18));
                using (new EditorGUI.DisabledScope(!_filterIgnoreMeshesEnabled))
                {
                    _filterIgnoreMeshesContaining = EditorGUILayout.TextField("Ignore Meshes containing", _filterIgnoreMeshesContaining);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                _filterOnlyIncludeMeshesEnabled = EditorGUILayout.Toggle(_filterOnlyIncludeMeshesEnabled, GUILayout.Width(18));
                using (new EditorGUI.DisabledScope(!_filterOnlyIncludeMeshesEnabled))
                {
                    _filterOnlyIncludeMeshesContaining = EditorGUILayout.TextField("Only include meshes containing", _filterOnlyIncludeMeshesContaining);
                }
                EditorGUILayout.EndHorizontal();

                DropAreaGUI(dropArea);

                DrawPendingSmrList();
                GUI.color = save;
                //GUIHelper.EndVerticalPadded(10);
            }
            GUIHelper.EndVerticalPadded(2);
        }

        private void DrawPendingSmrList()
        {
            if (_pendingSmrs == null)
            {
                _pendingSmrs = new List<PendingSmrEntry>();
            }

            if (_pendingSmrs.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Pending SkinnedMeshRenderers", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _pendingSmrsFilterEnabled = EditorGUILayout.Toggle(new GUIContent("Filter list", "Filter the pending list using 'Only include meshes containing' text."), _pendingSmrsFilterEnabled);
            GUILayout.FlexibleSpace();
            if (_pendingSmrsCompactView)
            {
                if (GUILayout.Button("Expanded View", GUILayout.Width(120)))
                {
                    _pendingSmrsCompactView = false;
                }
            }
            else
            {
                if (GUILayout.Button("Compact View", GUILayout.Width(120)))
                {
                    _pendingSmrsCompactView = true;
                }
            }
            EditorGUILayout.EndHorizontal();

            int itemCount = _pendingSmrsCompactView ? 5 : 25;
            float rowHeight = EditorGUIUtility.singleLineHeight + 4f;
            float listHeight = itemCount * rowHeight;

            using (var scroll = new EditorGUILayout.ScrollViewScope(_pendingSmrsScroll, GUILayout.MinHeight(listHeight)))
            {
                _pendingSmrsScroll = scroll.scrollPosition;

                string include = _filterOnlyIncludeMeshesContaining;
                bool filterActive = _pendingSmrsFilterEnabled && !string.IsNullOrEmpty(include);

                for (int i = 0; i < _pendingSmrs.Count; i++)
                {
                    var e = _pendingSmrs[i];
                    if (e == null)
                    {
                        continue;
                    }

                    if (filterActive)
                    {
                        string meshName = e.smr != null ? e.smr.name : string.Empty;
                        if (meshName.IndexOf(include, System.StringComparison.InvariantCultureIgnoreCase) < 0)
                        {
                            continue;
                        }
                    }

                    EditorGUILayout.BeginHorizontal();
                    e.selected = EditorGUILayout.Toggle(e.selected, GUILayout.Width(18));
                    using (new EditorGUI.DisabledScope(true))
                    {
                        e.smr = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(e.smr, typeof(SkinnedMeshRenderer), true);
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select All"))
            {
                for (int i = 0; i < _pendingSmrs.Count; i++)
                {
                    if (_pendingSmrs[i] != null)
                    {
                        if (_pendingSmrsFilterEnabled && !string.IsNullOrEmpty(_filterOnlyIncludeMeshesContaining))
                        {
                            string meshName = _pendingSmrs[i].smr != null ? _pendingSmrs[i].smr.name : string.Empty;
                            if (meshName.IndexOf(_filterOnlyIncludeMeshesContaining, System.StringComparison.InvariantCultureIgnoreCase) < 0)
                            {
                                continue;
                            }
                        }
                        _pendingSmrs[i].selected = true;
                    }
                }
            }
            if (GUILayout.Button("Select None"))
            {
                for (int i = 0; i < _pendingSmrs.Count; i++)
                {
                    if (_pendingSmrs[i] != null)
                    {
                        if (_pendingSmrsFilterEnabled && !string.IsNullOrEmpty(_filterOnlyIncludeMeshesContaining))
                        {
                            string meshName = _pendingSmrs[i].smr != null ? _pendingSmrs[i].smr.name : string.Empty;
                            if (meshName.IndexOf(_filterOnlyIncludeMeshesContaining, System.StringComparison.InvariantCultureIgnoreCase) < 0)
                            {
                                continue;
                            }
                        }
                        _pendingSmrs[i].selected = false;
                    }
                }
            }
            if (GUILayout.Button("Clear List"))
            {
                _pendingSmrs.Clear();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(3);
            if (GUILayout.Button("Process checked slots"))
            {
                ProcessPendingSmrs();
            }
        }

        private void ProcessPendingSmrs()
        {
            if (_pendingSmrs == null || _pendingSmrs.Count == 0)
            {
                return;
            }

            var processedEntries = new List<PendingSmrEntry>();

            // Aggregate results from all processed meshes
            var aggregate = new UMASlotProcessingUtil.SlotBuildResult
            {
                Slots = new List<SlotDataAsset>(),
                SlotToOverlay = new Dictionary<SlotDataAsset, OverlayDataAsset>(),
                IsUDIM = false,
                TempAssetsToDelete = new List<string>()
            };

            var deferredDeletes = new HashSet<string>();

            // Preserve original slotName and slotMesh to restore later
            string originalSlotName = slotName;
            var originalSlotMesh = slotMesh;

            // Count selected (after batch filters)
            int selectedCount = 0;
            for (int i = 0; i < _pendingSmrs.Count; i++)
            {
                var e = _pendingSmrs[i];
                if (e != null && e.selected && e.smr != null && ShouldProcessMeshByFilters(e.smr))
                {
                    selectedCount++;
                }
            }
            if (selectedCount == 0)
            {
                EditorUtility.DisplayDialog("Slot Builder", "No SkinnedMeshRenderers matched the current selection and filters.", "OK");
                return;
            }

            float current = 1f;
            float total = (float)selectedCount;

            try
            {
                int progressId = Progress.Start("Batch Slot Creation", "-> Preparing batch...");
                EditorUtility.DisplayProgressBar("Creating Slots", "Preparing batch...", 0f);
                Thread.Sleep(0);

                // Batch mode behavior:
                // - If isBaseRaceRecipe == false: create a Wardrobe subfolder and create one wardrobe recipe per processed slot.
                // - If isBaseRaceRecipe == true: defer to existing behavior (single base recipe can be created later if createRecipe enabled).
                string wardrobeFolderPath = null;
                if (!isBaseRaceRecipe && createRecipe)
                {
                    wardrobeFolderPath = EnsureWardrobeFolder();
                }

                for (int i = 0; i < _pendingSmrs.Count; i++)
                {
                    var e = _pendingSmrs[i];
                    if (e == null || !e.selected || e.smr == null)
                    {
                        continue;
                    }

                    if (!ShouldProcessMeshByFilters(e.smr))
                    {
                        continue;
                    }

                    var mesh = e.smr;
                    Progress.Report(progressId, current/total, $"Slot: {mesh.name} Slot {current} of {total}");
                    EditorUtility.DisplayProgressBar(string.Format("Creating Slots {0} of {1}", current, total), string.Format("Slot: {0}", mesh.name), (current / total));
                    Thread.Sleep(0);

                    slotMesh = mesh;
                    GetMaterialName(mesh.name, mesh);

                    // Build result for this mesh without creating a recipe per mesh
                    var result = CreateSlot_Internal_WithResult(true);
                    if (result != null && result.Slots != null && result.Slots.Count > 0)
                    {
                        aggregate.Slots.AddRange(result.Slots);
                        if (result.SlotToOverlay != null)
                        {
                            foreach (var kv in result.SlotToOverlay)
                            {
                                if (!aggregate.SlotToOverlay.ContainsKey(kv.Key))
                                {
                                    aggregate.SlotToOverlay.Add(kv.Key, kv.Value);
                                }
                            }
                        }
                        aggregate.IsUDIM = aggregate.IsUDIM || result.IsUDIM;

                        if (result.TempAssetsToDelete != null)
                        {
                            for (int ti = 0; ti < result.TempAssetsToDelete.Count; ti++)
                            {
                                var p = result.TempAssetsToDelete[ti];
                                if (!string.IsNullOrEmpty(p)) deferredDeletes.Add(p);
                            }
                        }

                        // Per-slot wardrobe recipe creation in batch mode
                        if (!isBaseRaceRecipe && createRecipe)
                        {
                            CreateWardrobeRecipesForSlots(result, wardrobeFolderPath);
                        }
                    }

                    processedEntries.Add(e);

                    current++;
                }
                Progress.Report(progressId,1.0f,"Finishing...");
                EditorUtility.DisplayProgressBar("Creating Slots", "Finishing...", 1f);
                Thread.Sleep(0);


                // When building base race recipes in batch mode, keep existing behavior (single base recipe for the whole batch)
                if (isBaseRaceRecipe && createRecipe && aggregate.Slots.Count > 0)
                {
                    Progress.Report(progressId, 1.0f, "Building Base Recipe...");
                    EditorUtility.DisplayProgressBar("Creating Base Recipe", "Building base-race recipe...", 1f);
                    Thread.Sleep(0);

                    CreateRecipeFromResult(aggregate);
                }

                if (addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.ForceSave();
                }
                Progress.Remove(progressId);
                foreach (var path in deferredDeletes)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(path))
                        {
                            AssetDatabase.DeleteAsset(path);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"Could not delete temp asset '{path}': {ex.Message}");
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                Thread.Sleep(0);

                slotName = originalSlotName;
                slotMesh = originalSlotMesh;

                // Clear pending list after processing
                if (_pendingSmrs != null)
                {
                   if (processedEntries.Count == _pendingSmrs.Count)
                    {
                        _pendingSmrs.Clear();
                    }
                    else
                    {
                        for (int i = _pendingSmrs.Count - 1; i >= 0; i--)
                        {
                            var entry = _pendingSmrs[i];
                            if (processedEntries.Contains(entry))
                            {
                                _pendingSmrs.RemoveAt(i);
                            }
                        }
                    }
                }
                Repaint();
            }
        }

        private SlotDataAsset CreateSlot()
        {
            if (slotName == null || slotName == "")
            {
                Debug.LogError("slotName must be specified.");
                return null;
            }

            var result = CreateSlot_Internal_WithResult(false);
            if (result == null || result.Slots == null || result.Slots.Count == 0)
            {
                return null;
            }

            // After building slots (non-UDIM and UDIM cases), optionally create one recipe containing ALL slots
            if (createRecipe)
            {
                CreateRecipeFromResult(result);
            }

            // Return first created slot for backward compatibility
            var first = result.Slots[0];
            UMAUpdateProcessor.UpdateSlot(first);
            return first;
        }

        private UMASlotProcessingUtil.SlotBuildResult CreateSlot_Internal_WithResult(bool batchMode)
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
            sbp.alwaysRecreateSlots = alwaysRecreateSlots;
            sbp.clearNormals = clearNormals;
            sbp.clearTangents = clearTangents;
            sbp.udimTilesU = udimTilesU;
            sbp.udimTilesV = udimTilesV;
            sbp.createOverlays = createOverlay;
            sbp.createRecipe = false; // moved out of util
            sbp.isBaseRaceRecipe = isBaseRaceRecipe;
            sbp.slotMaterialPath = material != null ? AssetDatabase.GetAssetPath(material) : string.Empty;
            sbp.slotFolderPath = slotFolder != null ? AssetDatabase.GetAssetPath(slotFolder) : string.Empty;
            sbp.addToGlobalLibrary = addToGlobalLibrary;
            sbp.batchMode = batchMode;
            sbp.appendTypeToName = appendTypeToName;
            sbp.addUDIMTileNumbers = addUDIMTileNumbers;

            // Slot LOD generation
            sbp.generateSlotLods = generateSlotLods;
            sbp.useUnityLodGenerator = useUnityLodGenerator;
            sbp.slotLodMaxLevels = slotLodMaxLevels;
            sbp.slotLodMinTriangles = slotLodMinTriangles;
            sbp.slotLodTargetReductionPerLevel = slotLodTargetReductionPerLevel;
            sbp.slotLodPreserveBoundaryEdges = slotLodPreserveBoundaryEdges;
            sbp.slotLodBoundaryWeight = slotLodBoundaryWeight;

            /*Debug.Log(string.Format("[SlotBuilder] Build slot='{0}' udimAdjustment={1} alwaysRecreateSlots={2} generateSlotLods={3} maxLevels={4} minTris={5} reduction={6} preserveBorders={7} borderWeight={8}",
                sbp.slotName,
                sbp.udimAdjustment,
                sbp.alwaysRecreateSlots,
                sbp.generateSlotLods,
                sbp.slotLodMaxLevels,
                sbp.slotLodMinTriangles,
                sbp.slotLodTargetReductionPerLevel,
                sbp.slotLodPreserveBoundaryEdges,
                sbp.slotLodBoundaryWeight)); */

            var result = UMASlotProcessingUtil.CreateSlotData(sbp);
            if (result == null)
            {
                Debug.LogError("Failed to create SlotDataAsset(s)");
                return null;
            }

            try
            {
                int totalSlots = result.Slots != null ? result.Slots.Count : 0;
                Debug.Log($"[SlotBuilder][SlotLOD] Result slots={totalSlots} generateSlotLods={generateSlotLods} useUnity={useUnityLodGenerator}");
                if (result.Slots != null)
                {
                    for (int si = 0; si < result.Slots.Count; si++)
                    {
                        var s = result.Slots[si];
                        if (s == null) continue;
                        var md = s.meshData;
                        int lodCount = (md != null && md.submeshes != null && md.submeshes.Length > 0 && md.submeshes[0] != null) ? md.submeshes[0].LODCount() : -1;
                        int triLen = (md != null && md.submeshes != null && md.submeshes.Length > 0 && md.submeshes[0] != null && md.submeshes[0].getManagedTriangles(0) != null)
                            ? md.submeshes[0].getManagedTriangles(0).Length
                            : -1;
                        Debug.Log($"[SlotBuilder][SlotLOD] Slot[{si}] '{s.slotName}' (new slots use submesh0) lodCount={lodCount} triLen={triLen}");

                        if (s != null)
                        {
                            string ap = AssetDatabase.GetAssetPath(s);
                            if (!string.IsNullOrEmpty(ap))
                            {
                                var reloaded = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(ap);
                                if (reloaded != null)
                                {
                                    var rmd = reloaded.meshData;
                                    int rlod = (rmd != null && rmd.submeshes != null && rmd.submeshes.Length > 0 && rmd.submeshes[0] != null) ? rmd.submeshes[0].LODCount() : -1;
                                    int rtriLen = (rmd != null && rmd.submeshes != null && rmd.submeshes.Length > 0 && rmd.submeshes[0] != null && rmd.submeshes[0].getManagedTriangles(0) != null)
                                        ? rmd.submeshes[0].getManagedTriangles(0).Length
                                        : -1;
                                    Debug.Log($"[SlotBuilder][SlotLOD] Reload Slot[{si}] path='{ap}' lodCount={rlod} triLen={rtriLen}");
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            // Apply tags to all created slot assets
            if (result.Slots != null)
            {
                for (int i = 0; i < result.Slots.Count; i++)
                {
                    var sd = result.Slots[i];
                    if (sd == null) continue;
                    sd.tags = Tags.ToArray();
                    EditorUtility.SetDirty(sd);
                }
                AssetDatabase.SaveAssets();
            }
            return result;
        }

        private void CreateRecipeFromResult(UMASlotProcessingUtil.SlotBuildResult result)
        {
            if (result == null || result.Slots == null || result.Slots.Count == 0)
            {
                return;
            }

            // Build UMA recipe that includes ALL slots and their overlays
            var recipe = new UMA.UMAData.UMARecipe();
            recipe.ClearDna();
            int index = 0;
            for (int i = 0; i < result.Slots.Count; i++)
            {
                var sda = result.Slots[i];
                if (sda == null) continue;
                var sd = new SlotData(sda);
                if (result.SlotToOverlay != null && result.SlotToOverlay.TryGetValue(sda, out var oda) && oda != null)
                {
                    var od = new OverlayData(oda);
                    sd.AddOverlay(od);
                }
                recipe.SetSlot(index++, sd);
            }

            string assetDir = AssetDatabase.GetAssetPath(slotFolder);
            string recipeBaseName = GetSlotName(slotMesh);
            if (appendTypeToName)
            {
                recipeBaseName += isBaseRaceRecipe ? "_BaseRaceRecipe" : "_Recipe";
            }
            string recipePath = Path.Combine(assetDir, recipeBaseName + ".asset").Replace('\\','/');
            recipePath = AssetDatabase.GenerateUniqueAssetPath(recipePath);

            if (isBaseRaceRecipe)
            {
                var utr = ScriptableObject.CreateInstance<UMATextRecipe>();
                utr.name = recipeBaseName;
                utr.Save(recipe);
                AssetDatabase.CreateAsset(utr, recipePath);
                if (addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(UMATextRecipe), utr);
                }
            }
            else
            {
                var uwr = ScriptableObject.CreateInstance<UMAWardrobeRecipe>();
                uwr.name = recipeBaseName;
                uwr.Save(recipe);
                AssetDatabase.CreateAsset(uwr, recipePath);
                if (addToGlobalLibrary)
                {
                    UMAAssetIndexer.Instance.EvilAddAsset(typeof(UMAWardrobeRecipe), uwr);
                }
            }
            AssetDatabase.SaveAssets();
        }

        // Restored helpers for drag & drop batch processing
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

                    if (_pendingSmrs == null)
                    {
                        _pendingSmrs = new List<PendingSmrEntry>();
                    }

                    // Rebuild pending list from this drop (default checked)
                    _pendingSmrs.Clear();
                    foreach (var mesh in meshes)
                    {
                        if (mesh == null)
                        {
                            continue;
                        }

                        if (!ShouldProcessMeshByFilters(mesh))
                        {
                            continue;
                        }

                        _pendingSmrs.Add(new PendingSmrEntry { smr = mesh, selected = true });
                    }

                    if (_pendingSmrs.Count == 0)
                    {
                        EditorUtility.DisplayDialog("Slot Builder", "No SkinnedMeshRenderers found in dropped objects.", "OK");
                    }
                    Repaint();
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
                    string temp = assetLocation.Substring(relativeLocation.Length +1); // remove the prefix
                    temp = temp.Substring(0, temp.LastIndexOf('/') +1); // remove the asset name
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
