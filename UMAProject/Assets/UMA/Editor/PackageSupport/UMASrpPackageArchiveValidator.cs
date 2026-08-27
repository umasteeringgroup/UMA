using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.PackageSupport
{
    public sealed class UMASrpPackageArchiveInfo
    {
        internal UMASrpPackageArchiveInfo(
            Dictionary<string, string> guidByPath,
            Dictionary<string, string> textByPath,
            Dictionary<string, IReadOnlyCollection<string>> referencedGuidsByPath,
            Dictionary<string, long> assetBytesByPath,
            Dictionary<string, string> assetSha256ByPath,
            Dictionary<string, long> metaBytesByPath,
            Dictionary<string, string> metaSha256ByPath)
        {
            GuidByPath = guidByPath;
            TextByPath = textByPath;
            ReferencedGuidsByPath = referencedGuidsByPath;
            AssetBytesByPath = assetBytesByPath;
            AssetSha256ByPath = assetSha256ByPath;
            MetaBytesByPath = metaBytesByPath;
            MetaSha256ByPath = metaSha256ByPath;
        }

        public IReadOnlyDictionary<string, string> GuidByPath { get; }
        public IReadOnlyDictionary<string, string> TextByPath { get; }
        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ReferencedGuidsByPath { get; }
        public IReadOnlyDictionary<string, long> AssetBytesByPath { get; }
        public IReadOnlyDictionary<string, string> AssetSha256ByPath { get; }
        public IReadOnlyDictionary<string, long> MetaBytesByPath { get; }
        public IReadOnlyDictionary<string, string> MetaSha256ByPath { get; }
        public IReadOnlyCollection<string> Paths => GuidByPath.Keys.ToArray();
        public IReadOnlyCollection<string> SharedPaths { get; internal set; } =
            Array.Empty<string>();
    }

    /// <summary>
    /// Reads and validates the small tar.gz format used by Unity .unitypackage files.
    /// This lets UMA reject damaged or incorrectly split SRP installers before it
    /// replaces the project's current Assets/UMA/SRP folder.
    /// </summary>
    public static class UMASrpPackageArchiveValidator
    {
        public const string SrpRoot = "Assets/UMA/SRP";

        private static readonly Regex GuidReferencePattern = new Regex(
            @"(?:(?:guid\s*:\s*)|(?:""guid""\s*:\s*""))([0-9a-fA-F]{32})",
            RegexOptions.Compiled);
        private static readonly Regex MetaGuidPattern = new Regex(
            @"(?m)^guid:\s*([0-9a-fA-F]{32})\s*$", RegexOptions.Compiled);
        private static readonly Regex DefaultUmaMaterialPattern = new Regex(
            @"(?m)^  _material:\s*\{[^\r\n]*guid:\s*([0-9a-fA-F]{32})",
            RegexOptions.Compiled);
        private static readonly Regex HdrpUmaMaterialPattern = new Regex(
            @"(?m)^  _HDRPMaterial:\s*\{[^\r\n]*guid:\s*([0-9a-fA-F]{32})",
            RegexOptions.Compiled);
        private static readonly Regex SourceOnlyBetterShaderPattern = new Regex(
            @"""betterShader""\s*:\s*\{[^}]*""guid""\s*:\s*""[0-9a-fA-F]{32}""[^}]*\}" +
            @"\s*,\s*""betterShaderPath""\s*:\s*""Assets/SourceShaders/",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> HdrpReferencesForbiddenInUrp =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "aa486462e6be1764e89c788ba30e61f7", // MaterialExternalReferences
                "b2686e09ec7aef44bad2843e4416f057", // DiffusionProfileSettings
                "da692e001514ec24dbc4cca1949ff7e8"  // HDRP AssetVersion
            };
        private static readonly HashSet<string> UrpReferencesForbiddenInHdrp =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "0b2db86121404754db890f4c8dfe81b2", // Bloom
                "221518ef91623a7438a71fef23660601", // WhiteBalance
                "474bcb49853aa07438625e644c072ee6", // UniversalAdditionalLightData
                "899c54efeace73346a0a16faa3afe726", // Vignette
                "97c23e3b12dc18c42a140437e53d3951", // Tonemapping
                "a79441f348de89743a2939f4d699eac1", // UniversalAdditionalCameraData
                "c01700fd266d6914ababb731e09af2eb", // DepthOfField
                "ccf1aba9553839d41ae37dd52e9ebcce", // MotionBlur
                "d0353a89b1f911e48b9e16bdc9f2e058"  // URP AssetVersion
            };

        #pragma warning disable 0649 // Populated by JsonUtility.
        [Serializable]
        private sealed class ContentManifest
        {
            public string pipeline;
            public int formatVersion;
            public string[] requiredPaths;
            public string[] ownedPaths;
            public string[] sharedPaths;
        }
        #pragma warning restore 0649

        private sealed class PendingEntry
        {
            public string guid;
            public string pathname;
            public string assetTempPath;
            public long assetBytes;
            public string assetSha256;
            public string meta;
            public byte[] metaBytes;
        }

        public static bool TryValidatePair(string urpArchivePath,
            string hdrpArchivePath, out string error)
        {
            error = string.Empty;
            if (!TryValidate(urpArchivePath, "URP", out UMASrpPackageArchiveInfo urp,
                    out error) ||
                !TryValidate(hdrpArchivePath, "HDRP", out UMASrpPackageArchiveInfo hdrp,
                    out error))
                return false;

            if (!new HashSet<string>(urp.SharedPaths,
                    StringComparer.OrdinalIgnoreCase).SetEquals(hdrp.SharedPaths))
            {
                error = "The URP and HDRP archives do not declare the same shared " +
                        "SRP content.";
                return false;
            }

            foreach (KeyValuePair<string, string> pair in urp.GuidByPath)
            {
                if (hdrp.GuidByPath.TryGetValue(pair.Key, out string hdrpGuid) &&
                    !string.Equals(pair.Value, hdrpGuid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Shared SRP path has different URP and HDRP GUIDs: " + pair.Key;
                    return false;
                }
            }

            if (!TryFindCrossPackageReference(urp, hdrp, out error) ||
                !TryFindCrossPackageReference(hdrp, urp, out error))
                return false;
            return true;
        }

        private static bool TryFindCrossPackageReference(
            UMASrpPackageArchiveInfo package,
            UMASrpPackageArchiveInfo otherPackage,
            out string error)
        {
            error = string.Empty;
            HashSet<string> ownGuids = new HashSet<string>(package.GuidByPath.Values,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> otherOnly = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in otherPackage.GuidByPath)
            {
                if (!ownGuids.Contains(pair.Value))
                    otherOnly[pair.Value] = pair.Key;
            }

            foreach (KeyValuePair<string, IReadOnlyCollection<string>> pair in
                     package.ReferencedGuidsByPath)
            {
                foreach (string guid in pair.Value)
                {
                    if (!otherOnly.TryGetValue(guid, out string dependency))
                        continue;
                    error = pair.Key + " references content available only in the other " +
                            "SRP archive: " + dependency;
                    return false;
                }
            }
            return true;
        }

        public static bool TryValidate(string archivePath, string expectedPipeline,
            out UMASrpPackageArchiveInfo info, out string error)
        {
            info = null;
            error = string.Empty;
            if (!TryRead(archivePath, out UMASrpPackageArchiveInfo archive, out error))
                return false;

            string pipeline = (expectedPipeline ?? string.Empty).Trim().ToUpperInvariant();
            if (pipeline != "URP" && pipeline != "HDRP")
            {
                error = "Expected pipeline must be URP or HDRP.";
                return false;
            }

            foreach (string path in archive.Paths)
            {
                if (path.Equals(SrpRoot, StringComparison.Ordinal))
                {
                    error = "The archive contains its install-root folder record. " +
                        "That GUID would redirect a UPM import into Packages: " + path;
                    return false;
                }
                if (!path.StartsWith(SrpRoot + "/", StringComparison.Ordinal))
                {
                    error = $"Archive path is outside {SrpRoot}: {path}";
                    return false;
                }
                if (path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                {
                    error = "The archive contains a nested unitypackage: " + path;
                    return false;
                }
            }

            foreach (KeyValuePair<string, string> pair in archive.GuidByPath)
            {
                string registeredPath = AssetDatabase.GUIDToAssetPath(pair.Value);
                if (!string.IsNullOrEmpty(registeredPath) &&
                    registeredPath.StartsWith("Packages/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Archive GUID " + pair.Value + " for " + pair.Key +
                        " is already registered to read-only package content at " +
                        registeredPath + ". Unity would redirect the import there.";
                    return false;
                }
                if (!string.IsNullOrEmpty(registeredPath) &&
                    !string.Equals(registeredPath, pair.Key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Archive GUID " + pair.Value + " for " + pair.Key +
                        " is already used by " + registeredPath + ".";
                    return false;
                }
            }

            string manifestPath = $"{SrpRoot}/UMA{pipeline}Manifest.json";
            if (!archive.Paths.Any(path => string.Equals(path, manifestPath,
                    StringComparison.Ordinal)) ||
                !archive.TextByPath.TryGetValue(manifestPath, out string manifestJson))
            {
                error = "The archive does not contain " + manifestPath + ".";
                return false;
            }

            ContentManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<ContentManifest>(manifestJson);
            }
            catch (Exception ex)
            {
                error = "The SRP content manifest is invalid: " + ex.Message;
                return false;
            }

            HashSet<string> archivePaths = new HashSet<string>(archive.Paths,
                StringComparer.OrdinalIgnoreCase);
            if (!TryValidateManifest(manifest, pipeline, archivePaths,
                    out HashSet<string> sharedPaths, out error) ||
                !TryResolveSharedGuids(sharedPaths,
                    out HashSet<string> sharedGuids, out error))
                return false;
            archive.SharedPaths = sharedPaths.ToArray();

            foreach (string path in archive.Paths)
            {
                if (pipeline == "URP" &&
                    (path.IndexOf("/HDRPSetup", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     path.IndexOf("/DiffusionProfiles", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     path.EndsWith("_HDRP.shadergraph", StringComparison.OrdinalIgnoreCase)))
                {
                    error = "The URP archive contains HDRP-only content: " + path;
                    return false;
                }
                if (pipeline == "HDRP" &&
                    (path.EndsWith("_URP.shadergraph", StringComparison.OrdinalIgnoreCase) ||
                     path.Equals(SrpRoot + "/Settings",
                         StringComparison.OrdinalIgnoreCase) ||
                     path.StartsWith(SrpRoot + "/Settings/",
                         StringComparison.OrdinalIgnoreCase)))
                {
                    error = "The HDRP archive contains URP-only content: " + path;
                    return false;
                }
            }

            HashSet<string> archiveGuids = new HashSet<string>(
                archive.GuidByPath.Values, StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in archive.TextByPath)
            {
                string text = pair.Value;
                HashSet<string> forbiddenReferences = pipeline == "URP"
                    ? HdrpReferencesForbiddenInUrp
                    : UrpReferencesForbiddenInHdrp;
                if (archive.ReferencedGuidsByPath.TryGetValue(pair.Key,
                        out IReadOnlyCollection<string> referencedGuids))
                {
                    foreach (string guid in referencedGuids)
                    {
                        if (!forbiddenReferences.Contains(guid))
                            continue;
                        error = pipeline +
                            " archive contains an opposite-pipeline serialized GUID " +
                            guid + " in " + pair.Key + ".";
                        return false;
                    }
                }
                if (SourceOnlyBetterShaderPattern.IsMatch(text))
                {
                    error = "The SRP archive contains an authoring-only BetterShader " +
                        "object reference: " + pair.Key;
                    return false;
                }
                if (pipeline == "URP" && Regex.IsMatch(text,
                        @"(?m)^  _HDRP(Material|SecondPass):\s*\{[^\r\n]*guid:"))
                {
                    error = "The URP archive contains a live HDRP material reference: " + pair.Key;
                    return false;
                }
                if (pipeline == "URP" && text.IndexOf(
                        "UnityEditor.Rendering.HighDefinition.ShaderGraph.",
                        StringComparison.Ordinal) >= 0)
                {
                    error = "The URP archive contains an HDRP ShaderGraph target: " + pair.Key;
                    return false;
                }
                if (pipeline == "HDRP" && text.IndexOf(
                        "UnityEditor.Rendering.Universal.ShaderGraph.",
                        StringComparison.Ordinal) >= 0)
                {
                    error = "The HDRP archive contains a URP ShaderGraph target: " + pair.Key;
                    return false;
                }

                if (Regex.IsMatch(text, @"(?m)^  _HDRPMaterial:"))
                {
                    Match activeMaterial = pipeline == "HDRP"
                        ? HdrpUmaMaterialPattern.Match(text)
                        : DefaultUmaMaterialPattern.Match(text);
                    if (pipeline == "HDRP" && !activeMaterial.Success)
                        activeMaterial = DefaultUmaMaterialPattern.Match(text);
                    if (!activeMaterial.Success)
                    {
                        error = pipeline + " UMA material has no active Unity material: " +
                            pair.Key;
                        return false;
                    }
                    string activeMaterialGuid = activeMaterial.Groups[1].Value;
                    if (!archiveGuids.Contains(activeMaterialGuid) &&
                        !sharedGuids.Contains(activeMaterialGuid))
                    {
                        error = pipeline + " UMA material references content outside its " +
                            "archive and declared shared assets: " + pair.Key;
                        return false;
                    }
                }
            }

            info = archive;
            return true;
        }

        public static bool TryValidateInstalledSupport(string expectedPipeline,
            out string error)
        {
            error = string.Empty;
            string pipeline = (expectedPipeline ?? string.Empty).Trim().ToUpperInvariant();
            if (pipeline != "URP" && pipeline != "HDRP")
            {
                error = "Expected pipeline must be URP or HDRP.";
                return false;
            }
            string manifestAssetPath = SrpRoot + "/UMA" + pipeline + "Manifest.json";
            string manifestPath = UMAPathUtility.ResolveAbsolutePath(manifestAssetPath);
            if (!File.Exists(manifestPath))
            {
                error = "No installed " + pipeline + " manifest was found at " +
                        manifestAssetPath + ".";
                return false;
            }
            try
            {
                ContentManifest manifest = JsonUtility.FromJson<ContentManifest>(
                    File.ReadAllText(manifestPath));
                if (!TryValidateManifest(manifest, pipeline, null,
                        out HashSet<string> sharedPaths, out error))
                    return false;
                foreach (string sharedPath in sharedPaths)
                {
                    string sharedAbsolute = UMAPathUtility.ResolveAbsolutePath(sharedPath);
                    if (!File.Exists(sharedAbsolute) &&
                        !Directory.Exists(sharedAbsolute))
                    {
                        error = "Installed " + pipeline +
                                " support is missing shared path " + sharedPath + ".";
                        return false;
                    }
                }
                foreach (string requiredPath in manifest.requiredPaths)
                {
                    string absolute = UMAPathUtility.ResolveAbsolutePath(requiredPath);
                    if (!File.Exists(absolute) && !Directory.Exists(absolute))
                    {
                        error = "Installed " + pipeline +
                                " support is missing required path " + requiredPath + ".";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "Could not validate installed " + pipeline +
                        " support: " + exception.Message;
                return false;
            }
        }

        public static bool TryValidateInstalledFiles(string expectedPipeline,
            UMASrpPackageArchiveInfo archive, out string error)
        {
            error = string.Empty;
            if (archive == null)
            {
                error = "No validated SRP archive was supplied.";
                return false;
            }
            if (!TryValidateInstalledSupport(expectedPipeline, out error))
                return false;

            HashSet<string> paths = new HashSet<string>(archive.Paths,
                StringComparer.OrdinalIgnoreCase);
            foreach (string path in archive.Paths)
            {
                string absolute = UMAPathUtility.ResolveAbsolutePath(path);
                bool hasAsset = archive.AssetBytesByPath.TryGetValue(path,
                    out long expectedBytes);
                bool missingEmptyLeaf = !hasAsset && !Directory.Exists(absolute) &&
                    !paths.Any(candidate => candidate.StartsWith(path.TrimEnd('/') + "/",
                        StringComparison.OrdinalIgnoreCase));
                if (missingEmptyLeaf)
                    continue;
                if (hasAsset)
                {
                    if (!File.Exists(absolute) ||
                        new FileInfo(absolute).Length != expectedBytes ||
                        !archive.AssetSha256ByPath.TryGetValue(path,
                            out string expectedHash) ||
                        !string.Equals(ComputeSha256(absolute), expectedHash,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        error = "Installed SRP asset does not match its archive: " + path;
                        return false;
                    }
                }
                else if (!Directory.Exists(absolute))
                {
                    error = "Installed SRP folder is missing: " + path;
                    return false;
                }

                string metaPath = absolute + ".meta";
                if (!File.Exists(metaPath) ||
                    !archive.MetaBytesByPath.TryGetValue(path,
                        out long expectedMetaBytes) ||
                    new FileInfo(metaPath).Length != expectedMetaBytes ||
                    !archive.MetaSha256ByPath.TryGetValue(path,
                        out string expectedMetaHash) ||
                    !string.Equals(ComputeSha256(metaPath), expectedMetaHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    error = "Installed SRP importer metadata does not match its archive: " +
                            path + ".meta";
                    return false;
                }
            }
            return true;
        }

        private static bool TryValidateManifest(ContentManifest manifest,
            string pipeline, HashSet<string> archivePaths,
            out HashSet<string> sharedPaths, out string error)
        {
            sharedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            error = string.Empty;
            if (manifest == null ||
                (manifest.formatVersion != 1 && manifest.formatVersion != 2) ||
                !string.Equals(manifest.pipeline, pipeline,
                    StringComparison.OrdinalIgnoreCase))
            {
                error = "The SRP content manifest has the wrong pipeline or format version.";
                return false;
            }
            string[] ownedArray = manifest.ownedPaths ?? Array.Empty<string>();
            string[] requiredArray = manifest.requiredPaths ?? Array.Empty<string>();
            var ownedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in ownedArray)
            {
                if (!IsSafeSrpPath(path) || !ownedPaths.Add(path))
                {
                    error = "The SRP manifest has an invalid or duplicate owned path: " +
                            (path ?? "<null>") + ".";
                    return false;
                }
            }
            string manifestPath = SrpRoot + "/UMA" + pipeline + "Manifest.json";
            if (ownedPaths.Count == 0 || !ownedPaths.Contains(manifestPath))
            {
                error = "The SRP manifest does not own " + manifestPath + ".";
                return false;
            }
            if (archivePaths != null && !archivePaths.SetEquals(ownedPaths))
            {
                error = "The SRP content manifest does not exactly describe the archive contents.";
                return false;
            }

            if (manifest.formatVersion >= 2)
            {
                foreach (string sharedPath in manifest.sharedPaths ??
                         Array.Empty<string>())
                {
                    if (!IsSafeSharedSrpPath(sharedPath) ||
                        ownedPaths.Contains(sharedPath) ||
                        !sharedPaths.Add(sharedPath))
                    {
                        error = "The SRP manifest has an invalid, owned, or duplicate " +
                                "shared path: " + (sharedPath ?? "<null>") + ".";
                        return false;
                    }
                }
                if (sharedPaths.Count == 0)
                {
                    error = "The version 2 SRP manifest has no shared paths.";
                    return false;
                }
            }

            var requiredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string requiredPath in requiredArray)
            {
                if (!IsSafeSrpPath(requiredPath) ||
                    (!ownedPaths.Contains(requiredPath) &&
                     !sharedPaths.Contains(requiredPath)) ||
                    !requiredPaths.Add(requiredPath))
                {
                    error = "The SRP manifest has an invalid or duplicate required path: " +
                            (requiredPath ?? "<null>") + ".";
                    return false;
                }
            }
            if (requiredPaths.Count == 0)
            {
                error = "The SRP manifest has no required paths.";
                return false;
            }
            return true;
        }

        private static bool TryResolveSharedGuids(IEnumerable<string> sharedPaths,
            out HashSet<string> sharedGuids, out string error)
        {
            sharedGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            error = string.Empty;
            foreach (string sharedPath in sharedPaths)
            {
                string absolute = FindSharedAbsolutePath(sharedPath);
                if (string.IsNullOrEmpty(absolute))
                {
                    error = "Declared shared SRP content is missing: " + sharedPath;
                    return false;
                }

                string metaPath = absolute + ".meta";
                if (!File.Exists(metaPath))
                {
                    error = "Declared shared SRP content has no metadata: " +
                            sharedPath + ".meta";
                    return false;
                }

                MatchCollection matches = MetaGuidPattern.Matches(
                    File.ReadAllText(metaPath));
                if (matches.Count != 1)
                {
                    error = "Declared shared SRP metadata has no unique GUID: " +
                            sharedPath + ".meta";
                    return false;
                }
                sharedGuids.Add(matches[0].Groups[1].Value);
            }
            return true;
        }

        private static string FindSharedAbsolutePath(string sharedPath)
        {
            string projectPath = UMAPathUtility.ResolveAbsolutePath(sharedPath);
            if (File.Exists(projectPath) || Directory.Exists(projectPath))
                return projectPath;

            string prefix = SrpRoot + "/";
            if (!sharedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            string installAssetPath = UMAPathUtility.ResolveInstallAssetPath(
                "SRP/" + sharedPath.Substring(prefix.Length));
            string installPath = UMAPathUtility.ResolveAbsolutePath(installAssetPath);
            return File.Exists(installPath) || Directory.Exists(installPath)
                ? installPath
                : string.Empty;
        }

        private static bool IsSafeSrpPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                !string.Equals(path, path.Trim(), StringComparison.Ordinal) ||
                string.Equals(path, SrpRoot, StringComparison.OrdinalIgnoreCase) ||
                !path.StartsWith(SrpRoot + "/", StringComparison.Ordinal) ||
                path.EndsWith("/", StringComparison.Ordinal) ||
                path.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase) ||
                path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0 ||
                path.Any(char.IsControl))
                return false;
            return path.Split('/').All(segment => !string.IsNullOrEmpty(segment) &&
                                                  segment != "." && segment != "..");
        }

        private static bool IsSafeSharedSrpPath(string path)
        {
            if (!IsSafeSrpPath(path))
                return false;

            string textureRoot = SrpRoot + "/Textures";
            string shaderPackageRoot = SrpRoot + "/ShaderPackages";
            return path.Equals(textureRoot, StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(textureRoot + "/",
                       StringComparison.OrdinalIgnoreCase) ||
                   path.Equals(shaderPackageRoot,
                       StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(shaderPackageRoot + "/",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryRead(string archivePath, out UMASrpPackageArchiveInfo info,
            out string error)
        {
            info = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            {
                error = "Archive does not exist: " + archivePath;
                return false;
            }

            try
            {
                string tempRoot = Path.Combine(Path.GetTempPath(),
                    "uma-package-validation-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempRoot);
                try
                {
                    Dictionary<string, PendingEntry> entriesByGuid =
                        new Dictionary<string, PendingEntry>(StringComparer.OrdinalIgnoreCase);
                    using (FileStream file = File.OpenRead(archivePath))
                    using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
                    {
                        ReadTar(gzip, entriesByGuid, tempRoot);
                    }

                    Dictionary<string, string> guidByPath =
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, string> textByPath =
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, IReadOnlyCollection<string>> referencesByPath =
                        new Dictionary<string, IReadOnlyCollection<string>>(
                            StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, long> assetBytesByPath =
                        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, string> assetSha256ByPath =
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, long> metaBytesByPath =
                        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, string> metaSha256ByPath =
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    foreach (PendingEntry entry in entriesByGuid.Values)
                    {
                        if (string.IsNullOrWhiteSpace(entry.pathname))
                            throw new InvalidDataException(
                                "Unitypackage entry has no pathname: " + entry.guid);
                        // Path validation must see the exact pathname from the
                        // archive. Normalizing here would hide backslashes or
                        // surrounding whitespace from the root-containment gate.
                        string path = entry.pathname;
                        if (guidByPath.ContainsKey(path))
                            throw new InvalidDataException(
                                "Duplicate package pathname: " + path);
                        if (entry.metaBytes == null)
                            throw new InvalidDataException(
                                "Package entry has no importer metadata: " + path);
                        MatchCollection metaGuids = MetaGuidPattern.Matches(
                            entry.meta ?? string.Empty);
                        if (metaGuids.Count != 1 || !string.Equals(
                                metaGuids[0].Groups[1].Value, entry.guid,
                                StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException(
                                "Asset GUID does not match its package entry: " + path);

                        guidByPath[path] = entry.guid;
                        if (!string.IsNullOrEmpty(entry.assetTempPath))
                        {
                            assetBytesByPath[path] = entry.assetBytes;
                            assetSha256ByPath[path] = entry.assetSha256;
                        }
                        metaBytesByPath[path] = entry.metaBytes.LongLength;
                        metaSha256ByPath[path] = ComputeSha256(entry.metaBytes);
                        if (!TryDecodeTextFile(path, entry.assetTempPath,
                                out string assetText))
                            continue;
                        textByPath[path] = assetText;
                        HashSet<string> references = new HashSet<string>(
                            StringComparer.OrdinalIgnoreCase);
                        foreach (Match match in GuidReferencePattern.Matches(assetText))
                            references.Add(match.Groups[1].Value.ToLowerInvariant());
                        referencesByPath[path] = references.ToArray();
                    }

                    if (guidByPath.Count == 0)
                        throw new InvalidDataException(
                            "Archive contains no Unity package entries.");

                    info = new UMASrpPackageArchiveInfo(guidByPath, textByPath,
                        referencesByPath, assetBytesByPath, assetSha256ByPath,
                        metaBytesByPath, metaSha256ByPath);
                    return true;
                }
                finally
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, true);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void ReadTar(Stream stream,
            Dictionary<string, PendingEntry> entries, string tempRoot)
        {
            byte[] header = new byte[512];
            var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (ReadExact(stream, header, 0, header.Length))
            {
                if (IsAllZero(header))
                    return;

                ValidateTarHeaderChecksum(header);

                string name = ReadNullTerminatedAscii(header, 0, 100);
                if (name.IndexOf('\\') >= 0)
                    throw new InvalidDataException(
                        "Unitypackage tar member uses a backslash: " + name);
                long size = ReadOctal(header, 124, 12);
                byte typeFlag = header[156];
                if (Regex.IsMatch(name, @"^[0-9a-fA-F]{32}/$"))
                {
                    if (typeFlag != (byte)'5' || size != 0 || !members.Add(name))
                        throw new InvalidDataException(
                            "Unitypackage contains an invalid directory member: " + name);
                    continue;
                }
                string[] parts = name.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || parts[0].Length != 32 ||
                    !parts[0].All(Uri.IsHexDigit) ||
                    (parts[1] != "asset" && parts[1] != "asset.meta" &&
                     parts[1] != "pathname" && parts[1] != "preview.png") ||
                    (typeFlag != 0 && typeFlag != (byte)'0'))
                    throw new InvalidDataException(
                        "Unitypackage contains an unexpected tar member: " + name);
                if (!members.Add(parts[0] + "/" + parts[1]))
                    throw new InvalidDataException(
                        "Unitypackage contains a duplicate tar member: " + name);
                if (parts[1] == "preview.png")
                {
                    SkipExact(stream, size);
                    SkipExact(stream, (512 - (size % 512)) % 512);
                    continue;
                }
                string guid = parts[0].ToLowerInvariant();
                if (!entries.TryGetValue(guid, out PendingEntry entry))
                {
                    entry = new PendingEntry { guid = guid };
                    entries.Add(guid, entry);
                }

                byte[] data = null;
                if (parts[1] == "asset")
                {
                    entry.assetTempPath = Path.Combine(tempRoot, guid + ".asset");
                    entry.assetBytes = size;
                    entry.assetSha256 = CopyEntryToFileAndHash(stream, size,
                        entry.assetTempPath);
                }
                else
                {
                    if (size > int.MaxValue)
                        throw new InvalidDataException(
                            "Unitypackage metadata entry is too large: " + name);
                    data = new byte[(int)size];
                    if (size > 0 && !ReadExact(stream, data, 0, data.Length))
                        throw new EndOfStreamException(
                            "Unexpected end of unitypackage tar entry: " + name);
                }
                SkipExact(stream, (512 - (size % 512)) % 512);

                switch (parts[1])
                {
                    case "pathname":
                        entry.pathname = new UTF8Encoding(false, true).GetString(data);
                        if (string.IsNullOrWhiteSpace(entry.pathname) ||
                            !string.Equals(entry.pathname, entry.pathname.Trim(),
                                StringComparison.Ordinal))
                            throw new InvalidDataException(
                                "Unitypackage pathname has surrounding whitespace or a line ending: " +
                                entry.pathname.Trim());
                        for (int i = 0; i < entry.pathname.Length; i++)
                        {
                            if (char.IsControl(entry.pathname[i]))
                                throw new InvalidDataException(
                                    "Unitypackage pathname contains a control character: " +
                                    entry.pathname);
                        }
                        break;
                    case "asset.meta":
                        entry.metaBytes = data;
                        entry.meta = new UTF8Encoding(false, true).GetString(data);
                        break;
                }
            }
        }

        private static string CopyEntryToFileAndHash(Stream source, long count,
            string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream output = File.Create(path);
            byte[] buffer = new byte[1024 * 1024];
            long remaining = count;
            while (remaining > 0)
            {
                int requested = (int)Math.Min(buffer.Length, remaining);
                int read = source.Read(buffer, 0, requested);
                if (read <= 0)
                    throw new EndOfStreamException(
                        "Unexpected end of unitypackage asset entry.");
                output.Write(buffer, 0, read);
                sha.TransformBlock(buffer, 0, read, null, 0);
                remaining -= read;
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha.Hash ?? Array.Empty<byte>())
                .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool TryDecodeTextFile(string path, string filePath,
            out string text)
        {
            text = null;
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            bool nativeUnityAsset = extension == ".anim" || extension == ".asset" ||
                                    extension == ".controller" || extension == ".lighting" ||
                                    extension == ".mat" || extension == ".prefab" ||
                                    extension == ".unity";
            bool knownText = extension == ".asmdef" || extension == ".asmref" ||
                             extension == ".compute" || extension == ".cginc" ||
                             extension == ".cs" || extension == ".hlsl" ||
                             extension == ".json" || extension == ".md" ||
                             extension == ".shader" || extension == ".shadergraph" ||
                             extension == ".txt" || extension == ".uss" ||
                             extension == ".uxml";
            if (!nativeUnityAsset && !knownText)
                return false;
            return TryDecodeText(path, File.ReadAllBytes(filePath), out text);
        }

        private static bool TryDecodeText(string path, byte[] bytes, out string text)
        {
            text = null;
            if (bytes == null)
                return false;

            // Native Unity SerializedFile assets must never pass through a
            // text decoder. Only the explicit Unity YAML header authorizes
            // treating these extensions as text.
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".anim" || extension == ".asset" ||
                extension == ".controller" || extension == ".lighting" ||
                extension == ".mat" || extension == ".prefab" ||
                extension == ".unity")
            {
                if (bytes.Length < 5 || bytes[0] != (byte)'%' ||
                    bytes[1] != (byte)'Y' || bytes[2] != (byte)'A' ||
                    bytes[3] != (byte)'M' || bytes[4] != (byte)'L')
                    return false;
            }

            int sampleLength = Math.Min(bytes.Length, 4096);
            for (int i = 0; i < sampleLength; i++)
            {
                if (bytes[i] == 0)
                    return false;
            }

            try
            {
                text = new UTF8Encoding(false, true).GetString(bytes);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes))
                .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream))
                .Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int offset, int count)
        {
            int readTotal = 0;
            while (readTotal < count)
            {
                int read = stream.Read(buffer, offset + readTotal, count - readTotal);
                if (read == 0)
                {
                    if (readTotal == 0)
                        return false;
                    throw new EndOfStreamException();
                }
                readTotal += read;
            }
            return true;
        }

        private static void SkipExact(Stream stream, long count)
        {
            byte[] buffer = new byte[512];
            while (count > 0)
            {
                int requested = (int)Math.Min(buffer.Length, count);
                if (!ReadExact(stream, buffer, 0, requested))
                    throw new EndOfStreamException();
                count -= requested;
            }
        }

        private static bool IsAllZero(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] != 0)
                    return false;
            }
            return true;
        }

        private static string ReadNullTerminatedAscii(byte[] data, int offset, int count)
        {
            int length = 0;
            while (length < count && data[offset + length] != 0)
                length++;
            return Encoding.ASCII.GetString(data, offset, length);
        }

        private static long ReadOctal(byte[] data, int offset, int count)
        {
            string value = ReadNullTerminatedAscii(data, offset, count).Trim();
            if (string.IsNullOrEmpty(value))
                return 0;
            return Convert.ToInt64(value, 8);
        }

        private static void ValidateTarHeaderChecksum(byte[] header)
        {
            long expected = ReadOctal(header, 148, 8);
            long actual = 0;
            for (int i = 0; i < header.Length; i++)
                actual += i >= 148 && i < 156 ? (byte)' ' : header[i];
            if (expected != actual)
                throw new InvalidDataException("Unitypackage tar header checksum is invalid.");
        }
    }
}
