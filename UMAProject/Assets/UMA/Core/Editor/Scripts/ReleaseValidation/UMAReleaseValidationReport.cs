#if UNITY_EDITOR

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UMA.Editors
{
    /// <summary>
    /// Stable, editor-facing contract written by the UMA release validation tests.
    /// Editor review and repair tools should deserialize this type rather than depend
    /// on the test assembly.
    /// </summary>
    [Serializable]
    public sealed class UMAReleaseValidationReport
    {
        public const int CurrentSchemaVersion = 2;
        public const string ProjectRelativePath = "Temp/UMA/LastReleaseTest.json";

        public int schemaVersion = CurrentSchemaVersion;
        public string testName = "UMA Release Tests/Asset Validation";
        public string generatedUtc;
        public string unityVersion;
        public string projectPath;
        public string reportPath = ProjectRelativePath;
        public bool passed;
        public int issueCount;
        public List<UMAReleaseValidationScopeReport> scopes = new();
        public List<UMAReleaseValidationAssetReport> assets = new();
        public List<UMAReleaseValidationReferenceReport> references = new();
        public List<UMAReleaseValidationIssueReport> issues = new();

        public static string GetAbsolutePath()
        {
            string projectPath = Path.GetDirectoryName(Application.dataPath);
            return Path.GetFullPath(Path.Combine(projectPath ?? Directory.GetCurrentDirectory(),
                ProjectRelativePath));
        }

        public static UMAReleaseValidationReport LoadLastReport()
        {
            string path = GetAbsolutePath();
            return File.Exists(path)
                ? JsonUtility.FromJson<UMAReleaseValidationReport>(File.ReadAllText(path))
                : null;
        }
    }

    [Serializable]
    public sealed class UMAReleaseValidationScopeReport
    {
        public string name;
        public string sourceFolder;
        public List<string> allowedFolders = new();
        public int releaseAssetCount;
        public int dependencyClosureCount;
        public List<UMAReleaseValidationCount> categories = new();
    }

    [Serializable]
    public sealed class UMAReleaseValidationCount
    {
        public string name;
        public int count;
    }

    [Serializable]
    public sealed class UMAReleaseValidationAssetReport
    {
        public string scope;
        public string category;
        public string assetName;
        public string assetPath;
        public string guid;
        public string assetType;
    }

    [Serializable]
    public sealed class UMAReleaseValidationReferenceReport
    {
        public string scope;
        public string sourceAssetName;
        public string sourceAssetPath;
        public string sourceAssetGuid;
        public string sourceAssetType;
        public string sourceFilePath;
        public string referenceKind;
        public string propertyPath;
        public int sourceLine;
        public string referencedAssetName;
        public string referencedAssetPath;
        public string referencedAssetGuid;
        public string referencedAssetType;
        public string status;
        public bool allowed;
        public string detail;
    }

    [Serializable]
    public sealed class UMAReleaseValidationIssueReport
    {
        public string scope;
        public string kind;
        public string ownerAssetName;
        public string ownerAssetPath;
        public string ownerAssetGuid;
        public string ownerAssetType;
        public string referencedAssetName;
        public string referencedAssetPath;
        public string referencedAssetGuid;
        public string referencedAssetType;
        public string propertyPath;
        public int sourceLine;
        public string detail;
        public bool canAutoRepair;
        public string suggestedAction;
    }
}

#endif
