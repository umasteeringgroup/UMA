using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// One place an overlay uses a texture: an overlay texture slot (or its alpha mask) mapped
    /// through the overlay's UMA Material to the material property ("keyword") it feeds.
    /// </summary>
    public sealed class UMATextureUsage
    {
        public OverlayDataAsset Overlay;
        public string OverlayPath;
        public UMAMaterial Material;
        /// <summary>Index in the overlay's texture list, or -1 for the alpha mask.</summary>
        public int ChannelIndex;
        public string Keyword;
        /// <summary>True when the UMA Material describes what this texture holds.</summary>
        public bool HasRecommendation;
        public UMAMaterial.TextureChannelLayout Layout;
        public UMAMaterial.TextureChannelOutputSettings Output;

        public string OverlayName => Overlay != null ? Overlay.overlayName : Path.GetFileNameWithoutExtension(OverlayPath);
        public string MaterialName => Material != null ? Material.name : "no UMA Material";
    }

    /// <summary>Finds which overlays use a set of textures, and as which UMA Material keyword.</summary>
    public static class UMATextureUsageScanner
    {
        public const string AlphaMaskKeyword = "Alpha Mask";

        /// <summary>
        /// Maps each texture GUID to every overlay texture slot that references it. Overlays are
        /// pre-filtered by the asset database's dependency list, so only overlays that reference
        /// one of the textures are loaded.
        /// </summary>
        public static Dictionary<string, List<UMATextureUsage>> Scan(IEnumerable<string> textureGuids,
            Func<float, string, bool> progress = null)
        {
            var result = new Dictionary<string, List<UMATextureUsage>>(StringComparer.Ordinal);
            var pathToGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string guid in textureGuids)
            {
                if (string.IsNullOrEmpty(guid) || result.ContainsKey(guid)) continue;
                result[guid] = new List<UMATextureUsage>();
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path)) pathToGuid[path] = guid;
            }
            if (pathToGuid.Count == 0) return result;

            string[] overlayGuids = AssetDatabase.FindAssets("t:OverlayDataAsset");
            for (int o = 0; o < overlayGuids.Length; o++)
            {
                string overlayPath = AssetDatabase.GUIDToAssetPath(overlayGuids[o]);
                if (progress != null && (o & 31) == 0 &&
                    progress(overlayGuids.Length == 0 ? 1f : (float)o / overlayGuids.Length, overlayPath))
                    break;
                if (string.IsNullOrEmpty(overlayPath)) continue;
                string[] dependencies = AssetDatabase.GetDependencies(overlayPath, false);
                bool referencesTarget = false;
                for (int d = 0; d < dependencies.Length && !referencesTarget; d++)
                    referencesTarget = pathToGuid.ContainsKey(dependencies[d]);
                if (!referencesTarget) continue;

                // A file can hold more than one overlay.
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(overlayPath))
                {
                    if (asset is OverlayDataAsset overlay)
                        AddOverlayUsages(overlay, overlayPath, pathToGuid, result);
                }
            }
            return result;
        }

        private static void AddOverlayUsages(OverlayDataAsset overlay, string overlayPath,
            Dictionary<string, string> pathToGuid, Dictionary<string, List<UMATextureUsage>> result)
        {
            Texture[] textures = overlay.textureList;
            for (int i = 0; textures != null && i < textures.Length; i++)
            {
                if (textures[i] == null) continue;
                if (!pathToGuid.TryGetValue(AssetDatabase.GetAssetPath(textures[i]), out string guid)) continue;
                result[guid].Add(CreateUsage(overlay, overlayPath, i));
            }
            if (overlay.alphaMask != null &&
                pathToGuid.TryGetValue(AssetDatabase.GetAssetPath(overlay.alphaMask), out string maskGuid))
            {
                result[maskGuid].Add(CreateUsage(overlay, overlayPath, -1));
            }
        }

        /// <summary>Describes overlay texture slot <paramref name="channelIndex"/> (-1 = alpha mask).</summary>
        public static UMATextureUsage CreateUsage(OverlayDataAsset overlay, string overlayPath, int channelIndex)
        {
            var usage = new UMATextureUsage
            {
                Overlay = overlay,
                OverlayPath = overlayPath,
                Material = overlay != null ? overlay.material : null,
                ChannelIndex = channelIndex
            };

            if (channelIndex < 0)
            {
                usage.Keyword = AlphaMaskKeyword;
                usage.HasRecommendation = true;
                usage.Layout = new UMAMaterial.TextureChannelLayout { alpha = UMAMaterial.TextureChannelUsage.Opacity };
                usage.Output = new UMAMaterial.TextureChannelOutputSettings
                {
                    importerType = UMAMaterial.TextureChannelImporterType.Default,
                    colorSpace = UMAMaterial.TextureChannelColorSpace.Linear,
                    alphaSource = UMAMaterial.TextureChannelAlphaSource.FromInput
                };
                return usage;
            }

            UMAMaterial material = usage.Material;
            if (material == null)
            {
                usage.Keyword = $"Channel {channelIndex} (no UMA Material)";
                return usage;
            }
            if (material.channels == null || channelIndex >= material.channels.Length)
            {
                usage.Keyword = $"Channel {channelIndex} (not in {material.name})";
                return usage;
            }

            UMAMaterial.MaterialChannel channel = material.channels[channelIndex];
            string property = (channel.materialPropertyName ?? string.Empty).Trim();
            usage.Keyword = property.Length > 0 ? property : $"Channel {channelIndex}";
            if (channel.channelType != UMAMaterial.ChannelType.MaterialColor)
            {
                usage.HasRecommendation = true;
                usage.Layout = UMAMaterial.GetTextureChannelLayout(channel, material.material);
                usage.Output = UMAMaterial.GetTextureChannelOutputSettings(channel, material.material, material);
            }
            return usage;
        }
    }

    /// <summary>A difference between a texture's import settings and what its UMA Material channels need.</summary>
    public sealed class UMATextureSettingIssue
    {
        public enum Severity { Info, Warning, Error }
        public Severity Level;
        public string Setting;
        public string Message;
        /// <summary>Applies the recommended value to the importer; null when it cannot be fixed automatically.</summary>
        public Action<TextureImporter> Fix;
    }

    /// <summary>Checks import settings against the overlay usages of a texture.</summary>
    public static class UMATextureSettingsChecker
    {
        public static List<UMATextureSettingIssue> Check(TextureImporter importer, IList<UMATextureUsage> usages)
        {
            var issues = new List<UMATextureSettingIssue>();
            if (importer == null) return issues;

            var recommended = usages?.Where(u => u.HasRecommendation).ToList() ?? new List<UMATextureUsage>();
            if (recommended.Count > 0) CheckAgainstMaterials(importer, recommended, issues);

            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            int largest = Math.Max(width, height);
            int maxSize = importer.GetDefaultPlatformTextureSettings().maxTextureSize;
            if (largest > maxSize)
                issues.Add(Info("Max Size", $"Imported at reduced size: the {width}×{height} source is limited to {maxSize}."));
            if (width > 0 && height > 0 && (!Mathf.IsPowerOfTwo(width) || !Mathf.IsPowerOfTwo(height)) &&
                importer.npotScale == TextureImporterNPOTScale.None)
                issues.Add(Info("Size", $"{width}×{height} is not a power of two; some platforms compress it poorly or not at all."));
            if (importer.isReadable)
                issues.Add(new UMATextureSettingIssue
                {
                    Level = UMATextureSettingIssue.Severity.Info,
                    Setting = "Read/Write",
                    Message = "Read/Write is on, which keeps a second copy of the texture in CPU memory. UMA does not need it to build atlases.",
                    Fix = i => i.isReadable = false
                });
            return issues;
        }

        private static void CheckAgainstMaterials(TextureImporter importer, List<UMATextureUsage> usages,
            List<UMATextureSettingIssue> issues)
        {
            string Describe(IEnumerable<UMATextureUsage> list) =>
                string.Join(", ", list.Select(u => u.Keyword).Distinct());

            // Texture type: normal map or not.
            var wantNormal = usages.Where(u => u.Output.importerType == UMAMaterial.TextureChannelImporterType.NormalMap).ToList();
            var wantDefault = usages.Where(u => u.Output.importerType != UMAMaterial.TextureChannelImporterType.NormalMap).ToList();
            bool typeConflict = wantNormal.Count > 0 && wantDefault.Count > 0;
            if (typeConflict)
            {
                issues.Add(new UMATextureSettingIssue
                {
                    Level = UMATextureSettingIssue.Severity.Error,
                    Setting = "Texture Type",
                    Message = $"Used as a normal map ({Describe(wantNormal)}) and as a color/data texture ({Describe(wantDefault)}). One import cannot suit both; use separate textures."
                });
            }
            else if (wantNormal.Count > 0 && importer.textureType != TextureImporterType.NormalMap)
            {
                issues.Add(new UMATextureSettingIssue
                {
                    Level = UMATextureSettingIssue.Severity.Error,
                    Setting = "Texture Type",
                    Message = $"Used as {Describe(wantNormal)} but imported as {importer.textureType}, not Normal map.",
                    Fix = i => i.textureType = TextureImporterType.NormalMap
                });
            }
            else if (wantDefault.Count > 0 && importer.textureType == TextureImporterType.NormalMap)
            {
                issues.Add(new UMATextureSettingIssue
                {
                    Level = UMATextureSettingIssue.Severity.Error,
                    Setting = "Texture Type",
                    Message = $"Used as {Describe(wantDefault)} but imported as a Normal map, which repacks its channels.",
                    Fix = i => i.textureType = TextureImporterType.Default
                });
            }

            // sRGB only matters for textures that are not (or should not be) normal maps.
            if (!typeConflict && wantNormal.Count == 0)
            {
                // Alpha masks are read from alpha only, which is never sRGB-converted.
                var colorUsers = wantDefault.Where(u => u.ChannelIndex >= 0).ToList();
                var srgb = colorUsers.Where(u => u.Output.colorSpace == UMAMaterial.TextureChannelColorSpace.SRGB).ToList();
                var linear = colorUsers.Where(u => u.Output.colorSpace != UMAMaterial.TextureChannelColorSpace.SRGB).ToList();
                if (srgb.Count > 0 && linear.Count > 0)
                {
                    issues.Add(new UMATextureSettingIssue
                    {
                        Level = UMATextureSettingIssue.Severity.Error,
                        Setting = "sRGB",
                        Message = $"Used as color ({Describe(srgb)}) and as linear data ({Describe(linear)}). One import cannot suit both; use separate textures."
                    });
                }
                else if (srgb.Count > 0 && !importer.sRGBTexture)
                {
                    issues.Add(new UMATextureSettingIssue
                    {
                        Level = UMATextureSettingIssue.Severity.Error,
                        Setting = "sRGB",
                        Message = $"{Describe(srgb)} holds color, but sRGB (Color Texture) is off, so colors render too dark and washed.",
                        Fix = i => i.sRGBTexture = true
                    });
                }
                else if (linear.Count > 0 && importer.sRGBTexture)
                {
                    issues.Add(new UMATextureSettingIssue
                    {
                        Level = UMATextureSettingIssue.Severity.Error,
                        Setting = "sRGB",
                        Message = $"{Describe(linear)} holds data (for example metallic, occlusion or masks), but sRGB (Color Texture) is on, so its RGB values are read too low. Alpha is never converted.",
                        Fix = i => i.sRGBTexture = false
                    });
                }
            }

            // Alpha. Turning alpha off is never suggested: UMA reads texture 0's alpha to blend an
            // overlay over the ones below it when the overlay has no alpha mask.
            // Alpha masks are read from alpha too (the atlas shader uses 1 - mask.a).
            var dataAlpha = usages.Where(u => u.ChannelIndex < 0 ||
                                              (u.Layout.alpha & ~UMAMaterial.TextureChannelUsage.Opacity) != 0).ToList();
            var blendAlpha = usages.Where(u => u.ChannelIndex == 0 && u.Overlay != null && u.Overlay.alphaMask == null &&
                                               (u.Layout.alpha & UMAMaterial.TextureChannelUsage.Opacity) != 0 &&
                                               !dataAlpha.Contains(u)).ToList();
            if (importer.alphaSource == TextureImporterAlphaSource.None)
            {
                if (dataAlpha.Count > 0)
                {
                    issues.Add(new UMATextureSettingIssue
                    {
                        Level = UMATextureSettingIssue.Severity.Error,
                        Setting = "Alpha Source",
                        Message = $"{Describe(dataAlpha)} stores data or a mask in alpha (for example smoothness), but Alpha Source is None, so alpha reads as 1.",
                        Fix = i => i.alphaSource = TextureImporterAlphaSource.FromInput
                    });
                }
                else if (blendAlpha.Count > 0 && importer.DoesSourceTextureHaveAlpha())
                {
                    issues.Add(new UMATextureSettingIssue
                    {
                        Level = UMATextureSettingIssue.Severity.Warning,
                        Setting = "Alpha Source",
                        Message = $"The source has alpha but Alpha Source is None. Overlays without an alpha mask ({string.Join(", ", blendAlpha.Select(u => u.OverlayName).Distinct())}) blend with this alpha when layered over another overlay, so they would cover everything below.",
                        Fix = i => i.alphaSource = TextureImporterAlphaSource.FromInput
                    });
                }
            }
            else if (dataAlpha.Count > 0 && importer.alphaSource == TextureImporterAlphaSource.FromInput &&
                     !importer.DoesSourceTextureHaveAlpha())
            {
                issues.Add(new UMATextureSettingIssue
                {
                    Level = UMATextureSettingIssue.Severity.Warning,
                    Setting = "Alpha",
                    Message = $"{Describe(dataAlpha)} stores data in alpha, but the source image has no alpha channel, so alpha reads as 1. Re-export the image with alpha."
                });
            }

            var transparencyMatters = dataAlpha.Where(u => u.ChannelIndex >= 0).ToList();
            if (transparencyMatters.Count > 0 && importer.alphaIsTransparency)
            {
                issues.Add(new UMATextureSettingIssue
                {
                    Level = UMATextureSettingIssue.Severity.Warning,
                    Setting = "Alpha Is Transparency",
                    Message = $"{Describe(transparencyMatters)} stores data in alpha, but Alpha Is Transparency is on, which changes RGB wherever alpha is near zero.",
                    Fix = i => i.alphaIsTransparency = false
                });
            }
        }

        private static UMATextureSettingIssue Info(string setting, string message) =>
            new UMATextureSettingIssue { Level = UMATextureSettingIssue.Severity.Info, Setting = setting, Message = message };
    }

    /// <summary>
    /// A set of import setting changes. Only settings whose "set" flag is on are changed;
    /// everything else on each importer is left as it is.
    /// </summary>
    [Serializable]
    public sealed class UMATextureImportEdit
    {
        public bool setType; public TextureImporterType type = TextureImporterType.Default;
        public bool setShape; public TextureImporterShape shape = TextureImporterShape.Texture2D;
        public bool setSRGB; public bool sRGB = true;
        public bool setAlphaSource; public TextureImporterAlphaSource alphaSource = TextureImporterAlphaSource.FromInput;
        public bool setAlphaIsTransparency; public bool alphaIsTransparency;
        public bool setReadable; public bool readable;
        public bool setMipmaps; public bool mipmaps = true;
        public bool setMipFilter; public TextureImporterMipFilter mipFilter = TextureImporterMipFilter.BoxFilter;
        public bool setStreaming; public bool streamingMipmaps;
        public bool setWrap; public TextureWrapMode wrap = TextureWrapMode.Repeat;
        public bool setFilter; public FilterMode filter = FilterMode.Bilinear;
        public bool setAniso; public int aniso = 1;
        public bool setNpot; public TextureImporterNPOTScale npot = TextureImporterNPOTScale.None;
        /// <summary>Platform the size/compression settings go to; empty means Default.</summary>
        public string platform = string.Empty;
        public bool setMaxSize; public int maxSize = 2048;
        public bool setResizeAlgorithm; public TextureResizeAlgorithm resizeAlgorithm = TextureResizeAlgorithm.Mitchell;
        public bool setCompression; public TextureImporterCompression compression = TextureImporterCompression.Compressed;
        public bool setCrunch; public bool crunch; public int crunchQuality = 50;
        public bool setFormat; public TextureImporterFormat format = TextureImporterFormat.Automatic;
        public bool removeOverride;

        public bool IsDefaultPlatform => string.IsNullOrEmpty(platform);

        public bool AnyPlatformChange => setMaxSize || setResizeAlgorithm || setCompression || setCrunch ||
                                         (setFormat && !IsDefaultPlatform) || (removeOverride && !IsDefaultPlatform);

        public bool Any => setType || setShape || setSRGB || setAlphaSource || setAlphaIsTransparency || setReadable ||
                           setMipmaps || setMipFilter || setStreaming || setWrap || setFilter || setAniso || setNpot ||
                           AnyPlatformChange;

        public void ClearFlags()
        {
            setType = setShape = setSRGB = setAlphaSource = setAlphaIsTransparency = setReadable = false;
            setMipmaps = setMipFilter = setStreaming = setWrap = setFilter = setAniso = setNpot = false;
            setMaxSize = setResizeAlgorithm = setCompression = setCrunch = setFormat = removeOverride = false;
        }

        /// <summary>Copies every value from an importer and ticks the general settings.</summary>
        public void CopyFrom(TextureImporter importer)
        {
            type = importer.textureType; shape = importer.textureShape;
            sRGB = importer.sRGBTexture; alphaSource = importer.alphaSource;
            alphaIsTransparency = importer.alphaIsTransparency; readable = importer.isReadable;
            mipmaps = importer.mipmapEnabled; mipFilter = importer.mipmapFilter;
            streamingMipmaps = importer.streamingMipmaps; wrap = importer.wrapMode;
            filter = importer.filterMode; aniso = importer.anisoLevel; npot = importer.npotScale;
            TextureImporterPlatformSettings settings = IsDefaultPlatform
                ? importer.GetDefaultPlatformTextureSettings()
                : importer.GetPlatformTextureSettings(platform);
            maxSize = settings.maxTextureSize; resizeAlgorithm = settings.resizeAlgorithm;
            compression = settings.textureCompression; crunch = settings.crunchedCompression;
            crunchQuality = settings.compressionQuality; format = settings.format;
            ClearFlags();
            setType = setSRGB = setAlphaSource = setAlphaIsTransparency = setReadable = true;
            setMipmaps = setWrap = setFilter = setAniso = setNpot = true;
            setMaxSize = setCompression = setCrunch = true;
            setFormat = !IsDefaultPlatform;
        }

        /// <summary>Applies the ticked settings. Returns a description of each value that changed.</summary>
        public List<string> ApplyTo(TextureImporter importer)
        {
            var changes = new List<string>();
            void Set<T>(bool enabled, string name, T current, T value, Action<T> assign)
            {
                if (!enabled || EqualityComparer<T>.Default.Equals(current, value)) return;
                assign(value);
                changes.Add($"{name}: {current} → {value}");
            }

            Set(setType, "Texture Type", importer.textureType, type, v => importer.textureType = v);
            Set(setShape, "Texture Shape", importer.textureShape, shape, v => importer.textureShape = v);
            Set(setSRGB, "sRGB", importer.sRGBTexture, sRGB, v => importer.sRGBTexture = v);
            Set(setAlphaSource, "Alpha Source", importer.alphaSource, alphaSource, v => importer.alphaSource = v);
            Set(setAlphaIsTransparency, "Alpha Is Transparency", importer.alphaIsTransparency, alphaIsTransparency, v => importer.alphaIsTransparency = v);
            Set(setReadable, "Read/Write", importer.isReadable, readable, v => importer.isReadable = v);
            Set(setMipmaps, "Generate Mipmaps", importer.mipmapEnabled, mipmaps, v => importer.mipmapEnabled = v);
            Set(setMipFilter, "Mip Filter", importer.mipmapFilter, mipFilter, v => importer.mipmapFilter = v);
            Set(setStreaming, "Mip Streaming", importer.streamingMipmaps, streamingMipmaps, v => importer.streamingMipmaps = v);
            Set(setWrap, "Wrap Mode", importer.wrapMode, wrap, v => importer.wrapMode = v);
            Set(setFilter, "Filter Mode", importer.filterMode, filter, v => importer.filterMode = v);
            Set(setAniso, "Aniso Level", importer.anisoLevel, Mathf.Clamp(aniso, 0, 16), v => importer.anisoLevel = v);
            Set(setNpot, "Non-Power of 2", importer.npotScale, npot, v => importer.npotScale = v);

            if (AnyPlatformChange)
            {
                TextureImporterPlatformSettings settings = IsDefaultPlatform
                    ? importer.GetDefaultPlatformTextureSettings()
                    : importer.GetPlatformTextureSettings(platform);
                string prefix = IsDefaultPlatform ? string.Empty : platform + " ";
                int before = changes.Count;
                if (!IsDefaultPlatform && removeOverride)
                {
                    Set(true, prefix + "Override", settings.overridden, false, v => settings.overridden = v);
                }
                else
                {
                    if (!IsDefaultPlatform)
                        Set(true, prefix + "Override", settings.overridden, true, v => settings.overridden = v);
                    Set(setMaxSize, prefix + "Max Size", settings.maxTextureSize, maxSize, v => settings.maxTextureSize = v);
                    Set(setResizeAlgorithm, prefix + "Resize Algorithm", settings.resizeAlgorithm, resizeAlgorithm, v => settings.resizeAlgorithm = v);
                    Set(setCompression, prefix + "Compression", settings.textureCompression, compression, v => settings.textureCompression = v);
                    Set(setCrunch, prefix + "Crunch", settings.crunchedCompression, crunch, v => settings.crunchedCompression = v);
                    if (setCrunch && crunch)
                        Set(true, prefix + "Crunch Quality", settings.compressionQuality, Mathf.Clamp(crunchQuality, 0, 100), v => settings.compressionQuality = v);
                    if (!IsDefaultPlatform)
                        Set(setFormat, prefix + "Format", settings.format, format, v => settings.format = v);
                }
                if (changes.Count > before) importer.SetPlatformTextureSettings(settings);
            }
            return changes;
        }
    }

    /// <summary>Batch operations on texture importers shared by Texture Tools and its tests.</summary>
    public static class UMATextureToolsOperations
    {
        public sealed class Result
        {
            public string Guid, Path, Message;
            public bool Changed, Failed;
        }

        /// <summary>Returns why an importer cannot be changed, or null when it can.</summary>
        public static string GetWriteBlocker(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return "Only textures under Assets can be changed (package textures are read-only).";
            if (!(AssetImporter.GetAtPath(path) is TextureImporter))
                return "This asset does not use the Texture importer.";
            string meta = path + ".meta";
            if (!AssetDatabase.IsOpenForEdit(meta) ||
                (File.Exists(meta) && (File.GetAttributes(meta) & FileAttributes.ReadOnly) != 0))
                return "Import settings are read-only or need to be checked out.";
            return null;
        }

        /// <summary>Applies an import edit to each texture and reimports only the ones that changed.</summary>
        public static List<Result> ApplyImportEdit(IList<string> guids, UMATextureImportEdit edit,
            Func<float, string, bool> progress = null)
        {
            return ForEachImporter(guids, "Apply import settings", progress, importer => edit.ApplyTo(importer));
        }

        /// <summary>Applies the automatic fixes from <see cref="UMATextureSettingsChecker"/>.</summary>
        public static List<Result> ApplyMaterialFixes(IList<string> guids,
            IReadOnlyDictionary<string, List<UMATextureUsage>> usages, Func<float, string, bool> progress = null,
            bool includeInfo = false)
        {
            return ForEachImporter(guids, "Match UMA Material settings", progress, importer =>
            {
                string guid = AssetDatabase.AssetPathToGUID(importer.assetPath);
                usages.TryGetValue(guid, out List<UMATextureUsage> list);
                var applied = new List<string>();
                foreach (UMATextureSettingIssue issue in UMATextureSettingsChecker.Check(importer, list))
                {
                    if (issue.Fix == null) continue;
                    if (issue.Level == UMATextureSettingIssue.Severity.Info && !includeInfo) continue;
                    issue.Fix(importer);
                    applied.Add("Fixed " + issue.Setting);
                }
                return applied;
            });
        }

        private static List<Result> ForEachImporter(IList<string> guids, string title,
            Func<float, string, bool> progress, Func<TextureImporter, List<string>> change)
        {
            var results = new List<Result>();
            var toImport = new List<string>();
            for (int i = 0; i < guids.Count; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var result = new Result { Guid = guids[i], Path = path };
                results.Add(result);
                if (progress != null && progress((float)i / Math.Max(1, guids.Count), path))
                {
                    result.Message = "Not processed (cancelled).";
                    continue;
                }
                string blocker = GetWriteBlocker(path);
                if (blocker != null) { result.Message = "Skipped: " + blocker; continue; }
                try
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    List<string> changes = change(importer);
                    if (changes.Count == 0) { result.Message = "Unchanged: already matches."; continue; }
                    EditorUtility.SetDirty(importer);
                    AssetDatabase.WriteImportSettingsIfDirty(path);
                    toImport.Add(path);
                    result.Changed = true;
                    result.Message = string.Join("; ", changes);
                }
                catch (Exception exception)
                {
                    result.Failed = true;
                    result.Message = "Failed: " + exception.Message;
                }
            }

            if (toImport.Count > 0)
            {
                // Reimport as one batch so Unity can import the textures in parallel.
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (string path in toImport) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                finally { AssetDatabase.StopAssetEditing(); }
            }
            return results;
        }

        /// <summary>Writes a CSV report of the textures, their import settings and their overlay usages.</summary>
        public static string BuildReport(IEnumerable<string> guids, IReadOnlyDictionary<string, List<UMATextureUsage>> usages)
        {
            string Csv(object value)
            {
                string text = value?.ToString() ?? string.Empty;
                return text.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
            }

            var builder = new StringBuilder();
            builder.AppendLine("Path,Source Width,Source Height,File Bytes,Type,sRGB,Alpha Source,Max Size,Compression,Mipmaps,Read/Write,Keywords,Overlays,Issues");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                usages.TryGetValue(guid, out List<UMATextureUsage> list);
                list ??= new List<UMATextureUsage>();
                long bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
                int width = 0, height = 0;
                importer?.GetSourceTextureWidthAndHeight(out width, out height);
                TextureImporterPlatformSettings platform = importer?.GetDefaultPlatformTextureSettings();
                var issues = importer != null ? UMATextureSettingsChecker.Check(importer, list) : new List<UMATextureSettingIssue>();
                builder.AppendLine(string.Join(",", new object[]
                {
                    path, width, height, bytes,
                    importer?.textureType, importer?.sRGBTexture, importer?.alphaSource,
                    platform?.maxTextureSize, platform?.textureCompression, importer?.mipmapEnabled, importer?.isReadable,
                    string.Join(" ", list.Select(u => u.Keyword).Distinct()),
                    string.Join(" ", list.Select(u => u.OverlayName).Distinct()),
                    string.Join(" | ", issues.Select(i => i.Setting + ": " + i.Message))
                }.Select(Csv)));
            }
            return builder.ToString();
        }
    }
}
