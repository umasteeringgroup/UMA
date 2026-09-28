using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class DynamicUMADnaSerializationTests
    {
        // Reproduce the old wire format using Unity's serializer, without querying object IDs.
        [Serializable]
        private sealed class LegacyDna
        {
            public Object bDnaAsset;
            public string bDnaAssetName;
            public DNASettings[] bDnaSettings;
        }

        public sealed class LegacyReferenceComponent : MonoBehaviour { }

        private static readonly FieldInfo IndexerField = typeof(UMAAssetIndexer).GetField(
            "theIndexer", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo DnaCacheField = typeof(DynamicUMADnaBase).GetField(
            "DynamicDNADictionary", BindingFlags.Static | BindingFlags.NonPublic);
        private readonly List<Object> objects = new List<Object>();
        private readonly List<string> warnings = new List<string>();
        private object originalIndexer;
        private object originalDnaCache;
        private UMASettings originalSettings;
        private UMAAssetIndexer indexer;
        private DynamicUMADnaAsset asset;

        [SetUp]
        public void SetUp()
        {
            Assert.That(IndexerField, Is.Not.Null);
            Assert.That(DnaCacheField, Is.Not.Null);
            originalIndexer = IndexerField.GetValue(null);
            originalDnaCache = DnaCacheField.GetValue(null);
            originalSettings = UMASettings.instance;
            // Keep tests independent of the project's index/settings and never repair or save them.
            UMASettings.instance = Create<UMASettings>();
            UMASettings.instance.autoRepairIndex = false;
            indexer = Create<UMAAssetIndexer>();
            IndexerField.SetValue(null, indexer);
            asset = Create<DynamicUMADnaAsset>();
            asset.name = "Legacy DNA Serialization Fixture";
            asset.Names = new[] { "height", "width" };
            asset.dnaTypeHash = 123456;
            Register(asset);
            warnings.Clear();
            Application.logMessageReceived += CaptureWarning;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= CaptureWarning;
            IndexerField.SetValue(null, originalIndexer);
            DnaCacheField.SetValue(null, originalDnaCache);
            UMASettings.instance = originalSettings;
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [Test]
        public void SaveStoresAssetNameAndValuesButNeverAnObjectReference()
        {
            var dna = new DynamicUMADna { dnaAsset = asset };
            dna.SetValue("height", 51f / 255f);
            dna.SetValue("width", 204f / 255f);

            string json = UMADna.SaveInstance(dna);

            StringAssert.DoesNotContain("\"bDnaAsset\":", json);
            StringAssert.DoesNotContain("instanceID", json);
            var packed = JsonUtility.FromJson<DynamicUMADna_Byte>(json);
            Assert.That(packed.bDnaAssetName, Is.EqualTo(asset.name));
            Assert.That(packed.bDnaSettings[0].value, Is.EqualTo(51));
            Assert.That(packed.bDnaSettings[1].value, Is.EqualTo(204));
        }

        [TestCase("valid")]
        [TestCase("different-dna")]
        [TestCase("game-object")]
        [TestCase("mono-behaviour")]
        [TestCase("stale")]
        [TestCase("null")]
        public void LegacyReferenceIsIgnoredBeforeDeserializationAndResolvedByName(string reference)
        {
            Object legacyObject = null;
            if (reference == "valid") legacyObject = asset;
            if (reference == "different-dna") legacyObject = Create<DynamicUMADnaAsset>();
            if (reference == "game-object" || reference == "mono-behaviour")
            {
                var go = new GameObject("Unrelated legacy reference");
                objects.Add(go);
                legacyObject = reference == "game-object"
                    ? (Object)go : go.AddComponent<LegacyReferenceComponent>();
            }
            string json = LegacyJson(legacyObject);
            if (reference == "stale")
                json = "{\"bDnaAsset\":{\"instanceID\":2147483647},\"bDnaAssetName\":\"" + asset.name +
                    "\",\"bDnaSettings\":[{\"name\":\"height\",\"value\":51},{\"name\":\"width\",\"value\":204}]}";

            var packed = JsonUtility.FromJson<DynamicUMADna_Byte>(json);
            Assert.That(packed.bDnaAsset, Is.Null, "Even a valid legacy reference must be ignored by the JSON reader.");
            DynamicUMADna loaded = DynamicUMADna.LoadInstance(json);

            AssertDna(loaded);
            Assert.That(warnings, Is.Empty, "Legacy references must not reach Unity's typed object deserializer.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void RoundTripAfterAssetReplacementPreservesEveryByteValueAndResolvesNewInstance()
        {
            asset.Names = new string[256];
            var values = new float[256];
            for (int i = 0; i < values.Length; i++)
            {
                asset.Names[i] = "dna_" + i;
                values[i] = i / 255f;
            }
            var dna = new DynamicUMADna { dnaAsset = asset, Values = values };
            string json = UMADna.SaveInstance(dna);
            string assetName = asset.name;
            string[] names = asset.Names;
            Object.DestroyImmediate(asset);
            asset = Create<DynamicUMADnaAsset>();
            asset.name = assetName;
            asset.Names = names;
            asset.dnaTypeHash = 987654;
            Register(asset);

            var loaded = (DynamicUMADna)UMADna.LoadInstance(typeof(DynamicUMADna), json, null);

            Assert.That(loaded.dnaAsset, Is.SameAs(asset));
            Assert.That(loaded.DNATypeHash, Is.EqualTo(asset.dnaTypeHash));
            Assert.That(loaded.Names, Is.EqualTo(names));
            for (int i = 0; i < values.Length; i++)
                Assert.That(loaded.Values[i], Is.EqualTo(values[i]).Within(0.000001f));
            Assert.That(UMADna.SaveInstance(loaded), Is.EqualTo(json));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void MissingAssetRetainsIdentityAndValuesAcrossSaveUntilItBecomesAvailable()
        {
            string json = LegacyJson(null);
            Register(null);
            ExpectMissingAssetWarnings(asset.name);

            DynamicUMADna loaded = DynamicUMADna.LoadInstance(json);
            Assert.That(loaded.dnaAsset, Is.Null);
            string saved = DynamicUMADna.SaveInstance(loaded);
            Assert.That(JsonUtility.FromJson<DynamicUMADna_Byte>(saved).bDnaAssetName,
                Is.EqualTo(asset.name), "Saving with an unavailable asset must not lose its identity.");

            ExpectMissingAssetWarnings(asset.name);
            DynamicUMADna stillMissing = DynamicUMADna.LoadInstance(saved);
            Assert.That(stillMissing.Values, Is.EqualTo(loaded.Values));
            Register(asset);
            AssertDna(DynamicUMADna.LoadInstance(DynamicUMADna.SaveInstance(stillMissing)));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(null)]
        [TestCase("")]
        public void UnnamedLegacyDnaPreservesValuesWithoutAttemptingNullNameLookup(string name)
        {
            string json = JsonUtility.ToJson(new LegacyDna
            {
                bDnaAssetName = name,
                bDnaSettings = new[] { new DNASettings("height", 51) }
            });
            if (Debug.isDebugBuild)
                LogAssert.Expect(LogType.Warning, "Deserialized DynamicUMADna with no matching asset!");

            DynamicUMADna loaded = DynamicUMADna.LoadInstance(json);

            Assert.That(loaded.dnaAsset, Is.Null);
            Assert.That(loaded.GetValue("height"), Is.EqualTo(51f / 255f).Within(0.000001f));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ResolvedAssetRemapsSavedValuesByNameAndDefaultsNewNames()
        {
            string json = LegacyJson(null);
            asset.Names = new[] { "width", "newDna", "height" };

            DynamicUMADna loaded = DynamicUMADna.LoadInstance(json);

            // Inspect the backing array before Names can lazily validate it itself.
            Assert.That(loaded._names, Is.EqualTo(asset.Names));
            Assert.That(loaded.Values[0], Is.EqualTo(204f / 255f).Within(0.000001f));
            Assert.That(loaded.Values[1], Is.EqualTo(0.5f));
            Assert.That(loaded.Values[2], Is.EqualTo(51f / 255f).Within(0.000001f));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void InMemoryConversionStillSupportsUnindexedAssetReferences()
        {
            Register(null);
            var dna = new DynamicUMADna { dnaAsset = asset };
            dna.SetValue("height", 51f / 255f);
            dna.SetValue("width", 204f / 255f);

            AssertDna(DynamicUMADna_Byte.FromDna(dna).ToDna());
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void DestroyedCachedAssetFallsBackToCurrentIndexedAsset()
        {
            string json = LegacyJson(null);
            DynamicUMADna.LoadInstance(json); // Prime the name cache.
            object cache = DnaCacheField.GetValue(null);
            string name = asset.name;
            Object.DestroyImmediate(asset);
            asset = Create<DynamicUMADnaAsset>();
            asset.name = name;
            asset.Names = new[] { "height", "width" };
            asset.dnaTypeHash = 123456;
            Register(asset);
            DnaCacheField.SetValue(null, cache); // Simulate a cached asset unloaded/replaced later.

            AssertDna(DynamicUMADna.LoadInstance(json));
            Assert.That(warnings, Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PackedRecipeEntryPointsLoadLegacyDnaWithoutReferenceWarnings(bool withRace)
        {
            var go = new GameObject("Stale recipe DNA target");
            objects.Add(go);
            var packed = new List<UMAPackedRecipeBase.UMAPackedDna>
            {
                new UMAPackedRecipeBase.UMAPackedDna
                {
                    dnaType = "DynamicUMADna",
                    dnaTypeHash = asset.dnaTypeHash,
                    packedDna = LegacyJson(go.AddComponent<LegacyReferenceComponent>())
                }
            };

            List<UMADnaBase> loaded = withRace
                ? UMAPackedRecipeBase.UnPackDNA(packed, Create<RaceData>())
                : UMAPackedRecipeBase.UnPackDNA(packed);

            Assert.That(loaded.Count, Is.EqualTo(1));
            AssertDna((DynamicUMADna)loaded[0]);
            Assert.That(warnings, Is.Empty);
        }

        private T Create<T>() where T : ScriptableObject
        {
            T obj = ScriptableObject.CreateInstance<T>();
            obj.hideFlags = HideFlags.HideAndDontSave;
            objects.Add(obj);
            return obj;
        }

        private void Register(DynamicUMADnaAsset dnaAsset)
        {
            indexer.SerializedItems.Clear();
            if (dnaAsset != null)
                indexer.SerializedItems.Add(new AssetItem(typeof(DynamicUMADnaAsset), dnaAsset.name, "", dnaAsset));
            indexer.DoInitialDictionaryLoad();
            DynamicUMADnaBase.StaticInitializeOnLoad();
        }

        private string LegacyJson(Object reference)
        {
            return JsonUtility.ToJson(new LegacyDna
            {
                bDnaAsset = reference,
                bDnaAssetName = asset.name,
                bDnaSettings = new[] { new DNASettings("height", 51), new DNASettings("width", 204) }
            });
        }

        private void AssertDna(DynamicUMADna loaded)
        {
            Assert.That(loaded.dnaAsset, Is.SameAs(asset));
            Assert.That(loaded.dnaAssetName, Is.EqualTo(asset.name));
            Assert.That(loaded.DNATypeHash, Is.EqualTo(asset.dnaTypeHash));
            Assert.That(loaded.Names, Is.EqualTo(new[] { "height", "width" }));
            Assert.That(loaded.GetValue("height"), Is.EqualTo(51f / 255f).Within(0.000001f));
            Assert.That(loaded.GetValue("width"), Is.EqualTo(204f / 255f).Within(0.000001f));
        }

        private static void ExpectMissingAssetWarnings(string name)
        {
            if (!Debug.isDebugBuild) return;
            LogAssert.Expect(LogType.Warning, "DynamicUMADna could not find DNAAsset " + name + "!");
            LogAssert.Expect(LogType.Warning, "Deserialized DynamicUMADna with no matching asset!");
        }

        private void CaptureWarning(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Warning) warnings.Add(message);
        }
    }
}
