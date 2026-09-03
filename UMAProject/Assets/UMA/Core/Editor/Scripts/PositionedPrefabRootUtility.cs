using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
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

        private sealed class ReferencingPrefabSnapshot
        {
            public string AssetPath;
            public readonly List<TransformSnapshot> InstanceRootTransforms =
                new List<TransformSnapshot>();
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

            List<ReferencingPrefabSnapshot> referencingPrefabs =
                new List<ReferencingPrefabSnapshot>();

            bool backupCreated = false;
            bool originalModified = false;
            try
            {
                referencingPrefabs =
                    CaptureReferencingPrefabRootTransforms(normalizedPath);

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

                EnsureConvertedRootLocalIds(
                    normalizedPath,
                    originalGuid,
                    sourceIdentities);

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

                MigrateReferencingPrefabRootTransforms(
                    referencingPrefabs,
                    normalizedPath,
                    positionedTransform);

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
                        try
                        {
                            RestoreReferencingPrefabRootTransforms(
                                referencingPrefabs,
                                normalizedPath);
                            AssetDatabase.DeleteAsset(positionedPath);
                            backupCreated = false;
                        }
                        catch (Exception referenceRollbackException)
                        {
                            rollbackMessage +=
                                " Referencing Prefab restoration failed: " +
                                referenceRollbackException.Message;
                        }
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

        private static void EnsureConvertedRootLocalIds(
            string prefabPath,
            string originalGuid,
            PrefabIdentityMap sourceIdentities)
        {
            GameObject wrapper =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (wrapper == null || wrapper.transform.childCount != 1)
            {
                throw new InvalidOperationException(
                    "The converted Prefab could not be loaded for persistent id repair.");
            }

            GameObject positionedRoot = wrapper.transform.GetChild(0).gameObject;
            if (!TryGetOwnedLocalId(
                    wrapper, originalGuid, out long wrapperGameObjectId) ||
                !TryGetOwnedLocalId(
                    wrapper.transform, originalGuid, out long wrapperTransformId) ||
                sourceIdentities == null)
            {
                throw new InvalidOperationException(
                    "Unity did not expose the converted Prefab root ids needed to preserve references.");
            }

            PrefabIdentityMap actualContents =
                CaptureOwnedIdentities(positionedRoot, originalGuid);
            if (actualContents.LocalIds.Count != sourceIdentities.LocalIds.Count)
            {
                throw new InvalidOperationException(
                    "The conversion changed the number of persistent objects in the positioned hierarchy.");
            }

            const string rootGameObjectKey = "GameObject|.";
            string rootTransformKey = null;
            foreach (KeyValuePair<string, long> pair in sourceIdentities.LocalIds)
            {
                if (pair.Value == sourceIdentities.RootTransformId)
                {
                    rootTransformKey = pair.Key;
                    break;
                }
            }
            if (string.IsNullOrEmpty(rootTransformKey) ||
                !actualContents.LocalIds.TryGetValue(
                    rootGameObjectKey, out long positionedGameObjectId) ||
                !actualContents.LocalIds.TryGetValue(
                    rootTransformKey, out long positionedTransformId))
            {
                throw new InvalidOperationException(
                    "The conversion did not expose the positioned root GameObject and Transform ids.");
            }

            HashSet<long> reservedOriginalIds =
                new HashSet<long>(sourceIdentities.LocalIds.Values);
            HashSet<long> assignedIds = new HashSet<long>(reservedOriginalIds);
            long positionedGameObjectTarget = ChooseContainerLocalId(
                positionedGameObjectId,
                wrapperGameObjectId,
                assignedIds);
            assignedIds.Add(positionedGameObjectTarget);
            long positionedTransformTarget = ChooseContainerLocalId(
                positionedTransformId,
                wrapperTransformId,
                assignedIds);
            assignedIds.Add(positionedTransformTarget);

            Dictionary<long, long> localIdRemap =
                new Dictionary<long, long>();
            AddLocalIdMapping(
                localIdRemap,
                wrapperGameObjectId,
                sourceIdentities.RootGameObjectId);
            AddLocalIdMapping(
                localIdRemap,
                wrapperTransformId,
                sourceIdentities.RootTransformId);
            AddLocalIdMapping(
                localIdRemap,
                positionedGameObjectId,
                positionedGameObjectTarget);
            AddLocalIdMapping(
                localIdRemap,
                positionedTransformId,
                positionedTransformTarget);

            foreach (KeyValuePair<string, long> expectedPair in
                     sourceIdentities.LocalIds)
            {
                if (expectedPair.Key == rootGameObjectKey ||
                    expectedPair.Key == rootTransformKey)
                {
                    continue;
                }
                if (!actualContents.LocalIds.TryGetValue(
                        expectedPair.Key, out long actualId))
                {
                    throw new InvalidOperationException(
                        "The converted positioned hierarchy is missing persistent object '" +
                        expectedPair.Key + "'.");
                }
                AddLocalIdMapping(
                    localIdRemap,
                    actualId,
                    expectedPair.Value);
            }

            RemapUnityYamlLocalIds(prefabPath, localIdRemap);
            ImportSynchronously(prefabPath);
        }

        private static long ChooseContainerLocalId(
            long currentId,
            long vacatedWrapperId,
            HashSet<long> assignedIds)
        {
            if (!assignedIds.Contains(currentId))
            {
                return currentId;
            }
            if (!assignedIds.Contains(vacatedWrapperId))
            {
                return vacatedWrapperId;
            }

            long candidate;
            do
            {
                candidate = BitConverter.ToInt64(
                    Guid.NewGuid().ToByteArray(), 0) & long.MaxValue;
            }
            while (candidate == 0 || assignedIds.Contains(candidate));
            return candidate;
        }

        private static void AddLocalIdMapping(
            Dictionary<long, long> remap,
            long source,
            long target)
        {
            if (source == target)
            {
                return;
            }
            if (remap.TryGetValue(source, out long existingTarget))
            {
                if (existingTarget != target)
                {
                    throw new InvalidOperationException(
                        "The converted Prefab has an ambiguous local id map for " +
                        source + ".");
                }
                return;
            }

            remap.Add(source, target);
        }

        private static void RemapUnityYamlLocalIds(
            string assetPath,
            Dictionary<long, long> remap)
        {
            if (remap == null || remap.Count == 0)
            {
                return;
            }

            byte[] sourceBytes = File.ReadAllBytes(GetAbsoluteAssetPath(assetPath));
            int textOffset = ValidateUnityYamlHeader(sourceBytes, assetPath);
            bool hasUtf8Bom = textOffset == 3;
            var strictUtf8 = new UTF8Encoding(false, true);
            string yaml = strictUtf8.GetString(
                sourceBytes,
                textOffset,
                sourceBytes.Length - textOffset);

            var anchorPattern = new Regex(
                @"(?<prefix>^--- !u!\d+ &)(?<id>-?\d+)(?<suffix>[^\n]*)$",
                RegexOptions.Multiline | RegexOptions.CultureInvariant);
            Dictionary<long, int> anchorCounts =
                new Dictionary<long, int>();
            foreach (Match match in anchorPattern.Matches(yaml))
            {
                if (long.TryParse(match.Groups["id"].Value, out long localId) &&
                    remap.ContainsKey(localId))
                {
                    anchorCounts.TryGetValue(localId, out int count);
                    anchorCounts[localId] = count + 1;
                }
            }

            foreach (long sourceId in remap.Keys)
            {
                if (!anchorCounts.TryGetValue(sourceId, out int count) ||
                    count != 1)
                {
                    throw new InvalidOperationException(
                        "The Unity YAML for '" + assetPath +
                        "' did not contain exactly one document for local id " +
                        sourceId + ". No text was changed.");
                }
            }

            HashSet<long> remappedAnchorIds = new HashSet<long>();
            foreach (Match match in anchorPattern.Matches(yaml))
            {
                if (!long.TryParse(
                        match.Groups["id"].Value, out long localId))
                {
                    throw new InvalidOperationException(
                        "The Unity YAML for '" + assetPath +
                        "' contains an invalid document id. No text was changed.");
                }
                long remappedId = remap.TryGetValue(
                    localId, out long replacement)
                    ? replacement
                    : localId;
                if (!remappedAnchorIds.Add(remappedId))
                {
                    throw new InvalidOperationException(
                        "The persistent id repair for '" + assetPath +
                        "' would create duplicate Unity YAML document id " +
                        remappedId + ". No text was changed.");
                }
            }

            string remappedYaml = anchorPattern.Replace(
                yaml,
                match => ReplaceMappedLocalId(match, remap));
            var localReferencePattern = new Regex(
                @"(?<prefix>\{fileID:\s*)(?<id>-?\d+)(?<suffix>\s*\})",
                RegexOptions.CultureInvariant);
            remappedYaml = localReferencePattern.Replace(
                remappedYaml,
                match => ReplaceMappedLocalId(match, remap));

            byte[] textBytes = new UTF8Encoding(false).GetBytes(remappedYaml);
            if (!hasUtf8Bom)
            {
                File.WriteAllBytes(GetAbsoluteAssetPath(assetPath), textBytes);
                return;
            }

            byte[] outputBytes = new byte[textBytes.Length + 3];
            outputBytes[0] = 0xEF;
            outputBytes[1] = 0xBB;
            outputBytes[2] = 0xBF;
            Buffer.BlockCopy(
                textBytes, 0, outputBytes, 3, textBytes.Length);
            File.WriteAllBytes(GetAbsoluteAssetPath(assetPath), outputBytes);
        }

        private static string ReplaceMappedLocalId(
            Match match,
            Dictionary<long, long> remap)
        {
            if (!long.TryParse(match.Groups["id"].Value, out long localId) ||
                !remap.TryGetValue(localId, out long replacement))
            {
                return match.Value;
            }

            return match.Groups["prefix"].Value + replacement +
                   match.Groups["suffix"].Value;
        }

        private static int ValidateUnityYamlHeader(
            byte[] bytes,
            string assetPath)
        {
            int offset = bytes != null && bytes.Length >= 3 &&
                         bytes[0] == 0xEF && bytes[1] == 0xBB &&
                         bytes[2] == 0xBF
                ? 3
                : 0;
            byte[] yamlHeader = Encoding.ASCII.GetBytes("%YAML 1.1");
            if (!HasBytePrefix(bytes, offset, yamlHeader))
            {
                throw new InvalidOperationException(
                    "The Prefab '" + assetPath +
                    "' is not a text-serialized Unity YAML asset with a valid %YAML header. " +
                    "Its persistent ids cannot be repaired safely; use Force Text asset serialization and try again.");
            }

            int secondLineOffset = offset + yamlHeader.Length;
            if (secondLineOffset < bytes.Length &&
                bytes[secondLineOffset] == (byte)'\r')
            {
                secondLineOffset++;
            }
            if (secondLineOffset >= bytes.Length ||
                bytes[secondLineOffset] != (byte)'\n')
            {
                throw new InvalidOperationException(
                    "The Prefab '" + assetPath +
                    "' has an invalid Unity YAML header. No text was changed.");
            }
            secondLineOffset++;

            byte[] unityTag = Encoding.ASCII.GetBytes(
                "%TAG !u! tag:unity3d.com,2011:");
            if (!HasBytePrefix(bytes, secondLineOffset, unityTag))
            {
                throw new InvalidOperationException(
                    "The Prefab '" + assetPath +
                    "' does not contain the Unity YAML tag directive. No text was changed.");
            }
            return offset;
        }

        private static bool HasBytePrefix(
            byte[] bytes,
            int offset,
            byte[] prefix)
        {
            if (bytes == null || prefix == null || offset < 0 ||
                bytes.Length - offset < prefix.Length)
            {
                return false;
            }
            for (int index = 0; index < prefix.Length; index++)
            {
                if (bytes[offset + index] != prefix[index])
                {
                    return false;
                }
            }
            return true;
        }

        private static string GetAbsoluteAssetPath(string assetPath)
        {
            return Path.GetFullPath(assetPath);
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

        private static List<ReferencingPrefabSnapshot>
            CaptureReferencingPrefabRootTransforms(string sourcePrefabPath)
        {
            List<ReferencingPrefabSnapshot> snapshots =
                new List<ReferencingPrefabSnapshot>();
            string[] assetPaths = AssetDatabase.GetAllAssetPaths();
            for (int assetIndex = 0;
                 assetIndex < assetPaths.Length;
                 assetIndex++)
            {
                string candidatePath = NormalizeAssetPath(assetPaths[assetIndex]);
                if (!candidatePath.StartsWith(
                        "Assets/", StringComparison.Ordinal) ||
                    !candidatePath.EndsWith(
                        ".prefab", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        candidatePath,
                        sourcePrefabPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !HasDirectDependency(
                        candidatePath,
                        sourcePrefabPath))
                {
                    continue;
                }

                GameObject contentsRoot = null;
                try
                {
                    contentsRoot =
                        PrefabUtility.LoadPrefabContents(candidatePath);
                    if (contentsRoot == null)
                    {
                        throw new InvalidOperationException(
                            "Unity could not load referencing Prefab '" +
                            candidatePath + "'.");
                    }

                    List<Transform> instanceRoots =
                        FindDirectPrefabInstanceRoots(
                            contentsRoot,
                            sourcePrefabPath);
                    if (instanceRoots.Count == 0)
                    {
                        continue;
                    }

                    ReferencingPrefabSnapshot snapshot =
                        new ReferencingPrefabSnapshot
                        {
                            AssetPath = candidatePath
                        };
                    for (int rootIndex = 0;
                         rootIndex < instanceRoots.Count;
                         rootIndex++)
                    {
                        snapshot.InstanceRootTransforms.Add(
                            new TransformSnapshot(instanceRoots[rootIndex]));
                    }
                    snapshots.Add(snapshot);
                }
                finally
                {
                    if (contentsRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(contentsRoot);
                    }
                }
            }

            return snapshots;
        }

        private static bool HasDirectDependency(
            string assetPath,
            string dependencyPath)
        {
            string[] dependencies =
                AssetDatabase.GetDependencies(assetPath, false);
            for (int dependencyIndex = 0;
                 dependencyIndex < dependencies.Length;
                 dependencyIndex++)
            {
                if (string.Equals(
                        NormalizeAssetPath(dependencies[dependencyIndex]),
                        dependencyPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void MigrateReferencingPrefabRootTransforms(
            List<ReferencingPrefabSnapshot> snapshots,
            string convertedPrefabPath,
            TransformSnapshot oldRootTransform)
        {
            for (int snapshotIndex = 0;
                 snapshotIndex < snapshots.Count;
                 snapshotIndex++)
            {
                ReferencingPrefabSnapshot snapshot = snapshots[snapshotIndex];
                GameObject contentsRoot = null;
                try
                {
                    contentsRoot =
                        PrefabUtility.LoadPrefabContents(snapshot.AssetPath);
                    if (contentsRoot == null)
                    {
                        throw new InvalidOperationException(
                            "Unity could not load referencing Prefab '" +
                            snapshot.AssetPath + "'.");
                    }

                    List<Transform> instanceRoots =
                        FindDirectPrefabInstanceRoots(
                            contentsRoot,
                            convertedPrefabPath);
                    ValidateInstanceRootCount(snapshot, instanceRoots);
                    for (int rootIndex = 0;
                         rootIndex < instanceRoots.Count;
                         rootIndex++)
                    {
                        ApplyWrapperDelta(
                            instanceRoots[rootIndex],
                            snapshot.InstanceRootTransforms[rootIndex],
                            oldRootTransform);
                    }

                    PrefabUtility.SaveAsPrefabAsset(
                        contentsRoot,
                        snapshot.AssetPath,
                        out bool savedSuccessfully);
                    if (!savedSuccessfully)
                    {
                        throw new InvalidOperationException(
                            "Unity could not migrate root transform overrides in referencing Prefab '" +
                            snapshot.AssetPath + "'.");
                    }
                }
                finally
                {
                    if (contentsRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(contentsRoot);
                    }
                }

                ImportSynchronously(snapshot.AssetPath);
            }
        }

        private static void RestoreReferencingPrefabRootTransforms(
            List<ReferencingPrefabSnapshot> snapshots,
            string restoredPrefabPath)
        {
            for (int snapshotIndex = 0;
                 snapshotIndex < snapshots.Count;
                 snapshotIndex++)
            {
                ReferencingPrefabSnapshot snapshot = snapshots[snapshotIndex];
                GameObject contentsRoot = null;
                try
                {
                    contentsRoot =
                        PrefabUtility.LoadPrefabContents(snapshot.AssetPath);
                    if (contentsRoot == null)
                    {
                        throw new InvalidOperationException(
                            "Unity could not load referencing Prefab '" +
                            snapshot.AssetPath + "'.");
                    }

                    List<Transform> instanceRoots =
                        FindDirectPrefabInstanceRoots(
                            contentsRoot,
                            restoredPrefabPath);
                    ValidateInstanceRootCount(snapshot, instanceRoots);
                    for (int rootIndex = 0;
                         rootIndex < instanceRoots.Count;
                         rootIndex++)
                    {
                        snapshot.InstanceRootTransforms[rootIndex].Apply(
                            instanceRoots[rootIndex]);
                    }

                    PrefabUtility.SaveAsPrefabAsset(
                        contentsRoot,
                        snapshot.AssetPath,
                        out bool savedSuccessfully);
                    if (!savedSuccessfully)
                    {
                        throw new InvalidOperationException(
                            "Unity could not restore root transform overrides in referencing Prefab '" +
                            snapshot.AssetPath + "'.");
                    }
                }
                finally
                {
                    if (contentsRoot != null)
                    {
                        PrefabUtility.UnloadPrefabContents(contentsRoot);
                    }
                }

                ImportSynchronously(snapshot.AssetPath);
            }
        }

        private static List<Transform> FindDirectPrefabInstanceRoots(
            GameObject contentsRoot,
            string sourcePrefabPath)
        {
            List<Transform> instanceRoots = new List<Transform>();
            Transform[] transforms =
                contentsRoot.GetComponentsInChildren<Transform>(true);
            for (int transformIndex = 0;
                 transformIndex < transforms.Length;
                 transformIndex++)
            {
                Transform candidate = transforms[transformIndex];
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(
                        candidate.gameObject))
                {
                    continue;
                }

                GameObject sourceObject =
                    PrefabUtility.GetCorrespondingObjectFromSource(
                        candidate.gameObject);
                if (sourceObject != null &&
                    string.Equals(
                        NormalizeAssetPath(
                            AssetDatabase.GetAssetPath(sourceObject)),
                        sourcePrefabPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    instanceRoots.Add(candidate);
                }
            }

            return instanceRoots;
        }

        private static void ValidateInstanceRootCount(
            ReferencingPrefabSnapshot snapshot,
            List<Transform> instanceRoots)
        {
            if (instanceRoots.Count ==
                snapshot.InstanceRootTransforms.Count)
            {
                return;
            }

            throw new InvalidOperationException(
                "Referencing Prefab '" + snapshot.AssetPath +
                "' contained " + snapshot.InstanceRootTransforms.Count +
                " direct instance(s) before conversion, but Unity exposed " +
                instanceRoots.Count + " afterward. The conversion cannot " +
                "migrate those transform overrides safely.");
        }

        private static void ApplyWrapperDelta(
            Transform instanceRoot,
            TransformSnapshot oldInstanceTransform,
            TransformSnapshot oldRootTransform)
        {
            if (Mathf.Approximately(oldRootTransform.Scale.x, 0f) ||
                Mathf.Approximately(oldRootTransform.Scale.y, 0f) ||
                Mathf.Approximately(oldRootTransform.Scale.z, 0f))
            {
                throw new InvalidOperationException(
                    "A positioned Prefab with a zero root scale cannot migrate existing instance transform overrides safely.");
            }

            Vector3 wrapperScale = new Vector3(
                oldInstanceTransform.Scale.x / oldRootTransform.Scale.x,
                oldInstanceTransform.Scale.y / oldRootTransform.Scale.y,
                oldInstanceTransform.Scale.z / oldRootTransform.Scale.z);
            Quaternion wrapperRotation =
                oldInstanceTransform.Rotation *
                Quaternion.Inverse(oldRootTransform.Rotation);
            Vector3 positionedTranslation =
                wrapperRotation *
                Vector3.Scale(wrapperScale, oldRootTransform.Position);

            instanceRoot.localPosition =
                oldInstanceTransform.Position - positionedTranslation;
            instanceRoot.localRotation = wrapperRotation;
            instanceRoot.localScale = wrapperScale;
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
                    EnsureIdentityMapLocalIds(
                        prefabPath,
                        originalGuid,
                        sourceIdentities);
                    ImportSynchronously(prefabPath);
                    restored =
                        AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
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

        private static void EnsureIdentityMapLocalIds(
            string prefabPath,
            string originalGuid,
            PrefabIdentityMap expected)
        {
            GameObject restored =
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            PrefabIdentityMap actual =
                CaptureOwnedIdentities(restored, originalGuid);
            if (expected == null ||
                actual.LocalIds.Count != expected.LocalIds.Count)
            {
                throw new InvalidOperationException(
                    "The restored Prefab hierarchy does not match the original persistent object map.");
            }

            Dictionary<long, long> remap = new Dictionary<long, long>();
            foreach (KeyValuePair<string, long> expectedPair in
                     expected.LocalIds)
            {
                if (!actual.LocalIds.TryGetValue(
                        expectedPair.Key, out long actualId))
                {
                    throw new InvalidOperationException(
                        "The restored Prefab is missing persistent object '" +
                        expectedPair.Key + "'.");
                }
                AddLocalIdMapping(remap, actualId, expectedPair.Value);
            }

            RemapUnityYamlLocalIds(prefabPath, remap);
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
