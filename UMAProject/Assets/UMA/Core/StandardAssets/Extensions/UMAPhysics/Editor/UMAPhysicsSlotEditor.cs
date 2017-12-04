using UnityEngine;
using UnityEditor;

namespace UMA.Dynamics.Editors
{
	[CustomEditor(typeof(UMAPhysicsSlotDefinition))]
	public class UMAPhysicsSlotEditor : Editor 
	{
		public override void OnInspectorGUI()
		{
			UMAPhysicsSlotDefinition slot = target as UMAPhysicsSlotDefinition;

			DrawDefaultInspector();

			EditorGUILayout.HelpBox ("Sets layer 8 and 9 to Ragdoll and Player. If your code uses different layers do not use this defaults button", MessageType.Info);
			if (GUILayout.Button ("Add Default Layers")) 
			{
				AddDefaultLayers ();
			}
			EditorGUILayout.HelpBox ("The Ragdoll layer needs it's collision matrix layers set to collide with only itself. Set this in Edit->Project Settings->Physics->Layer Collision Matrix", MessageType.Info);

			slot.ragdollLayer = EditorGUILayout.LayerField ("Ragdoll Layer", slot.ragdollLayer);
			slot.playerLayer = EditorGUILayout.LayerField ("Player Layer", slot.playerLayer);
		}

		private void AddDefaultLayers()
		{
			UMAPhysicsSlotDefinition slot = target as UMAPhysicsSlotDefinition;

			CreateLayer ("Ragdoll");
			CreateLayer ("Player");

			for (int i = 8; i < 32; i++)
			{
				if( i != slot.ragdollLayer )
				Physics.IgnoreLayerCollision(slot.ragdollLayer, i, true);
			}

			Physics.IgnoreLayerCollision(slot.ragdollLayer, slot.ragdollLayer, false);
		}

		private void CreateLayer(string name)
		{
			//  https://forum.unity3d.com/threads/adding-layer-by-script.41970/reply?quote=2274824
			SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
			SerializedProperty layers = tagManager.FindProperty("layers");
			bool ExistLayer = false;

			for (int i = 8; i < layers.arraySize; i++)
			{
				SerializedProperty layerSP = layers.GetArrayElementAtIndex(i);

				if (layerSP.stringValue == name)
				{
					ExistLayer = true;
					break;
				}

			}
			for (int j = 8; j < layers.arraySize; j++)
			{
				SerializedProperty layerSP = layers.GetArrayElementAtIndex(j);
				if (layerSP.stringValue == "" && !ExistLayer)
				{
					layerSP.stringValue = name;
					tagManager.ApplyModifiedProperties();

					break;
				}
			}
		}
	}
}
