using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	public class MenuItems 
	{
		private const string _MeshCombiner = "UMA/Tools/Mesh Combiner Switcher";
		[MenuItem(_MeshCombiner, priority = 11)]
		static void SelectMeshCombiner()
		{
			UMAMeshCombinerSwitcherWindow.ShowWindow();
		}

		[MenuItem(_MeshCombiner, true)]
		static bool SelectMeshCombinerActive()
		{
			Menu.SetChecked(_MeshCombiner, false);
			return UMAAssetIndexer.Instance.generator != null;
		}
	}
}