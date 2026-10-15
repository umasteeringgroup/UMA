using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor
{
    [InitializeOnLoad]
    public static class SpriteMaterialThumbnails
    {
        public const int Size = 256;
        private sealed class Work { public OverlayPainterSpriteSet set; public string hash; public int index, count; public bool cube; }
        private static readonly Queue<Work> Queue = new();
        private static readonly MethodInfo SetCreationScene = typeof(EditorSceneManager).GetMethod("SetTargetSceneForNewGameObjects", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(Scene) }, null);
        private static readonly MethodInfo GetCreationScene = typeof(EditorSceneManager).GetMethod("GetTargetSceneForNewGameObjects", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly MethodInfo ClearCreationScene = typeof(EditorSceneManager).GetMethod("ClearTargetSceneForNewGameObjects", BindingFlags.Static | BindingFlags.NonPublic);
        public static event Action<OverlayPainterSpriteSet> Changed;
        public static int PendingSets => Queue.Count;
        public static string LastError { get; private set; }
        static SpriteMaterialThumbnails()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += () => Queue.Clear();
        }

        public static string SourceHash(OverlayPainterSpriteSet set)
        {
            var text = new StringBuilder("URP-Lit-Sphere-1|");
            foreach (var sheet in set.spriteSheets)
            {
                if (sheet == null) { text.Append("missing|"); continue; }
                text.Append(sheet.channel).Append(sheet.inverted);
                if (sheet.sprites?.Count > 0)
                    foreach (var sprite in sheet.sprites)
                    {
                        if (sprite == null) { text.Append("missing|"); continue; }
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long local);
                        text.Append(guid).Append(local).Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(sprite)));
                    }
                else text.Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(sheet.spriteSheet)));
            }
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null) text.Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(shader)));
            var pack = Shader.Find("Hidden/UMA/TexturePaint/SpriteMaterialPreviewPack");
            if (pack != null) text.Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(pack)));
            return Hash128.Compute(text.ToString()).ToString();
        }

        public static void Enqueue(OverlayPainterSpriteSet set, bool force = false)
        {
            if (set == null || !AssetDatabase.Contains(set)) return;
            int count = OverlayPainterSpriteSetEditorUtility.GetCommonSpriteCount(set);
            if (count == 0) return;
            string hash = SourceHash(set);
            foreach (bool cube in new[] { false, true })
            {
                if (Queue.Any(w => w.set == set && w.cube == cube)) continue;
                string versionedHash = cube ? hash + "|CutoutCube-1" : hash;
                var thumbnails = cube ? set.cubeThumbnails : set.thumbnails;
                if (!force && (cube ? set.cubeThumbnailSourceHash : set.thumbnailSourceHash) == versionedHash &&
                    thumbnails?.Count >= count && Enumerable.Range(0, count).All(i => thumbnails[i] != null)) continue;
                Queue.Enqueue(new Work { set = set, hash = versionedHash, count = count, cube = cube });
            }
            LastError = null;
        }

        [MenuItem("UMA/Overlay Painter/Rebuild Sprite Material Thumbnails")]
        public static void RebuildAll()
        { foreach (var set in OverlayPainterSpriteSetEditorUtility.FindAllSpriteSets()) Enqueue(set, true); }

        private static void Update()
        {
            if (Queue.Count == 0 || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode) return;
            Work work = Queue.Peek();
            if (work.set == null) { Queue.Dequeue(); return; }
            Texture2D rendered = null;
            try
            {
                rendered = Render(work.set, work.index, work.cube);
                var thumbnails = work.cube ? work.set.cubeThumbnails ??= new List<Texture2D>()
                    : work.set.thumbnails ??= new List<Texture2D>();
                while (thumbnails.Count <= work.index) thumbnails.Add(null);
                var previous = thumbnails[work.index];
                string thumbnailName = (work.cube ? "Cube Thumbnail " : "Material Thumbnail ") + work.index;
                if (previous != null && AssetDatabase.GetAssetPath(previous) == AssetDatabase.GetAssetPath(work.set))
                {
                    EditorUtility.CopySerialized(rendered, previous);
                    previous.name = thumbnailName;
                    previous.hideFlags = HideFlags.HideInHierarchy;
                    EditorUtility.SetDirty(previous);
                }
                else
                {
                    rendered.name = thumbnailName;
                    rendered.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(rendered, work.set);
                    thumbnails[work.index] = rendered;
                    rendered = null;
                }
                EditorUtility.SetDirty(work.set);
                Changed?.Invoke(work.set);
                if (++work.index >= work.count)
                {
                    if (work.cube) work.set.cubeThumbnailSourceHash = work.hash;
                    else work.set.thumbnailSourceHash = work.hash;
                    AssetDatabase.SaveAssetIfDirty(work.set);
                    Queue.Dequeue();
                }
            }
            catch (Exception e)
            {
                LastError = work.set.DisplayName + ": " + e.Message;
                Debug.LogError("Sprite material thumbnail failed: " + LastError);
                Queue.Dequeue();
            }
            finally { if (rendered != null) Object.DestroyImmediate(rendered); }
        }

        // Sources are extracted into bounded temporary targets without changing importer settings
        // or filling the painter's global sprite cache. All resources belong to this render.
        public static Texture2D Render(OverlayPainterSpriteSet set, int index, bool cutoutCube = false)
        {
            if (SetCreationScene == null || GetCreationScene == null || ClearCreationScene == null)
                throw new NotSupportedException("Unity's isolated preview-object creation API is unavailable.");
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            Shader extractShader = Shader.Find("Hidden/UMA/TexturePaint/SourceExtract");
            Shader packShader = Shader.Find("Hidden/UMA/TexturePaint/SpriteMaterialPreviewPack");
            if (lit == null || extractShader == null || packShader == null)
                throw new InvalidOperationException("URP Lit or Overlay Painter's preview shaders are unavailable.");
            var owned = new List<Object>();
            var targets = new List<RenderTexture>();
            T Own<T>(T obj) where T : Object { owned.Add(obj); return obj; }
            RenderTexture Target()
            {
                var rt = RenderTexture.GetTemporary(512,512,0,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);
                rt.wrapMode = TextureWrapMode.Repeat; targets.Add(rt); return rt;
            }
            var preview = new PreviewRenderUtility();
            Scene previousCreationScene = (Scene)GetCreationScene.Invoke(null, null);
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                SetCreationScene.Invoke(null, new object[] { preview.camera.gameObject.scene });
                var extract = Own(new Material(extractShader));
                var pack = Own(new Material(packShader));
                var material = Own(new Material(lit));
                var maps = new Dictionary<TexturePaintChannel, Texture>();
                foreach (var sheet in set.spriteSheets)
                {
                    if (!OverlayPainterSpriteSetEditorUtility.TryGetSprite(sheet, index, out var sprite))
                        throw new InvalidOperationException("Missing sprite " + index + " in " + sheet?.SheetName);
                    Texture2D texture = sprite.texture;
                    Rect rect = sprite.packed ? sprite.textureRect : sprite.rect;
                    var scaleOffset = new Vector4(rect.width / texture.width, rect.height / texture.height, rect.x / texture.width, rect.y / texture.height);
                    switch (sprite.packingRotation)
                    {
                        case SpritePackingRotation.FlipHorizontal: scaleOffset.z += scaleOffset.x; scaleOffset.x *= -1; break;
                        case SpritePackingRotation.FlipVertical: scaleOffset.w += scaleOffset.y; scaleOffset.y *= -1; break;
                        case SpritePackingRotation.Rotate180: scaleOffset.z += scaleOffset.x; scaleOffset.w += scaleOffset.y; scaleOffset.x *= -1; scaleOffset.y *= -1; break;
                    }
                    bool normal = sheet.channel == TexturePaintChannel.Normal;
                    bool packed = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer && importer.textureType == TextureImporterType.NormalMap;
                    extract.SetVector("_ScaleOffset", scaleOffset);
                    extract.SetInt("_SourceIsNormalMap", packed ? 1 : 0);
                    extract.SetInt("_SourceIsSRGB", texture.isDataSRGB ? 1 : 0);
                    extract.SetInt("_InvertGreen", 0);
                    extract.SetInt("_InvertChannels", sheet.inverted ? 1 : 0);
                    extract.SetInt("_Grayscale", TexturePaintChannelUtility.IsGrayscale(sheet.channel) ? 1 : 0);
                    var rt = Target(); Graphics.Blit(texture, rt, extract, normal ? 2 : 0);
                    maps[sheet.channel] = rt;
                }
                Texture Map(TexturePaintChannel ch, Texture fallback) => maps.TryGetValue(ch, out var t) ? t : fallback;
                material.SetTexture("_BaseMap", Map(TexturePaintChannel.Albedo, Texture2D.whiteTexture));
                material.SetColor("_BaseColor", maps.ContainsKey(TexturePaintChannel.Albedo) ? Color.white : new Color(.55f,.58f,.62f));
                if (cutoutCube)
                {
                    Texture coverage = Map(TexturePaintChannel.Albedo, maps.Values.FirstOrDefault() ?? Texture2D.whiteTexture);
                    pack.SetTexture("_Albedo", Map(TexturePaintChannel.Albedo, Texture2D.whiteTexture));
                    pack.SetTexture("_Coverage", coverage);
                    pack.SetFloat("_Mode", 2);
                    var cutout = Target(); Graphics.Blit(null, cutout, pack);
                    material.SetTexture("_BaseMap", cutout);
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }
                pack.SetTexture("_Metallic", Map(TexturePaintChannel.Metallic, Texture2D.blackTexture));
                pack.SetTexture("_Roughness", Map(TexturePaintChannel.Roughness, Texture2D.grayTexture));
                pack.SetFloat("_Mode", 0);
                var metallic = Target(); Graphics.Blit(null, metallic, pack);
                material.SetTexture("_MetallicGlossMap", metallic);
                material.SetFloat("_Metallic", 1); material.SetFloat("_Smoothness", 1);
                material.SetFloat("_SmoothnessTextureChannel", 0);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                if (maps.ContainsKey(TexturePaintChannel.Normal) || maps.ContainsKey(TexturePaintChannel.NormalControl))
                {
                    pack.SetTexture("_Normal", Map(TexturePaintChannel.Normal, Texture2D.normalTexture));
                    pack.SetTexture("_Height", Map(TexturePaintChannel.NormalControl, Texture2D.grayTexture));
                    pack.SetFloat("_HasNormal", maps.ContainsKey(TexturePaintChannel.Normal) ? 1 : 0);
                    pack.SetFloat("_HasHeight", maps.ContainsKey(TexturePaintChannel.NormalControl) ? 1 : 0);
                    pack.SetFloat("_Mode", 1);
                    var bump = Target(); Graphics.Blit(null, bump, pack);
                    material.SetTexture("_BumpMap", bump); material.SetFloat("_BumpScale", 1); material.EnableKeyword("_NORMALMAP");
                }
                if (maps.ContainsKey(TexturePaintChannel.AmbientOcclusion))
                { material.SetTexture("_OcclusionMap", maps[TexturePaintChannel.AmbientOcclusion]); material.SetFloat("_OcclusionStrength",1); material.EnableKeyword("_OCCLUSIONMAP"); }
                if (maps.ContainsKey(TexturePaintChannel.Emission))
                { material.SetTexture("_EmissionMap", maps[TexturePaintChannel.Emission]); material.SetColor("_EmissionColor",Color.white); material.EnableKeyword("_EMISSION"); }
                material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", .05f); material.EnableKeyword("_ALPHATEST_ON");
                var sphere = GameObject.CreatePrimitive(cutoutCube ? PrimitiveType.Cube : PrimitiveType.Sphere);
                if (cutoutCube) sphere.transform.rotation = Quaternion.Euler(45,45,45);
                sphere.hideFlags = HideFlags.HideAndDontSave;
                preview.AddSingleGO(sphere);
                sphere.GetComponent<Renderer>().sharedMaterial = material;
                Object.DestroyImmediate(sphere.GetComponent<Collider>());
                var cube = Own(StudioReflection());
                var probeObject = new GameObject("Material studio reflection") { hideFlags = HideFlags.HideAndDontSave };
                preview.AddSingleGO(probeObject);
                var probe = probeObject.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom; probe.customBakedTexture = cube;
                probe.size = Vector3.one * 10; probe.intensity = 1;
                preview.cameraFieldOfView = 28;
                preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 20;
                preview.camera.transform.position = new Vector3(0,0,cutoutCube ? 3.8f : 2.4f); preview.camera.transform.LookAt(Vector3.zero);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.11f,.12f,.14f,1);
                preview.ambientColor = new Color(.25f,.27f,.3f,1);
                preview.lights[0].transform.rotation = Quaternion.Euler(35,215,0);
                preview.lights[0].intensity = 2.5f;
                preview.lights[1].transform.rotation = Quaternion.Euler(340,115,0);
                preview.lights[1].intensity = 1.5f;
                preview.BeginStaticPreview(new Rect(0,0,Size,Size));
                preview.Render(true);
                var result = preview.EndStaticPreview(); result.name = "Material Thumbnail " + index;
                return result;
            }
            finally
            {
                if (previousCreationScene.IsValid()) SetCreationScene.Invoke(null, new object[] { previousCreationScene });
                else ClearCreationScene.Invoke(null, null);
                RenderTexture.active = previousActive;
                preview.Cleanup();
                foreach (var rt in targets) RenderTexture.ReleaseTemporary(rt);
                for (int i = owned.Count-1; i >= 0; i--) Object.DestroyImmediate(owned[i]);
            }
        }

        private static Cubemap StudioReflection()
        {
            const int size = 32;
            var cube = new Cubemap(size, TextureFormat.RGBAHalf, true);
            for (int face = 0; face < 6; face++)
            {
                var pixels = new Color[size*size];
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float u = (x+.5f)/size, v = (y+.5f)/size;
                    float panel = (face == 0 || face == 4) && u > .18f && u < .68f && v > .15f && v < .85f ? 2.5f : 0;
                    float light = face == 2 ? .6f : .08f + .12f*v;
                    pixels[y*size+x] = new Color(light+panel,light+panel,light+panel,1);
                }
                cube.SetPixels(pixels,(CubemapFace)face);
            }
            cube.Apply(true); return cube;
        }
    }
}
