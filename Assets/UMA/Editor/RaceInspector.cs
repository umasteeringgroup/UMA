using UnityEngine;
using System.Collections;
using UnityEditor;

[CustomEditor(typeof(RaceData))]
public class RaceInspector : Editor 
{
    [MenuItem("Assets/Create/UMA Race")]
    public static void CreateRaceMenuItem()
    {
        CustomAssetUtility.CreateAsset<RaceData>();
    }


    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
    }
    
}