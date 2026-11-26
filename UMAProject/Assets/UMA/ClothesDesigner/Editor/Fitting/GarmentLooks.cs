using System;
using System.Collections.Generic;
using System.IO;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// A look per piece (<see cref="PieceLook"/>): for each look the pieces use, the bake draws its fabric
    /// textures into the slot's texture space, tiled at their real size along each piece's grain, and makes an
    /// overlay of them masked to those pieces' islands. The overlays go on the clothes' slot over its own
    /// overlay, so one slot and one material carry several fabrics, each with its own colour.
    /// </summary>
    public static class GarmentLooks
    {
        /// <summary>A look as baked: its overlay and how much of the texture it covers.</summary>
        public sealed class BakedLook
        {
            public PieceLook look;
            public OverlayDataAsset overlay;
            public List<string> pieces = new List<string>();
            public int texels;
        }

        /// <summary>True when any piece uses a look that exists.</summary>
        public static bool HasLooks(ClothesPattern pattern)
        {
            if (pattern?.looks == null || pattern.looks.Count == 0) return false;
            foreach (PatternPiece piece in pattern.pieces) if (piece != null && pattern.FindLook(piece.lookId) != null) return true;
            return false;
        }

        /// <summary>The piece whose island a piece's texels are: its original, for a mirror sharing its original's place.</summary>
        public static int TexelOwner(ClothesPattern pattern, int piece)
        {
            UvIsland island = pattern.pieces[piece].uvIsland;
            if (island != null && !string.IsNullOrEmpty(island.sharesWith))
            {
                int source = pattern.IndexOfPiece(island.sharesWith);
                if (source >= 0) return source;
            }
            return piece;
        }

        /// <summary>
        /// For each texel of a <paramref name="size"/> square texture, the piece whose island covers it (-1 for
        /// none), from the mesh's UV triangles, grown by <paramref name="gutter"/> texels so mipmaps and
        /// filtering at an island's edge read its own fabric.
        /// </summary>
        public static int[] OwnerMap(ClothesPattern pattern, PatternMesh mesh, int size, int gutter)
        {
            var owner = new int[size * size];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            for (int t = 0; t + 2 < mesh.triangles.Count; t += 3)
            {
                int ia = mesh.triangles[t], ib = mesh.triangles[t + 1], ic = mesh.triangles[t + 2];
                int piece = TexelOwner(pattern, mesh.piece[ia]);
                Vector2 a = mesh.uv[ia] * size, b = mesh.uv[ib] * size, c = mesh.uv[ic] * size;
                float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (Mathf.Abs(area) < 1e-9f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)))), x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)))), y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((b.x - px) * (c.y - py) - (b.y - py) * (c.x - px)) / area;
                        float w1 = ((c.x - px) * (a.y - py) - (c.y - py) * (a.x - px)) / area;
                        const float edge = -1e-4f;
                        if (w0 < edge || w1 < edge || 1f - w0 - w1 < edge) continue;
                        if (owner[y * size + x] < 0) owner[y * size + x] = piece;
                    }
                }
            }
            var next = new int[owner.Length];
            for (int step = 0; step < gutter; step++)
            {
                Array.Copy(owner, next, owner.Length);
                bool grew = false;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int i = y * size + x;
                        if (owner[i] >= 0) continue;
                        int o = -1;
                        if (x > 0 && owner[i - 1] >= 0) o = owner[i - 1];
                        else if (x < size - 1 && owner[i + 1] >= 0) o = owner[i + 1];
                        else if (y > 0 && owner[i - size] >= 0) o = owner[i - size];
                        else if (y < size - 1 && owner[i + size] >= 0) o = owner[i + size];
                        if (o >= 0) { next[i] = o; grew = true; }
                    }
                }
                Array.Copy(next, owner, owner.Length);
                if (!grew) break;
            }
            return owner;
        }

        /// <summary>The point on a piece as drawn (cm) that a UV of its island shows.</summary>
        public static Vector2 DrawnAt(ClothesPattern pattern, int piece, Vector2 uv)
        {
            UvIsland island = pattern.pieces[piece].uvIsland;
            float aspect = Mathf.Max(0.05f, pattern.uvLayout != null ? pattern.uvLayout.aspect : 1f);
            float k = Mathf.Max(1e-9f, island.scale);
            Vector2 q = new Vector2((uv.x - island.offset.x) / k, (uv.y - island.offset.y) / (k * aspect));
            return Rotate(q, -island.rotation);
        }

        /// <summary>Where a point of a piece as drawn falls in a look's textures (0 to 1 is one repeat).</summary>
        public static Vector2 LookCoordinate(PatternPiece piece, PieceLook look, Vector2 drawn) =>
            Rotate(drawn, -piece.grainAngle - look.rotation) / Mathf.Max(0.5f, look.repeatCm);

        /// <summary>The turn from a look's texture axes to the slot's UV axes on a piece (degrees), for its normal map.</summary>
        public static float NormalTurn(PatternPiece piece, PieceLook look) => piece.uvIsland.rotation + piece.grainAngle + look.rotation;

        /// <summary>
        /// Bakes every look the pieces use into an overlay in <paramref name="folder"/>: colour, normal and other
        /// channel textures where the look has them, and an alpha mask of its pieces' islands. Normal maps are
        /// combined with <paramref name="drapeNormals"/> (the drape's folds), when given, so a look's weave does not
        /// hide them.
        /// </summary>
        public static List<BakedLook> Bake(ClothesPattern pattern, PatternMesh mesh, UMAMaterial material, string folder, string slotName,
            Color32[] drapeNormals, int drapeSize, bool addToGlobalLibrary, List<string> createdPaths, List<string> messages)
        {
            var baked = new List<BakedLook>();
            if (!HasLooks(pattern)) return baked;
            if (material == null || material.channels == null || material.channels.Length == 0) { messages?.Add("No looks baked: the clothes' overlay has no material."); return baked; }
            int albedoChannel = -1, normalChannel = -1;
            for (int i = 0; i < material.channels.Length; i++)
            {
                UMAMaterial.ChannelType type = material.channels[i].channelType;
                if (type == UMAMaterial.ChannelType.NormalMap) { if (normalChannel < 0) normalChannel = i; }
                else if (albedoChannel < 0 && (type == UMAMaterial.ChannelType.Texture || type == UMAMaterial.ChannelType.DiffuseTexture || type == UMAMaterial.ChannelType.TintedTexture)) albedoChannel = i;
            }
            if (albedoChannel < 0) { messages?.Add("No looks baked: the material '" + material.name + "' has no colour texture channel."); return baked; }

            int size = Mathf.Max(64, pattern.uvLayout.bakeResolution);
            int[] owner = OwnerMap(pattern, mesh, size, Mathf.Max(1, pattern.uvLayout.gutterPixels));
            string baseName = UMASlotSkinTransfer.MakeSafeAssetName(slotName);
            var usedNames = new HashSet<string>();
            foreach (PieceLook look in pattern.looks)
            {
                if (look == null) continue;
                var mine = new bool[pattern.pieces.Count];
                var result = new BakedLook { look = look };
                for (int p = 0; p < pattern.pieces.Count; p++)
                {
                    // A mirror sharing its original's texels shows its original's look.
                    if (TexelOwner(pattern, p) == p && pattern.pieces[p].lookId == look.id) { mine[p] = true; result.pieces.Add(pattern.pieces[p].name); }
                }
                if (result.pieces.Count == 0) continue;
                string lookName = UMASlotSkinTransfer.MakeSafeAssetName(string.IsNullOrEmpty(look.name) ? "Look" : look.name);
                string unique = lookName;
                for (int n = 2; !usedNames.Add(unique); n++) unique = lookName + n;
                string stem = folder.TrimEnd('/') + "/" + baseName + "_Look_" + unique;

                Image albedo = Image.Read(look.albedo, false), normal = Image.Read(look.normal, true), other = Image.Read(look.mask, false);
                var albedoPixels = new Color32[size * size];
                var maskPixels = new Color32[size * size];
                Color32[] normalPixels = normal != null ? new Color32[size * size] : null;
                Color32[] otherPixels = other != null ? new Color32[size * size] : null;
                Color32 fillAlbedo = albedo != null ? (Color32)albedo.Mean : new Color32(255, 255, 255, 255);
                Color32 fillOther = other != null ? (Color32)other.Mean : default;
                float[] turnCos = new float[pattern.pieces.Count], turnSin = new float[pattern.pieces.Count];
                for (int p = 0; p < pattern.pieces.Count; p++)
                {
                    float turn = NormalTurn(pattern.pieces[p], look) * Mathf.Deg2Rad;
                    turnCos[p] = Mathf.Cos(turn); turnSin[p] = Mathf.Sin(turn);
                }
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int i = y * size + x;
                        int p = owner[i];
                        bool inside = p >= 0 && mine[p];
                        maskPixels[i] = new Color32(255, 255, 255, (byte)(inside ? 255 : 0));
                        Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                        Vector3 drape = drapeNormals != null ? Decode(Sample(drapeNormals, drapeSize, uv)) : Vector3.forward;
                        if (!inside)
                        {
                            albedoPixels[i] = fillAlbedo;
                            if (otherPixels != null) otherPixels[i] = fillOther;
                            if (normalPixels != null) normalPixels[i] = Encode(drape);
                            continue;
                        }
                        result.texels++;
                        Vector2 t = LookCoordinate(pattern.pieces[p], look, DrawnAt(pattern, p, uv));
                        albedoPixels[i] = albedo != null ? (Color32)albedo.Bilinear(t) : new Color32(255, 255, 255, 255);
                        if (otherPixels != null) otherPixels[i] = other.Bilinear(t);
                        if (normalPixels != null)
                        {
                            Color c = normal.Bilinear(t);
                            // Unity's normal maps keep x in alpha (DXT5nm) or red (BC5, plain RGB): red times alpha reads both.
                            float nx = (c.r * c.a) * 2f - 1f, ny = c.g * 2f - 1f;
                            nx *= look.normalStrength; ny *= look.normalStrength;
                            float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Min(1f, nx * nx + ny * ny)));
                            // From the look's axes to the slot's UV axes on this piece.
                            float rx = nx * turnCos[p] - ny * turnSin[p], ry = nx * turnSin[p] + ny * turnCos[p];
                            Vector3 n = new Vector3(drape.x + rx, drape.y + ry, drape.z * nz).normalized;
                            normalPixels[i] = Encode(n);
                        }
                    }
                }

                var textures = new Texture[material.channels.Length];
                textures[albedoChannel] = WriteTexture(albedoPixels, size, stem + "_Color.png", true, createdPaths);
                if (normalPixels != null && normalChannel >= 0)
                {
                    var map = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                    map.SetPixels32(normalPixels); map.Apply(false);
                    try { textures[normalChannel] = DrapeNormalMap.Write(map, stem + "_Normal.png"); }
                    finally { UnityEngine.Object.DestroyImmediate(map); }
                    createdPaths?.Add(stem + "_Normal.png");
                }
                else if (normalPixels != null) messages?.Add("Look '" + look.name + "': the material has no normal map channel, so its normal map was left out.");
                if (otherPixels != null)
                {
                    int otherChannel = -1;
                    for (int i = 0; i < material.channels.Length; i++)
                    {
                        if (i == albedoChannel || i == normalChannel) continue;
                        UMAMaterial.ChannelType type = material.channels[i].channelType;
                        if (type == UMAMaterial.ChannelType.Texture || type == UMAMaterial.ChannelType.DiffuseTexture || type == UMAMaterial.ChannelType.TintedTexture) { otherChannel = i; break; }
                    }
                    if (otherChannel >= 0) textures[otherChannel] = WriteTexture(otherPixels, size, stem + "_Mask.png", IsSrgb(look.mask), createdPaths);
                    else messages?.Add("Look '" + look.name + "': the material has no third texture channel, so its mask texture was left out.");
                }
                Texture alphaMask = WriteTexture(maskPixels, size, stem + "_Pieces.png", false, createdPaths);

                string overlayPath = stem + "_Overlay.asset";
                OverlayDataAsset overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(overlayPath);
                bool created = overlay == null;
                if (created) { overlay = ScriptableObject.CreateInstance<OverlayDataAsset>(); overlay.name = Path.GetFileNameWithoutExtension(overlayPath); }
                overlay._oldOverlayName = Path.GetFileNameWithoutExtension(stem);
                overlay.material = material;
                overlay.textureList = textures;
                overlay.alphaMask = alphaMask;
                overlay.noAutoAdd = !addToGlobalLibrary;
                if (created) { AssetDatabase.CreateAsset(overlay, overlayPath); createdPaths?.Add(overlayPath); }
                EditorUtility.SetDirty(overlay);
                AssetDatabase.SaveAssetIfDirty(overlay);
                if (addToGlobalLibrary) UMAGlobalLibraryAutoRegistration.AddAndSave(overlay);
                result.overlay = overlay;
                baked.Add(result);
            }
            return baked;
        }

        /// <summary>The colour an overlay of a look is worn with: its own colour, under its shared colour's name when it has one.</summary>
        public static OverlayColorData ColorFor(PieceLook look, int channels)
        {
            var color = new OverlayColorData(Mathf.Max(1, channels));
            color.channelMask[0] = look.color;
            color.name = string.IsNullOrWhiteSpace(look.sharedColorName) ? OverlayColorData.UNSHARED : look.sharedColorName.Trim();
            return color;
        }

        private static bool IsSrgb(Texture texture) => texture != null && GraphicsFormatUtility.IsSRGBFormat(texture.graphicsFormat);

        private static Texture2D WriteTexture(Color32[] pixels, int size, string assetPath, bool srgb, List<string> createdPaths)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
            try
            {
                texture.SetPixels32(pixels); texture.Apply(false);
                string full = Path.GetFullPath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllBytes(full, texture.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(assetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = srgb;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = true;
                importer.isReadable = false;
                importer.maxTextureSize = Mathf.Max(importer.maxTextureSize, size);
                importer.SaveAndReimport();
            }
            createdPaths?.Add(assetPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        private static Color32 Sample(Color32[] pixels, int size, Vector2 uv)
        {
            int x = Mathf.Clamp((int)(uv.x * size), 0, size - 1), y = Mathf.Clamp((int)(uv.y * size), 0, size - 1);
            return pixels[y * size + x];
        }

        private static Vector3 Decode(Color32 c) => new Vector3(c.r / 127.5f - 1f, c.g / 127.5f - 1f, c.b / 127.5f - 1f).normalized;

        private static Color32 Encode(Vector3 n) =>
            new Color32((byte)Mathf.RoundToInt((n.x * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((n.y * 0.5f + 0.5f) * 255f), (byte)Mathf.RoundToInt((n.z * 0.5f + 0.5f) * 255f), 255);

        private static Vector2 Rotate(Vector2 p, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        /// <summary>A texture's texels, read through the GPU so any import settings work, sampled with wrapping.</summary>
        private sealed class Image
        {
            private Color[] pixels;
            private int width, height;
            public Color Mean { get; private set; }

            /// <summary>The texture's stored values (encoded colours stay encoded), or null for none.</summary>
            public static Image Read(Texture2D texture, bool isNormal)
            {
                if (texture == null) return null;
                // An sRGB texture through an sRGB target, a linear one through a linear target: the stored values come back unchanged.
                bool srgb = !isNormal && IsSrgb(texture);
                var target = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
                RenderTexture previous = RenderTexture.active;
                var copy = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, !srgb);
                try
                {
                    Graphics.Blit(texture, target);
                    RenderTexture.active = target;
                    copy.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
                    copy.Apply(false);
                    var image = new Image { pixels = copy.GetPixels(), width = texture.width, height = texture.height };
                    Color sum = Color.clear;
                    foreach (Color c in image.pixels) sum += c;
                    image.Mean = sum / Mathf.Max(1, image.pixels.Length);
                    return image;
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target);
                    UnityEngine.Object.DestroyImmediate(copy);
                }
            }

            public Color Bilinear(Vector2 t)
            {
                float x = t.x * width - 0.5f, y = t.y * height - 0.5f;
                int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
                float fx = x - x0, fy = y - y0;
                Color a = At(x0, y0), b = At(x0 + 1, y0), c = At(x0, y0 + 1), d = At(x0 + 1, y0 + 1);
                return Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(c, d, fx), fy);
            }

            private Color At(int x, int y)
            {
                x %= width; if (x < 0) x += width;
                y %= height; if (y < 0) y += height;
                return pixels[y * width + x];
            }
        }
    }
}
