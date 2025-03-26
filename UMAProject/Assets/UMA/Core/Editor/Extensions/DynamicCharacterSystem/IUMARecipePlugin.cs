using UnityEditor;

namespace UMA
{
	public interface IUMARecipePlugin
	{
		bool foldOut
		{
			get; set;
		}
		string GetSectionLabel();
		void OnEnable();
		void OnDestroy();
		void OnInspectorGUI(SerializedObject serializedObject);
	}
}