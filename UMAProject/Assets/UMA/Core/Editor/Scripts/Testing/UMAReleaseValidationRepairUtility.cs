#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public enum UMAReleaseDestinationScope
    {
        UMA2,
        UMA3,
        Universal
    }

    public sealed class UMAReleaseRepairResult
    {
        public bool succeeded;
        public string message;
        public string sourcePath;
        public string destinationPath;
        public int updatedReferenceCount;
    }

    public sealed class UMAReleaseAutoMovePlan
    {
        public string sourcePath;
        public UMAReleaseDestinationScope destinationScope;
        public string destinationFolder;
        public int referringAssetCount;
    }

    public sealed class UMAReleaseMaterialCleanupPlan
    {
        public string materialPath;
        public string shaderName;
        public readonly List<string> propertyEntries = new();

        public int PropertyCount => propertyEntries.Count;
    }

    public static class UMAReleaseValidationRepairUtility
    {
        private const string Uma2Root = "Assets/UMA2";
        private static string Uma3Root => UMAPathUtility.ResolveInstallAssetPath("UMA3");
        private static string UniversalRoot => UMAPathUtility.ResolveInstallAssetPath("Universal");
        private static string WritableUma3Root => UMAPathUtility.IsPackageInstallation
            ? UMAPathUtility.ProjectDataRoot + "/UMA3"
            : Uma3Root;
        private static string WritableUniversalRoot => UMAPathUtility.IsPackageInstallation
            ? UMAPathUtility.ProjectDataRoot + "/Universal"
            : UniversalRoot;

        private readonly struct SavedMaterialPropertyCollection
        {
            public readonly string path;
            public readonly string displayName;

            public SavedMaterialPropertyCollection(string path, string displayName)
            {
                this.path = path;
                this.displayName = displayName;
            }
        }

        private static readonly SavedMaterialPropertyCollection[] SavedMaterialProperties =
        {
            new("m_SavedProperties.m_TexEnvs", "Texture"),
            new("m_SavedProperties.m_Ints", "Integer"),
            new("m_SavedProperties.m_Floats", "Float/Range"),
            new("m_SavedProperties.m_Colors", "Color/Vector")
        };

        public static bool CanRelocate(UMAReleaseValidationIssueReport issue)
        {
            return issue != null && IsProjectAsset(issue.referencedAssetPath) &&
                !AssetDatabase.IsValidFolder(issue.referencedAssetPath) &&
                AssetDatabase.LoadMainAssetAtPath(issue.referencedAssetPath) != null;
        }

        public static bool CanDeleteSource(UMAReleaseValidationIssueReport issue)
        {
            return issue != null && IsProjectAsset(issue.ownerAssetPath) &&
                !AssetDatabase.IsValidFolder(issue.ownerAssetPath) &&
                AssetDatabase.LoadMainAssetAtPath(issue.ownerAssetPath) != null;
        }

        public static bool CanReserializeStaleSource(
            UMAReleaseValidationIssueReport issue, out string diagnosis)
        {
            diagnosis = string.Empty;
            if (issue == null || issue.kind != "Missing GUID reference")
                return false;
            if (!IsProjectAsset(issue.ownerAssetPath) ||
                AssetDatabase.IsValidFolder(issue.ownerAssetPath) ||
                AssetDatabase.LoadMainAssetAtPath(issue.ownerAssetPath) == null)
            {
                diagnosis = "The source asset cannot be loaded for reserialization.";
                return false;
            }
            if (TryBuildMaterialCleanupPlan(issue.ownerAssetPath, out _))
            {
                diagnosis = "This is a saved material property that is not used by the " +
                    "current shader. Use Remove All Non-Applicable Shader Properties; " +
                    "ordinary reserialization intentionally preserves material properties.";
                return false;
            }
            if (string.IsNullOrEmpty(issue.propertyPath))
            {
                diagnosis = "Run Asset Validation again to record the serialized field before " +
                    "deciding whether reserialization is safe.";
                return false;
            }
            InspectSerializedState(issue.ownerAssetPath, issue.propertyPath,
                out bool propertyExists, out bool hasMissingReference);
            if (hasMissingReference)
            {
                diagnosis = "This field still exists on the current serialized type, so the " +
                    "reference is genuinely missing. Restore or remap the referenced asset, " +
                    "or clear that field/list entry; reserialization will not repair it.";
                return false;
            }

            diagnosis = propertyExists
                ? "The field still exists, but the loaded asset has already removed or " +
                  "normalized the missing reference in memory. Force-reserializing will save " +
                  "that corrected state to disk."
                : "The serialized field is not present on the current asset type. It is stale " +
                  "YAML from a removed or renamed field and can be removed by forcing the " +
                  "source asset to reserialize.";
            return true;
        }

        public static UMAReleaseRepairResult ReserializeStaleSource(
            UMAReleaseValidationIssueReport issue)
        {
            UMAReleaseRepairResult result = NewResult(issue?.ownerAssetPath);
            if (!CanReserializeStaleSource(issue, out string diagnosis))
                return Fail(result, string.IsNullOrEmpty(diagnosis)
                    ? "This issue cannot be repaired safely by reserialization."
                    : diagnosis);

            string sourcePath = Normalize(issue.ownerAssetPath);
            try
            {
                UnityEngine.Object sourceAsset =
                    AssetDatabase.LoadMainAssetAtPath(sourcePath);
                AssetDatabase.SaveAssetIfDirty(sourceAsset);
                AssetDatabase.ForceReserializeAssets(
                    new List<string> { sourcePath },
                    ForceReserializeAssetsOptions.ReserializeAssets);
                AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceUpdate);

                string absolutePath = ProjectAbsolutePath(sourcePath);
                if (!string.IsNullOrEmpty(issue.referencedAssetGuid) &&
                    File.Exists(absolutePath) &&
                    File.ReadAllText(absolutePath).IndexOf(issue.referencedAssetGuid,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    return Fail(result, "Unity reserialized the source asset, but the missing " +
                        "GUID is still present. No issue was removed; repair the reference manually.");

                result.succeeded = true;
                result.message = "Reserialized " + sourcePath +
                    " and removed the stale serialized reference. Review the YAML change in " +
                    "source control.";
                return result;
            }
            catch (Exception exception)
            {
                return Fail(result, "Could not reserialize the source asset: " + exception.Message);
            }
        }

        public static bool CanRemoveNonApplicableShaderProperties(
            UMAReleaseValidationIssueReport issue)
        {
            return TryBuildMaterialCleanupPlan(issue?.ownerAssetPath, out _);
        }

        public static bool TryBuildMaterialCleanupPlan(string materialPath,
            out UMAReleaseMaterialCleanupPlan plan)
        {
            plan = null;
            materialPath = Normalize(materialPath);
            if (!IsProjectAsset(materialPath)) return false;
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!HasUsableShader(material)) return false;

            HashSet<string> supportedNames = GetShaderPropertyNames(material.shader);
            var candidate = new UMAReleaseMaterialCleanupPlan
            {
                materialPath = materialPath,
                shaderName = material.shader.name
            };
            using var serialized = new SerializedObject(material);
            serialized.Update();
            for (int collectionIndex = 0; collectionIndex < SavedMaterialProperties.Length;
                collectionIndex++)
            {
                SavedMaterialPropertyCollection collection =
                    SavedMaterialProperties[collectionIndex];
                SerializedProperty properties = serialized.FindProperty(collection.path);
                if (properties == null || !properties.isArray) continue;
                for (int propertyIndex = 0; propertyIndex < properties.arraySize;
                    propertyIndex++)
                {
                    SerializedProperty entry = properties.GetArrayElementAtIndex(propertyIndex);
                    string propertyName = GetSavedMaterialPropertyName(entry);
                    if (string.IsNullOrEmpty(propertyName) ||
                        supportedNames.Contains(propertyName)) continue;
                    candidate.propertyEntries.Add(collection.displayName + ": " + propertyName);
                }
            }

            if (candidate.PropertyCount == 0) return false;
            plan = candidate;
            return true;
        }

        public static List<UMAReleaseMaterialCleanupPlan> BuildAutoMaterialCleanupPlan(
            UMAReleaseValidationReport report)
        {
            var plans = new List<UMAReleaseMaterialCleanupPlan>();
            if (report?.issues == null) return plans;
            var materialPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int issueIndex = 0; issueIndex < report.issues.Count; issueIndex++)
            {
                UMAReleaseValidationIssueReport issue = report.issues[issueIndex];
                string ownerPath = Normalize(issue?.ownerAssetPath);
                if (!materialPaths.Add(ownerPath)) continue;
                if (TryBuildMaterialCleanupPlan(ownerPath, out
                    UMAReleaseMaterialCleanupPlan plan)) plans.Add(plan);
            }
            plans.Sort((left, right) => string.Compare(left.materialPath,
                right.materialPath, StringComparison.Ordinal));
            return plans;
        }

        public static UMAReleaseRepairResult RemoveNonApplicableShaderProperties(
            UMAReleaseValidationIssueReport issue)
        {
            var result = NewResult(issue?.ownerAssetPath);
            if (!TryBuildMaterialCleanupPlan(issue?.ownerAssetPath, out
                UMAReleaseMaterialCleanupPlan plan))
                return Fail(result, "The source material has no non-applicable shader properties, or its current shader cannot be inspected safely.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(plan.materialPath);
            int removed = RemoveNonApplicableShaderProperties(material, true);
            if (removed <= 0)
                return Fail(result, "No non-applicable shader properties were removed.");
            AssetDatabase.SaveAssetIfDirty(material);
            result.succeeded = true;
            result.updatedReferenceCount = removed;
            result.message = "Removed " + removed + " non-applicable shader " +
                (removed == 1 ? "property" : "properties") + " from " +
                plan.materialPath + ".";
            return result;
        }

        public static int RemoveNonApplicableShaderProperties(Material material,
            bool recordUndo)
        {
            if (!HasUsableShader(material)) return 0;
            HashSet<string> supportedNames = GetShaderPropertyNames(material.shader);
            using var serialized = new SerializedObject(material);
            serialized.Update();
            int removed = 0;
            bool undoRecorded = false;
            for (int collectionIndex = 0; collectionIndex < SavedMaterialProperties.Length;
                collectionIndex++)
            {
                SerializedProperty properties = serialized.FindProperty(
                    SavedMaterialProperties[collectionIndex].path);
                if (properties == null || !properties.isArray) continue;
                for (int propertyIndex = properties.arraySize - 1; propertyIndex >= 0;
                    propertyIndex--)
                {
                    SerializedProperty entry = properties.GetArrayElementAtIndex(propertyIndex);
                    string propertyName = GetSavedMaterialPropertyName(entry);
                    if (string.IsNullOrEmpty(propertyName) ||
                        supportedNames.Contains(propertyName)) continue;
                    if (recordUndo && !undoRecorded)
                    {
                        Undo.RecordObject(material,
                            "Remove non-applicable material shader properties");
                        undoRecorded = true;
                    }
                    properties.DeleteArrayElementAtIndex(propertyIndex);
                    removed++;
                }
            }
            if (removed <= 0) return 0;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(material);
            return removed;
        }

        public static UMAReleaseDestinationScope DestinationForIssue(
            UMAReleaseValidationIssueReport issue)
        {
            if (issue != null)
            {
                if (IsInFolder(issue.ownerAssetPath, Uma2Root))
                    return UMAReleaseDestinationScope.UMA2;
                if (IsInFolder(issue.ownerAssetPath, Uma3Root))
                    return UMAReleaseDestinationScope.UMA3;
                if (string.Equals(issue.scope, "UMA2", StringComparison.OrdinalIgnoreCase))
                    return UMAReleaseDestinationScope.UMA2;
            }
            return UMAReleaseDestinationScope.UMA3;
        }

        public static string GetDestinationFolder(string sourcePath,
            UMAReleaseDestinationScope destinationScope)
        {
            string root = destinationScope switch
            {
                UMAReleaseDestinationScope.UMA2 => Uma2Root,
                UMAReleaseDestinationScope.UMA3 => WritableUma3Root,
                _ => WritableUniversalRoot
            };
            return root + "/" + CategoryForAsset(sourcePath);
        }

        public static string GetProposedDestination(string sourcePath,
            UMAReleaseDestinationScope destinationScope)
        {
            if (string.IsNullOrEmpty(sourcePath)) return string.Empty;
            return GetDestinationFolder(sourcePath, destinationScope) + "/" +
                Path.GetFileName(sourcePath);
        }

        public static UMAReleaseRepairResult MoveReferencedAsset(
            UMAReleaseValidationIssueReport issue, UMAReleaseDestinationScope destinationScope)
        {
            var result = NewResult(issue?.referencedAssetPath);
            if (!CanRelocate(issue)) return Fail(result, "The referenced asset cannot be moved.");

            string sourcePath = Normalize(issue.referencedAssetPath);
            string folder = GetDestinationFolder(sourcePath, destinationScope);
            if (!EnsureFolder(folder)) return Fail(result, "Could not create " + folder + ".");
            if (IsInFolder(sourcePath, folder))
                return Fail(result, "The asset is already in the destination folder.");

            string destination = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/" + Path.GetFileName(sourcePath));
            string error = AssetDatabase.MoveAsset(sourcePath, destination);
            if (!string.IsNullOrEmpty(error)) return Fail(result, error);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            result.succeeded = true;
            result.destinationPath = destination;
            result.message = "Moved asset to " + destination + ". Its GUID was preserved.";
            return result;
        }

        public static UMAReleaseRepairResult CopyAndRetarget(
            UMAReleaseValidationReport report, UMAReleaseValidationIssueReport issue)
        {
            var result = NewResult(issue?.referencedAssetPath);
            if (!CanRelocate(issue)) return Fail(result, "The referenced asset cannot be copied.");

            UMAReleaseDestinationScope scope = DestinationForIssue(issue);
            string sourcePath = Normalize(issue.referencedAssetPath);
            string folder = GetDestinationFolder(sourcePath, scope);
            if (!EnsureFolder(folder)) return Fail(result, "Could not create " + folder + ".");
            string destination = AssetDatabase.GenerateUniqueAssetPath(
                folder + "/" + Path.GetFileName(sourcePath));
            if (!AssetDatabase.CopyAsset(sourcePath, destination))
                return Fail(result, "Unity could not copy the asset.");

            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            string oldGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            string newGuid = AssetDatabase.AssetPathToGUID(destination);
            List<string> referrers = FindReferrers(report, issue, scope);
            int changed = 0;
            var failures = new List<string>();
            for (int i = 0; i < referrers.Count; i++)
            {
                int referrerChanges = RetargetAsset(referrers[i], sourcePath, destination,
                    oldGuid, newGuid, out string error);
                changed += referrerChanges;
                if (!string.IsNullOrEmpty(error)) failures.Add(error);
                else if (referrerChanges == 0)
                    failures.Add("No writable reference was found in " + referrers[i]);
            }

            if (changed == 0 || failures.Count > 0)
            {
                if (changed > 0)
                {
                    for (int i = 0; i < referrers.Count; i++)
                    {
                        RetargetAsset(referrers[i], destination, sourcePath, newGuid,
                            oldGuid, out string rollbackError);
                        if (!string.IsNullOrEmpty(rollbackError))
                            failures.Add("Rollback: " + rollbackError);
                    }
                    AssetDatabase.SaveAssets();
                }
                AssetDatabase.DeleteAsset(destination);
                AssetDatabase.Refresh();
                string message = changed == 0
                    ? "No writable reference to the original asset was found. The copy was rolled back."
                    : "Some references could not be updated. The copy was rolled back: " +
                      string.Join("; ", failures);
                return Fail(result, message);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            result.succeeded = true;
            result.destinationPath = destination;
            result.updatedReferenceCount = changed;
            result.message = "Copied asset to " + destination + " and updated " + changed +
                " reference" + (changed == 1 ? "." : "s.");
            return result;
        }

        public static UMAReleaseRepairResult DeleteSourceAsset(
            UMAReleaseValidationIssueReport issue)
        {
            var result = NewResult(issue?.ownerAssetPath);
            if (!CanDeleteSource(issue)) return Fail(result, "The source asset cannot be deleted.");
            string source = Normalize(issue.ownerAssetPath);
            if (!AssetDatabase.MoveAssetToTrash(source))
                return Fail(result, "Unity could not move " + source + " to the recycle bin.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            result.succeeded = true;
            result.message = "Deleted source asset " + source +
                " (moved to the recycle bin).";
            return result;
        }

        public static List<UMAReleaseAutoMovePlan> BuildAutoMovePlan(
            UMAReleaseValidationReport report)
        {
            var result = new List<UMAReleaseAutoMovePlan>();
            if (report == null) return result;
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < report.issues.Count; i++)
            {
                UMAReleaseValidationIssueReport issue = report.issues[i];
                if (issue == null || !issue.kind.StartsWith("Out-of-package",
                    StringComparison.Ordinal) || !IsProjectAsset(issue.referencedAssetPath) ||
                    AssetDatabase.LoadMainAssetAtPath(issue.referencedAssetPath) == null) continue;
                candidates.Add(Normalize(issue.referencedAssetPath));
            }

            foreach (string candidate in candidates)
            {
                var sourceAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var destinations = new HashSet<UMAReleaseDestinationScope>();
                bool ambiguous = false;
                for (int i = 0; i < report.references.Count; i++)
                {
                    UMAReleaseValidationReferenceReport reference = report.references[i];
                    if (reference == null || !SameReferencedAsset(reference, candidate) ||
                        string.Equals(reference.sourceAssetPath, candidate,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    string source = Normalize(reference.sourceAssetPath);
                    if (MaterialReferencesAssetOnlyThroughNonApplicableProperties(source,
                        candidate)) continue;
                    if (IsInFolder(source, Uma2Root))
                        destinations.Add(UMAReleaseDestinationScope.UMA2);
                    else if (IsInFolder(source, Uma3Root))
                        destinations.Add(UMAReleaseDestinationScope.UMA3);
                    else
                    {
                        ambiguous = true;
                        break;
                    }
                    sourceAssets.Add(source);
                }
                if (ambiguous || destinations.Count != 1 || sourceAssets.Count == 0) continue;
                UMAReleaseDestinationScope destination = First(destinations);
                string folder = GetDestinationFolder(candidate, destination);
                if (IsInFolder(candidate, folder)) continue;
                result.Add(new UMAReleaseAutoMovePlan
                {
                    sourcePath = candidate,
                    destinationScope = destination,
                    destinationFolder = folder,
                    referringAssetCount = sourceAssets.Count
                });
            }
            result.Sort((left, right) => string.Compare(left.sourcePath, right.sourcePath,
                StringComparison.Ordinal));
            return result;
        }

        public static UMAReleaseRepairResult ExecuteAutoRepair(
            IList<UMAReleaseMaterialCleanupPlan> materialPlans,
            IList<UMAReleaseAutoMovePlan> movePlans)
        {
            var result = NewResult(string.Empty);
            int materialCount = 0;
            int removedPropertyCount = 0;
            int movedAssetCount = 0;
            var errors = new List<string>();
            int cleanupCount = materialPlans?.Count ?? 0;
            int moveCount = movePlans?.Count ?? 0;
            int operationCount = cleanupCount + moveCount;
            if (operationCount == 0)
                return Fail(result, "There are no automatic material cleanups or unambiguous asset moves.");

            try
            {
                for (int i = 0; i < cleanupCount; i++)
                {
                    UMAReleaseMaterialCleanupPlan plan = materialPlans[i];
                    EditorUtility.DisplayProgressBar("UMA Release Asset Validation",
                        "Cleaning " + plan.materialPath, (float)i / operationCount);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(
                        plan.materialPath);
                    if (!HasUsableShader(material))
                    {
                        errors.Add(plan.materialPath + ": current shader is unavailable.");
                        continue;
                    }
                    int removed = RemoveNonApplicableShaderProperties(material, true);
                    if (removed <= 0) continue;
                    removedPropertyCount += removed;
                    materialCount++;
                }
                AssetDatabase.SaveAssets();

                for (int i = 0; i < moveCount; i++)
                {
                    UMAReleaseAutoMovePlan plan = movePlans[i];
                    EditorUtility.DisplayProgressBar("UMA Release Asset Validation",
                        "Moving " + plan.sourcePath,
                        (float)(cleanupCount + i) / operationCount);
                    if (AssetDatabase.LoadMainAssetAtPath(plan.sourcePath) == null) continue;
                    if (!EnsureFolder(plan.destinationFolder))
                    {
                        errors.Add("Could not create " + plan.destinationFolder);
                        continue;
                    }
                    string destination = AssetDatabase.GenerateUniqueAssetPath(
                        plan.destinationFolder + "/" + Path.GetFileName(plan.sourcePath));
                    string error = AssetDatabase.MoveAsset(plan.sourcePath, destination);
                    if (string.IsNullOrEmpty(error)) movedAssetCount++;
                    else errors.Add(plan.sourcePath + ": " + error);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            result.succeeded = errors.Count == 0 &&
                (removedPropertyCount > 0 || movedAssetCount > 0);
            result.updatedReferenceCount = removedPropertyCount + movedAssetCount;
            result.message = "Auto cleaned " + materialCount + " material" +
                (materialCount == 1 ? string.Empty : "s") + " (" +
                removedPropertyCount + " non-applicable shader " +
                (removedPropertyCount == 1 ? "property" : "properties") + ") and moved " +
                movedAssetCount + " asset" + (movedAssetCount == 1 ? "." : "s.");
            if (errors.Count > 0) result.message += " Errors: " + string.Join("; ", errors);
            return result;
        }

        public static UMAReleaseRepairResult ExecuteMaterialCleanupPlan(
            IList<UMAReleaseMaterialCleanupPlan> materialPlans)
        {
            var result = NewResult(string.Empty);
            if (materialPlans == null || materialPlans.Count == 0)
                return Fail(result, "There are no affected materials with non-applicable shader properties.");

            int materialCount = 0;
            int removedPropertyCount = 0;
            var errors = new List<string>();
            var processedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (int planIndex = 0; planIndex < materialPlans.Count; planIndex++)
                {
                    UMAReleaseMaterialCleanupPlan plan = materialPlans[planIndex];
                    string materialPath = Normalize(plan?.materialPath);
                    if (!processedMaterials.Add(materialPath)) continue;
                    EditorUtility.DisplayProgressBar("UMA Release Asset Validation",
                        "Cleaning " + materialPath,
                        (float)planIndex / materialPlans.Count);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (!HasUsableShader(material))
                    {
                        errors.Add(materialPath + ": current shader is unavailable.");
                        continue;
                    }
                    int removed = RemoveNonApplicableShaderProperties(material, true);
                    if (removed <= 0) continue;
                    removedPropertyCount += removed;
                    materialCount++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            result.succeeded = errors.Count == 0 && removedPropertyCount > 0;
            result.updatedReferenceCount = removedPropertyCount;
            result.message = "Cleaned " + materialCount + " material" +
                (materialCount == 1 ? string.Empty : "s") + " and removed " +
                removedPropertyCount + " non-applicable shader " +
                (removedPropertyCount == 1 ? "property." : "properties.");
            if (errors.Count > 0) result.message += " Errors: " + string.Join("; ", errors);
            return result;
        }

        public static UMAReleaseRepairResult ExecuteAutoMovePlan(
            IList<UMAReleaseAutoMovePlan> plans)
        {
            var result = NewResult(string.Empty);
            if (plans == null || plans.Count == 0)
                return Fail(result, "There are no unambiguous assets to move.");
            int moved = 0;
            var errors = new List<string>();
            try
            {
                for (int i = 0; i < plans.Count; i++)
                {
                    UMAReleaseAutoMovePlan plan = plans[i];
                    EditorUtility.DisplayProgressBar("UMA Release Asset Validation",
                        "Moving " + plan.sourcePath, (float)i / plans.Count);
                    if (AssetDatabase.LoadMainAssetAtPath(plan.sourcePath) == null) continue;
                    if (!EnsureFolder(plan.destinationFolder))
                    {
                        errors.Add("Could not create " + plan.destinationFolder);
                        continue;
                    }
                    string destination = AssetDatabase.GenerateUniqueAssetPath(
                        plan.destinationFolder + "/" + Path.GetFileName(plan.sourcePath));
                    string error = AssetDatabase.MoveAsset(plan.sourcePath, destination);
                    if (string.IsNullOrEmpty(error)) moved++;
                    else errors.Add(plan.sourcePath + ": " + error);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            result.succeeded = errors.Count == 0 && moved > 0;
            result.updatedReferenceCount = moved;
            result.message = "Auto moved " + moved + " asset" + (moved == 1 ? "." : "s.");
            if (errors.Count > 0) result.message += " Errors: " + string.Join("; ", errors);
            return result;
        }

        private static List<string> FindReferrers(UMAReleaseValidationReport report,
            UMAReleaseValidationIssueReport issue, UMAReleaseDestinationScope destinationScope)
        {
            var result = new List<string>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string root = destinationScope == UMAReleaseDestinationScope.UMA2
                ? Uma2Root : Uma3Root;
            if (report != null)
            {
                for (int i = 0; i < report.references.Count; i++)
                {
                    UMAReleaseValidationReferenceReport reference = report.references[i];
                    if (reference == null || !SameReferencedAsset(reference,
                        issue.referencedAssetPath) ||
                        !IsInFolder(reference.sourceAssetPath, root)) continue;
                    string source = Normalize(reference.sourceAssetPath);
                    if (unique.Add(source)) result.Add(source);
                }
            }
            if (result.Count == 0 && IsInFolder(issue.ownerAssetPath, root))
                result.Add(Normalize(issue.ownerAssetPath));
            return result;
        }

        private static int RetargetAsset(string ownerPath, string oldAssetPath,
            string newAssetPath, string oldGuid, string newGuid, out string error)
        {
            error = string.Empty;
            if (!IsProjectAsset(ownerPath))
            {
                error = "Referrer is not writable: " + ownerPath;
                return 0;
            }
            int changed = 0;
            try
            {
                UnityEngine.Object[] owners = AssetDatabase.LoadAllAssetsAtPath(ownerPath);
                for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
                {
                    UnityEngine.Object owner = owners[ownerIndex];
                    if (owner == null) continue;
                    using var serialized = new SerializedObject(owner);
                    SerializedProperty property = serialized.GetIterator();
                    bool recordedUndo = false;
                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference ||
                            property.objectReferenceValue == null) continue;
                        string currentPath = Normalize(
                            AssetDatabase.GetAssetPath(property.objectReferenceValue));
                        if (!string.Equals(currentPath, oldAssetPath,
                            StringComparison.OrdinalIgnoreCase)) continue;
                        UnityEngine.Object replacement = FindEquivalentObject(
                            property.objectReferenceValue, newAssetPath);
                        if (replacement == null) continue;
                        if (!recordedUndo)
                        {
                            Undo.RecordObject(owner, "Retarget release asset reference");
                            recordedUndo = true;
                        }
                        property.objectReferenceValue = replacement;
                        changed++;
                    }
                    if (recordedUndo)
                    {
                        serialized.ApplyModifiedProperties();
                        EditorUtility.SetDirty(owner);
                    }
                }
                AssetDatabase.SaveAssets();

                // Importer metadata and unresolved YAML references are not exposed by
                // SerializedObject. GUID replacement is safe because copied assets retain
                // their local file IDs and only the 32-character asset GUID changes.
                changed += ReplaceGuid(ownerPath, oldGuid, newGuid);
                changed += ReplaceGuid(ownerPath + ".meta", oldGuid, newGuid);
                if (changed > 0)
                    AssetDatabase.ImportAsset(ownerPath, ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception exception)
            {
                error = ownerPath + ": " + exception.Message;
            }
            return changed;
        }

        private static int ReplaceGuid(string projectPath, string oldGuid, string newGuid)
        {
            if (string.IsNullOrEmpty(oldGuid) || string.IsNullOrEmpty(newGuid)) return 0;
            string fullPath = ProjectAbsolutePath(projectPath);
            if (!File.Exists(fullPath)) return 0;
            string text;
            try { text = File.ReadAllText(fullPath); }
            catch { return 0; }
            string pattern = @"(\bguid:\s*)" + Regex.Escape(oldGuid) + @"\b";
            int count = Regex.Matches(text, pattern, RegexOptions.IgnoreCase).Count;
            if (count == 0) return 0;
            string updated = Regex.Replace(text, pattern, "${1}" + newGuid,
                RegexOptions.IgnoreCase);
            File.WriteAllText(fullPath, updated, new UTF8Encoding(false));
            return count;
        }

        private static UnityEngine.Object FindEquivalentObject(UnityEngine.Object original,
            string newAssetPath)
        {
            UnityEngine.Object[] candidates = AssetDatabase.LoadAllAssetsAtPath(newAssetPath);
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && candidates[i].GetType() == original.GetType() &&
                    string.Equals(candidates[i].name, original.name, StringComparison.Ordinal))
                    return candidates[i];
            for (int i = 0; i < candidates.Length; i++)
                if (candidates[i] != null && original.GetType().IsAssignableFrom(
                    candidates[i].GetType())) return candidates[i];
            return null;
        }

        private static string CategoryForAsset(string path)
        {
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset is SlotDataAsset) return "Slots";
            if (asset is OverlayDataAsset) return "Overlays";
            if (asset is RaceData) return "Races";
            if (asset is UmaTPose) return "TPose";
            if (asset is UMABonePose) return "BonePoses";
            if (asset is UMAExpressionSet || asset is UMAExpressionGroup) return "Expressions";
            if (asset is Texture) return "Textures";
            if (asset is Material) return "Materials";
            if (asset is AnimationClip || asset is RuntimeAnimatorController) return "Animation";
            if (asset is AudioClip) return "Audio";
            if (asset is GameObject && string.Equals(Path.GetExtension(path), ".prefab",
                StringComparison.OrdinalIgnoreCase)) return "Prefabs";
            if (AssetImporter.GetAtPath(path) is ModelImporter) return "Models";
            string typeName = asset == null ? string.Empty : asset.GetType().Name;
            if (typeName.IndexOf("DNA", StringComparison.OrdinalIgnoreCase) >= 0) return "DNA";
            if (asset is Shader) return "Shaders";
            return "Data";
        }

        private static bool EnsureFolder(string folder)
        {
            folder = Normalize(folder).Trim('/');
            if (AssetDatabase.IsValidFolder(folder)) return true;
            string[] parts = folder.Split('/');
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets",
                StringComparison.OrdinalIgnoreCase)) return false;
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(guid)) return false;
                }
                current = next;
            }
            return AssetDatabase.IsValidFolder(folder);
        }

        private static bool SameReferencedAsset(UMAReleaseValidationReferenceReport reference,
            string path)
        {
            if (string.Equals(Normalize(reference.referencedAssetPath), Normalize(path),
                StringComparison.OrdinalIgnoreCase)) return true;
            string guid = AssetDatabase.AssetPathToGUID(path);
            return !string.IsNullOrEmpty(guid) && string.Equals(reference.referencedAssetGuid,
                guid, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasUsableShader(Material material)
        {
            return material != null && material.shader != null &&
                !string.Equals(material.shader.name, "Hidden/InternalErrorShader",
                    StringComparison.Ordinal);
        }

        private static HashSet<string> GetShaderPropertyNames(Shader shader)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (shader == null) return names;
            int propertyCount = shader.GetPropertyCount();
            for (int propertyIndex = 0; propertyIndex < propertyCount; propertyIndex++)
            {
                string propertyName = shader.GetPropertyName(propertyIndex);
                if (!string.IsNullOrEmpty(propertyName)) names.Add(propertyName);
            }
            return names;
        }

        private static string GetSavedMaterialPropertyName(SerializedProperty entry)
        {
            SerializedProperty name = entry?.FindPropertyRelative("first");
            return name != null ? name.stringValue : string.Empty;
        }

        private static void InspectSerializedState(string assetPath, string yamlPropertyPath,
            out bool propertyExists, out bool hasMissingReference)
        {
            propertyExists = false;
            hasMissingReference = false;
            string[] segments = yamlPropertyPath.Split('.');
            string leaf = segments.Length == 0 ? yamlPropertyPath : segments[segments.Length - 1];
            leaf = leaf.Replace("[]", string.Empty);
            UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int objectIndex = 0; objectIndex < objects.Length; objectIndex++)
            {
                UnityEngine.Object target = objects[objectIndex];
                if (target == null) continue;
                try
                {
                    using var serialized = new SerializedObject(target);
                    SerializedProperty property = serialized.GetIterator();
                    while (property.Next(true))
                    {
                        if (string.Equals(property.name, leaf, StringComparison.Ordinal) ||
                            string.Equals(property.propertyPath, yamlPropertyPath,
                                StringComparison.Ordinal))
                            propertyExists = true;
                        if (property.propertyType == SerializedPropertyType.ObjectReference &&
                            property.objectReferenceValue == null)
#if UNITY_6000_5_OR_NEWER
                            if (!property.objectReferenceEntityIdValue.IsValid())
                                hasMissingReference = true;
#else
                            if (property.objectReferenceInstanceIDValue != 0)
                                hasMissingReference = true;
#endif
                    }
                }
                catch
                {
                    // Inspection failure is conservative: do not offer a destructive rewrite.
                    propertyExists = true;
                    hasMissingReference = true;
                    return;
                }
            }
        }

        private static bool MaterialReferencesAssetOnlyThroughNonApplicableProperties(
            string materialPath, string referencedAssetPath)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (!HasUsableShader(material)) return false;
            HashSet<string> supportedNames = GetShaderPropertyNames(material.shader);
            bool foundReference = false;
            using var serialized = new SerializedObject(material);
            SerializedProperty property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference ||
                    property.objectReferenceValue == null) continue;
                string currentPath = Normalize(
                    AssetDatabase.GetAssetPath(property.objectReferenceValue));
                if (!string.Equals(currentPath, referencedAssetPath,
                    StringComparison.OrdinalIgnoreCase)) continue;
                foundReference = true;
                if (!TryGetSavedMaterialPropertyName(serialized, property.propertyPath,
                    out string propertyName) || supportedNames.Contains(propertyName))
                    return false;
            }
            return foundReference;
        }

        private static bool TryGetSavedMaterialPropertyName(SerializedObject serialized,
            string propertyPath, out string propertyName)
        {
            propertyName = string.Empty;
            for (int collectionIndex = 0; collectionIndex < SavedMaterialProperties.Length;
                collectionIndex++)
            {
                string collectionPath = SavedMaterialProperties[collectionIndex].path;
                string prefix = collectionPath + ".Array.data[";
                if (!propertyPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
                int endIndex = propertyPath.IndexOf(']', prefix.Length);
                if (endIndex < 0 || !int.TryParse(propertyPath.Substring(prefix.Length,
                    endIndex - prefix.Length), out int arrayIndex)) return false;
                SerializedProperty properties = serialized.FindProperty(collectionPath);
                if (properties == null || !properties.isArray || arrayIndex < 0 ||
                    arrayIndex >= properties.arraySize) return false;
                propertyName = GetSavedMaterialPropertyName(
                    properties.GetArrayElementAtIndex(arrayIndex));
                return !string.IsNullOrEmpty(propertyName);
            }
            return false;
        }

        private static UMAReleaseDestinationScope First(
            HashSet<UMAReleaseDestinationScope> values)
        {
            foreach (UMAReleaseDestinationScope value in values) return value;
            return UMAReleaseDestinationScope.UMA3;
        }

        private static bool IsProjectAsset(string path) => !string.IsNullOrEmpty(path) &&
            Normalize(path).StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);

        private static bool IsInFolder(string path, string folder)
        {
            path = Normalize(path).TrimEnd('/');
            folder = Normalize(folder).TrimEnd('/');
            return string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string ProjectAbsolutePath(string projectPath)
        {
            string root = Path.GetDirectoryName(Application.dataPath) ??
                Directory.GetCurrentDirectory();
            return Path.GetFullPath(Path.Combine(root, Normalize(projectPath)));
        }

        private static string Normalize(string path) => string.IsNullOrEmpty(path)
            ? string.Empty : path.Replace('\\', '/');

        private static UMAReleaseRepairResult NewResult(string sourcePath) =>
            new() { sourcePath = Normalize(sourcePath) };

        private static UMAReleaseRepairResult Fail(UMAReleaseRepairResult result, string message)
        {
            result.succeeded = false;
            result.message = message;
            return result;
        }
    }
}

#endif
