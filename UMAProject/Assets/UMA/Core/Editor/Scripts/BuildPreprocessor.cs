using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class BuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder  { get { return 0; } }

    public void OnPreprocessBuild(BuildReport report)
    {
        UMAAssetIndexer.Instance.UpdateReferences();
    }
}
