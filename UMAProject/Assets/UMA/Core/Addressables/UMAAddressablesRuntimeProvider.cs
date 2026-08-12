#if UMA_ADDRESSABLES
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace UMA
{
    public static class UMAAddressablesRuntimeProvider
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register()
        {
            UMAAddressablesRuntimeBridge.Current = new UMAAddressablesRuntimeBridge.Provider
            {
                loadAssets = LoadAssets,
                release = Release
            };
        }

        private static UMAAddressableOperation LoadAssets(IReadOnlyList<string> keys)
        {
            AsyncOperationHandle<IList<UnityEngine.Object>> handle =
                Addressables.LoadAssetsAsync<UnityEngine.Object>(
                    keys, null, Addressables.MergeMode.Union, true);

            UMAAddressableOperation operation = null;
            operation = new UMAAddressableOperation(
                handle,
                () => handle.IsValid(),
                () => handle.IsDone,
                () => handle.PercentComplete,
                () => ToUmaStatus(handle.Status),
                () => handle.IsValid() ? handle.Result : null,
                () => handle.IsValid() ? handle.OperationException : null);
            handle.Completed += _ => operation.RaiseCompleted();
            return operation;
        }

        private static void Release(UMAAddressableOperation operation)
        {
            if (operation?.NativeHandle is AsyncOperationHandle<IList<UnityEngine.Object>> handle &&
                handle.IsValid())
                Addressables.Release(handle);
        }

        private static UMAAddressableOperationStatus ToUmaStatus(AsyncOperationStatus status)
        {
            switch (status)
            {
                case AsyncOperationStatus.Succeeded:
                    return UMAAddressableOperationStatus.Succeeded;
                case AsyncOperationStatus.Failed:
                    return UMAAddressableOperationStatus.Failed;
                default:
                    return UMAAddressableOperationStatus.None;
            }
        }
    }
}
#endif
