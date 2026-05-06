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
internal class UmaConsolidateCurrentSceneAssetsWindow : EditorWindow
	{
       private class ConsolidateCandidate
		{
			public string Name;
			public string Path;
			public string TypeName;
			public string Category;
            public string Reason;
            public UnityEngine.Object ReasonSourceObject;
			public bool Selected = true;
		}

		private const string DefaultDestinationFolder = "Assets/UMA/UMA3/Examples/ExampleAssets";
       private const string DefaultSourceFolder = "Assets";
       private const string PrefsPrefix = "UMA.ConsolidateCurrentSceneAssets.";
		private const string PrefsDestFolderPath = PrefsPrefix + "DestFolderPath";
		private const string PrefsSourceFolderPath = PrefsPrefix + "SourceFolderPath";
		private const string PrefsIgnoredFolders = PrefsPrefix + "IgnoredFolders";
		private DefaultAsset _destFolder;
		private string _destFolderPath = DefaultDestinationFolder;
		private DefaultAsset _sourceFolder;
		private string _sourceFolderPath = DefaultSourceFolder;
     private readonly List<DefaultAsset> _ignoreFolders = new List<DefaultAsset>();
		private readonly List<string> _ignoreFolderPaths = new List<string>();
		private readonly List<ConsolidateCandidate> _candidates = new List<ConsolidateCandidate>();
		private Vector2 _candidateScroll;

		public static void Open()
		{
			var window = GetWindow<UmaConsolidateCurrentSceneAssetsWindow>(true, "Consolidate Current Scene Assets", true);
            window.minSize = new Vector2(820f, 420f);
          window.LoadPreferences();
			if (string.IsNullOrEmpty(window._destFolderPath))
			{
				window._destFolderPath = DefaultDestinationFolder;
			}
			if (string.IsNullOrEmpty(window._sourceFolderPath))
			{
				window._sourceFolderPath = DefaultSourceFolder;
			}
			window.TryInitializeDefaultFolder();
           window.TryInitializeSourceFolder();
           window.RebuildCandidateList();
			window.ShowUtility();
			window.Focus();
		}

		private void LoadPreferences()
		{
			_destFolderPath = EditorPrefs.GetString(PrefsDestFolderPath, DefaultDestinationFolder);
			_sourceFolderPath = EditorPrefs.GetString(PrefsSourceFolderPath, DefaultSourceFolder);

			_ignoreFolders.Clear();
			_ignoreFolderPaths.Clear();

			string packedIgnored = EditorPrefs.GetString(PrefsIgnoredFolders, string.Empty);
			if (string.IsNullOrEmpty(packedIgnored))
			{
				return;
			}

			string[] paths = packedIgnored.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < paths.Length; i++)
			{
				string path = paths[i].Trim();
				if (string.IsNullOrEmpty(path) || !AssetDatabase.IsValidFolder(path))
				{
					continue;
				}

				if (_ignoreFolderPaths.Contains(path))
				{
					continue;
				}

				_ignoreFolderPaths.Add(path);
				var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(path);
				if (folder != null)
				{
					_ignoreFolders.Add(folder);
				}
			}
		}

		private void SavePreferences()
		{
            EnsureDestinationInIgnoredFolders();
			EditorPrefs.SetString(PrefsDestFolderPath, _destFolderPath ?? DefaultDestinationFolder);
			EditorPrefs.SetString(PrefsSourceFolderPath, _sourceFolderPath ?? DefaultSourceFolder);
			EditorPrefs.SetString(PrefsIgnoredFolders, string.Join("\n", _ignoreFolderPaths.ToArray()));
		}

		private void EnsureDestinationInIgnoredFolders()
		{
			if (string.IsNullOrEmpty(_destFolderPath) || !AssetDatabase.IsValidFolder(_destFolderPath))
			{
				return;
			}
			AddIgnoreFolderPath(_destFolderPath);
		}

		private void AddIgnoreFolderPath(string folderPath)
		{
			if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
			{
				return;
			}
			for (int i = 0; i < _ignoreFolderPaths.Count; i++)
			{
				if (string.Equals(_ignoreFolderPaths[i], folderPath, System.StringComparison.OrdinalIgnoreCase))
				{
					return;
				}
			}

			_ignoreFolderPaths.Add(folderPath);
			var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folderPath);
			if (folder != null)
			{
				_ignoreFolders.Add(folder);
			}
		}

		private void DrawIgnoreFolderDropArea()
		{
           EnsureDestinationInIgnoredFolders();
			EditorGUILayout.LabelField("Ignore Folders", EditorStyles.boldLabel);
			Rect dropRect = GUILayoutUtility.GetRect(0f, 42f, GUILayout.ExpandWidth(true));
			GUI.Box(dropRect, "Drop folders here to ignore", EditorStyles.helpBox);

			Event evt = Event.current;
			if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && dropRect.Contains(evt.mousePosition))
			{
				DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
				if (evt.type == EventType.DragPerform)
				{
					DragAndDrop.AcceptDrag();
					for (int i = 0; i < DragAndDrop.objectReferences.Length; i++)
					{
						var obj = DragAndDrop.objectReferences[i] as DefaultAsset;
						if (obj == null)
						{
							continue;
						}

						string path = AssetDatabase.GetAssetPath(obj);
						if (!string.IsNullOrEmpty(path) && AssetDatabase.IsValidFolder(path))
						{
							AddIgnoreFolderPath(path);
						}
					}
					SavePreferences();
					RebuildCandidateList();
				}
				evt.Use();
			}

			for (int i = 0; i < _ignoreFolderPaths.Count; i++)
			{
				string path = _ignoreFolderPaths[i];
              bool isDestinationFolder = string.Equals(path, _destFolderPath, System.StringComparison.OrdinalIgnoreCase);
				EditorGUILayout.BeginHorizontal();
              EditorGUILayout.LabelField(isDestinationFolder ? (path + " (destination)") : path, GUILayout.ExpandWidth(true));
				using (new EditorGUI.DisabledScope(isDestinationFolder))
				{
                 if (GUILayout.Button("x", GUILayout.Width(22)))
					{
                      _ignoreFolderPaths.RemoveAt(i);
						for (int f = _ignoreFolders.Count - 1; f >= 0; f--)
						{
                         if (_ignoreFolders[f] == null)
							{
								_ignoreFolders.RemoveAt(f);
								continue;
							}
							string fp = AssetDatabase.GetAssetPath(_ignoreFolders[f]);
							if (string.Equals(fp, path, System.StringComparison.OrdinalIgnoreCase))
							{
								_ignoreFolders.RemoveAt(f);
							}
						}
                       SavePreferences();
						RebuildCandidateList();
						GUIUtility.ExitGUI();
					}
				}
				EditorGUILayout.EndHorizontal();
			}
		}

		private void TryInitializeDefaultFolder()
		{
			if (!AssetDatabase.IsValidFolder(_destFolderPath))
			{
				_destFolder = null;
				return;
			}

			_destFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_destFolderPath);
           EnsureDestinationInIgnoredFolders();
		}

		private void TryInitializeSourceFolder()
		{
			if (!AssetDatabase.IsValidFolder(_sourceFolderPath))
			{
				_sourceFolder = null;
				return;
			}

			_sourceFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_sourceFolderPath);
		}

		private void OnGUI()
		{
			EditorGUILayout.LabelField("Consolidate Current Scene Assets", EditorStyles.boldLabel);
         EditorGUILayout.HelpBox("Copies allowed assets referenced by the current scene into category subfolders under a destination folder.", MessageType.Info);
			EditorGUILayout.Space(6);

			EditorGUILayout.LabelField("Destination Folder (under Assets)", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			_destFolder = (DefaultAsset)EditorGUILayout.ObjectField(_destFolder, typeof(DefaultAsset), false);
			if (EditorGUI.EndChangeCheck())
			{
				_destFolderPath = _destFolder != null ? AssetDatabase.GetAssetPath(_destFolder) : DefaultDestinationFolder;
				if (!string.IsNullOrEmpty(_destFolderPath) && !AssetDatabase.IsValidFolder(_destFolderPath))
				{
					_destFolder = null;
					_destFolderPath = DefaultDestinationFolder;
				}
              SavePreferences();
               RebuildCandidateList();
			}

			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.TextField("Path", _destFolderPath);
			}

			EditorGUILayout.Space(6);
			EditorGUILayout.LabelField("Source Folder (under Assets)", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			_sourceFolder = (DefaultAsset)EditorGUILayout.ObjectField(_sourceFolder, typeof(DefaultAsset), false);
			if (EditorGUI.EndChangeCheck())
			{
				_sourceFolderPath = _sourceFolder != null ? AssetDatabase.GetAssetPath(_sourceFolder) : DefaultSourceFolder;
				if (!string.IsNullOrEmpty(_sourceFolderPath) && !AssetDatabase.IsValidFolder(_sourceFolderPath))
				{
					_sourceFolder = null;
					_sourceFolderPath = DefaultSourceFolder;
				}
              SavePreferences();
               RebuildCandidateList();
			}

			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.TextField("Source Path", _sourceFolderPath);
			}

			DrawIgnoreFolderDropArea();

			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			if (GUILayout.Button("Rescan", GUILayout.Width(120), GUILayout.Height(24)))
			{
				RebuildCandidateList();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(10);
          DrawCandidateList();

			EditorGUILayout.Space(10);
			EditorGUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
           using (new EditorGUI.DisabledScope(CountSelectedCandidates() == 0))
			{
                if (GUILayout.Button("Consolidate", GUILayout.Width(120), GUILayout.Height(28)))
				{
					ContinueConsolidation();
				}
			}
			if (GUILayout.Button("Cancel", GUILayout.Width(120), GUILayout.Height(28)))
			{
				Close();
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawCandidateList()
		{
			EditorGUILayout.LabelField("Items To Consolidate", EditorStyles.boldLabel);
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Select All", GUILayout.Width(100)))
			{
				for (int i = 0; i < _candidates.Count; i++)
				{
					_candidates[i].Selected = true;
				}
			}
			if (GUILayout.Button("Clear Selection", GUILayout.Width(120)))
			{
				for (int i = 0; i < _candidates.Count; i++)
				{
					_candidates[i].Selected = false;
				}
			}
			if (GUILayout.Button("Invert Selection", GUILayout.Width(120)))
			{
				for (int i = 0; i < _candidates.Count; i++)
				{
					_candidates[i].Selected = !_candidates[i].Selected;
				}
			}
			GUILayout.FlexibleSpace();
			GUILayout.Label("Selected: " + CountSelectedCandidates() + " / " + _candidates.Count, EditorStyles.miniLabel);
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space(4);
			EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
			GUILayout.Label("", GUILayout.Width(20));
			GUILayout.Label("Object Name", EditorStyles.boldLabel, GUILayout.Width(220));
			GUILayout.Label("Path", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
			GUILayout.Label("Type", EditorStyles.boldLabel, GUILayout.Width(120));
            GUILayout.Label("Ref", EditorStyles.boldLabel, GUILayout.Width(38));
            GUILayout.Label("Reason", EditorStyles.boldLabel, GUILayout.Width(280));
			EditorGUILayout.EndHorizontal();

			_candidateScroll = EditorGUILayout.BeginScrollView(_candidateScroll, GUILayout.MinHeight(120f), GUILayout.ExpandHeight(true));
			if (_candidates.Count == 0)
			{
				EditorGUILayout.HelpBox("No allowed scene dependencies were found under the selected source folder.", MessageType.Info);
			}
			else
			{
				for (int i = 0; i < _candidates.Count; i++)
				{
					var candidate = _candidates[i];
					if (candidate == null)
					{
						continue;
					}

					EditorGUILayout.BeginVertical();
					EditorGUILayout.BeginHorizontal();
					candidate.Selected = EditorGUILayout.Toggle(candidate.Selected, GUILayout.Width(20));
					EditorGUILayout.SelectableLabel(candidate.Name ?? string.Empty, GUILayout.Width(220), GUILayout.Height(EditorGUIUtility.singleLineHeight));
					EditorGUILayout.SelectableLabel(candidate.Path ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
					EditorGUILayout.SelectableLabel(candidate.TypeName ?? string.Empty, GUILayout.Width(120), GUILayout.Height(EditorGUIUtility.singleLineHeight));
					using (new EditorGUI.DisabledScope(candidate.ReasonSourceObject == null))
					{
						if (GUILayout.Button("Ping", GUILayout.Width(38), GUILayout.Height(EditorGUIUtility.singleLineHeight)))
						{
							Selection.activeObject = candidate.ReasonSourceObject;
							EditorGUIUtility.PingObject(candidate.ReasonSourceObject);
						}
					}
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.BeginHorizontal();
					GUILayout.Space(64f);
					EditorGUILayout.SelectableLabel(candidate.Reason ?? string.Empty, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndVertical();
				}
			}
			EditorGUILayout.EndScrollView();
		}

		private static string BuildConsolidateReason(UnityEngine.Object asset, UnityEngine.Object source, string fieldName)
		{
			string sourceChain = GetSourceChain(source, fieldName);
			string sourcePrefix = !string.IsNullOrEmpty(sourceChain) ? (sourceChain + " -> ") : "";

			if (asset == null)
			{
				return sourcePrefix + "Scene dependency";
			}

			if (asset is UMARecipeBase || asset is UMATextRecipe || asset is UMAWardrobeRecipe)
			{
				return "Excluded: recipes are handled by a separate process";
			}
			if (asset is UMA.SlotDataAsset)
			{
				return "Excluded: slots are handled by a separate process";
			}
			if (asset is UMA.OverlayDataAsset)
			{
				return "Excluded: overlays are handled by a separate process";
			}

			if (asset is Material)
			{
				return sourcePrefix + "renderer material";
			}
			if (asset is Texture)
			{
				return sourcePrefix + "material texture channel";
			}
			if (asset is AudioClip)
			{
				return sourcePrefix + "scene component audio field";
			}
			if (asset is GameObject)
			{
				return sourcePrefix + "prefab dependency";
			}

			var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset));
			if (importer is ModelImporter)
			{
				return sourcePrefix + "model dependency";
			}

			return sourcePrefix + "scene dependency graph";
		}

		private static string GetSourceChain(UnityEngine.Object source, string fieldName)
		{
			if (source == null)
			{
				return string.Empty;
			}

			List<string> parts = new List<string>();
			GameObject sourceGameObject = GetSourceGameObject(source);

			if (sourceGameObject != null)
			{
				Transform current = sourceGameObject.transform;
				Stack<string> transformNames = new Stack<string>();
				while (current != null)
				{
					transformNames.Push(current.name);
					current = current.parent;
				}

				while (transformNames.Count > 0)
				{
					parts.Add(transformNames.Pop());
				}

				if (source is Component sourceComponent)
				{
					parts.Add("Component:" + sourceComponent.GetType().Name);
					if (!string.IsNullOrEmpty(fieldName))
					{
						parts.Add("field:" + fieldName);
					}
				}

				GameObject prefabSource = PrefabUtility.GetCorrespondingObjectFromSource(sourceGameObject);
				if (prefabSource != null)
				{
					parts.Add("Prefab:" + prefabSource.name);
				}
			}
			else
			{
				parts.Add(source.name);
			}

			return string.Join("->", parts.ToArray());
		}

		private static string TryGetReferenceFieldName(UnityEngine.Object source, UnityEngine.Object dependency)
		{
			if (source == null || dependency == null)
			{
				return string.Empty;
			}

			if (!(source is Component) && !(source is GameObject))
			{
				return string.Empty;
			}

			SerializedObject serializedObject = null;
			try
			{
				serializedObject = new SerializedObject(source);
			}
			catch
			{
				return string.Empty;
			}

			if (serializedObject == null)
			{
				return string.Empty;
			}

			SerializedProperty iterator = serializedObject.GetIterator();
			bool enterChildren = true;
			while (iterator.NextVisible(enterChildren))
			{
				enterChildren = true;
				if (iterator.propertyType != SerializedPropertyType.ObjectReference)
				{
					continue;
				}

				if (iterator.objectReferenceValue == dependency)
				{
					return iterator.propertyPath;
				}
			}

			return string.Empty;
		}

		private static GameObject GetSourceGameObject(UnityEngine.Object source)
		{
			if (source is GameObject sourceAsGameObject)
			{
				return sourceAsGameObject;
			}

			if (source is Component sourceAsComponent)
			{
				return sourceAsComponent.gameObject;
			}

			return null;
		}

		private int CountSelectedCandidates()
		{
			int count = 0;
			for (int i = 0; i < _candidates.Count; i++)
			{
				if (_candidates[i] != null && _candidates[i].Selected)
				{
					count++;
				}
			}
			return count;
		}

		private void RebuildCandidateList()
		{
			_candidates.Clear();
			_candidateScroll = Vector2.zero;

			if (string.IsNullOrEmpty(_sourceFolderPath) || !AssetDatabase.IsValidFolder(_sourceFolderPath))
			{
				return;
			}

			var activeScene = SceneManager.GetActiveScene();
			if (!activeScene.IsValid())
			{
				return;
			}

			var rootObjects = activeScene.GetRootGameObjects();
			if (rootObjects == null || rootObjects.Length == 0)
			{
				return;
			}

			var dependencySources = new List<UnityEngine.Object>();
			var sourceIds = new HashSet<EntityId>();

			for (int r = 0; r < rootObjects.Length; r++)
			{
				var root = rootObjects[r];
				if (root == null)
				{
					continue;
				}

				var transforms = root.GetComponentsInChildren<Transform>(true);
				for (int t = 0; t < transforms.Length; t++)
				{
					var tr = transforms[t];
					if (tr == null)
					{
						continue;
					}

					var go = tr.gameObject;
					if (go != null)
					{
						EntityId goId = go.GetEntityId();
						if (!sourceIds.Contains(goId))
						{
							sourceIds.Add(goId);
							dependencySources.Add(go);
						}

						var components = go.GetComponents<Component>();
						for (int c = 0; c < components.Length; c++)
						{
							var component = components[c];
							if (component == null)
							{
								continue;
							}
							if (component is UMAGeneratorBase)
							{
								continue;
							}

							EntityId componentId = component.GetEntityId();
							if (sourceIds.Contains(componentId))
							{
								continue;
							}

							sourceIds.Add(componentId);
							dependencySources.Add(component);
						}
					}
				}
			}

			if (dependencySources.Count == 0)
			{
				return;
			}

           var depByPath = new Dictionary<string, UnityEngine.Object>(System.StringComparer.OrdinalIgnoreCase);
			var reasonByPath = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
			var sourceByPath = new Dictionary<string, UnityEngine.Object>(System.StringComparer.OrdinalIgnoreCase);
			var fieldByPath = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            for (int s = 0; s < dependencySources.Count; s++)
			{
                var source = dependencySources[s];
				if (source == null)
				{
					continue;
				}

				var rootDependencies = EditorUtility.CollectDependencies(new UnityEngine.Object[] { source });
				if (rootDependencies == null || rootDependencies.Length == 0)
				{
					continue;
				}

				for (int i = 0; i < rootDependencies.Length; i++)
				{
                   var dep = rootDependencies[i];
					if (dep == null)
					{
						continue;
					}

					string sourcePath = AssetDatabase.GetAssetPath(dep);
					if (!IsCandidatePathAllowed(sourcePath))
					{
						continue;
					}
					if (!TryGetAllowedCategoryForAsset(sourcePath, dep, out _))
					{
						continue;
					}

					if (!depByPath.ContainsKey(sourcePath))
					{
						string fieldName = TryGetReferenceFieldName(source, dep);
						depByPath[sourcePath] = dep;
						reasonByPath[sourcePath] = BuildConsolidateReason(dep, source, fieldName);
						sourceByPath[sourcePath] = source;
						fieldByPath[sourcePath] = fieldName;
					}
					else
					{
						UnityEngine.Object existingSource = sourceByPath[sourcePath];
						GameObject existingSourceGameObject = GetSourceGameObject(existingSource);
						GameObject candidateSourceGameObject = GetSourceGameObject(source);
						if (existingSourceGameObject != null && candidateSourceGameObject != null && existingSourceGameObject != candidateSourceGameObject && IsAncestorGameObject(existingSourceGameObject, candidateSourceGameObject))
						{
							string fieldName = TryGetReferenceFieldName(source, dep);
							reasonByPath[sourcePath] = BuildConsolidateReason(dep, source, fieldName);
							sourceByPath[sourcePath] = source;
							fieldByPath[sourcePath] = fieldName;
						}
					}
				}
			}

          foreach (var kvp in depByPath)
			{
				string sourcePath = kvp.Key;
				var dep = kvp.Value;
				if (!TryGetAllowedCategoryForAsset(sourcePath, dep, out string category))
				{
					continue;
				}

				_candidates.Add(new ConsolidateCandidate
				{
					Name = dep.name,
					Path = sourcePath,
					TypeName = dep.GetType().Name,
					Category = category,
					Reason = reasonByPath.TryGetValue(sourcePath, out var reason) ? reason : BuildConsolidateReason(dep, null, string.Empty),
					ReasonSourceObject = sourceByPath.TryGetValue(sourcePath, out var sourceObj) ? sourceObj : null,
					Selected = true
				});
			}

			_candidates.Sort((a, b) =>
			{
				int pathCompare = string.Compare(a != null ? a.Path : string.Empty, b != null ? b.Path : string.Empty, System.StringComparison.OrdinalIgnoreCase);
				if (pathCompare != 0)
				{
					return pathCompare;
				}
				return string.Compare(a != null ? a.Name : string.Empty, b != null ? b.Name : string.Empty, System.StringComparison.OrdinalIgnoreCase);
			});
		}

		private static bool IsAncestorGameObject(GameObject ancestor, GameObject candidate)
		{
			if (ancestor == null || candidate == null)
			{
				return false;
			}
			if (ancestor == candidate)
			{
				return false;
			}
			return candidate.transform.IsChildOf(ancestor.transform);
		}

		private bool IsCandidatePathAllowed(string sourcePath)
		{
			if (string.IsNullOrEmpty(sourcePath))
			{
				return false;
			}
			if (!sourcePath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (AssetDatabase.IsValidFolder(sourcePath))
			{
				return false;
			}
			if (sourcePath.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			if (!sourcePath.StartsWith(_sourceFolderPath + "/", System.StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(sourcePath, _sourceFolderPath, System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			for (int i = 0; i < _ignoreFolderPaths.Count; i++)
			{
				string ignoredFolder = _ignoreFolderPaths[i];
				if (string.IsNullOrEmpty(ignoredFolder))
				{
					continue;
				}
				if (string.Equals(sourcePath, ignoredFolder, System.StringComparison.OrdinalIgnoreCase) ||
					sourcePath.StartsWith(ignoredFolder + "/", System.StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}
			return true;
		}

		private void ContinueConsolidation()
		{
			if (!EnsureFolderPathExists(_destFolderPath))
			{
				EditorUtility.DisplayDialog("Consolidate Current Scene Assets", "Could not create destination folder:\n" + _destFolderPath, "OK");
				return;
			}

			if (string.IsNullOrEmpty(_sourceFolderPath) || !AssetDatabase.IsValidFolder(_sourceFolderPath))
			{
				EditorUtility.DisplayDialog("Consolidate Current Scene Assets", "Select a valid source folder under Assets.", "OK");
				return;
			}

			if (_candidates.Count == 0)
			{
             EditorUtility.DisplayDialog("Consolidate Current Scene Assets", "No allowed scene dependencies were found.", "OK");
				return;
			}
			if (CountSelectedCandidates() == 0)
			{
				EditorUtility.DisplayDialog("Consolidate Current Scene Assets", "Select at least one item to consolidate.", "OK");
				return;
			}

         var movedByCategory = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
			{
				["Textures"] = 0,
				["Models"] = 0,
				["Sounds"] = 0,
				["Materials"] = 0,
             ["Prefabs"] = 0,
				["Slots"] = 0,
				["Overlays"] = 0,
			};
         int moveErrors = 0;

			try
			{
               for (int i = 0; i < _candidates.Count; i++)
				{
                  EditorUtility.DisplayProgressBar("Consolidate Current Scene Assets", "Moving selected assets...", Mathf.Clamp01((float)(i + 1) / Mathf.Max(1, _candidates.Count)));
					var candidate = _candidates[i];
					if (candidate == null || !candidate.Selected)
					{
						continue;
					}

                    string sourcePath = candidate.Path;
					if (!IsCandidatePathAllowed(sourcePath))
					{
						continue;
					}
					if (sourcePath.StartsWith(_destFolderPath + "/", System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
                 string categoryPath = _destFolderPath + "/" + candidate.Category;
					if (!EnsureFolderPathExists(categoryPath))
					{
                       moveErrors++;
						continue;
					}

					string fileName = Path.GetFileName(sourcePath);
					if (string.IsNullOrEmpty(fileName))
					{
						continue;
					}

					string destPath = categoryPath + "/" + fileName;
					if (string.Equals(sourcePath, destPath, System.StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

                    string uniqueDestPath = AssetDatabase.GenerateUniqueAssetPath(destPath);
					string moveError = AssetDatabase.MoveAsset(sourcePath, uniqueDestPath);
					if (!string.IsNullOrEmpty(moveError))
					{
                       moveErrors++;
						continue;
					}

                  movedByCategory[candidate.Category] = movedByCategory[candidate.Category] + 1;
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();
			}

			EditorUtility.DisplayDialog(
				"Consolidate Current Scene Assets",
            "Moved Textures: " + movedByCategory["Textures"] +
				"\nMoved Models: " + movedByCategory["Models"] +
				"\nMoved Sounds: " + movedByCategory["Sounds"] +
				"\nMoved Materials: " + movedByCategory["Materials"] +
				"\nMoved Prefabs: " + movedByCategory["Prefabs"] +
				"\nMoved Slots: " + movedByCategory["Slots"] +
				"\nMoved Overlays: " + movedByCategory["Overlays"] +
				"\nMove errors: " + moveErrors,
				"OK");

			Close();
		}

       private static bool TryGetAllowedCategoryForAsset(string assetPath, UnityEngine.Object asset, out string category)
		{
          category = null;

         if (asset is UMARecipeBase || asset is UMATextRecipe || asset is UMAWardrobeRecipe)
			{
				return false;
			}
			if (asset is UMA.SlotDataAsset || asset is UMA.OverlayDataAsset)
			{
				return false;
			}
			if (asset is Material)
			{
             category = "Materials";
				return true;
			}
			if (asset is Texture)
			{
              category = "Textures";
				return true;
			}
			if (asset is AudioClip)
			{
                category = "Sounds";
				return true;
			}

			if (asset is GameObject)
			{
				string gameObjectExt = Path.GetExtension(assetPath);
				if (!string.IsNullOrEmpty(gameObjectExt) && string.Equals(gameObjectExt, ".prefab", System.StringComparison.OrdinalIgnoreCase))
				{
					category = "Prefabs";
					return true;
				}
			}

			var importer = AssetImporter.GetAtPath(assetPath);
			if (importer is ModelImporter)
			{
                category = "Models";
				return true;
			}

			string ext = Path.GetExtension(assetPath);
			if (!string.IsNullOrEmpty(ext))
			{
				ext = ext.ToLowerInvariant();
				if (ext == ".fbx" || ext == ".obj" || ext == ".dae" || ext == ".3ds" || ext == ".blend")
				{
                    category = "Models";
					return true;
				}
				if (ext == ".prefab")
				{
					category = "Prefabs";
					return true;
				}
			}

            return false;
		}

		private static bool EnsureFolderPathExists(string folderPath)
		{
			if (string.IsNullOrEmpty(folderPath))
			{
				return false;
			}

			folderPath = folderPath.Replace('\\', '/').Trim('/');
			if (!folderPath.StartsWith("Assets", System.StringComparison.OrdinalIgnoreCase))
			{
				folderPath = "Assets/" + folderPath;
			}

			if (AssetDatabase.IsValidFolder(folderPath))
			{
				return true;
			}

			string[] parts = folderPath.Split('/');
			if (parts.Length == 0 || !string.Equals(parts[0], "Assets", System.StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			string current = "Assets";
			for (int i = 1; i < parts.Length; i++)
			{
				string part = parts[i];
				if (string.IsNullOrEmpty(part))
				{
					continue;
				}

				string next = current + "/" + part;
				if (!AssetDatabase.IsValidFolder(next))
				{
					AssetDatabase.CreateFolder(current, part);
				}
				current = next;
			}

			return AssetDatabase.IsValidFolder(folderPath);
		}
	}
}
