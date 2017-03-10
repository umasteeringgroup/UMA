#if UNITY_EDITOR
using UnityEngine;
using System.Collections;
using UnityEditor;
using UMA;


namespace UMAEditor
{
	[CustomEditor(typeof(OverlayData))]
	public class OverlayInspector : Editor 
	{
	    [MenuItem("Assets/Create/UMA Overlay Asset")]
	    public static void CreateOverlayMenuItem()
	    {
	        CustomAssetUtility.CreateAsset<OverlayDataAsset>();
	    }


	    public override void OnInspectorGUI()
	    {
	        base.OnInspectorGUI();
	    }
	    
	}
}
#endif