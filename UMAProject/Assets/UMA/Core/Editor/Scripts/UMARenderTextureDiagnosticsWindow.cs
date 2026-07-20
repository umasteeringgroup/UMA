using System;
using System.Collections.Generic;
using UMA;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Shows every currently loaded render texture and associates UMA atlas textures
    /// with the character that created them.
    /// </summary>
    public class UMARenderTextureDiagnosticsWindow : EditorWindow
    {
        private sealed class RenderTextureEntry
        {
            public RenderTexture texture;
            public bool isTracked;
            public UMARenderTextureTracker.Ownership ownership;
        }

        private sealed class RenderTextureGroup
        {
            public string label;
            public readonly List<RenderTextureEntry> entries = new List<RenderTextureEntry>();
        }

        private readonly List<RenderTextureGroup> groups = new List<RenderTextureGroup>();
        private readonly Dictionary<string, bool> groupExpansion = new Dictionary<string, bool>();
        private Vector2 scrollPosition;
        private bool includeUntracked = true;
        private bool autoRefresh;
        private int totalTextureCount;
        private int trackedTextureCount;
        private double nextAutoRefreshTime;

        [MenuItem("UMA/Debug/Render Texture Diagnostics", priority = 112)]
        public static void ShowWindow()
        {
            UMARenderTextureDiagnosticsWindow window = GetWindow<UMARenderTextureDiagnosticsWindow>();
            window.titleContent = new GUIContent("UMA Render Textures");
            window.minSize = new Vector2(600f, 250f);
            window.Show();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnInspectorUpdate()
        {
            if (!autoRefresh || EditorApplication.timeSinceStartup < nextAutoRefreshTime)
            {
                return;
            }

            Refresh();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
            {
                Refresh();
            }
            if (GUILayout.Button("Clean UMA Orphans...", GUILayout.Width(145f)))
            {
                CleanOrphanedRenderTextures();
            }
            autoRefresh = GUILayout.Toggle(autoRefresh, "Auto refresh", EditorStyles.miniButton, GUILayout.Width(100f));
            EditorGUI.BeginChangeCheck();
            includeUntracked = GUILayout.Toggle(includeUntracked, "Include untracked", EditorStyles.miniButton, GUILayout.Width(120f));
            if (EditorGUI.EndChangeCheck())
            {
                Refresh();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                string.Format(
                    "{0} loaded RenderTexture(s); {1} currently associated with a UMA character. " +
                    "Untracked textures are included so leaked textures from other sources remain visible.",
                    totalTextureCount,
                    trackedTextureCount),
                MessageType.Info);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                DrawGroup(groups[groupIndex]);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawGroup(RenderTextureGroup group)
        {
            bool expanded;
            if (!groupExpansion.TryGetValue(group.label, out expanded))
            {
                expanded = true;
            }

            expanded = EditorGUILayout.Foldout(
                expanded,
                string.Format("{0} ({1})", group.label, group.entries.Count),
                true);
            groupExpansion[group.label] = expanded;
            if (!expanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            for (int entryIndex = 0; entryIndex < group.entries.Count; entryIndex++)
            {
                DrawEntry(group.entries[entryIndex]);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(3f);
        }

        private static void DrawEntry(RenderTextureEntry entry)
        {
            RenderTexture texture = entry.texture;
            if (texture == null)
            {
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField(texture, typeof(RenderTexture), false, GUILayout.Width(220f));
            if (entry.isTracked && GUILayout.Button("Select Character", GUILayout.Width(115f)))
            {
                UMAData owner = FindOwner(entry.ownership.umaDataInstanceId);
                if (owner != null)
                {
                    Selection.activeObject = owner.gameObject;
                    EditorGUIUtility.PingObject(owner.gameObject);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(texture.name, EditorStyles.wordWrappedLabel);
            string details = string.Format(
                "{0} x {1} | {2} | Depth {3} | {4} | Instance {5}",
                texture.width,
                texture.height,
                texture.format,
                texture.depth,
                texture.IsCreated() ? "Created" : "Not created",
                texture.GetUmaObjectId());
            if (entry.isTracked)
            {
                details += entry.ownership.temporary ? " | Temporary" : " | Persistent atlas";
            }
            EditorGUILayout.LabelField(details, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void CleanOrphanedRenderTextures()
        {
            List<RenderTexture> orphanedTextures = UMARenderTextureTracker.FindOrphanedRenderTextures();
            for (int textureIndex = orphanedTextures.Count - 1; textureIndex >= 0; textureIndex--)
            {
                RenderTexture texture = orphanedTextures[textureIndex];
                if (texture == null || EditorUtility.IsPersistent(texture))
                {
                    orphanedTextures.RemoveAt(textureIndex);
                }
            }

            if (orphanedTextures.Count == 0)
            {
                EditorUtility.DisplayDialog("UMA Render Textures", "No orphaned UMA RenderTextures were found.", "OK");
                Refresh();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "Clean UMA RenderTextures",
                    string.Format(
                        "Release {0} UMA RenderTexture(s) that are not referenced by any current character? " +
                        "Textures used by pending GPU readbacks are excluded.",
                        orphanedTextures.Count),
                    "Release and Destroy",
                    "Cancel"))
            {
                return;
            }

            int releasedTemporaryCount = 0;
            int destroyedPersistentCount = 0;
            for (int textureIndex = 0; textureIndex < orphanedTextures.Count; textureIndex++)
            {
                RenderTexture texture = orphanedTextures[textureIndex];
                if (texture == null)
                {
                    continue;
                }

                UMARenderTextureTracker.Ownership ownership;
                if (!UMARenderTextureTracker.TryGetOwnership(texture, out ownership))
                {
                    continue;
                }

                if (ownership.temporary)
                {
                    UMARenderTextureTracker.ReleaseTemporary(texture);
                    releasedTemporaryCount++;
                }
                else
                {
                    UMARenderTextureTracker.Untrack(texture);
                    if (texture.IsCreated())
                    {
                        texture.Release();
                    }
                    UMAUtils.DestroySceneObject(texture);
                    destroyedPersistentCount++;
                }
            }

            Debug.Log(string.Format(
                "[UMA Render Textures] Cleaned {0} orphaned persistent texture(s) and returned {1} temporary texture(s) to Unity's pool.",
                destroyedPersistentCount,
                releasedTemporaryCount));
            Refresh();
        }

        private static UMAData FindOwner(UMAObjectId umaDataInstanceId)
        {
            UMAData[] umaDataComponents = Resources.FindObjectsOfTypeAll<UMAData>();
            for (int index = 0; index < umaDataComponents.Length; index++)
            {
                UMAData umaData = umaDataComponents[index];
                if (umaData != null && umaData.GetUmaObjectId() == umaDataInstanceId)
                {
                    return umaData;
                }
            }

            return null;
        }

        private void Refresh()
        {
            UMARenderTextureTracker.RefreshOwnersFromLiveUMAData();

            groups.Clear();
            totalTextureCount = 0;
            trackedTextureCount = 0;
            Dictionary<string, RenderTextureGroup> groupsByLabel = new Dictionary<string, RenderTextureGroup>();
            RenderTexture[] renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>();
            for (int textureIndex = 0; textureIndex < renderTextures.Length; textureIndex++)
            {
                RenderTexture texture = renderTextures[textureIndex];
                if (texture == null)
                {
                    continue;
                }

                totalTextureCount++;
                UMARenderTextureTracker.Ownership ownership;
                bool isTracked = UMARenderTextureTracker.TryGetOwnership(texture, out ownership);
                if (isTracked)
                {
                    trackedTextureCount++;
                }
                else if (!includeUntracked)
                {
                    continue;
                }

                string groupLabel = isTracked
                    ? ownership.CharacterLabel
                    : "Untracked / Non-UMA";
                RenderTextureGroup group;
                if (!groupsByLabel.TryGetValue(groupLabel, out group))
                {
                    group = new RenderTextureGroup { label = groupLabel };
                    groupsByLabel.Add(groupLabel, group);
                    groups.Add(group);
                }

                group.entries.Add(new RenderTextureEntry
                {
                    texture = texture,
                    isTracked = isTracked,
                    ownership = ownership
                });
            }

            groups.Sort((left, right) => string.Compare(left.label, right.label, StringComparison.OrdinalIgnoreCase));
            for (int groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                groups[groupIndex].entries.Sort((left, right) =>
                    string.Compare(left.texture.name, right.texture.name, StringComparison.OrdinalIgnoreCase));
            }

            nextAutoRefreshTime = EditorApplication.timeSinceStartup + 1d;
            Repaint();
        }
    }
}
