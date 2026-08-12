using System;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Dependency-neutral facade for the optional Unity FBX Exporter package.
    /// </summary>
    public static class UMAFbxExporterBridge
    {
        public sealed class Provider
        {
            public Action<string, GameObject> exportObject;
        }

        public static Provider Current { private get; set; }
        public static bool IsAvailable => Current?.exportObject != null;

        public static bool ExportObject(string fullPath, GameObject target)
        {
            if (!IsAvailable)
            {
                Debug.LogError(
                    "UMA FBX export is unavailable. Install Unity FBX Exporter and enable UMA_FBX_EXPORT.");
                return false;
            }

            Current.exportObject(fullPath, target);
            return true;
        }
    }
}
