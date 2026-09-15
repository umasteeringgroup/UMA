using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace UMA.HairCards.Editor
{
    /// <summary>Small, documented interchange for authored paths; imports data, never executable scripts.</summary>
    public static class HairGuideImport
    {
        public enum GenerationStyle { SweptClumps, CurlyVolume }
        [Serializable] public sealed class Guide { public string name; public Vector3[] points; }
        // Do not expose Unity Rect's private serialization fields in an external file format.
        [Serializable] public sealed class Region
        {
            public float x, y, width, height;
            public Rect Rectangle => new Rect(x, y, width, height);
        }
        [Serializable] public sealed class Data
        {
            public int version;
            public string name, sourceSlot, atlasFile;
            public Vector3[] vertices, normals;
            public Vector2[] uv;
            public int[] triangles;
            public float[] growth;
            public Guide[] guides;
            public Region[] regions;
            public bool rootAtVOne = true;
        }

        [MenuItem("UMA/Hair Cards/Import Authored Guide File…", priority = 105)]
        public static void ImportDialog()
        {
            string path = EditorUtility.OpenFilePanel("Import authored hair guides (Unity axes, meters)", string.Empty, "json");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                // Creating a new asset never overwrites the current groom or its sculpt history.
                string destination = EditorUtility.SaveFilePanelInProject("Save Imported Groom", "Swept_HairGroom", "asset", "A new resource folder is created beside this groom.");
                if (string.IsNullOrEmpty(destination)) return;
                var groom = ImportNew(path, destination);
                Selection.activeObject = groom; HairCardStage.ShowStage(groom);
            }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("Hair Guide Import", e.Message, "OK"); }
        }

        public static Data Read(string path)
        {
            if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new InvalidDataException("Guide files must be smaller than 64 MB.");
            var data = JsonUtility.FromJson<Data>(File.ReadAllText(path));
            if (data?.version != 1 || data.vertices == null || data.vertices.Length < 3 || data.vertices.Length > 1000000 ||
                data.triangles == null || data.triangles.Length < 3 || data.triangles.Length % 3 != 0 || data.guides == null || data.guides.Length > 20000)
                throw new InvalidDataException("Not a version 1 UMA authored-guide file. See the Swept Clumps guide for its format.");
            bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
            foreach (var p in data.vertices) if (!Finite(p)) throw new InvalidDataException("Source contains non-finite positions.");
            if (data.normals?.Length > 0 && (data.normals.Length != data.vertices.Length || Array.Exists(data.normals, n => !Finite(n) || n.sqrMagnitude < 1e-12f)))
                throw new InvalidDataException("Supply one finite, nonzero normal per source vertex, or omit normals to calculate them.");
            if (data.uv?.Length > 0 && (data.uv.Length != data.vertices.Length || Array.Exists(data.uv, uv => !float.IsFinite(uv.x) || !float.IsFinite(uv.y))))
                throw new InvalidDataException("Supply one finite UV per source vertex, or omit source UVs.");
            if (data.growth?.Length > 0 && (data.growth.Length != data.vertices.Length || Array.Exists(data.growth, v => !float.IsFinite(v))))
                throw new InvalidDataException("Supply one finite growth value per source vertex, or omit growth and paint it in Unity.");
            foreach (int i in data.triangles) if ((uint)i >= (uint)data.vertices.Length) throw new InvalidDataException("Source triangle index is out of range.");
            if (data.regions != null)
                foreach (var region in data.regions)
                    if (region == null || !float.IsFinite(region.x) || !float.IsFinite(region.y) || !float.IsFinite(region.width) || !float.IsFinite(region.height) ||
                        region.x < 0f || region.y < 0f || region.width <= 0f || region.height <= 0f || region.x + region.width > 1.00001f || region.y + region.height > 1.00001f)
                        throw new InvalidDataException("UV regions need finite x/y/width/height in 0..1, with nonzero width and height.");
            foreach (var guide in data.guides)
            {
                if (guide?.points == null || guide.points.Length < 2 || guide.points.Length > 256) throw new InvalidDataException("Each guide needs 2–256 ordered root-to-tip points.");
                foreach (var p in guide.points) if (!Finite(p)) throw new InvalidDataException("Guide contains non-finite positions.");
            }
            return data;
        }

        public static HairGroomAsset ImportNew(string file, string assetPath, GenerationStyle style = GenerationStyle.SweptClumps)
        {
            var data = Read(file);
            string parent = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(parent)) throw new ArgumentException("Choose an existing Assets folder.");
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            string name = Path.GetFileNameWithoutExtension(assetPath);
            string folderGuid = AssetDatabase.CreateFolder(parent, name + " Resources");
            string folder = AssetDatabase.GUIDToAssetPath(folderGuid);
            if (string.IsNullOrEmpty(folder)) throw new IOException("Cannot create the groom resource folder.");
            HairGroomAsset groom = null;
            try
            {
                var mesh = new Mesh { name = name + " Source", indexFormat = data.vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.vertices = data.vertices; mesh.triangles = data.triangles;
                if (data.normals?.Length == data.vertices.Length) mesh.normals = data.normals; else mesh.RecalculateNormals();
                if (data.uv?.Length == data.vertices.Length) mesh.uv = data.uv;
                mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh, folder + "/Source.asset");
                groom = ScriptableObject.CreateInstance<HairGroomAsset>(); groom.name = name;
                groom.SetSource(mesh, "asset:" + AssetDatabase.AssetPathToGUID(folder + "/Source.asset"));
                AssetDatabase.CreateAsset(groom, assetPath);
                var group = groom.Groups[0]; group.name = string.IsNullOrEmpty(data.name) ? "Swept Hair" : data.name;
                var growth = group.FindMap(HairMapKind.GrowthArea);
                if (data.growth?.Length == growth.values.Length)
                    for (int i = 0; i < growth.values.Length; i++) growth.values[i] = float.IsFinite(data.growth[i]) ? Mathf.Clamp01(data.growth[i]) : 0f;
                ImportGuides(groom, group, data.guides);
                if (style == GenerationStyle.CurlyVolume) HairGenerationEditor.ApplyCurlyPreset(groom, group, folder);
                else HairGenerationEditor.ApplySweptPreset(groom, group, folder);
                if (data.regions?.Length > 0)
                {
                    group.atlas.regions.Clear();
                    for (int i = 0; i < data.regions.Length; i++) group.atlas.CreateRegion("Strand strip " + (i + 1), data.regions[i].Rectangle).flipV = data.rootAtVOne;
                    group.atlasRegionSelection = HairAtlasRegionSelectionMode.All;
                    group.atlasRegionIds.Clear();
                }
                if (!string.IsNullOrEmpty(data.atlasFile))
                {
                    if (Path.GetFileName(data.atlasFile) != data.atlasFile || !data.atlasFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("atlasFile must be the name of a PNG beside the guide file, not a path.");
                    string texturePath = folder + "/StrandAtlas.png";
                    File.Copy(Path.Combine(Path.GetDirectoryName(file), data.atlasFile), texturePath, false);
                    AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
                    var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    importer.alphaSource = TextureImporterAlphaSource.FromInput; importer.alphaIsTransparency = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed; importer.anisoLevel = 8; importer.mipmapEnabled = true;
                    importer.wrapMode = TextureWrapMode.Clamp; importer.SaveAndReimport();
                    group.atlas.albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    group.atlas.ApplyTexturesTo(group.atlas.material); EditorUtility.SetDirty(group.atlas.material);
                }
                // Source geometry from a DCC is not proof of a UMA slot vertex mapping.
                // Preview works immediately; production baking requires binding the original slot.
                groom.BakeSettings.createWardrobeRecipe = false; groom.BakeSettings.createSlot = false; groom.BakeSettings.createOverlay = false;
                HairGroomCommands.AddSculptLayer(groom, group, "Refine imported flow");
                EditorUtility.SetDirty(group.atlas); HairGroomCommands.Commit(groom); AssetDatabase.SaveAssets();
                return AssetDatabase.LoadAssetAtPath<HairGroomAsset>(assetPath);
            }
            catch
            {
                // Only assets created by this failed import are removed. Never touch the source file.
                if (groom != null) AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.DeleteAsset(folder); throw;
            }
        }

        internal static void ImportGuides(HairGroomAsset groom, HairGroup group, Guide[] guides)
        {
            for (int i = 0; i < guides.Length; i++)
            {
                var input = guides[i];
                if (!HairMeshUtility.TryFindClosestSurface(groom.SourceMesh, groom.SourceMeshId, input.points[0], out var anchor))
                    throw new InvalidDataException("Cannot attach imported guide " + input.name);
                Vector3 delta = anchor.CachedLocalPosition - input.points[0];
                var guide = new HairGuide { name = input.name, root = anchor, seed = i * 16777619 + 31 };
                foreach (var point in input.points) guide.points.Add(new HairGuidePoint { position = point + delta, width = 0.01f, widthBaseline = 0.01f });
                guide.EnsureIntegrity(0.01f); group.guides.Add(guide);
            }
        }
    }
}
