using System.IO;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    public static class TexturePaintVDMImport
    {
        [MenuItem("Assets/Create/UMA/Overlay Painter/Import VDM EXR")]
        public static void Import()
        {
            string input = EditorUtility.OpenFilePanel("Import signed VDM EXR", "", "exr");
            if (string.IsNullOrEmpty(input)) return;
            string output = EditorUtility.SaveFilePanelInProject("Save VDM Brush",
                Path.GetFileNameWithoutExtension(input) + " VDM", "asset", "Create an editable VDM brush and a dedicated EXR copy.");
            if (string.IsNullOrEmpty(output)) return;
            try { Selection.activeObject = ImportCopy(input, output); }
            catch (System.Exception error) { Debug.LogException(error); }
        }

        public static TexturePaintVDMBrush ImportCopy(string input, string assetPath)
        {
            if (!input.EndsWith(".exr", System.StringComparison.OrdinalIgnoreCase))
                throw new System.ArgumentException("Choose an EXR containing signed displacement data.");
            if (!assetPath.StartsWith("Assets/", System.StringComparison.Ordinal) || !assetPath.EndsWith(".asset"))
                throw new System.ArgumentException("Save the brush under Assets with an .asset extension.");
            assetPath = AssetDatabase.GenerateUniqueAssetPath(assetPath);
            string copyPath = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(assetPath, ".exr"));
            File.Copy(input, copyPath, false);
            AssetDatabase.ImportAsset(copyPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(copyPath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            var platform = importer.GetDefaultPlatformTextureSettings();
            platform.format = TextureImporterFormat.RGBAFloat;
            platform.maxTextureSize = 8192;
            platform.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(platform);
            importer.SaveAndReimport();
            var brush = ScriptableObject.CreateInstance<TexturePaintVDMBrush>();
            brush.vectorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(copyPath);
            brush.sourceGuid = AssetDatabase.AssetPathToGUID(copyPath);
            brush.sourceRevision = TexturePaintVDMSource.Revision(brush.vectorTexture);
            AssetDatabase.CreateAsset(brush, assetPath);
            Undo.RegisterCreatedObjectUndo(brush, "Import VDM Brush");
            AssetDatabase.SaveAssetIfDirty(brush);
            return brush;
        }
    }

    public sealed partial class TexturePaintStageWindow
    {
        private static bool SupportsVDM(TexturePaintLayer layer, TexturePaintChannel channel) =>
            channel == TexturePaintChannel.NormalControl && layer != null &&
            (layer.kind == TexturePaintLayerKind.Projection && layer.projectionSettings?.garment?.enabled != true ||
             layer.IsSplineLayer && layer.splineSettings?.pathGenerator?.enabled != true &&
             layer.splineSettings?.garment?.enabled != true && layer.splineSettings?.hemSeam?.enabled != true);

        private static void DrawVDMSource(TexturePaintChannelSourceSettings source, TexturePaintLayer layer)
        {
            source.multiplier = Color.white; source.additive = Color.clear;
            source.invert = false; source.blur = 0;
            source.vdm ??= new TexturePaintVDMApplicationSettings { enabled = true };
            TexturePaintVDMApplicationSettings settings = source.vdm;
            var brush = (TexturePaintVDMBrush)EditorGUILayout.ObjectField("VDM Brush", settings.brush,
                typeof(TexturePaintVDMBrush), false);
            if (brush != settings.brush)
                source.vdm = settings = brush != null ? brush.CreateApplication() : new TexturePaintVDMApplicationSettings { enabled = true };
            settings.amplitude = EditorGUILayout.FloatField(new GUIContent("Displacement Amplitude",
                "Signed vector amplitude retained for future geometry output."), settings.amplitude);
            settings.reliefEncodingScale = EditorGUILayout.FloatField(new GUIContent("Relief Gain",
                "Converts signed height to Normal Control units around 0.5. Does not change future geometry amplitude."), settings.reliefEncodingScale);
            settings.coverage = (TexturePaintVDMCoverage)EditorGUILayout.EnumPopup("Coverage", settings.coverage);
            if (settings.coverage == TexturePaintVDMCoverage.MaskRed)
                settings.coverageMask = (Texture2D)EditorGUILayout.ObjectField("Mask (linear red)", settings.coverageMask, typeof(Texture2D), false);
            if (GUILayout.Button("Refresh Source Snapshot") && settings.brush != null)
            {
                var refreshed = settings.brush.CreateApplication();
                refreshed.amplitude = settings.amplitude;
                refreshed.reliefEncodingScale = settings.reliefEncodingScale;
                refreshed.coverage = settings.coverage;
                refreshed.coverageMask = settings.coverageMask;
                source.vdm = settings = refreshed;
                GUI.changed = true;
            }
            if (!TexturePaintVDMSource.Validate(settings, out string error))
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            else
            {
                Texture2D preview = TexturePaintVDMSource.Resolve(settings);
                if (preview != null)
                {
                    Rect rect = GUILayoutUtility.GetRect(80, 110, GUILayout.ExpandWidth(true));
                    EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
                }
                else EditorGUILayout.HelpBox(TexturePaintVDMSource.LastError ?? "VDM preview is unavailable.", MessageType.Warning);
            }
            if (layer.IsSplineLayer && layer.splineSettings != null &&
                layer.splineSettings.pathMode != TexturePaintPathMode.Stamps && layer.splineSettings.pathMode != TexturePaintPathMode.Ribbon)
                EditorGUILayout.HelpBox("VDM paths require Stamps or Ribbon mode. Existing pixels are preserved until you select a supported mode.", MessageType.Warning);
            EditorGUILayout.HelpBox("VDM Relief writes height to Normal Control. Use Height Strength below for shading. Geometry and silhouette stay unchanged. Ribbon image endcaps are ignored for this channel.", MessageType.None);
        }
    }

    [InitializeOnLoad]
    internal static class TexturePaintVDMCacheLifetime
    {
        static TexturePaintVDMCacheLifetime()
        {
            AssemblyReloadEvents.beforeAssemblyReload += TexturePaintVDMSource.ClearCache;
            EditorApplication.quitting += TexturePaintVDMSource.ClearCache;
        }
    }
}
