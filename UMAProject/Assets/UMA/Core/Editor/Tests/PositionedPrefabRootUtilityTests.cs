using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class PositionedPrefabRootUtilityTests
    {
        private string testFolder;

        [SetUp]
        public void SetUp()
        {
            testFolder = "Assets/__UMAPositionedPrefabRootTests_" +
                         Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(
                "Assets", testFolder.Substring("Assets/".Length));
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(testFolder) &&
                AssetDatabase.IsValidFolder(testFolder))
            {
                AssetDatabase.DeleteAsset(testFolder);
            }
            AssetDatabase.Refresh();
        }

        [Test]
        public void ConvertPrefabWrapsPositionedContentsAndPreservesGuid()
        {
            const string prefabName = "MountedSword";
            string prefabPath = testFolder + "/" + prefabName + ".prefab";
            string holderPath = testFolder + "/Holder.prefab";
            Vector3 position = new Vector3(0.15f, -0.4f, 0.7f);
            Quaternion rotation = Quaternion.Euler(12f, 34f, 56f);
            Vector3 scale = new Vector3(0.8f, 1.2f, 0.9f);

            GameObject source = new GameObject(prefabName);
            source.transform.localPosition = position;
            source.transform.localRotation = rotation;
            source.transform.localScale = scale;
            source.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 3f);
            GameObject blade = new GameObject("Blade");
            blade.transform.SetParent(source.transform, false);
            blade.transform.localPosition = Vector3.forward;
            PrefabUtility.SaveAsPrefabAsset(source, prefabPath, out bool sourceSaved);
            UnityEngine.Object.DestroyImmediate(source);
            Assert.IsTrue(sourceSaved);

            GameObject originalAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(originalAsset);
            string originalGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                originalAsset, out string originalRootGuid, out long originalRootGameObjectId));
            Assert.AreEqual(originalGuid, originalRootGuid);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                originalAsset.transform, out string originalTransformGuid, out long originalRootTransformId));
            Assert.AreEqual(originalGuid, originalTransformGuid);
            BoxCollider originalCollider = originalAsset.GetComponent<BoxCollider>();
            Assert.IsNotNull(originalCollider);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                originalCollider, out string originalColliderGuid, out long originalColliderId));
            Assert.AreEqual(originalGuid, originalColliderGuid);

            GameObject holder = new GameObject("Holder");
            GameObject nested = PrefabUtility.InstantiatePrefab(originalAsset) as GameObject;
            Assert.IsNotNull(nested);
            nested.transform.SetParent(holder.transform, false);
            PrefabUtility.SaveAsPrefabAsset(holder, holderPath, out bool holderSaved);
            UnityEngine.Object.DestroyImmediate(holder);
            Assert.IsTrue(holderSaved);

            PositionedPrefabConversionResult result =
                PositionedPrefabRootUtility.ConvertPrefab(prefabPath);

            Assert.AreEqual(PositionedPrefabConversionStatus.Converted, result.Status, result.Message);
            Assert.AreEqual(originalGuid, AssetDatabase.AssetPathToGUID(prefabPath));
            Assert.AreNotEqual(originalGuid, AssetDatabase.AssetPathToGUID(result.PositionedPath));

            GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.IsNotNull(wrapper);
            Assert.AreEqual(prefabName, wrapper.name);
            Assert.IsTrue(PositionedPrefabRootUtility.HasIdentityRoot(wrapper.transform));
            Assert.AreEqual(1, wrapper.transform.childCount);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                wrapper, out string wrapperGuid, out long wrapperGameObjectId));
            Assert.AreEqual(originalGuid, wrapperGuid);
            Assert.AreEqual(originalRootGameObjectId, wrapperGameObjectId);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                wrapper.transform, out string wrapperTransformGuid, out long wrapperTransformId));
            Assert.AreEqual(originalGuid, wrapperTransformGuid);
            Assert.AreEqual(originalRootTransformId, wrapperTransformId);

            Transform positioned = wrapper.transform.GetChild(0);
            Assert.AreEqual(prefabName + "_positioned", positioned.name);
            Assert.Less((positioned.localPosition - position).sqrMagnitude, 0.0000000001f);
            Assert.GreaterOrEqual(Mathf.Abs(Quaternion.Dot(positioned.localRotation, rotation)), 0.9999999f);
            Assert.Less((positioned.localScale - scale).sqrMagnitude, 0.0000000001f);
            BoxCollider positionedCollider = positioned.GetComponent<BoxCollider>();
            Assert.IsNotNull(positionedCollider);
            Assert.IsTrue(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                positionedCollider, out string positionedColliderGuid, out long positionedColliderId));
            Assert.AreEqual(originalGuid, positionedColliderGuid);
            Assert.AreEqual(originalColliderId, positionedColliderId);
            Assert.IsNotNull(positioned.Find("Blade"));

            GameObject positionedAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(result.PositionedPath);
            Assert.IsNotNull(positionedAsset);
            Assert.AreEqual(prefabName + "_positioned", positionedAsset.name);
            Assert.Less(
                (positionedAsset.transform.localPosition - position).sqrMagnitude,
                0.0000000001f);

            CollectionAssert.Contains(
                AssetDatabase.GetDependencies(holderPath, false), prefabPath,
                "A Prefab that referenced the old GUID must still reference the wrapper at the original path.");
            CollectionAssert.DoesNotContain(
                AssetDatabase.GetDependencies(prefabPath, false), result.PositionedPath,
                "The wrapper must contain unpacked contents, not a nested positioned Prefab instance.");

            GameObject holderAsset = AssetDatabase.LoadAssetAtPath<GameObject>(holderPath);
            Assert.IsNotNull(holderAsset);
            Assert.AreEqual(1, holderAsset.transform.childCount);
            Transform referencedWrapper = holderAsset.transform.GetChild(0);
            Assert.IsTrue(PositionedPrefabRootUtility.HasIdentityRoot(referencedWrapper));
            Assert.AreEqual(1, referencedWrapper.childCount);
            Transform referencedPositioned = referencedWrapper.GetChild(0);
            Assert.Less(
                (referencedPositioned.localPosition - position).sqrMagnitude,
                0.0000000001f,
                "An existing nested Prefab reference must retain the mounted content offset.");
        }

        [Test]
        public void IdentityPrefabIsSkippedWithoutCreatingPositionedCopy()
        {
            string prefabPath = testFolder + "/Identity.prefab";
            GameObject source = new GameObject("Identity");
            PrefabUtility.SaveAsPrefabAsset(source, prefabPath, out bool saved);
            UnityEngine.Object.DestroyImmediate(source);
            Assert.IsTrue(saved);
            string guid = AssetDatabase.AssetPathToGUID(prefabPath);

            PositionedPrefabConversionResult result =
                PositionedPrefabRootUtility.ConvertPrefab(prefabPath);

            Assert.AreEqual(PositionedPrefabConversionStatus.Skipped, result.Status);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(prefabPath));
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(result.PositionedPath));
        }

        [Test]
        public void ConvertVariantMaterializesInheritedContentsAndPreservesVariantGuid()
        {
            const string prefabName = "MountedVariant";
            string basePath = testFolder + "/MountedBase.prefab";
            string variantPath = testFolder + "/" + prefabName + ".prefab";
            string holderPath = testFolder + "/VariantHolder.prefab";
            Vector3 position = new Vector3(-0.5f, 1.5f, -0.3f);
            Quaternion rotation = Quaternion.Euler(-8f, 172f, 91f);
            Vector3 scale = new Vector3(1.1f, 0.9f, 1.2f);

            GameObject baseSource = new GameObject("MountedBase");
            baseSource.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 3f);
            GameObject inheritedChild = new GameObject("InheritedBlade");
            inheritedChild.transform.SetParent(baseSource.transform, false);
            PrefabUtility.SaveAsPrefabAsset(baseSource, basePath, out bool baseSaved);
            UnityEngine.Object.DestroyImmediate(baseSource);
            Assert.IsTrue(baseSaved);

            GameObject baseAsset = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            GameObject variantInstance =
                PrefabUtility.InstantiatePrefab(baseAsset) as GameObject;
            Assert.IsNotNull(variantInstance);
            variantInstance.name = prefabName;
            variantInstance.transform.localPosition = position;
            variantInstance.transform.localRotation = rotation;
            variantInstance.transform.localScale = scale;
            PrefabUtility.SaveAsPrefabAsset(
                variantInstance, variantPath, out bool variantSaved);
            UnityEngine.Object.DestroyImmediate(variantInstance);
            Assert.IsTrue(variantSaved);

            GameObject variantAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            Assert.AreEqual(
                PrefabAssetType.Variant,
                PrefabUtility.GetPrefabAssetType(variantAsset));
            string variantGuid = AssetDatabase.AssetPathToGUID(variantPath);

            GameObject holder = new GameObject("VariantHolder");
            GameObject referencedVariant =
                PrefabUtility.InstantiatePrefab(variantAsset) as GameObject;
            Assert.IsNotNull(referencedVariant);
            referencedVariant.transform.SetParent(holder.transform, false);
            PrefabUtility.SaveAsPrefabAsset(holder, holderPath, out bool holderSaved);
            UnityEngine.Object.DestroyImmediate(holder);
            Assert.IsTrue(holderSaved);

            PositionedPrefabConversionResult result =
                PositionedPrefabRootUtility.ConvertPrefab(variantPath);

            Assert.AreEqual(
                PositionedPrefabConversionStatus.Converted,
                result.Status,
                result.Message);
            Assert.AreEqual(variantGuid, AssetDatabase.AssetPathToGUID(variantPath));

            GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(variantPath);
            Assert.IsNotNull(wrapper);
            Assert.AreEqual(
                PrefabAssetType.Regular,
                PrefabUtility.GetPrefabAssetType(wrapper),
                "The old Variant relationship must be materialized into the regular wrapper.");
            Assert.IsTrue(PositionedPrefabRootUtility.HasIdentityRoot(wrapper.transform));
            Assert.AreEqual(1, wrapper.transform.childCount);
            Transform positioned = wrapper.transform.GetChild(0);
            Assert.AreEqual(prefabName + "_positioned", positioned.name);
            Assert.Less((positioned.localPosition - position).sqrMagnitude, 0.0000000001f);
            Assert.GreaterOrEqual(
                Mathf.Abs(Quaternion.Dot(positioned.localRotation, rotation)),
                0.9999999f);
            Assert.Less((positioned.localScale - scale).sqrMagnitude, 0.0000000001f);
            Assert.IsNotNull(positioned.GetComponent<BoxCollider>());
            Assert.IsNotNull(positioned.Find("InheritedBlade"));

            GameObject positionedBackup =
                AssetDatabase.LoadAssetAtPath<GameObject>(result.PositionedPath);
            Assert.IsNotNull(positionedBackup);
            Assert.AreEqual(
                PrefabAssetType.Variant,
                PrefabUtility.GetPrefabAssetType(positionedBackup),
                "The positioned backup should retain the original Variant relationship.");

            CollectionAssert.Contains(
                AssetDatabase.GetDependencies(holderPath, false),
                variantPath,
                "Existing Prefab references must retain the original Variant asset GUID.");
            GameObject holderAsset = AssetDatabase.LoadAssetAtPath<GameObject>(holderPath);
            Transform referencedWrapper = holderAsset.transform.GetChild(0);
            Assert.IsTrue(PositionedPrefabRootUtility.HasIdentityRoot(referencedWrapper));
            Assert.AreEqual(1, referencedWrapper.childCount);
            Assert.Less(
                (referencedWrapper.GetChild(0).localPosition - position).sqrMagnitude,
                0.0000000001f);
        }
    }
}
