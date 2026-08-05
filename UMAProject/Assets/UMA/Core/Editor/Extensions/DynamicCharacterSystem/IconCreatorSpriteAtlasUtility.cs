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

// Builds optional Sprite Atlas V2 assets from recipe thumbnails under the configured Icon Creator root.
// Recipes continue referencing their source Sprites; Unity resolves those Sprites to included atlases.
public static class IconCreatorSpriteAtlasUtility
{
    private const string AtlasFolderName = "SpriteAtlases";
    private const string AtlasNamePrefix = "UMAIcons_";
    public const string SpriteAtlasV2RequiredMessage =
        "Thumbnail Sprite Atlas generation is optional, but requires Sprite Atlas V2 in the Editor. " +
        "Open Edit > Project Settings > Editor and set Sprite Atlas > Mode to " +
        "'Sprite Atlas V2 - Enabled', then run the tool again.";

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
        EnsureSpriteAtlasV2Enabled(EditorSettings.spritePackerMode);

        string assetRootFolder = GetAssetRootFolder(rootFolder);
        string atlasFolder = assetRootFolder + "/" + AtlasFolderName;
        EnsureAssetFolder(atlasFolder);

        var warnings = new List<string>();
        var sourceAssignments = new Dictionary<string, AtlasGroupKey>(StringComparer.OrdinalIgnoreCase);
        int recipeCount;
        Dictionary<AtlasGroupKey, HashSet<Sprite>> groups = CollectGroups(
            assetRootFolder,
            atlasFolder,
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
            string atlasPath = GetUniqueAtlasPath(
                atlasFolder,
                groupKey,
                usedAtlasPaths);
            RebuildAtlas(atlasPath, sprites);
            rebuiltPaths.Add(atlasPath);
            spriteCount += sprites.Count;
        }

        int clearedAtlasCount = ClearObsoleteAtlasPackables(
            atlasFolder,
            rebuiltPaths);
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
        string assetRootFolder,
        string atlasFolder,
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
        // Stable recipe order makes atlas contents repeatable and makes the first assignment win
        // predictably when the same Sprite is referenced by conflicting race/region groups.
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

                string sourcePath = AssetDatabase.GetAssetPath(thumbnail.thumb).Replace('\\', '/');
                if (!IsThumbnailSourcePath(sourcePath, assetRootFolder, atlasFolder))
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

    private static bool IsThumbnailSourcePath(
        string sourcePath,
        string assetRootFolder,
        string atlasFolder)
    {
        return IsAssetInFolder(sourcePath, assetRootFolder) &&
            !IsAssetInFolder(sourcePath, atlasFolder);
    }

    private static bool IsAssetInFolder(string assetPath, string assetFolder)
    {
        if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(assetFolder))
        {
            return false;
        }

        string normalizedPath = assetPath.Replace('\\', '/');
        string normalizedFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
        return normalizedPath.StartsWith(
            normalizedFolder + "/",
            StringComparison.OrdinalIgnoreCase);
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
            if (!IsSpriteAtlasV2Path(atlasPath) || IsManagedAtlasPath(atlasPath, atlasFolder))
            {
                continue;
            }

            UnityEngine.Object[] packables = GetAtlasPackables(atlasPath);
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
        RebuildSpriteAtlasV2(atlasPath, sprites);
    }

    private static void RebuildSpriteAtlasV2(string atlasPath, List<Sprite> sprites)
    {
        SpriteAtlasAsset atlas = AssetFileExists(atlasPath)
            ? SpriteAtlasAsset.Load(atlasPath)
            : new SpriteAtlasAsset();
        if (atlas == null)
        {
            throw new InvalidOperationException("Unable to load Sprite Atlas V2 asset at '" + atlasPath + "'.");
        }

        try
        {
            SetSpriteAtlasV2Packables(atlas, sprites);
            SpriteAtlasAsset.Save(atlas, atlasPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(atlas);
        }

        AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
        ConfigureSpriteAtlasV2Importer(atlasPath);
    }

    private static void ConfigureSpriteAtlasV2Importer(string atlasPath)
    {
        SpriteAtlasImporter importer = AssetImporter.GetAtPath(atlasPath) as SpriteAtlasImporter;
        if (importer == null)
        {
            throw new InvalidOperationException("Unable to load Sprite Atlas V2 importer at '" + atlasPath + "'.");
        }

        SpriteAtlasPackingSettings packingSettings = importer.packingSettings;
        packingSettings.padding = 4;
        packingSettings.enableRotation = false;
        importer.packingSettings = packingSettings;

        SpriteAtlasTextureSettings textureSettings = importer.textureSettings;
        textureSettings.generateMipMaps = false;
        textureSettings.filterMode = FilterMode.Bilinear;
        textureSettings.sRGB = true;
        importer.textureSettings = textureSettings;

        TextureImporterPlatformSettings platformSettings =
            importer.GetPlatformSettings("DefaultTexturePlatform");
        platformSettings.maxTextureSize = 2048;
        platformSettings.textureCompression = TextureImporterCompression.Compressed;
        importer.SetPlatformSettings(platformSettings);
        // Included atlases let Unity bind the original recipe Sprite references automatically in builds.
        importer.includeInBuild = true;
        importer.SaveAndReimport();
    }

    private static int ClearObsoleteAtlasPackables(
        string atlasFolder,
        HashSet<string> rebuiltPaths)
    {
        // Keep obsolete managed atlas assets but empty their packables. Deleting or recreating them
        // would churn their .meta GUIDs and could break references outside this tool.
        int clearedCount = 0;
        string[] atlasGuids = AssetDatabase.FindAssets("t:SpriteAtlas", new[] { atlasFolder });
        for (int i = 0; i < atlasGuids.Length; i++)
        {
            string atlasPath = AssetDatabase.GUIDToAssetPath(atlasGuids[i]);
            if (!IsSpriteAtlasV2Path(atlasPath) ||
                !IsManagedAtlasPath(atlasPath, atlasFolder) ||
                rebuiltPaths.Contains(atlasPath))
            {
                continue;
            }

            UnityEngine.Object[] packables = GetAtlasPackables(atlasPath);
            if (packables.Length == 0)
            {
                continue;
            }

            SpriteAtlasAsset atlas = SpriteAtlasAsset.Load(atlasPath);
            try
            {
                SetSpriteAtlasV2Packables(atlas, Array.Empty<Sprite>());
                SpriteAtlasAsset.Save(atlas, atlasPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(atlas);
            }
            AssetDatabase.ImportAsset(atlasPath, ImportAssetOptions.ForceUpdate);
            clearedCount++;
        }
        return clearedCount;
    }

    private static UnityEngine.Object[] GetAtlasPackables(string atlasPath)
    {
        SpriteAtlasAsset atlas = SpriteAtlasAsset.Load(atlasPath);
        if (atlas == null)
        {
            return Array.Empty<UnityEngine.Object>();
        }
        try
        {
            return GetSpriteAtlasV2Packables(atlas);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(atlas);
        }
    }

    private static UnityEngine.Object[] GetSpriteAtlasV2Packables(SpriteAtlasAsset atlas)
    {
        // SpriteAtlasAsset has no public packable getter. Its serialized packable list is
        // the source data written by SpriteAtlasAsset.Save.
        var serializedAtlas = new SerializedObject(atlas);
        SerializedProperty packablesProperty =
            serializedAtlas.FindProperty("m_ImporterData.packables");
        if (packablesProperty == null || !packablesProperty.isArray)
        {
            return Array.Empty<UnityEngine.Object>();
        }

        var packables = new List<UnityEngine.Object>(packablesProperty.arraySize);
        for (int i = 0; i < packablesProperty.arraySize; i++)
        {
            UnityEngine.Object packable =
                packablesProperty.GetArrayElementAtIndex(i).objectReferenceValue;
            if (packable != null)
            {
                packables.Add(packable);
            }
        }
        return packables.ToArray();
    }

    private static void SetSpriteAtlasV2Packables(
        SpriteAtlasAsset atlas,
        IList<Sprite> sprites)
    {
        // SpriteAtlasAsset exposes no public packable setter in V2, so update the same serialized list
        // read by GetSpriteAtlasV2Packables before saving through SpriteAtlasAsset.Save.
        var serializedAtlas = new SerializedObject(atlas);
        SerializedProperty packablesProperty =
            serializedAtlas.FindProperty("m_ImporterData.packables");
        if (packablesProperty == null || !packablesProperty.isArray)
        {
            throw new InvalidOperationException("Unable to access Sprite Atlas V2 packables.");
        }

        packablesProperty.arraySize = sprites.Count;
        for (int i = 0; i < sprites.Count; i++)
        {
            packablesProperty.GetArrayElementAtIndex(i).objectReferenceValue = sprites[i];
        }
        serializedAtlas.ApplyModifiedPropertiesWithoutUndo();
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
        const string extension = ".spriteatlasv2";
        string atlasPath = atlasFolder + "/" + baseName + extension;
        int suffix = 2;
        while (!usedAtlasPaths.Add(atlasPath))
        {
            atlasPath = atlasFolder + "/" + baseName + "_" + suffix + extension;
            suffix++;
        }
        return atlasPath;
    }

    public static bool IsSpriteAtlasV2Enabled()
    {
        return IsSpriteAtlasV2Enabled(EditorSettings.spritePackerMode);
    }

    private static bool IsSpriteAtlasV2Enabled(SpritePackerMode spritePackerMode)
    {
        return spritePackerMode == SpritePackerMode.SpriteAtlasV2;
    }

    private static void EnsureSpriteAtlasV2Enabled(SpritePackerMode spritePackerMode)
    {
        if (!IsSpriteAtlasV2Enabled(spritePackerMode))
        {
            throw new InvalidOperationException(SpriteAtlasV2RequiredMessage);
        }
    }

    private static bool IsSpriteAtlasV2Path(string atlasPath)
    {
        return atlasPath.EndsWith(".spriteatlasv2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AssetFileExists(string assetPath)
    {
        // AssetDatabase can retain a stale path after an atlas file is removed outside Unity.
        // Check the project file directly before choosing between load and create.
        string projectFolder = Directory.GetParent(Application.dataPath).FullName;
        string absolutePath = Path.GetFullPath(Path.Combine(projectFolder, assetPath));
        return File.Exists(absolutePath);
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
