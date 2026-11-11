using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Bakes a race's rest skeleton into a <see cref="UMARigRestPoseAsset"/>, so another race can
    /// name it as its legacy rig without the baked race having to be in the build.
    /// </summary>
    public static class UMARigRestPoseAssetEditorUtility
    {
        private const string MenuPath = "UMA/Tools/Slot Tools/Bake Rig Rest Pose From Selected Race";

        /// <summary>
        /// Bakes <paramref name="race"/>'s rest pose to <paramref name="assetPath"/>. An asset already
        /// there is updated in place, keeping its GUID and every reference to it.
        /// </summary>
        public static UMARigRestPoseAsset Bake(RaceData race, string assetPath)
        {
            if (race == null)
            {
                throw new ArgumentNullException(nameof(race));
            }
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new ArgumentException("An asset path is required.", nameof(assetPath));
            }

            UMARigRestPose rig = UMARigRestPose.FromRace(race);
            var asset = AssetDatabase.LoadAssetAtPath<UMARigRestPoseAsset>(assetPath);
            if (asset == null)
            {
                EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
                asset = ScriptableObject.CreateInstance<UMARigRestPoseAsset>();
                asset.SetFrom(rig, race.raceName);
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                asset.SetFrom(rig, race.raceName);
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            UMALegacySlotConverter.ClearCache();
            return asset;
        }

        [MenuItem(MenuPath)]
        private static void BakeSelected()
        {
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                if (!(selected is RaceData race))
                {
                    continue;
                }
                string racePath = AssetDatabase.GetAssetPath(race);
                string folder = string.IsNullOrEmpty(racePath) ? "Assets" : Path.GetDirectoryName(racePath).Replace('\\', '/');
                string assetPath = folder + "/" + race.raceName + " Rig Rest Pose.asset";
                try
                {
                    UMARigRestPoseAsset asset = Bake(race, assetPath);
                    Debug.Log($"[UMA] Baked the rest pose of '{race.raceName}' ({asset.bones.Length} bones) to {assetPath}", asset);
                    EditorGUIUtility.PingObject(asset);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[UMA] Could not bake the rest pose of '{race.raceName}': {ex.Message}", race);
                }
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool CanBakeSelected()
        {
            foreach (UnityEngine.Object selected in Selection.objects)
            {
                if (selected is RaceData)
                {
                    return true;
                }
            }
            return false;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }

    /// <summary>
    /// Automatic legacy slot conversion keeps its converted copies for the session. When an asset
    /// changes they are dropped, so an edited slot, race, rest pose or profile is picked up by the
    /// next build.
    /// </summary>
    internal sealed class UMALegacySlotConverterCacheReset : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            for (int i = 0; i < importedAssets.Length; i++)
            {
                if (importedAssets[i].EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    UMALegacySlotConverter.ClearCache();
                    return;
                }
            }
        }
    }
}
