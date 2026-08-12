#if UMA_FBX_EXPORT
using UMA.Editors;
using UnityEditor;
using UnityEditor.Formats.Fbx.Exporter;

namespace UMA
{
    [InitializeOnLoad]
    internal static class UMAFbxExporterProvider
    {
        static UMAFbxExporterProvider()
        {
            UMAFbxExporterBridge.Current = new UMAFbxExporterBridge.Provider
            {
                exportObject = ExportObject
            };
        }

        private static void ExportObject(string fullPath, UnityEngine.GameObject target)
        {
            var options = new ExportModelOptions
            {
                ExportFormat = ExportFormat.Binary,
                ModelAnimIncludeOption = Include.Model,
                ObjectPosition = ObjectPosition.Reset,
                UseMayaCompatibleNames = false,
                ExportUnrendered = true
            };
            ModelExporter.ExportObject(fullPath, target, options);
        }
    }
}
#endif
