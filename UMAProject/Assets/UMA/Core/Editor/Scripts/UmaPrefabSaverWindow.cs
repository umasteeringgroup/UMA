using UnityEngine;
using UnityEditor;
using UMA.CharacterSystem;
using System.Collections.Generic;
using System.IO;
using UMA.Examples;
using UMA.PoseTools;
using static UMA.UMAData;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UMA.Editors
{
public class UmaPrefabSaverWindow : EditorWindow
	{
		private const string PrefsKeyPrefix = "UMA.UmaPrefabSaverWindow.";
		private const string PrefsKeyReplaceExisting = PrefsKeyPrefix + "ReplaceExisting";
		private const string PrefsKeyUnswizzleNormalMaps = PrefsKeyPrefix + "UnswizzleNormalMaps";
		private const string PrefsKeyAddStandaloneDNA = PrefsKeyPrefix + "AddStandaloneDNA";
		private const string PrefsKeyExportMode = PrefsKeyPrefix + "ExportMode";
		private const string PrefsKeyCharacterName = PrefsKeyPrefix + "CharacterName";
		private const string PrefsKeyPrefabFolderPath = PrefsKeyPrefix + "PrefabFolderPath";
		private const string PrefsKeyGltfExportSlots = PrefsKeyPrefix + "GltfExportSlots";

		public enum MeshExportMode
		{
			UnityAssetMeshes = 0,
			Gltf = 1,
#if UMA_FBX_EXPORT
			Fbx = 2
#endif
		}

        [Tooltip("The character that you want to convert")]
		public UMAAvatarBase baseObject;
		[Tooltip("If true, will replace the UMA with the generated prefab in the scene")]
        public bool replaceExisting = false;
        [Tooltip("Convert Swizzled normal maps back to standard normal maps")]
		public bool UnswizzleNormalMaps = true;
		[Tooltip("If True, will keep the umaData, and add a Standalone DNA component allowing you to load/save/Deform skeletal DNA")]
		public bool AddStandaloneDNA = true;
        [Tooltip("How meshes should be exported during conversion.")]
        public MeshExportMode ExportMode = MeshExportMode.UnityAssetMeshes;
        [Tooltip("When Mesh Export Mode is glTF, export each slot from SlotDataList instead of combined renderer meshes.")]
		public bool ExportGltfAsSlots = false;
        [Tooltip("The prefab will be named this, and it will be added to all assets saved")]
		public string CharacterName;
		[Tooltip("The folder where the prefab folder will be created")]
		public UnityEngine.Object prefabFolder;
		public string CheckFolder(ref UnityEngine.Object folderObject)
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
				}
				return destpath;
			}
			return null;
		}

		private void LoadSettings()
		{
			replaceExisting = EditorPrefs.GetBool(PrefsKeyReplaceExisting, replaceExisting);
			UnswizzleNormalMaps = EditorPrefs.GetBool(PrefsKeyUnswizzleNormalMaps, UnswizzleNormalMaps);
			AddStandaloneDNA = EditorPrefs.GetBool(PrefsKeyAddStandaloneDNA, AddStandaloneDNA);

			int exportModeValue = EditorPrefs.GetInt(PrefsKeyExportMode, (int)ExportMode);
			if (System.Enum.IsDefined(typeof(MeshExportMode), exportModeValue))
			{
				ExportMode = (MeshExportMode)exportModeValue;
			}

			CharacterName = EditorPrefs.GetString(PrefsKeyCharacterName, CharacterName ?? string.Empty);
			ExportGltfAsSlots = EditorPrefs.GetBool(PrefsKeyGltfExportSlots, ExportGltfAsSlots);

			string folderPath = EditorPrefs.GetString(PrefsKeyPrefabFolderPath, string.Empty);
			if (!string.IsNullOrEmpty(folderPath))
			{
				var loadedFolder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
				if (loadedFolder != null)
				{
					prefabFolder = loadedFolder;
				}
			}
		}

		private void SaveSettings()
		{
			EditorPrefs.SetBool(PrefsKeyReplaceExisting, replaceExisting);
			EditorPrefs.SetBool(PrefsKeyUnswizzleNormalMaps, UnswizzleNormalMaps);
			EditorPrefs.SetBool(PrefsKeyAddStandaloneDNA, AddStandaloneDNA);
			EditorPrefs.SetInt(PrefsKeyExportMode, (int)ExportMode);
			EditorPrefs.SetString(PrefsKeyCharacterName, CharacterName ?? string.Empty);
			EditorPrefs.SetBool(PrefsKeyGltfExportSlots, ExportGltfAsSlots);

			string folderPath = prefabFolder != null ? AssetDatabase.GetAssetPath(prefabFolder) : string.Empty;
			EditorPrefs.SetString(PrefsKeyPrefabFolderPath, folderPath ?? string.Empty);
		}

		private void OnEnable()
		{
			LoadSettings();
		}

void OnGUI()
{
 EditorGUI.BeginChangeCheck();
	EditorGUILayout.LabelField("UMA Prefab Saver", EditorStyles.boldLabel);
	EditorGUILayout.HelpBox("This will convert an UMA avatar into a non-UMA prefab. Once converted, it can be reused with little overhead, but all UMA functionality will be lost.", MessageType.None, false);
	baseObject = (UMAAvatarBase)EditorGUILayout.ObjectField("UMA Avatar", baseObject, typeof(UMAAvatarBase), true);

	EditorGUILayout.HelpBox("If you unswizzle normals (recommended) then they can be used in other applications, and UMA will automatically mark them as normal maps in the import settings.", MessageType.None);
	UnswizzleNormalMaps = EditorGUILayout.Toggle("Unswizzle Normals", UnswizzleNormalMaps);

	EditorGUILayout.HelpBox("Adding Standalone DNA will allow you to adjust most DNA of the character, without it being an UMA. However, it will require that you have the UMA system in the project.", MessageType.None);
	AddStandaloneDNA = EditorGUILayout.Toggle("Add Standalone DNA", AddStandaloneDNA);

	ExportMode = (MeshExportMode)EditorGUILayout.EnumPopup("Mesh Export Mode", ExportMode);
	switch (ExportMode)
	{
		case MeshExportMode.Gltf:
          ExportGltfAsSlots = EditorGUILayout.Toggle("glTF Export Slots", ExportGltfAsSlots);
			EditorGUILayout.HelpBox("Exports a .gltf + .bin package in the UMA default rest pose (A-pose where the race rig is authored that way). The generated prefab still uses saved .asset meshes because Unity does not include a built-in glTF model importer.", MessageType.None);
			break;

		#if UMA_FBX_EXPORT
		case MeshExportMode.Fbx:
			EditorGUILayout.HelpBox("Meshes will be exported as FBX, enabling Mesh LODs and compression via Model Import Settings. Requires the FBX Exporter package.", MessageType.None);
			break;
		#endif

		default:
			EditorGUILayout.HelpBox("Meshes will be saved as .asset files with read/write disabled.", MessageType.None);
			break;
	}

	replaceExisting = EditorGUILayout.Toggle("Replace Existing UMA", replaceExisting);
	if (replaceExisting)
	{
		EditorGUILayout.HelpBox("If you replace the existing UMA, it will be removed from the scene. If you do not replace it, you will need to manually add the prefab to the scene.", MessageType.None);
	}
	else
	{
		EditorGUILayout.HelpBox("If you do not replace the existing UMA, you will need to manually add the prefab to the scene.", MessageType.None);
	}

	CharacterName = EditorGUILayout.TextField("Prefab Name", CharacterName);
	prefabFolder = EditorGUILayout.ObjectField("Prefab Base Folder", prefabFolder, typeof(UnityEngine.Object), false) as UnityEngine.Object;

	string folder = CheckFolder(ref prefabFolder);

	if (prefabFolder != null && baseObject != null && !string.IsNullOrEmpty(CharacterName))
	{
		if (GUILayout.Button("Make Prefab") && prefabFolder != null)
		{
			bool exportAsFbx = false;
			#if UMA_FBX_EXPORT
			exportAsFbx = ExportMode == MeshExportMode.Fbx;
			#endif

			bool exportAsGltf = ExportMode == MeshExportMode.Gltf;

			UMAAvatarLoadSaveMenuItems.ConvertToNonUMA(
				baseObject.gameObject,
				baseObject,
				folder,
				UnswizzleNormalMaps,
				CharacterName,
				AddStandaloneDNA,
				replaceExisting,
				exportAsFbx,
              exportAsGltf,
				ExportGltfAsSlots);

			EditorUtility.DisplayDialog("UMA Prefab Saver", "Conversion complete", "OK");
		}
	}
	else
	{
		if (baseObject == null)
		{
			EditorGUILayout.HelpBox("A valid character with DynamicCharacterAvatar or DynamicAvatar must be supplied", MessageType.Error);
		}
		if (string.IsNullOrEmpty(CharacterName))
		{
			EditorGUILayout.HelpBox("Prefab Name cannot be empty", MessageType.Error);
		}
		if (prefabFolder == null)
		{
			EditorGUILayout.HelpBox("A valid base folder must be supplied", MessageType.Error);
		}
	}

	if (EditorGUI.EndChangeCheck())
	{
		SaveSettings();
	}
}

		[MenuItem("UMA/Prefab Maker", priority = 20)]
		public static void OpenUmaPrefabWindow()
		{
			UmaPrefabSaverWindow window = (UmaPrefabSaverWindow)EditorWindow.GetWindow(typeof(UmaPrefabSaverWindow));
			window.titleContent.text = "UMA Prefab Maker";
           window.LoadSettings();
		}
	}
}
