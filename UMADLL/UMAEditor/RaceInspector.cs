using UnityEngine;
using System.Collections;
using UnityEditor;
using UMA;

namespace UMAEditor
{
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
}