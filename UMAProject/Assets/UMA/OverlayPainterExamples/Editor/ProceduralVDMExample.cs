using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Examples
{
    /// <summary>Original analytic relief: no external images or mesh data are read.</summary>
    public static class ProceduralVDMExample
    {
        private const int Size = 512;

        [MenuItem("UMA/Overlay Painter/Examples/Create Procedural Abdominal VDM")]
        public static void Create()
        {
            const string folder = "Assets/UMA/OverlayPainterExamples/VDM";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/UMA/OverlayPainterExamples", "VDM");
            var vectors = new Texture2D(Size, Size, TextureFormat.RGBAFloat, false, true);
            var height = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
            var preview = new Texture2D(Size, Size, TextureFormat.RGBA32, false, false);
            string temporary = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid() + ".exr");
            try
            {
                var pixels = new Color[Size * Size];
                var heights = new Color[pixels.Length];
                var shaded = new Color[pixels.Length];
                float step = 2f / (Size - 1);
                Vector3 light = new Vector3(-.5f, .65f, .65f).normalized;
                for (int y = 0; y < Size; y++)
                    for (int x = 0; x < Size; x++)
                    {
                        float u = x * step - 1, v = y * step - 1;
                        float h = Relief(u, v);
                        float dx = (Relief(u + step, v) - Relief(u - step, v)) / (2 * step);
                        float dy = (Relief(u, v + step) - Relief(u, v - step)) / (2 * step);
                        float fade = Edge(u, v);
                        int i = y * Size + x;
                        // Small independent in-plane motion makes this a full vector source.
                        pixels[i] = new Color(-.018f * dx * fade, -.018f * dy * fade, h, fade);
                        float value = .5f + h * 4;
                        heights[i] = new Color(value, value, value, 1);
                        Vector3 normal = new Vector3(-dx * 4, -dy * 4, 1).normalized;
                        float shade = .18f + .72f * Mathf.Max(0, Vector3.Dot(normal, light));
                        shaded[i] = new Color(shade * .9f, shade * .94f, shade, 1);
                    }
                vectors.SetPixels(pixels); vectors.Apply();
                height.SetPixels(heights); height.Apply();
                preview.SetPixels(shaded); preview.Apply();
                File.WriteAllBytes(temporary, vectors.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat | Texture2D.EXRFlags.CompressZIP));
                var brush = TexturePaintVDMImport.ImportCopy(temporary, folder + "/Sculpted Core VDM.asset");
                brush.coverage = TexturePaintVDMCoverage.SourceAlpha;
                brush.tags = "original;procedural;abdominal;relief;eight-pad;no external source data";
                EditorUtility.SetDirty(brush);
                AssetDatabase.SaveAssetIfDirty(brush);
                string stem = Path.ChangeExtension(AssetDatabase.GetAssetPath(brush), null);
                File.WriteAllBytes(stem + " Height.png", height.EncodeToPNG());
                File.WriteAllBytes(stem + " Preview.png", preview.EncodeToPNG());
                AssetDatabase.ImportAsset(stem + " Height.png", ImportAssetOptions.ForceSynchronousImport);
                var importer = (TextureImporter)AssetImporter.GetAtPath(stem + " Height.png");
                importer.sRGBTexture = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
                AssetDatabase.ImportAsset(stem + " Preview.png", ImportAssetOptions.ForceSynchronousImport);
                Selection.activeObject = brush;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
                Object.DestroyImmediate(vectors);
                Object.DestroyImmediate(height);
                Object.DestroyImmediate(preview);
            }
        }

        private static float Bell(float x, float y, float cx, float cy, float rx, float ry)
        {
            x = (x - cx) / rx; y = (y - cy) / ry;
            return Mathf.Exp(-2 * (x * x + y * y));
        }

        private static float Edge(float x, float y)
        {
            float t = Mathf.Clamp01((.94f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(y))) / .18f);
            return t * t * (3 - 2 * t);
        }

        private static float Relief(float x, float y)
        {
            float h = 0;
            // Four staggered pairs, separated by a narrow midline and shallow transverse grooves.
            for (int row = 0; row < 4; row++)
                for (int side = -1; side <= 1; side += 2)
                {
                    float cy = .54f - row * .275f + side * .012f;
                    float cx = side * (.225f - row * .012f);
                    h += (.077f - row * .008f) * Bell(x, y, cx, cy, .235f - row * .012f, .17f);
                }
            h -= .012f * Bell(x, y, 0, .14f, .045f, .73f);
            // Oblique ridges taper independently from the central pads.
            for (int side = -1; side <= 1; side += 2)
            {
                float obliqueX = side * (.5f + .09f * y);
                h += .021f * Bell(x, y, obliqueX, .04f, .13f, .62f);
                h -= .008f * Bell(x, y, side * .365f, -.32f, .065f, .32f);
            }
            h -= .031f * Bell(x, y, .018f, -.49f, .063f, .053f);
            h += .012f * Bell(x, y, .018f, -.54f, .105f, .065f);
            h += .014f * Bell(x, y, 0, -.68f, .28f, .16f);
            return h * Edge(x, y);
        }
    }
}
