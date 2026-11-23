using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Bakes procedural expression wrinkles for a head slot: the wrinkle map and the zone control map read by
    /// "UMA/URP/Realistic Skin SSS Wrinkles". Both are written as linear, uncompressed PNGs with mipmaps.
    /// </summary>
    public static class UMAWrinkleMapBaker
    {
        public const string UMA3HeadSlotPath = "Assets/UMA/UMA3/Races/Slots/UMA30_Body/UMA30_Body_UDIM1001_slot.asset";
        public const string UMA3WrinkleMapPath = "Assets/UMA/UMA3/Textures/UMA3_Wrinkles.1001.png";
        public const string UMA3ControlMapPath = "Assets/UMA/UMA3/Textures/UMA3_WrinkleZones.1001.png";
        public const int DefaultSize = 2048;
        public const int DefaultSeed = 1;

        public sealed class BakeResult
        {
            public Texture2D WrinkleMap;
            public Texture2D ControlMap;
            public UMAWrinkleControlMapPacker.Report Report;
        }

        // Hand-editable sources, written next to each other as <wrinkle map name><suffix>.png.
        public const string SourceHeightSuffix = "_height";
        public const string SourceCreaseSuffix = "_crease";
        public const string SourceFlushSuffix = "_flush";
        public const string SourceGuideSuffix = "_guide";
        internal const string SourceFolderPref = "UMA.WrinkleMapSources.Folder";

        /// <summary>The folder the UMA 3 sources were last exported to or imported from, or empty.</summary>
        public static string LastSourceFolder => EditorPrefs.GetString(SourceFolderPref, "");

        public sealed class ImportResult
        {
            public Texture2D WrinkleMap;
            public string Report;
        }

        static SlotDataAsset LoadUMA3Head()
        {
            var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(UMA3HeadSlotPath);
            if (slot == null) EditorUtility.DisplayDialog("Wrinkle Maps", "The UMA 3 head slot was not found at\n" + UMA3HeadSlotPath, "OK");
            return slot;
        }

        [MenuItem("UMA/Skin/Bake Procedural Wrinkle Maps (UMA 3 Head)", priority = 40)]
        static void BakeUMA3HeadMenu()
        {
            var slot = LoadUMA3Head();
            if (slot == null) return;
            if (File.Exists(UMA3WrinkleMapPath) && !EditorUtility.DisplayDialog("Wrinkle Maps", "Replace the existing wrinkle maps with a procedural bake?\n" + UMA3WrinkleMapPath + "\n" + UMA3ControlMapPath +
                "\n\nA wrinkle map imported from edited sources is replaced too. Import the sources again afterwards to get it back.", "Replace", "Cancel"))
                return;
            try
            {
                EditorUtility.DisplayProgressBar("Wrinkle Maps", "Baking procedural wrinkles", 0.3f);
                var result = Bake(slot, UMAWrinkleFaceLandmarks.UMA3Head(), DefaultSeed, DefaultSize, UMA3WrinkleMapPath, UMA3ControlMapPath);
                if (result.Report.Ok) Debug.Log("Wrinkle maps baked. " + result.Report);
                else Debug.LogWarning("Wrinkle maps baked with problems. " + result.Report);
                // The overlay and wrinkle UMA Material take the maps through the atlas.
                string message;
                if (UMAWrinkleAssets.EnsureUMA3Assets(out message) == null) Debug.LogWarning(message);
                EditorGUIUtility.PingObject(result.WrinkleMap);
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        /// <summary>Bakes in memory and returns the two maps' RGBA bytes, row 0 at the top.</summary>
        public static void BakeBytes(SlotDataAsset slot, UMAWrinkleFaceLandmarks landmarks, int seed, int size,
            out byte[] wrinkle, out byte[] control, out UMAWrinkleControlMapPacker.Report report)
        {
            var bake = UMAWrinkleProceduralGenerator.Generate(BuildSurface(slot, size), landmarks, seed);
            control = UMAWrinkleControlMapPacker.Pack(size, size, bake.ZoneMasks, out report, 4f * size / 2048f);
            wrinkle = ToBytes(bake.WrinkleMap);
        }

        static byte[] ToBytes(float[] values)
        {
            var bytes = new byte[values.Length];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(values[i] * 255f), 0, 255);
            return bytes;
        }

        /// <summary>The slot's mesh rasterised into its UV square at the given size.</summary>
        public static UMAWrinkleSurfaceMap BuildSurface(SlotDataAsset slot, int size)
        {
            if (slot == null || slot.meshData == null) throw new ArgumentException("The slot has no mesh data.", nameof(slot));
            var md = slot.meshData;
            int n = md.vertices.Length;
            var positions = new System.Numerics.Vector3[n];
            var normals = new System.Numerics.Vector3[n];
            var tangents = new System.Numerics.Vector4[n];
            var uvs = new System.Numerics.Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var p = md.vertices[i]; var nr = md.normals[i];
                var t = md.tangents != null && md.tangents.Length == n ? md.tangents[i] : new Vector4(1, 0, 0, 1);
                positions[i] = new System.Numerics.Vector3(p.x, p.y, p.z);
                normals[i] = new System.Numerics.Vector3(nr.x, nr.y, nr.z);
                tangents[i] = new System.Numerics.Vector4(t.x, t.y, t.z, t.w);
                uvs[i] = new System.Numerics.Vector2(md.uv[i].x, md.uv[i].y);
            }
            int count = 0;
            for (int s = 0; s < md.subMeshCount; s++) count += md.submeshes[s].GetBaseTriangles().Length;
            var triangles = new int[count];
            int at = 0;
            for (int s = 0; s < md.subMeshCount; s++) { var tris = md.submeshes[s].GetBaseTriangles(); Array.Copy(tris, 0, triangles, at, tris.Length); at += tris.Length; }

            return UMAWrinkleSurfaceMap.Build(positions, normals, tangents, uvs, triangles, size);
        }

        /// <summary>
        /// Bakes and writes both maps as project textures, plus the wrinkle height next to the wrinkle map
        /// (<see cref="HeightSourcePath"/>) so Overlay Painter can edit the height the map was made from.
        /// </summary>
        public static BakeResult Bake(SlotDataAsset slot, UMAWrinkleFaceLandmarks landmarks, int seed, int size, string wrinklePath, string controlPath)
        {
            var bake = UMAWrinkleProceduralGenerator.Generate(BuildSurface(slot, size), landmarks, seed);
            var control = UMAWrinkleControlMapPacker.Pack(size, size, bake.ZoneMasks, out var report, 4f * size / 2048f);
            var height = new float[bake.HeightMM.Length];
            for (int i = 0; i < height.Length; i++) height[i] = UMAWrinkleSourceMaps.HeightToGray(bake.HeightMM[i]);
            var result = new BakeResult
            {
                WrinkleMap = WriteTexture(wrinklePath, ToBytes(bake.WrinkleMap), size, TextureImporterCompression.CompressedHQ),
                // Ids must survive exactly: no block compression on the control map.
                ControlMap = WriteTexture(controlPath, control, size, TextureImporterCompression.Uncompressed),
                Report = report,
            };
            WriteHeightSource(HeightSourcePath(wrinklePath), height, size);
            return result;
        }

        /// <summary>
        /// The editable wrinkle height kept next to a wrinkle map: "UMA3_Wrinkles.1001.png" has
        /// "UMA3_Wrinkles.1001_height.png". 16-bit grayscale, mid gray flat, black and white -/+
        /// <see cref="UMAWrinkleSourceMaps.HeightRangeMM"/> mm. Editor-only: no overlay or material references it.
        /// </summary>
        public static string HeightSourcePath(string wrinkleMapPath)
        {
            string directory = Path.GetDirectoryName(wrinkleMapPath) ?? string.Empty;
            return Path.Combine(directory, Path.GetFileNameWithoutExtension(wrinkleMapPath) + SourceHeightSuffix + ".png").Replace('\\', '/');
        }

        /// <summary>Writes a height (gray 0..1, row 0 at the top) as a 16-bit linear, uncompressed project texture.</summary>
        public static Texture2D WriteHeightSource(string path, float[] grayTopFirst, int size)
        {
            File.WriteAllBytes(path, EncodeGray16(grayTopFirst, size));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            ConfigureHeightImporter(path, size);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Import settings that keep a 16-bit height exact: linear, uncompressed, no mipmaps, R16.</summary>
        public static void ConfigureHeightImporter(string path, int size)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Math.Max(32, Mathf.NextPowerOfTwo(size));
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.format = TextureImporterFormat.R16;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
        }

        /// <summary>The height texture kept next to a wrinkle map, or null.</summary>
        public static Texture2D FindHeightSource(Texture wrinkleMap)
        {
            string path = wrinkleMap != null ? AssetDatabase.GetAssetPath(wrinkleMap) : null;
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(HeightSourcePath(path));
        }

        [MenuItem("UMA/Skin/Export Wrinkle Map Sources (UMA 3 Head)", priority = 42)]
        static void ExportUMA3SourcesMenu()
        {
            var slot = LoadUMA3Head();
            if (slot == null) return;
            string folder = EditorUtility.SaveFolderPanel("Export wrinkle map sources", SourceFolderParent(), SourceFolderName());
            if (string.IsNullOrEmpty(folder)) return;
            bool exists = File.Exists(SourcePath(folder, UMA3WrinkleMapPath, SourceHeightSuffix)) || File.Exists(SourcePath(folder, UMA3WrinkleMapPath, SourceCreaseSuffix)) ||
                          File.Exists(SourcePath(folder, UMA3WrinkleMapPath, SourceFlushSuffix));
            if (exists && !EditorUtility.DisplayDialog("Wrinkle Maps", "Replace the wrinkle map sources in\n" + folder + "\nwith a fresh procedural bake? Edits made to them are lost.", "Replace", "Cancel"))
                return;
            try
            {
                EditorUtility.DisplayProgressBar("Wrinkle Maps", "Baking procedural wrinkles", 0.3f);
                ExportSources(slot, UMAWrinkleFaceLandmarks.UMA3Head(), DefaultSeed, DefaultSize, folder, UMA3WrinkleMapPath);
                EditorPrefs.SetString(SourceFolderPref, folder);
                UMAWrinkleSourceWatcher.Reset(); // the fresh files are the starting point, not an edit
                Debug.Log("Wrinkle map sources exported to " + folder + ". Edit the height, crease and flush images (16-bit grayscale; the guide shows the zones), then use UMA > Skin > Import Wrinkle Map Sources (UMA 3 Head)" +
                    (UMAWrinkleSourceWatcher.Enabled ? ". Auto-import is on: saving one of them rebuilds the map." : ", or turn on UMA > Skin > Auto-Import Wrinkle Map Sources to rebuild the map whenever you save."));
                EditorUtility.RevealInFinder(SourcePath(folder, UMA3WrinkleMapPath, SourceHeightSuffix));
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("UMA/Skin/Import Wrinkle Map Sources (UMA 3 Head)", priority = 43)]
        static void ImportUMA3SourcesMenu()
        {
            var slot = LoadUMA3Head();
            if (slot == null) return;
            string folder = EditorUtility.OpenFolderPanel("Import wrinkle map sources", SourceFolderParent(), SourceFolderName());
            if (string.IsNullOrEmpty(folder)) return;
            if (!File.Exists(SourcePath(folder, UMA3WrinkleMapPath, SourceHeightSuffix)))
            {
                EditorUtility.DisplayDialog("Wrinkle Maps", "No wrinkle map sources in\n" + folder + "\n\nExpected " + Path.GetFileName(SourcePath(folder, UMA3WrinkleMapPath, SourceHeightSuffix)) + ", " +
                    Path.GetFileName(SourcePath(folder, UMA3WrinkleMapPath, SourceCreaseSuffix)) + " and " + Path.GetFileName(SourcePath(folder, UMA3WrinkleMapPath, SourceFlushSuffix)) + ".", "OK");
                return;
            }
            if (!EditorUtility.DisplayDialog("Wrinkle Maps", "Replace the wrinkle map with the sources in\n" + folder + "?\n" + UMA3WrinkleMapPath + "\n\nThe zone map is not changed.", "Replace", "Cancel"))
                return;
            try
            {
                EditorUtility.DisplayProgressBar("Wrinkle Maps", "Building the wrinkle map from its sources", 0.3f);
                var result = ImportSources(slot, folder, UMA3WrinkleMapPath);
                EditorPrefs.SetString(SourceFolderPref, folder);
                UMAWrinkleSourceWatcher.Reset();
                Debug.Log("Wrinkle map built from " + folder + ". " + result.Report);
                EditorGUIUtility.PingObject(result.WrinkleMap);
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is ArgumentException)
            {
                EditorUtility.DisplayDialog("Wrinkle Maps", "The wrinkle map was not changed.\n\n" + e.Message, "OK");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        static string SourceFolderParent()
        {
            string folder = EditorPrefs.GetString(SourceFolderPref, "");
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) return Path.GetDirectoryName(folder);
            return Path.GetDirectoryName(Application.dataPath); // the project folder, outside Assets
        }

        static string SourceFolderName()
        {
            string folder = EditorPrefs.GetString(SourceFolderPref, "");
            return !string.IsNullOrEmpty(folder) && Directory.Exists(folder) ? Path.GetFileName(folder) : "WrinkleSources";
        }

        /// <summary>The source image of one kind for a wrinkle map, e.g. "UMA3_Wrinkles.1001_height.png" in the folder.</summary>
        public static string SourcePath(string folder, string wrinkleMapPath, string suffix)
        {
            return Path.Combine(folder, Path.GetFileNameWithoutExtension(wrinkleMapPath) + suffix + ".png");
        }

        /// <summary>
        /// Writes the procedural bake as editable sources: height (16-bit gray, mid gray flat, black and white
        /// -/+ <see cref="UMAWrinkleSourceMaps.HeightRangeMM"/> mm), crease and flush (16-bit gray, as stored in the
        /// wrinkle map's blue and alpha), and a guide that shows each zone's area over the height.
        /// </summary>
        public static void ExportSources(SlotDataAsset slot, UMAWrinkleFaceLandmarks landmarks, int seed, int size, string folder, string wrinkleMapPath)
        {
            var bake = UMAWrinkleProceduralGenerator.Generate(BuildSurface(slot, size), landmarks, seed);
            Directory.CreateDirectory(folder);
            int n = size * size;
            var height = new float[n]; var crease = new float[n]; var flush = new float[n];
            var guide = new byte[n * 4];
            for (int i = 0; i < n; i++)
            {
                height[i] = UMAWrinkleSourceMaps.HeightToGray(bake.HeightMM[i]);
                crease[i] = bake.WrinkleMap[i * 4 + 2];
                flush[i] = bake.WrinkleMap[i * 4 + 3];
                // Guide: gray shaded by the height, tinted where each zone's mask is.
                Color c = Color.gray * (1f + Mathf.Clamp(bake.HeightMM[i] * 0.8f, -0.6f, 0.6f));
                for (int zone = 1; zone < bake.ZoneMasks.Length; zone++)
                    if (bake.ZoneMasks[zone] != null && bake.ZoneMasks[zone][i] > 0)
                        c = Color.Lerp(c, Color.HSVToRGB(zone * 0.381966f % 1f, 0.75f, 1f), bake.ZoneMasks[zone][i] * 0.6f);
                guide[i * 4] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
                guide[i * 4 + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
                guide[i * 4 + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
                guide[i * 4 + 3] = 255;
            }
            File.WriteAllBytes(SourcePath(folder, wrinkleMapPath, SourceHeightSuffix), EncodeGray16(height, size));
            File.WriteAllBytes(SourcePath(folder, wrinkleMapPath, SourceCreaseSuffix), EncodeGray16(crease, size));
            File.WriteAllBytes(SourcePath(folder, wrinkleMapPath, SourceFlushSuffix), EncodeGray16(flush, size));
            File.WriteAllBytes(SourcePath(folder, wrinkleMapPath, SourceGuideSuffix), EncodeRgba32(guide, size));
        }

        /// <summary>Builds the wrinkle map from the height, crease and flush sources in the folder and writes it as a project texture.</summary>
        public static ImportResult ImportSources(SlotDataAsset slot, string folder, string wrinkleMapPath)
        {
            var height = ReadSource(folder, wrinkleMapPath, SourceHeightSuffix);
            var crease = ReadSource(folder, wrinkleMapPath, SourceCreaseSuffix);
            var flush = ReadSource(folder, wrinkleMapPath, SourceFlushSuffix);
            int size = height.Width;
            foreach (var image in new[] { height, crease, flush })
                if (image.Width != size || image.Height != size)
                    throw new InvalidDataException("The height, crease and flush images must all be the same square size (the height image is " + height.Width + " x " + height.Height + ").");
            var surface = BuildSurface(slot, size);
            var gray = height.FirstChannel();
            var mm = (float[])gray.Clone();
            int atLimit = 0;
            for (int i = 0; i < mm.Length; i++)
            {
                if (surface.Covered[i] && (mm[i] <= 0f || mm[i] >= 1f)) atLimit++;
                mm[i] = UMAWrinkleSourceMaps.GrayToHeight(mm[i]);
            }
            var packed = UMAWrinkleSourceMaps.Pack(surface, mm, crease.FirstChannel(), flush.FirstChannel(), out int clampedSlopes);
            var report = new System.Text.StringBuilder(size + " x " + size + ".");
            if (height.BitDepth < 16) report.Append(" The height image has " + height.BitDepth + " bits per channel, so slopes come out in steps: save it with 16 bits per channel.");
            if (atLimit > 0) report.Append(" " + atLimit + " texels of the height are at the end of its range (+/-" + UMAWrinkleSourceMaps.HeightRangeMM + " mm) and may be cut off.");
            if (clampedSlopes > 0) report.Append(" " + clampedSlopes + " texels are steeper than the wrinkle map can store and were flattened to its limit.");
            var result = new ImportResult
            {
                WrinkleMap = WriteTexture(wrinkleMapPath, ToBytes(packed), size, TextureImporterCompression.CompressedHQ),
                Report = report.ToString(),
            };
            // Keep the height next to the map in step, for Overlay Painter.
            WriteHeightSource(HeightSourcePath(wrinkleMapPath), gray, size);
            return result;
        }

        static UMAPngImage ReadSource(string folder, string wrinkleMapPath, string suffix)
        {
            string path = SourcePath(folder, wrinkleMapPath, suffix);
            if (!File.Exists(path)) throw new FileNotFoundException("The wrinkle map source " + path + " is missing.", path);
            try { return UMAPngReader.Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException e) { throw new InvalidDataException(Path.GetFileName(path) + ": " + e.Message, e); }
        }

        /// <summary>A 16-bit grayscale PNG from top-first values 0..1.</summary>
        public static byte[] EncodeGray16(float[] valuesTopFirst, int size)
        {
            var data = new ushort[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    data[(size - 1 - y) * size + x] = (ushort)Mathf.Clamp(Mathf.RoundToInt(valuesTopFirst[y * size + x] * 65535f), 0, 65535);
            var tex = new Texture2D(size, size, TextureFormat.R16, false, true);
            try { tex.SetPixelData(data, 0); tex.Apply(false, false); return tex.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        static byte[] EncodeRgba32(byte[] rgbaTopFirst, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            try { tex.SetPixelData(FlipRows(rgbaTopFirst, size), 0); tex.Apply(false, false); return tex.EncodeToPNG(); }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
        }

        /// <summary>A linear RGBA texture in memory from top-first bytes, for previews.</summary>
        public static Texture2D CreateTexture(byte[] rgbaTopFirst, int size, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, true) { name = name, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixelData(FlipRows(rgbaTopFirst, size), 0);
            tex.Apply(true, false);
            return tex;
        }

        static byte[] FlipRows(byte[] rgba, int size)
        {
            var flipped = new byte[rgba.Length];
            int row = size * 4;
            for (int y = 0; y < size; y++) Buffer.BlockCopy(rgba, y * row, flipped, (size - 1 - y) * row, row);
            return flipped;
        }

        static Texture2D WriteTexture(string path, byte[] rgbaTopFirst, int size, TextureImporterCompression compression)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            try
            {
                tex.SetPixelData(FlipRows(rgbaTopFirst, size), 0);
                tex.Apply(false, false);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(tex); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = Math.Max(size, 32);
            importer.textureCompression = compression;
            importer.isReadable = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
