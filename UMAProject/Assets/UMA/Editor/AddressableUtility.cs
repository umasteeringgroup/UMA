#if UMA_ADDRESSABLES
#if !UMA_NOASMDEF
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
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

    public class AddressableEntryAndInfo
    {
        public AddressableAssetEntry Entry;
        public AddressableInfo Info;
        public AddressableEntryAndInfo(AddressableAssetEntry entry, AddressableInfo info)
        {
            Entry = entry;
            Info = info;
        }
    }

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
                {
                    return true;
            }
            }

            return false;
        }

        public static AddressableAssetEntry GetAddressableAssetEntry(string AssetGUID)
        {
            if (AddressableSettings == null)
            {
                return null;
            }

            foreach (var group in AddressableSettings.groups)
            {
                if (group.HasSchema<PlayerDataGroupSchema>())
                    continue;

                var entry = group.GetAssetEntry(AssetGUID);
                if (entry != null)
                {
                    return entry;
                }
            }

            // Not found
            return null;
        }
        public static AddressableAssetEntry GetAddressableAssetEntry(string AssetGUID, out AddressableAssetGroup Group)
        {
            Group = null;
            if (AddressableSettings == null)
            {
                return null;
            }

            foreach (var group in AddressableSettings.groups)
            {
                if (group.HasSchema<PlayerDataGroupSchema>())
                    continue;

                var entry = group.GetAssetEntry(AssetGUID);
                if (entry != null)
                {
                    Group = group;
                    return entry;
                }
            }

            // Not found
            return null;
        }

        public static string GetAddressableLabels(AddressableAssetEntry ae)
        {
            string retval = "";

            if (ae.labels == null)
            {
                return retval;
            }
            foreach (string s in ae.labels)
            {
                retval += s + ";";
            }
            return retval;
        }

        public static AddressableInfo GetAddressableInfo(string guid)
        {
            AddressableAssetEntry ae = GetAddressableAssetEntry(guid);
            if (ae != null)
            {
                string name = "";
                if (ae.parentGroup != null)
                {
                    name = ae.parentGroup.Name;
                }
                else
                {
                    name = "No Group";
                }
                return new AddressableInfo(ae.address, name, GetAddressableLabels(ae));
            }
            return null;
        }
    }
}
#endif
#endif
