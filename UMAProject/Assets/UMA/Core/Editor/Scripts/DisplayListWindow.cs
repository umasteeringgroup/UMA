using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Callbacks;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// DisplayListWindow is a generic dialog window that can be used to display a list of strings. 
/// To Use, call DisplayListWindow.ShowDialog("title", Pos, StringList);
/// </summary>
public class DisplayListWindow : EditorWindow
{
	static Vector2 winsize = new Vector2(200, 300);
	public static void ShowDialog(string Title, Rect parentPos, List<string> Items)
	{
		Rect Pos = new Rect(parentPos.center - (winsize / 2), winsize);

		DisplayListWindow window = ScriptableObject.CreateInstance(typeof(DisplayListWindow)) as DisplayListWindow;
		window.Initialize(Items);
		window.titleContent = new GUIContent(Title);
		window.ShowUtility();
		window.position = Pos;
	}

	const int itemHeight = 18;
	public List<string> displayItems = new List<string>();
	Vector2 scrollPosition = new Vector2(0.0f, 0.0f);

	Rect virtualBox
	{
		get { return new Rect(0, 0, position.width - 20, itemHeight * displayItems.Count); }
	}

	Rect listBox
	{
		get { return new Rect(10, 10, position.width - 20, position.height - 40); }
	}

	Rect buttonBar
	{
		get { return new Rect(10, position.height - 30, position.width - 20, 20); }
	}


	public void Initialize(List<string> Items)
	{
		displayItems = Items;
	}

	void OnGUI()
	{
		Rect ItemRect = new Rect(listBox);
		
		ItemRect.x = 10;
		ItemRect.width -= 20;
		ItemRect.y = 0;
		ItemRect.height = itemHeight;

		// An absolute-positioned example: We make a scrollview that has a really large client
		// rect and put it in a small rect on the screen.
		scrollPosition = GUI.BeginScrollView(listBox, scrollPosition, virtualBox);
		foreach (string s in displayItems)
		{
			EditorGUI.LabelField(ItemRect, s);
			ItemRect.y += itemHeight;
		}
		GUI.EndScrollView();

		Rect B = buttonBar;
		B.x = B.width - 80;
		B.width = 80;
		if (GUI.Button(B,"Close"))
		{
			this.Close();
		}
	}
}
