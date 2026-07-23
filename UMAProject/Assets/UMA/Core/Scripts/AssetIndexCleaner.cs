#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace UMA
{
    public partial class UMAAssetIndexer
    {
        public readonly struct TypeCleanupResult
        {
            public readonly int AddedTypesRemoved;
            public readonly int SerializedItemsRemoved;
            public readonly int IndexedTypeNamesRemoved;
            public readonly int TypeFolderEntriesRemoved;
            public readonly int SerializedItemsRemaining;
            public readonly int MainTypesRemaining;

            public TypeCleanupResult(
                int addedTypesRemoved,
                int serializedItemsRemoved,
                int indexedTypeNamesRemoved,
                int typeFolderEntriesRemoved,
                int serializedItemsRemaining,
                int mainTypesRemaining)
            {
                AddedTypesRemoved = addedTypesRemoved;
                SerializedItemsRemoved = serializedItemsRemoved;
                IndexedTypeNamesRemoved = indexedTypeNamesRemoved;
                TypeFolderEntriesRemoved = typeFolderEntriesRemoved;
                SerializedItemsRemaining = serializedItemsRemaining;
                MainTypesRemaining = mainTypesRemaining;
            }
        }

        /// <summary>
        /// Editor-only implementation kept on the partial indexer so it can reset
        /// every private runtime lookup without reflection.
        /// </summary>
        public TypeCleanupResult CleanupAddedTypes()
        {
            var mainTypes = new List<Type>(DefaultIndexedTypes.Length);
            var mainTypeSet = new HashSet<Type>();
            var mainTypeNames = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < DefaultIndexedTypes.Length; i++)
            {
                Type type = DefaultIndexedTypes[i];
                if (type != null && mainTypeSet.Add(type))
                {
                    mainTypes.Add(type);
                    mainTypeNames.Add(type.Name);
                }
            }

            var addedTypes = new HashSet<Type>();
            if (Types != null)
            {
                for (int i = 0; i < Types.Length; i++)
                {
                    Type type = Types[i];
                    if (type != null && !mainTypeSet.Contains(type))
                    {
                        addedTypes.Add(type);
                    }
                }
            }

            if (TypeToLookup != null)
            {
                foreach (Type type in TypeToLookup.Keys)
                {
                    if (type != null && !mainTypeSet.Contains(type))
                    {
                        addedTypes.Add(type);
                    }
                }
            }

            int serializedItemsRemoved = 0;
            if (SerializedItems == null)
            {
                SerializedItems = new List<AssetItem>();
            }
            else
            {
                for (int i = SerializedItems.Count - 1; i >= 0; i--)
                {
                    AssetItem item = SerializedItems[i];
                    Type itemType = GetCleanupItemType(item);
                    if (item == null || itemType == null || !mainTypeSet.Contains(itemType))
                    {
                        if (itemType != null && !mainTypeSet.Contains(itemType))
                        {
                            addedTypes.Add(itemType);
                        }

                        SerializedItems.RemoveAt(i);
                        serializedItemsRemoved++;
                    }
                }
            }

            int indexedTypeNamesRemoved = IndexedTypeNames != null ? IndexedTypeNames.Count : 0;
            if (IndexedTypeNames == null)
            {
                IndexedTypeNames = new List<string>();
            }
            else
            {
                IndexedTypeNames.Clear();
            }

            if (RemoveUnlabeledTypeNames == null)
            {
                RemoveUnlabeledTypeNames = new List<string>();
            }
            else
            {
                RemoveUnlabeledTypeNames.Clear();
            }

            int typeFolderEntriesRemoved = CleanupTypeFolderEntries(mainTypeNames);

            // Replace, rather than incrementally edit, every type structure. This
            // also clears stale keys that may no longer appear in IndexedTypeNames.
            Types = mainTypes.ToArray();
            TypeToLookup = new Dictionary<Type, Type>();
            for (int i = 0; i < mainTypes.Count; i++)
            {
                Type type = mainTypes[i];
                Type lookupType = type;
                if (type == typeof(AnimatorOverrideController) || type == typeof(AnimatorController))
                {
                    lookupType = typeof(RuntimeAnimatorController);
                }

                TypeToLookup[type] = lookupType;
            }

            TypeLookup = new Dictionary<Type, Dictionary<string, AssetItem>>();
            GuidTypes = new Dictionary<string, AssetItem>();
            raceRecipes.Clear();

            BuildStringTypes();
            CreateTypeFolderMapping();
            UpdateSerializedDictionaryItems();
            RebuildRaceRecipes();

            EditorUtility.SetDirty(this);
            ForceSave();

            return new TypeCleanupResult(
                addedTypes.Count,
                serializedItemsRemoved,
                indexedTypeNamesRemoved,
                typeFolderEntriesRemoved,
                SerializedItems.Count,
                mainTypes.Count);
        }

        private static Type GetCleanupItemType(AssetItem item)
        {
            if (item == null)
            {
                return null;
            }

            // CompressNulls calls AssetItem.Update(), which adopts the concrete
            // serialized object's type. Inspect it now so a subclass cannot survive
            // the filter and become an orphan while dictionaries are rebuilt.
            if (item._SerializedItem != null)
            {
                return item._SerializedItem.GetType();
            }

            return item._Type;
        }

        private int CleanupTypeFolderEntries(HashSet<string> mainTypeNames)
        {
            if (typeFolders == null)
            {
                typeFolders = new List<TypeFolders>();
                return 0;
            }

            int removed = 0;
            var retainedNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = typeFolders.Count - 1; i >= 0; i--)
            {
                TypeFolders entry = typeFolders[i];
                if (entry == null || string.IsNullOrEmpty(entry.typeName) ||
                    !mainTypeNames.Contains(entry.typeName) || !retainedNames.Add(entry.typeName))
                {
                    typeFolders.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
        }
    }
}
#endif
