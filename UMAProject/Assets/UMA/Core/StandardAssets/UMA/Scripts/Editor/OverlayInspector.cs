#if UNITY_EDITOR
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(OverlayData))]
	public class OverlayInspector : Editor 
	{
	    [MenuItem("Assets/Create/UMA/Core/Overlay Asset")]
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