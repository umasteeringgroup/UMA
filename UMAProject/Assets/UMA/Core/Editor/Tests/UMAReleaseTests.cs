#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    [TestFixture(TestName = "UMA Release Tests")]
    [Category("UMA")]
    [Category("UMA Release Tests")]
    public sealed class UMAReleaseTests
    {
        private static string UmaFolder => UMAPathUtility.InstallAssetRoot;
        private static string Uma3Folder => UMAPathUtility.ResolveInstallAssetPath("UMA3");
        private const string Uma2Folder = "Assets/UMA2";
        private static readonly Regex GuidReference = new Regex(
            @"\bguid:\s*([0-9a-fA-F]{32})\b", RegexOptions.Compiled);

        [TestCase(TestName = "Asset Validation")]
        [Category("Asset Validation")]
        [Timeout(300000)]
        public void AssetValidation()
        {
            var issues = new List<ValidationIssue>();
            var issueKeys = new HashSet<string>(StringComparer.Ordinal);
            var referenceKeys = new HashSet<string>(StringComparer.Ordinal);
            var structuredReport = new UMAReleaseValidationReport
            {
                generatedUtc = DateTime.UtcNow.ToString("O"),
                unityVersion = Application.unityVersion,
                projectPath = NormalizePath(Path.GetDirectoryName(Application.dataPath))
            };
            ValidationSummary uma3 = null;
            ValidationSummary uma2 = null;

            try
            {
                uma3 = ValidateScope(new ValidationScope(
                    "UMA3", Uma3Folder, new[] { UmaFolder }), issues, issueKeys,
                    structuredReport, referenceKeys);
                uma2 = ValidateScope(new ValidationScope(
                    "UMA2", Uma2Folder, new[] { UmaFolder, Uma2Folder }), issues, issueKeys,
                    structuredReport, referenceKeys);
            }
            catch (Exception exception)
            {
                AddIssue(issues, issueKeys, new ValidationIssue("Test",
                    "Unhandled validation exception", string.Empty, exception.ToString()));
            }
            finally
            {
                CompleteAndWriteReport(structuredReport, issues);
            }

            if (uma3 != null) TestContext.WriteLine(uma3.ToString());
            if (uma2 != null) TestContext.WriteLine(uma2.ToString());
            TestContext.WriteLine("Structured report: " +
                UMAReleaseValidationReport.GetAbsolutePath());
            if (issues.Count == 0) return;

            var report = new StringBuilder();
            report.AppendLine($"Asset Validation found {issues.Count} package-boundary " +
                $"issue{(issues.Count == 1 ? string.Empty : "s")}.");
            report.AppendLine($"UMA3 assets may reference {UmaFolder} only. " +
                $"UMA2 assets may reference {UmaFolder} or Assets/UMA2.");
            report.AppendLine("Package Manager and Unity built-in resources are external prerequisites " +
                "and are not treated as exported project assets.");
            for (int i = 0; i < issues.Count; i++)
                report.AppendLine($"{i + 1}. {issues[i]}");
            Assert.Fail(report.ToString());
        }

        private static ValidationSummary ValidateScope(ValidationScope scope,
            List<ValidationIssue> issues, HashSet<string> issueKeys,
            UMAReleaseValidationReport report, HashSet<string> referenceKeys)
        {
            if (!AssetDatabase.IsValidFolder(scope.sourceFolder))
            {
                AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                    "Release folder missing", scope.sourceFolder,
                    $"Release asset folder is missing: {scope.sourceFolder}"));
                var missingSummary = new ValidationSummary(scope, 0, 0,
                    new Dictionary<string, int>(StringComparer.Ordinal));
                report.scopes.Add(missingSummary.ToReport());
                return missingSummary;
            }
            string[] guids = AssetDatabase.FindAssets(string.Empty,
                new[] { scope.sourceFolder });
            var owners = new List<ReleaseAsset>();
            var categoryCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < guids.Length; i++)
            {
                string path = NormalizePath(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) continue;
                Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
                string category = ReleaseCategory(path, type);
                if (category == null) continue;
                owners.Add(new ReleaseAsset(path, category, TypeName(type)));
                report.assets.Add(CreateAssetReport(scope.name, category, path, type));
                categoryCounts.TryGetValue(category, out int count);
                categoryCounts[category] = count + 1;
            }

            if (owners.Count == 0)
                AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                    "No release assets", scope.sourceFolder,
                    $"No release assets were discovered under {scope.sourceFolder}."));
            var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int ownerIndex = 0; ownerIndex < owners.Count; ownerIndex++)
            {
                ReleaseAsset owner = owners[ownerIndex];
                string[] dependencies;
                try
                {
                    dependencies = AssetDatabase.GetDependencies(owner.path, true);
                    RecordDirectDependencies(scope, owner, report, referenceKeys);
                }
                catch (Exception exception)
                {
                    AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                        "Dependency scan failed", owner.path, exception.Message));
                    continue;
                }

                closure.Add(owner.path);
                for (int dependencyIndex = 0; dependencyIndex < dependencies.Length;
                    dependencyIndex++)
                {
                    string dependency = NormalizePath(dependencies[dependencyIndex]);
                    if (string.IsNullOrEmpty(dependency) || IsExternalPrerequisite(dependency))
                        continue;
                    if (!IsAllowed(scope, dependency))
                    {
                        AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                            "Out-of-package dependency", owner.path, dependency,
                            dependency, AssetDatabase.AssetPathToGUID(dependency)));
                        continue;
                    }
                    closure.Add(dependency);
                }
            }

            foreach (string path in closure)
            {
                ValidateSerializedGuids(scope, path, path, issues, issueKeys,
                    report, referenceKeys);
                ValidateSerializedGuids(scope, path + ".meta", path, issues, issueKeys,
                    report, referenceKeys);
                ValidateLoadedObjectReferences(scope, path, issues, issueKeys,
                    report, referenceKeys);
            }
            var summary = new ValidationSummary(scope, owners.Count, closure.Count,
                categoryCounts);
            report.scopes.Add(summary.ToReport());
            return summary;
        }

        private static string ReleaseCategory(string path, Type type)
        {
            if (type != null)
            {
                if (typeof(UmaTPose).IsAssignableFrom(type)) return "TPose";
                if (typeof(RaceData).IsAssignableFrom(type)) return "RaceData";
                if (typeof(SlotDataAsset).IsAssignableFrom(type)) return "Slot";
                if (typeof(OverlayDataAsset).IsAssignableFrom(type)) return "Overlay";
                if (typeof(Texture).IsAssignableFrom(type)) return "Texture";
                if (typeof(UMAExpressionSet).IsAssignableFrom(type) ||
                    typeof(UMAExpressionGroup).IsAssignableFrom(type)) return "Expression Set";
                if (typeof(UMABonePose).IsAssignableFrom(type)) return "Bone Pose";
                if (typeof(DNA).IsAssignableFrom(type) ||
                    typeof(DNAGroup).IsAssignableFrom(type) ||
                    typeof(DNACurve).IsAssignableFrom(type) ||
                    typeof(DynamicUMADnaAsset).IsAssignableFrom(type) ||
                    typeof(DNARangeAsset).IsAssignableFrom(type) ||
                    typeof(DynamicDNAConverterController).IsAssignableFrom(type) ||
                    typeof(DynamicDNAPlugin).IsAssignableFrom(type) ||
                    typeof(DNAEvaluationGraphPresetLibrary).IsAssignableFrom(type)) return "DNA";
            }

            // A missing script prevents Unity from resolving the main asset type. Keep known data
            // folders in the validation set so their unresolved script GUID is still reported.
            bool unresolvedAssetType = type == null ||
                (type == typeof(DefaultAsset) &&
                 string.Equals(Path.GetExtension(path), ".asset",
                     StringComparison.OrdinalIgnoreCase));
            if (!unresolvedAssetType) return null;
            string normalized = "/" + NormalizePath(path).Trim('/') + "/";
            if (normalized.IndexOf("/DNA/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "DNA";
            if (normalized.IndexOf("/BonePose", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Bone Pose";
            if (normalized.IndexOf("/Expressions/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Expression Set";
            if (normalized.IndexOf("/TPose", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TPose";
            if (normalized.IndexOf("/RaceData/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "RaceData";
            if (normalized.IndexOf("/Slots/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Slot";
            if (normalized.IndexOf("/Overlays/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Overlay";
            if (normalized.IndexOf("/Textures/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Texture";
            return null;
        }

        private static void ValidateSerializedGuids(ValidationScope scope, string filePath,
            string assetPath, List<ValidationIssue> issues, HashSet<string> issueKeys,
            UMAReleaseValidationReport report, HashSet<string> referenceKeys)
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath) || !IsTextSerializedFile(fullPath)) return;
            string text;
            try { text = File.ReadAllText(fullPath); }
            catch (Exception exception)
            {
                AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                    "Serialized data could not be read", assetPath, exception.Message));
                return;
            }

            MatchCollection matches = GuidReference.Matches(text);
            string ownerGuid = AssetDatabase.AssetPathToGUID(assetPath);
            for (int i = 0; i < matches.Count; i++)
            {
                string propertyPath = FindSerializedGuidPropertyPath(
                    text, matches[i].Index, out int sourceLine);
                string guid = matches[i].Groups[1].Value.ToLowerInvariant();
                if (filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(guid, ownerGuid, StringComparison.OrdinalIgnoreCase)) continue;
                if (IsUnityBuiltinGuid(guid))
                {
                    AddReference(report, referenceKeys, CreateReference(scope, assetPath,
                        filePath, "Serialized GUID", propertyPath, string.Empty, guid,
                        "Built-in prerequisite", true, string.Empty, sourceLine));
                    continue;
                }
                string dependency = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
                if (string.IsNullOrEmpty(dependency))
                {
                    AddReference(report, referenceKeys, CreateReference(scope, assetPath,
                        filePath, "Serialized GUID", propertyPath, string.Empty, guid,
                        "Missing", false, "GUID does not resolve to an asset.", sourceLine));
                    string locationDetail = string.IsNullOrEmpty(propertyPath)
                        ? $"Serialized source line {sourceLine} references unresolved GUID {guid}."
                        : $"Serialized field '{propertyPath}' on source line {sourceLine} " +
                          $"references unresolved GUID {guid}.";
                    AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                        "Missing GUID reference", assetPath, locationDetail,
                        string.Empty, guid, propertyPath, sourceLine));
                    continue;
                }
                bool external = IsExternalPrerequisite(dependency);
                bool allowed = external || IsAllowed(scope, dependency);
                AddReference(report, referenceKeys, CreateReference(scope, assetPath,
                    filePath, "Serialized GUID", propertyPath, dependency, guid,
                    external ? "External prerequisite" :
                    (allowed ? "Valid" : "Outside allowed folders"), allowed, string.Empty,
                    sourceLine));
                if (!external && !allowed)
                    AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                        "Out-of-package serialized reference", assetPath, dependency,
                        dependency, guid, propertyPath, sourceLine));
            }
        }

        internal static string FindSerializedGuidPropertyPath(
            string text, int guidIndex, out int sourceLine)
        {
            sourceLine = 0;
            if (string.IsNullOrEmpty(text) || guidIndex < 0 || guidIndex >= text.Length)
                return string.Empty;

            sourceLine = 1;
            for (int i = 0; i < guidIndex; i++)
                if (text[i] == '\n') sourceLine++;

            int referenceStart = text.LastIndexOf(
                "{fileID:", guidIndex, StringComparison.Ordinal);
            if (referenceStart < 0 ||
                text.IndexOf('}', referenceStart, guidIndex - referenceStart) >= 0)
                referenceStart = guidIndex;

            int lineStart = text.LastIndexOf('\n', referenceStart);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string prefix = text.Substring(lineStart, referenceStart - lineStart);
            string propertyPath = ExtractYamlPropertyName(prefix);
            if (!string.IsNullOrEmpty(propertyPath))
                return AddYamlParentProperty(text, lineStart, propertyPath);

            // Bare list references use "- {fileID: ...}". Name their owning list.
            string arrayOwner = FindPreviousYamlProperty(text, lineStart);
            return string.IsNullOrEmpty(arrayOwner) ? string.Empty : arrayOwner + "[]";
        }

        private static string AddYamlParentProperty(
            string text, int lineStart, string propertyName)
        {
            int currentIndent = YamlIndent(text, lineStart);
            int previousLineStart = PreviousLineStart(text, lineStart);
            while (previousLineStart >= 0)
            {
                int previousLineEnd = LineEnd(text, previousLineStart);
                string previousLine = text.Substring(
                    previousLineStart, previousLineEnd - previousLineStart).TrimEnd('\r');
                int parentIndent = YamlIndent(text, previousLineStart);
                if (parentIndent < currentIndent)
                {
                    string parent = ExtractYamlPropertyName(previousLine);
                    if (!string.IsNullOrEmpty(parent))
                    {
                        if (string.Equals(parent, "MonoBehaviour", StringComparison.Ordinal) ||
                            string.Equals(parent, "Material", StringComparison.Ordinal) ||
                            string.Equals(parent, "Prefab", StringComparison.Ordinal) ||
                            string.Equals(parent, "GameObject", StringComparison.Ordinal))
                            break;
                        propertyName = parent + "." + propertyName;
                        currentIndent = parentIndent;
                    }
                }
                previousLineStart = PreviousLineStart(text, previousLineStart);
            }
            return propertyName;
        }

        private static string FindPreviousYamlProperty(string text, int lineStart)
        {
            int previousLineStart = PreviousLineStart(text, lineStart);
            for (int linesBack = 0; linesBack < 32 && previousLineStart >= 0; linesBack++)
            {
                int previousLineEnd = LineEnd(text, previousLineStart);
                string previousLine = text.Substring(
                    previousLineStart, previousLineEnd - previousLineStart).TrimEnd('\r');
                string candidate = ExtractYamlPropertyName(previousLine);
                if (!string.IsNullOrEmpty(candidate) &&
                    !string.Equals(candidate, "MonoBehaviour", StringComparison.Ordinal) &&
                    !string.Equals(candidate, "Material", StringComparison.Ordinal))
                    return candidate;
                previousLineStart = PreviousLineStart(text, previousLineStart);
            }
            return string.Empty;
        }

        private static string ExtractYamlPropertyName(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix)) return string.Empty;
            string value = prefix.TrimEnd();
            int colon = value.LastIndexOf(':');
            if (colon < 0) return string.Empty;

            string beforeColon = value.Substring(0, colon);
            int separator = Math.Max(beforeColon.LastIndexOf('{'), beforeColon.LastIndexOf(','));
            string propertyName = beforeColon.Substring(separator + 1).Trim();
            if (propertyName.StartsWith("-", StringComparison.Ordinal))
                propertyName = propertyName.Substring(1).Trim();
            if (string.IsNullOrEmpty(propertyName) ||
                string.Equals(propertyName, "guid", StringComparison.Ordinal) ||
                string.Equals(propertyName, "fileID", StringComparison.Ordinal) ||
                string.Equals(propertyName, "type", StringComparison.Ordinal))
                return string.Empty;
            return propertyName;
        }

        private static int YamlIndent(string text, int lineStart)
        {
            int indent = 0;
            while (lineStart + indent < text.Length && text[lineStart + indent] == ' ')
                indent++;
            if (lineStart + indent < text.Length && text[lineStart + indent] == '-')
                indent += 2;
            return indent;
        }

        private static int PreviousLineStart(string text, int lineStart)
        {
            if (lineStart <= 0) return -1;
            int previousLineEnd = lineStart - 1;
            int start = text.LastIndexOf('\n', previousLineEnd - 1);
            return start < 0 ? 0 : start + 1;
        }

        private static int LineEnd(string text, int lineStart)
        {
            int end = text.IndexOf('\n', lineStart);
            return end < 0 ? text.Length : end;
        }

        private static void ValidateLoadedObjectReferences(ValidationScope scope, string assetPath,
            List<ValidationIssue> issues, HashSet<string> issueKeys,
            UMAReleaseValidationReport report, HashSet<string> referenceKeys)
        {
            string extension = Path.GetExtension(assetPath);
            if (!IsSerializedObjectAsset(extension)) return;
            UnityEngine.Object[] objects;
            try { objects = AssetDatabase.LoadAllAssetsAtPath(assetPath); }
            catch (Exception exception)
            {
                AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                    "Asset load failed", assetPath, exception.Message));
                return;
            }

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
                        if (property.propertyType != SerializedPropertyType.ObjectReference ||
#if UNITY_6000_5_OR_NEWER
                            !property.objectReferenceEntityIdValue.IsValid()) continue;
#else                                                    
                            property.objectReferenceInstanceIDValue == 0) continue;
#endif
                        UnityEngine.Object referencedObject = property.objectReferenceValue;
                        if (referencedObject != null)
                        {
                            string referencedPath = NormalizePath(
                                AssetDatabase.GetAssetPath(referencedObject));
                            string referencedGuid = string.IsNullOrEmpty(referencedPath)
                                ? string.Empty : AssetDatabase.AssetPathToGUID(referencedPath);
                            bool external = IsExternalPrerequisite(referencedPath);
                            bool allowed = string.IsNullOrEmpty(referencedPath) || external ||
                                IsAllowed(scope, referencedPath);
                            AddReference(report, referenceKeys, CreateReference(scope,
                                assetPath, assetPath, "Serialized object", property.propertyPath,
                                referencedPath, referencedGuid,
                                string.IsNullOrEmpty(referencedPath) ? "Scene or transient object" :
                                (external ? "External prerequisite" :
                                (allowed ? "Valid" : "Outside allowed folders")),
                                allowed, target.name));
                            continue;
                        }
                        AddReference(report, referenceKeys, CreateReference(scope, assetPath,
                            assetPath, "Serialized object", property.propertyPath,
                            string.Empty, string.Empty, "Missing", false, target.name));
                        AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                            "Missing object reference", assetPath,
                            target.name + "." + property.propertyPath,
                            string.Empty, string.Empty, property.propertyPath));
                    }
                }
                catch (Exception exception)
                {
                    AddIssue(issues, issueKeys, new ValidationIssue(scope.name,
                        "Serialized object inspection failed", assetPath,
                        target.name + ": " + exception.Message));
                }
            }
        }

        private static void RecordDirectDependencies(ValidationScope scope, ReleaseAsset owner,
            UMAReleaseValidationReport report, HashSet<string> referenceKeys)
        {
            string[] dependencies = AssetDatabase.GetDependencies(owner.path, false);
            for (int i = 0; i < dependencies.Length; i++)
            {
                string dependency = NormalizePath(dependencies[i]);
                if (string.IsNullOrEmpty(dependency) ||
                    string.Equals(dependency, owner.path, StringComparison.OrdinalIgnoreCase))
                    continue;
                bool external = IsExternalPrerequisite(dependency);
                bool allowed = external || IsAllowed(scope, dependency);
                AddReference(report, referenceKeys, CreateReference(scope, owner.path,
                    owner.path, "Direct dependency", string.Empty, dependency,
                    AssetDatabase.AssetPathToGUID(dependency),
                    external ? "External prerequisite" :
                    (allowed ? "Valid" : "Outside allowed folders"), allowed, string.Empty));
            }
        }

        private static UMAReleaseValidationAssetReport CreateAssetReport(string scope,
            string category, string path, Type type)
        {
            return new UMAReleaseValidationAssetReport
            {
                scope = scope,
                category = category,
                assetName = AssetName(path),
                assetPath = path,
                guid = AssetDatabase.AssetPathToGUID(path),
                assetType = TypeName(type)
            };
        }

        private static UMAReleaseValidationReferenceReport CreateReference(
            ValidationScope scope, string sourceAssetPath, string sourceFilePath,
            string referenceKind, string propertyPath, string referencedAssetPath,
            string referencedAssetGuid, string status, bool allowed, string detail,
            int sourceLine = 0)
        {
            Type sourceType = string.IsNullOrEmpty(sourceAssetPath) ? null :
                AssetDatabase.GetMainAssetTypeAtPath(sourceAssetPath);
            Type referencedType = string.IsNullOrEmpty(referencedAssetPath) ? null :
                AssetDatabase.GetMainAssetTypeAtPath(referencedAssetPath);
            return new UMAReleaseValidationReferenceReport
            {
                scope = scope.name,
                sourceAssetName = AssetName(sourceAssetPath),
                sourceAssetPath = sourceAssetPath,
                sourceAssetGuid = string.IsNullOrEmpty(sourceAssetPath) ? string.Empty :
                    AssetDatabase.AssetPathToGUID(sourceAssetPath),
                sourceAssetType = TypeName(sourceType),
                sourceFilePath = NormalizePath(sourceFilePath),
                referenceKind = referenceKind,
                propertyPath = propertyPath,
                sourceLine = sourceLine,
                referencedAssetName = AssetName(referencedAssetPath),
                referencedAssetPath = referencedAssetPath,
                referencedAssetGuid = referencedAssetGuid,
                referencedAssetType = TypeName(referencedType),
                status = status,
                allowed = allowed,
                detail = detail
            };
        }

        private static void AddReference(UMAReleaseValidationReport report,
            HashSet<string> keys, UMAReleaseValidationReferenceReport reference)
        {
            string key = reference.scope + "|" + reference.sourceAssetPath + "|" +
                reference.sourceFilePath + "|" + reference.referenceKind + "|" +
                reference.propertyPath + "|" + reference.sourceLine + "|" +
                reference.referencedAssetGuid + "|" +
                reference.referencedAssetPath + "|" + reference.status;
            if (keys.Add(key)) report.references.Add(reference);
        }

        private static string AssetName(string path) => string.IsNullOrEmpty(path)
            ? string.Empty : Path.GetFileNameWithoutExtension(path);

        private static string TypeName(Type type) => type == null
            ? string.Empty : type.FullName;

        private static bool IsAllowed(ValidationScope scope, string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) return true;
            for (int i = 0; i < scope.allowedFolders.Length; i++)
                if (IsInFolder(path, scope.allowedFolders[i])) return true;
            return false;
        }

        private static bool IsInFolder(string path, string folder)
        {
            path = NormalizePath(path).TrimEnd('/');
            folder = NormalizePath(folder).TrimEnd('/');
            return string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExternalPrerequisite(string path) =>
            !path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);

        private static bool IsUnityBuiltinGuid(string guid) =>
            string.IsNullOrEmpty(guid) ||
            guid.StartsWith("0000000000000000", StringComparison.Ordinal);

        private static bool IsTextSerializedFile(string fullPath)
        {
            string extension = Path.GetExtension(fullPath);
            if (string.Equals(extension, ".meta", StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".controller", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".overrideController", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".anim", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                using FileStream stream = File.OpenRead(fullPath);
                if (stream.Length < 5) return false;
                byte[] header = new byte[5];
                return stream.Read(header, 0, header.Length) == header.Length &&
                    header[0] == (byte)'%' && header[1] == (byte)'Y' &&
                    header[2] == (byte)'A' && header[3] == (byte)'M' &&
                    header[4] == (byte)'L';
            }
            catch { return false; }
        }

        private static bool IsSerializedObjectAsset(string extension) =>
            string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".mat", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".controller", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".overrideController", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".anim", StringComparison.OrdinalIgnoreCase);

        private static string NormalizePath(string path) =>
            string.IsNullOrEmpty(path) ? string.Empty : path.Replace('\\', '/');

        private static void AddIssue(List<ValidationIssue> issues, HashSet<string> keys,
            ValidationIssue issue)
        {
            string key = issue.scope + "|" + issue.kind + "|" + issue.owner + "|" +
                issue.referencePath + "|" + issue.referenceGuid + "|" +
                issue.propertyPath + "|" + issue.sourceLine + "|" + issue.detail;
            if (keys.Add(key)) issues.Add(issue);
        }

        private static void CompleteAndWriteReport(UMAReleaseValidationReport report,
            List<ValidationIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++)
                report.issues.Add(CreateIssueReport(issues[i]));
            report.issueCount = report.issues.Count;
            report.passed = report.issueCount == 0;

            report.scopes.Sort((left, right) =>
                string.Compare(left.name, right.name, StringComparison.Ordinal));
            report.assets.Sort((left, right) => CompareFields(
                left.scope, right.scope, left.assetPath, right.assetPath));
            report.references.Sort((left, right) =>
            {
                int result = CompareFields(left.scope, right.scope,
                    left.sourceAssetPath, right.sourceAssetPath);
                if (result != 0) return result;
                result = string.Compare(left.propertyPath, right.propertyPath,
                    StringComparison.Ordinal);
                return result != 0 ? result : string.Compare(left.referencedAssetPath,
                    right.referencedAssetPath, StringComparison.Ordinal);
            });
            report.issues.Sort((left, right) => CompareFields(
                left.scope, right.scope, left.ownerAssetPath, right.ownerAssetPath));

            string path = UMAReleaseValidationReport.GetAbsolutePath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(report, true),
                new UTF8Encoding(false));
        }

        private static UMAReleaseValidationIssueReport CreateIssueReport(ValidationIssue issue)
        {
            Type ownerType = string.IsNullOrEmpty(issue.owner) ? null :
                AssetDatabase.GetMainAssetTypeAtPath(issue.owner);
            Type referenceType = string.IsNullOrEmpty(issue.referencePath) ? null :
                AssetDatabase.GetMainAssetTypeAtPath(issue.referencePath);
            var result = new UMAReleaseValidationIssueReport
            {
                scope = issue.scope,
                kind = issue.kind,
                ownerAssetName = AssetName(issue.owner),
                ownerAssetPath = issue.owner,
                ownerAssetGuid = string.IsNullOrEmpty(issue.owner) ? string.Empty :
                    AssetDatabase.AssetPathToGUID(issue.owner),
                ownerAssetType = TypeName(ownerType),
                referencedAssetName = AssetName(issue.referencePath),
                referencedAssetPath = issue.referencePath,
                referencedAssetGuid = issue.referenceGuid,
                referencedAssetType = TypeName(referenceType),
                propertyPath = issue.propertyPath,
                sourceLine = issue.sourceLine,
                detail = issue.detail,
                canAutoRepair = false,
                suggestedAction = SuggestedAction(issue.kind)
            };
            if (issue.kind == "Missing GUID reference")
            {
                if (UMAReleaseValidationRepairUtility.CanReserializeStaleSource(
                    result, out _))
                    result.suggestedAction = "ReserializeSourceAsset";
                else if (UMAReleaseValidationRepairUtility.TryBuildMaterialCleanupPlan(
                    result.ownerAssetPath, out _))
                    result.suggestedAction = "RemoveNonApplicableShaderProperties";
                else
                    result.suggestedAction = "RestoreRemapOrClearReference";
            }
            return result;
        }

        private static string SuggestedAction(string kind)
        {
            if (kind == "Missing GUID reference") return "RestoreOrRemapReference";
            if (kind == "Missing object reference") return "RestoreRemapOrClearReference";
            if (kind == "Out-of-package dependency" ||
                kind == "Out-of-package serialized reference")
                return "MoveDuplicateOrRemapIntoAllowedFolder";
            if (kind == "Release folder missing") return "RestoreReleaseFolder";
            return "ManualReview";
        }

        private static int CompareFields(string leftPrimary, string rightPrimary,
            string leftSecondary, string rightSecondary)
        {
            int result = string.Compare(leftPrimary, rightPrimary, StringComparison.Ordinal);
            return result != 0 ? result : string.Compare(leftSecondary, rightSecondary,
                StringComparison.Ordinal);
        }

        private sealed class ValidationScope
        {
            public readonly string name;
            public readonly string sourceFolder;
            public readonly string[] allowedFolders;

            public ValidationScope(string name, string sourceFolder, string[] allowedFolders)
            {
                this.name = name;
                this.sourceFolder = sourceFolder;
                this.allowedFolders = allowedFolders;
            }
        }

        private readonly struct ReleaseAsset
        {
            public readonly string path;
            public readonly string category;
            public readonly string type;

            public ReleaseAsset(string path, string category, string type)
            {
                this.path = path;
                this.category = category;
                this.type = type;
            }
        }

        private readonly struct ValidationIssue
        {
            public readonly string scope;
            public readonly string kind;
            public readonly string owner;
            public readonly string detail;
            public readonly string referencePath;
            public readonly string referenceGuid;
            public readonly string propertyPath;
            public readonly int sourceLine;

            public ValidationIssue(string scope, string kind, string owner, string detail,
                string referencePath = "", string referenceGuid = "",
                string propertyPath = "", int sourceLine = 0)
            {
                this.scope = scope;
                this.kind = kind;
                this.owner = owner;
                this.detail = detail;
                this.referencePath = referencePath;
                this.referenceGuid = referenceGuid;
                this.propertyPath = propertyPath;
                this.sourceLine = sourceLine;
            }

            public override string ToString() => $"[{scope}] {kind}: {owner} -> {detail}";
        }

        private sealed class ValidationSummary
        {
            private readonly ValidationScope scope;
            private readonly int ownerCount;
            private readonly int closureCount;
            private readonly Dictionary<string, int> categories;

            public ValidationSummary(ValidationScope scope, int ownerCount, int closureCount,
                Dictionary<string, int> categories)
            {
                this.scope = scope;
                this.ownerCount = ownerCount;
                this.closureCount = closureCount;
                this.categories = categories;
            }

            public UMAReleaseValidationScopeReport ToReport()
            {
                var result = new UMAReleaseValidationScopeReport
                {
                    name = scope.name,
                    sourceFolder = scope.sourceFolder,
                    releaseAssetCount = ownerCount,
                    dependencyClosureCount = closureCount
                };
                result.allowedFolders.AddRange(scope.allowedFolders);
                var names = new List<string>(categories.Keys);
                names.Sort(StringComparer.Ordinal);
                for (int i = 0; i < names.Count; i++)
                    result.categories.Add(new UMAReleaseValidationCount
                    {
                        name = names[i],
                        count = categories[names[i]]
                    });
                return result;
            }

            public override string ToString()
            {
                var result = new StringBuilder();
                result.Append(scope.name).Append(": ").Append(ownerCount)
                    .Append(" release assets, ").Append(closureCount)
                    .Append(" assets in dependency closure");
                var names = new List<string>(categories.Keys);
                names.Sort(StringComparer.Ordinal);
                for (int i = 0; i < names.Count; i++)
                    result.Append(i == 0 ? " (" : ", ").Append(names[i]).Append(": ")
                        .Append(categories[names[i]]);
                if (names.Count > 0) result.Append(')');
                return result.ToString();
            }
        }
    }
}

#endif
