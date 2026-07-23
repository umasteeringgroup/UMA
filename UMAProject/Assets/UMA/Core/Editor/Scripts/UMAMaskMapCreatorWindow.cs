using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public sealed class UMAMaskMapCreatorWindow : EditorWindow
    {
        private enum TextureChannel
        {
            R,
            G,
            B,
            A
        }

        private struct ChannelMapping
        {
            public TextureChannel channel;
            public float constant;
            public bool useConstant;
            public bool invert;
        }

        private struct ChannelPreviewCache
        {
            public Texture2D sourceTexture;
            public TextureChannel sourceChannel;
            public bool sourceInvert;
            public Texture2D previewTexture;
        }

        private Texture2D metallicTexture;
        private Texture2D ambientOcclusionTexture;
        private Texture2D maskTexture;
        private Texture2D smoothnessTexture;

        private ChannelMapping redMapping;
        private ChannelMapping greenMapping;
        private ChannelMapping blueMapping;
        private ChannelMapping alphaMapping;

        private ChannelPreviewCache redPreviewCache;
        private ChannelPreviewCache greenPreviewCache;
        private ChannelPreviewCache bluePreviewCache;
        private ChannelPreviewCache alphaPreviewCache;

        private bool useDefaultSize = true;
        private int customWidth = 1024;
        private int customHeight = 1024;

        [MenuItem("UMA/Textures/Create MaskMap", priority = 125)]
        private static void OpenWindow()
        {
            UMAMaskMapCreatorWindow window = GetWindow<UMAMaskMapCreatorWindow>(true, "UMA MaskMap Creator");
            window.minSize = new Vector2(540f, 468f);
            window.maxSize = new Vector2(720f, 688f);
            window.Show();
        }

        private void OnEnable()
        {
            redMapping = new ChannelMapping { channel = TextureChannel.R, constant = 0f, useConstant = false, invert = false };
            greenMapping = new ChannelMapping { channel = TextureChannel.R, constant = 0f, useConstant = false, invert = false };
            blueMapping = new ChannelMapping { channel = TextureChannel.R, constant = 0f, useConstant = false, invert = false };
            alphaMapping = new ChannelMapping { channel = TextureChannel.R, constant = 0f, useConstant = false, invert = false };
        }

        private void OnDisable()
        {
            CleanupPreviewTextures();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Create RGBA MaskMap", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("R = Metallic, G = Ambient Occlusion, B = Mask, A = Smoothness", EditorStyles.miniLabel);

            EditorGUILayout.Space(8f);
            DrawFixedChannelRow("Metallic", "R", ref metallicTexture, ref redMapping, ref redPreviewCache);
            DrawFixedChannelRow("Ambient Occlusion", "G", ref ambientOcclusionTexture, ref greenMapping, ref greenPreviewCache);
            DrawFixedChannelRow("Mask", "B", ref maskTexture, ref blueMapping, ref bluePreviewCache);
            DrawFixedChannelRow("Smoothness", "A", ref smoothnessTexture, ref alphaMapping, ref alphaPreviewCache);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Output Size", EditorStyles.boldLabel);
            int defaultSize = CalculateDefaultSize();
            EditorGUILayout.LabelField("Default", defaultSize + " x " + defaultSize + " (largest source, or 1024)");

            useDefaultSize = EditorGUILayout.Toggle("Use Default Size", useDefaultSize);
            using (new EditorGUI.DisabledScope(useDefaultSize))
            {
                customWidth = Mathf.Max(1, EditorGUILayout.IntField("Width", customWidth));
                customHeight = Mathf.Max(1, EditorGUILayout.IntField("Height", customHeight));
            }

            EditorGUILayout.Space(12f);
            using (new EditorGUI.DisabledScope(!CanCreate()))
            {
                if (GUILayout.Button("Create MaskMap", GUILayout.Height(30f)))
                {
                    CreateMaskMap();
                }
            }

            if (!CanCreate())
            {
                EditorGUILayout.HelpBox("Assign at least one texture or enable constant value on one channel.", MessageType.Info);
            }
        }

        private void DrawFixedChannelRow(string label, string outputChannelLabel, ref Texture2D texture, ref ChannelMapping mapping, ref ChannelPreviewCache previewCache)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.BeginVertical(GUILayout.Width(220f));
            EditorGUILayout.LabelField(label + " (" + outputChannelLabel + ")", EditorStyles.boldLabel);
            Rect r = GUILayoutUtility.GetRect(64, 64, GUILayout.ExpandWidth(false));
            Rect previewRect = new Rect(r.x + 128, r.y, 64f, 64f);

            EditorGUI.BeginChangeCheck();
            Texture2D newTexture = (Texture2D)EditorGUI.ObjectField(r, texture, typeof(Texture2D), false);
            if (EditorGUI.EndChangeCheck())
            {
                texture = newTexture;
                InvalidatePreview(ref previewCache);
            }

            Texture2D previewTexture = EnsurePreviewTexture(texture, mapping.channel, mapping.invert, ref previewCache);
            if (previewTexture != null)
            {
                GUI.DrawTexture(previewRect, previewTexture, ScaleMode.ScaleToFit, false);
            }
            else
            {
                EditorGUI.DrawRect(previewRect, Color.black);
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            mapping.useConstant = EditorGUILayout.ToggleLeft("Use Constant Value", mapping.useConstant);

            using (new EditorGUI.DisabledScope(!mapping.useConstant))
            {
                mapping.constant = Mathf.Clamp01(EditorGUILayout.Slider("Constant", mapping.constant, 0f, 1f));
            }

            using (new EditorGUI.DisabledScope(mapping.useConstant))
            {
                EditorGUI.BeginChangeCheck();
                TextureChannel newChannel = (TextureChannel)EditorGUILayout.EnumPopup("Texture Channel", mapping.channel);
                if (EditorGUI.EndChangeCheck())
                {
                    mapping.channel = newChannel;
                    InvalidatePreview(ref previewCache);
                }

                EditorGUI.BeginChangeCheck();
                bool newInvert = EditorGUILayout.ToggleLeft("Invert", mapping.invert);
                if (EditorGUI.EndChangeCheck())
                {
                    mapping.invert = newInvert;
                    InvalidatePreview(ref previewCache);
                }

                if (texture == null)
                {
                    EditorGUILayout.HelpBox("Texture not assigned for this output channel.", MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox("Channel looks OK!", MessageType.Info);
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void CleanupPreviewTextures()
        {
            InvalidatePreview(ref redPreviewCache);
            InvalidatePreview(ref greenPreviewCache);
            InvalidatePreview(ref bluePreviewCache);
            InvalidatePreview(ref alphaPreviewCache);
        }

        private static Texture2D EnsurePreviewTexture(Texture2D sourceTexture, TextureChannel channel, bool invert, ref ChannelPreviewCache previewCache)
        {
            if (sourceTexture == null)
            {
                InvalidatePreview(ref previewCache);
                return null;
            }

            if (previewCache.previewTexture != null && previewCache.sourceTexture == sourceTexture && previewCache.sourceChannel == channel && previewCache.sourceInvert == invert)
            {
                return previewCache.previewTexture;
            }

            InvalidatePreview(ref previewCache);

            previewCache.previewTexture = BuildChannelPreviewTexture(sourceTexture, channel, invert);
            previewCache.sourceTexture = sourceTexture;
            previewCache.sourceChannel = channel;
            previewCache.sourceInvert = invert;
            return previewCache.previewTexture;
        }

        private static void InvalidatePreview(ref ChannelPreviewCache previewCache)
        {
            if (previewCache.previewTexture != null)
            {
                DestroyImmediate(previewCache.previewTexture);
            }

            previewCache.previewTexture = null;
            previewCache.sourceTexture = null;
            previewCache.sourceChannel = TextureChannel.R;
            previewCache.sourceInvert = false;
        }

        private static Texture2D BuildChannelPreviewTexture(Texture2D sourceTexture, TextureChannel channel, bool invert)
        {
            const int previewSize = 64;

            RenderTexture rt = RenderTexture.GetTemporary(previewSize, previewSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;

            try
            {
                Graphics.Blit(sourceTexture, rt);
                RenderTexture.active = rt;

                Texture2D previewTexture = new Texture2D(previewSize, previewSize, TextureFormat.RGBA32, false, true);
                previewTexture.ReadPixels(new Rect(0f, 0f, previewSize, previewSize), 0, 0, false);
                previewTexture.Apply(false, false);

                Color32[] pixels = previewTexture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte channelValue = ResolveChannelValue(channel, pixels[i]);
                    if (invert)
                    {
                        channelValue = (byte)(255 - channelValue);
                    }
                    pixels[i] = new Color32(channelValue, channelValue, channelValue, 255);
                }

                previewTexture.SetPixels32(pixels);
                previewTexture.Apply(false, false);
                return previewTexture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private bool CanCreate()
        {
            bool hasMetallicSource = metallicTexture != null || redMapping.useConstant;
            bool hasAoSource = ambientOcclusionTexture != null || greenMapping.useConstant;
            bool hasMaskSource = maskTexture != null || blueMapping.useConstant;
            bool hasSmoothnessSource = smoothnessTexture != null || alphaMapping.useConstant;
            return hasMetallicSource || hasAoSource || hasMaskSource || hasSmoothnessSource;
        }

        private int CalculateDefaultSize()
        {
            int size = 1024;
            if (metallicTexture != null)
            {
                size = Mathf.Max(size, metallicTexture.width, metallicTexture.height);
            }
            if (ambientOcclusionTexture != null)
            {
                size = Mathf.Max(size, ambientOcclusionTexture.width, ambientOcclusionTexture.height);
            }
            if (maskTexture != null)
            {
                size = Mathf.Max(size, maskTexture.width, maskTexture.height);
            }
            if (smoothnessTexture != null)
            {
                size = Mathf.Max(size, smoothnessTexture.width, smoothnessTexture.height);
            }
            return size;
        }

        private void CreateMaskMap()
        {
            int width = useDefaultSize ? CalculateDefaultSize() : customWidth;
            int height = useDefaultSize ? CalculateDefaultSize() : customHeight;

            Color32[] metallicPixels = ReadPixels(metallicTexture, width, height);
            Color32[] aoPixels = ReadPixels(ambientOcclusionTexture, width, height);
            Color32[] maskPixels = ReadPixels(maskTexture, width, height);
            Color32[] smoothnessPixels = ReadPixels(smoothnessTexture, width, height);

            Color32[] output = new Color32[width * height];
            for (int i = 0; i < output.Length; i++)
            {
                byte r = ResolveChannelValue(redMapping, metallicPixels, i);
                byte g = ResolveChannelValue(greenMapping, aoPixels, i);
                byte b = ResolveChannelValue(blueMapping, maskPixels, i);
                byte a = ResolveChannelValue(alphaMapping, smoothnessPixels, i);
                output[i] = new Color32(r, g, b, a);
            }

            Texture2D maskMap = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            maskMap.SetPixels32(output);
            maskMap.Apply(false, false);

            string path = EditorUtility.SaveFilePanelInProject("Save MaskMap", "MaskMap", "png", "Choose where to save the generated mask map.");
            if (string.IsNullOrEmpty(path))
            {
                DestroyImmediate(maskMap);
                return;
            }

            byte[] png = maskMap.EncodeToPNG();
            DestroyImmediate(maskMap);
            System.IO.File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        private static Color32[] ReadPixels(Texture2D source, int width, int height)
        {
            if (source == null)
            {
                Color32[] white = new Color32[width * height];
                for (int i = 0; i < white.Length; i++)
                {
                    white[i] = new Color32(255, 255, 255, 255);
                }
                return white;
            }

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            readable.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            readable.Apply(false, false);
            Color32[] pixels = readable.GetPixels32();

            DestroyImmediate(readable);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return pixels;
        }

        private byte ResolveChannelValue(ChannelMapping mapping, Color32[] sourcePixels, int pixelIndex)
        {
            byte value;
            if (mapping.useConstant)
            {
                value = FloatToByte(mapping.constant);
            }
            else
            {
                Color32 sample = sourcePixels[pixelIndex];
                value = ResolveChannelValue(mapping.channel, sample);
            }

            if (mapping.invert)
            {
                value = (byte)(255 - value);
            }

            return value;
        }

        private static byte ResolveChannelValue(TextureChannel channel, Color32 sample)
        {
            switch (channel)
            {
                case TextureChannel.R:
                    return sample.r;
                case TextureChannel.G:
                    return sample.g;
                case TextureChannel.B:
                    return sample.b;
                case TextureChannel.A:
                    return sample.a;
                default:
                    return 0;
            }
        }

        private static byte FloatToByte(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(value) * 255f), 0, 255);
        }
    }
}
