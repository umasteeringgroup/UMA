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
internal class UmaExamineSlotsWindow : EditorWindow
		{
			private enum SlotSortMode
			{
				None = 0,
				Name = 1,
				SlotName = 2
			}

			private readonly List<UMA.SlotDataAsset> _slots = new List<UMA.SlotDataAsset>();
			private bool[] _slotSelected = new bool[0];
			private Vector2 _leftScroll;
			private Vector2 _rightScroll;
			private DefaultAsset _destFolder;
			private string _destFolderPath;
			private SlotSortMode _sortMode = SlotSortMode.None;
			private bool _setMaterial;
			private UMA.UMAMaterial _targetMaterial;
			private bool _setOverlayScale;
			private float _overlayScale = 1f;
			private bool _addTags;
			private string _tagsText = string.Empty;
			private bool _setWildcard;
			private bool _wildcardValue;
			private bool _addWildcardRaces;
			private string _racesText = string.Empty;
			private bool _copyToFolderFoldout = true;
			private string _copyToFolderPath = string.Empty;

			public static void Open(List<UMA.SlotDataAsset> slots)
			{
				var window = GetWindow<UmaExamineSlotsWindow>(false, "Examine Slots", true);
				window.minSize = new Vector2(860f, 420f);
				window._slots.Clear();
				if (slots != null)
				{
					window._slots.AddRange(slots);
				}
				window._slotSelected = new bool[window._slots.Count];
				for (int i = 0; i < window._slotSelected.Length; i++)
				{
					window._slotSelected[i] = true;
				}
				window._destFolder = null;
				window._destFolderPath = string.Empty;
				window.Show();
				window.Focus();
			}

			private void RefreshFromSelection()
			{
              var selected = UMAAvatarLoadSaveMenuItems.GetSelectedSlots();
				_slots.Clear();
				_slots.AddRange(selected);
				_slotSelected = new bool[_slots.Count];
				for (int i = 0; i < _slotSelected.Length; i++)
				{
					_slotSelected[i] = true;
				}
				SortSlots();
				Repaint();
			}

			private void OnGUI()
			{
				EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
				GUILayout.Label("Examine Slots", EditorStyles.boldLabel);
				GUILayout.FlexibleSpace();
				if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
				{
					RefreshFromSelection();
				}
				EditorGUILayout.EndHorizontal();

				if (_slots.Count == 0)
				{
					EditorGUILayout.HelpBox("Select one or more SlotDataAsset assets in the Project window.", MessageType.Info);
					return;
				}

				EditorGUILayout.BeginHorizontal();
				DrawSlotsColumn();
				GUILayout.Space(10);
				DrawOptionsColumn();
				EditorGUILayout.EndHorizontal();
			}

			private void DrawOptionsColumn()
			{
				EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.42f));
				EditorGUILayout.LabelField("Slot Updates", EditorStyles.boldLabel);

				_leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));
				_setMaterial = EditorGUILayout.ToggleLeft("Set UMAMaterial", _setMaterial);
				using (new EditorGUI.DisabledScope(!_setMaterial))
				{
					_targetMaterial = (UMA.UMAMaterial)EditorGUILayout.ObjectField("UMAMaterial", _targetMaterial, typeof(UMA.UMAMaterial), false);
				}

				_setOverlayScale = EditorGUILayout.ToggleLeft("Set OverlayScale", _setOverlayScale);
				using (new EditorGUI.DisabledScope(!_setOverlayScale))
				{
					_overlayScale = EditorGUILayout.FloatField("OverlayScale", _overlayScale);
				}

				_addTags = EditorGUILayout.ToggleLeft("Add Tags", _addTags);
				using (new EditorGUI.DisabledScope(!_addTags))
				{
					_tagsText = EditorGUILayout.TextField("Tags (comma/semicolon)", _tagsText);
				}

				_setWildcard = EditorGUILayout.ToggleLeft("Set Wildcard", _setWildcard);
				using (new EditorGUI.DisabledScope(!_setWildcard))
				{
					_wildcardValue = EditorGUILayout.Toggle("Wildcard Value", _wildcardValue);
				}

				_addWildcardRaces = EditorGUILayout.ToggleLeft("Add Wildcard Races", _addWildcardRaces);
				using (new EditorGUI.DisabledScope(!_addWildcardRaces))
				{
					_racesText = EditorGUILayout.TextField("Races (comma/semicolon)", _racesText);
				}

				EditorGUILayout.Space(6);
				EditorGUILayout.LabelField("Destination Folder", EditorStyles.boldLabel);
				EditorGUI.BeginChangeCheck();
				_destFolder = (DefaultAsset)EditorGUILayout.ObjectField(_destFolder, typeof(DefaultAsset), false);
				if (EditorGUI.EndChangeCheck())
				{
					_destFolderPath = _destFolder != null ? AssetDatabase.GetAssetPath(_destFolder) : string.Empty;
					if (!string.IsNullOrEmpty(_destFolderPath) && !AssetDatabase.IsValidFolder(_destFolderPath))
					{
						_destFolder = null;
						_destFolderPath = string.Empty;
					}
				}

				using (new EditorGUI.DisabledScope(true))
				{
					EditorGUILayout.TextField("Path", _destFolderPath);
				}

				EditorGUILayout.Space(6);
				_drawCopyPanel();

				EditorGUILayout.EndScrollView();

				EditorGUILayout.Space(8);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Apply Updates", GUILayout.Width(140), GUILayout.Height(28)))
				{
					ApplyUpdates();
				}
				if (GUILayout.Button("Replace Slots In Folder", GUILayout.Width(180), GUILayout.Height(28)))
				{
					ReplaceSlotsInFolder();
				}
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.EndVertical();
			}

			private void _drawCopyPanel()
			{
				_copyToFolderFoldout = EditorGUILayout.Foldout(_copyToFolderFoldout, "Copy Slots To Folder", true);
				if (!_copyToFolderFoldout)
				{
					return;
				}

				EditorGUILayout.HelpBox("Searches the specified folder and all child folders for duplicate SlotDataAsset filenames. Found duplicates are moved into a backup folder and replaced by copies of the selected slot assets. Slots with no match are copied into a Not found folder under the specified root.", MessageType.Info);

				EditorGUILayout.BeginHorizontal();
				EditorGUI.BeginChangeCheck();
				_copyToFolderPath = EditorGUILayout.TextField("Folder", _copyToFolderPath ?? string.Empty);
				if (EditorGUI.EndChangeCheck())
				{
					_copyToFolderPath = NormalizeAssetFolderPath(_copyToFolderPath);
				}

				if (GUILayout.Button("Browse", GUILayout.Width(80)))
				{
					BrowseForCopyFolder();
				}
				EditorGUILayout.EndHorizontal();

				if (!string.IsNullOrEmpty(_copyToFolderPath) && !AssetDatabase.IsValidFolder(_copyToFolderPath))
				{
					EditorGUILayout.HelpBox("Select a valid folder under the project's Assets folder.", MessageType.Warning);
				}

				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_copyToFolderPath) || !AssetDatabase.IsValidFolder(_copyToFolderPath)))
				{
					using (new EditorGUI.DisabledScope(GetCheckedSlots().Count == 0))
					{
						if (GUILayout.Button("Process Selected Slot", GUILayout.Width(180), GUILayout.Height(24)))
						{
							ProcessSlotsInCopyFolder(GetCheckedSlots(), "selected slots");
						}
					}

					using (new EditorGUI.DisabledScope(_slots.Count == 0))
					{
						if (GUILayout.Button("Process All Slots", GUILayout.Width(180), GUILayout.Height(24)))
						{
							ProcessSlotsInCopyFolder(_slots, "all slots");
						}
					}
				}
				EditorGUILayout.EndHorizontal();
			}

			private void DrawSlotsColumn()
			{
				EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
				EditorGUILayout.LabelField("Selected Slots", EditorStyles.boldLabel);
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Sort By", GUILayout.Width(50));
				EditorGUI.BeginChangeCheck();
				_sortMode = (SlotSortMode)EditorGUILayout.EnumPopup(_sortMode, GUILayout.Width(120));
				if (EditorGUI.EndChangeCheck())
				{
					SortSlots();
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Select All", GUILayout.Width(90)))
				{
					SetAllSelections(true);
				}
				if (GUILayout.Button("Deselect All", GUILayout.Width(100)))
				{
					SetAllSelections(false);
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.Space(4);

				_rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
				for (int i = 0; i < _slots.Count; i++)
				{
					var slot = _slots[i];
					if (slot == null)
					{
						continue;
					}

					EditorGUILayout.BeginHorizontal();
					_slotSelected[i] = EditorGUILayout.Toggle(_slotSelected[i], GUILayout.Width(18));
					EditorGUILayout.ObjectField(slot, typeof(UMA.SlotDataAsset), false);
					EditorGUILayout.EndHorizontal();
				}
				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();
			}

			private void SetAllSelections(bool value)
			{
				for (int i = 0; i < _slotSelected.Length; i++)
				{
					_slotSelected[i] = value;
				}
			}

			private void SortSlots()
			{
				if (_sortMode == SlotSortMode.None || _slots.Count == 0)
				{
					return;
				}

				var entries = new List<SlotEntry>(_slots.Count);
				for (int i = 0; i < _slots.Count; i++)
				{
					entries.Add(new SlotEntry { Slot = _slots[i], Selected = (i < _slotSelected.Length && _slotSelected[i]) });
				}

				if (_sortMode == SlotSortMode.Name)
				{
					entries.Sort((a, b) => string.Compare(a.GetName(), b.GetName(), System.StringComparison.OrdinalIgnoreCase));
				}
				else if (_sortMode == SlotSortMode.SlotName)
				{
					entries.Sort((a, b) => string.Compare(a.GetSlotName(), b.GetSlotName(), System.StringComparison.OrdinalIgnoreCase));
				}

				_slots.Clear();
				_slotSelected = new bool[entries.Count];
				for (int i = 0; i < entries.Count; i++)
				{
					_slots.Add(entries[i].Slot);
					_slotSelected[i] = entries[i].Selected;
				}
			}

			private struct SlotEntry
			{
				public UMA.SlotDataAsset Slot;
				public bool Selected;

				public string GetName()
				{
					if (Slot == null)
					{
						return string.Empty;
					}
					return Slot.name ?? string.Empty;
				}

				public string GetSlotName()
				{
					if (Slot == null)
					{
						return string.Empty;
					}
					return Slot.slotName ?? string.Empty;
				}
			}

			private void ApplyUpdates()
			{
				bool anySaved = false;
				var tagsToAdd = ParseTokens(_tagsText);
				var racesToAdd = ParseTokens(_racesText);

				for (int i = 0; i < _slots.Count; i++)
				{
					if (i >= _slotSelected.Length || !_slotSelected[i])
					{
						continue;
					}
					var slot = _slots[i];
					if (slot == null)
					{
						continue;
					}

					bool changed = false;
					Undo.RecordObject(slot, "Update Slot");

					if (_setMaterial)
					{
                        // SlotDataAsset has no direct material/materialName fields in this repo.
						// Keep UI toggle harmless here; material assignment is handled at overlay/recipe slot level.
					}

					if (_setOverlayScale)
					{
						slot.overlayScale = _overlayScale;
						changed = true;
					}

					if (_addTags && tagsToAdd.Count > 0)
					{
						var merged = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
						if (slot.tags != null)
						{
							for (int t = 0; t < slot.tags.Length; t++)
							{
								var tag = slot.tags[t];
								if (!string.IsNullOrEmpty(tag))
								{
									merged.Add(tag.Trim());
								}
							}
						}
						for (int t = 0; t < tagsToAdd.Count; t++)
						{
							merged.Add(tagsToAdd[t]);
						}
						if (merged.Count > 0)
						{
							slot.tags = new List<string>(merged).ToArray();
							changed = true;
						}
					}

					if (_setWildcard)
					{
						slot.isWildCardSlot = _wildcardValue;
						changed = true;
					}

					if (_addWildcardRaces && racesToAdd.Count > 0)
					{
						var merged = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
						if (slot.Races != null)
						{
							for (int r = 0; r < slot.Races.Length; r++)
							{
								var race = slot.Races[r];
								if (!string.IsNullOrEmpty(race))
								{
									merged.Add(race.Trim());
								}
							}
						}
						for (int r = 0; r < racesToAdd.Count; r++)
						{
							merged.Add(racesToAdd[r]);
						}
						if (merged.Count > 0)
						{
							slot.Races = new List<string>(merged).ToArray();
							changed = true;
						}
					}

					if (changed)
					{
						EditorUtility.SetDirty(slot);
#if UNITY_2021_1_OR_NEWER
						AssetDatabase.SaveAssetIfDirty(slot);
#endif
						anySaved = true;
					}
				}

				if (anySaved)
				{
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
				}
			}

			private void ReplaceSlotsInFolder()
			{
				if (string.IsNullOrEmpty(_destFolderPath))
				{
					EditorUtility.DisplayDialog("Replace Slots In Folder", "Select a destination folder.", "OK");
					return;
				}

				int updated = 0;
				for (int i = 0; i < _slots.Count; i++)
				{
					if (i >= _slotSelected.Length || !_slotSelected[i])
					{
						continue;
					}
					var slot = _slots[i];
					if (slot == null)
					{
						continue;
					}

					var searchNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
					if (!string.IsNullOrEmpty(slot.name))
					{
						searchNames.Add(slot.name);
					}
					if (!string.IsNullOrEmpty(slot.slotName))
					{
						searchNames.Add(slot.slotName);
					}

					string[] guids = AssetDatabase.FindAssets("t:SlotDataAsset", new[] { _destFolderPath });
					for (int g = 0; g < guids.Length; g++)
					{
						string path = AssetDatabase.GUIDToAssetPath(guids[g]);
						if (string.IsNullOrEmpty(path))
						{
							continue;
						}
						var target = AssetDatabase.LoadAssetAtPath<UMA.SlotDataAsset>(path);
						if (target == null)
						{
							continue;
						}
						if (target == slot)
						{
							continue;
						}
						if (!searchNames.Contains(target.name) && !searchNames.Contains(target.slotName))
						{
							continue;
						}

						Undo.RecordObject(target, "Replace Slot In Folder");
						EditorUtility.CopySerialized(slot, target);
						EditorUtility.SetDirty(target);
						AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
#if UNITY_2021_1_OR_NEWER
						AssetDatabase.SaveAssetIfDirty(target);
#endif
						updated++;
					}
				}

				if (updated > 0)
				{
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh();
					UMAAssetIndexer.RebuildAllUMAS();
				}
				EditorUtility.DisplayDialog("Replace Slots In Folder", "Updated slots: " + updated, "OK");
			}

			private void BrowseForCopyFolder()
			{
				string startFolder = Application.dataPath;
				if (!string.IsNullOrEmpty(_copyToFolderPath) && AssetDatabase.IsValidFolder(_copyToFolderPath))
				{
					string absoluteFolder = Path.GetFullPath(_copyToFolderPath);
					if (Directory.Exists(absoluteFolder))
					{
						startFolder = absoluteFolder;
					}
				}

				string pickedFolder = EditorUtility.OpenFolderPanel("Select slot copy folder", startFolder, string.Empty);
				if (string.IsNullOrEmpty(pickedFolder))
				{
					return;
				}

				string assetFolder = GetAssetFolderPathFromAbsolutePath(pickedFolder);
				if (string.IsNullOrEmpty(assetFolder) || !AssetDatabase.IsValidFolder(assetFolder))
				{
					EditorUtility.DisplayDialog("Copy Slots To Folder", "Select a folder under the project's Assets folder.", "OK");
					return;
				}

				_copyToFolderPath = assetFolder;
			}

			private List<UMA.SlotDataAsset> GetCheckedSlots()
			{
				var result = new List<UMA.SlotDataAsset>();
				for (int i = 0; i < _slots.Count; i++)
				{
					if (i >= _slotSelected.Length || !_slotSelected[i])
					{
						continue;
					}

					var slot = _slots[i];
					if (slot != null)
					{
						result.Add(slot);
					}
				}
				return result;
			}

			private void ProcessSlotsInCopyFolder(IList<UMA.SlotDataAsset> slotsToProcess, string progressLabel)
			{
				if (string.IsNullOrEmpty(_copyToFolderPath) || !AssetDatabase.IsValidFolder(_copyToFolderPath))
				{
					EditorUtility.DisplayDialog("Copy Slots To Folder", "Select a valid folder under the project's Assets folder.", "OK");
					return;
				}

				if (slotsToProcess == null || slotsToProcess.Count == 0)
				{
					return;
				}

				Dictionary<string, List<string>> duplicatesByFileName = BuildSlotPathLookup(_copyToFolderPath);
				List<string> errors = new List<string>();
				int slotsWithMatches = 0;
				int duplicatesReplaced = 0;
				int movedToBackup = 0;
				int copiedToNotFound = 0;
				string backupRoot = null;
				string notFoundRoot = null;

				try
				{
					for (int i = 0; i < slotsToProcess.Count; i++)
					{
						var slot = slotsToProcess[i];
						if (slot == null)
						{
							continue;
						}

						EditorUtility.DisplayProgressBar("Copy Slots To Folder", "Processing " + progressLabel + "...", Mathf.Clamp01((float)(i + 1) / Mathf.Max(1, slotsToProcess.Count)));

						string sourcePath = AssetDatabase.GetAssetPath(slot);
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
							slotsWithMatches++;
							if (string.IsNullOrEmpty(backupRoot))
							{
								backupRoot = EnsureAssetFolder(_copyToFolderPath + "/backup");
							}

							for (int p = 0; p < duplicatePaths.Count; p++)
							{
								string duplicatePath = duplicatePaths[p];
								string relativePath = GetRelativeAssetPath(_copyToFolderPath, duplicatePath);
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
								notFoundRoot = EnsureAssetFolder(_copyToFolderPath + "/Not found");
							}

							string notFoundPath = AssetDatabase.GenerateUniqueAssetPath(notFoundRoot + "/" + fileName);
							if (!AssetDatabase.CopyAsset(sourcePath, notFoundPath))
							{
								errors.Add("Copy failed for missing slot '" + sourcePath + "' to '" + notFoundPath + "'.");
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
					"Copy Slots To Folder",
					"Slots processed: " + slotsToProcess.Count +
					"\nSlots with duplicates found: " + slotsWithMatches +
					"\nDuplicates moved to backup: " + movedToBackup +
					"\nDuplicates replaced: " + duplicatesReplaced +
					"\nCopied to Not found: " + copiedToNotFound,
					"OK");

				if (errors.Count > 0)
				{
					EditorUtility.DisplayDialog("Copy Slots Errors", string.Join("\n", errors.ToArray()), "OK");
				}
			}

			private static Dictionary<string, List<string>> BuildSlotPathLookup(string rootFolder)
			{
				Dictionary<string, List<string>> result = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase);
				if (string.IsNullOrEmpty(rootFolder))
				{
					return result;
				}

				string backupFolder = rootFolder.TrimEnd('/') + "/backup";
				string notFoundFolder = rootFolder.TrimEnd('/') + "/Not found";
				string[] guids = AssetDatabase.FindAssets("t:SlotDataAsset", new[] { rootFolder });
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

			private static List<string> ParseTokens(string input)
			{
				var results = new List<string>();
				if (string.IsNullOrEmpty(input))
				{
					return results;
				}
				char[] separators = new[] { ',', ';', '\n', '\r', '\t' };
				string[] parts = input.Split(separators, System.StringSplitOptions.RemoveEmptyEntries);
				for (int i = 0; i < parts.Length; i++)
				{
					string token = parts[i].Trim();
					if (!string.IsNullOrEmpty(token))
					{
						results.Add(token);
					}
				}
				return results;
			}
		}
}
