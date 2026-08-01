#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

public static class IconCreatorSpriteAtlasUtility
{
    private const string AtlasFolderName = "SpriteAtlases";
    private const string AtlasNamePrefix = "UMAIcons_";

    public sealed class RebuildResult
    {
        public string OutputFolder { get; }
        public int RecipeCount { get; }
        public int SpriteCount { get; }
        public int AtlasCount { get; }
        public int ClearedAtlasCount { get; }
        public int WarningCount { get; }

        public RebuildResult(
            string outputFolder,
            int recipeCount,
            int spriteCount,
            int atlasCount,
            int clearedAtlasCount,
            int warningCount)
        {
            OutputFolder = outputFolder;
            RecipeCount = recipeCount;
            SpriteCount = spriteCount;
            AtlasCount = atlasCount;
            ClearedAtlasCount = clearedAtlasCount;
            WarningCount = warningCount;
        }
    }

    public static string GetAtlasFolder(string rootFolder)
    {
        return GetAssetRootFolder(rootFolder) + "/" + AtlasFolderName;
    }

    public static RebuildResult Rebuild(string rootFolder)
    {
        string atlasFolder = GetAtlasFolder(rootFolder);
        EnsureAssetFolder(atlasFolder);

        var warnings = new List<string>();
        var sourceAssignments = new Dictionary<string, AtlasGroupKey>(StringComparer.OrdinalIgnoreCase);
        int recipeCount;
        Dictionary<AtlasGroupKey, HashSet<Sprite>> groups = CollectGroups(
            sourceAssignments,
            warnings,
            out recipeCount);

        WarnAboutExternalAtlasAssignments(sourceAssignments, atlasFolder, warnings);

        var groupKeys = new List<AtlasGroupKey>(groups.Keys);
        groupKeys.Sort(CompareGroupKeys);
        var rebuiltPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedAtlasPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int spriteCount = 0;

        for (int i = 0; i < groupKeys.Count; i++)
        {
            AtlasGroupKey groupKey = groupKeys[i];
            List<Sprite> sprites = GetSortedSprites(groups[groupKey]);
            string atlasPath = GetUniqueAtlasPath(atlasFolder, groupKey, usedAtlasPaths);
            RebuildAtlas(atlasPath, sprites);
            rebuiltPaths.Add(atlasPath);
            spriteCount += sprites.Count;
        }

        int clearedAtlasCount = ClearObsoleteAtlasPackables(atlasFolder, rebuiltPaths);
        AssetDatabase.SaveAssets();

        for (int i = 0; i < warnings.Count; i++)
        {
            Debug.LogWarning("[IconCreator] " + warnings[i]);
        }

        return new RebuildResult(
            atlasFolder,
            recipeCount,
            spriteCount,
            groupKeys.Count,
            clearedAtlasCount,
            warnings.Count);
    }

    private static Dictionary<AtlasGroupKey, HashSet<Sprite>> CollectGroups(
        Dictionary<string, AtlasGroupKey> sourceAssignments,
        List<string> warnings,
        out int recipeCount)
    {
        var groups = new Dictionary<AtlasGroupKey, HashSet<Sprite>>();
        string[] recipeGuids = AssetDatabase.FindAssets("t:UMAWardrobeRecipe");
        var recipePaths = new List<string>(recipeGuids.Length);
        for (int i = 0; i < recipeGuids.Length; i++)
        {
            recipePaths.Add(AssetDatabase.GUIDToAssetPath(recipeGuids[i]));
        }
        recipePaths.Sort(ComparePaths);
        recipeCount = 0;

        for (int i = 0; i < recipePaths.Count; i++)
        {
            UMAWardrobeRecipe recipe = AssetDatabase.LoadAssetAtPath<UMAWardrobeRecipe>(recipePaths[i]);
            if (recipe == null || recipe.wardrobeRecipeThumbs == null || string.IsNullOrEmpty(recipe.wardrobeSlot))
            {
                continue;
            }

            bool hasReferencedThumbnail = false;
            for (int thumbIndex = 0; thumbIndex < recipe.wardrobeRecipeThumbs.Count; thumbIndex++)
            {
                WardrobeRecipeThumb thumbnail = recipe.wardrobeRecipeThumbs[thumbIndex];
                if (thumbnail == null || thumbnail.thumb == null || string.IsNullOrEmpty(thumbnail.race))
                {
                    continue;
                }

                string sourcePath = AssetDatabase.GetAssetPath(thumbnail.thumb);
                if (string.IsNullOrEmpty(sourcePath))
                {
                    continue;
                }
                hasReferencedThumbnail = true;

                var groupKey = new AtlasGroupKey(thumbnail.race, recipe.wardrobeSlot);
                if (sourceAssignments.TryGetValue(sourcePath, out AtlasGroupKey assignedGroup))
                {
                    if (!assignedGroup.Equals(groupKey))
                    {
                        warnings.Add(
                            "Sprite '" + sourcePath + "' is referenced by conflicting thumbnail groups " +
                            assignedGroup + " and " + groupKey + ". It remains assigned to " + assignedGroup + ".");
                    }
                    continue;
                }

                sourceAssignments.Add(sourcePath, groupKey);
                if (!groups.TryGetValue(groupKey, out HashSet<Sprite> sprites))
                {
                    sprites = new HashSet<Sprite>();
                    groups.Add(groupKey, sprites);
                }
                sprites.Add(thumbnail.thumb);
            }

            if (hasReferencedThumbnail)
            {
                recipeCount++;
            }
        }

        return groups;
    }

    private static void WarnAboutExternalAtlasAssignments(
        Dictionary<string, AtlasGroupKey> sourceAssignments,
        string atlasFolder,
        List<string> warnings)
    {
        string[] atlasGuids = AssetDatabase.FindAssets("t:SpriteAtlas");
        var reportedConflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string atlasGuid in atlasGuids)
        {
            string atlasPath = AssetDatabase.GUIDToAssetPath(atlasGuid);
            if (IsManagedAtlasPath(atlasPath, atlasFolder))
            {
                continue;
            }

            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                continue;
            }

            UnityEngine.Object[] packables = SpriteAtlasExtensions.GetPackables(atlas);
            for (int i = 0; i < packables.Length; i++)
            {
                string packablePath = AssetDatabase.GetAssetPath(packables[i]);
                if (string.IsNullOrEmpty(packablePath))
                {
                    continue;
                }

                foreach (KeyValuePair<string, AtlasGroupKey> assignment in sourceAssignments)
                {
                    bool isFolderAssignment = AssetDatabase.IsValidFolder(packablePath) &&
                        assignment.Key.StartsWith(packablePath + "/", StringComparison.OrdinalIgnoreCase);
                    if (!assignment.Key.Equals(packablePath, StringComparison.OrdinalIgnoreCase) && !isFolderAssignment)
                    {
                        continue;
                    }

                    string conflictKey = assignment.Key + "|" + atlasPath;
                    if (reportedConflicts.Add(conflictKey))
                    {
                        warnings.Add(
                            "Sprite '" + assignment.Key + "' is already packable in atlas '" + atlasPath +
                            "' and will also be assigned to " + assignment.Value + ".");
                    }
                }
            }
        }
    }

    private static void RebuildAtlas(string atlasPath, List<Sprite> sprites)
    {
        SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
        if (atlas == null)
        {
            atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, atlasPath);
            Undo.RegisterCreatedObjectUndo(atlas, "Create Thumbnail Sprite Atlas");
        }
        else
        {
            Undo.RecordObject(atlas, "Rebuild Thumbnail Sprite Atlas");
        }

        UnityEngine.Object[] currentPackables = SpriteAtlasExtensions.GetPackables(atlas);
        if (currentPackables.Length > 0)
        {
            SpriteAtlasExtensions.Remove(atlas, currentPackables);
        }

        var packables = new UnityEngine.Object[sprites.Count];
        for (int i = 0; i < sprites.Count; i++)
        {
            packables[i] = sprites[i];
        }
        if (packables.Length > 0)
        {
            SpriteAtlasExtensions.Add(atlas, packables);
        }

        SpriteAtlasPackingSettings packingSettings = SpriteAtlasExtensions.GetPackingSettings(atlas);
        packingSettings.padding = 4;
        packingSettings.enableRotation = false;
        SpriteAtlasExtensions.SetPackingSettings(atlas, packingSettings);

        SpriteAtlasTextureSettings textureSettings = SpriteAtlasExtensions.GetTextureSettings(atlas);
        textureSettings.generateMipMaps = false;
        textureSettings.filterMode = FilterMode.Bilinear;
        textureSettings.sRGB = true;
        SpriteAtlasExtensions.SetTextureSettings(atlas, textureSettings);

        TextureImporterPlatformSettings platformSettings =
            SpriteAtlasExtensions.GetPlatformSettings(atlas, "DefaultTexturePlatform");
        platformSettings.maxTextureSize = 2048;
        platformSettings.textureCompression = TextureImporterCompression.Compressed;
        SpriteAtlasExtensions.SetPlatformSettings(atlas, platformSettings);
        SpriteAtlasExtensions.SetIncludeInBuild(atlas, true);

        EditorUtility.SetDirty(atlas);
    }

    private static int ClearObsoleteAtlasPackables(
        string atlasFolder,
        HashSet<string> rebuiltPaths)
    {
        int clearedCount = 0;
        string[] atlasGuids = AssetDatabase.FindAssets("t:SpriteAtlas", new[] { atlasFolder });
        for (int i = 0; i < atlasGuids.Length; i++)
        {
            string atlasPath = AssetDatabase.GUIDToAssetPath(atlasGuids[i]);
            if (!IsManagedAtlasPath(atlasPath, atlasFolder) || rebuiltPaths.Contains(atlasPath))
            {
                continue;
            }

            SpriteAtlas atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            if (atlas == null)
            {
                continue;
            }
            UnityEngine.Object[] packables = SpriteAtlasExtensions.GetPackables(atlas);
            if (packables.Length == 0)
            {
                continue;
            }

            Undo.RecordObject(atlas, "Clear Obsolete Thumbnail Sprite Atlas");
            SpriteAtlasExtensions.Remove(atlas, packables);
            EditorUtility.SetDirty(atlas);
            clearedCount++;
        }
        return clearedCount;
    }

    private static List<Sprite> GetSortedSprites(HashSet<Sprite> sourceSprites)
    {
        var sprites = new List<Sprite>(sourceSprites);
        sprites.Sort((left, right) =>
        {
            int pathComparison = ComparePaths(
                AssetDatabase.GetAssetPath(left),
                AssetDatabase.GetAssetPath(right));
            if (pathComparison != 0)
            {
                return pathComparison;
            }
            int nameComparison = string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
            if (nameComparison != 0)
            {
                return nameComparison;
            }
            return string.Compare(left.name, right.name, StringComparison.Ordinal);
        });
        return sprites;
    }

    private static string GetUniqueAtlasPath(
        string atlasFolder,
        AtlasGroupKey groupKey,
        HashSet<string> usedAtlasPaths)
    {
        string baseName = AtlasNamePrefix + MakeAssetName(groupKey.Race) + "_" + MakeAssetName(groupKey.Region);
        string atlasPath = atlasFolder + "/" + baseName + ".spriteatlas";
        int suffix = 2;
        while (!usedAtlasPaths.Add(atlasPath))
        {
            atlasPath = atlasFolder + "/" + baseName + "_" + suffix + ".spriteatlas";
            suffix++;
        }
        return atlasPath;
    }

    private static string MakeAssetName(string value)
    {
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (char.IsLetterOrDigit(character) || character == '_' || character == '-')
            {
                result.Append(character);
            }
        }
        return result.Length > 0 ? result.ToString() : "Unnamed";
    }

    private static string GetAssetRootFolder(string rootFolder)
    {
        string dataPath = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string projectPath = Directory.GetParent(dataPath).FullName;
        string rootPath;
        if (string.IsNullOrWhiteSpace(rootFolder))
        {
            rootPath = dataPath;
        }
        else if (Path.IsPathRooted(rootFolder))
        {
            rootPath = Path.GetFullPath(rootFolder);
        }
        else
        {
            string normalizedRoot = rootFolder.Replace('\\', '/').Trim('/');
            if (normalizedRoot.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
                normalizedRoot.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                rootPath = Path.GetFullPath(Path.Combine(projectPath, normalizedRoot));
            }
            else
            {
                rootPath = Path.GetFullPath(Path.Combine(dataPath, normalizedRoot));
            }
        }

        rootPath = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!rootPath.Equals(dataPath, StringComparison.OrdinalIgnoreCase) &&
            !rootPath.StartsWith(dataPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Thumbnail Sprite Atlases must be created inside the project's Assets folder.");
        }

        string relativePath = rootPath.Substring(dataPath.Length).Replace('\\', '/');
        return "Assets" + relativePath;
    }

    private static void EnsureAssetFolder(string assetFolder)
    {
        string[] folderParts = assetFolder.Split('/');
        string currentFolder = folderParts[0];
        for (int i = 1; i < folderParts.Length; i++)
        {
            string nextFolder = currentFolder + "/" + folderParts[i];
            if (!AssetDatabase.IsValidFolder(nextFolder))
            {
                AssetDatabase.CreateFolder(currentFolder, folderParts[i]);
            }
            currentFolder = nextFolder;
        }
    }

    private static bool IsManagedAtlasPath(string atlasPath, string atlasFolder)
    {
        string directory = Path.GetDirectoryName(atlasPath)?.Replace('\\', '/');
        string fileName = Path.GetFileName(atlasPath);
        return directory != null &&
            directory.Equals(atlasFolder, StringComparison.OrdinalIgnoreCase) &&
            fileName.StartsWith(AtlasNamePrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareGroupKeys(AtlasGroupKey left, AtlasGroupKey right)
    {
        int raceComparison = string.Compare(left.Race, right.Race, StringComparison.OrdinalIgnoreCase);
        if (raceComparison != 0)
        {
            return raceComparison;
        }
        int regionComparison = string.Compare(left.Region, right.Region, StringComparison.OrdinalIgnoreCase);
        if (regionComparison != 0)
        {
            return regionComparison;
        }

        raceComparison = string.Compare(left.Race, right.Race, StringComparison.Ordinal);
        if (raceComparison != 0)
        {
            return raceComparison;
        }
        return string.Compare(left.Region, right.Region, StringComparison.Ordinal);
    }

    private static int ComparePaths(string left, string right)
    {
        int comparison = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        return comparison != 0
            ? comparison
            : string.Compare(left, right, StringComparison.Ordinal);
    }

    private readonly struct AtlasGroupKey : IEquatable<AtlasGroupKey>
    {
        public string Race { get; }
        public string Region { get; }

        public AtlasGroupKey(string race, string region)
        {
            Race = race;
            Region = region;
        }

        public bool Equals(AtlasGroupKey other)
        {
            return string.Equals(Race, other.Race, StringComparison.Ordinal) &&
                string.Equals(Region, other.Region, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AtlasGroupKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Race != null ? Race.GetHashCode() : 0) * 397) ^
                    (Region != null ? Region.GetHashCode() : 0);
            }
        }

        public override string ToString()
        {
            return "'" + Race + "/" + Region + "'";
        }
    }
}
#endif
