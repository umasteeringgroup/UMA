using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Addressables-neutral facade used by UMA's general editor assembly. The optional
    /// Addressables assembly registers a provider when UMA_ADDRESSABLES is enabled.
    /// </summary>
    public static class UMAAddressablesEditorBridge
    {
        public sealed class Provider
        {
            public Action<IUMAAddressablePlugin, UMAAssetIndexer> generate;
            public Action generateDefault;
            public Action<bool, bool> cleanup;
            public Func<Type, bool, string, int> cleanupOrphans;
            public Func<Type, List<AssetItem>> getOrphans;
            public Func<UMATextRecipe, bool> addRecipeGroup;
            public Func<bool, IUMAAddressablePlugin> createSingleGroupGenerator;
        }

        public static Provider Current { private get; set; }
        public static bool IsAvailable => Current != null;

        public static void GenerateAddressables(IUMAAddressablePlugin plugin,
            UMAAssetIndexer index = null)
        {
            if (Current?.generate != null) Current.generate(plugin, index);
            else ReportUnavailable();
        }

        public static void GenerateAddressables()
        {
            if (Current?.generateDefault != null) Current.generateDefault();
            else ReportUnavailable();
        }

        public static void CleanupAddressables(bool onlyEmpty = false, bool removeFlags = false)
        {
            if (Current?.cleanup != null) Current.cleanup(onlyEmpty, removeFlags);
            else ReportUnavailable();
        }

        public static int CleanupOrphans(Type type, bool forceSave = true, string message = "")
        {
            if (Current?.cleanupOrphans != null)
                return Current.cleanupOrphans(type, forceSave, message);
            ReportUnavailable();
            return 0;
        }

        public static List<AssetItem> GetOrphans(Type type)
        {
            if (Current?.getOrphans != null) return Current.getOrphans(type);
            ReportUnavailable();
            return new List<AssetItem>();
        }

        public static bool AddRecipeGroup(UMATextRecipe recipe)
        {
            if (Current?.addRecipeGroup != null) return Current.addRecipeGroup(recipe);
            ReportUnavailable();
            return false;
        }

        public static IUMAAddressablePlugin CreateSingleGroupGenerator(bool clearMaterials)
        {
            if (Current?.createSingleGroupGenerator != null)
                return Current.createSingleGroupGenerator(clearMaterials);
            ReportUnavailable();
            return null;
        }

        private static void ReportUnavailable()
        {
            Debug.LogError("UMA Addressables support is unavailable. Install Addressables and enable UMA_ADDRESSABLES.");
        }
    }
}
