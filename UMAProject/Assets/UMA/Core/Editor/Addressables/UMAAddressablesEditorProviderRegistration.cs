#if UMA_ADDRESSABLES
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace UMA
{
    [InitializeOnLoad]
    internal static class UMAAddressablesEditorProviderRegistration
    {
        static UMAAddressablesEditorProviderRegistration()
        {
            UMAAddressablesRuntimeProvider.Register();
            UMAAddressablesEditorBridge.Current = new UMAAddressablesEditorBridge.Provider
            {
                generate = (plugin, index) =>
                    UMAAddressablesSupport.Instance.GenerateAddressables(plugin, index),
                generateDefault = () =>
                    UMAAddressablesSupport.Instance.GenerateAddressables(),
                cleanup = (onlyEmpty, removeFlags) =>
                    UMAAddressablesSupport.Instance.CleanupAddressables(onlyEmpty, removeFlags),
                cleanupOrphans = (type, forceSave, message) =>
                    UMAAddressablesSupport.Instance.CleanupOrphans(type, forceSave, message),
                getOrphans = type =>
                    UMAAddressablesSupport.Instance.GetOrphans(type),
                addRecipeGroup = recipe =>
                    UMAAddressablesSupport.Instance.AddRecipeGroup(recipe),
                createSingleGroupGenerator = clearMaterials =>
                    new SingleGroupGenerator { ClearMaterials = clearMaterials },
                buildPlayerContent = () =>
                {
                    AddressableAssetSettings.BuildPlayerContent(out var result);
                    return result.Error;
                }
            };
        }
    }
}
#endif
