using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	public class MenuItems 
	{
		private const string _SceneBoneBaking = "UMA/Tools/Scene Bone Baking";
		[MenuItem(_SceneBoneBaking)]
		static void ToggleBoneBaking()
		{
			var generator = UMAAssetIndexer.Instance.generator;
			if (generator == null) return;
			Undo.RecordObject(generator, "Toggle Scene Bone Baking");
			if (generator.meshCombiner is UMABoneBakingMeshCombiner)
			{
				var defaultMeshCombiner = Object.FindObjectOfType<UMADefaultMeshCombiner>();
				if (defaultMeshCombiner == null)
					defaultMeshCombiner = Spawn<UMADefaultMeshCombiner>(generator.transform.parent);
				generator.meshCombiner = defaultMeshCombiner;
			}
			else
			{
				var boneBakingMeshCombiner = Object.FindObjectOfType<UMABoneBakingMeshCombiner>();
				if (boneBakingMeshCombiner == null)
					boneBakingMeshCombiner = Spawn<UMABoneBakingMeshCombiner>(generator.transform.parent);
				generator.meshCombiner = boneBakingMeshCombiner;
			}
			if (PrefabUtility.IsPartOfAnyPrefab(generator))
			{
				PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
			}
		}

		[MenuItem(_SceneBoneBaking, true)]
		static bool ToggleBoneBakingActive()
		{
			var generator = UMAAssetIndexer.Instance.generator;
			if (generator == null)
			{
				Menu.SetChecked(_SceneBoneBaking, false);
				return false;
			}
			Menu.SetChecked(_SceneBoneBaking, generator.meshCombiner is UMABoneBakingMeshCombiner);
			return true;
		}

		private static T Spawn<T>(Transform parent)
			where T : MonoBehaviour
		{
			var go = new GameObject(typeof(T).Name);
			go.transform.parent = parent;
			return go.AddComponent<T>();
		}
	}
}