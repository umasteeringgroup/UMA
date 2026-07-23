using System.Collections.Generic;
using System.Text;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Compact live summary of the UMA under the active selection.
    /// </summary>
    public sealed class UMAToolbarDiagnosticsWindow : EditorWindow
    {
        private Vector2 scrollPosition;

        [MenuItem("UMA/Debug/Selected UMA Diagnostics", priority = 100)]
        public static void OpenWindow()
        {
            UMAToolbarDiagnosticsWindow window = GetWindow<UMAToolbarDiagnosticsWindow>("UMA Diagnostics");
            window.titleContent = new GUIContent(
                "UMA Diagnostics",
                EditorGUIUtility.IconContent("d_UnityEditor.InspectorWindow").image);
            window.minSize = new Vector2(330f, 360f);
            window.Show();
        }

        private void OnEnable()
        {
            Selection.selectionChanged += Repaint;
            UMAToolbarActions.DiagnosticsChanged += Repaint;
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= Repaint;
            UMAToolbarActions.DiagnosticsChanged -= Repaint;
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }

        private void OnGUI()
        {
            DynamicCharacterAvatar avatar = UMAToolbarActions.GetActiveAvatar();
            if (avatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a DynamicCharacterAvatar or one of its child objects to inspect it.",
                    MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            DrawAvatarSummary(avatar);
            EditorGUILayout.Space(8f);
            DrawMeshSummary(avatar);
            EditorGUILayout.Space(8f);
            DrawGeneratorSummary();
            EditorGUILayout.Space(8f);
            DrawActions(avatar);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawAvatarSummary(DynamicCharacterAvatar avatar)
        {
            EditorGUILayout.LabelField("Character", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Avatar", avatar, typeof(DynamicCharacterAvatar), true);
                EditorGUILayout.TextField("Race", GetRaceName(avatar));
                EditorGUILayout.Toggle("Editor Generation", avatar.editorTimeGeneration);
                EditorGUILayout.Toggle("Generation Paused", DynamicCharacterAvatar.EditorGenerationPaused);
            }

            int skeletonBoneCount = avatar.skeleton != null && avatar.skeleton.boneHashData != null
                ? avatar.skeleton.boneHashData.Count
                : 0;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Skeleton Bones", skeletonBoneCount);
                EditorGUILayout.Toggle("Rig Dirty", avatar.isShapeDirty);
                EditorGUILayout.Toggle("Mesh Dirty", avatar.isMeshDirty);
                EditorGUILayout.Toggle("Textures Dirty", avatar.isTextureDirty);
                EditorGUILayout.Toggle("Atlas Dirty", avatar.isAtlasDirty);
            }

            string timingDescription;
            double timingMilliseconds;
            if (UMAToolbarActions.TryGetLastBuildTiming(
                    avatar,
                    out timingDescription,
                    out timingMilliseconds))
            {
                EditorGUILayout.LabelField("Last Toolbar Build", timingDescription);
                EditorGUILayout.LabelField("Elapsed", timingMilliseconds.ToString("F2") + " ms");
            }
        }

        private static void DrawMeshSummary(DynamicCharacterAvatar avatar)
        {
            SkinnedMeshRenderer[] renderers = avatar.GetRenderers();
            int rendererCount = 0;
            int vertexCount = 0;
            int triangleCount = 0;
            int subMeshCount = 0;
            int blendShapeCount = 0;
            var weightedBones = new HashSet<Transform>();

            if (renderers != null)
            {
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    SkinnedMeshRenderer renderer = renderers[rendererIndex];
                    if (renderer == null)
                    {
                        continue;
                    }

                    rendererCount++;
                    Transform[] bones = renderer.bones;
                    if (bones != null)
                    {
                        for (int boneIndex = 0; boneIndex < bones.Length; boneIndex++)
                        {
                            if (bones[boneIndex] != null)
                            {
                                weightedBones.Add(bones[boneIndex]);
                            }
                        }
                    }

                    Mesh mesh = renderer.sharedMesh;
                    if (mesh == null)
                    {
                        continue;
                    }

                    vertexCount += mesh.vertexCount;
                    subMeshCount += mesh.subMeshCount;
                    blendShapeCount += mesh.blendShapeCount;
                    for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                    {
                        if (mesh.GetTopology(subMeshIndex) == MeshTopology.Triangles)
                        {
                            triangleCount += (int)(mesh.GetIndexCount(subMeshIndex) / 3u);
                        }
                    }
                }
            }

            EditorGUILayout.LabelField("Generated Mesh", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Renderers", rendererCount);
                EditorGUILayout.IntField("Vertices", vertexCount);
                EditorGUILayout.IntField("Triangles", triangleCount);
                EditorGUILayout.IntField("Submeshes", subMeshCount);
                EditorGUILayout.IntField("Blendshapes", blendShapeCount);
                EditorGUILayout.IntField("Renderer Bones", weightedBones.Count);
            }
        }

        private static void DrawGeneratorSummary()
        {
            UMAGenerator generator = UMAToolbarActions.GetGenerator();
            EditorGUILayout.LabelField("Generator", EditorStyles.boldLabel);
            if (generator == null)
            {
                EditorGUILayout.HelpBox("No generator is assigned in the UMA Global Library.", MessageType.Warning);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Generator", generator, typeof(UMAGenerator), true);
                EditorGUILayout.IntField("Atlas Resolution", generator.atlasResolution);
            }
            EditorGUILayout.LabelField("Mesh Combiner", UMAToolbarActions.GetCurrentCombinerName(generator));
            EditorGUILayout.LabelField("Average Rig Update", generator.averageSkeletonUpdatesTime.ToString("F2") + " ms");
            EditorGUILayout.LabelField("Average Mesh Update", generator.averageMeshUpdatesTime.ToString("F2") + " ms");
            EditorGUILayout.LabelField("Average Texture Update", generator.averageTextureProcessingTime.ToString("F2") + " ms");
        }

        private static void DrawActions(DynamicCharacterAvatar avatar)
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Frame UMA"))
            {
                UMAToolbarActions.SelectAndFrame(avatar.gameObject);
            }
            if (GUILayout.Button("Runtime Viewer"))
            {
                RuntimeDataViewerWindow.Open(avatar);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Full Rebuild"))
            {
                UMAToolbarActions.RebuildSelected(UMASelectedRebuildMode.Full);
            }
            if (GUILayout.Button("Copy Summary"))
            {
                EditorGUIUtility.systemCopyBuffer = BuildClipboardSummary(avatar);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static string GetRaceName(DynamicCharacterAvatar avatar)
        {
            return avatar != null && avatar.umaRecipe != null && avatar.umaRecipe.raceData != null
                ? avatar.umaRecipe.raceData.raceName
                : "None";
        }

        private static string BuildClipboardSummary(DynamicCharacterAvatar avatar)
        {
            var summary = new StringBuilder();
            UMAGenerator generator = UMAToolbarActions.GetGenerator();
            SkinnedMeshRenderer[] renderers = avatar.GetRenderers();
            int rendererCount = 0;
            int vertexCount = 0;
            int skeletonBoneCount = avatar.skeleton != null && avatar.skeleton.boneHashData != null
                ? avatar.skeleton.boneHashData.Count
                : 0;

            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null)
                    {
                        continue;
                    }
                    rendererCount++;
                    if (renderers[i].sharedMesh != null)
                    {
                        vertexCount += renderers[i].sharedMesh.vertexCount;
                    }
                }
            }

            summary.AppendLine("UMA: " + avatar.name);
            summary.AppendLine("Race: " + GetRaceName(avatar));
            summary.AppendLine("Combiner: " + UMAToolbarActions.GetCurrentCombinerName(generator));
            summary.AppendLine("Renderers: " + rendererCount);
            summary.AppendLine("Vertices: " + vertexCount);
            summary.AppendLine("Skeleton bones: " + skeletonBoneCount);
            summary.AppendLine($"Dirty: rig={avatar.isShapeDirty}, mesh={avatar.isMeshDirty}, textures={avatar.isTextureDirty}, atlas={avatar.isAtlasDirty}");
            return summary.ToString();
        }
    }
}
