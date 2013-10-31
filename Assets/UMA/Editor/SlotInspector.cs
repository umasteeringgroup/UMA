using UnityEngine;
using System.Collections;
using UnityEditor;

[CustomEditor(typeof(SlotData))]
public class SlotInspector : Editor 
{
    [MenuItem("Assets/Create/UMA Slot")]
    public static void CreateSlotMenuItem()
    {
        CustomAssetUtility.CreateAsset<SlotData>();
    }


    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();
    }
    
}