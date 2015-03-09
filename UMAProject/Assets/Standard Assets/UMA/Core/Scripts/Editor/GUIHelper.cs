#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections;

namespace UMAEditor
{
	public static class GUIHelper
	{
		public static void BeginVerticalPadded(float padding, Color backgroundColor)
		{
			GUI.color = backgroundColor;
			GUILayout.BeginHorizontal(EditorStyles.textField);
			GUI.color = Color.white;

			GUILayout.Space(padding);
			GUILayout.BeginVertical();
			GUILayout.Space(padding);
		}

		public static void EndVerticalPadded(float padding)
		{
			GUILayout.Space(padding);
			GUILayout.EndVertical();
			GUILayout.Space(padding);
			GUILayout.EndHorizontal();
		}

		public static void BeginVerticalIndented(float indentation, Color backgroundColor)
		{
			GUI.color = backgroundColor;
			GUILayout.BeginHorizontal();
			GUILayout.Space(indentation);
			GUI.color = Color.white;
			GUILayout.BeginVertical();
		}

		public static void EndVerticalIndented()
		{
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}

		public static void BeginHorizontalPadded(float padding, Color backgroundColor)
		{
			GUI.color = backgroundColor;
			GUILayout.BeginVertical(EditorStyles.textField);
			GUI.color = Color.white;

			GUILayout.Space(padding);
			GUILayout.BeginHorizontal();
			GUILayout.Space(padding);
		}

		public static void EndHorizontalPadded(float padding)
		{
			GUILayout.Space(padding);
			GUILayout.EndHorizontal();
			GUILayout.Space(padding);
			GUILayout.EndVertical();
		}

		public static void Separator()
		{
			GUILayout.BeginHorizontal(EditorStyles.textField);
			GUILayout.EndHorizontal();
		}

		public static void BeginCollapsableGroupPartStart(ref bool show, string text, string boneName, ref bool selected)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUI.SetNextControlName(boneName);
			show = EditorGUILayout.Foldout(show, text);
			var control = GUI.GetNameOfFocusedControl();
			selected = control == boneName;
			//GUI.color = selected ? Color.yellow : Color.white;
			//if (GUILayout.Button(text, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
			//{
			//    selected = true;
			//}
			//GUI.color = Color.white;
		}

		public static void BeginCollapsableGroupPartMiddle(ref bool show, string text, ref bool selected)
		{
			GUILayout.Label("", EditorStyles.toolbarButton);
		}

		public static bool BeginCollapsableGroupPartEnd(ref bool show, string text, ref bool selected)
		{
			GUILayout.EndHorizontal();

			if (show)
			{
				GUILayout.BeginVertical();
			}
			return show;
		}


		public static bool BeginCollapsableGroup(ref bool show, string text)
		{
			GUILayout.BeginHorizontal();
			show = GUILayout.Toggle(show, show ? "\u002d" : "\u002b", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
			//GUILayout.Label(text, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
			GUILayout.Label("", EditorStyles.toolbarButton);
			GUILayout.EndHorizontal();

			if (show)
			{
				GUILayout.BeginVertical();
			}
			return show;
		}

		public static void EndCollapsableGroup(ref bool show)
		{
			if (show)
			{
				EndCollapsableGroup();
			}
		}

		public static void EndCollapsableGroup()
		{
			GUILayout.EndVertical();
		}

		public static void BeginObject(string label, int minWidth)
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label(label, EditorStyles.boldLabel, GUILayout.ExpandWidth(false), GUILayout.MinWidth(minWidth));
		}

		public static void EndObject()
		{
			GUILayout.EndHorizontal();
		}

		public static void FoldoutBar(ref bool foldout, string content, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content);
			delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
		}

		public static void FoldoutBar(ref bool foldout, string content, out int move, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content);

			move = 0;
			if (GUILayout.Button("\u25B2", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) move--;
			if (GUILayout.Button("\u25BC", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) move++;

			delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
		}
	}
}
#endif