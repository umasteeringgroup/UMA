using System;
using System.IO;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace UMA.HairCards.Editor
{
    public static class HairCardMenu
    {
        [MenuItem("UMA/Hair Cards/Open Hair Card Stage", priority = 200)]
        public static void OpenSelectedSource()
        {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected is HairGroomAsset groom)
            {
                HairCardStage.ShowStage(groom);
                return;
            }

            DynamicCharacterAvatar avatar = ResolveAvatar(selected);
            if (avatar != null)
            {
                OpenAvatar(avatar);
                return;
            }

            Mesh mesh = selected as Mesh;
            if (mesh == null && selected is GameObject gameObject)
            {
                mesh = gameObject.GetComponent<MeshFilter>()?.sharedMesh ??
                       gameObject.GetComponent<SkinnedMeshRenderer>()?.sharedMesh;
            }
            if (mesh != null)
            {
                HairGroomAsset asset = CreateGroomForMesh(mesh);
                if (asset != null) HairCardStage.ShowStage(asset);
                return;
            }

            EditorUtility.DisplayDialog("Open Hair Card Stage",
                "Select a HairGroomAsset, readable Mesh, MeshFilter, SkinnedMeshRenderer, or generated DynamicCharacterAvatar.",
                "OK");
        }

        [MenuItem("UMA/Hair Cards/Create Groom From Selected Mesh", priority = 201)]
        private static void CreateFromSelectedMesh()
        {
            Mesh mesh = Selection.activeObject as Mesh;
            if (mesh == null)
            {
                EditorUtility.DisplayDialog("Create Hair Groom", "Select a readable Mesh asset.", "OK");
                return;
            }
            HairGroomAsset groom = CreateGroomForMesh(mesh);
            if (groom != null) Selection.activeObject = groom;
        }

        [MenuItem("Assets/Open in Hair Card Stage", priority = 1900)]
        private static void OpenAssetContext()
        {
            OpenSelectedSource();
        }

        [MenuItem("Assets/Open in Hair Card Stage", true)]
        private static bool ValidateOpenAssetContext()
        {
            return Selection.activeObject is HairGroomAsset || Selection.activeObject is Mesh ||
                   ResolveAvatar(Selection.activeObject) != null;
        }

        public static HairCardProfileAsset CreateDefaultProfileNear(HairGroomAsset groom)
        {
            string groomPath = AssetDatabase.GetAssetPath(groom);
            string folder = string.IsNullOrEmpty(groomPath) ? "Assets" : Path.GetDirectoryName(groomPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + groom.name + "_RibbonProfile.asset");
            HairCardProfileAsset profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            profile.name = Path.GetFileNameWithoutExtension(path);
            profile.Configure(HairCardShape.Ribbon, 0.012f, 0f, 12, 6, true);
            AssetDatabase.CreateAsset(profile, path);
            Undo.RegisterCreatedObjectUndo(profile, "Create Hair Card Profile");
            AssetDatabase.SaveAssetIfDirty(profile);
            return profile;
        }

        public static HairAtlasProfileAsset CreateDefaultAtlasNear(HairGroomAsset groom)
        {
            string groomPath = AssetDatabase.GetAssetPath(groom);
            string folder = string.IsNullOrEmpty(groomPath) ? "Assets" : Path.GetDirectoryName(groomPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + groom.name + "_HairAtlas.asset");
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            atlas.name = Path.GetFileNameWithoutExtension(path);
            atlas.CreateRegion("Area 1", new Rect(0f, 0f, 1f, 1f));
            AssetDatabase.CreateAsset(atlas, path);
            Undo.RegisterCreatedObjectUndo(atlas, "Create Hair Atlas Profile");
            AssetDatabase.SaveAssetIfDirty(atlas);
            return atlas;
        }

        private static void OpenAvatar(DynamicCharacterAvatar avatar)
        {
            if (PrefabStageUtility.GetPrefabStage(avatar.gameObject) != null)
            {
                EditorUtility.DisplayDialog("Hair Card Stage Unavailable",
                    "Exit Prefab Mode and select a generated DynamicCharacterAvatar in an open scene.", "OK");
                return;
            }
            SkinnedMeshRenderer renderer = ResolveRenderer(avatar);
            if (renderer == null || renderer.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Hair Card Stage",
                    "Generate the DynamicCharacterAvatar before opening the hair authoring stage.", "OK");
                return;
            }
            HairGroomAsset groom = CreateGroomForMesh(renderer.sharedMesh, avatar.activeRace?.name,
                renderer.name, avatar.name + "_HairGroom");
            if (groom != null) HairCardStage.ShowStage(groom, avatar);
        }

        private static HairGroomAsset CreateGroomForMesh(Mesh mesh, string raceName = null,
            string slotName = null, string defaultName = null)
        {
            if (mesh == null) return null;
            try
            {
                _ = mesh.vertices;
            }
            catch (Exception)
            {
                EditorUtility.DisplayDialog("Create Hair Groom",
                    "The source mesh must have Read/Write enabled so roots and painted maps can bind to its topology.", "OK");
                return null;
            }
            string cleanName = Sanitize(string.IsNullOrWhiteSpace(defaultName) ? mesh.name + "_HairGroom" : defaultName);
            string path = EditorUtility.SaveFilePanelInProject("Save Hair Groom", cleanName, "asset",
                "Choose where to save the editable HairGroomAsset.");
            if (string.IsNullOrEmpty(path)) return null;
            HairGroomAsset groom = ScriptableObject.CreateInstance<HairGroomAsset>();
            groom.name = Path.GetFileNameWithoutExtension(path);
            string meshPath = AssetDatabase.GetAssetPath(mesh);
            string stableId = !string.IsNullOrEmpty(meshPath)
                ? "asset:" + AssetDatabase.AssetPathToGUID(meshPath)
                : "generated:" + HairStableId.Create();
            groom.SetSource(mesh, stableId, raceName, slotName);
            AssetDatabase.CreateAsset(groom, path);
            Undo.RegisterCreatedObjectUndo(groom, "Create Hair Groom");
            HairCardProfileAsset profile = CreateDefaultProfileNear(groom);
            groom.Groups[0].profile = profile;
            groom.BakeSettings.assetName = groom.name.Replace("_HairGroom", string.Empty);
            EditorUtility.SetDirty(groom);
            AssetDatabase.SaveAssetIfDirty(groom);
            Selection.activeObject = groom;
            return groom;
        }

        private static DynamicCharacterAvatar ResolveAvatar(UnityEngine.Object selected)
        {
            if (selected is DynamicCharacterAvatar avatar) return avatar;
            if (selected is GameObject gameObject) return gameObject.GetComponentInParent<DynamicCharacterAvatar>();
            if (selected is Component component) return component.GetComponentInParent<DynamicCharacterAvatar>();
            return null;
        }

        private static SkinnedMeshRenderer ResolveRenderer(DynamicCharacterAvatar avatar)
        {
            if (avatar?.umaData != null)
            {
                SkinnedMeshRenderer renderer = avatar.umaData.GetRenderer(0);
                if (renderer != null) return renderer;
            }
            return avatar != null ? avatar.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        }

        private static string Sanitize(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        [Shortcut("UMA Hair Cards/Select Tool", typeof(HairGroomWorkspace), KeyCode.Q)]
        private static void SelectTool() => SetTool(HairSceneTool.Select);

        [Shortcut("UMA Hair Cards/Paint Growth", typeof(HairGroomWorkspace), KeyCode.P)]
        private static void PaintTool() => SetTool(HairSceneTool.PaintGrowth);

        [Shortcut("UMA Hair Cards/Comb", typeof(HairGroomWorkspace), KeyCode.C)]
        private static void CombTool() => SetTool(HairSceneTool.Comb);

        [Shortcut("UMA Hair Cards/Grab", typeof(HairGroomWorkspace), KeyCode.G)]
        private static void GrabTool() => SetTool(HairSceneTool.Grab);

        [Shortcut("UMA Hair Cards/Smooth", typeof(HairGroomWorkspace), KeyCode.S)]
        private static void SmoothTool() => SetTool(HairSceneTool.Smooth);

        [Shortcut("UMA Hair Cards/Rebuild Preview", typeof(HairGroomWorkspace), KeyCode.R,
            ShortcutModifiers.Shift)]
        private static void RebuildPreview() => HairCardStage.ActiveStage?.QueueRebuild(true);

        [Shortcut("UMA Hair Cards/Toggle Growth X Mirror", typeof(HairGroomWorkspace), KeyCode.M)]
        private static void ToggleGrowthXMirror()
        {
            HairCardStage stage = HairCardStage.ActiveStage;
            if (stage != null) stage.MirrorPaintX = !stage.MirrorPaintX;
        }

        private static void SetTool(HairSceneTool tool)
        {
            if (HairCardStage.ActiveStage != null) HairCardStage.ActiveStage.SceneTool = tool;
        }
    }

    [CustomEditor(typeof(HairGroomAsset))]
    internal sealed class HairGroomAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("HairGroomAsset is an editable procedural source. Generated card meshes are disposable bake outputs.", MessageType.Info);
            if (GUILayout.Button("Open Hair Card Stage", GUILayout.Height(32f)))
                HairCardStage.ShowStage((HairGroomAsset)target);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate"))
                {
                    HairBakeOutcome outcome = HairBakePipeline.DryRun((HairGroomAsset)target);
                    EditorUtility.DisplayDialog("Hair Groom Validation",
                        $"{outcome.validation.ErrorCount} errors, {outcome.validation.WarningCount} warnings\n" +
                        $"{outcome.cardCount:N0} cards, {outcome.triangleCount:N0} triangles", "OK");
                }
                if (GUILayout.Button("Restore Recovery")) HairGroomRecovery.TryRestoreSnapshot((HairGroomAsset)target);
            }
            EditorGUILayout.Space(6f);
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
