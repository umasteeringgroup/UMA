using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public class NodeGeneratorWindow : EditorWindow
{
	System.Random random = new System.Random();
	public float rotationAmount;
	public string selectedfolder = "";
	public Label FolderLabel;


	[MenuItem("UMA/Node Generator")]
	public static void ShowWindow()
	{
		var defaultPath = Path.Combine(Application.dataPath, "UMA/UMAXnode/Editor/UMA/Nodes");

		var window = GetWindow<NodeGeneratorWindow>(true, "Node Generator");
		window.minSize = new Vector2(400, 200);
		window.maxSize = new Vector2(600, 200);
		window.selectedfolder = EditorPrefs.GetString("UMAXNodeGeneratorFolder", Path.Combine(Application.dataPath, "UMA")) ;
		// center the window
		window.position = new Rect(Screen.width / 2, Screen.height / 2, 400, 200);
		window.ShowUtility();
	}

	void CreateGUI()
	{
		// Get the node name.
		// Ask if you want to create a node editor for it.
		// Ask if you want to create a node for it.

		


		var label = new Label("Selected an object and click Randomize!");
		rootVisualElement.Add(label);

		var buttonRandomize = new Button();
		buttonRandomize.text = "Randomize!";
		buttonRandomize.clicked += () => RandomizeSelected();
		rootVisualElement.Add(buttonRandomize);

		var folderField = new ObjectField("Select output folder");
		var folder = new ObjectField();
		folder.objectType = typeof(DefaultAsset);
		folderField.RegisterValueChangedCallback(evt =>
		{
			var obj = folderField.value;
			if (obj != null)
			{
				selectedfolder = AssetDatabase.GetAssetPath(obj);
				FolderLabel.text = selectedfolder;
				// write the path to Unity Preferences
				EditorPrefs.SetString("UMAXNodeGeneratorFolder", selectedfolder);
			}
		});

		FolderLabel = new Label("No folder selected");
		rootVisualElement.Add(folderField);
	}

	
	void RandomizeSelected()
	{
		/*
		foreach (var transform in Selection.transforms)
		{
			Quaternion rotation = Random.rotation;
			rotationAmount = (float)random.NextDouble();
			transform.localRotation = Quaternion.Slerp(transform.localRotation, rotation, rotationAmount);
		}
		*/
	}
}
