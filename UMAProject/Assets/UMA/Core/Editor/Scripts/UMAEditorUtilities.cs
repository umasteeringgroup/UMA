using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;
using UMA.CharacterSystem;
using UnityEditor.Animations;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Build;

namespace UMA
{
    [InitializeOnLoad]
    public static class UMAEditorUtilities
    {
        public static Dictionary<Type, string> FriendlyNames = new Dictionary<Type, string>();
        private static Texture icon;
        private static Texture missingIndexIcon;
		private static bool ranOnce = false;
        private static bool showIndexedTypes = false;
        private static bool showUnindexedTypes = true;
		public  static string umaDefaultLabel = "UMA_Default";
        public const string umaDefaultTags = "Head,Hair,Torso,Legs,Feet,Hands,Smooshable,Unsmooshable";


		private const string umaDefaultLabelKey = "UMA_DEFAULTLABEL";
		private const string umaHotkeyWord = "UMA_HOTKEYS";


        static UMAEditorUtilities()
        {
            UMASettings.ProjectWindowTypeDisplayChanged -= RefreshProjectWindowTypeDisplay;
            UMASettings.ProjectWindowTypeDisplayChanged += RefreshProjectWindowTypeDisplay;
            EditorApplication.update -= RunCallbacks;
			EditorApplication.update += RunCallbacks;
		}

		private static void RunCallbacks()
		{
			if (ranOnce)
			{
                EditorApplication.update -= RunCallbacks;
                return;
			}

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            FriendlyNames = new Dictionary<Type, string>
            {
                { typeof(SlotDataAsset), "Slot" },
                { typeof(OverlayDataAsset), "Overlay" },
                { typeof(RaceData), "Race" },
                { typeof(UMATextRecipe), "Text Recipe" },
                { typeof(UMAWardrobeRecipe), "Wardrobe Recipe" },
                { typeof(UMAWardrobeCollection), "Wardrobe Collection" },
                { typeof(AnimatorController), "Animator Controller" },
                { typeof(TextAsset), "Text" },
                { typeof(DynamicUMADnaAsset), "Dynamic DNA" }
            };

            string[] iconTextures = AssetDatabase.FindAssets("t:texture UmaIndex");
            if (iconTextures != null && iconTextures.Length > 0)
            {
                icon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    AssetDatabase.GUIDToAssetPath(iconTextures[0]));
            }
            else if (Debug.isDebugBuild)
            {
                Debug.LogWarning("Unable to load UMA index texture icon.");
            }

            missingIndexIcon =
                EditorGUIUtility.IconContent("console.erroricon.sml").image;
            if (missingIndexIcon == null)
            {
                missingIndexIcon =
                    EditorGUIUtility.IconContent("console.erroricon").image;
            }

            ranOnce = true;
            EditorApplication.update -= RunCallbacks;
            RefreshProjectWindowTypeDisplay();
		}

        private static void RefreshProjectWindowTypeDisplay()
        {
            showIndexedTypes = UMASettings.ShowIndexedTypes;
            showUnindexedTypes = UMASettings.ShowUnindexedTypes;

            // Remove first so initialization and settings changes can never register
            // the same Project window callback more than once.
            EditorApplication.projectWindowItemOnGUI -= DrawItems;
            if (showIndexedTypes || showUnindexedTypes)
            {
                EditorApplication.projectWindowItemOnGUI += DrawItems;
            }

            EditorApplication.RepaintProjectWindow();
        }

        public static string FindUMAFolder()
        {
            return UMAPathUtility.InstallAssetRoot;
        }

        public static string FindUMAFullPath()
        {
            return UMAPathUtility.InstallAssetRoot;
        }
        public static NamedBuildTarget CurrentNamedBuildTarget
        {
            get
            {
#if UNITY_SERVER
                    return NamedBuildTarget.Server;
#else
                BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
                BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
                NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
                return namedBuildTarget;
#endif
            }
        }

        public static string[] GetDefaultTags()
        {
			var settings = UMASettings.GetOrCreateSettings();
			return settings.tagLookupValues;
        }

		public static string[] GetDefaultBaseTags()
		{
			string[] strings = GetDefaultTags();
			string[] baseTags = new string[strings.Length];
			// trim everything past the last slash
			for (int i = 0; i < strings.Length; i++)
			{
				string[] split = strings[i].Split('/');
				if (split.Length > 1)
				{
					baseTags[i] = split[split.Length-1];
                }
                else
				{
					baseTags[i] = strings[i];
                }
            }
			return baseTags;
		}

		public static string GetDefaultAddressableLabel()
		{
			return UMASettings.AddrDefaultLabel;
		}

		public static bool LeanMeanSceneFiles()
		{
			return UMASettings.CleanRegenOnSave;
		}

		public static bool UseSharedGroupConfigured()
		{
			return UMASettings.AddrUseSharedGroup;
        }

		public static bool StripUMAMaterials()
        {
			return UMASettings.AddrStripMaterials;
        }

        public static bool StripUVAttachedShaders()
        {
            return UMASettings.AddrStripUVAttachedShaders;
        }
        public static bool StripTextures()
        {
            return UMASettings.AddrStripTextures;
        }
        public static bool PostProcessAllAssets()
		{
			return UMASettings.PostProcessAllAssets;
        }

		public static bool IsAddressable()
		{
#if UMA_ALWAYSADDRESSABLE
            return true;
#else
			return UMASettings.UseAddressables;
#endif
		}

		public static bool IsAutoRepairIndex()
		{
			return UMASettings.AutoRepairIndex;
        }

		public static bool IsAsmdef(HashSet<string> defineSymbols, string Symbol)
        {
			return (defineSymbols.Contains(Symbol));
		}


        private static void DrawItems(string guid, Rect selectionRect)
        {
            if (String.IsNullOrEmpty(guid))
            {
                return;
            }

            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            if (indexer == null)
            {
                return;
            }

            AssetItem ai = indexer.FromGuid(guid);
            if (ai != null)
            {
                if (showIndexedTypes)
                {
                    ShowAsset(
                        selectionRect,
                        GetFriendlyTypeName(ai._Type),
                        icon);
                }
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            UnityEngine.Object asset =
                AssetDatabase.LoadMainAssetAtPath(path);
            if (asset == null)
            {
                return;
            }

            Type assetType = asset.GetType();
            if (!indexer.IsIndexedType(assetType))
            {
                return;
            }

            // GuidTypes is a derived cache and can temporarily be empty or stale
            // even while the authoritative type dictionary contains the item.
            // Resolve by the index key as a fallback, but verify the candidate is
            // this exact project asset so duplicate UMA names are not mislabeled.
            AssetItem indexedItem =
                indexer.GetAssetItemForObject(asset);
            if (IsSameIndexedAsset(indexedItem, asset, guid, path))
            {
                if (!indexer.GuidTypes.ContainsKey(guid))
                {
                    indexer.GuidTypes[guid] = indexedItem;
                }

                if (showIndexedTypes)
                {
                    ShowAsset(
                        selectionRect,
                        GetFriendlyTypeName(assetType),
                        icon);
                }
                return;
            }

            if (showUnindexedTypes)
            {
                ShowAsset(
                    selectionRect,
                    GetFriendlyTypeName(assetType),
                    missingIndexIcon);
            }
        }

        private static bool IsSameIndexedAsset(
            AssetItem indexedItem,
            UnityEngine.Object asset,
            string guid,
            string path)
        {
            if (indexedItem == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(indexedItem._Guid) &&
                string.Equals(
                    indexedItem._Guid,
                    guid,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrEmpty(indexedItem._Path) &&
                string.Equals(
                    indexedItem._Path.Replace('\\', '/'),
                    path.Replace('\\', '/'),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return indexedItem._SerializedItem == asset;
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null)
            {
                return "Unknown";
            }

            string friendlyName;
            return FriendlyNames.TryGetValue(type, out friendlyName)
                ? friendlyName
                : ObjectNames.NicifyVariableName(type.Name);
        }

        private static void ShowAsset(
            Rect selectionRect,
            string FriendlyType,
            Texture statusIcon)
        {
            if (selectionRect.width <= 0f || selectionRect.height <= 0f)
            {
                return;
            }

            const float iconSize = 16f;
            const float edgePadding = 2f;
            GUIStyle labelStyle = EditorStyles.miniLabel;
            GUIContent labelContent =
                new GUIContent(FriendlyType, FriendlyType);
            float desiredLabelWidth =
                labelStyle.CalcSize(labelContent).x + 2f;
            float availableWidth = Mathf.Max(
                iconSize,
                selectionRect.width - edgePadding * 2f);
            float badgeWidth = Mathf.Min(
                desiredLabelWidth + iconSize,
                availableWidth);
            float badgeHeight = Mathf.Min(
                iconSize,
                selectionRect.height);

            // Right-align in list view and pin to the upper-right in icon/grid
            // view. The old height/width gate caused the overlay to disappear
            // entirely when users resized the Project window or changed views.
            Rect badgeRect = new Rect(
                selectionRect.xMax - badgeWidth - edgePadding,
                selectionRect.y + (selectionRect.height <= 22f
                    ? Mathf.Max(0f, (selectionRect.height - badgeHeight) * 0.5f)
                    : edgePadding),
                badgeWidth,
                badgeHeight);

            float labelWidth = Mathf.Max(
                0f,
                badgeRect.width - iconSize);
            if (labelWidth > 0f)
            {
                Rect labelRect = new Rect(
                    badgeRect.x,
                    badgeRect.y,
                    labelWidth,
                    badgeRect.height);
                Color background = EditorGUIUtility.isProSkin
                    ? (Color)new Color32(56, 56, 56, 230)
                    : (Color)new Color32(194, 194, 194, 230);
                EditorGUI.DrawRect(labelRect, background);
                GUI.Label(labelRect, labelContent, labelStyle);
            }

            if (statusIcon != null)
            {
                Rect iconRect = new Rect(
                    badgeRect.xMax - iconSize,
                    badgeRect.y,
                    iconSize,
                    badgeRect.height);
                GUI.DrawTexture(
                    iconRect,
                    statusIcon,
                    ScaleMode.ScaleToFit,
                    true);
            }
        }

#if UNITY_2018_4_OR_NEWER || UNITY_2019_1_OR_NEWER
		public static void EnableAsmdef()
		{
			RenameFiles(".asmdefTemp", ".asmdef", "Asmdef files are in place.", "Unable to find asmdefTemp files. Have you already ran this?");
		}

		public static void DisableAsmDef()
		{
			RenameFiles(".asmdef", ".asmdefTemp", "Asmdef files are removed.", "Unable to find asmdef files. Have you already ran this?");
		}

		public static void RenameFiles(string oldpattern,string newpattern, string completeMessage, string notFoundMessage)
		{
			if (UMAPathUtility.IsPackageInstallation)
			{
				EditorUtility.DisplayDialog(
					"UMA package is read-only",
					"Assembly definitions cannot be enabled or disabled inside an installed package.",
					"OK");
				return;
			}

			string assetPath = UMAPathUtility.ResolveAbsolutePath(UMAPathUtility.InstallAssetRoot);
			string[] files = Directory.GetFiles(assetPath, "*"+oldpattern, SearchOption.AllDirectories);

			if (files.Length == 0)
			{
				EditorUtility.DisplayDialog("Warning", notFoundMessage , "Guess so");
				return;
			}
			foreach (string s in files)
			{
				string newFile = s.Replace(oldpattern, newpattern);
				if (newFile == s)
                {
					// 
					newFile = s.ToLower().Replace(oldpattern.ToLower(), newpattern.ToLower());
                }
				File.Move(s, newFile);
			}
			AssetDatabase.Refresh();
			EditorUtility.DisplayDialog("Complete",completeMessage , "OK");
		}
#endif

#if UMA_HOTKEYS
		[MenuItem("UMA/Project Setup/Toggle Hotkeys (enabled)",priority =130)]
#else
		[MenuItem("UMA/Project Setup/Toggle Hotkeys (disabled)", priority = 130)]
#endif
		public static void ToggleUMAHotkeys()
		{
			string definesString = PlayerSettings.GetScriptingDefineSymbols ( CurrentNamedBuildTarget );
            List<string> allDefines = new List<string>();
            allDefines.AddRange(definesString.Split(';'));

			if (allDefines.Contains(umaHotkeyWord))
            {
                allDefines.Remove(umaHotkeyWord);
            }
            else
            {
                allDefines.Add(umaHotkeyWord);
            }
			PlayerSettings.SetScriptingDefineSymbols(CurrentNamedBuildTarget, string.Join(";", allDefines.ToArray()));
            //PlayerSettings.SetScriptingDefineSymbolsForGroup(EditorUserBuildSettings.selectedBuildTargetGroup, string.Join( ";", allDefines.ToArray()));
		}
      
        // Rotation from Blender Z-up to Unity Y-up
        private static readonly Quaternion BlenderToUnityRotation = Quaternion.Euler(90, 0, 0);

        public static void ConvertSkinnedMesh(SkinnedMeshRenderer smr)
        {
            if (smr == null || smr.sharedMesh == null)
            {
                Debug.LogWarning("SkinnedMeshRenderer or Mesh is null.");
                return;
            }

            Mesh mesh = UnityEngine.Object.Instantiate(smr.sharedMesh); // Clone to avoid modifying original
            ConvertMeshGeometry(mesh);
            ConvertBindposes(mesh);
            smr.sharedMesh = mesh;

            // Convert bone transforms
            ConvertSkeletonRoot(smr.rootBone);
        }

        /// <summary>
        /// Converts a UMAMeshData from Blender Z-up to Unity Y-up space (same logic as ConvertSkinnedMesh but in-place on UMAMeshData).
        /// Geometry (vertices, normals, tangents), bindposes, and optionally bone/local transforms are rotated.
        /// </summary>
        /// <param name="meshData">Source UMA mesh data.</param>
        /// <param name="adjustBindposes">Multiply bindposes by inverse rotation (keeps skinning aligned after vertex rotation).</param>
        /// <param name="convertBones">
        /// If true, rotate UMA bone local transforms. Typically leave false to avoid double-compensating animation data.
        /// </param>
        /// <param name="rotateRootBoneOnly">
        /// When converting bones, rotate only the root (recommended). Set false to rotate all UMA bones.
        /// </param>
        /// <param name="mirrorHandednessAdjust">
        /// If true, flips tangent w to compensate handedness change (needed for normal maps).
        /// </param>
        public static void ConvertMeshData(
            UMAMeshData meshData,
            bool adjustBindposes = true,
            bool convertBones = false,
            bool rotateRootBoneOnly = true,
            bool mirrorHandednessAdjust = true)
        {
            if (UMAMeshData.IsNullOrEmptyMeshData(meshData))
            {
                Debug.LogWarning("ConvertMeshData: meshData is null.");
                return;
            }
            if (meshData.vertices == null || meshData.vertexCount == 0)
            {
                Debug.LogWarning($"ConvertMeshData: meshData has no vertices ({meshData.vertexCount}).");
                return;
            }

            // Use same rotation as geometry conversion (-90� about X).
            Quaternion rot = BlenderToUnityRot;
            Matrix4x4 rotM = Matrix4x4.Rotate(rot);
            Matrix4x4 invRotM = rotM.inverse;

            // 1. Geometry
            var verts = meshData.vertices;
            var norms = meshData.normals;
            var tangs = meshData.tangents;

            for (int i = 0; i < meshData.vertexCount; i++)
            {
                verts[i] = rot * verts[i];
                if (norms != null && i < norms.Length)
                {
                    norms[i] = (rot * norms[i]).normalized;
                }
                if (tangs != null && i < tangs.Length)
                {
                    Vector3 dir = new Vector3(tangs[i].x, tangs[i].y, tangs[i].z);
                    dir = (rot * dir).normalized;
                    float w = tangs[i].w;
                    if (mirrorHandednessAdjust) w = -w;
                    tangs[i] = new Vector4(dir.x, dir.y, dir.z, w);
                }
            }

            meshData.verticesModified = true;
            if (norms != null) meshData.normalsModified = true;
            if (tangs != null) meshData.tangentsModified = true;

            // 2. Bindposes
            if (adjustBindposes && meshData.bindPoses != null && meshData.bindPoses.Length > 0)
            {
                var binds = meshData.bindPoses;
                for (int i = 0; i < binds.Length; i++)
                {
                    // Same approach as ConvertBindposes: rotate mesh space, compensate by inverse.
                    binds[i] = binds[i] * invRotM;
                }
            }

            // 3. Bone transforms (optional)
            // Inside ConvertMeshData, replace the bone rotation block with:
            // 3. Bone transforms (optional; enforce root-only when geometry adjusted)
            if (convertBones && meshData.umaBones != null && meshData.umaBones.Length > 0)
            {
                int rootHash = meshData.rootBoneHash;
                for (int i = 0; i < meshData.umaBones.Length; i++)
                {
                    var bt = meshData.umaBones[i];
                    if (bt == null) continue;
                    // Always restrict to root to avoid double application
                    if (bt.hash != rootHash) continue;

                    bt.position = rot * bt.position;
                    bt.rotation = rot * bt.rotation;
                }

                if (meshData.rootBone != null)
                {
                    meshData.rootBone.localPosition = rot * meshData.rootBone.localPosition;
                    meshData.rootBone.localRotation = rot * meshData.rootBone.localRotation;
                }
            }
        }

        private static readonly Quaternion BlenderToUnityRot = Quaternion.Euler(-90f, 0f, 0f);

        private static void ConvertMeshGeometry(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var tangents = mesh.tangents;

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = BlenderToUnityRot * vertices[i];
            }

            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] = (BlenderToUnityRot * normals[i]).normalized;
            }

            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3 dir = new Vector3(tangents[i].x, tangents[i].y, tangents[i].z);
                dir = BlenderToUnityRot * dir;
                tangents[i] = new Vector4(dir.x, dir.y, dir.z, -tangents[i].w); // w inverted because (x,z,-y) changes handedness
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.RecalculateBounds();
        }

        private static void ConvertBindposes(Mesh mesh)
        {
            var bindposes = mesh.bindposes;
            Matrix4x4 rot = Matrix4x4.Rotate(BlenderToUnityRot);
            Matrix4x4 invRot = rot.inverse;

            for (int i = 0; i < bindposes.Length; i++)
            {
                // Rotate mesh space; adjust bindpose accordingly
                bindposes[i] = bindposes[i] * invRot;
            }
            mesh.bindposes = bindposes;
        }

        private static void ConvertSkeletonRoot(Transform root)
        {
            if (root == null) return;
            root.localRotation = BlenderToUnityRot * root.localRotation;
            root.localPosition = BlenderToUnityRot * root.localPosition;
        }

        /// <summary>
        /// Create a Wardrobe Recipe from the slot (and optionally overlay)
        /// </summary>
        /// <param name="path"></param>
        /// <param name="sd"></param>
        /// <param name="od"></param>
        /// <param name="slotName"></param>
        /// <param name="addToGlobalLibrary"></param>
        public static UMAWardrobeRecipe CreateRecipe(string path, SlotDataAsset sd, OverlayDataAsset od, string slotName, bool addToGlobalLibrary)
		{
			// Generate an asset in memory
			UMAWardrobeRecipe asset = ScriptableObject.CreateInstance<CharacterSystem.UMAWardrobeRecipe>();
			UMAData.UMARecipe recipe = new UMAData.UMARecipe();
			recipe.ClearDna();
			SlotData mySlot = new SlotData(sd);
			if (od != null)
			{
				OverlayData myOverlay = new OverlayData(od);
				mySlot.AddOverlay(myOverlay);
			}
			recipe.SetSlot(0, mySlot);
			asset.Save(recipe);
			asset.DisplayValue = slotName;

			// Write the asset to disk
			AssetDatabase.CreateAsset(asset, path);
			AssetDatabase.SaveAssets();
			if (addToGlobalLibrary)
			{
				// Add it to the global libary
				UMAAssetIndexer.Instance.EvilAddAsset(typeof(CharacterSystem.UMAWardrobeRecipe), asset);
				EditorUtility.SetDirty(UMAAssetIndexer.Instance);
			}
			// Inform the asset database a file has changes
			AssetDatabase.Refresh();
			return asset;
		}

		[MenuItem("UMA/Content Creation/Wardrobe/Create Wardrobe Recipe from selected slot and overlay")]
		public static void SaveAsRecipe()
		{
			SlotDataAsset sd = null;
			OverlayDataAsset od = null;

			foreach (UnityEngine.Object obj in Selection.objects)
			{
				// Make sure it's in the project, not the hierarchy.
				// Not sure how we would ever have Slots and Overlays in the hierarchy though.
				if (AssetDatabase.Contains(obj))
				{
					if (obj is SlotDataAsset)
					{
						sd = obj as SlotDataAsset;
					}
					if (obj is OverlayDataAsset)
					{
						od = obj as OverlayDataAsset;
					}
				}
			}

			if (sd == null)
			{
				EditorUtility.DisplayDialog("Notice", "A SlotDataAsset must be selected in the project view", "Got it");
				return;
			}

			string assetPath = AssetDatabase.GetAssetPath(sd.GetEntityId());
			string path = Path.GetDirectoryName(assetPath);
			string AssetName = Path.GetFileNameWithoutExtension(assetPath);
			if (AssetName.ToLower().Contains("_slot"))
			{
				AssetName = Regex.Replace(AssetName, "_slot", "_Recipe", RegexOptions.IgnoreCase);
			}
			else
			{
				AssetName += "_Recipe";
			}
			assetPath = Path.Combine(path, AssetName + ".asset");

			bool doCreate = false;
			if (File.Exists(assetPath))
			{
				if (EditorUtility.DisplayDialog("File Already Exists!", "An asset at that location already exists! Overwrite it?", "Yes", "Cancel"))
                {
                    doCreate = true;
                }
            }
			else
            {
                doCreate = true;
            }

            if (doCreate)
			{
				CreateRecipe(assetPath, sd, od, sd.name, true);
				Debug.Log("Recipe created at: " + assetPath);
			}
		}
	}

	public static class UMAExtensions
    {
		public static void Fill(this bool[] array, bool value, int count = 0, int threshold = 32)
		{
			if (threshold <= 0)
            {
                throw new ArgumentException("threshold");
            }

            if (count == 0)
            {
                count = array.Length;
            }

            int current_size = 0, keep_looping_up_to = Math.Min(count, threshold);

			while (current_size < keep_looping_up_to)
            {
                array[current_size++] = value;
            }

            for (int at_least_half = (count + 1) >> 1; current_size < at_least_half; current_size <<= 1)
            {
                Array.Copy(array, 0, array, current_size, current_size);
            }

            Array.Copy(array, 0, array, current_size, count - current_size);
		}
		public static System.Type[] GetAllDerivedTypes(this System.AppDomain aAppDomain, System.Type aType)
        {
            var result = new List<System.Type>();
            var assemblies = aAppDomain.GetAssemblies();
			
            foreach (var assembly in assemblies)
            {
				if (assembly.IsDynamic) { continue; }

                var types = assembly.GetExportedTypes();
                foreach (var type in types)
                {
                    if (type.IsSubclassOf(aType))
                    {
                        result.Add(type);
                    }
                }
            }
            return result.ToArray();
        }

        public static Rect GetEditorMainWindowPos()
        {
            Resolution r = Screen.currentResolution;
            return new Rect(0, 0, r.width, r.height);
        }

        public static void CenterOnMainWin(this UnityEditor.EditorWindow aWin)
        {
            var main = GetEditorMainWindowPos();
            var pos = aWin.position;
            float w = (main.width - pos.width) * 0.5f;
            float h = (main.height - pos.height) * 0.5f;
            pos.x = main.x + w;
            pos.y = main.y + h;
            aWin.position = pos;
        }
    }
}
