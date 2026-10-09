using System;
using System.IO;
using UnityEditor;

namespace UMA.TexturePaint.Editor
{
    public static class TexturePaintAssets
    {
        // TexturePaintDocument.cs: retain this GUID when relocating or exporting the plugin.
        private const string AnchorGuid = "14803d4e3bfa4b0d87e8ad6d50d2e17f";
        public static string Root
        {
            get
            {
                string anchor = AssetDatabase.GUIDToAssetPath(AnchorGuid);
                if (string.IsNullOrEmpty(anchor)) throw new InvalidOperationException("Overlay Painter installation anchor is missing.");
                return Path.GetDirectoryName(Path.GetDirectoryName(anchor)).Replace('\\', '/');
            }
        }

        public static string ResolveInstallAssetPath(string relative)
        {
            // Accept historical UMA-relative paths so integrations can migrate without changing asset names.
            if (relative == "OverlayPainter") return Root;
            if (relative.StartsWith("OverlayPainter/", StringComparison.Ordinal)) relative = relative.Substring(15);
            if (Path.IsPathRooted(relative) || Array.Exists(relative.Split('/', '\\'), p => p == ".."))
                throw new ArgumentException("Expected a path within Overlay Painter.", nameof(relative));
            return Root + "/" + relative;
        }
    }
}
