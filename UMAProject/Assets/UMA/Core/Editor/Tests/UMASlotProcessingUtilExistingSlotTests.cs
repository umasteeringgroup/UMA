#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMASlotProcessingUtilExistingSlotTests
    {
        private string testFolder;

        [SetUp]
        public void SetUp()
        {
            string folderName = "__UMAExistingSlotLookupTests_" + Guid.NewGuid().ToString("N");
            testFolder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(testFolder))
            {
                AssetDatabase.DeleteAsset(testFolder);
            }
        }

        [Test]
        public void ExistingSlotLookupFindsNonUdimSlotAliasOutsideIntendedFolder()
        {
            const string slotName = "UMA_ExistingSlotLookup_Test";
            string existingPath = testFolder + "/StoredSomewhereElse.asset";

            SlotDataAsset existing = ScriptableObject.CreateInstance<SlotDataAsset>();
            existing.PrepareForAssetPath(existingPath, slotName + "_slot");
            AssetDatabase.CreateAsset(existing, existingPath);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();

            Type lookupType = typeof(UMASlotProcessingUtil).GetNestedType(
                "ExistingSlotAssetLookup",
                BindingFlags.NonPublic);
            Assert.NotNull(lookupType);

            object lookup = Activator.CreateInstance(lookupType, true);
            MethodInfo findMethod = lookupType.GetMethod(
                "Find",
                BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(findMethod);

            var found = findMethod.Invoke(
                lookup,
                new object[]
                {
                    slotName,
                    "Assets/AnUnrelatedDestination/" + slotName + "_slot.asset"
                }) as SlotDataAsset;

            Assert.AreSame(existing, found);
            Assert.AreEqual(existingPath, AssetDatabase.GetAssetPath(found));

            var result = new UMASlotProcessingUtil.SlotBuildResult();
            result.AddSlotWrite(found, true);

            Assert.IsTrue(result.SlotWasReplaced[found]);
            Assert.AreEqual(existingPath, result.SlotWrittenPath[found]);
        }

        [Test]
        public void PersistentSlotObjectNameMatchesFilenameWithoutChangingLogicalSlotName()
        {
            const string logicalName = "UMA30_Body_UDIM1004";
            string path = testFolder + "/" + logicalName + "_slot.asset";
            SlotDataAsset slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.name = logicalName;

            slot.PrepareForAssetPath(path, logicalName);
            AssetDatabase.CreateAsset(slot, path);
            AssetDatabase.SaveAssets();

            SlotDataAsset reloaded = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(path);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded.name, Is.EqualTo(logicalName + "_slot"));
            Assert.That(reloaded.slotName, Is.EqualTo(logicalName));
            Assert.That(reloaded.sourceSlot, Is.EqualTo(logicalName));

            // Rebuilding the same slot must not undo Unity's main-object repair.
            reloaded.PrepareForAssetPath(path, logicalName);
            Assert.That(reloaded.name, Is.EqualTo(logicalName + "_slot"));
            Assert.That(reloaded.slotName, Is.EqualTo(logicalName));
        }
    }

    public sealed class UMASlotProcessingUtilUdimMetadataTests
    {
        private string testFolder;
        private GameObject sourceObject;
        private SkinnedMeshRenderer sourceRenderer;

        [SetUp]
        public void SetUp()
        {
            string folderName =
                "__UMAUdimMetadataTests_" + Guid.NewGuid().ToString("N");
            testFolder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            var sourceMesh = new Mesh { name = "UDIM Source Mesh" };
            AssetDatabase.CreateAsset(sourceMesh, testFolder + "/SourceMesh.asset");

            sourceObject = new GameObject("UDIM Source Renderer");
            sourceRenderer = sourceObject.AddComponent<SkinnedMeshRenderer>();
            sourceRenderer.sharedMesh = sourceMesh;
        }

        [TearDown]
        public void TearDown()
        {
            if (sourceObject != null)
            {
                UnityEngine.Object.DestroyImmediate(sourceObject);
            }
            if (!string.IsNullOrEmpty(testFolder))
            {
                AssetDatabase.DeleteAsset(testFolder);
            }
        }

        [Test]
        public void UdimGroupIdIsStableForTheSameSourceMeshAndCompositeName()
        {
            var parameters = new SlotBuilderParameters
            {
                slotMesh = sourceRenderer,
                slotName = "Human Body"
            };

            MethodInfo buildGroupId = GetMetadataMethod("BuildUdimGroupId");
            string first = (string)buildGroupId.Invoke(null, new object[] { parameters });
            string second = (string)buildGroupId.Invoke(null, new object[] { parameters });

            Assert.That(first, Is.Not.Empty);
            Assert.That(second, Is.EqualTo(first));

            parameters.slotName = "Human Body Variant";
            Assert.That(
                (string)buildGroupId.Invoke(null, new object[] { parameters }),
                Is.Not.EqualTo(first));
        }

        [Test]
        public void UdimMetadataCanBeAssignedAndCleared()
        {
            SlotDataAsset slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                slot.UdimSharedVertexMap = new SlotDataAsset.UdimSeamMap
                {
                    originalIndices = new[] { 1 },
                    localIndices = new[] { 2 }
                };

                MethodInfo setMetadata = GetMetadataMethod("SetUdimMetadata");
                setMetadata.Invoke(
                    null,
                    new object[] { slot, "group-id", "Human Body", 1012, 3 });

                Assert.That(slot.IsUdimMember, Is.True);
                Assert.That(slot.udimGroupId, Is.EqualTo("group-id"));
                Assert.That(slot.udimGroupName, Is.EqualTo("Human Body"));
                Assert.That(slot.udimTileNumber, Is.EqualTo(1012));
                Assert.That(slot.udimSourceSubmeshIndex, Is.EqualTo(3));

                GetMetadataMethod("ClearUdimMetadata").Invoke(
                    null,
                    new object[] { slot });

                Assert.That(slot.IsUdimMember, Is.False);
                Assert.That(slot.udimGroupId, Is.Empty);
                Assert.That(slot.udimGroupName, Is.Empty);
                Assert.That(slot.udimTileNumber, Is.Zero);
                Assert.That(slot.udimSourceSubmeshIndex, Is.EqualTo(-1));
                Assert.That(slot.UdimSharedVertexMap, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(slot);
            }
        }

        [Test]
        public void UdimSeamKeysIncludeDuplicatedVerticesSharedAcrossTiles()
        {
            var tileMembershipByOriginalVertex = new Dictionary<int, HashSet<(int u, int v)>>
            {
                { 0, new HashSet<(int u, int v)> { (0, 0) } },
                { 1, new HashSet<(int u, int v)> { (1, 0) } }
            };

            MethodInfo buildSeamKeys = GetMetadataMethod("BuildUdimSeamKeys");
            var seamKeys = buildSeamKeys.Invoke(
                null,
                new object[]
                {
                    new[] { Vector3.zero, Vector3.zero },
                    tileMembershipByOriginalVertex
                }) as Dictionary<int, int>;

            Assert.NotNull(seamKeys);
            Assert.That(seamKeys[0], Is.EqualTo(0));
            Assert.That(seamKeys[1], Is.EqualTo(0));
        }

        [Test]
        public void AssignCopiesUdimMetadataAndOwnsItsSeamMap()
        {
            SlotDataAsset source = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset destination = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                source.udimGroupId = "group-id";
                source.udimGroupName = "Human Body";
                source.udimTileNumber = 1012;
                source.udimSourceSubmeshIndex = 3;
                source.UdimSharedVertexMap = new SlotDataAsset.UdimSeamMap
                {
                    originalIndices = new[] { 10, 11 },
                    localIndices = new[] { 2, 4 }
                };

                destination.Assign(source);

                Assert.That(destination.udimGroupId, Is.EqualTo(source.udimGroupId));
                Assert.That(destination.udimGroupName, Is.EqualTo(source.udimGroupName));
                Assert.That(destination.udimTileNumber, Is.EqualTo(source.udimTileNumber));
                Assert.That(destination.udimSourceSubmeshIndex, Is.EqualTo(source.udimSourceSubmeshIndex));
                Assert.That(destination.UdimSharedVertexMap.originalIndices, Is.EqualTo(source.UdimSharedVertexMap.originalIndices));
                Assert.That(destination.UdimSharedVertexMap.localIndices, Is.EqualTo(source.UdimSharedVertexMap.localIndices));

                destination.UdimSharedVertexMap.localIndices[0] = 99;
                Assert.That(source.UdimSharedVertexMap.localIndices[0], Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(destination);
            }
        }

        private static MethodInfo GetMetadataMethod(string methodName)
        {
            MethodInfo method = typeof(UMASlotProcessingUtil).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method, "Missing metadata helper " + methodName + ".");
            return method;
        }
    }
}

#endif
