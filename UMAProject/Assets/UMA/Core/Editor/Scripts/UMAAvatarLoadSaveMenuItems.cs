using UnityEngine;
using UnityEditor;
using UMA.CharacterSystem;
using System.Collections.Generic;
using System.IO;
using UMA.Examples;
using UMA.PoseTools;
using static UMA.UMAData;
using UnityEngine.Rendering;

namespace UMA.Editors
{
    public class UMAAvatarLoadSaveMenuItems : Editor
	{
		[MenuItem("Assets/UMA/Examine Wearables", false, 2001)]
		private static void AssignLocationsToWearablesMenu()
		{
			var selectedRecipes = GetSelectedWardrobeRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Assign Locations", "Select one or more UMAWardrobeRecipe assets in the Project window.", "OK");
				return;
			}

			ExamineWearables.Open(selectedRecipes);
		}

		[MenuItem("Assets/UMA/Examine Wearables", true)]
		private static bool AssignLocationsToWearablesMenu_Validate()
		{
			return GetSelectedWardrobeRecipes().Count > 0;
		}

		[MenuItem("Assets/UMA/Consolidate Textures", false, 2002)]
		private static void ConsolidateTexturesMenu()
		{
			var selectedRecipes = GetSelectedWardrobeRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Consolidate Textures", "Select one or more UMAWardrobeRecipe assets in the Project window.", "OK");
				return;
			}

			UmaConsolidateTexturesWindow.Open(selectedRecipes);
		}

		[MenuItem("Assets/UMA/Consolidate Textures", true)]
		private static bool ConsolidateTexturesMenu_Validate()
		{
			return GetSelectedWardrobeRecipes().Count > 0;
		}

		[MenuItem("Assets/UMA/Examine Overlays", false, 2003)]
		private static void ExamineOverlaysMenu()
		{
			var overlays = GetSelectedOverlays();
			if (overlays.Count == 0)
			{
				EditorUtility.DisplayDialog("Examine Overlays", "Select one or more OverlayDataAsset assets in the Project window.", "OK");
				return;
			}

			UmaExamineOverlaysWindow.Open(overlays);
		}

		[MenuItem("Assets/UMA/Examine Overlays", true)]
		private static bool ExamineOverlaysMenu_Validate()
		{
			return GetSelectedOverlays().Count > 0;
		}

		[MenuItem("Assets/UMA/Convert selected textures to PNG", false, 2004)]
		private static void ConvertSelectedTexturesToPngMenu()
		{
			var textures = GetSelectedTextures();
			if (textures.Count == 0)
			{
				EditorUtility.DisplayDialog("Convert textures", "Select one or more Texture2D assets in the Project window.", "OK");
				return;
			}

			UmaConvertTexturesToPngWindow.Open(textures);
		}

		[MenuItem("Assets/UMA/Convert selected textures to PNG", true)]
		private static bool ConvertSelectedTexturesToPngMenu_Validate()
		{
			return GetSelectedTextures().Count > 0;
		}

		[MenuItem("Assets/UMA/Add Race(s) to Selected Recipes", false, 2000)]
		private static void AddRacesToSelectedRecipesMenu()
		{
			var selectedRecipes = GetSelectedWardrobeRecipes();
			if (selectedRecipes.Count == 0)
			{
				EditorUtility.DisplayDialog("Add Races", "Select one or more UMAWardrobeRecipe assets in the Project window.", "OK");
				return;
			}
			UmaAddRacesToRecipesWindow.Open(selectedRecipes);
		}

	internal class UmaConsolidateTexturesWindow : EditorWindow
	{
		private readonly List<UMAWardrobeRecipe> _recipes = new List<UMAWardrobeRecipe>();
		private DefaultAsset _destFolder;
		private string _destFolderPath;
		private Vector2 _scroll;

		public static void Open(List<UMAWardrobeRecipe> recipes)
		{
			var window = GetWindow<UmaConsolidateTexturesWindow>(true, "Consolidate Textures", true);
			window.minSize = new Vector2(520f, 180f);
			window._recipes.Clear();
			if (recipes != null)
			{
				window._recipes.AddRange(recipes);
			}
			window._destFolder = null;
			window._destFolderPath = "";
			window.ShowUtility();
			window.Focus();
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Consolidate Textures", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Copies textures referenced by overlays in the selected UMAWardrobeRecipe assets into a chosen folder.", MessageType.Info);
			EditorGUILayout.Space(6);

			_scroll = EditorGUILayout.BeginScrollView(_scroll);
			EditorGUILayout.LabelField("Destination Folder", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			_destFolder = (DefaultAsset)EditorGUILayout.ObjectField(_destFolder, typeof(DefaultAsset), false);
			if (EditorGUI.EndChangeCheck())
			{
				_destFolderPath = _destFolder != null ? AssetDatabase.GetAssetPath(_destFolder) : "";
				if (!string.IsNullOrEmpty(_destFolderPath) && !AssetDatabase.IsValidFolder(_destFolderPath))
				{
					_destFolder = null;
					_destFolderPath = "";
				}
			}

			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.TextField("Path", _destFolderPath);
			}
			EditorGUILayout.EndScrollView();

			EditorGUILayout.Space(8);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_destFolderPath) || _recipes.Count == 0))
			{
				if (GUILayout.Button("Move Textures", GUILayout.Width(140), GUILayout.Height(28)))
				{
					CopyTextures();
				}
			}
			if (GUILayout.Button("Cancel", GUILayout.Width(140), GUILayout.Height(28)))
			{
				Close();
			}
			EditorGUILayout.EndHorizontal();
		}

		private void CopyTextures()
		{
			var textures = new HashSet<string>();
			var overlaysToSave = new HashSet<UMA.OverlayDataAsset>();
			var overlayTextureRefs = new Dictionary<string, List<(UMA.OverlayDataAsset overlay, int index)>>(System.StringComparer.OrdinalIgnoreCase);
			try
			{
				for (int i = 0; i < _recipes.Count; i++)
				{
					var recipe = _recipes[i];
					if (recipe == null)
					{
						continue;
					}

					EditorUtility.DisplayProgressBar("Consolidate Textures", "Scanning recipes...", Mathf.Clamp01((float)i / Mathf.Max(1, _recipes.Count)));

					var umaRecipe = new UMA.UMAData.UMARecipe();
					recipe.Load(umaRecipe, true);
					if (umaRecipe.slotDataList == null)
					{
						continue;
					}

					for (int s = 0; s < umaRecipe.slotDataList.Length; s++)
					{
						var slot = umaRecipe.slotDataList[s];
						if (slot == null)
						{
							continue;
						}

						for (int o = 0; o < slot.OverlayCount; o++)
						{
							var overlay = slot.GetOverlay(o);
							if (overlay == null)
							{
								continue;
							}
							var overlayAsset = overlay.asset;
							if (overlayAsset == null)
							{
								continue;
							}

							if (overlayAsset.textureList != null)
							{
								for (int t = 0; t < overlayAsset.textureList.Length; t++)
								{
									var tex = overlayAsset.textureList[t];
									if (tex == null)
									{
										continue;
									}
									string srcPath = AssetDatabase.GetAssetPath(tex);
									if (!string.IsNullOrEmpty(srcPath))
									{
										textures.Add(srcPath);
										if (!overlayTextureRefs.TryGetValue(srcPath, out var refsForPath) || refsForPath == null)
										{
											refsForPath = new List<(UMA.OverlayDataAsset overlay, int index)>();
											overlayTextureRefs[srcPath] = refsForPath;
										}
										refsForPath.Add((overlayAsset, t));
									}
								}
							}

							if (overlayAsset.alphaMask != null)
							{
								string srcPath = AssetDatabase.GetAssetPath(overlayAsset.alphaMask);
								if (!string.IsNullOrEmpty(srcPath))
								{
									textures.Add(srcPath);
								}
							}
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (textures.Count == 0)
			{
				EditorUtility.DisplayDialog("Consolidate Textures", "No textures were found in overlays for the selected recipes.", "OK");
				return;
			}

			int moved = 0;
			int total = textures.Count;
			int index = 0;
			try
			{
				foreach (string srcPath in textures)
				{
					index++;
					EditorUtility.DisplayProgressBar("Consolidate Textures", "Moving textures...", Mathf.Clamp01((float)index / Mathf.Max(1, total)));
					if (string.IsNullOrEmpty(srcPath))
					{
						continue;
					}

					// Skip textures that are already inside the destination folder
					if (!string.IsNullOrEmpty(_destFolderPath) && srcPath.StartsWith(_destFolderPath + "/", System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					string fileName = Path.GetFileName(srcPath);
					if (string.IsNullOrEmpty(fileName))
					{
						continue;
					}

					string destPathAlready = _destFolderPath + "/" + fileName;
					var existingDestTexture = AssetDatabase.LoadAssetAtPath<Texture>(destPathAlready);
					if (existingDestTexture != null)
					{
						if (overlayTextureRefs.TryGetValue(srcPath, out var refsForPath) && refsForPath != null)
						{
							for (int r = 0; r < refsForPath.Count; r++)
							{
								var (overlay, texIndex) = refsForPath[r];
								if (overlay == null)
								{
									continue;
								}
								var list = overlay.textureList;
								if (list == null || texIndex < 0 || texIndex >= list.Length)
								{
									continue;
								}
								if (list[texIndex] == existingDestTexture)
								{
									continue;
								}
								Undo.RecordObject(overlay, "Relink overlay texture");
								list[texIndex] = existingDestTexture;
								overlay.textureList = list;
								EditorUtility.SetDirty(overlay);
								overlaysToSave.Add(overlay);
							}
						}
						continue;
					}

					string destPath = AssetDatabase.GenerateUniqueAssetPath(destPathAlready);
					string moveError = AssetDatabase.MoveAsset(srcPath, destPath);
					if (!string.IsNullOrEmpty(moveError))
					{
						continue;
					}

					moved++;
					var movedTexture = AssetDatabase.LoadAssetAtPath<Texture>(destPath);
					if (movedTexture == null)
					{
						continue;
					}

					if (overlayTextureRefs.TryGetValue(srcPath, out var refsAfterMove) && refsAfterMove != null)
					{
						for (int r = 0; r < refsAfterMove.Count; r++)
						{
							var (overlay, texIndex) = refsAfterMove[r];
							if (overlay == null)
							{
								continue;
							}
							var list = overlay.textureList;
							if (list == null || texIndex < 0 || texIndex >= list.Length)
							{
								continue;
							}
							if (list[texIndex] == movedTexture)
							{
								continue;
							}
							Undo.RecordObject(overlay, "Relink overlay texture");
							list[texIndex] = movedTexture;
							overlay.textureList = list;
							EditorUtility.SetDirty(overlay);
							overlaysToSave.Add(overlay);
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			if (overlaysToSave.Count > 0)
			{
				AssetDatabase.SaveAssets();
			}
			AssetDatabase.Refresh();
			EditorUtility.DisplayDialog("Consolidate Textures", "Moved " + moved + " texture asset(s) into: " + _destFolderPath + "\nUpdated overlays: " + overlaysToSave.Count, "OK");
		}
	}

		[MenuItem("Assets/UMA/Add Race(s) to Selected Recipes", true)]
		private static bool AddRacesToSelectedRecipesMenu_Validate()
		{
			return GetSelectedWardrobeRecipes().Count > 0;
		}

		private static List<UMAWardrobeRecipe> GetSelectedWardrobeRecipes()
		{
			var selected = Selection.GetFiltered(typeof(UMAWardrobeRecipe), SelectionMode.Assets);
			var recipes = new List<UMAWardrobeRecipe>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var r = selected[i] as UMAWardrobeRecipe;
				if (r != null)
				{
					recipes.Add(r);
				}
			}
			return recipes;
		}

		private static List<UMA.OverlayDataAsset> GetSelectedOverlays()
		{
			var selected = Selection.GetFiltered(typeof(UMA.OverlayDataAsset), SelectionMode.Assets);
			var overlays = new List<UMA.OverlayDataAsset>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var o = selected[i] as UMA.OverlayDataAsset;
				if (o != null)
				{
					overlays.Add(o);
				}
			}
			return overlays;
		}

		private static List<Texture2D> GetSelectedTextures()
		{
			var selected = Selection.GetFiltered(typeof(Texture2D), SelectionMode.Assets);
			var textures = new List<Texture2D>(selected.Length);
			for (int i = 0; i < selected.Length; i++)
			{
				var tex = selected[i] as Texture2D;
				if (tex != null)
				{
					textures.Add(tex);
				}
			}
			return textures;
		}

		internal class UmaConvertTexturesToPngWindow : EditorWindow
		{
			private class TextureEntry
			{
				public Texture2D Texture;
				public string AssetPath;
				public bool Selected;
				public long BeforeBytes;
				public long AfterBytes;
			}

			private readonly List<TextureEntry> _entries = new List<TextureEntry>();
			private readonly List<string> _log = new List<string>();
			private Vector2 _leftScroll;
			private Vector2 _rightScroll;
			private bool _isRunning;
			private System.Collections.IEnumerator _convertRoutine;
			private long _beforeTotalBytes;
			private long _afterTotalBytes;
			private bool _overwriteExistingPng = true;
			private bool _keepOriginalFiles;
			private bool _replaceInIndexedOverlays;

			public static void Open(List<Texture2D> textures)
			{
				var window = GetWindow<UmaConvertTexturesToPngWindow>(true, "Convert selected textures to PNG", true);
				window.minSize = new Vector2(820f, 360f);
				window._entries.Clear();
				window._log.Clear();
				window._beforeTotalBytes = 0;
				window._afterTotalBytes = 0;
				window._isRunning = false;
				window._convertRoutine = null;
				if (textures != null)
				{
					window.LoadTextures(textures);
				}
				window.ShowUtility();
				window.Focus();
			}

			private void LoadTextures(List<Texture2D> textures)
			{
				for (int i = 0; i < textures.Count; i++)
				{
					var tex = textures[i];
					if (tex == null)
					{
						continue;
					}

					string path = AssetDatabase.GetAssetPath(tex);
					long size = GetFileSize(path);
					_entries.Add(new TextureEntry
					{
						Texture = tex,
						AssetPath = path,
						Selected = true,
						BeforeBytes = size,
						AfterBytes = size
					});
				}
				RecalculateTotals();
			}

			private void OnDisable()
			{
				StopConversion();
			}

			private void OnGUI()
			{
				EditorGUILayout.LabelField("Convert selected textures to PNG", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Converts selected Texture2D assets to PNG using Unity's PNG encoder (RGBA32).", MessageType.Info);
				EditorGUILayout.Space(6);

				EditorGUILayout.LabelField("PNG Options", EditorStyles.boldLabel);
				using (new EditorGUI.DisabledScope(_isRunning))
				{
					_overwriteExistingPng = EditorGUILayout.ToggleLeft("Overwrite existing .png", _overwriteExistingPng);
					_keepOriginalFiles = EditorGUILayout.ToggleLeft("Keep original file (create *_converted.png)", _keepOriginalFiles);
					_replaceInIndexedOverlays = EditorGUILayout.ToggleLeft("Replace references in indexed overlays", _replaceInIndexedOverlays);
					EditorGUILayout.HelpBox("Unity's built-in encoder does not expose PNG options like interlacing or compression level.", MessageType.None);
				}
				EditorGUILayout.Space(6);

				EditorGUILayout.BeginHorizontal();
				DrawTextureList();
				GUILayout.Space(10);
				DrawLogList();
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.Space(6);
				EditorGUILayout.BeginHorizontal();
				GUILayout.FlexibleSpace();
				using (new EditorGUI.DisabledScope(_isRunning || _entries.Count == 0))
				{
					if (GUILayout.Button("Convert selected textures to PNG", GUILayout.Width(260), GUILayout.Height(28)))
					{
						StartConversion();
					}
				}
				using (new EditorGUI.DisabledScope(!_isRunning))
				{
					if (GUILayout.Button("Stop", GUILayout.Width(100), GUILayout.Height(28)))
					{
						StopConversion();
					}
				}
				if (GUILayout.Button("Close", GUILayout.Width(100), GUILayout.Height(28)))
				{
					Close();
				}
				EditorGUILayout.EndHorizontal();
			}

			private void DrawTextureList()
			{
				EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.58f));
				EditorGUILayout.LabelField("Selected Texture2D", EditorStyles.boldLabel);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("All", GUILayout.Width(70)))
				{
					SetAllSelections(true);
					RecalculateTotals();
				}
				if (GUILayout.Button("None", GUILayout.Width(70)))
				{
					SetAllSelections(false);
					RecalculateTotals();
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.Space(4);

				_leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUILayout.ExpandHeight(true));
				for (int i = 0; i < _entries.Count; i++)
				{
					var entry = _entries[i];
					if (entry == null)
					{
						continue;
					}

					EditorGUILayout.BeginHorizontal();
					bool newSelected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(18));
					if (newSelected != entry.Selected)
					{
						entry.Selected = newSelected;
						RecalculateTotals();
					}
					EditorGUILayout.ObjectField(entry.Texture, typeof(Texture2D), false);
					GUILayout.Label(FormatBytes(entry.AfterBytes), GUILayout.Width(90));
					EditorGUILayout.EndHorizontal();
				}
				EditorGUILayout.EndScrollView();

				EditorGUILayout.Space(4);
				EditorGUILayout.LabelField("Before total (selected): " + FormatBytes(_beforeTotalBytes));
				EditorGUILayout.LabelField("After total (selected): " + FormatBytes(_afterTotalBytes));
				EditorGUILayout.EndVertical();
			}

			private void DrawLogList()
			{
				EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
				EditorGUILayout.LabelField("Conversion Log", EditorStyles.boldLabel);
				_rightScroll = EditorGUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));
				for (int i = 0; i < _log.Count; i++)
				{
					EditorGUILayout.LabelField(_log[i], EditorStyles.wordWrappedLabel);
				}
				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();
			}

			private void SetAllSelections(bool value)
			{
				for (int i = 0; i < _entries.Count; i++)
				{
					var entry = _entries[i];
					if (entry != null)
					{
						entry.Selected = value;
					}
				}
			}

			private void StartConversion()
			{
				if (_isRunning)
				{
					return;
				}
				_log.Clear();
				RecalculateTotals();
				_convertRoutine = ConvertTextures();
				_isRunning = true;
				EditorApplication.update += UpdateConversion;
			}

			private void StopConversion()
			{
				if (!_isRunning)
				{
					return;
				}
				EditorApplication.update -= UpdateConversion;
				_convertRoutine = null;
				_isRunning = false;
			}

			private void UpdateConversion()
			{
				if (_convertRoutine == null)
				{
					StopConversion();
					return;
				}
				if (!_convertRoutine.MoveNext())
				{
					StopConversion();
					RecalculateTotals();
					return;
				}
				Repaint();
			}

			private System.Collections.IEnumerator ConvertTextures()
			{
				for (int i = 0; i < _entries.Count; i++)
				{
					var entry = _entries[i];
					if (entry == null || !entry.Selected || entry.Texture == null)
					{
						continue;
					}

					string srcPath = entry.AssetPath;
					if (string.IsNullOrEmpty(srcPath))
					{
						LogLine("Skipped: Missing asset path for " + entry.Texture.name);
						yield return null;
						continue;
					}

					string fileName = Path.GetFileNameWithoutExtension(srcPath);
					string folder = Path.GetDirectoryName(srcPath) ?? string.Empty;
					string destPath;
					if (_keepOriginalFiles)
					{
						destPath = Path.Combine(folder, fileName + "_converted.png");
					}
					else
					{
						destPath = Path.Combine(folder, fileName + ".png");
					}
					destPath = CustomAssetUtility.UnityFriendlyPath(destPath);
					if (!_overwriteExistingPng && File.Exists(destPath))
					{
						LogLine("Skipped (exists): " + destPath);
						yield return null;
						continue;
					}

					LogLine("Starting: " + srcPath);
					yield return null;

					Texture2D readable = null;
					Texture2D rgba32 = null;
					byte[] data = null;
					string error = null;
					try
					{
						readable = GetReadableTexture(entry.Texture, false);
						if (readable == null)
						{
							error = "could not read";
						}
						else
						{
							rgba32 = new Texture2D(readable.width, readable.height, TextureFormat.RGBA32, false, false);
							rgba32.SetPixels32(readable.GetPixels32());
							rgba32.Apply(false, false);
							data = rgba32.EncodeToPNG();
							if (data == null || data.Length == 0)
							{
								error = "PNG encode returned empty data";
							}
						}
					}
					catch (System.Exception ex)
					{
						error = ex.Message;
					}
					finally
					{
						if (readable != null)
						{
							UnityEngine.Object.DestroyImmediate(readable);
						}
						if (rgba32 != null)
						{
							UnityEngine.Object.DestroyImmediate(rgba32);
						}
					}

					if (!string.IsNullOrEmpty(error))
					{
						LogLine("Failed: " + srcPath + " (" + error + ")");
						yield return null;
						continue;
					}

					File.WriteAllBytes(destPath, data);
					AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);
					var pngTex = AssetDatabase.LoadAssetAtPath<Texture2D>(destPath);
					if (pngTex != null)
					{
						if (_replaceInIndexedOverlays)
						{
							ReplaceInIndexedOverlays(entry.Texture, pngTex);
						}
						entry.Texture = pngTex;
						entry.AssetPath = destPath;
					}
					entry.AfterBytes = GetFileSize(destPath);
					LogLine("Done: " + destPath);
					yield return null;
				}

				RecalculateTotals();
			}

			private void ReplaceInIndexedOverlays(Texture2D oldTexture, Texture2D newTexture)
			{
				if (oldTexture == null || newTexture == null)
				{
					return;
				}

				var idx = UMA.UMAAssetIndexer.Instance;
				if (idx == null)
				{
					LogLine("Overlay relink skipped: UMAAssetIndexer not ready.");
					return;
				}

				var overlays = idx.GetAssetItems<UMA.OverlayDataAsset>();
				if (overlays == null || overlays.Count == 0)
				{
					return;
				}

				int updated = 0;
				for (int i = 0; i < overlays.Count; i++)
				{
					var ai = overlays[i];
					if (ai == null)
					{
						continue;
					}

					var overlay = ai.Item as UMA.OverlayDataAsset;
					if (overlay == null)
					{
						continue;
					}

					var list = overlay.textureList;
					if (list == null || list.Length == 0)
					{
						continue;
					}

					bool changed = false;
					for (int t = 0; t < list.Length; t++)
					{
						if (list[t] == oldTexture)
						{
							list[t] = newTexture;
							changed = true;
						}
					}

					if (!changed)
					{
						continue;
					}

					Undo.RecordObject(overlay, "Replace overlay texture");
					overlay.textureList = list;

					// Best-effort: update textureNames for any slots matching the old texture name
					var names = overlay.textureNames;
					if (names != null)
					{
						for (int n = 0; n < names.Length; n++)
						{
							if (!string.IsNullOrEmpty(names[n]) && names[n] == oldTexture.name)
							{
								names[n] = newTexture.name;
							}
						}
						overlay.textureNames = names;
					}

					EditorUtility.SetDirty(overlay);
					updated++;
				}

				if (updated > 0)
				{
					AssetDatabase.SaveAssets();
					LogLine("Overlay relink updated overlays: " + updated);
				}
			}

			private void LogLine(string message)
			{
				_log.Add(message);
				_rightScroll.y = float.MaxValue;
			}

			private void RecalculateTotals()
			{
				long beforeTotalSelected = 0;
				long afterTotalSelected = 0;
				for (int i = 0; i < _entries.Count; i++)
				{
					var entry = _entries[i];
					if (entry == null || !entry.Selected)
					{
						continue;
					}
					beforeTotalSelected += entry.BeforeBytes;
					afterTotalSelected += entry.AfterBytes;
				}
				_beforeTotalBytes = beforeTotalSelected;
				_afterTotalBytes = afterTotalSelected;
			}

			private static long GetFileSize(string assetPath)
			{
				if (string.IsNullOrEmpty(assetPath))
				{
					return 0;
				}
				try
				{
					if (File.Exists(assetPath))
					{
						return new FileInfo(assetPath).Length;
					}
				}
				catch
				{
				}
				return 0;
			}

			private static string FormatBytes(long bytes)
			{
				if (bytes < 1024)
				{
					return bytes + " B";
				}
				float kb = bytes / 1024f;
				if (kb < 1024f)
				{
					return kb.ToString("F1") + " KB";
				}
				float mb = kb / 1024f;
				return mb.ToString("F1") + " MB";
			}
		}

	internal class UmaExamineOverlaysWindow : EditorWindow
	{
		private readonly List<UMA.OverlayDataAsset> _overlays = new List<UMA.OverlayDataAsset>();
		private readonly List<UMA.OverlayDataAsset> _filteredOverlays = new List<UMA.OverlayDataAsset>();
		private UMA.OverlayDataAsset _selectedOverlay;
		private Vector2 _leftScroll;
		private Vector2 _rightScroll;
		private static readonly GUIContent _completeLabel = new GUIContent("Complete");
		private static readonly GUIContent _incompleteLabel = new GUIContent("Incomplete");
		private enum OverlayFilter { All, Complete, Incomplete }
		private OverlayFilter _filter = OverlayFilter.All;

		public static void Open(List<UMA.OverlayDataAsset> overlays)
		{
			var window = GetWindow<UmaExamineOverlaysWindow>(false, "Examine Overlays", true);
			window.minSize = new Vector2(860f, 420f);
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
				bool isComplete = IsComplete(overlay);
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

			EditorGUILayout.BeginHorizontal();
			DrawOverlayList();
			GUILayout.Space(10);
			DrawOverlayDetails();
			EditorGUILayout.EndHorizontal();
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
				GUILayout.Label(IsComplete(overlay) ? _completeLabel : _incompleteLabel, GUILayout.Width(78));
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

		private static bool IsComplete(UMA.OverlayDataAsset overlay)
		{
			if (overlay == null)
			{
				return false;
			}
			var list = overlay.textureList;
			if (list == null || list.Length == 0)
			{
				return false;
			}
			for (int i = 0; i < list.Length; i++)
			{
				if (list[i] == null)
				{
					return false;
				}
			}
			return true;
		}


	}

	internal class ExamineWearables : EditorWindow
	{
		private readonly List<UMAWardrobeRecipe> _recipes = new List<UMAWardrobeRecipe>();
		private bool[] _recipeSelected = new bool[0];
		private Vector2 _recipesScroll;

		private readonly List<string> _slots = new List<string>();
		private int _selectedSlotIndex = -1;
		private Vector2 _slotsScroll;

		private UMAMaterial _targetMaterial;
		private string _matchText = "";
		private enum MatchMode
		{
			Contains = 0,
			StartsWith = 1,
			EndsWith = 2,
		}
		private MatchMode _matchMode = MatchMode.Contains;
		private static GUIContent _inspectContent;
		private static GUIContent InspectContent
		{
			get
			{
				if (_inspectContent == null)
				{
					var icon = EditorGUIUtility.FindTexture("ViewToolOrbit");
					_inspectContent = new GUIContent("", icon, "Inspect");
				}
				return _inspectContent;
			}
		}

		public static void Open(List<UMAWardrobeRecipe> recipes)
		{
			var window = GetWindow<ExamineWearables>(false, "Examine Wearables", true);
			window.minSize = new Vector2(700f, 420f);
			window._recipes.Clear();
			if (recipes != null)
			{
				window._recipes.AddRange(recipes);
			}
			window._recipeSelected = new bool[window._recipes.Count];
			window.RebuildSlots();
			window.Show();
			window.Focus();
		}

		private void OnSelectionChange()
		{
			// Intentionally no-op: we only refresh the window contents when the user presses Refresh.
		}

		private void RebuildSlots()
		{
			_slots.Clear();
			var slotSet = new HashSet<string>();

			var idx = UMAAssetIndexer.Instance;
			if (idx == null)
			{
				_selectedSlotIndex = -1;
				return;
			}

			for (int i = 0; i < _recipes.Count; i++)
			{
				var recipe = _recipes[i];
				if (recipe == null || recipe.compatibleRaces == null)
				{
					continue;
				}

				for (int r = 0; r < recipe.compatibleRaces.Count; r++)
				{
					string raceName = recipe.compatibleRaces[r];
					if (string.IsNullOrEmpty(raceName))
					{
						continue;
					}

					RaceData race = null;
					try
					{
						race = idx.GetAsset<RaceData>(raceName);
					}
					catch
					{
						race = null;
					}

					if (race == null || race.wardrobeSlots == null)
					{
						continue;
					}

					for (int s = 0; s < race.wardrobeSlots.Count; s++)
					{
						string slot = race.wardrobeSlots[s];
						if (!string.IsNullOrEmpty(slot))
						{
							slotSet.Add(slot);
						}
					}
				}
			}

			_slots.AddRange(slotSet);
			_slots.Sort(System.StringComparer.OrdinalIgnoreCase);
			if (_selectedSlotIndex >= _slots.Count)
			{
				_selectedSlotIndex = -1;
			}
		}

		private void OnGUI()
		{
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Label("Examine Wearables", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
			{
				RebuildSlots();
			}
			EditorGUILayout.EndHorizontal();

			if (_recipes.Count == 0)
			{
				EditorGUILayout.HelpBox("Select one or more UMAWardrobeRecipe assets in the Project window.", MessageType.Info);
				EditorGUILayout.Space(8);
				if (GUILayout.Button("Close", GUILayout.Height(26)))
				{
					Close();
				}
				return;
			}

			EditorGUILayout.Space(6);
			DrawUtilitiesPanel();
			EditorGUILayout.BeginHorizontal();
			DrawRecipesColumn();
			GUILayout.Space(10);
			DrawSlotsColumn();
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(10);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(!HasAnyRecipeChecked() || _selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count))
			{
				if (GUILayout.Button("Assign", GUILayout.Width(120), GUILayout.Height(28)))
				{
					AssignSelectedSlot();
				}
			}
			if (GUILayout.Button("Close", GUILayout.Width(120), GUILayout.Height(28)))
			{
				Close();
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawUtilitiesPanel()
		{
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			EditorGUILayout.LabelField("Utilities", EditorStyles.boldLabel);
			EditorGUILayout.LabelField("Set UMAMaterial on slots and overlays based on overlay texture[0] name matching.", EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
			_targetMaterial = (UMAMaterial)EditorGUILayout.ObjectField("UMAMaterial", _targetMaterial, typeof(UMAMaterial), false);
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			_matchMode = (MatchMode)EditorGUILayout.EnumPopup("Match Mode", _matchMode);
			_matchText = EditorGUILayout.TextField("Texture[0] Match Text", _matchText);
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			using (new EditorGUI.DisabledScope(_targetMaterial == null || string.IsNullOrEmpty(_matchText) || !HasAnyRecipeChecked()))
			{
				if (GUILayout.Button("Process", GUILayout.Width(140), GUILayout.Height(24)))
				{
					ProcessMaterialUpdates();
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space(6);
		}

		private bool DoesOverlayMatch(UMA.OverlayDataAsset overlayAsset)
		{
			if (overlayAsset == null)
			{
				return false;
			}
			var texList = overlayAsset.textureList;
			if (texList == null || texList.Length == 0 || texList[0] == null)
			{
				return false;
			}
			string texName = texList[0].name;
			if (string.IsNullOrEmpty(texName))
			{
				return false;
			}
			if (string.IsNullOrEmpty(_matchText))
			{
				return false;
			}

			if (_matchMode == MatchMode.StartsWith)
			{
				return texName.StartsWith(_matchText, System.StringComparison.OrdinalIgnoreCase);
			}
			if (_matchMode == MatchMode.EndsWith)
			{
				return texName.EndsWith(_matchText, System.StringComparison.OrdinalIgnoreCase);
			}
			return texName.IndexOf(_matchText, System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private void ProcessMaterialUpdates()
		{
			if (_targetMaterial == null)
			{
				EditorUtility.DisplayDialog("Process", "Select an UMAMaterial.", "OK");
				return;
			}
			if (string.IsNullOrEmpty(_matchText))
			{
				EditorUtility.DisplayDialog("Process", "Enter text to match against overlay texture[0].name.", "OK");
				return;
			}

			var changedSlots = new HashSet<UMA.SlotDataAsset>();
			var changedOverlays = new HashSet<UMA.OverlayDataAsset>();
			int processedRecipes = 0;
			int matchedOverlays = 0;

			try
			{
				for (int i = 0; i < _recipes.Count; i++)
				{
					if (i >= _recipeSelected.Length || !_recipeSelected[i])
					{
						continue;
					}
					var recipe = _recipes[i];
					if (recipe == null)
					{
						continue;
					}
					processedRecipes++;
					EditorUtility.DisplayProgressBar("Process", "Scanning recipes...", Mathf.Clamp01((float)processedRecipes / Mathf.Max(1, CountCheckedRecipes())));

					var umaRecipe = new UMA.UMAData.UMARecipe();
					recipe.Load(umaRecipe, true);
					if (umaRecipe.slotDataList == null)
					{
						continue;
					}

					for (int s = 0; s < umaRecipe.slotDataList.Length; s++)
					{
						var slot = umaRecipe.slotDataList[s];
						if (slot == null)
						{
							continue;
						}
						var slotAsset = slot.asset;
						if (slotAsset == null)
						{
							continue;
						}

						bool anyOverlayMatchedOnSlot = false;
						for (int o = 0; o < slot.OverlayCount; o++)
						{
							var overlay = slot.GetOverlay(o);
							if (overlay == null)
							{
								continue;
							}
							var overlayAsset = overlay.asset;
							if (overlayAsset == null)
							{
								continue;
							}
							if (!DoesOverlayMatch(overlayAsset))
							{
								continue;
							}
							matchedOverlays++;

							if (overlayAsset.material != _targetMaterial)
							{
								Undo.RecordObject(overlayAsset, "Update Overlay UMAMaterial");
								overlayAsset.material = _targetMaterial;
								overlayAsset.materialName = _targetMaterial != null ? _targetMaterial.name : "";
								changedOverlays.Add(overlayAsset);
							}

							anyOverlayMatchedOnSlot = true;
						}

						if (anyOverlayMatchedOnSlot && slotAsset.material != _targetMaterial)
						{
							Undo.RecordObject(slotAsset, "Update Slot UMAMaterial");
							slotAsset.material = _targetMaterial;
							slotAsset.materialName = _targetMaterial != null ? _targetMaterial.name : "";
							changedSlots.Add(slotAsset);
						}
					}
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			foreach (var overlayAsset in changedOverlays)
			{
				if (overlayAsset != null)
				{
					EditorUtility.SetDirty(overlayAsset);
				}
			}
			foreach (var slotAsset in changedSlots)
			{
				if (slotAsset != null)
				{
					EditorUtility.SetDirty(slotAsset);
				}
			}
			if (changedOverlays.Count > 0 || changedSlots.Count > 0)
			{
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog("Process", "Matched overlays: " + matchedOverlays + "\nUpdated overlays: " + changedOverlays.Count + "\nUpdated slots: " + changedSlots.Count, "OK");
		}

		private int CountCheckedRecipes()
		{
			int count = 0;
			for (int i = 0; i < _recipeSelected.Length; i++)
			{
				if (_recipeSelected[i])
				{
					count++;
				}
			}
			return count;
		}

			private void DrawRecipesColumn()
			{
				EditorGUILayout.BeginVertical(GUILayout.Width(position.width * 0.62f));
				EditorGUILayout.LabelField("Wardrobe Recipes", EditorStyles.boldLabel);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("All", GUILayout.Width(60)))
				{
					for (int i = 0; i < _recipeSelected.Length; i++) _recipeSelected[i] = true;
				}
				if (GUILayout.Button("None", GUILayout.Width(60)))
				{
					for (int i = 0; i < _recipeSelected.Length; i++) _recipeSelected[i] = false;
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.Space(4);
				_recipesScroll = EditorGUILayout.BeginScrollView(_recipesScroll, GUILayout.ExpandHeight(true));
				EditorGUILayout.BeginVertical();
				for (int i = 0; i < _recipes.Count; i++)
				{
					var recipe = _recipes[i];
					if (recipe == null) continue;

					EditorGUILayout.BeginHorizontal();
					_recipeSelected[i] = EditorGUILayout.Toggle(_recipeSelected[i], GUILayout.Width(18));
					if (GUILayout.Button(InspectContent, EditorStyles.miniButton, GUILayout.Width(22), GUILayout.Height(18)))
					{
						UMA.InspectorUtlity.InspectTarget(recipe);
					}
					EditorGUILayout.ObjectField(recipe, typeof(UMAWardrobeRecipe), false);
					string slot = string.IsNullOrEmpty(recipe.wardrobeSlot) ? "<Unassigned>" : recipe.wardrobeSlot;
					GUILayout.Label(slot, GUILayout.Width(160));
					if (!RecipeHasAnySlots(recipe))
					{
						GUILayout.Label("Warning - no slots", EditorStyles.miniLabel, GUILayout.Width(120));
					}
					else
					{
						GUILayout.Label("Slots look OK", EditorStyles.miniLabel, GUILayout.Width(120));
					}
					EditorGUILayout.EndHorizontal();
				}
				EditorGUILayout.EndVertical();
				EditorGUILayout.EndScrollView();
				EditorGUILayout.EndVertical();
			}

		private static bool RecipeHasAnySlots(UMAWardrobeRecipe recipe)
		{
			if (recipe == null)
			{
				return false;
			}
			try
			{
				var umaRecipe = new UMA.UMAData.UMARecipe();
				recipe.Load(umaRecipe, true);
				if (umaRecipe.slotDataList == null)
				{
					return false;
				}
				for (int i = 0; i < umaRecipe.slotDataList.Length; i++)
				{
					if (umaRecipe.slotDataList[i] != null)
					{
						return true;
					}
				}
				return false;
			}
			catch
			{
				return false;
			}
		}

		private void DrawSlotsColumn()
		{
			EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
			EditorGUILayout.LabelField("Wardrobe Slots (union of slots across compatible races)", EditorStyles.boldLabel);
			if (_slots.Count == 0)
			{
				EditorGUILayout.HelpBox("No wardrobe slots found from compatible races on the selected recipes.", MessageType.Warning);
				EditorGUILayout.EndVertical();
				return;
			}

			_slotsScroll = EditorGUILayout.BeginScrollView(_slotsScroll, GUILayout.ExpandHeight(true));
			for (int i = 0; i < _slots.Count; i++)
			{
				bool isSelected = (_selectedSlotIndex == i);
				bool newSelected = EditorGUILayout.ToggleLeft(_slots[i], isSelected);
				if (newSelected != isSelected)
				{
					_selectedSlotIndex = newSelected ? i : -1;
				}
			}
			EditorGUILayout.EndScrollView();
			EditorGUILayout.EndVertical();
		}

		private bool HasAnyRecipeChecked()
		{
			for (int i = 0; i < _recipeSelected.Length; i++)
			{
				if (_recipeSelected[i]) return true;
			}
			return false;
		}

		private void AssignSelectedSlot()
		{
			if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count)
			{
				EditorUtility.DisplayDialog("Assign", "Select exactly one wardrobe slot.", "OK");
				return;
			}

			string slot = _slots[_selectedSlotIndex];
			if (string.IsNullOrEmpty(slot))
			{
				EditorUtility.DisplayDialog("Assign", "Selected wardrobe slot is empty.", "OK");
				return;
			}

			int updated = 0;
			for (int i = 0; i < _recipes.Count; i++)
			{
				if (i >= _recipeSelected.Length || !_recipeSelected[i])
				{
					continue;
				}
				var recipe = _recipes[i];
				if (recipe == null)
				{
					continue;
				}

				if (recipe.wardrobeSlot == slot)
				{
					continue;
				}

				Undo.RecordObject(recipe, "Assign wardrobe slot");
				recipe.wardrobeSlot = slot;
				EditorUtility.SetDirty(recipe);
				updated++;
			}

			if (updated > 0)
			{
				AssetDatabase.SaveAssets();
			}

			EditorUtility.DisplayDialog("Assign", "Updated " + updated + " recipe(s).", "OK");
		}
	}

		public static void ConvertToNonUMA(GameObject baseObject, UMAAvatarBase avatar, string Folder, bool ConvertNormalMaps, string CharName, bool AddStandaloneDNA, bool replaceExisting)
		{
			Folder = Folder + "/" + CharName;

			if (!System.IO.Directory.Exists(Folder))
			{
				System.IO.Directory.CreateDirectory(Folder);
			}

			SkinnedMeshRenderer[] renderers = avatar.umaData.GetRenderers();
			int meshno = 0;
			foreach (SkinnedMeshRenderer smr in renderers)
			{
				Material[] omats = smr.sharedMaterials;
                Material[] mats = new Material[omats.Length];
                for (int i = 0; i < omats.Length; i++)
                {
                    mats[i] = new Material(omats[i]);
                }

                int Material = 0;
				foreach (Material m in mats)
				{
					// get each texture.
					// if the texture has been generated (has no path) then we need to convert to Texture2D (if needed) save that asset.
					// update the material with that material.
					List<Texture> allTexture = new List<Texture>();
					Shader shader = m.shader;
					for (int i = 0; i < shader.GetPropertyCount(); i++)
					{
						if (shader.GetPropertyType(i) == ShaderPropertyType.Texture)
						{
							string propertyName = shader.GetPropertyName(i);
							Texture texture = m.GetTexture(propertyName);
							if (texture is Texture2D || texture is RenderTexture)
							{
								string path = AssetDatabase.GetAssetPath(texture.GetInstanceID());
								if (string.IsNullOrEmpty(path))
								{
									bool isNormal = (propertyName.ToLower().Contains("bumpmap") || propertyName.ToLower().Contains("normal"));

                                    if (ConvertNormalMaps)
									{
										if (isNormal)
										{
											texture = sconvertNormalMap(texture);
										}
									}
									string texName = Path.Combine(Folder, CharName + "_Mat_" + Material + propertyName + ".png");
									if (texture is RenderTexture)
                                    {
										Debug.Log("Saving Render Texture " + texName);
                                        LinearSave(texture as RenderTexture, texName,isNormal);
                                    }
                                    else
                                    {
										Debug.Log("Saving texture " + texName);
                                        SaveTexture2D(texture as Texture2D, texName, isNormal);
                                    }
                                    //SaveTexture(texture, texName);
									AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
									if (isNormal)
                                    {
										TextureImporter importer = (TextureImporter)TextureImporter.GetAtPath(texName);
										importer.isReadable = true;
										importer.textureType = TextureImporterType.NormalMap;
										importer.maxTextureSize = 1024; // or whatever
										importer.textureCompression = TextureImporterCompression.CompressedHQ;
										EditorUtility.SetDirty(importer);
										importer.SaveAndReimport();
									}
                                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CustomAssetUtility.UnityFriendlyPath(texName));

                                    m.SetTexture(propertyName, tex);
								}
								else
								{
									m.SetTexture(propertyName, texture);
                                }
							}
						}
					}
					string matname = Folder + "/"+CharName+"_Mat_" + Material + ".mat"; 
                    CustomAssetUtility.SaveAsset<Material>(m, matname);
					Material++;
					// Save the material to disk?
					// update the SMR
				}

				string meshName = Folder + "/"+CharName+"_Mesh_" + meshno + ".asset";
				meshno++;
				// Save Mesh to disk.
				// smr.sharedMesh.Optimize(); This blows up some versions of Unity.
				CustomAssetUtility.SaveAsset<Mesh>(smr.sharedMesh, meshName);
				smr.sharedMaterials = mats;
				smr.materials = mats;
			}

            // save Animator Avatar.
            var animator = baseObject.GetComponent<Animator>();
            string avatarName = Folder + "/" + CharName + "_Avatar.asset";
            CustomAssetUtility.SaveAsset<Avatar>(animator.avatar, avatarName);


            if (replaceExisting)
			{
				DestroyImmediate(avatar);
				var lod = baseObject.GetComponent<UMASimpleLOD>();
				if (lod != null)
				{
					DestroyImmediate(lod);
				}

				if (AddStandaloneDNA)
				{
					UMAData uda = baseObject.GetComponent<UMAData>();
					StandAloneDNA sda = baseObject.AddComponent<UMA.StandAloneDNA>();
					sda.PackedDNA = UMAPackedRecipeBase.GetPackedDNA(uda._umaRecipe);
					if (avatar is DynamicCharacterAvatar)
					{
						DynamicCharacterAvatar avt = avatar as DynamicCharacterAvatar;
						sda.avatarDefinition = avt.GetAvatarDefinition(true);
					}
					sda.umaData = uda;
				}
				else
				{
					var ud = baseObject.GetComponent<UMAData>();
					if (ud != null)
					{
						DestroyImmediate(ud);
					}
				}
				var ue = baseObject.GetComponent<UMAExpressionPlayer>();
				if (ue != null)
				{
					DestroyImmediate(ue);
				}

				baseObject.name = CharName;
				string prefabName = Folder + "/" + CharName + ".prefab";
				prefabName = CustomAssetUtility.UnityFriendlyPath(prefabName);
				PrefabUtility.SaveAsPrefabAssetAndConnect(baseObject, prefabName, InteractionMode.AutomatedAction);
			}
			else
			{
                // Create a new GameObject to hold the converted avatar.
                GameObject newAvatar = GameObject.Instantiate(baseObject);
                var lod = newAvatar.GetComponent<UMASimpleLOD>();
                if (lod != null)
                {
                    DestroyImmediate(lod);
                }

                if (AddStandaloneDNA)
                {
                    UMAData uda = newAvatar.GetComponent<UMAData>();
                    StandAloneDNA sda = newAvatar.AddComponent<UMA.StandAloneDNA>();
                    sda.PackedDNA = UMAPackedRecipeBase.GetPackedDNA(uda._umaRecipe);
                    if (avatar is DynamicCharacterAvatar)
                    {
                        DynamicCharacterAvatar avt = avatar as DynamicCharacterAvatar;
                        sda.avatarDefinition = avt.GetAvatarDefinition(true);
                    }
                    sda.umaData = uda;
                }
                else
                {
                    var ud = newAvatar.GetComponent<UMAData>();
                    if (ud != null)
                    {
                        DestroyImmediate(ud);
                    }
                }
                var ue = newAvatar.GetComponent<UMAExpressionPlayer>();
                if (ue != null)
                {
                    DestroyImmediate(ue);
                }

                newAvatar.name = CharName;
                string prefabName = Folder + "/" + CharName + ".prefab";
                prefabName = CustomAssetUtility.UnityFriendlyPath(prefabName);
                PrefabUtility.SaveAsPrefabAssetAndConnect(newAvatar, prefabName, InteractionMode.AutomatedAction);
            }
        }


		[UnityEditor.MenuItem("GameObject/UMA/Save Atlas Textures")]
		[MenuItem("CONTEXT/DynamicCharacterAvatar/Save Selected Avatars generated textures to PNG", false, 10)]
		[MenuItem("UMA/Runtime/Save Selected Avatar Atlas Textures")]
		public static void SaveSelectedAvatarsPNG()
		{
			if (Selection.gameObjects.Length != 1)
			{
				EditorUtility.DisplayDialog("Notice", "Only one Avatar can be selected.", "OK");
				return;
			}

			var selectedTransform = Selection.gameObjects[0].transform;
			var avatar = selectedTransform.GetComponent<UMAAvatarBase>();

			if (avatar == null)
			{
				EditorUtility.DisplayDialog("Notice", "An Avatar must be selected to use this function", "OK");
				return;
			}

			SkinnedMeshRenderer smr = avatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
			if (smr == null)
			{
				EditorUtility.DisplayDialog("Warning", "Could not find SkinnedMeshRenderer in Avatar hierarchy", "OK");
				return;
			}

			string path = EditorUtility.SaveFilePanelInProject("Save Texture(s)", "Texture.png", "png", "Base Filename to save PNG files to.");
			if (!string.IsNullOrEmpty(path))
			{
				string basename = System.IO.Path.GetFileNameWithoutExtension(path);
				string pathname = System.IO.Path.GetDirectoryName(path);

                // Get the UMAMaterials for each atlas.

                UMAData umaData = avatar.umaData;
                GeneratedMaterials gmatContainer = umaData.generatedMaterials;

				int i = 0;
				foreach (var gm in gmatContainer.materials)
				{
					UMAMaterial umat = gm.umaMaterial;
					Material mat = gm.skinnedMeshRenderer.sharedMaterials[gm.materialIndex];
                    Material omat = gm.material;
					foreach(var tex in umat.GetTexturePropertyNames())
                    {
                        Texture texture = mat.GetTexture(tex);
                        if (texture != null)
						{
							string tname = $"{pathname}/{basename}_{i}_{umat.name}{tex}.PNG";
							string altName = $"{pathname}/{basename}_alt_{i}_{umat.name}{tex}.PNG";

                            try
							{
								if (tex.ToLower().Contains("normal") || tex.ToLower().Contains("bump"))
                                {
                                    SaveTexture(texture, tname, true);
                                }
                                else
                                {
                                    SaveTexture(texture, tname);
                                }
							}
                            catch  
                            { 
								// Not a readable texture. This is actually OK. Wish isReadable wasn't broken.
                            }
                        }
                    }
					i++;
                }
            }
		}


        private static Texture2D GetReadableTexture(RenderTexture texture, bool isNormal)
        {
            RenderTexture tmp;

            if (isNormal)
            {
                tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);
            }
            else
            {
                tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.sRGB);
            }

            Graphics.Blit(texture, tmp);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;

            Texture2D readableTexture = new Texture2D(texture.width, texture.height,TextureFormat.RGBA32, false, isNormal);
            readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readableTexture.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);

            return readableTexture;
        }
        // Thanks, Brooklyn!
        private static Texture2D GetReadableTexture(Texture2D texture, bool isNormal)
        {
			RenderTexture tmp;

            if (isNormal)
			{
                tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);
            }
            else
			{
                tmp = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.sRGB);
            }

            Graphics.Blit(texture, tmp);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = tmp;

			// Always read back into a CPU RGBA32 texture; using `texture.format` can be incompatible
			// with `ReadPixels` (e.g., compressed/HDR formats) and can produce garbage data.
			Texture2D readableTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, isNormal);
            readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
            readableTexture.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(tmp);

            return readableTexture;
        }

        private static void SaveTexture(Texture texture, string diffuseName, bool isNormal = false)
		{
			if (isNormal)
			{
				texture = sconvertNormalMap(texture);
                SaveTexture(texture, diffuseName, false);
				return;
			}

			if (texture is RenderTexture)
			{
                //Debug.Log("Saving render texture: " + diffuseName);
                //SaveRenderTexture(texture as RenderTexture, diffuseName, isNormal);
                Texture2D tex = GetReadableTexture(texture as RenderTexture, isNormal);
                SaveTexture2D(tex, diffuseName, isNormal);
                DestroyImmediate(tex);
                return;
			}
			else if (texture is Texture2D)
			{
                Texture2D tex = GetReadableTexture(texture as Texture2D, isNormal);
                SaveTexture2D(tex, diffuseName, isNormal);
                DestroyImmediate(tex);
                return;
			}
			EditorUtility.DisplayDialog("Error", "Texture is not RenderTexture or Texture2D", "OK");
		}

		/// <param name="normalMap"></param>
		/// <returns></returns>
		private static Texture2D SConvertNormalMap(Texture2D normalMap)
		{
			ComputeShader normalMapConverter = Resources.Load<ComputeShader>("Shader/NormalShader");
			int kernel = normalMapConverter.FindKernel("NormalCvt");
			// RenderTexture normalMapRenderTex = new RenderTexture(normalMap.width, normalMap.height, 24);
			var normalMapRenderTex = RenderTexture.GetTemporary(normalMap.width, normalMap.height, 24);
			normalMapRenderTex.enableRandomWrite = true;
            //normalMapRenderTex.Create();

            normalMapConverter.SetTexture(kernel, "Input", normalMap);
			normalMapConverter.SetTexture(kernel, "Result", normalMapRenderTex);
			normalMapConverter.Dispatch(kernel, normalMap.width / 8, normalMap.height / 8, 1);
            Texture2D convertedNormalMap = new Texture2D(normalMap.width, normalMap.height, TextureFormat.RGBA32, false, true);
            RenderTexture.active = normalMapRenderTex;
            convertedNormalMap.ReadPixels(new Rect(0, 0, normalMap.width, normalMap.height), 0, 0);
			convertedNormalMap.Apply();

			RenderTexture.ReleaseTemporary(normalMapRenderTex);
			return convertedNormalMap;
		}

		public static Texture2D SConvertNormalMap(RenderTexture normalMap)
		{
			ComputeShader normalMapConverter = Resources.Load<ComputeShader>("Shader/NormalShader");
			int kernel = normalMapConverter.FindKernel("NormalCvt");
			RenderTexture normalMapRenderTex = new RenderTexture(normalMap.width, normalMap.height, 24);
			normalMapRenderTex.enableRandomWrite = true;
			normalMapRenderTex.Create();
			normalMapConverter.SetTexture(kernel, "Input", normalMap);
			normalMapConverter.SetTexture(kernel, "Result", normalMapRenderTex);
			normalMapConverter.Dispatch(kernel, normalMap.width/8, normalMap.height/8, 1);
			RenderTexture.active = normalMapRenderTex;

			Texture2D convertedNormalMap = new Texture2D(normalMap.width, normalMap.height, TextureFormat.RGBA32, false, true);
			convertedNormalMap.ReadPixels(new Rect(0, 0, normalMap.width, normalMap.height), 0, 0);
			convertedNormalMap.Apply();

			DestroyImmediate(normalMapRenderTex);
			return convertedNormalMap;
		}

		private static Texture2D sconvertNormalMap2(RenderTexture rt)
		{
			Texture2D tex = GetRTPixels(rt);
			Texture2D result = SConvertNormalMap(tex);
			DestroyImmediate(tex);
			return result;
		}

		private static Texture2D sconvertNormalMap(Texture tex)
		{
			if (tex is RenderTexture)
            {
                return SConvertNormalMap(tex as RenderTexture);
            }
            return SConvertNormalMap(tex as Texture2D);
		}

		static public Texture2D GetRTPixels(RenderTexture rt)
		{
            // Remember crrently active render texture
            RenderTexture currentActiveRT = RenderTexture.active;

            /// Some goofiness ends up with the texture being too dark unless
            /// I send it to a new render texture.
            RenderTexture outputMap = new RenderTexture(rt.width, rt.height, 32, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); 
			outputMap.enableRandomWrite = true;
			outputMap.Create();
			RenderTexture.active = outputMap;
			GL.Clear(true, true, Color.black);
			Graphics.Blit(rt, outputMap);



			// Set the supplied RenderTexture as the active one
			RenderTexture.active = outputMap;

			// Create a new Texture2D and read the RenderTexture image into it
			Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
			tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);

			// Restore previously active render texture
			RenderTexture.active = currentActiveRT;
			DestroyImmediate(outputMap);
			return tex;
		}

        static public void LinearSave(RenderTexture rt, string textureName, bool isNormal)
        {
            // Remember crrently active render texture
            RenderTexture currentActiveRT = RenderTexture.active;

            // Set the supplied RenderTexture as the active one
            RenderTexture.active = rt;

            // Create a new Texture2D and read the RenderTexture image into it
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false, true);
            tex.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);

            // Restore previously active render texture
            RenderTexture.active = currentActiveRT;
            SaveTexture2D(tex, textureName, isNormal);
            
        }

        public static void SaveRenderTexture(RenderTexture texture, string textureName, bool isNormal = false)
		{
			Texture2D tex;

			if (isNormal)
			{
				tex = SConvertNormalMap(texture);
			}
			else
			{
				tex = GetRTPixels(texture);
			}
			SaveTexture2D(tex, textureName, isNormal);
		}

		private static void SaveTexture2D(Texture2D texture, string textureName, bool isNormal)
		{
            Texture2D convertedTexture = GetReadableTexture(texture, isNormal);
			byte[] data = convertedTexture.EncodeToPNG();
			DestroyImmediate(convertedTexture);
            System.IO.File.WriteAllBytes(textureName, data);
        }

        [UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as UMA Preset")]
		[UnityEditor.MenuItem("GameObject/UMA/Save as UMA Preset")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar as UMA Preset", priority = 1)]
		public static void SaveSelectedAvatarsPreset()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save avatar preset", "Assets", avatar.name + ".umapreset", "umapreset");
					if (path.Length != 0)
					{

						UMAPreset prs = new UMAPreset();
						prs.DefaultColors = avatar.characterColors;
						var DNA = avatar.GetDNA();
						prs.PredefinedDNA = new UMAPredefinedDNA();
						foreach (DnaSetter d in DNA.Values)
						{
							prs.PredefinedDNA.AddDNA(d.Name, d.Value);
						}
						prs.DefaultWardrobe = new DynamicCharacterAvatar.WardrobeRecipeList();
						foreach (UMATextRecipe utr in avatar.WardrobeRecipes.Values)
						{
							prs.DefaultWardrobe.recipes.Add(new DynamicCharacterAvatar.WardrobeRecipeListItem(utr));
						}
						string presetstring = JsonUtility.ToJson(prs);
						System.IO.File.WriteAllText(path, presetstring);
					}
				}
			}
		}


		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Character text file (runtime only)")]
		[UnityEditor.MenuItem("GameObject/UMA/Save as Character Text file (runtime only)")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar(s) Txt", priority = 1)]
		public static void SaveSelectedAvatarsTxt()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save serialized Avatar", "Assets", avatar.name + ".txt", "txt");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						//check if Avatar is DCS
						if (avatar is UMA.CharacterSystem.DynamicCharacterAvatar)
						{
							asset.Save(avatar.umaData.umaRecipe,(avatar as DynamicCharacterAvatar).WardrobeRecipes, true);
						}
						else
						{
							asset.Save(avatar.umaData.umaRecipe);
						}
						System.IO.File.WriteAllText(path, asset.recipeString);
						UMAUtils.DestroySceneObject(asset);
					}
				}
			}
		}


		[UnityEditor.MenuItem("GameObject/UMA/Show Mesh Info (runtime only)")]
		public static void ShowSelectedAvatarStats()
		{
			if (Selection.gameObjects.Length == 1)
			{
				var selectedTransform = Selection.gameObjects[0].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}
				if (avatar != null)
				{
					SkinnedMeshRenderer sk = avatar.gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
					if (sk != null)
					{
						List<string> info = new List<string>
					{
						sk.gameObject.name,
						"Mesh index type: " + sk.sharedMesh.indexFormat.ToString(),
						"VertexLength: " + sk.sharedMesh.vertices.Length,
						"Submesh Count: " + sk.sharedMesh.subMeshCount
					};
						for (int i = 0; i < sk.sharedMesh.subMeshCount; i++)
						{
							int[] tris = sk.sharedMesh.GetTriangles(i);
							info.Add("Submesh " + i + " Tri count: " + tris.Length);
						}
						Rect R = new Rect(200.0f, 200.0f, 300.0f, 600.0f);
						DisplayListWindow.ShowDialog("Mesh Info", R, info);
					}
				}
			}

		}

		[UnityEditor.MenuItem("GameObject/UMA/Save as Character Asset (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Asset (runtime only)")]
		[MenuItem("UMA/Load and Save/Save Selected Avatar(s) asset", priority = 1)]
		public static void SaveSelectedAvatarsAsset()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}
				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanelInProject("Save serialized Avatar", avatar.name + ".asset", "asset", "Message 2");
					if (path.Length != 0)
					{
						var asset = ScriptableObject.CreateInstance<UMATextRecipe>();
						//check if Avatar is DCS
						if (avatar is DynamicCharacterAvatar)
						{
							asset.Save(avatar.umaData.umaRecipe, (avatar as DynamicCharacterAvatar).WardrobeRecipes, true);
						}
						else
						{
							asset.Save(avatar.umaData.umaRecipe);
						}
						AssetDatabase.CreateAsset(asset, path);
						AssetDatabase.SaveAssets();
						Debug.Log("Recipe size: " + asset.recipeString.Length + " chars");

					}
				}
			}
		}

		[UnityEditor.MenuItem("GameObject/UMA/Load from AvatarDefinition file (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Load Avatar from an AvatarDefinition file (runtime only)")]
		[MenuItem("UMA/Load and Save/Load Selected Avatar(s) txt", priority = 1)]
		public static void LoadSelectedAvatarsTxt()
		{
			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				while (avatar == null && selectedTransform.parent != null)
				{
					selectedTransform = selectedTransform.parent;
					avatar = selectedTransform.GetComponent<UMAAvatarBase>();
				}

				if (avatar != null)
				{
					var path = EditorUtility.OpenFilePanel("Load serialized Avatar", "Assets", "txt,adf");
					if (path.Length != 0)
					{
						string recipeString = FileUtils.ReadAllText(path);
						//check if Avatar is DCS
						if (avatar is DynamicCharacterAvatar)
						{
							(avatar as DynamicCharacterAvatar).LoadAvatarDefinition(recipeString);
						}
					}
				}
			}
		}


		//@jaimi this is the equivalent of your previous JSON save but the resulting file does not need a special load method
		[UnityEditor.MenuItem("GameObject/UMA/Save as AvatarDefinition (runtime only)")]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Save as Optimized AvatarDefinition File")]
		[MenuItem("UMA/Load and Save/Save DynamicCharacterAvatar(s) AvatarDefinition (optimized)", priority = 1)]
		public static void SaveSelectedAvatarsDefinition()
		{
			if (!Application.isPlaying)
			{
				EditorUtility.DisplayDialog("Notice", "This function is only available at runtime", "Got it");
				return;
			}

			for (int i = 0; i < Selection.gameObjects.Length; i++)
			{
				var selectedTransform = Selection.gameObjects[i].transform;
				var avatar = selectedTransform.GetComponent<DynamicCharacterAvatar>();

				if (avatar != null)
				{
					var path = EditorUtility.SaveFilePanel("Save DynamicCharacterAvatar Text", "Assets", avatar.name + ".txt", "txt");
					if (path.Length != 0)
					{
						avatar.DoSave(false, path);
					}
				}
			}
		}



		[UnityEditor.MenuItem("Assets/Add Selected Assets to UMA Global Library")]
		public static void AddSelectedToGlobalLibrary()
		{
			int added = 0;
			UMAAssetIndexer UAI = UMAAssetIndexer.Instance;

			foreach (Object o in Selection.objects)
			{
				System.Type type = o.GetType();
				if (UAI.IsIndexedType(type))
				{
					if (UAI.EvilAddAsset(type, o))
                    {
                        added++;
                    }
                }
			}
			UAI.ForceSave();
			EditorUtility.DisplayDialog("Success", added + " item(s) added to Global Library", "OK");
		}
	}

	internal class UmaAddRacesToRecipesWindow : EditorWindow
	{
		private readonly List<UMAWardrobeRecipe> _targetRecipes = new List<UMAWardrobeRecipe>();
		private readonly List<RaceData> _allRaces = new List<RaceData>();
		private bool[] _raceSelected = new bool[0];
		private Vector2 _scroll;

		public static void Open(List<UMAWardrobeRecipe> targetRecipes)
		{
			var window = GetWindow<UmaAddRacesToRecipesWindow>(true, "Add Races to Recipes", true);
			window.minSize = new Vector2(420f, 420f);

			window._targetRecipes.Clear();
			if (targetRecipes != null)
			{
				window._targetRecipes.AddRange(targetRecipes);
			}

			window.RefreshRaces();
			window.ShowUtility();
			window.Focus();
		}

		private void RefreshRaces()
		{
			_allRaces.Clear();

			var idx = UMAAssetIndexer.Instance;
			if (idx != null)
			{
				var races = idx.GetAllAssets<RaceData>();
				if (races != null)
				{
					for (int i = 0; i < races.Count; i++)
					{
						if (races[i] != null)
						{
							_allRaces.Add(races[i]);
						}
					}
				}
			}

			_allRaces.Sort((a, b) => string.Compare(a != null ? a.raceName : "", b != null ? b.raceName : "", System.StringComparison.OrdinalIgnoreCase));
			_raceSelected = new bool[_allRaces.Count];
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Add Race(s) to Selected UMAWardrobeRecipe", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox($"Selected recipes: {_targetRecipes.Count}", MessageType.Info);

			EditorGUILayout.Space(8);
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField("RaceData", EditorStyles.boldLabel);
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Refresh", GUILayout.Width(80)))
			{
				RefreshRaces();
			}
			EditorGUILayout.EndHorizontal();

			if (_allRaces.Count == 0)
			{
				EditorGUILayout.HelpBox("No RaceData found (UMAAssetIndexer not ready, or project has no RaceData).", MessageType.Warning);
			}
			else
			{
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("All", GUILayout.Width(80)))
				{
					for (int i = 0; i < _raceSelected.Length; i++)
					{
						_raceSelected[i] = true;
					}
				}
				if (GUILayout.Button("None", GUILayout.Width(80)))
				{
					for (int i = 0; i < _raceSelected.Length; i++)
					{
						_raceSelected[i] = false;
					}
				}
				GUILayout.FlexibleSpace();
				EditorGUILayout.EndHorizontal();

				EditorGUILayout.Space(4);
				_scroll = EditorGUILayout.BeginScrollView(_scroll);
				for (int i = 0; i < _allRaces.Count; i++)
				{
					var race = _allRaces[i];
					if (race == null)
					{
						continue;
					}

					string label = !string.IsNullOrEmpty(race.raceName) ? race.raceName : race.name;
					_raceSelected[i] = EditorGUILayout.ToggleLeft(label, _raceSelected[i]);
				}
				EditorGUILayout.EndScrollView();
			}

			EditorGUILayout.Space(8);
			using (new EditorGUI.DisabledScope(_targetRecipes.Count == 0 || _allRaces.Count == 0))
			{
				if (GUILayout.Button("Apply to Selected Recipes", GUILayout.Height(28)))
				{
					Apply();
				}
			}
		}

		private void Apply()
		{
			var selectedRaces = new List<RaceData>();
			for (int i = 0; i < _allRaces.Count; i++)
			{
				if (i < _raceSelected.Length && _raceSelected[i])
				{
					if (_allRaces[i] != null)
					{
						selectedRaces.Add(_allRaces[i]);
					}
				}
			}

			if (selectedRaces.Count == 0)
			{
				EditorUtility.DisplayDialog("Add Races", "Select one or more RaceData entries to apply.", "OK");
				return;
			}

			int added = 0;
			for (int i = 0; i < _targetRecipes.Count; i++)
			{
				var recipe = _targetRecipes[i];
				if (recipe == null)
				{
					continue;
				}

				Undo.RecordObject(recipe, "Add Races to Recipe");
				foreach (var race in selectedRaces)
				{
					if (race == null || string.IsNullOrEmpty(race.raceName))
					{
						continue;
					}

					if (!recipe.compatibleRaces.Contains(race.raceName))
					{
						recipe.compatibleRaces.Add(race.raceName);
						added++;
					}
				}
				EditorUtility.SetDirty(recipe);
			}

			AssetDatabase.SaveAssets();
			var idx = UMAAssetIndexer.Instance;
			if (idx != null)
			{
				idx.ForceSave();
			}

			EditorUtility.DisplayDialog("Add Races", $"Added {added} race assignment(s) to {_targetRecipes.Count} recipe(s).", "OK");
			Close();
		}
	}

	public class UmaPrefabSaverWindow : EditorWindow
	{
		[Tooltip("The character that you want to convert")]
		public UMAAvatarBase baseObject;
		[Tooltip("If true, will replace the UMA with the generated prefab in the scene")]
        public bool replaceExisting = false;
        [Tooltip("Convert Swizzled normal maps back to standard normal maps")]
		public bool UnswizzleNormalMaps = true;
		[Tooltip("If True, will keep the umaData, and add a Standalone DNA component allowing you to load/save/Deform skeletal DNA")]
		public bool AddStandaloneDNA = true;
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

		void OnGUI()
		{
			EditorGUILayout.LabelField("UMA Prefab Saver", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("This will convert an UMA avatar into a non-UMA prefab. Once converted, it can be reused with little overhead, but all UMA functionality will be lost.", MessageType.None, false);
			baseObject = (UMAAvatarBase)EditorGUILayout.ObjectField("UMA Avatar",baseObject, typeof(UMAAvatarBase),true);
			EditorGUILayout.HelpBox("If you unswizzle normals (recommended) then they can be used in other applications, and UMA will automatically mark them as normal maps in the import settings.", MessageType.None);
			UnswizzleNormalMaps = EditorGUILayout.Toggle("Unswizzle Normals", UnswizzleNormalMaps);
			EditorGUILayout.HelpBox("Adding Standalone DNA will allow you to adjust most DNA of the character, without it being an UMA. However, it will require that you have the UMA system in the project.",MessageType.None);
			AddStandaloneDNA = EditorGUILayout.Toggle("Add Standalone DNA", AddStandaloneDNA);

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
					UMAAvatarLoadSaveMenuItems.ConvertToNonUMA(baseObject.gameObject, baseObject, folder, UnswizzleNormalMaps, CharacterName,AddStandaloneDNA,replaceExisting);
					EditorUtility.DisplayDialog("UMA Prefab Saver", "Conversion complete", "OK");
				}
			}
			else
            {
				if (baseObject == null)
				{
					EditorGUILayout.HelpBox("A valid character with DynamicCharacterAvatar or DynamicAvatar must be supplied",MessageType.Error);
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
		}

		[MenuItem("UMA/Prefab Maker", priority = 20)]
		public static void OpenUmaPrefabWindow()
		{
			UmaPrefabSaverWindow window = (UmaPrefabSaverWindow)EditorWindow.GetWindow(typeof(UmaPrefabSaverWindow));
			window.titleContent.text = "UMA Prefab Maker";
		}
	}
}
