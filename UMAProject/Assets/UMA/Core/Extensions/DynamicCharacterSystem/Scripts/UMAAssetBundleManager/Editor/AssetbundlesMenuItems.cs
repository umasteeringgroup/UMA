using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMA.AssetBundles
{
	public class AssetBundlesMenuItems
	{
		const string kSimulationMode = "Assets/AssetBundles/Simulation Mode";

		[MenuItem("Assets/AssetBundles/Build AssetBundles")]
		static public void BuildAssetBundles()
		{
			BuildScript.BuildAssetBundles();
		}
		//DOS NOTE This was added in the latest pull requests but I dont understand what its for...
		/*[MenuItem("Assets/AssetBundles/Build Player (for use with engine code stripping)")]
        static public void BuildPlayer()
        {
            BuildScript.BuildPlayer();
        }*/
		[MenuItem("Assets/AssetBundles/Build Player (using LocalAssetServer if enabled)")]
		static public void BuildPlayer()
		{
			BuildScript.BuildAndRunPlayer(false);
		}
	}
}
