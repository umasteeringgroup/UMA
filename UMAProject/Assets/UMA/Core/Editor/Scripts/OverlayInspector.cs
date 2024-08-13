#if UNITY_EDITOR
using UnityEditor;

namespace UMA.Editors
{
	[CustomEditor(typeof(OverlayData))]
	public class OverlayInspector : Editor 
	{
#if UMA_HOTKEYS
		[MenuItem("Assets/Create/UMA/Core/Overlay Asset %#o")]
#else
	    [MenuItem("Assets/Create/UMA/Core/Overlay Asset")]
#endif
	    public static void CreateOverlayMenuItem()
	    {
	        var ovl = CustomAssetUtility.CreateAsset<OverlayDataAsset>();
	    }


	    public override void OnInspectorGUI()
	    {
	        base.OnInspectorGUI();
	    }
	    
	}
}
#endif