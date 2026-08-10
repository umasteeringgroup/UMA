using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// The single editor surface for maintaining the UMAAssetIndexer.
    /// </summary>
    public class AssetIndexUtilities : EditorWindow
    {
        private Vector2 scrollPosition;
        private string lastResult = "No maintenance operation has been run in this window.";

        [MenuItem("UMA/Global Library Maintenance", priority = 22)]
        public static void ShowWindow()
        {
            AssetIndexUtilities window = GetWindow<AssetIndexUtilities>();
            window.titleContent = new GUIContent("Global Library Maintenance");
            window.minSize = new Vector2(430f, 420f);
            window.Show();
            window.Focus();
        }

        [MenuItem("UMA/Global Library Maintenance", true)]
        private static bool ValidateMenu()
        {
            return !EditorApplication.isCompiling;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("UMA Asset Index Maintenance", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Run UMAAssetIndexer maintenance from this window. Close the UMA Global Library before changing the index.",
                MessageType.Info);



            UMAAssetIndexer indexer = UMAAssetIndexer.Instance;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Asset Index", indexer, typeof(UMAAssetIndexer), false);
            }

            if (Application.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorGUILayout.HelpBox("Maintenance is unavailable while Unity is playing, compiling, or updating.", MessageType.Warning);
                return;
            }

            if (indexer == null)
            {
                EditorGUILayout.HelpBox("UMAAssetIndexer could not be loaded.", MessageType.Error);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawCleanupOperations(indexer);
            DrawRebuildOperations(indexer);
            DrawReferenceOperations(indexer);
            DrawBackupOperations(indexer);
            DrawDangerousOperations(indexer);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(lastResult, MessageType.None);
        }

        private void DrawCleanupOperations(UMAAssetIndexer indexer)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Cleanup", EditorStyles.boldLabel);

            if (GUILayout.Button(new GUIContent("Clean Added Types", "Remove all non-standard indexed types and their indexed items.")))
            {
                if (ConfirmAndRequireLibraryClosed(
                        "Cleanup UMA Asset Index Types",
                        "This removes all added index types and every indexed item belonging to those types. Only UMA's standard index types and their items will remain."))
                {
                    RunOperation("Clean Added Types", indexer, () =>
                    {
                        UMAAssetIndexer.TypeCleanupResult result = indexer.CleanupAddedTypes();
                        return $"Removed {result.AddedTypesRemoved} added type(s), {result.SerializedItemsRemoved} serialized item(s), " +
                               $"{result.IndexedTypeNamesRemoved} persisted type name(s), and {result.TypeFolderEntriesRemoved} orphaned type-folder entry/entries. " +
                               $"{result.SerializedItemsRemaining} serialized item(s) remain across {result.MainTypesRemaining} standard type(s).";
                    });
                }
            }

            if (GUILayout.Button(new GUIContent("Repair and Remove Invalid Items", "Remove index entries whose assets can no longer be found, then rebuild lookups.")))
            {
                if (ConfirmAndRequireLibraryClosed("Repair UMA Asset Index", "This removes serialized index entries that cannot be resolved to an asset."))
                {
                    RunOperation("Repair and Remove Invalid Items", indexer, () =>
                    {
                        indexer.BuildStringTypes();
                        indexer.RepairAndCleanup();
                        Resources.UnloadUnusedAssets();
                        return "Removed invalid entries and rebuilt the asset index.";
                    });
                }
            }

            if (GUILayout.Button(new GUIContent("Remove Duplicate Serialized Items", "Remove duplicate serialized entries, then rebuild the index.")))
            {
                if (ConfirmAndRequireLibraryClosed("Remove Duplicate Serialized Items", "This removes duplicate serialized entries from the asset index."))
                {
                    RunOperation("Remove Duplicate Serialized Items", indexer, () =>
                    {
                        int removed = indexer.RemoveDuplicateSerializedItems();
                        return $"Removed {removed} duplicate serialized item(s).";
                    });
                }
            }

            if (GUILayout.Button(new GUIContent("Rebuild Dictionaries", "Recreate type, GUID, and name lookups from the serialized index.")))
            {
                if (RequireGlobalLibraryClosed())
                {
                    RunOperation("Rebuild Dictionaries", indexer, () =>
                    {
                        indexer.BuildStringTypes();
                        indexer.UpdateSerializedDictionaryItems();
                        indexer.RebuildRaceRecipes();
                        indexer.ForceSave();
                        return "Rebuilt serialized-item dictionaries and recipe lookups.";
                    });
                }
            }
        }

        private void DrawRebuildOperations(UMAAssetIndexer indexer)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Rebuild", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Adjust the per-asset-type folder filters in the UMA Global Library window before rebuilding the index. If a type exists in the filters, then it will only be added if it is under one of the folders for that type (including subfolders)", MessageType.Info);
            if (GUILayout.Button(new GUIContent(
                    "Global Library Filters...",
                    "Configure the per-asset-type folder filters used when rebuilding the Global Library.")))
            {
                AssetIndexerFilterEditor.GetWindow();
            }
            if (GUILayout.Button("Rebuild Library From Project"))
            {
                if (ConfirmAndRequireLibraryClosed("Rebuild UMA Asset Index", "This clears the current index and scans the project again using configured type and folder filters."))
                {
                    RunOperation("Rebuild Library From Project", indexer, () =>
                    {
                        indexer.RebuildLibrary();
                        return "Rebuilt the asset index from the project.";
                    });
                }
            }

            if (GUILayout.Button("Rebuild Library From Project (Include Text Assets)"))
            {
                if (ConfirmAndRequireLibraryClosed("Rebuild UMA Asset Index", "This clears the current index and scans the project again, including TextAsset files."))
                {
                    RunOperation("Rebuild Library From Project (Include Text Assets)", indexer, () =>
                    {
                        indexer.SaveKeeps();
                        indexer.Clear();
                        indexer.BuildStringTypes();
                        indexer.AddEverything(true);
                        indexer.RestoreKeeps();
                        indexer.ForceSave();
                        Resources.UnloadUnusedAssets();
                        return "Rebuilt the asset index from the project, including TextAssets.";
                    });
                }
            }
        }

        private void DrawReferenceOperations(UMAAssetIndexer indexer)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);

            if (GUILayout.Button(new GUIContent("Clear Cached References", "Release asset references held by the index without changing indexed entries.")))
            {
                if (RequireGlobalLibraryClosed())
                {
                    RunOperation("Clear Cached References", indexer, () =>
                    {
                        indexer.RemoveReferences();
                        Resources.UnloadUnusedAssets();
                        return "Cleared cached asset references.";
                    });
                }
            }

            if (GUILayout.Button(new GUIContent("Refresh Cached References", "Reload references for non-addressable indexed items.")))
            {
                if (RequireGlobalLibraryClosed())
                {
                    RunOperation("Refresh Cached References", indexer, () =>
                    {
                        indexer.UpdateReferences();
                        return "Refreshed cached asset references.";
                    });
                }
            }

            if (GUILayout.Button("Save Asset Index"))
            {
                RunOperation("Save Asset Index", indexer, () =>
                {
                    indexer.ForceSave();
                    return "Saved the UMA asset index.";
                });
            }
        }

        private void DrawBackupOperations(UMAAssetIndexer indexer)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Backup and Restore", EditorStyles.boldLabel);

            if (GUILayout.Button("Backup Asset Index..."))
            {
                if (!RequireGlobalLibraryClosed()) return;

                string path = EditorUtility.SaveFilePanel("Backup Asset Index", "", "librarybackup", "bak");
                if (!string.IsNullOrEmpty(path))
                {
                    RunOperation("Backup Asset Index", indexer, () =>
                    {
                        File.WriteAllText(path, indexer.Backup());
                        return $"Saved an asset-index backup to '{path}'.";
                    });
                }
            }

            if (GUILayout.Button("Restore Asset Index..."))
            {
                if (!RequireGlobalLibraryClosed()) return;

                string path = EditorUtility.OpenFilePanel("Restore Asset Index", "", "bak");
                if (!string.IsNullOrEmpty(path) && Confirm("Restore UMA Asset Index", "This replaces the current serialized index with the selected backup."))
                {
                    RunOperation("Restore Asset Index", indexer, () =>
                    {
                        if (!indexer.Restore(File.ReadAllText(path)))
                        {
                            throw new InvalidOperationException("UMAAssetIndexer could not restore the selected backup.");
                        }

                        return $"Restored the asset index from '{path}'.";
                    });
                }
            }
        }

        private void DrawDangerousOperations(UMAAssetIndexer indexer)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Danger Zone", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Emptying the index removes every indexed item. Use a backup first if you may need to restore it.", MessageType.Warning);

            if (GUILayout.Button("Empty Asset Index"))
            {
                if (ConfirmAndRequireLibraryClosed("Empty UMA Asset Index", "This removes every indexed item. It does not delete project assets."))
                {
                    RunOperation("Empty Asset Index", indexer, () =>
                    {
                        indexer.Clear();
                        return "Removed every serialized item from the UMA asset index.";
                    });
                }
            }
        }

        private bool ConfirmAndRequireLibraryClosed(string title, string message)
        {
            return Confirm(title, message) && RequireGlobalLibraryClosed();
        }

        private static bool Confirm(string title, string message)
        {
            return EditorUtility.DisplayDialog(title, message + "\n\nThis operation cannot be undone.", "Continue", "Cancel");
        }

        private static bool RequireGlobalLibraryClosed()
        {
            EditorWindow[] windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            bool globalLibraryOpen = false;
            for (int i = 0; i < windows.Length; i++)
            {
                EditorWindow window = windows[i];
                if (window != null && window.GetType().Name == "AssetIndexerWindow")
                {
                    globalLibraryOpen = true;
                    break;
                }
            }

            if (!globalLibraryOpen)
            {
                return true;
            }

            EditorUtility.DisplayDialog(
                "Close the UMA Global Library",
                "The UMA Global Library window is open. Close it before changing the UMA asset index, then run the operation again.",
                "OK");
            return false;
        }

        private void RunOperation(string operationName, UMAAssetIndexer indexer, Func<string> operation)
        {
            try
            {
                string result = operation();
                lastResult = operationName + ": " + result;
                Debug.Log("[UMA] " + lastResult, indexer);
                EditorUtility.DisplayDialog("UMA Asset Index Maintenance", lastResult, "OK");
            }
            catch (Exception exception)
            {
                lastResult = operationName + " failed. See the Console for details.";
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("UMA Asset Index Maintenance", lastResult, "OK");
            }

            Repaint();
        }
    }
}
