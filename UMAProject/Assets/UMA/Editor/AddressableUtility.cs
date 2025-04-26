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

        public static void ClearAddressableEntries()
        {
            if (_addressableEntries == null)
            {
                return;
            }
            _addressableEntries.Clear();
            _addressableEntries = null;
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


        public static void RebuildAddressableEntries()
        {
            _addressableEntries = new Dictionary<string, AddressableEntryAndInfo>();

            if (AddressableUtility.AddressableSettings == null)
                return;

            foreach (var group in AddressableUtility.AddressableSettings.groups)
            {
                if (group == null) continue;

                foreach (var entry in group.entries)
                {
                    if (!_addressableEntries.ContainsKey(entry.AssetPath))
                    {
                        AddEntry(entry.AssetPath, entry);
                    }
                }
            }
        }

        private static Dictionary<string, AddressableEntryAndInfo> _addressableEntries = new Dictionary<string, AddressableEntryAndInfo>();

        public static void AddEntry(string assetPath, AddressableAssetEntry entry)
        {
            ValidateEntryAndInfo();
            if (!_addressableEntries.ContainsKey(assetPath))
            {
                _addressableEntries.Add(assetPath, new AddressableEntryAndInfo(entry, new AddressableInfo(entry.address, entry.parentGroup.Name, GetAddressableLabels(entry))));
            }
        }

        public static AddressableAssetEntry GetAddressableAssetEntry(string assetPath)
        {
            ValidateEntryAndInfo();
            if (_addressableEntries.ContainsKey(assetPath))
            {
                return _addressableEntries[assetPath].Entry;
            }
            AddressableAssetEntry entry = internalGetAddressableAssetEntry(assetPath);
            if (entry != null)
            {
                AddEntry(assetPath, entry);
            }
            return entry;
        }

        private static void ValidateEntryAndInfo()
        {
            if (_addressableEntries == null)
            {
                RebuildAddressableEntries();
            }
        }

        public static AddressableAssetEntry internalGetAddressableAssetEntry(string AssetPath)
        {
            if (AddressableSettings == null)
            {
                return null;
            }

            foreach (var group in AddressableSettings.groups)
            {
                if (group == null)
                {
                    continue;
                }


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

        public static AddressableInfo GetAddressableInfo(string assetPath)
        {
            AddressableAssetEntry ae = GetAddressableAssetEntry(assetPath);
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
