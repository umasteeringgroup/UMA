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
                        readable = UMAAvatarLoadSaveMenuItems.GetReadableTexture(entry.Texture, false);
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
					string absDestPath = GetAbsolutePathFromAssetPath(destPath);
					if (!string.IsNullOrEmpty(absDestPath))
					{
						File.WriteAllBytes(absDestPath, data);
					}
					else
					{
						File.WriteAllBytes(destPath, data);
					}
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
							if (overlay.textureNames != null && t < overlay.textureNames.Length)
							{
								overlay.textureNames[t] = newTexture.name;
							}
							changed = true;
						}
					}

					if (overlay.alphaMask == oldTexture)
					{
						overlay.alphaMask = newTexture;
						changed = true;
					}

					if (!changed)
					{
						continue;
					}

					Undo.RecordObject(overlay, "Replace overlay texture");
					overlay.textureList = list;

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
					string absPath = GetAbsolutePathFromAssetPath(assetPath);
					if (!string.IsNullOrEmpty(absPath) && File.Exists(absPath))
					{
						return new FileInfo(absPath).Length;
					}
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

			private static string GetAbsolutePathFromAssetPath(string assetPath)
			{
				if (string.IsNullOrEmpty(assetPath))
				{
					return null;
				}
				if (!assetPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
				{
					return null;
				}
				string projectRoot = Path.GetDirectoryName(Application.dataPath);
				if (string.IsNullOrEmpty(projectRoot))
				{
					return null;
				}
				string relative = assetPath.Substring("Assets/".Length);
				return Path.Combine(projectRoot, "Assets", relative);
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
}
