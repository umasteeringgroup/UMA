#if UNITY_EDITOR
using System;

namespace UMA
{
    /// <summary>
    /// Allows optional editor integrations to provide Addressables metadata without making
    /// the runtime UMA_Core assembly depend on an editor-only assembly.
    /// </summary>
    public static class UMAAddressableEditorBridge
    {
        public readonly struct Info
        {
            private readonly bool exists;
            public readonly string AddressableAddress;
            public readonly string AddressableGroup;
            public readonly string AddressableLabels;

            public Info(string addressableAddress, string addressableGroup, string addressableLabels)
            {
                exists = true;
                AddressableAddress = addressableAddress;
                AddressableGroup = addressableGroup;
                AddressableLabels = addressableLabels;
            }

            public bool IsValid => exists;
        }

        public static Func<string, Info> AddressableInfoResolver { private get; set; }

        public static bool TryGetAddressableInfo(string guid, out Info info)
        {
            info = default;
            if (string.IsNullOrEmpty(guid) || AddressableInfoResolver == null)
            {
                return false;
            }

            info = AddressableInfoResolver(guid);
            return info.IsValid;
        }
    }
}
#endif
