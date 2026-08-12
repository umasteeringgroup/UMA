
using UnityEngine;
using System.IO;
using System;
using System.Collections.Generic;
using UMA.CharacterSystem;

#if UMA_ADDRESSABLES
using AsyncOp = UMA.UMAAddressableOperation;
#endif
using PackSlot = UMA.UMAPackedRecipeBase.PackedSlotDataV3;
using SlotRecipes = System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UMA.UMATextRecipe>>;
using RaceRecipes = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<UMA.UMATextRecipe>>>;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
#endif

#if UNITY_EDITOR
using StackTrace = System.Diagnostics.StackTrace;
using StackFrame = System.Diagnostics.StackFrame;
#endif

using UnityEngine.SceneManagement;
using System.Text;
using System.Collections;
using System.Xml.Serialization;
using UnityEngine.Events;


namespace UMA
{
    [PreferBinarySerialization]
    public partial class UMAAssetIndexer : ScriptableObject /*, ISerializationCallbackReceiver */
    {
        const float DefaultLife = 5.0f;
        const string generatorName = "UMAGeneratorInternal";

        private string instanceKey = "<" + Guid.NewGuid().ToString() + ">";

        public UMALabelsEvent BeforeProcessingLabels = new UMALabelsEvent();

        [Serializable]
        public class TypeFolders
        {
            public string typeName;
            public string[] Folders;
        }

        public List<TypeFolders> typeFolders = new List<TypeFolders>();

        public Dictionary<string, List<string>> TypeFolderSearch = new Dictionary<string, List<string>>();

        private void CreateTypeFolderMapping()
        {
            TypeFolderSearch = new Dictionary<string, List<string>>();
            for (int i = 0; i < typeFolders.Count; i++)
            {
                var tf = typeFolders[i];
                List<string> flist = new();
                flist.AddRange(tf.Folders);
                TypeFolderSearch.Add(tf.typeName, flist);
            }
        }

        private static bool ContainsString(string[] source, string value)
        {
            if (source == null)
            {
                return false;
            }

            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (source[sourceIndex] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] AppendString(string[] source, string value)
        {
            if (source == null || source.Length == 0)
            {
                return new[] { value };
            }

            string[] result = new string[source.Length + 1];
            Array.Copy(source, result, source.Length);
            result[source.Length] = value;
            return result;
        }

        private static string[] RemoveString(string[] source, string value)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<string>();
            }

            int keptCount = 0;
            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (source[sourceIndex] != value)
                {
                    keptCount++;
                }
            }

            if (keptCount == source.Length)
            {
                return source;
            }

            string[] result = new string[keptCount];
            int resultIndex = 0;
            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (source[sourceIndex] != value)
                {
                    result[resultIndex++] = source[sourceIndex];
                }
            }

            return result;
        }

        private static string GetAssetTypeName(AssetItem item)
        {
            return item != null && item._Type != null ? item._Type.Name : "Unknown";
        }

        private static int CompareAssetItemNames(AssetItem left, AssetItem right)
        {
            string leftName = left != null ? left._Name ?? string.Empty : string.Empty;
            string rightName = right != null ? right._Name ?? string.Empty : string.Empty;
            return StringComparer.Ordinal.Compare(leftName, rightName);
        }

        private static List<KeyValuePair<string, List<AssetItem>>> GroupAssetItemsByType(List<AssetItem> items)
        {
            Dictionary<string, List<AssetItem>> groupedItems = new Dictionary<string, List<AssetItem>>(StringComparer.Ordinal);
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                AssetItem item = items[itemIndex];
                string typeName = GetAssetTypeName(item);
                if (!groupedItems.TryGetValue(typeName, out List<AssetItem> typeItems))
                {
                    typeItems = new List<AssetItem>();
                    groupedItems[typeName] = typeItems;
                }

                typeItems.Add(item);
            }

            List<KeyValuePair<string, List<AssetItem>>> result = new List<KeyValuePair<string, List<AssetItem>>>(groupedItems);
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

            for (int groupIndex = 0; groupIndex < result.Count; groupIndex++)
            {
                result[groupIndex].Value.Sort(CompareAssetItemNames);
            }

            return result;
        }

        private static void WriteGroupedAssetItemsReport(StringBuilder report, List<AssetItem> items)
        {
            List<KeyValuePair<string, List<AssetItem>>> groupedItems = GroupAssetItemsByType(items);
            for (int groupIndex = 0; groupIndex < groupedItems.Count; groupIndex++)
            {
                KeyValuePair<string, List<AssetItem>> typeGroup = groupedItems[groupIndex];
                report.AppendLine($"  {typeGroup.Key} ({typeGroup.Value.Count} items):");
                for (int itemIndex = 0; itemIndex < typeGroup.Value.Count; itemIndex++)
                {
                    AssetItem item = typeGroup.Value[itemIndex];
                    report.AppendLine($"    • {item._Name}");
                    if (!string.IsNullOrEmpty(item._Path))
                    {
                        report.AppendLine($"      Path: {item._Path}");
                    }
                    if (!string.IsNullOrEmpty(item._Guid))
                    {
                        report.AppendLine($"      GUID: {item._Guid}");
                    }
                }
                report.AppendLine();
            }
        }

        private static List<string> GetSortedAssetTypeNames(Dictionary<string, AssetItem> firstItems, Dictionary<string, AssetItem> secondItems)
        {
            HashSet<string> allTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssetItem item in firstItems.Values)
            {
                allTypes.Add(GetAssetTypeName(item));
            }

            foreach (AssetItem item in secondItems.Values)
            {
                allTypes.Add(GetAssetTypeName(item));
            }

            List<string> result = new List<string>(allTypes);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static int CountAssetItemsByType(Dictionary<string, AssetItem> items, string typeName)
        {
            int count = 0;
            foreach (AssetItem item in items.Values)
            {
                if (GetAssetTypeName(item) == typeName)
                {
                    count++;
                }
            }

            return count;
        }

        private static string ExtractMissingAssetTypeName(string assetDescription)
        {
            int typeStart = assetDescription.LastIndexOf("Type: ", StringComparison.Ordinal);
            if (typeStart < 0)
            {
                return "Unknown";
            }

            typeStart += 6;
            int typeEnd = assetDescription.LastIndexOf(")", StringComparison.Ordinal);
            return typeEnd > typeStart ? assetDescription.Substring(typeStart, typeEnd - typeStart) : "Unknown";
        }

        private static List<KeyValuePair<string, List<string>>> GroupMissingAssetsByType(List<string> missingAssets)
        {
            Dictionary<string, List<string>> groupedAssets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            for (int assetIndex = 0; assetIndex < missingAssets.Count; assetIndex++)
            {
                string assetDescription = missingAssets[assetIndex];
                string typeName = ExtractMissingAssetTypeName(assetDescription);
                if (!groupedAssets.TryGetValue(typeName, out List<string> typeAssets))
                {
                    typeAssets = new List<string>();
                    groupedAssets[typeName] = typeAssets;
                }

                typeAssets.Add(assetDescription);
            }

            List<KeyValuePair<string, List<string>>> result = new List<KeyValuePair<string, List<string>>>(groupedAssets);
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
            return result;
        }

        private static int CompareTypesByName(Type left, Type right)
        {
            string leftName = left != null ? left.Name : string.Empty;
            string rightName = right != null ? right.Name : string.Empty;
            return StringComparer.Ordinal.Compare(leftName, rightName);
        }

        private static bool ContainsType(Type[] source, Type value)
        {
            if (source == null)
            {
                return false;
            }

            for (int sourceIndex = 0; sourceIndex < source.Length; sourceIndex++)
            {
                if (source[sourceIndex] == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Type> GetSortedCommonTypes(HashSet<Type> firstTypes, HashSet<Type> secondTypes)
        {
            List<Type> commonTypes = new List<Type>();
            foreach (Type type in firstTypes)
            {
                if (secondTypes.Contains(type))
                {
                    commonTypes.Add(type);
                }
            }

            commonTypes.Sort(CompareTypesByName);
            return commonTypes;
        }

        private static List<Type> GetSortedUnionTypes(HashSet<Type> firstTypes, HashSet<Type> secondTypes)
        {
            HashSet<Type> allTypes = new HashSet<Type>(firstTypes);
            foreach (Type type in secondTypes)
            {
                allTypes.Add(type);
            }

            List<Type> result = new List<Type>(allTypes);
            result.Sort(CompareTypesByName);
            return result;
        }

        // Runtime cache only. Serializing this field can persist a reference to
        // the UMAGenerator component on a prefab asset. A prefab component is
        // non-null, but Unity never updates it because it is not in a scene.
        [NonSerialized]
        public UMAGenerator generator;
        [NonSerialized]
        private EntityId consolidatedGeneratorEntityId;

        public UMAGenerator Generator
        {
            get
            {
                if (!IsUsableSceneGenerator(generator))
                {
                    generator = null;
                    generator = FindUsableSceneGenerator();
                    if (!IsUsableSceneGenerator(generator))
                    {
                        generator = null;
                        CreateGenerator();
                    }
                }
                ConsolidateInternalGenerators();
                return generator;
            }
        }

        private static UMAGenerator FindUsableSceneGenerator()
        {
            UMAGenerator internalGenerator = null;
            UMAGenerator[] candidates = Resources.FindObjectsOfTypeAll<UMAGenerator>();
            for (int i = 0; i < candidates.Length; i++)
            {
                UMAGenerator candidate = candidates[i];
                if (!IsUsableSceneGenerator(candidate))
                {
                    continue;
                }

                // A generator explicitly placed by the user takes precedence
                // over UMA's transient fallback generator.
                if (!string.Equals(candidate.gameObject.name, generatorName, StringComparison.Ordinal))
                {
                    return candidate;
                }

                if (internalGenerator == null)
                {
                    internalGenerator = candidate;
                }
            }

            return internalGenerator;
        }

        private void ConsolidateInternalGenerators()
        {
            if (!IsUsableSceneGenerator(generator))
            {
                consolidatedGeneratorEntityId = EntityId.None;
                return;
            }

            EntityId entityId = generator.GetEntityId();
            if (consolidatedGeneratorEntityId == entityId)
            {
                return;
            }

            consolidatedGeneratorEntityId = entityId;
            UMAGenerator[] candidates = Resources.FindObjectsOfTypeAll<UMAGenerator>();
            for (int i = 0; i < candidates.Length; i++)
            {
                UMAGenerator candidate = candidates[i];
                if (candidate == generator || !IsUsableSceneGenerator(candidate) ||
                    !string.Equals(candidate.gameObject.name, generatorName, StringComparison.Ordinal))
                {
                    continue;
                }

#if UNITY_EDITOR
                if (!EditorApplication.isPlaying)
                {
                    GameObject.DestroyImmediate(candidate.gameObject);
                    continue;
                }
#endif
                GameObject.Destroy(candidate.gameObject);
            }
        }

        private static bool IsUsableSceneGenerator(UMAGenerator candidate)
        {
            if (candidate == null || candidate.gameObject == null)
            {
                return false;
            }

            Scene scene = candidate.gameObject.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        public void Awake()
        {
            instanceKey = Guid.NewGuid().ToString();
        }

        private void DebugLog(string msg)
        {
            // File.AppendAllText("d:\\indexerlog.txt", msg + "\n");
        }

#if UNITY_EDITOR
		private static class IndexerBuildTrace
		{
			private const string PrefEnabled = "UMA_INDEXER_TRACE_DUPLICATES";
			private const string PrefMaxStacks = "UMA_INDEXER_TRACE_MAX_STACKS";
			private const string PrefLogEvery = "UMA_INDEXER_TRACE_LOG_EVERY";
			private const string PrefLogToFile = "UMA_INDEXER_TRACE_LOG_TO_FILE";

			private static readonly Dictionary<string, int> CountsByKey = new Dictionary<string, int>(StringComparer.Ordinal);
			private static readonly Dictionary<string, Dictionary<string, int>> StacksByKey = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
			private static int _sessionId;
			private static int _prepareBuildDepth;
			private static int _addTypeDepth;
			private static long _events;
			private static string _logPath;

            private static List<KeyValuePair<string, int>> GetTopCounts(Dictionary<string, int> source, int maxEntries)
            {
                List<KeyValuePair<string, int>> orderedCounts = new List<KeyValuePair<string, int>>(source);
                orderedCounts.Sort((left, right) =>
                {
                    int countCompare = right.Value.CompareTo(left.Value);
                    return countCompare != 0 ? countCompare : StringComparer.Ordinal.Compare(left.Key, right.Key);
                });

                if (orderedCounts.Count > maxEntries)
                {
                    orderedCounts.RemoveRange(maxEntries, orderedCounts.Count - maxEntries);
                }

                return orderedCounts;
            }

			public static bool Enabled => EditorPrefs.GetBool(PrefEnabled, false);
			public static int MaxStacks => Mathf.Clamp(EditorPrefs.GetInt(PrefMaxStacks, 8), 1, 64);
			public static int LogEvery => Mathf.Clamp(EditorPrefs.GetInt(PrefLogEvery, 2500), 1, 1000000);
			public static bool LogToFile => EditorPrefs.GetBool(PrefLogToFile, true);

			public static void BeginSession(string reason)
			{
				_sessionId++;
				CountsByKey.Clear();
				StacksByKey.Clear();
				_events = 0;
				_logPath = Path.Combine(Application.dataPath, "..", "Logs", $"UMAIndexerTrace_{DateTime.Now:yyyyMMdd_HHmmss}_{_sessionId}.log");
				WriteShort($"[UMAAssetIndexer][Trace] Begin session {_sessionId}: {reason}. WritingTo={(LogToFile ? _logPath : "<console-only>")}");
			}

			public static void EndSession(string reason)
			{
				if (!Enabled)
				{
					return;
				}
				WriteShort($"[UMAAssetIndexer][Trace] End session {_sessionId}: {reason}. Events={_events}, UniqueKeys={CountsByKey.Count}");

				// Print the most duplicated keys + their top stacks
                foreach (var kvp in GetTopCounts(CountsByKey, 25))
				{
					if (kvp.Value <= 1) break;
					WriteDuplicate($"[UMAAssetIndexer][Trace] DuplicateKey '{kvp.Key}' count={kvp.Value}");
					if (StacksByKey.TryGetValue(kvp.Key, out var stacks) && stacks != null)
					{
                        foreach (var s in GetTopCounts(stacks, MaxStacks))
						{
							WriteDuplicate($"[UMAAssetIndexer][Trace]   stackHits={s.Value} stack='{s.Key}'");
						}
					}
				}
			}

			public static void EnterPrepareBuild()
			{
				_prepareBuildDepth++;
				if (_prepareBuildDepth > 1)
				{
					WriteShort($"[UMAAssetIndexer][Trace][WARN] Re-entrant PrepareBuild depth={_prepareBuildDepth}");
				}
			}

			public static void ExitPrepareBuild()
			{
				_prepareBuildDepth = Math.Max(0, _prepareBuildDepth - 1);
			}

			public static void EnterAddType(Type type)
			{
				_addTypeDepth++;
				if (_addTypeDepth > 1)
				{
					WriteShort($"[UMAAssetIndexer][Trace][WARN] Re-entrant AddType depth={_addTypeDepth} type={type?.Name}");
				}
			}

			public static void ExitAddType()
			{
				_addTypeDepth = Math.Max(0, _addTypeDepth - 1);
			}

			public static void RecordAdd(AssetItem ai)
			{
				if (!Enabled || ai == null)
				{
					return;
				}

				_events++;
				string typePart = ai._Type != null ? (ai._Type.FullName ?? ai._Type.Name) : "<nulltype>";
				string guidPart = !string.IsNullOrWhiteSpace(ai._Guid) ? ai._Guid : "";
				string namePart = !string.IsNullOrWhiteSpace(ai._Name) ? ai._Name : "<noname>";
				string pathPart = !string.IsNullOrWhiteSpace(ai._Path) ? ai._Path.Replace('\\', '/') : "";
				string key = !string.IsNullOrEmpty(guidPart)
					? $"{typePart}|guid:{guidPart}"
					: $"{typePart}|name:{namePart}|path:{pathPart}";

				if (!CountsByKey.TryGetValue(key, out var c))
				{
					c = 0;
				}
				c++;
				CountsByKey[key] = c;

				if (c > 1)
				{
					string stack = GetCompactStack();
					if (!StacksByKey.TryGetValue(key, out var stackCounts) || stackCounts == null)
					{
						stackCounts = new Dictionary<string, int>(StringComparer.Ordinal);
						StacksByKey[key] = stackCounts;
					}
					stackCounts.TryGetValue(stack, out var sc);
					stackCounts[stack] = sc + 1;
				}

				if ((_events % LogEvery) == 0)
				{
					UnityEngine.Debug.Log($"[UMAAssetIndexer][Trace] events={_events} uniqueKeys={CountsByKey.Count} prepareDepth={_prepareBuildDepth} addTypeDepth={_addTypeDepth}");
				}
			}

			private static void WriteShort(string line)
			{
				UnityEngine.Debug.Log(line);
				if (!LogToFile)
				{
					return;
				}
				try
				{
					string dir = Path.GetDirectoryName(_logPath);
					if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
					{
						Directory.CreateDirectory(dir);
					}
					File.AppendAllText(_logPath, line + Environment.NewLine);
				}
				catch
				{
					// ignore logging failures
				}
			}

			private static void WriteDuplicate(string line)
			{
				// Duplicates are special: include a compact stack on disk/console to identify the call site.
				string stack = GetCompactStack();
				string full = string.IsNullOrEmpty(stack) ? line : (line + " | at " + stack);
				WriteShort(full);
			}

			private static string GetCompactStack()
			{
				// Skip 0: this method, 1: RecordAdd, 2: AddAssetItem, 3+: callers
				var st = new StackTrace(3, true);
				var frames = st.GetFrames();
				if (frames == null || frames.Length == 0)
				{
					return "<no-stack>";
				}

				var sb = new StringBuilder(256);
				int max = Math.Min(frames.Length, 10);
				for (int i = 0; i < max; i++)
				{
					var m = frames[i].GetMethod();
					if (m == null) continue;
					var dt = m.DeclaringType;
					sb.Append(dt != null ? dt.FullName : "<type>");
					sb.Append('.');
					sb.Append(m.Name);
					if (i != max - 1) sb.Append(" <- ");
				}
				return sb.ToString();
			}
		}
#endif

#if UMA_ADDRESSABLES
        private class CachedOp
        {
            public AsyncOp Operation;
            public float OperationTime;
            public float Life; // life in seconds
            public string Info;
            public List<string> Keys;

            public CachedOp(AsyncOp op, string info, float OpLife = 0.0f)
            {
                if (OpLife == 0.0f)
                {
                    OpLife = DefaultLife;
                }

                Operation = op;
                OperationTime = Time.time;
                Life = OpLife;
                Info = info;
                Keys = null;
            }

            public CachedOp(AsyncOp op, List<string> keys, string info, float OpLife = 0.0f) : this(op, info, OpLife)
            {
                Keys = keys;
            }

            public bool Expired
            {
                get
                {
                    if (Time.time - OperationTime > Life)
                    {
                        return true;
                    }
                    return false;
                }
            }
        }
#endif
#if UMA_ADDRESSABLES
        public Dictionary<string, bool> Preloads = new Dictionary<string, bool>();
        private List<CachedOp> LoadedItems = new List<CachedOp>();
#endif

#if UMA_ADDRESSABLES
        private CachedOp FindCachedOp(AsyncOp op)
        {
            if (!op.IsValid())
            {
                return null;
            }

            for (int i = 0; i < LoadedItems.Count; i++)
            {
                var c = LoadedItems[i];
                if (c != null && c.Operation.Equals(op))
                {
                    return c;
                }
            }
            return null;
        }

        private static string SafeJoinKeys(List<string> keys)
        {
            if (keys == null || keys.Count == 0)
            {
                return "<none>";
            }
            return string.Join("; ", keys);
        }

        private void DumpAddressablesLoadDebug(AsyncOp op)
        {
            if (!op.IsValid())
            {
                return;
            }

            CachedOp cached = FindCachedOp(op);
            List<string> requestedKeys = cached != null ? cached.Keys : null;

            if (requestedKeys == null && cached != null && !string.IsNullOrEmpty(cached.Info))
            {
                var split = cached.Info.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                requestedKeys = new List<string>(split.Length);
                for (int i = 0; i < split.Length; i++)
                {
                    string k = split[i].Trim();
                    if (!string.IsNullOrEmpty(k)) requestedKeys.Add(k);
                }
            }

            Debug.Log($"[UMAAssetIndexer] Addressables load complete. Requested Keys: {SafeJoinKeys(requestedKeys)}");

            if (op.Result == null)
            {
                Debug.Log("[UMAAssetIndexer] Addressables load returned null result list.");
                return;
            }

            for (int i = 0; i < op.Result.Count; i++)
            {
                var o = op.Result[i];
                if (o == null)
                {
                    Debug.Log($"[UMAAssetIndexer] Loaded item[{i}]: <null>");
                    continue;
                }

                Debug.Log($"[UMAAssetIndexer] Loaded item[{i}]: '{o.name}' ({o.GetType().Name})");

                var item = GetAssetItemForObject(o);
                if (item == null)
                {
                    Debug.Log($"[UMAAssetIndexer]   Not currently in index (will be indexed by ProcessNewItem)." );
                    continue;
                }

                string itemLabels = null;
                try
                {
                    itemLabels = item.AddressableLabels;
                }
                catch
                {
                    itemLabels = null;
                }

                if (string.IsNullOrEmpty(itemLabels))
                {
                    Debug.Log("[UMAAssetIndexer]   AddressableLabels: <none>");
                }
                else
                {
                    Debug.Log($"[UMAAssetIndexer]   AddressableLabels: {itemLabels}");
                }

                if (requestedKeys != null && requestedKeys.Count > 0)
                {
                    var matched = new List<string>();
                    if (!string.IsNullOrEmpty(itemLabels))
                    {
                        for (int k = 0; k < requestedKeys.Count; k++)
                        {
                            var key = requestedKeys[k];
                            if (!string.IsNullOrEmpty(key) && itemLabels.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matched.Add(key);
                            }
                        }
                    }
                    Debug.Log($"[UMAAssetIndexer]   Included via keys: {SafeJoinKeys(matched)}");
                }
            }
        }
#endif

        RaceRecipes raceRecipes = new RaceRecipes();

        #region constants and static strings
        public static string SortOrder = "Name";
        public static string[] SortOrders = { "Name", "AssetName" };
        public static Dictionary<string, System.Type> TypeFromString = new Dictionary<string, System.Type>();
        public Dictionary<string, AssetItem> GuidTypes = new Dictionary<string, AssetItem>();
        #endregion
        #region Fields
        protected Dictionary<System.Type, System.Type> TypeToLookup = new Dictionary<System.Type, System.Type>()
        {
        {  (typeof(SlotDataAsset)),(typeof(SlotDataAsset)) },
        {   (typeof(OverlayDataAsset)),(typeof(OverlayDataAsset)) },
        {   (typeof(RaceData)),(typeof(RaceData)) },
        {   (typeof(UMATextRecipe)),(typeof(UMATextRecipe)) },
        {   (typeof(UMAWardrobeRecipe)),(typeof(UMAWardrobeRecipe)) },
        {   (typeof(UMAWardrobeCollection)),(typeof(UMAWardrobeCollection)) },
        {   (typeof(RuntimeAnimatorController)),(typeof(RuntimeAnimatorController)) },
        { (typeof(AnimatorOverrideController)),(typeof(RuntimeAnimatorController)) },
#if UNITY_EDITOR
        { (typeof(AnimatorController)),(typeof(RuntimeAnimatorController)) },
#endif
        {   typeof(TextAsset), typeof(TextAsset) },
        {   typeof(DynamicUMADnaAsset), typeof(DynamicUMADnaAsset) },
        {   typeof(UMAMaterial), typeof(UMAMaterial) },
        {   typeof(UMAColorScheme), typeof(UMAColorScheme) },
        {   typeof(MeshHideAsset), typeof(MeshHideAsset) }
        };


        // The names of the fully qualified types.
        public List<string> IndexedTypeNames = new List<string>();
        public List<string> RemoveUnlabeledTypeNames = new List<string>();
        // These list is used so Unity will serialize the data
        public List<AssetItem> SerializedItems = new List<AssetItem>();
        // This is really where we keep the data.
        private Dictionary<System.Type, Dictionary<string, AssetItem>> TypeLookup = new Dictionary<System.Type, Dictionary<string, AssetItem>>();
        // This list tracks the types for use in iterating through the dictionaries
        private System.Type[] Types =
        {
        (typeof(SlotDataAsset)),
        (typeof(OverlayDataAsset)),
        (typeof(RaceData)),
        (typeof(UMATextRecipe)),
        (typeof(UMAWardrobeRecipe)),
        (typeof(UMAWardrobeCollection)),
        (typeof(RuntimeAnimatorController)),
        (typeof(AnimatorOverrideController)),
        (typeof(MeshHideAsset)),
#if UNITY_EDITOR
        (typeof(AnimatorController)),
#endif
        (typeof(DynamicUMADnaAsset)),
        (typeof(TextAsset)),
        (typeof(UMAMaterial)),
        (typeof(UMAColorScheme))
    };

        private static readonly System.Type[] DefaultIndexedTypes =
        {
        (typeof(SlotDataAsset)),
        (typeof(OverlayDataAsset)),
        (typeof(RaceData)),
        (typeof(UMATextRecipe)),
        (typeof(UMAWardrobeRecipe)),
        (typeof(UMAWardrobeCollection)),
        (typeof(RuntimeAnimatorController)),
        (typeof(AnimatorOverrideController)),
        (typeof(MeshHideAsset)),
#if UNITY_EDITOR
        (typeof(AnimatorController)),
#endif
        (typeof(DynamicUMADnaAsset)),
        (typeof(TextAsset)),
        (typeof(UMAMaterial)),
        (typeof(UMAColorScheme))
    };


        #endregion
        #region Static Fields
        static UMAAssetIndexer theIndexer = null;

        private static UMAAssetIndexer LoadPreferredIndexer()
        {
#if UNITY_EDITOR
            UMAAssetIndexer projectIndexer =
                AssetDatabase.LoadAssetAtPath<UMAAssetIndexer>(
                    UMAPathUtility.ProjectIndexerPath);
            if (projectIndexer != null) return projectIndexer;

            UMAAssetIndexer installDefault =
                UMAPathUtility.LoadInstallAsset<UMAAssetIndexer>(
                    "InternalDataStore/InGame/Resources/AssetIndexer.asset");
            if (installDefault != null) return installDefault;
#endif
            UMAAssetIndexer indexer = Resources.Load("AssetIndexerProject") as UMAAssetIndexer;
            return indexer != null ? indexer : Resources.Load("AssetIndexer") as UMAAssetIndexer;
        }

#if UNITY_EDITOR
        private static UMAAssetIndexer EnsureProjectOwnedIndexer(UMAAssetIndexer indexer)
        {
            if (indexer == null || !UMAPathUtility.IsPackageInstallation) return indexer;

            UMAAssetIndexer projectIndexer = AssetDatabase.LoadAssetAtPath<UMAAssetIndexer>(
                UMAPathUtility.ProjectIndexerPath);
            if (projectIndexer != null) return projectIndexer;

            string sourcePath = AssetDatabase.GetAssetPath(indexer);
            if (!sourcePath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)) return indexer;

            projectIndexer = Instantiate(indexer);
            projectIndexer.name = "AssetIndexerProject";
            projectIndexer.generator = null;
            UMAPathUtility.EnsureAssetFolder(UMAPathUtility.ProjectResourcesRoot);
            AssetDatabase.CreateAsset(projectIndexer, UMAPathUtility.ProjectIndexerPath);
            EditorUtility.SetDirty(projectIndexer);
            AssetDatabase.SaveAssetIfDirty(projectIndexer);
            return projectIndexer;
        }
#endif

        public static UMAAssetIndexer bareInstance
        {
            get { return theIndexer; }
        }

        public UMAGenerator bareGenerator
        {
            get
            {
                return IsUsableSceneGenerator(generator)
                    ? generator
                    : null;
            }
        }

        #endregion

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void RuntimeInitializeOnLoad()
        {

            SortOrder = "Name";
            SortOrders = new string[] { "Name", "AssetName" };
            WasChecked = false;
            TypeFromString = new Dictionary<string, System.Type>();
            if (theIndexer != null)
            {
                theIndexer.generator = null;
            }
            theIndexer = null;
/*
            // This method is called after all assemblies are loaded, so we can initialize static data here if needed.
            // Currently, we don't have any static initialization logic, but this method is a good place to add it in the future.
            if (theIndexer == null)
            {
                theIndexer = Resources.Load("AssetIndexer") as UMAAssetIndexer;
                if (theIndexer != null)
                {
                    theIndexer.Initialize();
                }
            }
            else
            {
                theIndexer.ReinitializePrivateFields();
            }*/
        }

        public void ReinitializePrivateFields()
        {
            // Reinitialize the private fields of the indexer
            instanceKey = "<" + Guid.NewGuid().ToString() + ">";
            generator = null;
            raceRecipes.Clear();
            TypeLookup.Clear();
            SerializedItems.Clear();
            CreateGenerator();
            RestoreIndexedTypesFromNames();
            BuildStringTypes();
            CreateTypeFolderMapping();
            DoInitialDictionaryLoad();
            RebuildRaceRecipes();
        }

		private void RestoreIndexedTypesFromNames()
		{
			// `Types` and `TypeToLookup` are not serialized by Unity.
			// The persisted list is `IndexedTypeNames` (assembly-qualified names).
			// After a domain reload we must rebuild the runtime structures from that list.
			if (IndexedTypeNames == null || IndexedTypeNames.Count == 0)
			{
				return;
			}

			for (int i = 0; i < IndexedTypeNames.Count; i++)
			{
				string qn = IndexedTypeNames[i];
				if (string.IsNullOrEmpty(qn))
				{
					continue;
				}

				var t = Type.GetType(qn);
				if (t == null)
				{
					continue;
				}

				// Ensure `Types` contains it
                if (!ContainsType(Types, t))
				{
					var newTypes = new List<System.Type>(Types.Length + 1);
					newTypes.AddRange(Types);
					newTypes.Add(t);
					Types = newTypes.ToArray();
				}

				// Ensure `TypeToLookup` knows about it
				if (!TypeToLookup.ContainsKey(t))
				{
                    Debug.Log($"Restoring indexed type: {t.FullName}");
                    TypeToLookup.Add(t, t);
				}
			}
		}


        public static System.Diagnostics.Stopwatch StartTimer()
        {
#if TIMEINDEXER
            if(Debug.isDebugBuild)
                Debug.Log("Timer started at " + Time.realtimeSinceStartup + " Sec");
            System.Diagnostics.Stopwatch st = new System.Diagnostics.Stopwatch();
            st.Start();

            return st;
#else
            return null;
#endif
        }

        public static void StopTimer(System.Diagnostics.Stopwatch st, string Status)
        {
#if TIMEINDEXER
            st.Stop();
            if(Debug.isDebugBuild)
                Debug.Log(Status + " Timer Completed " + st.ElapsedMilliseconds + "ms");
            return;
#endif
        }

        public static void Unload()
        {
            if (theIndexer != null)
            {
                theIndexer = null;
            }
        }

        public static UMAAssetIndexer Instance
        {
            get
            {
                if (theIndexer == null)
                {
#if UNITY_EDITOR
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        return null;
                    }
                    //DebugSerializationStatic("Loading AssetIndexer from resources...");
#endif

                    //var st = StartTimer();
                    theIndexer = LoadPreferredIndexer();
#if UNITY_EDITOR
                    theIndexer = EnsureProjectOwnedIndexer(theIndexer);
#endif
                    if (theIndexer == null)
                    {
#if UNITY_EDITOR
                        //DebugSerializationStatic("AssetIndexer is NULL - ON LOAD!!! How can this happen?");
#endif
                        return null;
                    }

                    // The generator is a scene-only runtime cache. Explicitly
                    // clear it before initialization to recover from older
                    // AssetIndexer assets that serialized a prefab component.
                    theIndexer.generator = null;

#if UNITY_EDITOR
                    //DebugSerializationStatic("Rebulding Lookup Tables");
#endif
                    theIndexer.Initialize();

                    /*theIndexer.UpdateSerializedDictionaryItems();
					theIndexer.RebuildRaceRecipes();*/

#if UNITY_EDITOR
                    EditorSceneManager.sceneSaving += EditorSceneManager_sceneSaving;
                    EditorSceneManager.sceneSaved += EditorSceneManager_sceneSaved;
                    EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
                    ;
#endif
                }
                else
                {
#if UNITY_EDITOR
                    //DebugSerializationStatic("Instance is NOT NULL - returning existing instance.");
                    //if (!theIndexer.IsValid()) 
                    //{
                    //    theIndexer.HealIndex();
                    //}
#endif
                }
                return theIndexer;
            }
        }

#if UNITY_EDITOR

        private static void EditorApplication_playModeStateChanged(PlayModeStateChange obj)
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode &&
                 !EditorApplication.isPlaying)
            {
                RebuildUMAS(SceneManager.GetActiveScene());
            }
            if (obj == PlayModeStateChange.ExitingEditMode)
            {
                if (theIndexer != null)
                {
                    // Debug.Log("playmde. creating generator");
                    if (IsUsableSceneGenerator(theIndexer.generator))
                    {
                        //Debug.Log("Entered Edit Mode. Destroying generator");
                        GameObject.DestroyImmediate(theIndexer.generator.gameObject);
                    }
                    theIndexer.generator = null;
                }
            }
            if (obj == PlayModeStateChange.EnteredEditMode)
            {
                if (theIndexer != null)
                {
                    if (IsUsableSceneGenerator(theIndexer.generator))
                    {
                        //Debug.Log("Entered Edit Mode. Destroying generator");
                        GameObject.DestroyImmediate(theIndexer.generator.gameObject);
                    }
                    theIndexer.generator = null;
                }
                //Debug.Log("playmde. exiting playmode");
                //theIndexer.generator = null;
                //theIndexer.CreateGenerator();
            }
            UMAMeshData.CleanupGlobalBuffers();
        }


        [UnityEditor.Callbacks.DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                UMAMeshData.CleanupGlobalBuffers();
            }
        }
        public const string ConfigToggle_LeanMeanSceneFiles = "UMA_CLEANUP_GENERATED_DATA_ON_SAVE";

        public static bool LeanMeanSceneFiles()
        {
            return UMASettings.CleanRegenOnSave;
        }

        private static void EditorSceneManager_sceneSaved(UnityEngine.SceneManagement.Scene scene)
        {
            if (!LeanMeanSceneFiles())
            {
                return;
            }
            EditorApplication.delayCall += () =>
            {
                RebuildUMAS(scene);
            };
        }

        private static void EditorSceneManager_sceneSaving(UnityEngine.SceneManagement.Scene scene, string path)
        {
            if (!LeanMeanSceneFiles())
            {
                return;
            }

            CleanupUMAS(scene);
        }

        public static void RebuildAllUMAS()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene != null)
            {
                RebuildUMAS(scene);
            }
        }

        public static void RebuildUMAS(Scene scene)
        {
            if (!scene.isLoaded || !scene.IsValid())
            {
                return;
            }
            GameObject[] sceneObjs = scene.GetRootGameObjects();
            for (int i = 0; i < sceneObjs.Length; i++)
            {
                GameObject go = sceneObjs[i];
                DynamicCharacterAvatar[] dcas = go.GetComponentsInChildren<DynamicCharacterAvatar>(false);
                if (dcas.Length > 0)
                {
                    for (int i1 = 0; i1 < dcas.Length; i1++)
                    {
                        DynamicCharacterAvatar dca = dcas[i1];
                        if (dca.editorTimeGeneration)
                        {
                            dca.GenerateSingleUMA();
                        }
                    }
                }
            }
        }

        public static void CleanupUMAS(Scene scene)
        {
            // Cleanup any editor generated UMAS
            GameObject[] sceneObjs = scene.GetRootGameObjects();
            for (int i = 0; i < sceneObjs.Length; i++)
            {
                GameObject go = sceneObjs[i];
                DynamicCharacterAvatar[] dcas = go.GetComponentsInChildren<DynamicCharacterAvatar>(false);
                if (dcas.Length > 0)
                {
                    for (int i1 = 0; i1 < dcas.Length; i1++)
                    {
                        DynamicCharacterAvatar dca = dcas[i1];
                        // Free all the generated data so we don't junk up the scene file.
                        // it will be regenerated later.
                        dca.CleanupGeneratedData();
                    }
                }
            }
        }

        public struct IndexBackup
        {
            public DateTime BackupTime;
            public AssetItem[] Items;
        }

        public string Backup()
        {
            try
            {
                RepairAndCleanup();

                IndexBackup backup = new IndexBackup();
                backup.BackupTime = DateTime.Now;
                backup.Items = SerializedItems.ToArray();

                return JsonUtility.ToJson(backup);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return "";
            }
        }

        public bool Restore(string s, bool quiet = false)
        {
            try
            {
                IndexBackup restore = JsonUtility.FromJson<IndexBackup>(s);
                SerializedItems.Clear();
                SerializedItems.AddRange(restore.Items);
                if (!quiet)
                {
                    EditorUtility.DisplayProgressBar("Restore", "Restoring index", 0.33f);
                }

                UpdateSerializedDictionaryItems();
                if (!quiet)
                {
                    EditorUtility.DisplayProgressBar("Restore", "Restoring index", 0.66f);
                }

                RepairAndCleanup();
                if (!quiet)
                {
                    EditorUtility.DisplayProgressBar("Restore", "Restoring index", 1.0f);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }
#endif

        public void Initialize()
        {
            // reset the public variables


            CreateGenerator();
			RestoreIndexedTypesFromNames();
            BuildStringTypes();
            CreateTypeFolderMapping();
            DoInitialDictionaryLoad();
            RebuildRaceRecipes();
            // UpdateSerializedDictionaryItems();
            //heIndexer.RebuildRaceRecipes();*/

        }

        public void DoInitialRecipeLoad() {
            // Load the serialized items into the raceRecipes dictionary
            raceRecipes.Clear();

        }



        public void DoInitialDictionaryLoad() {
            // Load the serialized items into the TypeLookup dictionary
            TypeLookup.Clear();
            foreach (var type in Types)
            {
                TypeLookup[type] = new Dictionary<string, AssetItem>();
            }

            foreach (var item in SerializedItems)
            {
                if (item != null && item._Type != null)
                {
                    if (!TypeLookup.ContainsKey(item._Type))
                    {
#if UNITY_EDITOR
                        Debug.Log("TypeLookup missing type " + item._Type + " Adding it.");
#endif
                        AddType(item._Type);                    
                        TypeLookup[item._Type] = new Dictionary<string, AssetItem>();   
                    }

                    Dictionary<string, AssetItem> lookup =
                        TypeLookup[item._Type];
                    if (lookup.TryGetValue(item._Name,
                            out AssetItem existingItem) &&
                        GetLookupPriority(existingItem) >
                        GetLookupPriority(item))
                    {
                        continue;
                    }

                    lookup[item._Name] = item;
                }
            }
            //if (added)
            //{
                BuildStringTypes();
           // }
        }

        private static int GetLookupPriority(AssetItem item)
        {
            if (item == null)
            {
                return int.MinValue;
            }

            int priority = 0;
            if (item._SerializedItem != null)
            {
                priority += 4;
            }
            if (item.IsAddressable)
            {
                priority += 2;
            }
            if (item.Ignore)
            {
                priority -= 1;
            }

#if UNITY_EDITOR
            string resolvedPath = string.IsNullOrEmpty(item._Guid)
                ? string.Empty
                : AssetDatabase.GUIDToAssetPath(item._Guid);
            string path = string.IsNullOrEmpty(resolvedPath)
                ? item._Path
                : resolvedPath;
            string installRoot = UMAPathUtility.InstallAssetRoot;
            if (IsPathInside(path, installRoot))
            {
                priority += 8;
            }
#endif

            return priority;
        }

#if UNITY_EDITOR
        private static bool IsPathInside(string path, string root)
        {
            string normalizedPath = (path ?? string.Empty)
                .Replace('\\', '/').TrimEnd('/');
            string normalizedRoot = (root ?? string.Empty)
                .Replace('\\', '/').TrimEnd('/');
            return string.Equals(normalizedPath, normalizedRoot,
                       StringComparison.OrdinalIgnoreCase) ||
                   normalizedPath.StartsWith(normalizedRoot + "/",
                       StringComparison.OrdinalIgnoreCase);
        }
#endif


        private void CreateGenerator()
        {
#if UMA_VES //VES added
			if(SceneManager.GetActiveScene().name.StartsWith("Init")) {
                GameObject coreManagers = GameObject.Find("CoreManagers");
				generator = coreManagers != null
                    ? coreManagers.GetComponentInChildren<UMAGenerator>()
                    : null;
				return;
			}
#endif
            if (IsUsableSceneGenerator(generator))
            {
                return;
            }
            generator = null;

            generator = FindUsableSceneGenerator();
            if (IsUsableSceneGenerator(generator))
            {
                ConsolidateInternalGenerators();
                return;
            }
            generator = null;

            UMASettings settings = UMASettings.GetSettingsFromResources();
            if (settings == null)
            {
                Debug.LogError("Unable to load UMASettings!!! UMA Will Not Work!");
                return;
            }

            if (settings.generatorPrefab == null)
            {
                Debug.LogError(
                    "UMASettings does not specify a Generator Prefab. UMA will not work.");
                return;
            }

            UMAGenerator prefabGenerator =
                settings.generatorPrefab.GetComponent<UMAGenerator>();
            if (prefabGenerator == null)
            {
                Debug.LogError(
                    $"The configured UMA Generator Prefab '{settings.generatorPrefab.name}' " +
                    "does not contain an UMAGenerator component.",
                    settings.generatorPrefab);
                return;
            }

            GameObject go = GameObject.Instantiate(settings.generatorPrefab);
            go.name = generatorName;
            generator = go.GetComponent<UMAGenerator>();
            /*
            if (!IsUsableSceneGenerator(generator))
            {
                Debug.LogError(
                    $"Unable to create a scene UMAGenerator from prefab " +
                    $"'{settings.generatorPrefab.name}'.",
                    settings.generatorPrefab);
                if (Application.isPlaying)
                {
                    GameObject.Destroy(go);
                }
                else
                {
                    GameObject.DestroyImmediate(go);
                }
                generator = null;
                return;
            }*/

            if (!generator.showInHierarchy)
            {
                go.hideFlags = HideFlags.HideAndDontSave | HideFlags.DontUnloadUnusedAsset;
            }
            else
            {
                go.hideFlags = HideFlags.DontSave | HideFlags.DontUnloadUnusedAsset;
            }

#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
                GameObject.DontDestroyOnLoad(go);
            }
#else
            GameObject.DontDestroyOnLoad(go);
#endif
            go.SetActive(true);
            ConsolidateInternalGenerators();
        }

#if UNITY_EDITOR
        public void AddSearchFolder(string type, string FolderName)
        {
            var tf = typeFolders.Find(x => x.typeName == type);
            if (tf != null)
            {
                if (ContainsString(tf.Folders, FolderName))
                {
                    return;
                }
                tf.Folders = AppendString(tf.Folders, FolderName);
            }
            else
            {
                tf = new TypeFolders();
                tf.typeName = type;
                tf.Folders = new string[] { FolderName };
                typeFolders.Add(tf);
            }

            CreateTypeFolderMapping();
            ForceSave();
        }

        public void RemoveSearchFolder(string type, string FolderName)
        {
            var tf = typeFolders.Find(x => x.typeName == type);
            if (tf != null)
            {
                tf.Folders = RemoveString(tf.Folders, FolderName);
                CreateTypeFolderMapping();
                ForceSave();
            }
        }
#endif

        public Type GetRuntimeType(Type type)
        {
            return TypeToLookup[type];
        }


#if UNITY_EDITOR
        /// <summary>
        /// This returns TRUE (isValid) if any type has valid entries
        /// This returns FALSE if all types have no entries, or there are no types.
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            foreach (var t in TypeToLookup.Keys)
            {
                var typeDic = GetAssetDictionary(t);
                if (typeDic.Keys.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }
#endif

#if UMA_ADDRESSABLES
        private HashSet<CachedOp> Cleanup = new HashSet<CachedOp>();
        public void CheckCache()
        {
            Cleanup.Clear();

            for(int i=0;i<LoadedItems.Count;i++)
            {
                CachedOp c = LoadedItems[i];
                if (c.Expired && c.Operation.IsDone)
                {
                    Cleanup.Add(c);
                }
            }

            foreach (CachedOp cachedOp in Cleanup)
            {
                Unload(cachedOp.Operation);
            }
        }
#endif
#if UNITY_EDITOR

        /*
        public void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool changed = false;

            // Build a dictionary of the items by path.
            Dictionary<string, AssetItem> ItemsByPath = new Dictionary<string, AssetItem>();
            UpdateSerializedList();
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                if (ItemsByPath.ContainsKey(ai._Path))
                {
                    if (Debug.isDebugBuild)
                    {
                        Debug.Log("Duplicate path for item: " + ai._Path);
                    }

                    continue;
                }
                ItemsByPath.Add(ai._Path, ai);
            }

            // see if they moved it in the editor.
            for (int i = 0; i < movedAssets.Length; i++)
            {
                string NewPath = movedAssets[i];
                string OldPath = movedFromAssetPaths[i];

                // Check to see if this is an indexed asset.
                if (ItemsByPath.ContainsKey(OldPath))
                {
                    changed = true;
                    ItemsByPath[OldPath]._Path = NewPath;
                }
            }

            // Rebuild the tables
            SerializedItems.Clear();
            foreach (AssetItem ai in ItemsByPath.Values)
            {
                // We null things out when we want to delete them. This prevents it from going back into
                // the dictionary when rebuilt.
                if (ai == null)
                {
                    continue;
                }

                SerializedItems.Add(ai);
            }

            UpdateSerializedDictionaryItems();
            if (changed)
            {
                ForceSave();
            }
        }
        */

        /// <summary>
        /// Force the Index to save and reload
        /// </summary>
        public void ForceSave()
        {
            var st = StartTimer();
#if UNITY_EDITOR
            UMAAssetIndexer writableIndexer = EnsureProjectOwnedIndexer(this);
            if (!ReferenceEquals(writableIndexer, this))
            {
                theIndexer = writableIndexer;
                writableIndexer.ForceSave();
                StopTimer(st, "ForceSave");
                return;
            }
#endif
            EditorUtility.SetDirty(this);
            // Save all assets
            //AssetDatabase.SaveAssetIfDirty(this);
            AssetDatabase.SaveAssets();
            StopTimer(st, "ForceSave");
        }
#endif

#if UNITY_EDITOR
        public void CompareSerializedItems2(UMAAssetIndexer after, string filePath)
        {
            if (after == null)
            {
                Debug.LogError("Cannot compare to null UMAAssetIndexer");
                return;
            }

            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("File path cannot be null or empty");
                return;
            }

            try
            {
                // Create dictionaries for faster lookup by name and type
                var thisItems = new Dictionary<string, AssetItem>();
                var otherItems = new Dictionary<string, AssetItem>();

                // Build lookup dictionaries for this indexer
                foreach (var item in SerializedItems)
                {
                    if (item != null && !string.IsNullOrEmpty(item._Name))
                    {
                        string key = $"{item._Type?.Name ?? "Unknown"}:{item._Name}";
                        if (!thisItems.ContainsKey(key))
                        {
                            thisItems[key] = item;
                        }
                        else
                        {
                            Debug.LogWarning($"Duplicate item found in this indexer: {item._Type?.Name ?? "Unknown"}:{item._Name}");
                        }
                    }
                }

                // Build lookup dictionaries for other indexer
                foreach (var item in after.SerializedItems)
                {
                    if (item != null && !string.IsNullOrEmpty(item._Name))
                    {
                        string key = $"{item._Type?.Name ?? "Unknown"}:{item._Name}";
                        if (!otherItems.ContainsKey(key))
                        {
                            otherItems[key] = item;
                        }
                        else
                        {
                            Debug.LogWarning($"Duplicate item found in other indexer: {item._Type?.Name ?? "Unknown"}:{item._Name}");
                        }
                    }
                }

                // Find items missing in other indexer
                var missingInOther = new List<AssetItem>();
                foreach (var kvp in thisItems)
                {
                    if (!otherItems.ContainsKey(kvp.Key))
                    {
                        missingInOther.Add(kvp.Value);
                    }
                }

                // Find items missing in this indexer
                var missingInThis = new List<AssetItem>();
                foreach (var kvp in otherItems)
                {
                    if (!thisItems.ContainsKey(kvp.Key))
                    {
                        missingInThis.Add(kvp.Value);
                    }
                }

                // Generate detailed report
                StringBuilder report = new StringBuilder();
                report.AppendLine($"UMA Asset Indexer SerializedItems Comparison Report");
                report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                report.AppendLine("=".PadRight(70, '='));
                report.AppendLine();

                // Summary
                report.AppendLine("SUMMARY:");
                report.AppendLine($"  Before indexer SerializedItems count: {SerializedItems.Count}");
                report.AppendLine($"  After indexer SerializedItems count: {after.SerializedItems.Count}");
                report.AppendLine($"  Items missing in After indexer: {missingInOther.Count}");
                report.AppendLine($"  Items missing in Before indexer: {missingInThis.Count}");
                report.AppendLine($"  Total differences: {missingInOther.Count + missingInThis.Count}");
                report.AppendLine();

                // Items missing in other indexer
                if (missingInOther.Count > 0)
                {
                    report.AppendLine($"ITEMS MISSING IN AFTER INDEXER ({missingInOther.Count}):");
                    report.AppendLine("-".PadRight(50, '-'));

                    WriteGroupedAssetItemsReport(report, missingInOther);
                }

                // Items missing in this indexer
                if (missingInThis.Count > 0)
                {
                    report.AppendLine($"ITEMS MISSING IN BEFORE INDEXER ({missingInThis.Count}):");
                    report.AppendLine("-".PadRight(50, '-'));

                    WriteGroupedAssetItemsReport(report, missingInThis);
                }

                // Type breakdown comparison
                report.AppendLine("TYPE BREAKDOWN COMPARISON:");
                report.AppendLine("-".PadRight(50, '-'));

                // Collect all type names from both dictionaries
                List<string> allTypesList = new List<string>();

                foreach (var item in thisItems.Values)
                {
                    string typeName = item._Type != null ? item._Type.Name : "Unknown";
                    if (!allTypesList.Contains(typeName))
                    {
                        allTypesList.Add(typeName);
                    }
                }
                foreach (var item in otherItems.Values)
                {
                    string typeName = item._Type != null ? item._Type.Name : "Unknown";
                    if (!allTypesList.Contains(typeName))
                    {
                        allTypesList.Add(typeName);
                    }
                }

                // Sort the type names alphabetically
                allTypesList.Sort();

                for (int t = 0; t < allTypesList.Count; t++)
                {
                    string typeName = allTypesList[t];
                    int thisCount = 0;
                    int otherCount = 0;

                    foreach (var item in thisItems.Values)
                    {
                        string tn = item._Type != null ? item._Type.Name : "Unknown";
                        if (tn == typeName)
                            thisCount++;
                    }
                    foreach (var item in otherItems.Values)
                    {
                        string tn = item._Type != null ? item._Type.Name : "Unknown";
                        if (tn == typeName)
                            otherCount++;
                    }

                    string status = (thisCount == otherCount) ? "✓" : "⚠";
                    report.AppendLine($"  {status} {typeName}: Before={thisCount}, After={otherCount}");
                }
                report.AppendLine();

                // Final status
                if (missingInOther.Count == 0 && missingInThis.Count == 0)
                {
                    report.AppendLine("✓ SUCCESS: SerializedItems lists are identical.");
                }
                else
                {
                    report.AppendLine($"⚠ DIFFERENCES FOUND: {missingInOther.Count} items missing in After, {missingInThis.Count} items missing in Before indexer");
                }

                // Write report to file
                File.WriteAllText(filePath, report.ToString());
                Debug.Log($"SerializedItems comparison complete. Report written to: {filePath}");
                if (missingInOther.Count > 0 || missingInThis.Count > 0)
                {
                    Debug.LogWarning($"Found differences: {missingInOther.Count} missing in other indexer, {missingInThis.Count} missing in this indexer.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error during SerializedItems comparison: {ex.Message}");
                Debug.LogException(ex);

                // Write error report
                try
                {
                    File.WriteAllText(filePath, $"SerializedItems Comparison Error\n" +
                                            $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                                            $"Error: {ex.Message}\n" +
                                            $"Stack Trace:\n{ex.StackTrace}");
                }
                catch
                {
                    // Ignore file write errors in error handler
                }

            }
        }

        public void CompareSerializedItems(UMAAssetIndexer After, string filePath) {
			if(After == null) {
				Debug.LogError("Cannot compare to null UMAAssetIndexer");
				return;
			}

			if(string.IsNullOrEmpty(filePath)) {
				Debug.LogError("File path cannot be null or empty");
				return;
			}

			Initialize();
			UpdateSerializedDictionaryItems();

			After.Initialize();
			After.UpdateSerializedDictionaryItems();


			try {
				// Create dictionaries for faster lookup by name and type
				var thisItems = new Dictionary<string, AssetItem>();
				var otherItems = new Dictionary<string, AssetItem>();

				// Build lookup dictionaries for this indexer
				foreach(var item in SerializedItems) {
					if(item != null && !string.IsNullOrEmpty(item._Name)) {
						string key = $"{item._Type?.Name ?? "Unknown"}:{item._Name}";
						if(!thisItems.ContainsKey(key)) {
							thisItems[key] = item;
						}
					}
				}

				// Build lookup dictionaries for other indexer
				foreach(var item in After.SerializedItems) {
					if(item != null && !string.IsNullOrEmpty(item._Name)) {
						string key = $"{item._Type?.Name ?? "Unknown"}:{item._Name}";
						if(!otherItems.ContainsKey(key)) {
							otherItems[key] = item;
						}
					}
				}

				// Find items missing in other indexer
				var missingInOther = new List<AssetItem>();
				foreach(var kvp in thisItems) {
					if(!otherItems.ContainsKey(kvp.Key)) {
						missingInOther.Add(kvp.Value);
					}
				}

				// Find items missing in this indexer
				var missingInThis = new List<AssetItem>();
				foreach(var kvp in otherItems) {
					if(!thisItems.ContainsKey(kvp.Key)) {
						missingInThis.Add(kvp.Value);
					}
				}

				// Generate detailed report
				StringBuilder report = new StringBuilder();
				report.AppendLine($"UMA Asset Indexer SerializedItems Comparison Report");
				report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
				report.AppendLine("=".PadRight(70, '='));
				report.AppendLine();

				// Summary
				report.AppendLine("SUMMARY:");
				report.AppendLine($"  Before indexer SerializedItems count: {SerializedItems.Count}");
				report.AppendLine($"  After indexer SerializedItems count: {After.SerializedItems.Count}");
				report.AppendLine($"  Items missing in After indexer: {missingInOther.Count}");
				report.AppendLine($"  Items missing in Before indexer: {missingInThis.Count}");
				report.AppendLine($"  Total differences: {missingInOther.Count + missingInThis.Count}");
				report.AppendLine();

				// Items missing in other indexer
				if(missingInOther.Count > 0) {
					report.AppendLine($"ITEMS MISSING IN After INDEXER ({missingInOther.Count}):");
					report.AppendLine("-".PadRight(50, '-'));

					// Group by type for better readability
                    WriteGroupedAssetItemsReport(report, missingInOther);
				}

				// Items missing in this indexer
				if(missingInThis.Count > 0) {
					report.AppendLine($"ITEMS MISSING IN BEFORE INDEXER ({missingInThis.Count}):");
					report.AppendLine("-".PadRight(50, '-'));

					// Group by type for better readability
                    WriteGroupedAssetItemsReport(report, missingInThis);
				}

				// Type breakdown comparison
				report.AppendLine("TYPE BREAKDOWN COMPARISON:");
				report.AppendLine("-".PadRight(50, '-'));

                List<string> allTypes = GetSortedAssetTypeNames(thisItems, otherItems);

                foreach(var typeName in allTypes) {
                    int thisCount = CountAssetItemsByType(thisItems, typeName);
                    int otherCount = CountAssetItemsByType(otherItems, typeName);
					var status = thisCount == otherCount ? "✓" : "⚠";

					report.AppendLine($"  {status} {typeName}: Before={thisCount}, After={otherCount}");
				}
				report.AppendLine();

				// Final status
				if(missingInOther.Count == 0 && missingInThis.Count == 0) {
					report.AppendLine("✓ SUCCESS: SerializedItems lists are identical.");
				} else {
					report.AppendLine($"⚠ DIFFERENCES FOUND: {missingInOther.Count} items missing in After, {missingInThis.Count} items missing in Before indexer");
				}

				// Write report to file
				File.WriteAllText(filePath, report.ToString());
                Debug.Log($"SerializedItems comparison complete. Report written to: {filePath}");
				if(missingInOther.Count > 0 || missingInThis.Count > 0) {
					Debug.LogWarning($"Found differences: {missingInOther.Count} missing in After indexer, {missingInThis.Count} missing in Before indexer.");
				}


            } catch(Exception ex) {
				Debug.LogError($"Error during SerializedItems comparison: {ex.Message}");
				Debug.LogException(ex);

				// Write error report
				try {
					File.WriteAllText(filePath, $"SerializedItems Comparison Error\n" +
											$"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
											$"Error: {ex.Message}\n" +
											$"Stack Trace:\n{ex.StackTrace}");
				} catch {
					// Ignore file write errors in error handler
				}
			}
		}

		public void CompareTo(UMAAssetIndexer After, string filePath) {
			// compare the types first. Log any that are missing the the file at filePath.

			if(After == null) {
				Debug.LogError("Cannot compare to null UMAAssetIndexer");
				return;
			}

			if(string.IsNullOrEmpty(filePath)) {
				Debug.LogError("File path cannot be null or empty");
				return;
			}

			List<string> missingTypes = new List<string>();
			List<string> missingAssets = new List<string>();
			List<string> comparisonLog = new List<string>();

			Initialize();
			UpdateSerializedDictionaryItems();

			After.Initialize();
			After.UpdateSerializedDictionaryItems();

			try {
				// Compare TypeToLookup dictionaries
				var thisTypes = new HashSet<System.Type>(TypeToLookup.Keys);
				var otherTypes = new HashSet<System.Type>(After.TypeToLookup.Keys);

				// Find types missing in other indexer
				foreach(var thisType in thisTypes) {
					if(!otherTypes.Contains(thisType)) {
						missingTypes.Add($"Type missing in After indexer: {thisType.Name} ({thisType.FullName})");
					}
				}

				// Find types missing in this indexer
				foreach(var otherType in otherTypes) {
					if(!thisTypes.Contains(otherType)) {
						missingTypes.Add($"Type missing in Before indexer: {otherType.Name} ({otherType.FullName})");
					}
				}

				// Compare assets within common types
                List<Type> commonTypes = GetSortedCommonTypes(thisTypes, otherTypes);
                foreach(var type in commonTypes) {
					var thisAssets = GetAssetDictionary(type);
					var otherAssets = After.GetAssetDictionary(type);

					// Assets in this indexer but not in other
					foreach(var assetName in thisAssets.Keys) {
						if(!otherAssets.ContainsKey(assetName)) {
							missingAssets.Add($"Asset missing in After indexer: '{assetName}' (Type: {type.Name})");
						}
					}

					// Assets in other indexer but not in this
					foreach(var assetName in otherAssets.Keys) {
						if(!thisAssets.ContainsKey(assetName)) {
							missingAssets.Add($"Asset missing in Before indexer: '{assetName}' (Type: {type.Name})");
						}
					}
				}

				// Compare additional indexed type names
				var thisAdditionalTypes = new HashSet<string>(IndexedTypeNames);
				var otherAdditionalTypes = new HashSet<string>(After.IndexedTypeNames);

				foreach(var typeName in thisAdditionalTypes) {
					if(!otherAdditionalTypes.Contains(typeName)) {
						missingTypes.Add($"Additional indexed type missing in After indexer: {typeName}");
					}
				}

				foreach(var typeName in otherAdditionalTypes) {
					if(!thisAdditionalTypes.Contains(typeName)) {
						missingTypes.Add($"Additional indexed type missing in Before indexer: {typeName}");
					}
				}

				// Generate comparison report
				StringBuilder report = new StringBuilder();
				report.AppendLine($"UMA Asset Indexer Comparison Report");
				report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
				report.AppendLine("=".PadRight(60, '='));
				report.AppendLine();

				// Summary
				report.AppendLine("SUMMARY:");
				report.AppendLine($"  This indexer contains {SerializedItems.Count} total items");
				report.AppendLine($"  Comparison indexer contains {After.SerializedItems.Count} total items");
				report.AppendLine($"  Missing types found: {missingTypes.Count}");
				report.AppendLine($"  Missing assets found: {missingAssets.Count}");
				report.AppendLine();

				// Type comparison details
				if(missingTypes.Count > 0) {
					report.AppendLine($"MISSING TYPES ({missingTypes.Count}):");
					report.AppendLine("-".PadRight(40, '-'));
					foreach(var missingType in missingTypes) {
						report.AppendLine($"  • {missingType}");
					}
					report.AppendLine();
				}

				// Asset comparison details
				if(missingAssets.Count > 0) {
					report.AppendLine($"MISSING ASSETS ({missingAssets.Count}):");
					report.AppendLine("-".PadRight(40, '-'));

					// Group missing assets by type for better readability
                    List<KeyValuePair<string, List<string>>> assetsByType = GroupMissingAssetsByType(missingAssets);

                    foreach(var typeGroup in assetsByType) {
                        report.AppendLine($"  {typeGroup.Key}:");
                        foreach(var asset in typeGroup.Value) {
							report.AppendLine($"    • {asset}");
						}
						report.AppendLine();
					}
				}

				// Type details comparison
				report.AppendLine("TYPE DETAILS COMPARISON:");
				report.AppendLine("-".PadRight(40, '-'));
                List<Type> allTypes = GetSortedUnionTypes(thisTypes, otherTypes);
                foreach(var type in allTypes) {
					var thisCount = thisTypes.Contains(type) ? GetAssetDictionary(type).Count : 0;
					var otherCount = otherTypes.Contains(type) ? After.GetAssetDictionary(type).Count : 0;
					var status = thisCount == otherCount ? "✓" : "⚠";

					report.AppendLine($"  {status} {type.Name}: This={thisCount}, Other={otherCount}");
				}
				report.AppendLine();

				if(missingTypes.Count == 0 && missingAssets.Count == 0) {
					report.AppendLine("✓ SUCCESS: No missing types or assets found. Indexers are identical.");
				} else {
					report.AppendLine($"⚠ DIFFERENCES FOUND: {missingTypes.Count} missing types, {missingAssets.Count} missing assets");
				}

				// Write report to file
				File.WriteAllText(filePath, report.ToString());

				Debug.Log($"UMA Asset Indexer comparison complete. Report written to: {filePath}");
				if(missingTypes.Count > 0 || missingAssets.Count > 0) {
					Debug.LogWarning($"Found {missingTypes.Count} missing types and {missingAssets.Count} missing assets between indexers.");
				}
			} catch(Exception ex) {
				Debug.LogError($"Error during UMA Asset Indexer comparison: {ex.Message}");
				Debug.LogException(ex);

				// Write error report
				try {
					File.WriteAllText(filePath, $"UMA Asset Indexer Comparison Error\n" +
													$"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
													$"Error: {ex.Message}\n" +
													$"Stack Trace:\n{ex.StackTrace}");
				} catch {
					// Ignore file write errors in error handler
				}
			}
		}
#endif


        #region Manage Types
        /// <summary>
        /// Returns a list of all types that we know about.
        /// </summary>
        /// <returns></returns>
        public System.Type[] GetTypes()
        {
            return Types;
        }

        public System.Type GetIndexedType(System.Type type)
        {
            if (TypeToLookup.ContainsKey(type))
            {
                return TypeToLookup[type];
            }
            return type;
        }

        public Dictionary<System.Type, System.Type>.ValueCollection GetIndexedTypeValues()
        {
            return TypeToLookup.Values;
        }

        public bool IsIndexedType(System.Type type)
        {

            foreach (System.Type check in TypeToLookup.Keys)
            {
                if (check == type)
                {
                    return true;
                }
            }
            return false;
        }

        public bool isRemoveUnlabelledType(string QualifiedName)
        {
            for (int i = 0; i < RemoveUnlabeledTypeNames.Count; i++)
            {
                string s = RemoveUnlabeledTypeNames[i];
                if (s == QualifiedName)
                {
                    return true;
                }
            }
            return false;
        }

        public void toggleRemoveUnabelledType(string QualifiedName)
        {
            if (isRemoveUnlabelledType(QualifiedName))
            {
                RemoveUnlabeledTypeNames.Remove(QualifiedName);
            }
            else
            {
                RemoveUnlabeledTypeNames.Add(QualifiedName);
            }
        }

        public bool setRemoveUnlabelledType(string QualifiedName, bool remove)
        {
            if (remove)
            {
                if (!isRemoveUnlabelledType(QualifiedName))
                {
                    RemoveUnlabeledTypeNames.Add(QualifiedName);
                    return true;
                }
            }
            else
            {
                if (isRemoveUnlabelledType(QualifiedName))
                {
                    RemoveUnlabeledTypeNames.Remove(QualifiedName);
                    return true;
                }
            }
            return false;
        }

        public bool IsAdditionalIndexedType(string QualifiedName)
        {
            if (string.IsNullOrEmpty(QualifiedName))
            {
                return false;
            }

            for (int i = 0; i < DefaultIndexedTypes.Length; i++)
            {
                System.Type defaultType = DefaultIndexedTypes[i];
                if (defaultType == null)
                {
                    continue;
                }

                string defaultQualifiedName = defaultType.AssemblyQualifiedName;
                if (defaultQualifiedName == QualifiedName)
                {
                    return false;
                }
            }

            return true;
        }
        /// <summary>
        /// Add a type to the types tracked
        /// </summary>
        /// <param name="sType"></param>
        public void AddType(System.Type sType)
        {
            string QualifiedName = sType.AssemblyQualifiedName;

            if (!ContainsType(Types, sType))
            {
                List<System.Type> newTypes = new List<System.Type>();
                newTypes.AddRange(Types);
                newTypes.Add(sType);
                Types = newTypes.ToArray();
            }
            if (!TypeLookup.ContainsKey(sType))
            {
                TypeLookup.Add(sType, new Dictionary<string, AssetItem>());
            }
            if (!TypeToLookup.ContainsKey(sType))
            {
                Debug.Log("Adding type: " + sType.ToString());
                TypeToLookup.Add(sType, sType);
            }
            if (!IndexedTypeNames.Contains(QualifiedName))
            {
                Debug.Log("Adding indexed type: " + QualifiedName);
                IndexedTypeNames.Add(QualifiedName);
            }
            BuildStringTypes();

#if UNITY_EDITOR
			// Persist the updated `IndexedTypeNames` list so it survives domain reload.
			EditorUtility.SetDirty(this);
			AssetDatabase.SaveAssets();
#endif
        }

        public void RemoveType(System.Type sType)
        {
            string QualifiedName = sType.AssemblyQualifiedName;

            TypeToLookup.Remove(sType);

            List<System.Type> newTypes = new List<System.Type>();
            newTypes.AddRange(Types);
            newTypes.Remove(sType);
            Types = newTypes.ToArray();
            TypeLookup.Remove(sType);
            IndexedTypeNames.Remove(QualifiedName);
            BuildStringTypes();
        }
        #endregion

        #region Access the index
        public AssetItem GetRecipeItem(UMAPackedRecipeBase recipe)
        {
            if (recipe is UMAWardrobeCollection)
            {
                return GetAssetItem<UMAWardrobeCollection>(recipe.name);
            }

            if (recipe is UMAWardrobeRecipe)
            {
                return GetAssetItem<UMAWardrobeRecipe>(recipe.name);
            }

            if (recipe is UMATextRecipe)
            {
                return GetAssetItem<UMATextRecipe>(recipe.name);
            }

            return null;
        }

        public UMAData.UMARecipe GetRecipe(UMATextRecipe recipe)
        {
            UMAPackedRecipeBase.UMAPackRecipe PackRecipe = recipe.PackedLoad();
            try
            {
                UMAData.UMARecipe TempRecipe = UMATextRecipe.UnpackRecipe(PackRecipe);
                return TempRecipe;
            }
            catch (Exception ex)
            {
                Debug.LogError("Error unpacking recipe: " + recipe.name + ". " + ex.Message);
            }
            return new UMAData.UMARecipe();
        }

        public bool HasAsset<T>(string Name)
        {
            System.Type ot = typeof(T);
            System.Type theType = TypeToLookup[ot];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            return TypeDic.ContainsKey(Name);
        }


        public int LevenshteinDistance(string s1, string s2)
        {
            if (s1 == null) s1 = "";
            if (s2 == null) s2 = "";
            int len1 = s1.Length;
            int len2 = s2.Length;
            int[,] d = new int[len1 + 1, len2 + 1];
            for (int i = 0; i <= len1; i++) d[i, 0] = i;
            for (int j = 0; j <= len2; j++) d[0, j] = j;
            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[len1, len2];
        }

        private float Similarity(string s1, string s2)
        {
            if (s1 == null || s2 == null)
            {
                return 0f;
            }
            int maxLength = Math.Max(s1.Length, s2.Length);
            if (maxLength == 0)
            {
                return 1f; // Both strings are empty
            }
            if (s1.ToLower().Equals(s2.ToLower())) {
                return 1f;
            }
            if (s2.StartsWith(s1, StringComparison.OrdinalIgnoreCase) || s1.StartsWith(s2, StringComparison.OrdinalIgnoreCase))
            {
                return 0.9f; // One string is a prefix of the other
            }
            if (s2.EndsWith(s1, StringComparison.OrdinalIgnoreCase) || s1.EndsWith(s2, StringComparison.OrdinalIgnoreCase))
            {
                return 0.9f; // One string is a suffix of the other
            }

            int distance = LevenshteinDistance(s1.ToLower(), s2.ToLower());
            return 1f - (float)distance / maxLength;
        }



        public List<string> FindSimilar<T>(string Name, string possibleID)
        {
            List<string> returnval = new List<string>();
            System.Type ot = typeof(T);
            System.Type theType = TypeToLookup[ot];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            foreach (string s in TypeDic.Keys)
            {
                if (Similarity(Name, s) > 0.8f)
                {
                    returnval.Add(s);
                    continue;
                }
                string s1ID = Name.Replace(possibleID, "");
                string s2ID = s.Replace(possibleID, "");
                if (Similarity(s1ID, s2ID) > 0.8f)
                {
                    returnval.Add(s);
                    continue;
                }
            }
            return returnval;
        }

        public bool HasAsset<T>(int NameHash)
        {
            System.Type ot = typeof(T);
            System.Type theType = TypeToLookup[ot];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            // This honestly hurt my heart typing this.
            // Todo: replace this loop with a dictionary.
            foreach (string s in TypeDic.Keys)
            {
                if (UMAUtils.StringToHash(s) == NameHash)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Return the asset specified, if it exists.
        /// if it can't be found by name, then we do a scan of the assets to see if
        /// we can find the name directly on the object, and return that.
        /// We then rebuild the index to make sure it's up to date.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="Name"></param>
        /// <returns></returns>
        public AssetItem GetAssetItem<T>(string Name)
        {
            if (string.IsNullOrEmpty(Name))
            {
                return null;
            }
#if UMA_INDEX_LC
            Name = Name.ToLower();
#endif
            System.Type ot = typeof(T);

            if (!TypeToLookup.ContainsKey(ot))
            {
                Debug.LogError($"Unknown type: {ot.ToString()} for item {Name}");
                return null;
            }
            System.Type theType = TypeToLookup[ot];

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            if (TypeDic.ContainsKey(Name))
            {
#if UNITY_EDITOR
                if (Debug.isDebugBuild)
                {
                    if (TypeDic[Name] == null)
                    {
                        Debug.LogError($"Asset with Name {Name} is NULL for type {ot.ToString()}");
                    }
                }
#endif
                return TypeDic[Name];
            }
            else
            {
#if !UNITY_EDITOR
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning($"Unknown item [{Name}] for type {ot.ToString()}.");
                }
#endif
            }

            return null;
        }

        /// <summary>
        /// Return the asset specified, if it exists.
        /// if it can't be found by name, then we do a scan of the assets to see if
        /// we can find the name directly on the object, and return that.
        /// We then rebuild the index to make sure it's up to date.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="Name"></param>
        /// <returns></returns>
        public AssetItem GetAssetItemForObject(UnityEngine.Object o)
        {
            System.Type ot = o.GetType();
            System.Type theType = TypeToLookup[ot];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            string Name = AssetItem.GetEvilName(o);

            if (TypeDic.ContainsKey(Name))
            {
                return TypeDic[Name];
            }
            return null;
        }

        /// <summary>
        /// If we know the type, we can get the dictionary directly.
        /// </summary>
        /// <param name="ot"></param>
        /// <param name="Name"></param>
        /// <returns></returns>
        public AssetItem GetAssetItem(System.Type ot, string Name)
        {
            System.Type theType = TypeToLookup[ot];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            if (TypeDic.ContainsKey(Name))
            {
                return TypeDic[Name];
            }
            return null;
        }



        public List<AssetItem> GetAssetItems(string recipe, bool LookForLODs = false)
        {
            AssetItem ai = GetAssetItem<UMAWardrobeRecipe>(recipe);
            if (ai != null)
            {
                return GetAssetItems(ai.Item as UMAWardrobeRecipe, LookForLODs);
            }
            return new List<AssetItem>();
        }

        public List<AssetItem> GetAssetItems(UMAPackedRecipeBase recipe, bool LookForLODs = false)
        {
            if (recipe is UMAWardrobeCollection)
            {
                return new List<AssetItem>();
            }
            UMAPackedRecipeBase.UMAPackRecipe PackRecipe = recipe.PackedLoad();

            var Slots = PackRecipe.slotsV3;

            if (Slots == null)
            {
                return GetAssetItemsV2(PackRecipe, LookForLODs);
            }

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(typeof(SlotDataAsset));
            List<AssetItem> returnval = new List<AssetItem>();

            for (int i1 = 0; i1 < Slots.Length; i1++)
            {
                PackSlot slot = Slots[i1];
                // We are getting extra blank slots. That's weird.
                if (slot == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(slot.id))
                {
                    continue;
                }

                AssetItem s = GetAssetItem<SlotDataAsset>(slot.id);
                if (s != null)
                {
                    returnval.Add(s);
                    string LodIndicator = slot.id.Trim() + "_LOD";
                    if (slot.id.Contains("_LOD"))
                    {
                        // LOD is directly in the base recipe.
                        LodIndicator = slot.id.Substring(0, slot.id.Length - 1);
                    }

                    if (slot.overlays != null)
                    {
                        for (int i = 0; i < slot.overlays.Length; i++)
                        {
                            UMAPackedRecipeBase.PackedOverlayDataV3 overlay = slot.overlays[i];
                            if (overlay == null)
                            {
                                continue;
                            }

                            AssetItem o = GetAssetItem<OverlayDataAsset>(overlay.id);
                            if (o != null)
                            {
                                returnval.Add(o);
                            }
                        }
                    }
                    if (LookForLODs)
                    {
                        foreach (string slod in TypeDic.Keys)
                        {
                            if (String.IsNullOrEmpty(slod))
                            {
                                continue;
                            }

                            if (slod.StartsWith(LodIndicator))
                            {
                                AssetItem lodSlot = GetAssetItem<SlotDataAsset>(slod);
                                returnval.Add(lodSlot);
                            }
                        }
                    }
                }
            }
            return returnval;
        }

        private List<AssetItem> GetAssetItemsV2(UMAPackedRecipeBase.UMAPackRecipe PackRecipe, bool LookForLods)
        {
            List<AssetItem> returnval = new List<AssetItem>();

            var Slots = PackRecipe.slotsV2;

            if (Slots == null)
            {
                return returnval;
            }

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(typeof(SlotDataAsset));

            for (int i1 = 0; i1 < Slots.Length; i1++)
            {
                UMAPackedRecipeBase.PackedSlotDataV2 slot = Slots[i1];
                if (slot == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(slot.id))
                {
                    continue;
                }

                string LodIndicator = slot.id.Trim() + "_LOD";
                AssetItem s = GetAssetItem<SlotDataAsset>(slot.id);
                if (s != null)
                {
                    returnval.Add(s);
                    var overlays = slot.overlays;
                    for (int i = 0; i < overlays.Length; i++)
                    {
                        UMAPackedRecipeBase.PackedOverlayDataV2 overlay = overlays[i];
                        AssetItem o = GetAssetItem<OverlayDataAsset>(overlay.id);
                        if (o != null)
                        {
                            returnval.Add(o);
                        }
                    }
                }
                if (LookForLods)
                {
                    foreach (string slod in TypeDic.Keys)
                    {
                        if (slod.StartsWith(LodIndicator))
                        {
                            AssetItem lodSlot = GetAssetItem<SlotDataAsset>(slod);
                            returnval.Add(lodSlot);
                        }
                    }
                }
            }
            return returnval;
        }

        /// <summary>
        /// Gets the asset hash and name for the given object
        /// </summary>
        private void GetEvilAssetNameAndHash(System.Type type, UnityEngine.Object o, ref string assetName, int assetHash)
        {
            if (o is SlotDataAsset)
            {
                SlotDataAsset sd = o as SlotDataAsset;
                assetName = sd.slotName;
                assetHash = sd.nameHash;
            }
            else if (o is OverlayDataAsset)
            {
                OverlayDataAsset od = o as OverlayDataAsset;
                assetName = od.overlayName;
                assetHash = od.nameHash;
            }
            else if (o is RaceData)
            {
                RaceData rd = o as RaceData;
                assetName = rd.raceName;
                assetHash = UMAUtils.StringToHash(assetName);
            }
            else
            {
                assetName = o.name;
                assetHash = UMAUtils.StringToHash(assetName);
            }
#if UMA_INDEX_LC
            assetName = assetName.ToLower();
            assetHash = UMAUtils.StringToHash(assetName);
#endif
        }

        public List<AssetItem> GetAssetItems<T>()
        {
            List<AssetItem> Items = new List<AssetItem>();
            System.Type ot = typeof(T);
            System.Type theType = TypeToLookup[ot];

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            Items.AddRange(TypeDic.Values);

            return Items;
        }
        public List<AssetItem> GetAssetItems(Type t)
        {
            List<AssetItem> Items = new List<AssetItem>();
            System.Type theType = TypeToLookup[t];

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            Items.AddRange(TypeDic.Values);

            return Items;
        }

        public List<T> GetAllAssets<T>() where T : UnityEngine.Object
        {
            var st = StartTimer();

            var ret = new List<T>();
            System.Type ot = typeof(T);
            System.Type theType = TypeToLookup[ot];

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            foreach (KeyValuePair<string, AssetItem> kp in TypeDic)
            {

                        ret.Add((kp.Value.Item as T));
            }
            StopTimer(st, "GetAllAssets type=" + typeof(T).Name);
            return ret;
        }

        // Only do a full check of the index one time after domain reload

        protected static bool WasChecked = false;

#if UNITY_EDITOR
        /// <summary>
        /// returns true if it rebuilt the index.
        /// returns false if it did NOT rebuild the index.
        /// </summary>
        public bool CheckIndex()
        {

            var settings = UMASettings.GetOrCreateSettings();
            if (settings == null || !settings.autoRepairIndex)
            {
                return false;
            }
            // Unfortunately that asmdef is not available here
            string autoconfig = "UMA_INDEX_AUTOREPAIR";
            if (EditorPrefs.GetBool(autoconfig, false))
            {
                return false;
            }

            if (WasChecked)
            {
                return false;
            }

            WasChecked = true;

            if (!IsValid())
            {
                HealIndex();
                return true;
            }
            return false;
        }
#endif

#if UNITY_EDITOR
        Dictionary<System.Type, HashSet<int>> repairsAttempted = new Dictionary<System.Type, HashSet<int>>();

        private static bool IsMissingAssetRepairEnabled()
        {
            UMASettings settings = UMASettings.GetOrCreateSettings();
            return settings != null && settings.autoRepairIndex;
        }

        public bool AlreadyAttempted<T>(int nameHash)
        {
            if (repairsAttempted.ContainsKey(typeof(T)) == false)
            {
                repairsAttempted.Add(typeof(T), new HashSet<int>());
            }

            HashSet<int> processedTable = repairsAttempted[typeof(T)];
            if (!processedTable.Contains(nameHash))
            {
                processedTable.Add(nameHash);
                return false;
            }
            return true;
        }
#endif

        public T GetAsset<T>(int nameHash, string[] foldersToSearch = null, bool recursionGuard = false) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            bool indexUpdated = CheckIndex();
#endif
            System.Type ot = typeof(T);
            Dictionary<string, AssetItem> TypeDic = (Dictionary<string, AssetItem>)TypeLookup[ot];
            string assetName = "";
            int assetHash = -1;
            foreach (KeyValuePair<string, AssetItem> kp in TypeDic)
            {
                assetName = "";
                assetHash = -1;
                GetEvilAssetNameAndHash(typeof(T), kp.Value.Item, ref assetName, assetHash);
                if (assetHash == nameHash)
                {
                    if (AssetFolderCheck(kp.Value, foldersToSearch))
                    {
                        return (kp.Value.Item as T);
                    }
                    else
                    {
                        return null;
                    }
                }
            }
#if UNITY_EDITOR

            // If this is NOT the second time through the retrieval
            // AND it is not in play mode
            // AND we have not already rebuilt the library because it was corrupt or lost,
            // THEN we rebuild the type library for this specific type and try again.
            if (!recursionGuard && !indexUpdated && !Application.isPlaying &&
                IsMissingAssetRepairEnabled())
            {
                // If we've never done this before for this item, try again.
                if (!AlreadyAttempted<T>(nameHash))
                {
                    RefreshType(ot);
                    return GetAsset<T>(nameHash, foldersToSearch, true);
                }
            }
#endif
            return null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Refresh a specific type by searching the folders
        /// </summary>
        /// <param name="ot"></param>
        private void RefreshType(Type ot)
        {
            //Debug.Log($"Refreshing type {ot.Name} in UMAAssetIndexer.");
            string typeString = ot.Name;

            List<string> FolderFilter = null;
            if (TypeFolderSearch.ContainsKey(typeString))
            {
                FolderFilter = TypeFolderSearch[typeString];
            }
			SerializedItems.RemoveAll(item => item._Type == ot);
            AddType(typeString, ot, FolderFilter);
            ForceSave();
        }
#endif

        public T GetAsset<T>(string name, string[] foldersToSearch, bool recursionGuard = false) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            bool indexUpdated = CheckIndex();
#endif
            var thisAssetItem = GetAssetItem<T>(name);
            if (thisAssetItem != null)
            {
                if (AssetFolderCheck(thisAssetItem, foldersToSearch))
                {
                    return (thisAssetItem.Item as T);
                }
                else
                {
                    return null;
                }
            }
            else
            {
#if UNITY_EDITOR

                // If this is NOT the second time through the retrieval
                // AND it is not in play mode
                // AND we have not already rebuilt the library because it was corrupt or lost,
                // THEN we rebuild the type library for this specific type and try again.
                if (!recursionGuard && !indexUpdated && !Application.isPlaying &&
                    IsMissingAssetRepairEnabled())
                {
                    // If we've never done this before for this item, try again.
                    int nameHash = UMAUtils.StringToHash(name);
                    if (!AlreadyAttempted<T>(nameHash))
                    {

                        RefreshType(typeof(T));
                        return GetAsset<T>(name, foldersToSearch, true);
                    }
                }
#endif
                return null;
            }
        }

        public UMATextRecipe GetRecipeWardrobeTextCollection(string name)
        {

            var wr = GetAssetItem<UMAWardrobeRecipe>(name);
            if (wr != null)
            {
                return wr.Item as UMAWardrobeRecipe;
            }

            var utr = GetAssetItem<UMATextRecipe>(name);
            if (utr != null)
            {
                return utr.Item as UMATextRecipe;
            }

            var wc = GetAssetItem<UMAWardrobeCollection>(name);
            if (wc != null)
            {
                return wc.Item as UMAWardrobeCollection;
            }
            return null;
        }

		public T RawGetAsset<T>(string name) where T : UnityEngine.Object
		{
			System.Type ot = typeof(T);
			if (!TypeToLookup.ContainsKey(ot))
			{
				Debug.LogError($"Unknown type: {ot.ToString()} for item {name}");
			}
			System.Type theType = TypeToLookup[ot];
			Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
			if (TypeDic.ContainsKey(name))
			{
				return (TypeDic[name].Item as T);
			}
			return null;
		}

		/// <summary>
		/// Get an asset by name and type.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="name"></param>
		/// <param name="recursionGuard">If true, we will not try to rebuild the index if it fails.</param>
		/// <param name="inStartup">If true, we will not log an error if the asset is not found.</param>
		/// <returns></returns>
		public T GetAsset<T>(string name, bool recursionGuard = false, bool inStartup = false) where T : UnityEngine.Object
        {
#if UNITY_EDITOR
            bool indexUpdated = false;
            UMASettings settings = UMASettings.GetOrCreateSettings();
            if (settings == null)
            {
                Debug.LogError("Unable to load UMASettings!!! UMA Will Not Work!");
                return null;
            }
			if (!inStartup && settings.autoRepairIndex && !WasChecked)
            {
                indexUpdated = CheckIndex();
            }
#endif
            var thisAssetItem = GetAssetItem<T>(name);
			if(inStartup && thisAssetItem == null) 
			{
#if UNITY_EDITOR
				Debug.Log("Unable to find asset " + name + " of type " + typeof(T).Name + ".");
#endif
				return null;
			}
            if (thisAssetItem != null)
            {
#if UNITY_EDITOR
                if (thisAssetItem.Item == null)
                {
                    if (settings.alwaysGetAddressables)
                    {
                        return thisAssetItem.GetItem<T>();
                    }
                }
#endif
                return (thisAssetItem.Item as T);
            }
            else
            {
#if UNITY_EDITOR
                // If this is NOT the second time through the retrieval
                // AND it is not in play mode
                // AND we have not already rebuilt the library because it was corrupt or lost,
                // THEN we rebuild the type library for this specific type and try again.
                if (!recursionGuard && !indexUpdated && !Application.isPlaying &&
                    settings.autoRepairIndex)
                {
                    // If we've never done this before for this item, try again.
                    int nameHash = UMAUtils.StringToHash(name);
                    if (!AlreadyAttempted<T>(nameHash))
                    {
                        RefreshType(typeof(T));
                        return GetAsset<T>(name, true);
                    }
                }
#endif
                return null;
            }
        }
        public List<UMARecipeBase> GetRecipesForRaceSlot(string race, string slot)
        {
            // This will get the aggregate for all compatible races with no duplicates.
            List<string> recipes = GetRecipeNamesForRaceSlot(race, slot);

            // Build a list of recipes to return.
            List<UMARecipeBase> results = new List<UMARecipeBase>();

            for (int i = 0; i < recipes.Count; i++)
            {
                string recipeName = recipes[i];
                UMAWardrobeRecipe uwr = GetAsset<UMAWardrobeRecipe>(recipeName);
                if (uwr != null)
                {
                    results.Add(uwr);
                }
            }
            return results;
        }


        private void internalGetRecipes(string race, ref Dictionary<string, HashSet<UMATextRecipe>> results)
        {
            if (raceRecipes.ContainsKey(race))
            {
                SlotRecipes sr = raceRecipes[race];

                foreach (KeyValuePair<string, List<UMATextRecipe>> kp in sr)
                {
                    if (!results.ContainsKey(kp.Key))
                    {
                        results.Add(kp.Key, new HashSet<UMATextRecipe>());
                    }
                    results[kp.Key].UnionWith(kp.Value);
                }
            }
            return;
        }

#if false
        public Dictionary<string, List<UMATextRecipe>> GetRecipes(string race)
        {
            Dictionary<string, HashSet<UMATextRecipe>> aggregate = new Dictionary<string, HashSet<UMATextRecipe>>();

            internalGetRecipes(race, ref aggregate);

            RaceData rc = GetAsset<RaceData>(race);
            if (rc != null)
            {
                List<string> list = rc.GetCrossCompatibleRaces();
                for (int i = 0; i < list.Count; i++)
                {
                    string CompatRace = list[i];
                    internalGetRecipes(CompatRace, ref aggregate);
                }
            }

            SlotRecipes results = new SlotRecipes();
            foreach (KeyValuePair<string, HashSet<UMATextRecipe>> kp in aggregate)
            {
                List<UMATextRecipe> listForKey = new List<UMATextRecipe>(kp.Value.Count);
                foreach (UMATextRecipe recipe in kp.Value)
                {
                    listForKey.Add(recipe);
                }
                results.Add(kp.Key, listForKey);
            }

            return results;
        }
#endif
        public Dictionary<string, List<UMATextRecipe>> GetRecipes(string race)
        {
            Dictionary<string, HashSet<UMATextRecipe>> aggregate = new Dictionary<string, HashSet<UMATextRecipe>>();

            internalGetRecipes(race, ref aggregate);

            RaceData rc = GetAsset<RaceData>(race);
            if (rc != null)
            {
                List<string> list = rc.GetCrossCompatibleRaces();
                for (int i = 0; i < list.Count; i++)
                {
                    string CompatRace = list[i];
                    internalGetRecipes(CompatRace, ref aggregate);
                }
            }

            SlotRecipes results = new SlotRecipes();
            foreach (KeyValuePair<string, HashSet<UMATextRecipe>> kp in aggregate)
            {
                List<UMATextRecipe> listForKey = new List<UMATextRecipe>(kp.Value.Count);
                foreach (UMATextRecipe recipe in kp.Value)
                {
                    listForKey.Add(recipe);
                }
                results.Add(kp.Key, listForKey);
            }

            return results;
        }
        private HashSet<string> internalGetRecipeNamesForRaceSlot(string race, string slot)
        {
            HashSet<string> results = new HashSet<string>();

            if (raceRecipes.ContainsKey(race))
            {
                SlotRecipes sr = raceRecipes[race];
                if (sr.ContainsKey(slot))
                {
                    for (int i = 0; i < sr[slot].Count; i++)
                    {
                        UMAWardrobeRecipe uwr = (UMAWardrobeRecipe)sr[slot][i];
                        results.Add(uwr.name);
                    }
                }
            }
            return results;
        }

        public List<string> GetRecipeNamesForRaceSlot(string race, string slot)
        {
            // Start with recipes that are directly marked for this race.
            HashSet<string> results = internalGetRecipeNamesForRaceSlot(race, slot);

            RaceData rc = GetAsset<RaceData>(race);
            if (rc != null)
            {
                List<string> list = rc.GetCrossCompatibleRaces();
                for (int i = 0; i < list.Count; i++)
                {
                    string CompatRace = list[i];
                    results.UnionWith(internalGetRecipeNamesForRaceSlot(CompatRace, slot));
                }
            }

            // Manual conversion of HashSet<string> to List<string> without LINQ
            List<string> resultList = new List<string>(results.Count);
            foreach (string s in results)
            {
                resultList.Add(s);
            }
            return resultList;
        }

        /// <summary>
        /// Load all items from the asset bundle into the index.
        /// </summary>
        /// <param name="ab"></param>
        public void AddFromAssetBundle(AssetBundle ab)
        {
            for (int i = 0; i < Types.Length; i++)
            {
                Type t = Types[i];
                var objs = ab.LoadAllAssets(t);

                for (int i1 = 0; i1 < objs.Length; i1++)
                {
                    UnityEngine.Object o = objs[i1];
                    ProcessNewItem(o, false, false);
                }
            }
        }

        /// <summary>
        /// Load all items from the asset bundle into the index.
        /// </summary>
        /// <param name="ab"></param>
        public void UnloadBundle(AssetBundle ab)
        {
            for (int i = 0; i < Types.Length; i++)
            {
                Type t = Types[i];
                var objs = ab.LoadAllAssets(t);

                for (int i1 = 0; i1 < objs.Length; i1++)
                {
                    UnityEngine.Object o = objs[i1];
                    RemoveItem(o);
                }
            }
        }

        /// <summary>
        /// Checks if the given asset path resides in one of the given folder paths. Returns true if foldersToSearch is null or empty and no check is required
        /// </summary>
        private bool AssetFolderCheck(AssetItem itemToCheck, string[] foldersToSearch = null)
        {
            if (foldersToSearch == null)
            {
                return true;
            }

            if (foldersToSearch.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < foldersToSearch.Length; i++)
            {
                if (itemToCheck._Path.IndexOf(foldersToSearch[i]) > -1)
                {
                    return true;
                }
            }
            return false;
        }

#endregion

        #region Addressables


#if UMA_ADDRESSABLES
        public string GetLabel(UMARecipeBase recipe)
        {
            return recipe.AssignedLabel;
        }

        public AsyncOp PreloadWardrobe(DynamicCharacterAvatar avatar, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			RaceData race = GetAsset<RaceData>(avatar.activeRace.name);

			// preload any assigned recipes.
			foreach (var wr in avatar.WardrobeRecipes.Values)
			{
                //Debug.Log("Adding Wardrobe recipe: " + wr.name);
                if (wr != null)
                {
                    keys.Add(GetLabel(wr));
			}
            }

            // preload any additive recipes.
            foreach (var addList in avatar.AdditiveRecipes.Values)
            {
                if (addList != null)
                {
                    foreach (var wr in addList)
                    {
                        if (wr != null)
                        {
                            keys.Add(GetLabel(wr));
                        }
                    }
                }
            }

			// preload utility recipes
			foreach (var tr in avatar.umaAdditionalRecipes)
			{
                if (tr != null)
                {
			        keys.Add(GetLabel(tr));
			}
            }

			return LoadLabelList(keys, keepLoaded);
		}


        public AsyncOp Preload(DynamicCharacterAvatar avatar, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			RaceData race = GetAsset<RaceData>(avatar.activeRace.name);

			// preload the race
			if (race != null)
			{
                if (race.baseRaceRecipe != null)
                {
                    keys.Add(GetLabel(race.baseRaceRecipe));
			}
            }


			// preload any assigned recipes.
			foreach (var wr in avatar.WardrobeRecipes.Values)
			{
                if (wr != null)
                {
                    keys.Add(GetLabel(wr));
            }
            }

            foreach(var addList in avatar.AdditiveRecipes.Values)
            {
                if (addList != null)
                {
                    foreach(var wr in addList)
                    {
                        if (wr != null)
                        {
                            keys.Add(GetLabel(wr));
                        }
                    }
                }
            }

            if (avatar.umaAdditionalRecipes != null)
            {
                foreach (var tr in avatar.umaAdditionalRecipes)
                {
                    if (tr != null)
                    {
                        keys.Add(GetLabel(tr));
                }
            }
            }
			var op = LoadLabelList(keys, keepLoaded);
			return op;
		}

		public AsyncOp Preload(RaceData theRace, bool keepLoaded = false)
		{
            if (theRace == null || theRace.baseRaceRecipe == null)
            {
                return LoadLabelList(new List<string>(), keepLoaded);
            }
			return LoadLabel(GetLabel(theRace.baseRaceRecipe), keepLoaded);
		}

		public AsyncOp Preload(List<RaceData> theRaces, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			foreach(RaceData rc in theRaces)
			{
                if (rc == null || rc.baseRaceRecipe == null)
                {
                    continue;
                }
				string key = GetLabel(rc.baseRaceRecipe);

				if (keys.Contains(key))
                {
					continue;
                }

				keys.Add(key);
			}
			return LoadLabelList(keys, keepLoaded);
		}

		public AsyncOp LoadLabel(string label, bool keepLoaded = false)
		{
			List<string> keys = new List<string>();
			keys.Add(label);
			return LoadLabelList(keys, keepLoaded);
		}


        public static string KeysToString(string msg, List<string> keys)
        {
            StringBuilder sb = new StringBuilder(msg);
            sb.Append(String.Join("; ", keys));
            return sb.ToString();
        }

		public AsyncOp Preload(UMATextRecipe theRecipe, bool keepLoaded = false)
		{
#if SUPER_LOGGING
			Debug.Log("Preloading: " + theRecipe.name);
#endif
			List<string> keys = new List<string>();
			keys.Add(GetLabel(theRecipe));
			return LoadLabelList(keys, keepLoaded);
		}

		public AsyncOp Preload(List<UMATextRecipe> theRecipes, bool keepLoaded = false)
		{
			List<string> Keys = new List<string>();

			foreach (UMATextRecipe utr in theRecipes)
			{
				Keys.Add(GetLabel(utr));
			}

			return LoadLabelList(Keys,keepLoaded);
		}
        public AsyncOp LoadLabelList(List<string> Keys, bool keepLoaded)
        {
            if (Keys == null)
            {
                Keys = new List<string>();
            }
#if UMA_VES
            Keys.RemoveAll(label => VesUmaLabelMaker.DO_NOT_INCLUDE_LABELS.Contains(label)); //VES added
#endif

            BeforeProcessingLabels.Invoke(Keys);

            if (Keys.Count == 0)
            {
                IList<UnityEngine.Object> emptyResult = new List<UnityEngine.Object>();
                return UMAAddressablesRuntimeBridge.CreateCompleted(emptyResult);
            }

            foreach (string label in Keys)
            {
                if (!Preloads.ContainsKey(label))
                {
                    Preloads[label] = keepLoaded;
                }
                else
                {
                    if (keepLoaded) // only overwrite if keepLoaded = true. All "keepLoaded" take precedence.
                    {
                        Preloads[label] = keepLoaded;
                }
            }
            }

            AsyncOp op = UMAAddressablesRuntimeBridge.LoadAssets(Keys);
            if (op.Status == UMAAddressableOperationStatus.Failed)
            {
                string detail = op.OperationException != null
                    ? op.OperationException.Message
                    : "Addressables call failed but an exception was not specified.";
                throw new UMAInvalidKeyException(
                    "Resources for the following recipes cannot be loaded from the Addressables System: " +
                    KeysToString(string.Empty, Keys) + ". " + detail,
                    Keys);
            }
            op.Completed += ProcessItems;
            if (!keepLoaded)
            {
                string info = "";
                foreach (string s in Keys)
                {
                    info += s + "; ";
                }

                LoadedItems.Add(new CachedOp(op, new List<string>(Keys), info));
            }
            return op;
        }

        // It appears that Addressables can now call this function on an invalid result.
        // We need to ensure that the operation succeeded, and that the result value is not null
        private void ProcessItems(AsyncOp Op)
        {
			if (Op.IsDone && Op.Status == UMAAddressableOperationStatus.Succeeded)
            {
                if (Op.Result != null)
                {
                    foreach (var o in Op.Result)
                    {
                        ProcessNewItem(o, true, false);
                    }
                    PostProcessItems(Op);
                }
            }
        }

        private void PostProcessItems(AsyncOp Op)
        {
            foreach (var o in Op.Result)
            {
                PostProcessItem(o);
            }
        }

		private void PostProcessItem(UnityEngine.Object o) {
			if (o is OverlayDataAsset) {
				var od = (OverlayDataAsset)o;
				if (od.textureList != null && od.textureNames != null) {
					for (int i = 0; i < od.textureList.Length; i++) {
						if (i >= od.textureNames.Length) break;
						if (od.textureList[i] == null && !string.IsNullOrEmpty(od.textureNames[i])) {
                            od.textureList[i] = GetAsset<Texture2D>(od.textureNames[i]);
                        }
                    }
                }
            }

            if (o is SlotDataAsset)
            {
                var sd = (SlotDataAsset)o;
                if (sd.SlotProcessed != null || sd.CharacterCompleted != null)
                {
                    //Debug.Log("[UMAAssetIndexer] PostProcessing SlotDataAsset UVAttachedItemLauncher for slot '" + sd.slotName + "'.");
                    UnityEventBase evt = sd.SlotProcessed;
                    int count = evt.GetPersistentEventCount();
                    if (count == 0)
                    {
                        evt = sd.CharacterCompleted;
                        count = evt.GetPersistentEventCount();
                    }
                    for (int i = 0; i < count; i++)
                    {
                        var target = evt.GetPersistentTarget(i) as GameObject;
                        if (target == null)
                        {
                            Debug.LogWarning($"[UMAAssetIndexer] Null target GameObject for SlotDataAsset '{sd.slotName}' event index {i}.");
                            continue;
                        }
                        try
                        {
                            var uvItem = target.GetComponent<UMAUVAttachedItemLauncher>();
                            if (uvItem == null)
                            {
                                Debug.LogWarning($"[UMAAssetIndexer] No UMAUVAttachedItemLauncher found on '{target.name}' for slot '{sd.slotName}'.");
                                continue;
                            }
                            var mrs = target.GetComponentsInChildren<MeshRenderer>();
                            if (mrs.Length == 0)
                            {
                                Debug.LogWarning($"[UMAAssetIndexer] No MeshRenderers found under '{target.name}' for slot '{sd.slotName}'.");
                                continue;
                            }
                            for (int j = 0; j < mrs.Length; j++)
                            {
                                var mr = mrs[j];
                                if (mr == null)
                                {
                                    Debug.LogWarning($"[UMAAssetIndexer] Null MeshRenderer element {j} under '{target.name}' for slot '{sd.slotName}'.");
                                    continue;
                                }

                                var materials = mr.sharedMaterials;
                                if (materials != null)
                                {
                                    for (int k = 0; k < materials.Length; k++)
                                    {
                                        var mat = materials[k];
                                        if (mat == null)
                                        {
                                            Debug.LogWarning($"[UMAAssetIndexer] MeshRenderer '{mr.name}' has null sharedMaterial (slot '{sd.slotName}').");
                                            continue;
                                        }
                                        var shader = mat.shader;
                                        if (shader == null)
                                        {
                                            Debug.LogWarning($"[UMAAssetIndexer] Material '{mat.name}' on '{mr.name}' has null shader (slot '{sd.slotName}').");
                                            continue;
                                        }
                                        if (shader.name == "Hidden/InternalErrorShader")
                                        {
                                            string original = mat.GetTag("OriginalShader", false, "");
                                            if (string.IsNullOrEmpty(original))
                                            {
                                                Debug.LogWarning($"[UMAAssetIndexer] ErrorShader on '{mr.name}' but OriginalShader tag missing (slot '{sd.slotName}').");
                                                continue;
                                            }
                                            if (original == "Hidden/InternalErrorShader")
                                            {
                                                Debug.LogWarning($"[UMAAssetIndexer] OriginalShader tag on '{mr.name}' is also ErrorShader (slot '{sd.slotName}'). Not sure how this happened. (Manual rebuild maybe?)");
                                                continue;
                                            }
                                            var restored = Shader.Find(original);
                                            if (restored != null)
                                            {
                                                mat.shader = restored;
                                                Debug.Log($"[UMAAssetIndexer] Restored shader '{original}' on material '{mat.name}' for slot '{sd.slotName}'.");
                                            }
                                            else
                                            {
                                                Debug.LogWarning($"[UMAAssetIndexer] Failed to find original shader '{original}' for material '{mat.name}' (slot '{sd.slotName}').");
                                            }
                                        }
                                    }
                                }


                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Error processing SlotDataAsset UVAttachedItemLauncher for slot '{sd.slotName}' on GameObject '{target.name}': {e.Message}");
                        }
                    }
                }
            }
        }

#endif

        private void RemoveItem(UnityEngine.Object ob)
        {
            if (!IsIndexedType(ob.GetType()))
            {
                return;
            }

            System.Type ot = ob.GetType();
            System.Type theType = TypeToLookup[ot];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            AssetItem ai = null;
            string Name = AssetItem.GetEvilName(ob);

            if (TypeDic.ContainsKey(Name))
            {
                ai = TypeDic[Name];
                TypeDic.Remove(Name);
            }
            if (GuidTypes.ContainsKey(Name))
            {
                GuidTypes.Remove(Name);
            }
#if UNITY_EDITOR
            if (ai != null)
            {
                SerializedItems.Remove(ai);
            }
            ForceSave();
            RebuildIndex();
#endif
        }

        public void ProcessNewItem(UnityEngine.Object result, bool isAddressable, bool keepLoaded)
        {
            if (!IsIndexedType(result.GetType())) // JRRM
            {
                return;
            }

            DebugLog("Processing new item: " + result.name + " of type " + result.GetType().ToString());
            AssetItem resultItem = GetAssetItemForObject(result);
            if (resultItem == null)
            {
                DebugLog("  Creating new item: " + result.name + " of type " + result.GetType().ToString());
                resultItem = new AssetItem(result.GetType(), result);
                resultItem.IsAddressable = isAddressable;
                resultItem.IsAlwaysLoaded = keepLoaded;
                AddAssetItem(resultItem, noDirty:true);

                resultItem._SerializedItem = result;
                resultItem.AddReference();
            }
            else
            {
                if (resultItem._SerializedItem == null)
                {
                    DebugLog("  Adding reference to index item: " + result.name + " of type " + result.GetType().ToString());
                }
                else
                {
                    DebugLog("  Updating reference to index item: " + result.name + " of type " + result.GetType().ToString());
                }
                if (keepLoaded)
                {
                    resultItem.IsAlwaysLoaded = keepLoaded;
                }

                resultItem._SerializedItem = result;
                resultItem.AddReference();
            }

            if (result is UMAMaterial um)
            {
                if (um.material.shader == null)
                {
                    // if the shader has been stripped, then we need to reset it.
                    um.material.shader = Shader.Find(um.ShaderName);
                }
            }
            if (result is UMAWardrobeRecipe)
            {
                AddRaceRecipe(result as UMAWardrobeRecipe);
            }
            else if (result is SlotDataAsset)
            {
            }
            else if (result is OverlayDataAsset)
            {
                OverlayDataAsset od = result as OverlayDataAsset;
                if (od.material == null)
                {
                    if (!string.IsNullOrEmpty(od.materialName))
                    {
                        od.material = GetAsset<UMAMaterial>(od.materialName);
                    }
                }
            }
        }

        public int ResetStrippedShaders()
        {
#if UNITY_EDITOR
            int totcount = 0;
            var slots = GetAllAssets<SlotDataAsset>();
            foreach (var slot in slots)
            {
                if (slot == null)
                {
                    Debug.LogError("Null slot found in index!");
                    continue;
                }

                if (slot.SlotProcessed != null || slot.CharacterCompleted != null)
                {
                    //Debug.Log("[UMAAssetIndexer] PostProcessing SlotDataAsset UVAttachedItemLauncher for slot '" + slot.slotName + "'.");
                    UnityEventBase evt = slot.SlotProcessed;
                    int count = evt.GetPersistentEventCount();
                    if (count == 0)
                    {
                        evt = slot.CharacterCompleted;
                        count = evt.GetPersistentEventCount();
                    }
                    for (int i = 0; i < count; i++)
                    {
                        UnityEngine.Object target = evt.GetPersistentTarget(i);
                        var uvItem = target as UMAUVAttachedItemLauncher;
                        if (uvItem != null)
                        {
                            GameObject prefab = uvItem.prefab;
                            MeshRenderer[] mrs = prefab.GetComponentsInChildren<MeshRenderer>();
                            if (mrs != null)
                            {
                                if (mrs.Length == 0) continue;
                                for (int j = 0; j < mrs.Length; j++)
                                {
                                    MeshRenderer mr = mrs[j];
                                    Material mat = mr.sharedMaterial;
                                    if (mat.shader.name == "Hidden/InternalErrorShader")
                                    {
                                        string shaderName = mat.GetTag("OriginalShader", false, "");
                                        if (!string.IsNullOrEmpty(shaderName))
                                        {
                                            Shader s = Shader.Find(shaderName);
                                            if (s != null)
                                            {
                                                mat.shader = s;
                                                totcount++;
                                            }
                                            else
                                            {
                                                Debug.LogError("Unable to find shader " + shaderName + " for material " + mat.name + " on slot " + slot.name);
                                            }
                                        }
                                    }
                                }
                            }

                        }
                    }
                }
            }
            return totcount;
#else
            return 0;
#endif
        }

        public void PostBuildMaterialFixup()
        {
#if UNITY_EDITOR
            var slots = GetAllAssets<SlotDataAsset>();
            var overlays = GetAllAssets<OverlayDataAsset>();
            var umaMaterials = GetAllAssets<UMAMaterial>();

            // if we stripped the shaders from the materials, we need to look them up
            // and reassign them here.
            for (int i = 0; i < umaMaterials.Count; i++)
            {
                UMAMaterial um = umaMaterials[i];
                if (um.material == null)
                {
                    if (!string.IsNullOrEmpty(um.MaterialName))
                    {
                        var guids = AssetDatabase.FindAssets("t:Material " + um.MaterialName);
                        if (guids != null && guids.Length > 0)
                        {
                            string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                            um.material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                            EditorUtility.SetDirty(um);
                        }
                    }
                }

                if (um.material != null && um.material.shader == null)
                    {
                        um.material.shader = Shader.Find(um.ShaderName);
                        if (um.material.shader == null)
                        {
                            Debug.LogError("Unable to find shader " + um.ShaderName + " on UMAMaterial " + um.name);
                        }
                        else
                        {
                            // Shader was found. We need to resave the material with the correct shader
                            EditorUtility.SetDirty(um);
                        }
                    }
                }

            for (int i = 0; i < slots.Count; i++)
            {
                SlotDataAsset sd = slots[i];
            }
            for (int i = 0; i < overlays.Count; i++)
            {
                OverlayDataAsset od = overlays[i];
                if (od.material == null)
                {
                    if (!string.IsNullOrEmpty(od.materialName))
                    {
                        od.material = GetAsset<UMAMaterial>(od.materialName);
                        if (od.material == null)
                        {
                            Debug.LogWarning("Unable to find material '" + od.materialName + "' for overlay: " + od.name);
                        }
                        EditorUtility.SetDirty(od);
                    }
                    else
                    {
                        Debug.LogWarning("Material name is null on overlay: " + od.name);
                    }
                }
            }
            ForceSave();
#endif
        }
#if UMA_ADDRESSABLES
        private void ReleaseAddressableReference(UnityEngine.Object obj)
        {
            if (obj == null || !TypeToLookup.ContainsKey(obj.GetType()))
            {
                return;
            }

            System.Type indexedType = TypeToLookup[obj.GetType()];
            Dictionary<string, AssetItem> typeDictionary = GetAssetDictionary(indexedType);
            string itemName = AssetItem.GetEvilName(obj);

            if (typeDictionary.TryGetValue(itemName, out AssetItem assetItem))
            {
                assetItem.ReleaseItem();
            }
        }

        public void Unload(AsyncOp AssetOperation)
        {
#if SUPER_LOGGING
            Debug.Log("Unloading AsyncOperationHandle<> in Indexer.Unload()");
#endif
            // Remove our bookkeeping before releasing the handle. A released handle can
            // become invalid immediately, but its value is still sufficient to find the
            // CachedOp while it is valid.
            LoadedItems.RemoveAll(x => x.Operation.Equals(AssetOperation));

            if (!AssetOperation.IsValid())
            {
                return;
            }

            if (AssetOperation.Status == UMAAddressableOperationStatus.Succeeded && AssetOperation.Result != null)
            {
                foreach(UnityEngine.Object obj in AssetOperation.Result)
                {
                    ReleaseAddressableReference(obj);
                }
            }

            UMAAddressablesRuntimeBridge.Release(AssetOperation);
        }

        public void UnloadAll(bool forceResourceUnload)
		{

            foreach (CachedOp op in LoadedItems)
			{
				if (op.Operation.IsValid())
                {
				    UMAAddressablesRuntimeBridge.Release(op.Operation);
                }
			}
			Dictionary<string, AssetItem> SlotDic = GetAssetDictionary(typeof(SlotDataAsset));
			Dictionary<string, AssetItem> OverlayDic = GetAssetDictionary(typeof(OverlayDataAsset));

			foreach (AssetItem ai in SlotDic.Values)
			{
				if ((ai._SerializedItem != null && ai.IsAddressable && ai.IsAlwaysLoaded == false) || ai.Ignore)
				{
					ai.ReleaseItem();
                    ai.ReferenceCount = 0;
				}
			}

            // Preloads is tracking if a loaded item is "keep" or not.
            // After freeing everything, we really only need to know about the "keeps".
            // This is necessary, because it's possible to request to "keep" something in one call
            // and NOT keep it in another call. In this case, the previous "Keep" needs to be kept, so
            // we can honor the keep.
            //
			// cheesiest cheap way to clear the Preloads
			Dictionary<string, bool> newPreloads = new Dictionary<string, bool>();
			foreach(KeyValuePair<string,bool> kvp in Preloads)
			{
				if (kvp.Value == true)
                {
					newPreloads.Add(kvp.Key, kvp.Value);
			}
            }
			Preloads = newPreloads;

			foreach (AssetItem ai in OverlayDic.Values)
			{
				if ((ai._SerializedItem != null && ai.IsAddressable && ai.IsAlwaysLoaded == false) || ai.Ignore)
				{
					ai.ReleaseItem();
                    ai.ReferenceCount = 0;
				}
			}
			LoadedItems.Clear();
			if (forceResourceUnload)
				{
					Resources.UnloadUnusedAssets();
				}
		}
#endif
#endregion

        #region Add Remove Assets

#if UNITY_EDITOR

        public void AddIfIndexed(UnityEngine.Object o)
        {
            System.Type type = o.GetType();
            if (IsIndexedType(type))
            {
                EvilAddAsset(type, o);
            }
        }

        public void RemoveIfIndexed(UnityEngine.Object o, bool refresh)
        {
            RemoveAsset(o.GetType(), AssetItem.GetEvilName(o),refresh);
        }

        public void RecursiveScanFoldersForAssets(string path)
        {
            var assetFiles = System.IO.Directory.GetFiles(path);

            for (int i = 0; i < assetFiles.Length; i++)
            {
                string assetFile = assetFiles[i];
                string Extension = System.IO.Path.GetExtension(assetFile).ToLower();
                if (Extension == ".asset" || Extension == ".controller" || Extension == ".txt")
                {
                    UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(assetFile);

                    if (o)
                    {
                        AddIfIndexed(o);
                    }
                }
            }
            string[] array = System.IO.Directory.GetDirectories(path);
            for (int i = 0; i < array.Length; i++)
            {
                string subFolder = array[i];
                RecursiveScanFoldersForAssets(subFolder.Replace('\\', '/'));
            }
        }

        public void RecursiveScanFoldersForRemovingAssets(string path, bool topLevel = true)
        {
            var assetFiles = System.IO.Directory.GetFiles(path);

            for (int i = 0; i < assetFiles.Length; i++)
            {
                string assetFile = assetFiles[i];
                string Extension = System.IO.Path.GetExtension(assetFile).ToLower();
                if (Extension == ".asset" || Extension == ".controller" || Extension == ".txt")
                {
                    UnityEngine.Object o = AssetDatabase.LoadMainAssetAtPath(assetFile);

                    if (o)
                    {
                        RemoveIfIndexed(o,false);
                    }
                }
            }
            string[] array = System.IO.Directory.GetDirectories(path);
            for (int i = 0; i < array.Length; i++)
            {
                string subFolder = array[i];
                RecursiveScanFoldersForRemovingAssets(subFolder.Replace('\\', '/'), false);
            }
            if (topLevel)
            {
                // We need to force a save here, because the indexer is not dirty.
                ForceSave();
                RebuildIndex();
            }
        }
#endif
        /// <summary>
        /// Adds an asset to the index. Does NOT save the asset! you must do that separately.
        /// </summary>
        /// <param name="type">System Type of the object to add.</param>
        /// <param name="name">Name for the object.</param>
        /// <param name="path">Path to the object.</param>
        /// <param name="o">The Object to add.</param>
        /// <param name="skipBundleCheck">Option to skip checking Asset Bundles.</param>
        public void AddAsset(System.Type type, string name, string path, UnityEngine.Object o)
        {
            if (o == null)
            {
#if UNITY_EDITOR
                if (Debug.isDebugBuild)
                {
                    Debug.Log("Skipping null item");
                }
#endif
                return;
            }
            if (type == null)
            {
                type = o.GetType();
            }

            AssetItem ai = new AssetItem(type, name, path, o);
            AddAssetItem(ai);
        }


        //System.Diagnostics.Stopwatch addtoracelookup = new System.Diagnostics.Stopwatch();
        //System.Diagnostics.Stopwatch getAddrInfo = new System.Diagnostics.Stopwatch();

        /// <summary>
        /// Adds an asset to the index. If the name already exists, it is not added. (Should we do this, or replace it?)
        /// </summary>
        /// <param name="ai"></param>
        /// <param name="SkipBundleCheck"></param>
        /// <returns>Whether the asset was added or not.</returns>
        public bool AddAssetItem(AssetItem ai, bool noDirty = false)
        {
            try
            {
                Dictionary<string, AssetItem> TypeDic;
                bool found = GetTypeDictionary(ai, out TypeDic);
                if (!found)
                {
                    return false;
                }

                //addtoracelookup.Start();
                if (ai._Type == typeof(UMAWardrobeRecipe))
                {
                    AddToRaceLookup(ai._SerializedItem as UMAWardrobeRecipe);
                }
                //addtoracelookup.Stop();

#if UNITY_EDITOR
                if (string.IsNullOrWhiteSpace(ai._Name))
                {
                    throw new Exception("Invalid name on Asset type " + ai._Type.ToString() + " - asset is: " + ai.Item.name);
                }
                if (ai.IsAddressable || ai.Ignore)
                {
                    ai._SerializedItem = null;
                }
#if UMA_ADDRESSABLES
                if (UMAAddressableEditorBridge.TryGetAddressableInfo(ai._Guid, out UMAAddressableEditorBridge.Info ainfo))
                {
                    ai.IsAddressable = true;
                    ai.AddressableAddress = ainfo.AddressableAddress;
                    ai.AddressableGroup = ainfo.AddressableGroup;
                    ai.AddressableLabels = ainfo.AddressableLabels;
                }
#endif
                if (!string.IsNullOrEmpty(ai._Guid))
                {
                    AddToGUIDTypes(ai);
                }
#endif
                if (ai._SerializedItem != null)
                {
                    if (ai._SerializedItem is IUMAIndexOptions)
                    {
                        var iso = ai._SerializedItem as IUMAIndexOptions;
                        if (iso.ForceKeep)
                        {
                            ai.IsAlwaysLoaded = true;
                        }
                    }
                }

                AddToTypeDictionary(ai, TypeDic);
            }
#if !UNITY_EDITOR
            catch
            {
                // this is onyl here to stop compiler warnings
            }
#else
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning("Exception in UMAAssetIndexer.AddAssetItem: " + ex.StackTrace);
            }
#endif
            if (noDirty == false)
            {
#if UNITY_EDITOR
                EditorUtility.SetDirty(this);
#endif
            }
            return true;
        }

        private void AddToTypeDictionary(AssetItem ai, Dictionary<string, AssetItem> TypeDic)
        {
            try
            {
                if (ai.Index == -1)
                {
                    ai.Index = SerializedItems.Count;
                    SerializedItems.Add(ai);
                }
                SerializedItems[ai.Index] = ai;

                if (!TypeDic.ContainsKey(ai._Name))
                {
                    TypeDic.Add(ai._Name, ai);
                }
                else
                {
                    // New:  update existing items. This will allow for mods.
                    TypeDic[ai._Name] = ai;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void AddToGUIDTypes(AssetItem ai)
        {
            try
            {
            if (!GuidTypes.ContainsKey(ai._Guid))
            {
                GuidTypes.Add(ai._Guid, ai);
            }
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private bool AlreadyHasItem(AssetItem ai, Dictionary<string, AssetItem> typeDic)
        {
            try
            {
                // Get out if we already have it.
                if (typeDic.ContainsKey(ai._Name))
                {
                    return true;
                }
                return false;
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                return false;
            }
        }

        private bool GetTypeDictionary(AssetItem ai, out Dictionary<string, AssetItem> TypeDic)
        {
            try
            {
                TypeDic = null;
                if (ai._SerializedItem)
                if (ai._Type == null)
                {
                    // this is an unindexed type. How did we get here?
                    return false;
                }
                if (!TypeToLookup.ContainsKey(ai._Type))
                {
                    Debug.LogError("Unable to get Lookup Type for Type: " + ai._Type.ToString() + " for Object " + ai._Name);
                    return false;
                }

                System.Type theType = TypeToLookup[ai._Type];
                TypeDic = GetAssetDictionary(theType);
                if (TypeDic == null)
                {
                    if (Debug.isDebugBuild)
                    {
                        Debug.Log("Unable to add asset item!. Unable to get Type Dictionary of type " + theType.ToString() + "For object " + ai._Name);
                    }
                    return false;
                }
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                TypeDic = null;
                return false;
            }
        }


        /// <summary>
        /// If we added a new AssetItem that is a Wardrobe Recipe, then it needs to be added to the tables.
        /// </summary>
        /// <param name="uwr"></param>
        private void AddToRaceLookup(UMAWardrobeRecipe uwr)
        {
            if (uwr == null)
            {
                return;
            }

            for (int i = 0; i < uwr.compatibleRaces.Count; i++)
            {
                string raceName = uwr.compatibleRaces[i];
                if (!raceRecipes.ContainsKey(raceName))
                {
                    raceRecipes.Add(raceName, new SlotRecipes());
                }
                SlotRecipes sl = raceRecipes[raceName];
                if (!sl.ContainsKey(uwr.wardrobeSlot))
                {
                    sl.Add(uwr.wardrobeSlot, new List<UMATextRecipe>());
                }
                List<UMATextRecipe> recipes = sl[uwr.wardrobeSlot];
                if (recipes.Contains(uwr)) // I'm hoping this function isn't called much outside of updates, editor.
                {
                    continue;
                }

                recipes.Add(uwr);
            }
        }

        public void ClearItem(UnityEngine.Object obj)
        {

        }

        /// <summary>
        /// releases an asset an asset reference
        /// </summary>
        /// <param name="type"></param>
        /// <param name="Name"></param>
        public void ReleaseReference(UnityEngine.Object obj)
        {
            if (obj == null)
            {
                return;
            }

            string Name = AssetItem.GetEvilName(obj);

            // Leave if this is an unreferenced type - for example, a texture (etc).
            // This can happen because these are referenced by the Overlay.
            if (!TypeToLookup.ContainsKey(obj.GetType()))
            {
                return;
            }

            System.Type theType = TypeToLookup[obj.GetType()];

            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);

            if (TypeDic.ContainsKey(Name))
            {
                AssetItem ai = TypeDic[Name];
                ai.FreeReference();
            }
        }


#if UNITY_EDITOR

        public AssetItem FromGuid(string GUID)
        {
            if (GuidTypes.ContainsKey(GUID))
            {
                return GuidTypes[GUID];
            }
            return null;
        }
        /// <summary>
        /// This is the evil version of AddAsset. This version cares not for the good of the project, nor
        /// does it care about readability, expandibility, and indeed, hates goodness with every beat of it's
        /// tiny evil shrivelled heart.
        /// I started going down the good path - I created an interface to get the name info, added it to all the
        /// classes. Then we ran into RuntimeAnimatorController. I would have had to wrap it. And Visual Studio kept
        /// complaining about the interface, even though Unity thought it was OK.
        ///
        /// So in the end, good was defeated. And would never raise it's sword in the pursuit of chivalry again.
        ///
        /// And EvilAddAsset doesn't save either. You have to do that manually.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="o"></param>
        /// <returns>Whether the Asset was added or not.</returns>
        public bool EvilAddAsset(System.Type type, UnityEngine.Object o)
        {
            AssetItem ai = null;
            ai = new AssetItem(TypeToLookup[type], o);
            ai._Path = AssetDatabase.GetAssetPath(o.GetEntityId());
            return AddAssetItem(ai);
        }

        public void RemoveAsset(AssetItem ai, bool compressAndSave = true)
        {
            if (ai.Index != -1)
            {
                if (ai.Index >= SerializedItems.Count)
                {
                    Debug.Log($"Out of range index {ai.Index} removing asset {ai.EvilName} ");
                }
                else
                {
                    SerializedItems[ai.Index] = null;
                    if (compressAndSave)
                    {
                        CompressNulls();
                        RebuildIndex();
                        ForceSave();
                    }
                }
            }
        }

		public void RemoveAssetsComplete() {
			CompressNulls();
			RebuildIndex();
			ForceSave();
		}


        /// <summary>
        /// Removes an asset from the index
        /// </summary>
        /// <param name="type"></param>
        /// <param name="Name"></param>
        public void RemoveAsset(System.Type type, string Name, bool refresh = true)
        {
            System.Type theType = TypeToLookup[type];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            if (TypeDic.ContainsKey(Name))
            {
                AssetItem ai = TypeDic[Name];
                if (ai.Index != -1)
                {
                    SerializedItems[ai.Index] = null;
                }
                TypeDic.Remove(Name);
                if (GuidTypes.ContainsKey(ai._Guid))
                {
                    GuidTypes.Remove(ai._Guid);
                }
                if (theType == typeof(UMAWardrobeRecipe))
                {
                    // remove it from the race lookup.
                    foreach (SlotRecipes sl in raceRecipes.Values)
                    {
                        foreach (List<UMATextRecipe> recipes in sl.Values)
                        {
                            recipes.Remove(ai.Item as UMATextRecipe);
                        }
                    }
                }
                if (refresh)
                {
                    CompressNulls();
                    ForceSave();
                    RebuildIndex();
                }
            }
        }

        // Permanently delete the item from the filesystem.
        public void DeleteAsset(System.Type type, string Name)
        {
            System.Type theType = TypeToLookup[type];
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(theType);
            if (TypeDic.ContainsKey(Name))
            {
                AssetItem ai = TypeDic[Name];
                TypeDic.Remove(Name);
                if (GuidTypes.ContainsKey(ai._Guid))
                {
                    GuidTypes.Remove(ai._Guid);
                }
                if (theType == typeof(UMAWardrobeRecipe))
                {
                    // remove it from the race lookup.
                    foreach (SlotRecipes sl in raceRecipes.Values)
                    {
                        foreach (List<UMATextRecipe> recipes in sl.Values)
                        {
                            recipes.Remove(ai.Item as UMATextRecipe);
                        }
                    }
                }
                File.Delete(ai._Path);
            }
        }

#endif
#endregion

            #region Maintenance
#if UMA_ADDRESSABLES
#if UNITY_EDITOR
        public void ClearAddressableFlags()
        {
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                ai.IsAddressable = false;
            }
            UpdateSerializedDictionaryItems();
            ForceSave();
        }

        public void RemoveUnlabelledAssetsForType(Type type)
        {
            // For each item of this type, if it is addressable, and has no labels, remove it from the index.
            Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(type);
            List<string> toRemove = new List<string>();
            foreach (var kvp in TypeDic)
            {
                AssetItem ai = kvp.Value;
                if (ai.IsAddressable)
                {
                    if (ai.AddressableLabels == null || ai.AddressableLabels.Length == 0)
                    {
                        toRemove.Add(kvp.Key);
                    }
                }
            }
            foreach (string s in toRemove)
            {
                RemoveAsset(type, s);
            }
        }
#endif
#endif
        /// <summary>
        /// Updates the dictionaries from this list.
        /// Used when restoring items after modification, or after deserialization.
        /// </summary>
        public void UpdateSerializedDictionaryItems()
        {
#if UNITY_EDITOR
            CompressNulls();
#endif
            ClearDictionaries();
            //DebugSerialization("Updating serialized Dictionary Items");
            if (SerializedItems == null)
            {
                //DebugSerialization("Serialized Items is null");
                return;
            }
            if (SerializedItems.Count == 0)
            {
                //DebugSerialization("Serialized Items is empty!!!");
                return;
            }
            // Rebuuild all the lookup tables
            // Lookup by guid
            GuidTypes = new Dictionary<string, AssetItem>();
            // Lookup by type, object name
            RecreateTypeLookups();
            //DebugSerialization($"Adding Items from SerializedItems - size is {SerializedItems.Count}");
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];

                // We null things out when we want to delete them. This prevents it from going back into
                // the dictionary when rebuilt.
                if (ai == null)
                {
                    //DebugSerialization("Skipping null item in SerializedItems");
                    continue;
                }
                //DebugSerialization($"Adding item {ai._Name}");
                AddAssetItem(ai, noDirty: true);
            }
            DebugSerialization("All items added");
        }

#if UNITY_EDITOR
        private void CompressNulls()
        {
            List<AssetItem> compresseditems = new List<AssetItem>();
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                if (ai == null)
                {
                    continue;
                }
                ai.Update();
                ai.Index = compresseditems.Count;
                compresseditems.Add(ai);
            }
            SerializedItems = compresseditems;
            EditorUtility.SetDirty(this);
        }

        public int RemoveDuplicateSerializedItems(bool rebuildIndex = true, bool forceSave = true)
        {
			if(SerializedItems == null || SerializedItems.Count == 0) {
                return 0;
            }

            int removed = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < SerializedItems.Count; i++)
            {
                var ai = SerializedItems[i];
                if (ai == null)
                {
                    continue;
                }

                string typePart = ai._Type != null ? (ai._Type.FullName ?? ai._Type.Name) : "<nulltype>";
                string key;

                if (!string.IsNullOrWhiteSpace(ai._Guid))
                {
                    key = $"guid:{ai._Guid}|type:{typePart}";
                }
                else
                {
                    string namePart = !string.IsNullOrEmpty(ai._Name) ? ai._Name : "<noname>";
                    string pathPart = !string.IsNullOrEmpty(ai._Path) ? ai._Path.Replace('\\', '/').ToLowerInvariant() : "<nopath>";
                    key = $"type:{typePart}|name:{namePart}|path:{pathPart}";
                }

                if (!seen.Add(key))
                {
                    SerializedItems[i] = null;
                    removed++;
                }
            }

            if (removed == 0)
            {
                return 0;
            }

            CompressNulls();

            if (rebuildIndex)
            {
                RebuildIndex();
            }
            else
            {
                UpdateSerializedDictionaryItems();
            }

            if (forceSave)
            {
                ForceSave();
            }

            return removed;
        }
#endif

        private void RecreateTypeLookups()
        {
            for (int i = 0; i < Types.Length; i++)
            {
                Type type = Types[i];
                CreateLookupDictionary(type);
            }
        }

        class recipeEqualityComparer : IEqualityComparer<UMAWardrobeRecipe>
        {
            public bool Equals(UMAWardrobeRecipe b1, UMAWardrobeRecipe b2)
            {
                if (b2 == null && b1 == null)
                {
                    return true;
                }
                else if (b1 == null || b2 == null)
                {
                    return false;
                }
                else if (b1.name == b2.name)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            public int GetHashCode(UMAWardrobeRecipe bx)
            {
                return bx.GetHashCode();
            }
        }

        private recipeEqualityComparer req;

        // public System.Diagnostics.Stopwatch CompatLookup = new System.Diagnostics.Stopwatch();
        //public System.Diagnostics.Stopwatch getAsset = new System.Diagnostics.Stopwatch();
        //public System.Diagnostics.Stopwatch crossCompatLookup = new System.Diagnostics.Stopwatch();


        private void AddRaceRecipe(UMAWardrobeRecipe uwr)
        {

            if (!uwr)
            {
                return;
            }

            Dictionary<string, AssetItem> TypeDic;
            TypeDic = GetAssetDictionary(typeof(RaceData));

            // if (req == null)
            //     req = new recipeEqualityComparer();
            List<string> CompatibleRaces = new List<string>(uwr.compatibleRaces);

            List<string> AdditionalRaces = new List<string>();

            foreach (string s in CompatibleRaces)
            {
                if (!TypeDic.TryGetValue(s, out var _))
                    continue;
                RaceData r = RawGetAsset<RaceData>(s);
                if (r != null && !AdditionalRaces.Contains(r.name))
                {
                    if (r.IsCrossCompatibleWith(s))
                    {
                        if (!AdditionalRaces.Contains(r.name) && !CompatibleRaces.Contains(r.name))
                        {
                            AdditionalRaces.Add(r.name);
                        }
                    }
                }
            }
            CompatibleRaces.AddRange(AdditionalRaces);


            for (int i = 0; i < CompatibleRaces.Count; i++)
            {
                string racename = CompatibleRaces[i];
                if (!raceRecipes.ContainsKey(racename))
                {
                    raceRecipes.Add(racename, new SlotRecipes());
                }
                SlotRecipes sl = raceRecipes[racename];
                if (!sl.ContainsKey(uwr.wardrobeSlot))
                {
                    sl.Add(uwr.wardrobeSlot, new List<UMATextRecipe>());
                }
                if (!sl[uwr.wardrobeSlot].Contains(uwr))//, req))
                {
                    sl[uwr.wardrobeSlot].Add(uwr);
                }
            }
        }

        public void RebuildRaceRecipes()
        {
            raceRecipes.Clear();

            /// Add all the directly assigned items.
            var wardrobe = GetAllAssets<UMAWardrobeRecipe>();

            for (int i = 0; i < wardrobe.Count; i++)
            {
                UMAWardrobeRecipe uwr = wardrobe[i];
                AddRaceRecipe(uwr);
            }
        }

        /// <summary>
        /// Creates a lookup dictionary for a list. Used when reloading after deserialization
        /// </summary>
        /// <param name="type"></param>
        private void CreateLookupDictionary(System.Type type)
        {
            DebugSerialization($"Creating lookup dictionary for type: {type.ToString()}");
            Dictionary<string, AssetItem> dic = new Dictionary<string, AssetItem>();
            if (TypeLookup.ContainsKey(type))
            {
                DebugSerialization($"Dictionary already exists for type: {type.ToString()}");
                TypeLookup[type] = dic;
            }
            else
            {
                DebugSerialization($"Dictionary did not exist for type: {type.ToString()}");
                TypeLookup.Add(type, dic);
            }
        }

        private void DebugSerialization(string msg, bool isClear = false)
        {
#if DEBUG_SERIALIZATION
            DebugSerializationStatic(msg, instanceKey, isClear);
#endif
        }

        private static void DebugSerializationStatic(string msg, string instanceKey = "", bool isClear = false)
        {
#if DEBUG_SERIALIZATION
#if UNITY_EDITOR
            float time = 0;
            try
            {
                time = Time.time;
            }
            catch 
            {
            }

            // get the current stacktrace
            // string stackTrace = Environment.StackTrace;
            // SQLDebugger.LogSerialization(msg, stackTrace , instanceKey, isClear, Time.time);

            Debug.Log("[Serializing] "+msg);
#endif
#endif
        }

        /// <summary>
        /// Builds a list of types and a string to look them up.
        /// </summary>
		public void BuildStringTypes()
        {
            TypeFromString.Clear();
            for (int i = 0; i < Types.Length; i++)
            {
                Type st = Types[i];            
                TypeFromString.Add(st.Name, st);
            }
        }

#if UNITY_EDITOR

        private List<AssetItem> Keeps = new List<AssetItem>();

        public void RebuildLibrary()
        {
            SaveKeeps();
            Clear();
            BuildStringTypes();
            AddEverything(false);
            RestoreKeeps();
            RebuildRaceRecipes();
            ForceSave();
            Resources.UnloadUnusedAssets();
        }

        public Dictionary<string, int> GetCounts()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            foreach (System.Type type in TypeToLookup.Keys)
            {
                Dictionary<string, AssetItem> TypeDic = GetAssetDictionary(type);
                counts.Add(type.Name, TypeDic.Count);
            }
            return counts;
        }

        public void SaveKeeps()
        {
            Keeps.Clear();

            foreach (AssetItem ai in SerializedItems)
            {
                if (ai == null)
                {
                    continue;
                }
                if (ai.IsAlwaysLoaded)
                {
                    Keeps.Add(ai);
                }
            }
        }

        public void RestoreKeeps()
        {
            foreach (AssetItem ai in Keeps)
            {
                AssetItem assetItem = GetAssetItem(ai._Type, ai._Name);
                if (assetItem != null)
                {
                    assetItem.IsAlwaysLoaded = true;
                }
            }
            Keeps.Clear();
        }

        public void AddEverything(bool includeText)
        {
            Debug.Log("Adding everything to the library. This may take a while...");
            Clear(false); 

            List<string> types = new List<string>();
            types.AddRange(TypeFromString.Keys);

            for (int i = 0; i < types.Count; i++)
            {
                string s = types[i];
                System.Type CurrentType = TypeFromString[s];

                if (!includeText)
                {
                    if (IsText(CurrentType))
                    {
                        continue;
                    }
                }
                List<string> FolderFilter = null;
                if (TypeFolderSearch.ContainsKey(s))
                {
                    FolderFilter = TypeFolderSearch[s];
                }

                // AnimatorController and AnimatorOverrideController are processed as "RuntimeAnimatorController"
                if (s != "AnimatorController" && s != "AnimatorOverrideController")
                {
                    AddType(s, CurrentType, FolderFilter);
                }
            }
            Debug.Log("Library rebuild complete. Rebuilding index and saving...");
            ForceSave();
            Debug.Log("Index rebuild complete. Unloading unused assets...");
            Resources.UnloadUnusedAssets();
            Debug.Log("All done.");
        }

        private void AddType(string s, Type CurrentType, List<string> FolderFilter)
        {
            bool ignoreBackups = UMASettings.IgnoreBackupFolders;

            string qualifiedName = CurrentType.AssemblyQualifiedName;
            bool removeUnlabeled = isRemoveUnlabelledType(qualifiedName);

            string[] guids = AssetDatabase.FindAssets("t:" + s);

            for (int i = 0; i < guids.Length; i++)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);

                if (ignoreBackups)
                {
                    if (assetPath.ToLower().Contains("backup"))
                    {
                        continue;
                    }
                }

                // IF we have filters
                if (FolderFilter != null && FolderFilter.Count > 0)
                {
                    // IF the assetpath contains any of the filters, then it passed.
                    // we will add it.
                    // otherwise, go on to the next asset
                    bool filterPassed = false;
                    string fixedPath = assetPath.Replace("\\", "/").ToLowerInvariant();


                    for (int i1 = 0; i1 < FolderFilter.Count; i1++)
                    {
                        string fldr = FolderFilter[i1];
                        string fixedfldr = fldr.Replace("\\", "/").ToLowerInvariant();
                        if (fixedPath.Contains(fixedfldr))
                        {
                            filterPassed = true;
                        }
                    }
                    if (!filterPassed)
                    {
                        continue;
                    }
                }

                string fileName = Path.GetFileName(assetPath);
                EditorUtility.DisplayProgressBar("Adding Items to Global Library.", fileName, ((float)i / (float)guids.Length));

                if (assetPath.ToLower().Contains(".shader"))
                {
                    continue;
                }
                UnityEngine.Object o = AssetDatabase.LoadAssetAtPath(assetPath, CurrentType);
                if (o != null)
                {
                    if (SkipDuplicateType(o, CurrentType))
                    {
                        continue;
                    }
                    
                    if (o is IUMAIndexOptions)
                    {
                        IUMAIndexOptions iso = o as IUMAIndexOptions;
                        if (iso.NoAutoAdd)
                        {
                            continue;
                        }
                    }
#if UMA_VES
					var labels = AssetDatabase.GetLabels(o); 
					if(labels != null) {
                        bool hasExcludedLabel = false;
                        foreach (string excludedLabel in VesUmaLabelMaker.DO_NOT_INCLUDE_LABELS)
                        {
                            for (int labelIndex = 0; labelIndex < labels.Length; labelIndex++)
                            {
                                if (labels[labelIndex] == excludedLabel)
                                {
                                    hasExcludedLabel = true;
                                    break;
                                }
                            }

                            if (hasExcludedLabel)
                            {
                                break;
                            }
                        }

                        if (hasExcludedLabel) {
							// Do not add this item during a library rebuild!
							continue;
						}
					}
#endif
#if UMA_ADDRESSABLES
                    if (removeUnlabeled)
                    {
                        if (!UMAAddressableEditorBridge.TryGetAddressableInfo(guids[i], out UMAAddressableEditorBridge.Info ainfo) ||
                            string.IsNullOrEmpty(ainfo.AddressableLabels))
                        {
                            // if we are removing unlabeled assets, and there are no labels, skip this asset.
                            continue;
                        }
                    }
#endif
                    AssetItem ai = new AssetItem(CurrentType, o);
                    AddAssetItem(ai);
                }
            }
            EditorUtility.ClearProgressBar();
        }


        private static bool IsText(Type CurrentType)
        {
            return CurrentType == typeof(TextAsset);
        }

        private bool SkipDuplicateType(UnityEngine.Object o, Type currentType)
        {
            if (o.GetType() == typeof(UMAWardrobeRecipe) && currentType == typeof(UMATextRecipe))
            {
                return true;
            }

            if (o.GetType() == typeof(UMAWardrobeCollection) && currentType == typeof(UMATextRecipe))
            {
                return true;
            }

            if (o.GetType() == typeof(UMAWardrobeCollection) && currentType == typeof(UMAWardrobeRecipe))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears the index
        /// </summary>
        public void Clear(bool forceSave = true)
        {
            generator = null;
            // Rebuild the tables
            GuidTypes.Clear();
            ClearReferences();
            SerializedItems.Clear();
            RecreateTypeLookups();
            if (forceSave)
            {
                ForceSave();
            }
        }


		public bool IsRemoveableItem(AssetItem ai)
		{
			if (ai._SerializedItem != null)
			{
				if (ai._SerializedItem.GetType() == typeof(SlotDataAsset))
                {
                    return true;
                }

                if (ai._SerializedItem.GetType() == typeof(OverlayDataAsset))
                {
                    return true;
                }
            }
			return false;
		}
        /// <summary>
        /// Adds references to all items by accessing the item property.
        /// This forces Unity to load the item and return a reference to it.
        /// When building, Unity needs the references to the items because we
        /// cannot demand load them without the AssetDatabase.
        /// </summary>
        public void AddReferences()
        {
            // Rebuild the tables
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                if (ai.IsAddressable || ai.Ignore)
                {
                    ai.FreeReference();
                }
                else
                {
                    ai.CacheSerializedItem();
                }
            }
            ForceSave();
        }

        public void UpdateReferences()
        {
            DebugSerialization("Updating references");
            // Rebuild the tables
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                if (ai.IsAddressable || ai.Ignore)
                {
                    ai.FreeReference();
                }
                else
                {
                    ai.CacheSerializedItem();
                }
            }
            ForceSave();
        }

        /// <summary>
        /// This releases items by dereferencing them so they can be
        /// picked up by garbage collection.
        /// This also makes working with the index much faster.
        /// </summary>
        public void ClearReferences()
        {
            DebugSerialization("Clearing references");
            // Rebuild the tables
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                ai.FreeReference();
            }
            ForceSave();
            Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// This releases items by dereferencing them so they can be
        /// picked up by garbage collection.
        /// This also makes working with the index much faster.
        /// </summary>
        public void RemoveReferences()
        {
            DebugSerialization("Removing references");
            // Rebuild the tables
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                ai.FreeReference();
            }
            //UpdateSerializedDictionaryItems();
            ForceSave();
        }


        /// <summary>
        /// Repairs the index. Removes anything that it cannot find.
        /// </summary>
        public void RepairAndCleanup()
        {
            DebugSerialization("Repairing and cleaning up index");

            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                ai.IsAddressable = false;
                ai.AddressableLabels = "";
                ai.AddressableGroup = "";
                ai.AddressableAddress = "";
#if UNITY_EDITOR
#if UMA_ADDRESSABLES
                if (UMAAddressableEditorBridge.TryGetAddressableInfo(ai._Guid, out UMAAddressableEditorBridge.Info ainfo))
                {
                    ai.AddressableAddress = ainfo.AddressableAddress;
                    ai.IsAddressable = true;
                    ai.AddressableGroup = ainfo.AddressableGroup;
                    ai._SerializedItem = null;
                    ai.AddressableLabels = ainfo.AddressableLabels;
                }
                else
#endif
#endif

                if (!ai.IsAssetBundle)
                {
                    // If we already have a reference to the item, let's verify that everything is correct on it.
                    UnityEngine.Object obj = ai.Item;
                    if (obj != null)
                    {
                        ai._Name = ai.EvilName;
                        ai._Path = AssetDatabase.GetAssetPath(obj.GetEntityId());
                        ai._Guid = AssetDatabase.AssetPathToGUID(ai._Path);
                    }
                    else
                    {
                        // Clear out the item reference so we will attempt to fix it if it's broken.
                        ai._SerializedItem = null;
                        // This will attempt to load the item, using the path, guid or name (in that order).
                        // This is in case we didn't have a reference to the item, and it was moved
                        ai.CacheSerializedItem();
                        // If an item can't be found and we didn't ahve a reference to it, then we need to delete it.
                        if (ai._SerializedItem == null)
                        {
                            // Can't be found or loaded
                            // null it out, so it doesn't get added back.
                            SerializedItems[i] = null;
                        }
                        ai.FreeReference();
                    }
                }
            }


            UpdateSerializedDictionaryItems();
            RebuildRaceRecipes();
            ForceSave();
        }

#endif
                        /// <summary>
                        /// returns the entire lookup dictionary for a specific type.
                        /// </summary>
                        /// <param name="type"></param>
                        /// <returns></returns>
        public Dictionary<string, AssetItem> GetAssetDictionary(System.Type type)
        {
            System.Type LookupType = TypeToLookup[type];
            if (TypeLookup.ContainsKey(LookupType) == false)
            {
                DebugSerialization("Creating new dictionary for type: " + LookupType.ToString());
                TypeLookup[LookupType] = new Dictionary<string, AssetItem>();
            }
            return TypeLookup[LookupType];
        }

        public bool IndexIsValid
        {
            get
            {
                if (TypeToLookup == null)
                {
                    return false;
                }
                if (TypeToLookup.Count == 0)
                {
                    return false;
                }

                return false;
            }
        }


#if UNITY_EDITOR
        /// <summary>
        /// Heals the index if possible, if not rebuilds
        /// </summary>
        public void HealIndex(bool AlwaysRebuild = false)
        {
            // do not heal in the editor if we are playing.
            if (Application.isPlaying == true)
            {
                return;
            }

            if (!AlwaysRebuild)
            {
                DebugSerialization("Healing index");
                // See if we can shortcut 
                if (SerializedItems.Count > 0)
                {
                    DebugSerialization("Repairing from serialized items");
                    for (int i = 0; i < SerializedItems.Count; i++)
                    {
                        AssetItem ai = SerializedItems[i];
                        ai._Name = ai.EvilName;
                    }
                    UpdateSerializedDictionaryItems();
                    RebuildRaceRecipes();
                    return;
                }
            }

            DebugSerialization("Healing index through rebuild.");
            SaveKeeps();
            Clear();
            BuildStringTypes();
            AddEverything(false);
            RestoreKeeps();
            Resources.UnloadUnusedAssets();
            ForceSave();
        }
#endif

        /// <summary>
        /// Rebuilds the name indexes by dumping everything back to the list, updating the name, and then rebuilding
        /// the dictionaries.
        /// </summary>
        public void RebuildIndex()
        {
#if UNITY_EDITOR
            CompressNulls();
#endif
            DebugSerialization("Rebuilding index");
            for (int i = 0; i < SerializedItems.Count; i++)
            {
                AssetItem ai = SerializedItems[i];
                if (ai._SerializedItem != null)
                {
                    ai._Name = ai.EvilName;
                }
            }
            ClearDictionaries();
            UpdateSerializedDictionaryItems();
            RebuildRaceRecipes();
        }

        /// <summary>
        /// Clear the type dictionaries
        /// </summary>
        public void ClearDictionaries()
        {
            DebugSerialization("Clearing dictionaries");
            TypeLookup.Clear();
            GuidTypes.Clear();
            raceRecipes.Clear();
        }
#endregion

#region BuildStuff

#if UNITY_EDITOR
        public void PrepareBuild()
        {
            SaveKeeps();
            Clear();
            BuildStringTypes();
            AddEverything(false);
            RestoreKeeps();
            AddReferences();
#if UMA_ADDRESSABLES
            // TODO: Build addressable bundles here.
            // For now, we will leave that in the build script.
#endif
        }

        /// <summary>
        /// This should be called by your build script 
        /// </summary>
        public void ClearMHASlotReferences()
        {
            string[] mhaGUIDS = AssetDatabase.FindAssets("t:MeshHideAsset");
            for (int i = 0; i < mhaGUIDS.Length; i++)
            {
                string guid = mhaGUIDS[i];
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileName(assetPath);
                MeshHideAsset mha = AssetDatabase.LoadAssetAtPath<MeshHideAsset>(assetPath);
                // mha.FreeReference();
                EditorUtility.SetDirty(mha);
#if UNITY_2021_1_OR_NEWER
                AssetDatabase.SaveAssetIfDirty(mha);
#endif
            }
#if !UNITY_2021_1_OR_NEWER
            AssetDatabase.SaveAssets();
#endif
        }
#endif
        #endregion
    }
#if UMA_ADDRESSABLES

    /// <summary>
    /// This exception exists as a separate exception so we can track keys.
    /// </summary>
    public class UMAInvalidKeyException : Exception
    {
        public string Labels { get; private set; }
        public UMAInvalidKeyException()
        {
            Labels = "No Key Specified";
        }

        public UMAInvalidKeyException(string msg) : base(msg)
        {
            Labels = "No Key Specified";
        }
        public UMAInvalidKeyException(string msg, Exception inner) : base(msg,inner)
        {
            Labels = "No Key Specified";
        }

        public UMAInvalidKeyException(string msg, List<string> Keys) : base(msg)
        {
            Labels = UMAAssetIndexer.KeysToString(msg,Keys);
        }
    };
#endif
}
