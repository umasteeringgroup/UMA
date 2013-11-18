using UnityEngine;
using System.Collections;
using UnityEditor;
using UMA;


namespace UMAEditor
{
	[CustomEditor(typeof(OverlayData))]
	public class OverlayInspector : Editor 
	{
	    [MenuItem("Assets/Create/UMA Overlay")]
	    public static void CreateOverlayMenuItem()
	    {
	        CustomAssetUtility.CreateAsset<OverlayData>();
	    }


	    public override void OnInspectorGUI()
	    {
	        base.OnInspectorGUI();
	    }
	    
	}
}