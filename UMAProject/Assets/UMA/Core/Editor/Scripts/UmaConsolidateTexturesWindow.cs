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
}
