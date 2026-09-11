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
                HairGroomAsset asset = CreateGroomForMesh(mesh, sourceObject: selected);
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
                renderer.name, avatar.name + "_HairGroom", avatar);
            if (groom != null) HairCardStage.ShowStage(groom, avatar);
        }

        private static HairGroomAsset CreateGroomForMesh(Mesh mesh, string raceName = null,
            string slotName = null, string defaultName = null, UnityEngine.Object sourceObject = null)
        {
            if (mesh == null) return null;
            if (!mesh.isReadable)
            {
                EditorUtility.DisplayDialog("Create Hair Groom", "The source mesh must have Read/Write enabled.", "OK");
                return null;
            }
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
                "Choose where to save the editable HairGroomAsset.",
                HairGroomCreationLocation.GetLastFolder());
            if (string.IsNullOrEmpty(path)) return null;
            HairCardStage.ActiveStage?.SaveNow(false);
            HairGroomAsset groom = ScriptableObject.CreateInstance<HairGroomAsset>();
            groom.name = Path.GetFileNameWithoutExtension(path);
            string meshPath = AssetDatabase.GetAssetPath(mesh);
            string stableId = !string.IsNullOrEmpty(meshPath)
                ? "asset:" + AssetDatabase.AssetPathToGUID(meshPath)
                : "generated:" + HairStableId.Create();
            groom.SetSource(mesh, stableId, raceName, slotName);
            AssetDatabase.CreateAsset(groom, path);
            HairGroomSourcePersistence.RememberOrigin(groom, sourceObject != null ? sourceObject : mesh);
            HairGroomSourcePersistence.EnsurePersistent(groom);
            HairGroomCreationLocation.RememberAssetPath(path);
            Undo.RegisterCreatedObjectUndo(groom, "Create Hair Groom");
            HairEditorPreferences.instance.ApplySetupToNewGroom(groom);
            if (groom.Groups[0].profile == null) groom.Groups[0].profile = CreateDefaultProfileNear(groom);
            // Keep the new output name unique to this groom instead of inheriting a previous
            // bake target that could be overwritten by the first bake.
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

    internal static class HairGroomSourcePersistence
    {
        private const string SnapshotName = "Hair Groom Source Snapshot";

        internal static void RememberOrigin(HairGroomAsset groom, UnityEngine.Object original)
        {
            if (groom == null || original == null) return;
            // Never store a transient mesh's object identifier. Generated meshes instead
            // remember their scene character/renderer when one was supplied.
            if (original is Mesh && !EditorUtility.IsPersistent(original)) return;
            if (original is Mesh && AssetDatabase.GetAssetPath(original) == AssetDatabase.GetAssetPath(groom)) return;
            groom.SetSourceObjectId(GlobalObjectId.GetGlobalObjectIdSlow(original).ToString());
            EditorUtility.SetDirty(groom);
        }

        internal static UnityEngine.Object ResolveOrigin(HairGroomAsset groom)
        {
            return groom != null && !string.IsNullOrEmpty(groom.SourceObjectId) &&
                GlobalObjectId.TryParse(groom.SourceObjectId, out GlobalObjectId id)
                ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) : null;
        }

        internal static DynamicCharacterAvatar ResolveAvatar(HairGroomAsset groom)
        {
            UnityEngine.Object original = ResolveOrigin(groom);
            if (original is Component component) return component.GetComponentInParent<DynamicCharacterAvatar>();
            return (original as GameObject)?.GetComponentInParent<DynamicCharacterAvatar>();
        }

        internal static Mesh ResolveMesh(UnityEngine.Object original)
        {
            if (original is Mesh mesh) return mesh;
            if (original is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            if (original is MeshFilter filter) return filter.sharedMesh;
            GameObject owner = original is GameObject gameObject ? gameObject : (original as Component)?.gameObject;
            if (owner == null) return null;
            DynamicCharacterAvatar avatar = owner.GetComponentInParent<DynamicCharacterAvatar>();
            if (avatar?.umaData != null)
            {
                SkinnedMeshRenderer renderer = avatar.umaData.GetRenderer(0);
                if (renderer != null) return renderer.sharedMesh;
            }
            return owner.GetComponent<MeshFilter>()?.sharedMesh ?? owner.GetComponent<SkinnedMeshRenderer>()?.sharedMesh ??
                owner.GetComponentInChildren<SkinnedMeshRenderer>(true)?.sharedMesh;
        }

        internal static bool EnsurePersistent(HairGroomAsset groom)
        {
            if (groom == null || groom.SourceMesh == null || !groom.SourceMesh.isReadable) return false;
            if (EditorUtility.IsPersistent(groom.SourceMesh)) return true;
            string path = AssetDatabase.GetAssetPath(groom);
            if (string.IsNullOrEmpty(path)) return false;
            Mesh source = groom.SourceMesh;
            // Keep exact vertex order, submeshes, skin weights, bind poses and UVs. Rebuilding
            // a character later can change all of those and invalidate painted/root bindings.
            Mesh snapshot = UnityEngine.Object.Instantiate(source);
            // Source bindings use base geometry and skinning, not the character's animation
            // blendshape payload (which can dwarf the mesh itself).
            snapshot.ClearBlendShapes();
            snapshot.name = SnapshotName;
            snapshot.hideFlags = HideFlags.None;
            AssetDatabase.AddObjectToAsset(snapshot, groom);
            AssetDatabase.SetMainObject(groom, path);
            groom.SetSource(snapshot, groom.SourceMeshId, groom.SourceRace, groom.SourceSlot);
            EditorUtility.SetDirty(snapshot);
            EditorUtility.SetDirty(groom);
            AssetDatabase.SaveAssetIfDirty(groom);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return true;
        }

        internal static bool CanRestore(HairGroomAsset groom, Mesh mesh)
        {
            return groom != null && mesh != null && mesh.isReadable &&
                !string.IsNullOrEmpty(groom.SourceTopologySignature) &&
                string.Equals(groom.SourceTopologySignature, HairMeshUtility.ComputeTopologySignature(mesh), StringComparison.Ordinal);
        }

        internal static bool RestoreFrom(HairGroomAsset groom, UnityEngine.Object original, out string message)
        {
            Mesh mesh = ResolveMesh(original);
            if (!CanRestore(groom, mesh))
            {
                message = "The replacement must be readable and have exactly the original vertex/triangle topology. Regenerate the original character with the same race, wardrobe and LOD, or select its original mesh. No groom data was changed.";
                return false;
            }
            if (!EditorUtility.IsPersistent(groom))
            {
                message = "Save the groom asset before repairing its source.";
                return false;
            }
            Undo.RecordObject(groom, "Restore Hair Groom Source");
            groom.SetSource(mesh, groom.SourceMeshId, groom.SourceRace, groom.SourceSlot);
            RememberOrigin(groom, original);
            EnsurePersistent(groom);
            EditorUtility.SetDirty(groom);
            AssetDatabase.SaveAssetIfDirty(groom);
            message = "Source restored and saved. Existing maps, guides and source identity were preserved.";
            return true;
        }

        internal static bool TryEnsureSource(HairGroomAsset groom, out string message)
        {
            if (groom.SourceMesh != null)
            {
                if (!groom.SourceMesh.isReadable)
                {
                    message = "The source mesh is not readable. Enable Read/Write on its importer, or use Restore Source in the groom Inspector.";
                    return false;
                }
                if (EditorUtility.IsPersistent(groom)) EnsurePersistent(groom);
                message = null;
                return true;
            }
            // Prefer the exact embedded snapshot, never a random similarly named scene mesh.
            string path = AssetDatabase.GetAssetPath(groom);
            Mesh candidate = null;
            int matches = 0;
            if (!string.IsNullOrEmpty(path))
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is Mesh snapshot && snapshot.name == SnapshotName && CanRestore(groom, snapshot))
                    { candidate = snapshot; matches++; }
            if (matches == 1) return RestoreFrom(groom, candidate, out message);
            UnityEngine.Object original = ResolveOrigin(groom);
            if (CanRestore(groom, ResolveMesh(original))) return RestoreFrom(groom, original, out message);
            // Legacy imported meshes stored a GUID but no local file ID. Recover only if
            // exactly one mesh in that asset has the bound topology.
            if (groom.SourceMeshId?.StartsWith("asset:", StringComparison.Ordinal) == true)
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(groom.SourceMeshId.Substring(6));
                candidate = null; matches = 0;
                if (!string.IsNullOrEmpty(sourcePath))
                    foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(sourcePath))
                        if (asset is Mesh mesh && CanRestore(groom, mesh)) { candidate = mesh; matches++; }
                if (matches == 1) return RestoreFrom(groom, candidate, out message);
            }
            message = "The saved source mesh is missing. Older generated-character grooms could reference a temporary mesh that was never saved. In this groom's Inspector, select the original generated character/renderer or mesh under Restore Source, then click Restore Source. The topology must match; existing painted maps and guides will be kept.";
            return false;
        }
    }

    internal static class HairGroomCreationLocation
    {
        internal const string LastFolderEditorPrefKey = "UMA.HairCards.LastGroomFolder";
        internal const string DefaultFolder = "Assets";

        internal static string GetLastFolder()
        {
            string folder = NormalizeFolder(EditorPrefs.GetString(LastFolderEditorPrefKey, DefaultFolder));
            if (AssetDatabase.IsValidFolder(folder)) return folder;

            // Folder assets can be renamed or deleted between sessions. Self-heal the preference so
            // the save panel always receives a valid project-relative directory.
            EditorPrefs.SetString(LastFolderEditorPrefKey, DefaultFolder);
            return DefaultFolder;
        }

        internal static void RememberAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return;

            string normalizedPath = assetPath.Replace('\\', '/');
            string folder = NormalizeFolder(Path.GetDirectoryName(normalizedPath));
            if (AssetDatabase.IsValidFolder(folder))
                EditorPrefs.SetString(LastFolderEditorPrefKey, folder);
        }

        private static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder)) return DefaultFolder;
            return folder.Replace('\\', '/').TrimEnd('/');
        }
    }

    [CustomEditor(typeof(HairGroomAsset))]
    internal sealed class HairGroomAssetEditor : UnityEditor.Editor
    {
        private UnityEngine.Object repairSource;
        private string repairStatus;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("HairGroomAsset is an editable procedural source. Generated card meshes are disposable bake outputs.", MessageType.Info);
            HairGroomAsset groom = (HairGroomAsset)target;
            if (groom.SourceMesh == null || !groom.SourceMesh.isReadable)
            {
                EditorGUILayout.HelpBox("The source mesh is missing or unreadable. Restore from the original mesh or generated character; matching topology is required. This preserves the remaining paint and guides.", MessageType.Warning);
                repairSource = EditorGUILayout.ObjectField("Restore Source From", repairSource, typeof(UnityEngine.Object), true);
                using (new EditorGUI.DisabledScope(repairSource == null))
                    if (GUILayout.Button("Restore Source"))
                        HairGroomSourcePersistence.RestoreFrom(groom, repairSource, out repairStatus);
                if (!string.IsNullOrEmpty(repairStatus)) EditorGUILayout.HelpBox(repairStatus, MessageType.Info);
            }
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
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();
        }
    }
}
