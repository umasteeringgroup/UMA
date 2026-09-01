using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UMA.Editors
{
    public enum PositionedPrefabConversionStatus
    {
        Converted,
        Skipped,
        Error
    }

    public sealed class PositionedPrefabConversionResult
    {
        public PositionedPrefabConversionStatus Status { get; }
        public string OriginalPath { get; }
        public string PositionedPath { get; }
        public string OriginalGuid { get; }
        public string Message { get; }

        internal PositionedPrefabConversionResult(
            PositionedPrefabConversionStatus status,
            string originalPath,
            string positionedPath,
            string originalGuid,
            string message)
        {
            Status = status;
            OriginalPath = originalPath;
            PositionedPath = positionedPath;
            OriginalGuid = originalGuid;
            Message = message;
        }
    }

    /// <summary>
    /// Wraps a positioned Prefab hierarchy beneath a new identity root while
    /// retaining the original asset path, GUID, and owned local object ids.
    /// </summary>
    public static class PositionedPrefabRootUtility
    {
        private const string AssetsMenuPath =
            "Assets/UMA/Convert Positioned Prefab to Identity Root";
        private const string UmaMenuPath =
            "UMA/Asset Management/Convert Positioned Prefabs to Identity Roots";
        private const string PositionedSuffix = "_positioned";

        private readonly struct TransformSnapshot
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;

            public TransformSnapshot(Transform transform)
            {
                Position = transform.localPosition;
                Rotation = transform.localRotation;
                Scale = transform.localScale;
            }

            public void Apply(Transform transform)
            {
                transform.localPosition = Position;
                transform.localRotation = Rotation;
                transform.localScale = Scale;
            }

            public bool Matches(Transform transform)
            {
                return Approximately(Position, transform.localPosition) &&
                       Approximately(Rotation, transform.localRotation) &&
                       Approximately(Scale, transform.localScale);
            }
        }

        private sealed class PrefabIdentityMap
        {
            public readonly Dictionary<string, long> LocalIds =
                new Dictionary<string, long>(StringComparer.Ordinal);
            public long RootGameObjectId;
            public long RootTransformId;
            public bool HasRootGameObjectId;
            public bool HasRootTransformId;
        }

        [MenuItem(AssetsMenuPath, false, 2012)]
        private static void ConvertSelectedPrefabsFromAssetsMenu()
        {
            ConvertSelectedPrefabs();
        }

        [MenuItem(AssetsMenuPath, true)]
        private static bool ValidateConvertSelectedPrefabsFromAssetsMenu()
        {
            return GetSelectedPrefabPaths().Count > 0;
        }

        [MenuItem(UmaMenuPath, false, 127)]
        private static void ConvertSelectedPrefabsFromUmaMenu()
        {
            ConvertSelectedPrefabs();
        }

        private static void ConvertSelectedPrefabs()
        {
            List<string> prefabPaths = GetSelectedPrefabPaths();
            if (prefabPaths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Convert Positioned Prefabs",
                    "Select one or more regular Prefab Assets or Prefab Variants in the Project window.",
                    "OK");
                return;
            }

            StringBuilder confirmation = new StringBuilder();
            confirmation.AppendLine(
                "Each non-identity Prefab will be converted as follows:");
            confirmation.AppendLine();
            confirmation.AppendLine(
                "• The original hierarchy is copied to <name>_positioned.prefab with a new GUID.");
            confirmation.AppendLine(
                "• The original path and GUID become an identity-root wrapper.");
            confirmation.AppendLine(
                "• The unpacked old hierarchy becomes the wrapper's positioned child.");
            confirmation.AppendLine(
                "• A selected Prefab Variant is materialized; its _positioned copy retains the Variant relationship.");
            confirmation.AppendLine();
            confirmation.AppendLine(
                "The operation is not registered with Unity Undo. The _positioned copy is retained as a backup.");
            confirmation.AppendLine();
            confirmation.AppendLine("Selected Prefabs: " + prefabPaths.Count);
            int previewCount = Mathf.Min(prefabPaths.Count, 8);
            for (int index = 0; index < previewCount; index++)
            {
                confirmation.AppendLine("• " + prefabPaths[index]);
            }
            if (prefabPaths.Count > previewCount)
            {
                confirmation.AppendLine(
                    "• …and " + (prefabPaths.Count - previewCount) + " more");
            }

            if (!EditorUtility.DisplayDialog(
                    "Convert Positioned Prefabs",
                    confirmation.ToString(),
                    "Convert",
                    "Cancel"))
            {
                return;
            }

            List<PositionedPrefabConversionResult> results =
                new List<PositionedPrefabConversionResult>(prefabPaths.Count);
            try
            {
                for (int index = 0; index < prefabPaths.Count; index++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Convert Positioned Prefabs",
                        prefabPaths[index],
                        index / (float)prefabPaths.Count);
                    results.Add(ConvertPrefab(prefabPaths[index]));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ShowResults(results);
        }

        /// <summary>
        /// Converts one regular Prefab Asset or Prefab Variant. The asset at
        /// <paramref name="prefabPath"/> keeps its GUID; the positioned copy
        /// receives a newly generated GUID.
        /// </summary>
        public static PositionedPrefabConversionResult ConvertPrefab(string prefabPath)
        {
            string normalizedPath = NormalizeAssetPath(prefabPath);
            string positionedPath = GetPositionedPrefabPath(normalizedPath);
            string originalGuid = string.IsNullOrEmpty(normalizedPath)
                ? string.Empty
                : AssetDatabase.AssetPathToGUID(normalizedPath);

            if (!TryValidateSourcePrefab(
                    normalizedPath, positionedPath, out GameObject sourceAsset, out string validationError))
            {
                return Error(normalizedPath, positionedPath, originalGuid, validationError);
            }

            if (HasIdentityRoot(sourceAsset.transform))
            {
                return new PositionedPrefabConversionResult(
                    PositionedPrefabConversionStatus.Skipped,
                    normalizedPath,
                    positionedPath,
                    originalGuid,
                    "The Prefab root already has an identity transform.");
            }

            string assetName = Path.GetFileNameWithoutExtension(normalizedPath);
            string originalRootName = sourceAsset.name;
            string positionedName = assetName + PositionedSuffix;
            TransformSnapshot positionedTransform = new TransformSnapshot(sourceAsset.transform);
            bool sourceWasVariant =
                PrefabUtility.GetPrefabAssetType(sourceAsset) == PrefabAssetType.Variant;
            PrefabIdentityMap sourceIdentities = sourceWasVariant
                ? null
                : CaptureOwnedIdentities(sourceAsset, originalGuid);
            if (!sourceWasVariant && !HasRequiredPersistentIds(sourceIdentities))
            {
                return Error(
                    normalizedPath,
                    positionedPath,
                    originalGuid,
                    "Unity did not expose the persistent object ids needed to preserve existing references. No assets were changed.");
            }

            bool backupCreated = false;
            bool originalModified = false;
            try
            {
                if (!AssetDatabase.CopyAsset(normalizedPath, positionedPath))
                {
                    throw new InvalidOperationException(
                        "Unity could not create the positioned Prefab copy.");
                }
                backupCreated = true;
                AssetDatabase.ImportAsset(
                    positionedPath,
                    ImportAssetOptions.ForceSynchronousImport |
                    ImportAssetOptions.ForceUpdate);

                string positionedGuid = AssetDatabase.AssetPathToGUID(positionedPath);
                if (string.IsNullOrEmpty(positionedGuid) ||
                    string.Equals(positionedGuid, originalGuid, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The positioned copy did not receive a distinct GUID.");
                }

                if (sourceWasVariant)
                {
                    originalModified = true;
                    MaterializeVariantAtOriginalPath(
                        normalizedPath,
                        originalGuid,
                        originalRootName,
                        positionedTransform);
                    ImportSynchronously(normalizedPath);

                    GameObject materializedAsset =
                        AssetDatabase.LoadAssetAtPath<GameObject>(normalizedPath);
                    sourceIdentities = CaptureOwnedIdentities(
                        materializedAsset, originalGuid);
                    if (!HasRequiredPersistentIds(sourceIdentities))
                    {
                        throw new InvalidOperationException(
                            "Unity did not expose persistent object ids after materializing the Prefab Variant.");
                    }
                }

                originalModified = true;
                SaveIdentityWrapperFirstPass(
                    normalizedPath,
                    originalRootName,
                    positionedName,
                    positionedTransform);
                ImportSynchronously(normalizedPath);

                ValidateFirstPass(
                    normalizedPath,
                    originalGuid,
                    originalRootName,
                    positionedName,
                    positionedTransform,
                    sourceIdentities);

                FinalizeWrapperNames(
                    normalizedPath,
                    assetName,
                    positionedName,
                    positionedTransform);
                ImportSynchronously(normalizedPath);

                RenamePositionedBackup(
                    positionedPath,
                    positionedName,
                    positionedTransform);
                ImportSynchronously(positionedPath);

                ValidateFinalAssets(
                    normalizedPath,
                    positionedPath,
                    originalGuid,
                    assetName,
                    positionedName,
                    positionedTransform,
                    sourceIdentities);

                GameObject convertedAsset =
                    AssetDatabase.LoadAssetAtPath<GameObject>(normalizedPath);
                EditorGUIUtility.PingObject(convertedAsset);
                return new PositionedPrefabConversionResult(
                    PositionedPrefabConversionStatus.Converted,
                    normalizedPath,
                    positionedPath,
                    originalGuid,
                    sourceWasVariant
                        ? "Converted successfully. The original Variant GUID was preserved, its overrides were materialized, and the positioned backup retains the Variant relationship."
                        : "Converted successfully. The original GUID and owned object references were preserved.");
            }
            catch (Exception exception)
            {
                string rollbackMessage = string.Empty;
                if (originalModified && backupCreated)
                {
                    bool restored = TryRestoreOriginal(
                        normalizedPath,
                        positionedPath,
                        originalGuid,
                        originalRootName,
                        positionedTransform,
                        sourceIdentities,
                        sourceWasVariant,
                        out rollbackMessage);
                    if (restored)
                    {
                        AssetDatabase.DeleteAsset(positionedPath);
                        backupCreated = false;
                    }
                }
                else if (backupCreated)
                {
                    AssetDatabase.DeleteAsset(positionedPath);
                    backupCreated = false;
                }

                string message = exception.Message;
                if (!string.IsNullOrEmpty(rollbackMessage))
                {
                    message += " " + rollbackMessage;
                }
                if (backupCreated)
                {
                    message += " The recovery copy remains at '" + positionedPath + "'.";
                }

                Debug.LogError(
                    "UMA positioned Prefab conversion failed for '" + normalizedPath + "': " + message);
                return Error(normalizedPath, positionedPath, originalGuid, message);
            }
        }

        public static bool HasIdentityRoot(Transform transform)
        {
            if (transform == null)
            {
                return false;
            }

            Vector3 position = transform.localPosition;
            Quaternion rotation = transform.localRotation;
            Vector3 scale = transform.localScale;
            return position.x == 0f && position.y == 0f && position.z == 0f &&
                   rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 1f &&
                   scale.x == 1f && scale.y == 1f && scale.z == 1f;
        }

        public static string GetPositionedPrefabPath(string prefabPath)
        {
            string normalizedPath = NormalizeAssetPath(prefabPath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return string.Empty;
            }

            string directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(normalizedPath);
            return string.IsNullOrEmpty(directory)
                ? name + PositionedSuffix + ".prefab"
                : directory + "/" + name + PositionedSuffix + ".prefab";
        }

        private static bool TryValidateSourcePrefab(
            string prefabPath,
            string positionedPath,
            out GameObject sourceAsset,
            out string error)
        {
            sourceAsset = null;
            error = string.Empty;
            if (string.IsNullOrEmpty(prefabPath) ||
                !prefabPath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                error = "Select a writable .prefab asset below the project's Assets folder.";
                return false;
            }

            sourceAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (sourceAsset == null)
            {
                error = "The selected path is not a loadable Prefab Asset.";
                return false;
            }

            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(sourceAsset);
            if (assetType != PrefabAssetType.Regular &&
                assetType != PrefabAssetType.Variant)
            {
                error = "Only regular Prefab Assets and Prefab Variants can be converted; Model and missing Prefabs are not writable Prefab contents.";
                return false;
            }

            if (sourceAsset.transform.GetType() != typeof(Transform))
            {
                error = "Only Prefabs with a standard Transform root are supported. UI Prefabs with a RectTransform root require a purpose-built conversion.";
                return false;
            }

            if (!AssetDatabase.IsOpenForEdit(
                    prefabPath, out string assetEditMessage,
                    StatusQueryOptions.UseCachedIfPossible))
            {
                error = string.IsNullOrEmpty(assetEditMessage)
                    ? "The Prefab is not open for edit."
                    : assetEditMessage;
                return false;
            }
            if (!AssetDatabase.IsOpenForEdit(
                    prefabPath + ".meta", out string metaEditMessage,
                    StatusQueryOptions.UseCachedIfPossible))
            {
                error = string.IsNullOrEmpty(metaEditMessage)
                    ? "The Prefab metadata is not open for edit."
                    : metaEditMessage;
                return false;
            }

            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(positionedPath)) ||
                AssetDatabase.LoadMainAssetAtPath(positionedPath) != null)
            {
                error = "The destination already exists: '" + positionedPath +
                        "'. Rename or remove it before converting this Prefab.";
                return false;
            }

            if (Path.GetFileNameWithoutExtension(prefabPath)
                .EndsWith(PositionedSuffix, StringComparison.OrdinalIgnoreCase))
            {
                error = "A Prefab whose name already ends in '" + PositionedSuffix +
                        "' is treated as positioned source content and is not converted again.";
                return false;
            }

            return true;
        }

        private static void MaterializeVariantAtOriginalPath(
            string prefabPath,
            string originalGuid,
            string originalRootName,
            TransformSnapshot positionedTransform)
        {
            GameObject contentsRoot = null;
            try
            {
                contentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (contentsRoot == null ||
                    !PrefabUtility.IsPartOfPrefabInstance(contentsRoot))
                {
                    throw new InvalidOperationException(
                        "Unity could not load the Prefab Variant's inherited contents.");
                }

                int unpackCount = 0;
                while (PrefabUtility.IsPartOfPrefabInstance(contentsRoot))
                {
                    GameObject outermostRoot =
                        PrefabUtility.GetOutermostPrefabInstanceRoot(contentsRoot);
                    if (outermostRoot != contentsRoot)
                    {
                        throw new InvalidOperationException(
                            "The Prefab Variant did not expose its inherited root as an unpackable instance.");
                    }

                    PrefabUtility.UnpackPrefabInstance(
                        contentsRoot,
                        PrefabUnpackMode.OutermostRoot,
                        InteractionMode.AutomatedAction);
                    unpackCount++;
                    if (unpackCount > 64)
                    {
                        throw new InvalidOperationException(
                            "The Prefab Variant has an unexpectedly deep base chain and could not be materialized safely.");
                    }
                }

                contentsRoot.name = originalRootName;
                positionedTransform.Apply(contentsRoot.transform);
                PrefabUtility.SaveAsPrefabAsset(
                    contentsRoot, prefabPath, out bool savedSuccessfully);
                if (!savedSuccessfully)
                {
                    throw new InvalidOperationException(
                        "Unity could not save the materialized Prefab Variant contents.");
                }
            }
            finally
            {
                if (contentsRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(contentsRoot);
                }
            }

            ImportSynchronously(prefabPath);
            GameObject materialized =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (materialized == null ||
                PrefabUtility.GetPrefabAssetType(materialized) != PrefabAssetType.Regular ||
                !string.Equals(materialized.name, originalRootName, StringComparison.Ordinal) ||
                !positionedTransform.Matches(materialized.transform) ||
                !string.Equals(
                    AssetDatabase.AssetPathToGUID(prefabPath),
                    originalGuid,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The Prefab Variant was not materialized as a regular Prefab with its original GUID and transform.");
            }
        }

        private static void SaveIdentityWrapperFirstPass(
            string prefabPath,
            string originalRootName,
            string positionedName,
            TransformSnapshot positionedTransform)
        {
            GameObject contentsRoot = null;
            GameObject wrapper = null;
            try
            {
                contentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (contentsRoot == null)
                {
                    throw new InvalidOperationException("Unity could not load the Prefab contents.");
                }
                if (!string.Equals(
                        contentsRoot.name, originalRootName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The Prefab root changed before conversion could begin.");
                }

                Scene prefabScene = contentsRoot.scene;
                wrapper = new GameObject(originalRootName);
                SceneManager.MoveGameObjectToScene(wrapper, prefabScene);
                SetIdentity(wrapper.transform);
                contentsRoot.name = positionedName;
                positionedTransform.Apply(contentsRoot.transform);
                contentsRoot.transform.SetParent(wrapper.transform, false);

                PrefabUtility.SaveAsPrefabAsset(
                    wrapper, prefabPath, out bool savedSuccessfully);
                if (!savedSuccessfully)
                {
                    throw new InvalidOperationException(
                        "Unity could not save the identity-root Prefab.");
                }
            }
            finally
            {
                if (contentsRoot != null)
                {
                    if (contentsRoot.transform.parent != null)
                    {
                        contentsRoot.transform.SetParent(null, false);
                    }
                    if (wrapper != null)
                    {
                        UnityEngine.Object.DestroyImmediate(wrapper);
                    }
                    PrefabUtility.UnloadPrefabContents(contentsRoot);
                }
                else if (wrapper != null)
                {
                    UnityEngine.Object.DestroyImmediate(wrapper);
                }
            }
        }

        private static void ValidateFirstPass(
            string prefabPath,
            string originalGuid,
            string originalRootName,
            string positionedName,
            TransformSnapshot positionedTransform,
            PrefabIdentityMap sourceIdentities)
        {
            GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (wrapper == null || !HasIdentityRoot(wrapper.transform) ||
                !string.Equals(wrapper.name, originalRootName, StringComparison.Ordinal) ||
                wrapper.transform.childCount != 1)
            {
                throw new InvalidOperationException(
                    "The first conversion pass did not produce the expected identity wrapper.");
            }

            GameObject positionedRoot = wrapper.transform.GetChild(0).gameObject;
            if (!string.Equals(positionedRoot.name, positionedName, StringComparison.Ordinal) ||
                !positionedTransform.Matches(positionedRoot.transform))
            {
                throw new InvalidOperationException(
                    "The original root transform was not preserved beneath the wrapper.");
            }

            ValidateConvertedIdentityMap(
                sourceIdentities, wrapper, positionedRoot, originalGuid);
        }

        private static void FinalizeWrapperNames(
            string prefabPath,
            string wrapperName,
            string positionedName,
            TransformSnapshot positionedTransform)
        {
            GameObject contentsRoot = null;
            try
            {
                contentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (contentsRoot == null || contentsRoot.transform.childCount != 1)
                {
                    throw new InvalidOperationException(
                        "The converted Prefab hierarchy could not be reopened.");
                }

                contentsRoot.name = wrapperName;
                SetIdentity(contentsRoot.transform);
                Transform positionedRoot = contentsRoot.transform.GetChild(0);
                positionedRoot.name = positionedName;
                positionedTransform.Apply(positionedRoot);

                PrefabUtility.SaveAsPrefabAsset(
                    contentsRoot, prefabPath, out bool savedSuccessfully);
                if (!savedSuccessfully)
                {
                    throw new InvalidOperationException(
                        "Unity could not save the final Prefab names.");
                }
            }
            finally
            {
                if (contentsRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(contentsRoot);
                }
            }
        }

        private static void RenamePositionedBackup(
            string positionedPath,
            string positionedName,
            TransformSnapshot positionedTransform)
        {
            GameObject contentsRoot = null;
            try
            {
                contentsRoot = PrefabUtility.LoadPrefabContents(positionedPath);
                if (contentsRoot == null)
                {
                    throw new InvalidOperationException(
                        "Unity could not reopen the positioned Prefab copy.");
                }

                contentsRoot.name = positionedName;
                positionedTransform.Apply(contentsRoot.transform);
                PrefabUtility.SaveAsPrefabAsset(
                    contentsRoot, positionedPath, out bool savedSuccessfully);
                if (!savedSuccessfully)
                {
                    throw new InvalidOperationException(
                        "Unity could not rename the positioned Prefab copy.");
                }
            }
            finally
            {
                if (contentsRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(contentsRoot);
                }
            }
        }

        private static void ValidateFinalAssets(
            string prefabPath,
            string positionedPath,
            string originalGuid,
            string wrapperName,
            string positionedName,
            TransformSnapshot positionedTransform,
            PrefabIdentityMap sourceIdentities)
        {
            if (!string.Equals(
                    AssetDatabase.AssetPathToGUID(prefabPath),
                    originalGuid,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The converted Prefab did not retain the original GUID.");
            }

            string positionedGuid = AssetDatabase.AssetPathToGUID(positionedPath);
            if (string.IsNullOrEmpty(positionedGuid) ||
                string.Equals(positionedGuid, originalGuid, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The positioned Prefab does not have its own GUID.");
            }

            GameObject wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (wrapper == null ||
                PrefabUtility.GetPrefabAssetType(wrapper) != PrefabAssetType.Regular ||
                !string.Equals(wrapper.name, wrapperName, StringComparison.Ordinal) ||
                !HasIdentityRoot(wrapper.transform) ||
                wrapper.transform.childCount != 1)
            {
                throw new InvalidOperationException(
                    "The final Prefab root is not the expected identity wrapper.");
            }

            GameObject positionedRoot = wrapper.transform.GetChild(0).gameObject;
            if (!string.Equals(positionedRoot.name, positionedName, StringComparison.Ordinal) ||
                !positionedTransform.Matches(positionedRoot.transform))
            {
                throw new InvalidOperationException(
                    "The positioned child does not retain the original transform.");
            }

            ValidateConvertedIdentityMap(
                sourceIdentities, wrapper, positionedRoot, originalGuid);

            GameObject positionedAsset =
                AssetDatabase.LoadAssetAtPath<GameObject>(positionedPath);
            if (positionedAsset == null ||
                !string.Equals(positionedAsset.name, positionedName, StringComparison.Ordinal) ||
                !positionedTransform.Matches(positionedAsset.transform))
            {
                throw new InvalidOperationException(
                    "The positioned Prefab copy does not match the original hierarchy transform.");
            }

            string[] directDependencies = AssetDatabase.GetDependencies(prefabPath, false);
            for (int index = 0; index < directDependencies.Length; index++)
            {
                if (string.Equals(
                        directDependencies[index], positionedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The new Prefab still nests the positioned Prefab instead of containing unpacked contents.");
                }
            }
        }

        private static bool TryRestoreOriginal(
            string prefabPath,
            string positionedPath,
            string originalGuid,
            string originalRootName,
            TransformSnapshot positionedTransform,
            PrefabIdentityMap sourceIdentities,
            bool sourceWasVariant,
            out string message)
        {
            try
            {
                PrepareConvertedAssetForRollback(prefabPath, originalRootName);

                GameObject backupRoot = null;
                try
                {
                    backupRoot = PrefabUtility.LoadPrefabContents(positionedPath);
                    if (backupRoot == null)
                    {
                        throw new InvalidOperationException(
                            "The positioned recovery copy could not be loaded.");
                    }
                    backupRoot.name = originalRootName;
                    positionedTransform.Apply(backupRoot.transform);
                    PrefabUtility.SaveAsPrefabAsset(
                        backupRoot, prefabPath, out bool savedSuccessfully);
                    if (!savedSuccessfully)
                    {
                        throw new InvalidOperationException(
                            "Unity could not restore the original Prefab contents.");
                    }
                }
                finally
                {
                    if (backupRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(backupRoot);
                    }
                }

                ImportSynchronously(prefabPath);
                GameObject restored = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (restored == null || !positionedTransform.Matches(restored.transform) ||
                    (sourceWasVariant &&
                     PrefabUtility.GetPrefabAssetType(restored) != PrefabAssetType.Variant) ||
                    !string.Equals(
                        AssetDatabase.AssetPathToGUID(prefabPath), originalGuid,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "The restored Prefab did not match the original transform and GUID.");
                }
                if (!sourceWasVariant)
                {
                    ValidateIdentityMap(
                        sourceIdentities,
                        CaptureOwnedIdentities(restored, originalGuid));
                }

                message = "The original Prefab was restored after the failed conversion.";
                return true;
            }
            catch (Exception rollbackException)
            {
                message = "Automatic restoration also failed: " + rollbackException.Message;
                return false;
            }
        }

        private static void PrepareConvertedAssetForRollback(
            string prefabPath,
            string originalRootName)
        {
            GameObject contentsRoot = null;
            try
            {
                contentsRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (contentsRoot == null || contentsRoot.transform.childCount != 1 ||
                    !HasIdentityRoot(contentsRoot.transform))
                {
                    return;
                }

                contentsRoot.name = CreateTemporaryWrapperName(
                    contentsRoot, "Rollback");
                contentsRoot.transform.GetChild(0).name = originalRootName;
                PrefabUtility.SaveAsPrefabAsset(
                    contentsRoot, prefabPath, out bool savedSuccessfully);
                if (!savedSuccessfully)
                {
                    throw new InvalidOperationException(
                        "Unity could not prepare the converted asset for restoration.");
                }
                ImportSynchronously(prefabPath);
            }
            finally
            {
                if (contentsRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(contentsRoot);
                }
            }
        }

        private static PrefabIdentityMap CaptureOwnedIdentities(
            GameObject hierarchyRoot,
            string expectedGuid)
        {
            PrefabIdentityMap map = new PrefabIdentityMap();
            if (hierarchyRoot == null || string.IsNullOrEmpty(expectedGuid))
            {
                return map;
            }

            CaptureOwnedIdentitiesRecursive(
                hierarchyRoot.transform, ".", expectedGuid, map.LocalIds);
            map.HasRootGameObjectId = TryGetOwnedLocalId(
                hierarchyRoot, expectedGuid, out map.RootGameObjectId);
            map.HasRootTransformId = TryGetOwnedLocalId(
                hierarchyRoot.transform, expectedGuid, out map.RootTransformId);
            return map;
        }

        private static bool HasRequiredPersistentIds(PrefabIdentityMap identities)
        {
            return identities != null &&
                   identities.HasRootGameObjectId &&
                   identities.HasRootTransformId &&
                   identities.LocalIds.Count >= 2;
        }

        private static void CaptureOwnedIdentitiesRecursive(
            Transform transform,
            string relativePath,
            string expectedGuid,
            Dictionary<string, long> identities)
        {
            AddOwnedIdentity(
                transform.gameObject, "GameObject|" + relativePath,
                expectedGuid, identities);

            Component[] components = transform.GetComponents<Component>();
            Dictionary<string, int> typeOccurrences =
                new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null)
                {
                    continue;
                }

                string typeName = component.GetType().AssemblyQualifiedName ??
                                  component.GetType().FullName ??
                                  component.GetType().Name;
                typeOccurrences.TryGetValue(typeName, out int occurrence);
                typeOccurrences[typeName] = occurrence + 1;
                AddOwnedIdentity(
                    component,
                    "Component|" + relativePath + "|" + typeName + "|" + occurrence,
                    expectedGuid,
                    identities);
            }

            for (int childIndex = 0; childIndex < transform.childCount; childIndex++)
            {
                CaptureOwnedIdentitiesRecursive(
                    transform.GetChild(childIndex),
                    relativePath + "/" + childIndex,
                    expectedGuid,
                    identities);
            }
        }

        private static void AddOwnedIdentity(
            UnityEngine.Object assetObject,
            string key,
            string expectedGuid,
            Dictionary<string, long> identities)
        {
            if (assetObject != null &&
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                    assetObject, out string guid, out long localId) &&
                string.Equals(guid, expectedGuid, StringComparison.OrdinalIgnoreCase))
            {
                identities[key] = localId;
            }
        }

        private static bool TryGetOwnedLocalId(
            UnityEngine.Object assetObject,
            string expectedGuid,
            out long localId)
        {
            localId = 0;
            return assetObject != null &&
                   AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                       assetObject, out string guid, out localId) &&
                   string.Equals(guid, expectedGuid, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateConvertedIdentityMap(
            PrefabIdentityMap expected,
            GameObject wrapper,
            GameObject positionedRoot,
            string expectedGuid)
        {
            if (!expected.HasRootGameObjectId || !expected.HasRootTransformId)
            {
                throw new InvalidOperationException(
                    "Unity did not expose persistent ids for the source Prefab root.");
            }
            if (!TryGetOwnedLocalId(wrapper, expectedGuid, out long wrapperGameObjectId) ||
                wrapperGameObjectId != expected.RootGameObjectId)
            {
                throw new InvalidOperationException(
                    "The identity wrapper did not inherit the old Prefab root GameObject id. Direct Prefab references would not resolve to the new wrapper.");
            }
            if (!TryGetOwnedLocalId(wrapper.transform, expectedGuid, out long wrapperTransformId) ||
                wrapperTransformId != expected.RootTransformId)
            {
                throw new InvalidOperationException(
                    "The identity wrapper did not inherit the old Prefab root Transform id.");
            }

            PrefabIdentityMap actualContents =
                CaptureOwnedIdentities(positionedRoot, expectedGuid);
            int expectedContentCount = expected.LocalIds.Count - 2;
            int actualContentCount = actualContents.LocalIds.Count - 2;
            if (expectedContentCount < 0 || actualContentCount != expectedContentCount)
            {
                throw new InvalidOperationException(
                    "The conversion changed the number of persistent content objects owned by the Prefab.");
            }

            foreach (KeyValuePair<string, long> pair in expected.LocalIds)
            {
                if (pair.Key == "GameObject|." ||
                    pair.Value == expected.RootTransformId)
                {
                    continue;
                }
                if (!actualContents.LocalIds.TryGetValue(pair.Key, out long actualId) ||
                    actualId != pair.Value)
                {
                    throw new InvalidOperationException(
                        "The conversion could not preserve the persistent object id for positioned content '" +
                        pair.Key + "'.");
                }
            }
        }

        private static void ValidateIdentityMap(
            PrefabIdentityMap expected,
            PrefabIdentityMap actual)
        {
            if (expected.LocalIds.Count == 0)
            {
                throw new InvalidOperationException(
                    "Unity did not expose persistent object ids for the source Prefab; conversion was stopped rather than risk component references.");
            }
            if (actual.LocalIds.Count != expected.LocalIds.Count)
            {
                throw new InvalidOperationException(
                    "The conversion changed the number of persistent objects owned by the Prefab.");
            }

            foreach (KeyValuePair<string, long> pair in expected.LocalIds)
            {
                if (!actual.LocalIds.TryGetValue(pair.Key, out long actualId) ||
                    actualId != pair.Value)
                {
                    throw new InvalidOperationException(
                        "The conversion could not preserve the persistent object id for '" +
                        pair.Key + "'.");
                }
            }
        }

        private static List<string> GetSelectedPrefabPaths()
        {
            HashSet<string> uniquePaths =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> paths = new List<string>();
            UnityEngine.Object[] selectedObjects = Selection.objects;
            for (int index = 0; index < selectedObjects.Length; index++)
            {
                string path = NormalizeAssetPath(
                    AssetDatabase.GetAssetPath(selectedObjects[index]));
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                    AssetDatabase.LoadAssetAtPath<GameObject>(path) != null &&
                    uniquePaths.Add(path))
                {
                    paths.Add(path);
                }
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private static void ShowResults(
            List<PositionedPrefabConversionResult> results)
        {
            int converted = 0;
            int skipped = 0;
            int errors = 0;
            StringBuilder details = new StringBuilder();
            for (int index = 0; index < results.Count; index++)
            {
                PositionedPrefabConversionResult result = results[index];
                switch (result.Status)
                {
                    case PositionedPrefabConversionStatus.Converted:
                        converted++;
                        break;
                    case PositionedPrefabConversionStatus.Skipped:
                        skipped++;
                        break;
                    default:
                        errors++;
                        break;
                }
                details.AppendLine();
                details.Append(result.Status).Append(": ").AppendLine(result.OriginalPath);
                details.AppendLine(result.Message);
            }

            EditorUtility.DisplayDialog(
                "Positioned Prefab Conversion",
                "Converted: " + converted + "\n" +
                "Skipped: " + skipped + "\n" +
                "Errors: " + errors + "\n" +
                details,
                "OK");
        }

        private static string CreateTemporaryWrapperName(
            GameObject sourceRoot,
            string assetName)
        {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            Transform[] transforms = sourceRoot.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                names.Add(transforms[index].name);
            }

            string candidate = "__UMA_IdentityRoot_" + assetName;
            while (names.Contains(candidate))
            {
                candidate += "_";
            }
            return candidate;
        }

        private static void SetIdentity(Transform transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }

        private static void ImportSynchronously(string assetPath)
        {
            AssetDatabase.ImportAsset(
                assetPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').Trim();
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return (left - right).sqrMagnitude <= 0.0000000001f;
        }

        private static bool Approximately(Quaternion left, Quaternion right)
        {
            return Mathf.Abs(Quaternion.Dot(left, right)) >= 0.9999999f;
        }

        private static PositionedPrefabConversionResult Error(
            string originalPath,
            string positionedPath,
            string originalGuid,
            string message)
        {
            return new PositionedPrefabConversionResult(
                PositionedPrefabConversionStatus.Error,
                originalPath,
                positionedPath,
                originalGuid,
                message);
        }
    }
}
