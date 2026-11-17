using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Converts textures to 8-bit RGBA PNG from their full-resolution, uncompressed source pixels.
    /// </summary>
    public static class UMATexturePngConverter
    {
        public enum Mode
        {
            /// <summary>Replace the file with a PNG of the same name, keeping its GUID and import settings.</summary>
            ReplaceInPlace,
            /// <summary>Write a PNG next to the original and copy the original's import settings to it.</summary>
            CreateCopy
        }

        public sealed class Result
        {
            public string SourceGuid, SourcePath, OutputPath, OutputGuid, Message;
            public bool Converted, Failed;
            public long BytesBefore, BytesAfter;
        }

        private static readonly string[] HighDynamicRangeExtensions = { ".exr", ".hdr" };

        public static List<Result> Convert(IList<string> guids, Mode mode, bool relinkOverlays,
            Func<float, string, bool> progress = null)
        {
            var results = new List<Result>();
            var pendingMoves = new List<(Result result, byte[] png)>();
            for (int i = 0; i < guids.Count; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var result = new Result { SourceGuid = guids[i], SourcePath = path, BytesBefore = FileSize(path) };
                results.Add(result);
                if (progress != null && progress((float)i / Math.Max(1, guids.Count), path))
                {
                    result.Message = "Not processed (cancelled).";
                    continue;
                }

                try
                {
                    string skip = GetSkipReason(path, mode);
                    if (skip != null) { result.Message = "Skipped: " + skip; continue; }

                    byte[] png = EncodeSourceAsPng(path);
                    if (mode == Mode.ReplaceInPlace)
                    {
                        result.OutputPath = Path.ChangeExtension(path, ".png").Replace('\\', '/');
                        pendingMoves.Add((result, png));
                    }
                    else
                    {
                        WriteCopy(result, png, relinkOverlays);
                    }
                }
                catch (Exception exception)
                {
                    result.Failed = true;
                    result.Message = "Failed: " + exception.Message;
                }
            }

            if (pendingMoves.Count > 0) ReplaceInPlace(pendingMoves);
            return results;
        }

        /// <summary>Returns why a texture is not converted, or null when it can be.</summary>
        public static string GetSkipReason(string path, Mode mode)
        {
            string blocker = UMATextureToolsOperations.GetWriteBlocker(path);
            if (blocker != null) return blocker;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".png") return "already a PNG.";
            if (HighDynamicRangeExtensions.Contains(extension))
                return "HDR images would lose their range as 8-bit PNG.";
            if (mode == Mode.ReplaceInPlace)
            {
                string target = Path.ChangeExtension(path, ".png");
                if (File.Exists(target) || File.Exists(target + ".meta"))
                    return Path.GetFileName(target) + " already exists. Use Create Copy, or move that file first.";
                if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
                    return "the image file is read-only.";
            }
            return null;
        }

        /// <summary>
        /// Reads the image's own pixels: full resolution, uncompressed, no sRGB or normal-map
        /// conversion. The importer settings are put back afterwards.
        /// </summary>
        private static byte[] EncodeSourceAsPng(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".jpg" || extension == ".jpeg")
            {
                var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
                try
                {
                    if (!decoded.LoadImage(File.ReadAllBytes(path), false)) throw new InvalidDataException("Unity could not decode the JPEG file.");
                    return EncodePixels(decoded.GetPixels32(), decoded.width, decoded.height);
                }
                finally { UnityEngine.Object.DestroyImmediate(decoded); }
            }

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var savedSettings = new TextureImporterSettings();
            importer.ReadTextureSettings(savedSettings);
            var savedPlatforms = new List<TextureImporterPlatformSettings> { importer.GetDefaultPlatformTextureSettings() };
            savedPlatforms.AddRange(GetPlatformNames(importer).Select(importer.GetPlatformTextureSettings));
            try
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.textureType = TextureImporterType.Default;
                settings.textureShape = TextureImporterShape.Texture2D;
                settings.readable = true;
                settings.mipmapEnabled = false;
                settings.npotScale = TextureImporterNPOTScale.None;
                settings.alphaSource = TextureImporterAlphaSource.FromInput;
                settings.alphaIsTransparency = false;
                importer.SetTextureSettings(settings);
                TextureImporterPlatformSettings defaults = importer.GetDefaultPlatformTextureSettings();
                defaults.maxTextureSize = 16384;
                defaults.textureCompression = TextureImporterCompression.Uncompressed;
                defaults.crunchedCompression = false;
                importer.SetPlatformTextureSettings(defaults);
                foreach (string platform in GetPlatformNames(importer))
                {
                    TextureImporterPlatformSettings overrideSettings = importer.GetPlatformTextureSettings(platform);
                    if (!overrideSettings.overridden) continue;
                    overrideSettings.overridden = false;
                    importer.SetPlatformTextureSettings(overrideSettings);
                }
                importer.SaveAndReimport();

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (texture == null) throw new InvalidDataException("The texture could not be loaded.");
                if (!texture.isReadable) throw new InvalidDataException("The texture could not be made readable.");
                return EncodePixels(texture.GetPixels32(), texture.width, texture.height);
            }
            finally
            {
                // Put the original import settings back. The .meta file is never written directly:
                // Unity can hold it open right after an import.
                importer.SetTextureSettings(savedSettings);
                foreach (TextureImporterPlatformSettings platform in savedPlatforms) importer.SetPlatformTextureSettings(platform);
                importer.SaveAndReimport();
            }
        }

        /// <summary>Names of the platforms that have their own entry in the importer (not Default).</summary>
        public static List<string> GetPlatformNames(TextureImporter importer)
        {
            var names = new List<string>();
            using var serialized = new SerializedObject(importer);
            SerializedProperty platforms = serialized.FindProperty("m_PlatformSettings");
            string defaultName = importer.GetDefaultPlatformTextureSettings().name;
            for (int i = 0; platforms != null && i < platforms.arraySize; i++)
            {
                string name = platforms.GetArrayElementAtIndex(i).FindPropertyRelative("m_BuildTarget").stringValue;
                if (string.IsNullOrEmpty(name) || name == defaultName || names.Contains(name)) continue;
                names.Add(name);
            }
            return names;
        }

        private static byte[] EncodePixels(Color32[] pixels, int width, int height)
        {
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            try
            {
                output.SetPixels32(pixels);
                output.Apply(false, false);
                byte[] png = output.EncodeToPNG();
                if (png == null || png.Length == 0) throw new InvalidDataException("PNG encoding returned no data.");
                return png;
            }
            finally { UnityEngine.Object.DestroyImmediate(output); }
        }

        /// <summary>
        /// Renames each image to .png with AssetDatabase.MoveAsset, which keeps its .meta (GUID and
        /// import settings), and writes the PNG bytes before Unity imports the renamed file. Every
        /// reference to the texture therefore still works.
        /// </summary>
        private static void ReplaceInPlace(List<(Result result, byte[] png)> moves)
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (result, png) in moves)
                {
                    string error = AssetDatabase.MoveAsset(result.SourcePath, result.OutputPath);
                    if (!string.IsNullOrEmpty(error))
                    {
                        result.Failed = true;
                        result.Message = "Failed: " + error;
                        continue;
                    }
                    try
                    {
                        File.WriteAllBytes(result.OutputPath, png);
                        result.Converted = true;
                    }
                    catch (Exception exception)
                    {
                        // Put the original back under its own name; its bytes were not touched.
                        AssetDatabase.MoveAsset(result.OutputPath, result.SourcePath);
                        result.Failed = true;
                        result.Message = "Failed: " + exception.Message;
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var (result, _) in moves)
                    if (result.Converted) AssetDatabase.ImportAsset(result.OutputPath, ImportAssetOptions.ForceUpdate);
            }
            finally { AssetDatabase.StopAssetEditing(); }

            foreach (var (result, _) in moves)
            {
                if (!result.Converted) continue;
                result.OutputGuid = AssetDatabase.AssetPathToGUID(result.OutputPath);
                result.BytesAfter = FileSize(result.OutputPath);
                result.Message = result.OutputGuid == result.SourceGuid
                    ? $"Replaced with {Path.GetFileName(result.OutputPath)}; GUID, references and import settings kept."
                    : $"Replaced with {Path.GetFileName(result.OutputPath)}, but Unity gave it a new GUID; check references.";
            }
        }

        private static void WriteCopy(Result result, byte[] png, bool relinkOverlays)
        {
            string source = result.SourcePath;
            string target = Path.ChangeExtension(source, ".png").Replace('\\', '/');
            if (File.Exists(target)) target = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(Path.GetDirectoryName(source) ?? "Assets", Path.GetFileNameWithoutExtension(source) + "_converted.png").Replace('\\', '/'));
            File.WriteAllBytes(target, png);
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);

            var from = (TextureImporter)AssetImporter.GetAtPath(source);
            var to = AssetImporter.GetAtPath(target) as TextureImporter;
            if (to == null) throw new InvalidDataException("No texture importer was created for " + target);
            CopyImportSettings(from, to);
            to.SaveAndReimport();

            result.OutputPath = target;
            result.OutputGuid = AssetDatabase.AssetPathToGUID(target);
            result.BytesAfter = FileSize(target);
            result.Converted = true;
            result.Message = "Created " + Path.GetFileName(target) + " with the original's import settings.";
            if (relinkOverlays)
            {
                int relinked = RelinkOverlays(AssetDatabase.LoadAssetAtPath<Texture>(source), AssetDatabase.LoadAssetAtPath<Texture>(target));
                result.Message += relinked > 0 ? $" Updated {relinked} overlay(s) to use it." : " No overlays referenced the original.";
            }
        }

        /// <summary>Copies texture and per-platform import settings from one importer to another.</summary>
        public static void CopyImportSettings(TextureImporter from, TextureImporter to)
        {
            var settings = new TextureImporterSettings();
            from.ReadTextureSettings(settings);
            to.SetTextureSettings(settings);
            to.SetPlatformTextureSettings(from.GetDefaultPlatformTextureSettings());
            foreach (string name in GetPlatformNames(from)) to.SetPlatformTextureSettings(from.GetPlatformTextureSettings(name));
        }

        /// <summary>Points every overlay that uses <paramref name="oldTexture"/> at <paramref name="newTexture"/>.</summary>
        public static int RelinkOverlays(Texture oldTexture, Texture newTexture)
        {
            if (oldTexture == null || newTexture == null) return 0;
            string oldPath = AssetDatabase.GetAssetPath(oldTexture);
            int updated = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:OverlayDataAsset"))
            {
                string overlayPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!AssetDatabase.GetDependencies(overlayPath, false).Contains(oldPath, StringComparer.OrdinalIgnoreCase)) continue;
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(overlayPath))
                {
                    if (!(asset is OverlayDataAsset overlay)) continue;
                    Texture[] list = overlay.textureList;
                    bool uses = overlay.alphaMask == oldTexture || (list != null && list.Contains(oldTexture));
                    if (!uses) continue;
                    Undo.RecordObject(overlay, "Relink overlay texture");
                    for (int t = 0; list != null && t < list.Length; t++)
                    {
                        if (list[t] != oldTexture) continue;
                        list[t] = newTexture;
                        if (overlay.textureNames != null && t < overlay.textureNames.Length) overlay.textureNames[t] = newTexture.name;
                    }
                    overlay.textureList = list;
                    if (overlay.alphaMask == oldTexture) overlay.alphaMask = newTexture;
                    EditorUtility.SetDirty(overlay);
                    AssetDatabase.SaveAssetIfDirty(overlay);
                    updated++;
                }
            }
            return updated;
        }

        private static long FileSize(string path) => !string.IsNullOrEmpty(path) && File.Exists(path) ? new FileInfo(path).Length : 0;
    }
}
