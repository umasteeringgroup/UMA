using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.PackageSupport
{
    public enum UMAContentKind
    {
        Uma3,
        Uma2,
        OverlayPainter,
        OverlayPainterExamples,
        OverlayPainterTests,
        HairCards,
        HairCardsExamples,
        HairCardsTests,
        Dismemberment,
        DismembermentExamples,
        DismembermentTests
    }

    public static class UMAContentCatalog
    {
        public const string ManifestFileName = "UMAContentManifest.json";
        public const int CurrentManifestFormatVersion = 2;

        public static string Id(UMAContentKind kind) => kind switch
        {
            UMAContentKind.Uma3 => "uma3",
            UMAContentKind.Uma2 => "uma2",
            UMAContentKind.OverlayPainter => "overlay-painter",
            UMAContentKind.OverlayPainterExamples => "overlay-painter-examples",
            UMAContentKind.OverlayPainterTests => "overlay-painter-tests",
            UMAContentKind.HairCards => "hair-cards",
            UMAContentKind.HairCardsExamples => "hair-cards-examples",
            UMAContentKind.HairCardsTests => "hair-cards-tests",
            UMAContentKind.Dismemberment => "dismemberment",
            UMAContentKind.DismembermentExamples => "dismemberment-examples",
            UMAContentKind.DismembermentTests => "dismemberment-tests",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static string DisplayName(UMAContentKind kind) => kind switch
        {
            UMAContentKind.Uma3 => "UMA 3 Content",
            UMAContentKind.Uma2 => "UMA 2 Legacy Content",
            UMAContentKind.OverlayPainter => "Overlay Painter",
            UMAContentKind.OverlayPainterExamples => "Overlay Painter Examples",
            UMAContentKind.OverlayPainterTests => "Overlay Painter Tests",
            UMAContentKind.HairCards => "Hair Card Editor",
            UMAContentKind.HairCardsExamples => "Hair Card Examples",
            UMAContentKind.HairCardsTests => "Hair Card Tests",
            UMAContentKind.Dismemberment => "Dismemberment",
            UMAContentKind.DismembermentExamples => "Dismemberment Examples",
            UMAContentKind.DismembermentTests => "Dismemberment Tests",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static string Root(UMAContentKind kind) => kind switch
        {
            UMAContentKind.Uma3 => UMAPathUtility.Uma3ContentRoot,
            UMAContentKind.Uma2 => UMAPathUtility.Uma2ContentRoot,
            UMAContentKind.OverlayPainter => "Assets/UMA/OverlayPainter",
            UMAContentKind.OverlayPainterExamples => "Assets/UMA/OverlayPainterExamples",
            UMAContentKind.OverlayPainterTests => "Assets/UMA/OverlayPainterTests",
            UMAContentKind.HairCards => "Assets/UMA/HairCards",
            UMAContentKind.HairCardsExamples => "Assets/UMAProjectData/HairCards/Examples",
            UMAContentKind.HairCardsTests => "Assets/UMA/HairCards/Editor/Tests",
            UMAContentKind.Dismemberment => "Assets/UMA/UMADismemberment",
            UMAContentKind.DismembermentExamples => "Assets/UMA/UMADismemberment/Samples",
            UMAContentKind.DismembermentTests => "Assets/UMA/UMADismemberment/Tests",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static string ManifestPath(UMAContentKind kind) =>
            Root(kind) + "/" + ManifestFileName;

        public static string[] Dependencies(UMAContentKind kind) => kind switch
        {
            UMAContentKind.Uma3 => new[] { "core", "srp" },
            UMAContentKind.Uma2 => new[] { "core", "srp", "uma3" },
            UMAContentKind.OverlayPainter => new[] { "core" },
            UMAContentKind.OverlayPainterExamples => new[] { "core", "overlay-painter", "uma3", "srp" },
            UMAContentKind.OverlayPainterTests => new[] { "core", "overlay-painter", "test-framework" },
            UMAContentKind.HairCards => new[] { "core" },
            UMAContentKind.HairCardsExamples => new[] { "core", "hair-cards", "uma3", "srp" },
            UMAContentKind.HairCardsTests => new[] { "core", "hair-cards", "test-framework" },
            UMAContentKind.Dismemberment => new[] { "core" },
            UMAContentKind.DismembermentExamples => new[] { "core", "dismemberment", "uma3", "srp" },
            UMAContentKind.DismembermentTests => new[] { "core", "dismemberment", "test-framework" },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static bool IsPlugin(UMAContentKind kind) => kind >= UMAContentKind.OverlayPainter;

        public static IEnumerable<UMAContentKind> Plugins =>
            Enum.GetValues(typeof(UMAContentKind)).Cast<UMAContentKind>().Where(IsPlugin);

        public static UMAContentKind? ParentPlugin(UMAContentKind kind) => kind switch
        {
            UMAContentKind.OverlayPainterExamples or UMAContentKind.OverlayPainterTests => UMAContentKind.OverlayPainter,
            UMAContentKind.HairCardsExamples or UMAContentKind.HairCardsTests => UMAContentKind.HairCards,
            UMAContentKind.DismembermentExamples or UMAContentKind.DismembermentTests => UMAContentKind.Dismemberment,
            _ => null
        };

        public static IEnumerable<UMAContentKind> Companions(UMAContentKind kind) => Plugins.Where(p => ParentPlugin(p) == kind);
        public static IEnumerable<UMAContentKind> PluginDisplayOrder => Plugins.Where(p => ParentPlugin(p) == null)
            .SelectMany(p => new[] { p }.Concat(Companions(p)));
        public static bool IsTests(UMAContentKind kind) => Dependencies(kind).Contains("test-framework");

        public static bool IsCompanionPath(UMAContentKind parent, string path) => Companions(parent).Any(child =>
            path == Root(child) || path == Root(child) + ".meta" || path.StartsWith(Root(child) + "/", StringComparison.Ordinal));

        public static bool OwnsPluginPath(UMAContentKind kind, string path) =>
            path.StartsWith(Root(kind) + "/", StringComparison.Ordinal) && !IsCompanionPath(kind, path);

        public static string Description(UMAContentKind kind) => kind switch
        {
            UMAContentKind.OverlayPainter => "Paint and author layered textures directly on UMA characters or in the 2D texture view. Includes material channels, masks, layer effects, editable paths, and procedural generators for clothing, skin, and other surfaces.",
            UMAContentKind.OverlayPainterExamples => "Example assets and presets for learning Overlay Painter workflows. Requires Overlay Painter, UMA 3 Content, and render pipeline support.",
            UMAContentKind.OverlayPainterTests => "Automated tests for developers extending or validating Overlay Painter. Requires Overlay Painter and the Unity Test Framework; it is optional for normal painting workflows.",
            UMAContentKind.HairCards => "Create and groom hair cards on meshes and UMA characters. Paint growth regions, sculpt guides, build procedural hairstyles, author LODs, and bake meshes, slots, overlays, and wardrobe recipes. Includes runtime groom support. UMA's render pipeline support supplies the optional hair shaders and atlas textures. User grooms, exports, recovery, and preferences remain outside the plugin folder.",
            UMAContentKind.HairCardsExamples => "Optional authored hair grooms, preview assets and textures from Assets/UMAProjectData/HairCards/Examples. Requires Hair Cards, UMA 3 content and render pipeline support. Other user hair projects are not part of this package.",
            UMAContentKind.HairCardsTests => "Optional automated Hair Cards editor and mesh-generation tests. Requires Hair Cards and Unity Test Framework.",
            UMAContentKind.Dismemberment => "Runtime UMA mesh dismemberment, cut capping, detached-piece ownership and physics, surface cuts, bleeding and fluid decals. Includes editor configuration and documentation. Normal UMA avatar generation does not require this plugin.",
            UMAContentKind.DismembermentExamples => "Dismemberment demo scene, callbacks, UI helper scripts, cap materials and surface-fluid profiles. Requires Dismemberment, UMA3 Content and SRP Support.",
            UMAContentKind.DismembermentTests => "Optional automated dismemberment geometry, physics, resource and surface-cut tests. Requires Dismemberment and Unity Test Framework.",
            _ => string.Empty
        };

        public static string PackageStem(UMAContentKind kind) => kind switch
        {
            UMAContentKind.OverlayPainter => "OverlayPainter",
            UMAContentKind.OverlayPainterExamples => "OverlayPainterExamples",
            UMAContentKind.OverlayPainterTests => "OverlayPainterTests",
            UMAContentKind.HairCards => "HairCards",
            UMAContentKind.HairCardsExamples => "HairCardsExamples",
            UMAContentKind.HairCardsTests => "HairCardsTests",
            UMAContentKind.Dismemberment => "Dismemberment",
            UMAContentKind.DismembermentExamples => "DismembermentExamples",
            UMAContentKind.DismembermentTests => "DismembermentTests",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static string ManifestGuid(UMAContentKind kind) => kind switch
        {
            UMAContentKind.OverlayPainter => "baf87f8fd4ef4ae49c08b67233d50f71",
            UMAContentKind.OverlayPainterExamples => "f3d05e75b8aa4d3bb6d02fbb6e6f34b8",
            UMAContentKind.OverlayPainterTests => "5f78f34f3fa748d0b47ad8ba7609428f",
            UMAContentKind.HairCards => "6adee602b17a4f099ff31323a6c8dc4c",
            UMAContentKind.HairCardsExamples => "a35472ded5b94e1eb0cc5bce746180f1",
            UMAContentKind.HairCardsTests => "64c3ec7799ed40c4bd7a116079de4025",
            UMAContentKind.Dismemberment => "1a936e502e1b4d81b2f97b4e8ce0b563",
            UMAContentKind.DismembermentExamples => "d14a3c192acf4fce9caa7ef74dc87eb0",
            UMAContentKind.DismembermentTests => "9ea72f06bca04ee7a4d2100630cffe54",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static string[] PluginRequiredPaths(UMAContentKind kind)
        {
            string[] paths = kind switch
            {
                UMAContentKind.OverlayPainter => new[] { "Runtime/UMA.TexturePaint.Runtime.asmdef", "Editor/UMA.TexturePaint.Editor.asmdef", "Editor/TexturePaintPluginActions.cs", "Shaders/StrokeRasterize.compute" },
                UMAContentKind.OverlayPainterExamples => new[] { "Samples/Overlay Painter Document.asset" },
                UMAContentKind.OverlayPainterTests => new[] { "UMA.TexturePaint.Editor.Tests.asmdef" },
                UMAContentKind.HairCards => new[] { "Core/UMA.HairCards.Core.asmdef", "Runtime/UMA.HairCards.Runtime.asmdef", "Editor/UMA.HairCards.Editor.asmdef", "Editor/HairCardsPluginActions.cs", "UMAPluginDocumentation.json" },
                UMAContentKind.HairCardsExamples => new[] { "ShortHairPart/ShortHairPart_HairGroom.asset" },
                UMAContentKind.HairCardsTests => new[] { "UMA.HairCards.Editor.Tests.asmdef" },
                UMAContentKind.Dismemberment => new[] { "Runtime/UMA.Dismemberment.Runtime.asmdef", "Editor/UMA.Dismemberment.Editor.asmdef", "Editor/DismembermentPluginActions.cs", "UMAPluginDocumentation.json" },
                UMAContentKind.DismembermentExamples => new[] { "Scripts/UMA.Dismemberment.Samples.asmdef", "Materials/SliceFill.mat" },
                UMAContentKind.DismembermentTests => new[] { "Editor/UMA.Dismemberment.Editor.Tests.asmdef" },
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            return paths.Select(path => Root(kind) + "/" + path).ToArray();
        }
    }

    [Serializable]
    public sealed class UMAContentManifestAsset
    {
        public string path;
        public string guid;
        public long bytes;
        public string sha256;
        public long metaBytes;
        public string metaSha256;
    }

    [Serializable]
    public sealed class UMAContentManifest
    {
        public int formatVersion;
        public int requiredPluginApiVersion;
        public string contentId;
        public string contentVersion;
        public string umaVersion;
        public string requiredCoreVersion;
        public string minimumCoreVersion;
        public string maximumCoreVersionExclusive;
        public string installRoot;
        public string[] dependencies;
        public string[] requiredPaths;
        public string[] ownedPaths;
        public UMAContentManifestAsset[] assets;
    }

    public sealed class UMAContentPackageArchiveInfo
    {
        internal UMAContentPackageArchiveInfo(UMAContentManifest manifest,
            UMASrpPackageArchiveInfo archive)
        {
            Manifest = manifest;
            Archive = archive;
        }

        public UMAContentManifest Manifest { get; }
        public UMASrpPackageArchiveInfo Archive { get; }
    }

    /// <summary>
    /// Validates UMA's project-owned content installers before Unity imports
    /// them. The archive format is Unity's tar.gz based .unitypackage format.
    /// </summary>
    public static class UMAContentPackageArchiveValidator
    {
        public static bool TryValidate(string archivePath, UMAContentKind kind,
            out UMAContentPackageArchiveInfo info, out string error)
        {
            info = null;
            error = string.Empty;
            if (!UMASrpPackageArchiveValidator.TryRead(archivePath,
                    out UMASrpPackageArchiveInfo archive, out error))
                return false;

            string expectedRoot = UMAContentCatalog.Root(kind);
            string manifestPath = UMAContentCatalog.ManifestPath(kind);
            foreach (string path in archive.Paths)
            {
                if (string.Equals(path, expectedRoot,
                        StringComparison.Ordinal))
                {
                    error = "The archive contains its install-root folder record. " +
                            "That GUID can redirect an upgrade back into Packages: " + path;
                    return false;
                }
                if (!path.StartsWith(expectedRoot + "/",
                        StringComparison.Ordinal))
                {
                    error = "Archive path is outside " + expectedRoot + ": " + path;
                    return false;
                }
                if (path.EndsWith(".unitypackage",
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "The content archive contains a nested unitypackage: " + path;
                    return false;
                }
            }

            if (!archive.Paths.Any(path => string.Equals(path, manifestPath,
                    StringComparison.Ordinal)) ||
                !archive.TextByPath.TryGetValue(manifestPath,
                    out string manifestJson))
            {
                error = "The archive does not contain " + manifestPath + ".";
                return false;
            }

            UMAContentManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<UMAContentManifest>(manifestJson);
            }
            catch (Exception exception)
            {
                error = "The content manifest is invalid: " + exception.Message;
                return false;
            }

            if (!TryValidateManifestStructure(manifest, kind, out error))
                return false;

            var archivePaths = new HashSet<string>(archive.Paths,
                StringComparer.OrdinalIgnoreCase);
            var ownedPaths = new HashSet<string>(
                manifest.ownedPaths ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            if (!archivePaths.SetEquals(ownedPaths))
            {
                error = "The content manifest does not exactly describe the archive paths.";
                return false;
            }

            foreach (string requiredPath in manifest.requiredPaths ??
                     Array.Empty<string>())
            {
                if (!archivePaths.Contains(requiredPath))
                {
                    error = "The archive is missing required content: " + requiredPath;
                    return false;
                }
            }

            var describedAssets = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (UMAContentManifestAsset asset in manifest.assets ??
                     Array.Empty<UMAContentManifestAsset>())
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.path) ||
                    string.IsNullOrWhiteSpace(asset.guid) || asset.bytes < 0 ||
                    !archivePaths.Contains(asset.path) ||
                    !archive.GuidByPath.TryGetValue(asset.path, out string archiveGuid) ||
                    !string.Equals(asset.guid, archiveGuid,
                        StringComparison.OrdinalIgnoreCase) ||
                    !describedAssets.Add(asset.path))
                {
                    error = "The content manifest has an invalid asset record for " +
                            (asset?.path ?? "<null>") + ".";
                    return false;
                }
                if (asset.bytes > 0 && (asset.sha256 == null ||
                    asset.sha256.Length != 64))
                {
                    error = "The content manifest has no valid SHA-256 for " +
                            asset.path + ".";
                    return false;
                }
                bool archiveHasAsset = archive.AssetBytesByPath.TryGetValue(
                    asset.path, out long archiveAssetBytes);
                if (asset.bytes == 0)
                {
                    if (archiveHasAsset)
                    {
                        error = "Folder record unexpectedly contains asset bytes for " +
                                asset.path + ".";
                        return false;
                    }
                }
                else if (!archiveHasAsset || archiveAssetBytes != asset.bytes ||
                    !archive.AssetSha256ByPath.TryGetValue(asset.path,
                        out string archiveAssetHash) ||
                    !string.Equals(archiveAssetHash, asset.sha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Archive asset does not match its manifest hash: " +
                            asset.path + ".";
                    return false;
                }
                if (asset.metaBytes <= 0 || asset.metaSha256 == null ||
                    asset.metaSha256.Length != 64)
                {
                    error = "The content manifest has no valid importer hash for " +
                            asset.path + ".";
                    return false;
                }
                if (!archive.MetaBytesByPath.TryGetValue(asset.path,
                        out long archiveMetaBytes) ||
                    archiveMetaBytes != asset.metaBytes ||
                    !archive.MetaSha256ByPath.TryGetValue(asset.path,
                        out string archiveMetaHash) ||
                    !string.Equals(archiveMetaHash, asset.metaSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Archive importer metadata does not match its manifest hash: " +
                            asset.path + ".meta.";
                    return false;
                }
            }

            foreach (KeyValuePair<string, string> pair in archive.GuidByPath)
            {
                bool isManifest = string.Equals(pair.Key, manifestPath,
                    StringComparison.OrdinalIgnoreCase);
                if (!isManifest && !describedAssets.Contains(pair.Key))
                {
                    error = "The content manifest has no asset record for " + pair.Key + ".";
                    return false;
                }

                string registeredPath = AssetDatabase.GUIDToAssetPath(pair.Value);
                if (!string.IsNullOrEmpty(registeredPath) &&
                    registeredPath.StartsWith("Packages/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Archive GUID " + pair.Value + " for " + pair.Key +
                            " is still registered to package content at " +
                            registeredPath + ". Update or remove the old package, wait " +
                            "for Unity to refresh, and retry.";
                    return false;
                }
                bool isMovableLegacyUma2Path = kind == UMAContentKind.Uma2 &&
                    !AssetDatabase.IsValidFolder(expectedRoot) &&
                    pair.Key.StartsWith(expectedRoot + "/",
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(registeredPath,
                        "Assets/UMA2" + pair.Key.Substring(expectedRoot.Length),
                        StringComparison.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(registeredPath) &&
                    !string.Equals(registeredPath, pair.Key,
                        StringComparison.OrdinalIgnoreCase) &&
                    !isMovableLegacyUma2Path)
                {
                    error = "Archive GUID " + pair.Value + " for " + pair.Key +
                            " is already used by " + registeredPath + ".";
                    return false;
                }
            }

            info = new UMAContentPackageArchiveInfo(manifest, archive);
            return true;
        }

        public static bool TryReadInstalledManifest(UMAContentKind kind,
            out UMAContentManifest manifest, out string error)
        {
            manifest = null;
            error = string.Empty;
            string path = UMAContentCatalog.ManifestPath(kind);
            string absolutePath = UMAPathUtility.ResolveAbsolutePath(path);
            if (!File.Exists(absolutePath))
            {
                error = "No installed content manifest was found at " + path + ".";
                return false;
            }

            try
            {
                manifest = JsonUtility.FromJson<UMAContentManifest>(
                    File.ReadAllText(absolutePath));
                return TryValidateManifestStructure(manifest, kind, out error);
            }
            catch (Exception exception)
            {
                error = "Could not read the installed content manifest: " +
                        exception.Message;
                manifest = null;
                return false;
            }
        }

        public static bool TryValidateManifestStructure(UMAContentManifest manifest,
            UMAContentKind kind, out string error)
        {
            error = string.Empty;
            if (manifest == null || manifest.formatVersion < 1 ||
                manifest.formatVersion > UMAContentCatalog.CurrentManifestFormatVersion ||
                !string.Equals(manifest.contentId, UMAContentCatalog.Id(kind),
                    StringComparison.Ordinal) ||
                !string.Equals(manifest.installRoot, UMAContentCatalog.Root(kind),
                    StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(manifest.contentVersion))
            {
                error = "The content manifest has the wrong identity, root, or format version.";
                return false;
            }
            if (!TryParseSemanticVersion(manifest.contentVersion, out _))
            {
                error = "The content manifest has an invalid content version.";
                return false;
            }

            if (manifest.formatVersion == 1)
            {
                if (!TryParseSemanticVersion(manifest.requiredCoreVersion, out _))
                {
                    error = "The legacy content manifest has no valid required Core version.";
                    return false;
                }
            }
            else if (string.IsNullOrWhiteSpace(manifest.minimumCoreVersion) ||
                     string.IsNullOrWhiteSpace(manifest.maximumCoreVersionExclusive))
            {
                error = "The content manifest has no compatible Core version range.";
                return false;
            }
            else if (!TryParseSemanticVersion(manifest.minimumCoreVersion,
                         out Version minimum) ||
                     !TryParseSemanticVersion(manifest.maximumCoreVersionExclusive,
                         out Version maximum) || minimum.CompareTo(maximum) >= 0)
            {
                error = "The content manifest has an invalid Core compatibility range.";
                return false;
            }

            var actualDependencies = new HashSet<string>(
                manifest.dependencies ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);
            var expectedDependencies = new HashSet<string>(
                UMAContentCatalog.Dependencies(kind),
                StringComparer.OrdinalIgnoreCase);
            if (!actualDependencies.SetEquals(expectedDependencies) ||
                actualDependencies.Count != (manifest.dependencies ?? Array.Empty<string>()).Length)
            {
                error = UMAContentCatalog.DisplayName(kind) +
                        " must declare exactly these dependencies: " +
                        string.Join(", ", expectedDependencies) + ".";
                return false;
            }

            string rootPrefix = UMAContentCatalog.Root(kind) + "/";
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in manifest.ownedPaths ?? Array.Empty<string>())
            {
                if (!IsSafeOwnedPath(path, rootPrefix) ||
                    !owned.Add(path))
                {
                    error = "The content manifest has an invalid or duplicate owned path: " +
                            (path ?? "<null>") + ".";
                    return false;
                }
            }

            string manifestPath = UMAContentCatalog.ManifestPath(kind);
            if (!owned.Contains(manifestPath))
            {
                error = "The content manifest does not own " + manifestPath + ".";
                return false;
            }

            var assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UMAContentManifestAsset asset in manifest.assets ??
                     Array.Empty<UMAContentManifestAsset>())
            {
                if (asset == null || string.IsNullOrWhiteSpace(asset.path) ||
                    !IsSafeOwnedPath(asset.path, rootPrefix) ||
                    string.Equals(asset.path, manifestPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !owned.Contains(asset.path) || !assets.Add(asset.path) ||
                    !IsHex(asset.guid, 32) ||
                    asset.bytes < 0 || asset.metaBytes <= 0 ||
                    !IsHex(asset.metaSha256, 64) ||
                    (asset.bytes > 0 && !IsHex(asset.sha256, 64)))
                {
                    error = "The installed content manifest has an invalid asset record for " +
                            (asset?.path ?? "<null>") + ".";
                    return false;
                }
            }
            if (assets.Count + 1 != owned.Count)
            {
                error = "The content manifest owned paths and asset records do not match.";
                return false;
            }
            foreach (UMAContentManifestAsset asset in manifest.assets ??
                     Array.Empty<UMAContentManifestAsset>())
            {
                if (asset.bytes != 0)
                    continue;
                string childPrefix = asset.path.TrimEnd('/') + "/";
                bool hasPackagedDescendant = assets.Any(path => path.StartsWith(
                    childPrefix, StringComparison.OrdinalIgnoreCase));
                if (!hasPackagedDescendant)
                {
                    error = "The content manifest contains an empty leaf folder that " +
                            "Unity cannot materialize from a unitypackage: " + asset.path + ".";
                    return false;
                }
            }

            var requiredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string required in manifest.requiredPaths ?? Array.Empty<string>())
            {
                if (!IsSafeOwnedPath(required, rootPrefix) ||
                    !owned.Contains(required) || !requiredPaths.Add(required))
                {
                    error = "The content manifest has an invalid required path: " +
                            (required ?? "<null>") + ".";
                    return false;
                }
            }
            if (requiredPaths.Count == 0)
            {
                error = "The content manifest has no required paths.";
                return false;
            }
            return true;
        }

        private static bool IsSafeOwnedPath(string path, string rootPrefix)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(path, path.Trim(), StringComparison.Ordinal) ||
                !path.StartsWith(rootPrefix, StringComparison.Ordinal) ||
                path.EndsWith("/", StringComparison.Ordinal) ||
                path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 ||
                path.Any(char.IsControl))
                return false;
            string[] segments = path.Split('/');
            return segments.All(segment => !string.IsNullOrEmpty(segment) &&
                                           segment != "." && segment != "..");
        }

        private static bool IsHex(string value, int length)
        {
            return !string.IsNullOrEmpty(value) && value.Length == length &&
                   value.All(Uri.IsHexDigit);
        }

        private static bool TryParseSemanticVersion(string value, out Version version)
        {
            version = null;
            var match = System.Text.RegularExpressions.Regex.Match(value ?? string.Empty,
                @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:[-+].*)?$");
            return match.Success && Version.TryParse(
                match.Groups["major"].Value + "." + match.Groups["minor"].Value +
                "." + match.Groups["patch"].Value, out version);
        }
    }
}
