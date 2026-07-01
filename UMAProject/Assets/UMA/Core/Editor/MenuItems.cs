using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	public class MenuItems 
	{
		private const string _MeshCombiner = "UMA/Tools/Select Mesh Combiner";
		[MenuItem(_MeshCombiner)]
		static void SelectMeshCombiner()
		{
			var generator = UMAAssetIndexer.Instance.generator;
			if (generator == null) return;
			var selection = EditorUtility.DisplayDialogComplex(
				"Select Mesh Combiner",
				"Choose which mesh combiner the UMA generator should use.",
				"Jobified Combiner",
				"Bone Baking Combiner",
				"Default Combiner");
			switch (selection)
			{
				case 0:
					UseMeshCombiner<UMAJobifiedMeshCombiner>(generator);
					break;
				case 1:
					UseMeshCombiner<UMABoneBakingMeshCombiner>(generator);
					break;
				case 2:
					UseMeshCombiner<UMADefaultMeshCombiner>(generator);
					break;
			}
		}

		[MenuItem(_MeshCombiner, true)]
		static bool SelectMeshCombinerActive()
		{
			Menu.SetChecked(_MeshCombiner, false);
			return UMAAssetIndexer.Instance.generator != null;
		}

		private static void UseMeshCombiner<T>(UMAGenerator generator)
			where T : UMAMeshCombiner
		{
			if (generator.meshCombiner is T)
			{
				return;
			}

			var meshCombiner = Object.FindFirstObjectByType<T>();
			if (meshCombiner == null)
			{
				meshCombiner = Spawn<T>(generator.transform.parent);
			}

			Undo.RecordObject(generator, "Select Mesh Combiner");
			generator.meshCombiner = meshCombiner;
			if (PrefabUtility.IsPartOfAnyPrefab(generator))
			{
				PrefabUtility.RecordPrefabInstancePropertyModifications(generator);
			}
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