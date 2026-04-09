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
internal class UmaExamineOverlaysWindow : EditorWindow
	{
       private const string OverlayPrefsPrefix = "UMA.ExamineOverlays.";
		private const string OverlayPrefsUtilitiesFoldout = OverlayPrefsPrefix + "UtilitiesFoldout";
		private const string OverlayPrefsRelinkFoldout = OverlayPrefsPrefix + "RelinkFoldout";
		private const string OverlayPrefsUpdateFolderFoldout = OverlayPrefsPrefix + "UpdateFolderFoldout";
		private const string OverlayPrefsUtilitiesMaterialPath = OverlayPrefsPrefix + "UtilitiesMaterialPath";
		private const string OverlayPrefsTextureFolderPath = OverlayPrefsPrefix + "TextureFolderPath";
		private const string OverlayPrefsIncludeSubfolders = OverlayPrefsPrefix + "IncludeSubfolders";
		private const string OverlayPrefsSkipWhenSameAsset = OverlayPrefsPrefix + "SkipWhenSameAsset";
		private const string OverlayPrefsOverlayFilter = OverlayPrefsPrefix + "OverlayFilter";
		private const string OverlayPrefsUpdateFolderPath = OverlayPrefsPrefix + "UpdateFolderPath";

		private readonly List<UMA.OverlayDataAsset> _overlays = new List<UMA.OverlayDataAsset>();
		private readonly List<UMA.OverlayDataAsset> _filteredOverlays = new List<UMA.OverlayDataAsset>();
		private UMA.OverlayDataAsset _selectedOverlay;
		private Vector2 _leftScroll;
		private Vector2 _rightScroll;
     private DefaultAsset _textureFolder;
		private string _textureFolderPath;
		private bool _includeSubfolders;
		private bool _skipWhenSameAsset = true;
     private UMAMaterial _utilitiesTargetMaterial;
     private bool _utilitiesFoldout = true;
		private bool _relinkFoldout = true;
		private bool _updateFolderFoldout = true;
		private string _updateFolderPath = string.Empty;
		private static readonly GUIContent _completeLabel = new GUIContent("Complete");
		private static readonly GUIContent _missingTexturesLabel = new GUIContent("missing textures");
		private static readonly GUIContent _missingTexturesAndOvlLabel = new GUIContent("missing textures and UMAT");
		private static readonly GUIContent _missingOvlLabel = new GUIContent("missing UMAMaterial");
		private enum OverlayFilter { All, Complete, Incomplete }
		private OverlayFilter _filter = OverlayFilter.All;

		public static void Open(List<UMA.OverlayDataAsset> overlays)
		{
			var window = GetWindow<UmaExamineOverlaysWindow>(false, "Examine Overlays", true);
			window.minSize = new Vector2(860f, 420f);
           window.LoadPreferences();
			window._overlays.Clear();
			if (overlays != null)
			{
				window._overlays.AddRange(overlays);
			}
			window.SortOverlays();
			window._selectedOverlay = window._overlays.Count > 0 ? window._overlays[0] : null;
			window.Show();
			window.Focus();
		}

		private void OnEnable()
		{
			LoadPreferences();
		}

		private void OnDisable()
		{
			SavePreferences();
		}

		private void LoadPreferences()
		{
			_utilitiesFoldout = EditorPrefs.GetBool(OverlayPrefsUtilitiesFoldout, true);
			_relinkFoldout = EditorPrefs.GetBool(OverlayPrefsRelinkFoldout, true);
			_updateFolderFoldout = EditorPrefs.GetBool(OverlayPrefsUpdateFolderFoldout, true);
			_includeSubfolders = EditorPrefs.GetBool(OverlayPrefsIncludeSubfolders, false);
			_skipWhenSameAsset = EditorPrefs.GetBool(OverlayPrefsSkipWhenSameAsset, true);
			_filter = (OverlayFilter)EditorPrefs.GetInt(OverlayPrefsOverlayFilter, (int)OverlayFilter.All);
			_textureFolderPath = EditorPrefs.GetString(OverlayPrefsTextureFolderPath, string.Empty);
			_updateFolderPath = EditorPrefs.GetString(OverlayPrefsUpdateFolderPath, string.Empty);

			string materialPath = EditorPrefs.GetString(OverlayPrefsUtilitiesMaterialPath, string.Empty);
			_utilitiesTargetMaterial = !string.IsNullOrEmpty(materialPath)
				? AssetDatabase.LoadAssetAtPath<UMAMaterial>(materialPath)
				: null;

			_textureFolder = AssetDatabase.IsValidFolder(_textureFolderPath)
				? AssetDatabase.LoadAssetAtPath<DefaultAsset>(_textureFolderPath)
				: null;

			_updateFolderPath = NormalizeAssetFolderPath(_updateFolderPath);
		}

		private void SavePreferences()
		{
			EditorPrefs.SetBool(OverlayPrefsUtilitiesFoldout, _utilitiesFoldout);
			EditorPrefs.SetBool(OverlayPrefsRelinkFoldout, _relinkFoldout);
			EditorPrefs.SetBool(OverlayPrefsUpdateFolderFoldout, _updateFolderFoldout);
			EditorPrefs.SetBool(OverlayPrefsIncludeSubfolders, _includeSubfolders);
			EditorPrefs.SetBool(OverlayPrefsSkipWhenSameAsset, _skipWhenSameAsset);
			EditorPrefs.SetInt(OverlayPrefsOverlayFilter, (int)_filter);
			EditorPrefs.SetString(OverlayPrefsTextureFolderPath, _textureFolderPath ?? string.Empty);
			EditorPrefs.SetString(OverlayPrefsUpdateFolderPath, _updateFolderPath ?? string.Empty);
			EditorPrefs.SetString(OverlayPrefsUtilitiesMaterialPath, _utilitiesTargetMaterial != null ? AssetDatabase.GetAssetPath(_utilitiesTargetMaterial) : string.Empty);
		}

		private void RefreshFromSelection()
		{
			var selected = Selection.GetFiltered(typeof(UMA.OverlayDataAsset), SelectionMode.Assets);
			_overlays.Clear();
			for (int i = 0; i < selected.Length; i++)
			{
				var o = selected[i] as UMA.OverlayDataAsset;
				if (o != null)
				{
					_overlays.Add(o);
				}
			}
			SortOverlays();
			if (_selectedOverlay != null && !_overlays.Contains(_selectedOverlay))
			{
				_selectedOverlay = null;
			}
			RebuildFilteredOverlays(_selectedOverlay);
			Repaint();
		}

		private void OnSelectionChange()
		{
			// Intentionally no-op: we only refresh the window contents when the user presses Refresh.
		}

		private void SortOverlays()
		{
			_overlays.Sort((a, b) => string.Compare(a != null ? a.name : "", b != null ? b.name : "", System.StringComparison.OrdinalIgnoreCase));
		}

		private void RebuildFilteredOverlays(UMA.OverlayDataAsset keepSelected)
		{
			_filteredOverlays.Clear();
			for (int i = 0; i < _overlays.Count; i++)
			{
				var overlay = _overlays[i];
				if (overlay == null)
				{
					continue;
				}
              bool isComplete = GetOverlayStatus(overlay) == OverlayStatus.Complete;
				switch (_filter)
				{
					case OverlayFilter.Complete:
						if (!isComplete) continue;
						break;
					case OverlayFilter.Incomplete:
						if (isComplete) continue;
						break;
				}
				_filteredOverlays.Add(overlay);
			}

			if (_filteredOverlays.Count == 0)
			{
				_selectedOverlay = null;
				return;
			}

			if (keepSelected != null)
			{
				if (_filteredOverlays.Contains(keepSelected))
				{
					_selectedOverlay = keepSelected;
					return;
				}
			}
			_selectedOverlay = _filteredOverlays[0];
		}

		private void OnGUI()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Label("Examine Overlays", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
			{
				RefreshFromSelection();
			}
			EditorGUILayout.EndHorizontal();

			if (_overlays.Count == 0)
			{
				EditorGUILayout.HelpBox("Select one or more OverlayDataAsset assets in the Project window.", MessageType.Info);
				return;
			}

			RebuildFilteredOverlays(_selectedOverlay);

            DrawUtilitiesPanel();
			DrawRelinkPanel();
			DrawUpdateFolderPanel();

			EditorGUILayout.BeginHorizontal();
			DrawOverlayList();
			GUILayout.Space(10);
			DrawOverlayDetails();
			EditorGUILayout.EndHorizontal();
		}

		private void DrawUtilitiesPanel()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
          _utilitiesFoldout = EditorGUILayout.Foldout(_utilitiesFoldout, "Utilities", true);
			if (_utilitiesFoldout)
			{
             EditorGUILayout.BeginHorizontal();
				EditorGUI.BeginChangeCheck();
				_utilitiesTargetMaterial = (UMAMaterial)EditorGUILayout.ObjectField("UMAMaterial", _utilitiesTargetMaterial, typeof(UMAMaterial), false);
				if (EditorGUI.EndChangeCheck())
				{
                 SavePreferences();
				}
                using (new EditorGUI.DisabledScope(_utilitiesTargetMaterial == null || _overlays.Count == 0))
				{
                 if (GUILayout.Button("Assign UMAMaterial to selected", GUILayout.Width(220), GUILayout.Height(22)))
					{
						AssignMaterialToSelectedOverlays();
					}
					if (GUILayout.Button("Assign UMAMaterial to ALL", GUILayout.Width(200), GUILayout.Height(22)))
					{
						AssignMaterialToAllOverlaysInList();
					}
				}
             EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(6);
		}

		private void AssignMaterialToSelectedOverlays()
		{
			if (_utilitiesTargetMaterial == null)
			{
				EditorUtility.DisplayDialog("Assign UMAMaterial", "Select a UMAMaterial.", "OK");
				return;
			}

			int updated = 0;
			for (int i = 0; i < _overlays.Count; i++)
			{
				var overlay = _overlays[i];
				if (overlay == null)
				{
					continue;
				}

				if (overlay.material == _utilitiesTargetMaterial)
				{
					continue;
				}

				Undo.RecordObject(overlay, "Assign Overlay UMAMaterial");
				overlay.material = _utilitiesTargetMaterial;
				overlay.materialName = _utilitiesTargetMaterial.name;
				EditorUtility.SetDirty(overlay);
				updated++;
			}

			if (updated > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Assign UMAMaterial", "Updated overlays: " + updated, "OK");
		}

		private void AssignMaterialToAllOverlaysInList()
		{
			if (_utilitiesTargetMaterial == null)
			{
				EditorUtility.DisplayDialog("Assign UMAMaterial", "Select a UMAMaterial.", "OK");
				return;
			}

			int updated = 0;
			for (int i = 0; i < _filteredOverlays.Count; i++)
			{
				var overlay = _filteredOverlays[i];
				if (overlay == null)
				{
					continue;
				}

				if (overlay.material == _utilitiesTargetMaterial)
				{
					continue;
				}

				Undo.RecordObject(overlay, "Assign Overlay UMAMaterial");
				overlay.material = _utilitiesTargetMaterial;
				overlay.materialName = _utilitiesTargetMaterial.name;
				EditorUtility.SetDirty(overlay);
				updated++;
			}

			if (updated > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Assign UMAMaterial", "Updated overlays in list: " + updated, "OK");
		}

		private void DrawRelinkPanel()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
          _relinkFoldout = EditorGUILayout.Foldout(_relinkFoldout, "Relink Textures", true);
			if (_relinkFoldout)
			{
                EditorGUILayout.HelpBox("Replaces textures on the selected OverlayDataAsset list by name, using textures found in the specified folder.", MessageType.Info);
				EditorGUI.BeginChangeCheck();
				_textureFolder = (DefaultAsset)EditorGUILayout.ObjectField("Texture Folder", _textureFolder, typeof(DefaultAsset), false);
				if (EditorGUI.EndChangeCheck())
				{
                  _textureFolderPath = _textureFolder != null ? AssetDatabase.GetAssetPath(_textureFolder) : string.Empty;
					if (!string.IsNullOrEmpty(_textureFolderPath) && !AssetDatabase.IsValidFolder(_textureFolderPath))
					{
						_textureFolder = null;
						_textureFolderPath = string.Empty;
					}
					SavePreferences();
				}
               using (new EditorGUI.DisabledScope(true))
				{
                    EditorGUILayout.TextField("Path", _textureFolderPath ?? string.Empty);
				}
               EditorGUI.BeginChangeCheck();
				_includeSubfolders = EditorGUILayout.ToggleLeft("Include subfolders", _includeSubfolders);
				_skipWhenSameAsset = EditorGUILayout.ToggleLeft("Skip if already same asset", _skipWhenSameAsset);
				if (EditorGUI.EndChangeCheck())
				{
					SavePreferences();
				}

				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_textureFolderPath) || _overlays.Count == 0))
				{
					if (GUILayout.Button("Replace textures in selected overlays", GUILayout.Width(260), GUILayout.Height(24)))
					{
						ReplaceTexturesInSelectedOverlays();
					}
				}
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(6);
		}

		private void DrawUpdateFolderPanel()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			_updateFolderFoldout = EditorGUILayout.Foldout(_updateFolderFoldout, "Update Folder", true);
			if (_updateFolderFoldout)
			{
				EditorGUILayout.HelpBox("Searches the specified folder and all child folders for duplicate OverlayDataAsset filenames. Found duplicates are moved into a backup folder and replaced by copies of the selected overlay assets. Selected overlays with no match are copied into a not found folder under the specified root.", MessageType.Info);
				EditorGUILayout.BeginHorizontal();
				EditorGUI.BeginChangeCheck();
				_updateFolderPath = EditorGUILayout.TextField("Folder", _updateFolderPath ?? string.Empty);
				if (EditorGUI.EndChangeCheck())
				{
					_updateFolderPath = NormalizeAssetFolderPath(_updateFolderPath);
					SavePreferences();
				}

				if (GUILayout.Button("Browse", GUILayout.Width(80)))
				{
					BrowseForUpdateFolder();
				}
				EditorGUILayout.EndHorizontal();

				if (!string.IsNullOrEmpty(_updateFolderPath) && !AssetDatabase.IsValidFolder(_updateFolderPath))
				{
					EditorGUILayout.HelpBox("Select a valid folder under the project's Assets folder.", MessageType.Warning);
				}

				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
             using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_updateFolderPath) || !AssetDatabase.IsValidFolder(_updateFolderPath)))
				{
                  using (new EditorGUI.DisabledScope(_selectedOverlay == null || !_filteredOverlays.Contains(_selectedOverlay)))
					{
						if (GUILayout.Button("Process Selected Overlay", GUILayout.Width(180), GUILayout.Height(24)))
						{
							ProcessOverlaysInUpdateFolder(new List<UMA.OverlayDataAsset> { _selectedOverlay }, "selected overlay");
						}
					}

					using (new EditorGUI.DisabledScope(_filteredOverlays.Count == 0))
					{
                        if (GUILayout.Button("Process All Overlays", GUILayout.Width(180), GUILayout.Height(24)))
						{
							ProcessOverlaysInUpdateFolder(_filteredOverlays, "overlays in list");
						}
					}
				}
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(6);
		}

		private void BrowseForUpdateFolder()
		{
			string startFolder = Application.dataPath;
			if (!string.IsNullOrEmpty(_updateFolderPath) && AssetDatabase.IsValidFolder(_updateFolderPath))
			{
				string absoluteFolder = Path.GetFullPath(_updateFolderPath);
				if (Directory.Exists(absoluteFolder))
				{
					startFolder = absoluteFolder;
				}
			}

			string pickedFolder = EditorUtility.OpenFolderPanel("Select overlay update folder", startFolder, string.Empty);
			if (string.IsNullOrEmpty(pickedFolder))
			{
				return;
			}

			string assetFolder = GetAssetFolderPathFromAbsolutePath(pickedFolder);
			if (string.IsNullOrEmpty(assetFolder) || !AssetDatabase.IsValidFolder(assetFolder))
			{
				EditorUtility.DisplayDialog("Update Folder", "Select a folder under the project's Assets folder.", "OK");
				return;
			}

			_updateFolderPath = assetFolder;
			SavePreferences();
		}

        private void ProcessOverlaysInUpdateFolder(IList<UMA.OverlayDataAsset> overlaysToProcess, string progressLabel)
		{
			if (string.IsNullOrEmpty(_updateFolderPath) || !AssetDatabase.IsValidFolder(_updateFolderPath))
			{
				EditorUtility.DisplayDialog("Update Folder", "Select a valid folder under the project's Assets folder.", "OK");
				return;
			}

           if (overlaysToProcess == null || overlaysToProcess.Count == 0)
			{
				return;
			}

			Dictionary<string, List<string>> duplicatesByFileName = BuildOverlayPathLookup(_updateFolderPath);
			List<string> errors = new List<string>();
			int overlaysWithMatches = 0;
			int duplicatesReplaced = 0;
			int movedToBackup = 0;
			int copiedToNotFound = 0;
			string backupRoot = null;
			string notFoundRoot = null;

			try
			{
               for (int i = 0; i < overlaysToProcess.Count; i++)
				{
                    UMA.OverlayDataAsset overlay = overlaysToProcess[i];
					if (overlay == null)
					{
						continue;
					}

                 EditorUtility.DisplayProgressBar("Update Folder", "Processing " + progressLabel + "...", Mathf.Clamp01((float)(i + 1) / Mathf.Max(1, overlaysToProcess.Count)));

					string sourcePath = AssetDatabase.GetAssetPath(overlay);
					if (string.IsNullOrEmpty(sourcePath))
					{
						continue;
					}

					string fileName = Path.GetFileName(sourcePath);
					if (string.IsNullOrEmpty(fileName))
					{
						continue;
					}

					List<string> duplicatePaths = new List<string>();
					if (duplicatesByFileName.TryGetValue(fileName, out var foundPaths) && foundPaths != null)
					{
						for (int p = 0; p < foundPaths.Count; p++)
						{
							string candidatePath = foundPaths[p];
							if (string.Equals(candidatePath, sourcePath, System.StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}
							duplicatePaths.Add(candidatePath);
						}
					}

					if (duplicatePaths.Count > 0)
					{
						overlaysWithMatches++;
						if (string.IsNullOrEmpty(backupRoot))
						{
							backupRoot = EnsureAssetFolder(_updateFolderPath + "/backup");
						}

						for (int p = 0; p < duplicatePaths.Count; p++)
						{
							string duplicatePath = duplicatePaths[p];
							string relativePath = GetRelativeAssetPath(_updateFolderPath, duplicatePath);
							string relativeFolder = Path.GetDirectoryName(relativePath);
							relativeFolder = string.IsNullOrEmpty(relativeFolder) ? string.Empty : relativeFolder.Replace('\\', '/');
							string backupFolder = string.IsNullOrEmpty(relativeFolder)
								? backupRoot
								: EnsureAssetFolder(backupRoot + "/" + relativeFolder);
							string backupPath = AssetDatabase.GenerateUniqueAssetPath(backupFolder + "/" + Path.GetFileName(duplicatePath));

							string moveError = AssetDatabase.MoveAsset(duplicatePath, backupPath);
							if (!string.IsNullOrEmpty(moveError))
							{
								errors.Add("Move failed for '" + duplicatePath + "': " + moveError);
								continue;
							}

							movedToBackup++;
							if (!AssetDatabase.CopyAsset(sourcePath, duplicatePath))
							{
								errors.Add("Copy failed for '" + sourcePath + "' to '" + duplicatePath + "'.");
								continue;
							}

							duplicatesReplaced++;
						}
					}
					else
					{
						if (string.IsNullOrEmpty(notFoundRoot))
						{
							notFoundRoot = EnsureAssetFolder(_updateFolderPath + "/not found");
						}

						string notFoundPath = AssetDatabase.GenerateUniqueAssetPath(notFoundRoot + "/" + fileName);
						if (!AssetDatabase.CopyAsset(sourcePath, notFoundPath))
						{
							errors.Add("Copy failed for missing overlay '" + sourcePath + "' to '" + notFoundPath + "'.");
							continue;
						}

						copiedToNotFound++;
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog(
				"Update Folder",
              "Overlays processed: " + overlaysToProcess.Count +
				"\nOverlays with duplicates found: " + overlaysWithMatches +
				"\nDuplicates moved to backup: " + movedToBackup +
				"\nDuplicates replaced: " + duplicatesReplaced +
				"\nCopied to not found: " + copiedToNotFound,
				"OK");

			if (errors.Count > 0)
			{
				EditorUtility.DisplayDialog("Update Folder Errors", string.Join("\n", errors.ToArray()), "OK");
			}
		}

		private static Dictionary<string, List<string>> BuildOverlayPathLookup(string rootFolder)
		{
			Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
			if (string.IsNullOrEmpty(rootFolder))
			{
				return result;
			}

			string backupFolder = rootFolder.TrimEnd('/') + "/backup";
			string notFoundFolder = rootFolder.TrimEnd('/') + "/not found";
			string[] guids = AssetDatabase.FindAssets("t:OverlayDataAsset", new[] { rootFolder });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (string.IsNullOrEmpty(path)
					|| IsPathUnderFolder(path, backupFolder)
					|| IsPathUnderFolder(path, notFoundFolder))
				{
					continue;
				}

				string fileName = Path.GetFileName(path);
				if (string.IsNullOrEmpty(fileName))
				{
					continue;
				}

				if (!result.TryGetValue(fileName, out var paths) || paths == null)
				{
					paths = new List<string>();
					result[fileName] = paths;
				}
				paths.Add(path);
			}

			return result;
		}

		private static bool IsPathUnderFolder(string assetPath, string folderPath)
		{
			if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(folderPath))
			{
				return false;
			}

			string normalizedFolder = folderPath.Replace('\\', '/').TrimEnd('/');
			string normalizedAssetPath = assetPath.Replace('\\', '/');
			return normalizedAssetPath.StartsWith(normalizedFolder + "/", System.StringComparison.OrdinalIgnoreCase);
		}

		private static string GetRelativeAssetPath(string rootFolder, string assetPath)
		{
			string normalizedRoot = rootFolder.Replace('\\', '/').TrimEnd('/');
			string normalizedAssetPath = assetPath.Replace('\\', '/');
			if (normalizedAssetPath.StartsWith(normalizedRoot + "/", System.StringComparison.OrdinalIgnoreCase))
			{
				return normalizedAssetPath.Substring(normalizedRoot.Length + 1);
			}

			return Path.GetFileName(normalizedAssetPath) ?? string.Empty;
		}

		private static string EnsureAssetFolder(string folderPath)
		{
			string normalizedPath = NormalizeAssetFolderPath(folderPath);
			if (string.IsNullOrEmpty(normalizedPath))
			{
				return string.Empty;
			}

			if (AssetDatabase.IsValidFolder(normalizedPath))
			{
				return normalizedPath;
			}

			string[] parts = normalizedPath.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length == 0)
			{
				return string.Empty;
			}

			string current = parts[0];
			for (int i = 1; i < parts.Length; i++)
			{
				string next = current + "/" + parts[i];
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(current, parts[i]);
				}
				current = next;
			}

			return current;
		}

		private static string NormalizeAssetFolderPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return string.Empty;
			}

			string normalized = path.Trim().Replace('\\', '/').TrimEnd('/');
			if (Path.IsPathRooted(normalized))
			{
				string assetPath = GetAssetFolderPathFromAbsolutePath(normalized);
				return assetPath ?? normalized;
			}

			return normalized;
		}

		private static string GetAssetFolderPathFromAbsolutePath(string absolutePath)
		{
			if (string.IsNullOrEmpty(absolutePath))
			{
				return string.Empty;
			}

			string normalizedAbsolutePath = absolutePath.Replace('\\', '/').TrimEnd('/');
			string normalizedAssetsPath = Application.dataPath.Replace('\\', '/').TrimEnd('/');
			if (string.Equals(normalizedAbsolutePath, normalizedAssetsPath, System.StringComparison.OrdinalIgnoreCase))
			{
				return "Assets";
			}

			if (normalizedAbsolutePath.StartsWith(normalizedAssetsPath + "/", System.StringComparison.OrdinalIgnoreCase))
			{
				return "Assets" + normalizedAbsolutePath.Substring(normalizedAssetsPath.Length);
			}

			return null;
		}

		private void ReplaceTexturesInSelectedOverlays()
		{
			if (string.IsNullOrEmpty(_textureFolderPath))
			{
				EditorUtility.DisplayDialog("Relink Textures", "Select a valid texture folder.", "OK");
				return;
			}
			if (_overlays.Count == 0)
			{
				return;
			}

			var nameToTexture = BuildTextureLookup(_textureFolderPath, _includeSubfolders);
			if (nameToTexture.Count == 0)
			{
				EditorUtility.DisplayDialog("Relink Textures", "No textures found in folder: " + _textureFolderPath, "OK");
				return;
			}

            int overlaysUpdated = 0;
			int texturesReplaced = 0;
			int texturesMissing = 0;
         int alphaMasksReplaced = 0;
         var missingNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			try
			{
				for (int i = 0; i < _overlays.Count; i++)
				{
					var overlay = _overlays[i];
					if (overlay == null) continue;

					var list = overlay.textureList;
					if (list == null || list.Length == 0) continue;

                    bool anyChanged = false;
					Undo.RecordObject(overlay, "Relink overlay textures");

					if (overlay.alphaMask != null)
					{
						string alphaBaseName = GetTextureBaseName(overlay.alphaMask);
						if (!string.IsNullOrEmpty(alphaBaseName) && nameToTexture.TryGetValue(alphaBaseName, out var alphaReplacement) && alphaReplacement != null)
						{
							if (!_skipWhenSameAsset || alphaReplacement != overlay.alphaMask)
							{
								overlay.alphaMask = alphaReplacement;
								alphaMasksReplaced++;
								anyChanged = true;
							}
						}
						else
						{
							texturesMissing++;
                           if (!string.IsNullOrEmpty(alphaBaseName))
							{
								missingNames.Add(alphaBaseName);
							}
						}
					}

					for (int t = 0; t < list.Length; t++)
					{
						var current = list[t];
						if (current == null) continue;

						string baseName = GetTextureBaseName(current);
						if (string.IsNullOrEmpty(baseName)) continue;

						if (!nameToTexture.TryGetValue(baseName, out var replacement) || replacement == null)
						{
							texturesMissing++;
                           missingNames.Add(baseName);
							continue;
						}

						if (_skipWhenSameAsset && replacement == current)
						{
							continue;
						}

						list[t] = replacement;
						texturesReplaced++;
						anyChanged = true;
						if (overlay.textureNames != null && t < overlay.textureNames.Length)
						{
							overlay.textureNames[t] = replacement.name;
						}
					}

					if (!anyChanged) continue;
					overlay.textureList = list;
					EditorUtility.SetDirty(overlay);
					overlaysUpdated++;
				}
			}
			finally
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog(
				"Relink Textures",
				"Overlays updated: " + overlaysUpdated +
				"\nTextures replaced: " + texturesReplaced +
             "\nAlpha masks replaced: " + alphaMasksReplaced +
				"\nTextures not found: " + texturesMissing,
				"OK");

			if (missingNames.Count > 0)
			{
				var list = new List<string>(missingNames);
				list.Sort(System.StringComparer.OrdinalIgnoreCase);
				string details = string.Join("\n", list);
				EditorUtility.DisplayDialog("Textures not found", details, "OK");
			}
		}

        private static Dictionary<string, Texture> BuildTextureLookup(string folderPath, bool includeSubfolders)
		{
			var result = new Dictionary<string, Texture>(System.StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(folderPath)) return result;
			folderPath = folderPath.Replace('\\', '/');

			string[] search = new[] { folderPath };
          const string filter = "t:Texture";
			// `FindAssets` will search recursively within provided folder(s).
			string[] guids = AssetDatabase.FindAssets(filter, search);
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				if (string.IsNullOrEmpty(path)) continue;
				if (!includeSubfolders)
				{
                   string dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
					if (!string.Equals(dir, folderPath, System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
				}
             var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
				if (tex == null) continue;
				string key = Path.GetFileNameWithoutExtension(path);
				if (string.IsNullOrEmpty(key)) continue;
               if (result.TryGetValue(key, out var existing) && existing != null)
				{
					if (GetExtensionPriority(path) >= GetExtensionPriority(AssetDatabase.GetAssetPath(existing)))
					{
						continue;
					}
					result[key] = tex;
				}
				else
				{
					result[key] = tex;
				}
			}
			return result;
		}

		private static int GetExtensionPriority(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPath)) return int.MaxValue;
			string ext = Path.GetExtension(assetPath);
			if (string.IsNullOrEmpty(ext)) return int.MaxValue;
			ext = ext.TrimStart('.').ToLowerInvariant();
			switch (ext)
			{
				case "png": return 0;
				case "jpg":
				case "jpeg": return 1;
				case "tga": return 2;
				case "tif":
				case "tiff": return 3;
				default: return 10;
			}
		}

		private static string GetTextureBaseName(Texture texture)
		{
			if (texture == null) return null;
			string path = AssetDatabase.GetAssetPath(texture);
			if (!string.IsNullOrEmpty(path))
			{
				return Path.GetFileNameWithoutExtension(path);
			}
			// Fallback: if texture is generated/unassigned to disk, use object name.
			return texture.name;
		}

		private void DrawOverlayList()
		{
			EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.40f));
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("Selected Overlays", EditorStyles.boldLabel);
			if (GUILayout.Button("Refresh", GUILayout.Width(70)))
			{
				RefreshFromSelection();
			}
			EditorGUILayout.EndHorizontal();
			var previouslySelected = _selectedOverlay;
			EditorGUI.BeginChangeCheck();
			string[] filterLabels = { "all", "complete", "incomplete" };
			_filter = (OverlayFilter)EditorGUILayout.Popup((int)_filter, filterLabels);
			if (EditorGUI.EndChangeCheck())
			{
				RebuildFilteredOverlays(previouslySelected);
				GUI.FocusControl(null);
			}
			EditorGUILayout.Space(2);
			_leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));
			for (int i = 0; i < _filteredOverlays.Count; i++)
			{
				var overlay = _filteredOverlays[i];
				if (overlay == null)
				{
					continue;
				}

				EditorGUILayout.BeginHorizontal();
				bool selected = (overlay == _selectedOverlay);
				if (GUILayout.Toggle(selected, GUIContent.none, GUILayout.Width(18)) != selected)
				{
					_selectedOverlay = overlay;
					GUI.FocusControl(null);
				}
				var buttonStyle = selected ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
				if (GUILayout.Button(overlay.name, buttonStyle, GUILayout.ExpandWidth(true)))
				{
					_selectedOverlay = overlay;
					GUI.FocusControl(null);
				}
              GUILayout.Label(GetOverlayStatusLabel(overlay), GUILayout.Width(170));
				EditorGUILayout.EndHorizontal();
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private void DrawOverlayDetails()
		{
			EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
			EditorGUILayout.LabelField("Overlay Textures", EditorStyles.boldLabel);

			if (_selectedOverlay == null)
			{
				EditorGUILayout.HelpBox("Select an overlay to view its textures.", MessageType.Info);
				EditorGUILayout.EndVertical();
				return;
			}

			var overlay = _selectedOverlay;
			if (overlay == null)
			{
				EditorGUILayout.HelpBox("Selected overlay is missing.", MessageType.Warning);
				EditorGUILayout.EndVertical();
				return;
			}

			EditorGUILayout.LabelField("Overlay", overlay.name);
			EditorGUILayout.Space(4);

			var mat = overlay.GetMaterial();

			var texList = overlay.textureList;
			int displayCount = texList != null ? texList.Length : 0;
			_rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
			for (int i = 0; i < displayCount; i++)
			{
				Texture current = texList[i];
				string paramName = "Texture " + i;
				if (mat != null && mat.channels != null && i < mat.channels.Length && !string.IsNullOrEmpty(mat.channels[i].materialPropertyName))
				{
					paramName = mat.channels[i].materialPropertyName;
				}
				string texName = current != null ? current.name : "<Not Set>";
				const float rowHeight = 128f;
				const float previewSize = 96f;

				EditorGUILayout.BeginVertical(EditorStyles.helpBox);
				EditorGUILayout.BeginHorizontal();
				GUILayout.Label(i.ToString(), GUILayout.Width(26));
				GUILayout.Label(paramName, EditorStyles.boldLabel);
				GUILayout.FlexibleSpace();
				GUILayout.Label(texName, EditorStyles.miniLabel, GUILayout.Width(180));
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.BeginHorizontal(GUILayout.Height(rowHeight));
				EditorGUI.BeginChangeCheck();
				var newTex = (Texture)EditorGUILayout.ObjectField(current, typeof(Texture), false, GUILayout.Width(previewSize), GUILayout.Height(previewSize));
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(overlay, "Set overlay texture");
					var list = overlay.textureList;
					if (list != null && i < list.Length)
					{
						list[i] = newTex;
						overlay.textureList = list;
						EditorUtility.SetDirty(overlay);
						AssetDatabase.SaveAssets();
					}
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

        private enum OverlayStatus
		{
			Complete = 0,
			MissingTextures = 1,
			MissingTexturesAndOvl = 2,
			MissingOvl = 3
		}

		private static OverlayStatus GetOverlayStatus(UMA.OverlayDataAsset overlay)
		{
			if (overlay == null)
			{
               return OverlayStatus.MissingTexturesAndOvl;
			}

			bool missingOvl = overlay.material == null;
			bool missingTextures = false;
			var list = overlay.textureList;
			if (list == null || list.Length == 0)
			{
               missingTextures = true;
			}
           else
			{
                for (int i = 0; i < list.Length; i++)
				{
                   if (list[i] == null)
					{
						missingTextures = true;
						break;
					}
				}
			}

			if (missingTextures && missingOvl)
			{
				return OverlayStatus.MissingTexturesAndOvl;
			}
			if (missingTextures)
			{
				return OverlayStatus.MissingTextures;
			}
			if (missingOvl)
			{
				return OverlayStatus.MissingOvl;
			}
			return OverlayStatus.Complete;
		}

		private static GUIContent GetOverlayStatusLabel(UMA.OverlayDataAsset overlay)
		{
			switch (GetOverlayStatus(overlay))
			{
				case OverlayStatus.MissingTextures:
					return _missingTexturesLabel;
				case OverlayStatus.MissingTexturesAndOvl:
					return _missingTexturesAndOvlLabel;
				case OverlayStatus.MissingOvl:
					return _missingOvlLabel;
				default:
					return _completeLabel;
			}
		}


	}
}
