using System.Collections;
using System.Collections.Generic;
#if UNITY_EDITOR
#if UMA_ADDRESSABLES
#if UMA_NOASMDEF
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.VersionControl;
using UnityEngine;

namespace UMA
{
    public class AddressableInfo
    {
        public string AddressableAddress;
        public string AddressableGroup;
        public string AddressableLabels;
        public AddressableInfo(string addressableAddress, string addressableGroup, string addressableLabels)
        {
            AddressableAddress = addressableAddress;
            AddressableGroup = addressableGroup;
            AddressableLabels = addressableLabels;
        }
    };

    public class AddressableUtility
    {
        private static readonly AddressableUtility addressableUtility = new AddressableUtility();
        private static AddressableAssetSettings _AddressableSettings;

        public static AddressableAssetSettings AddressableSettings
        {
            get
            {
                if (_AddressableSettings == null)
                {
                    string[] Settings = AssetDatabase.FindAssets("AddressableAssetSettings");
                    string path = AssetDatabase.GUIDToAssetPath(Settings[0]);
                    _AddressableSettings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(path);
                }
                return _AddressableSettings;
            }
        }

        public static bool DoesAddressExist(string label)
        {
            List<AddressableAssetEntry> allEntries = new List<AddressableAssetEntry>();
            AddressableUtility.AddressableSettings.GetAllAssets(allEntries, false);

            foreach (AddressableAssetEntry entry in allEntries)
            {
                if (entry.labels.Contains(label))
                    return true;
            }

            return false;
        }

        public static AddressableAssetEntry GetAddressableAssetEntry(string assetPath, out AddressableAssetGroup assetgroup)
        {
            assetgroup = null;

            if (AddressableSettings == null)
            {
                return null;
            }

            foreach (var group in AddressableSettings.groups)
            {
                if (group.HasSchema<PlayerDataGroupSchema>())
                    continue;

                foreach (AddressableAssetEntry e in group.entries)
                {
                    if (e.AssetPath == assetPath)
                    {
                        assetgroup = group;
                        return e;
                    }
                }
            }
            // Not found
            return null;
        }

        public static AddressableAssetEntry GetAddressableAssetEntry(string AssetPath)
        {
            if (AddressableSettings == null)
            {
                return null;
            }

            foreach (var group in AddressableSettings.groups)
            {
                if (group.HasSchema<PlayerDataGroupSchema>())
                    continue;

                foreach (AddressableAssetEntry e in group.entries)
                {
                    if (e.AssetPath == AssetPath)
                    {
                        return e;
                    }
                }
            }

            // Not found
            return null;
        }

        public static string GetAddressableLabels(AddressableAssetEntry ae)
        {
            string retval = "";

            foreach (string s in ae.labels)
            {
                retval += s + ";";
            }
            return retval;
        }

        public static AddressableInfo GetAddressableInfo(string assetPath)
        {
            AddressableAssetEntry ae = GetAddressableAssetEntry(assetPath);
            if (ae != null)
            {
                return new AddressableInfo(ae.address, ae.parentGroup.Name, GetAddressableLabels(ae));
            }
            return null;
        }
    }
}
#endif
#endif
#endif
