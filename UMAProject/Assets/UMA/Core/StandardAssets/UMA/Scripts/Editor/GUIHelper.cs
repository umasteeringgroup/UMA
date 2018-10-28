#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
	public static class GUIHelper
	{

		private static Texture _helpIcon;

		private static GUIStyle _iconLabel;

		public static Texture helpIcon
		{
			get {
				if (_helpIcon != null)
					return _helpIcon;
				//Sometimes editor styles is not set up when we ask for this
				if (EditorStyles.label == null)
					return new Texture();
				_helpIcon = EditorGUIUtility.FindTexture("_Help");
				return _helpIcon;
			}
		}

		public static GUIStyle iconLabel
		{
			get
			{
				if (_iconLabel != null)
					return _iconLabel;
				if (EditorStyles.label == null)
					return new GUIStyle();
				_iconLabel = new GUIStyle(EditorStyles.label);
				_iconLabel.fixedHeight = 18f;
				_iconLabel.contentOffset = new Vector2(-4.0f, 0f);
				return _iconLabel;
			}
		}

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

		public static void FoldoutBarButton(ref bool foldout, string content, string button, out bool pressed, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content);
			pressed = GUILayout.Button(button, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
		}

		public static void FoldoutBarButton(ref bool foldout, string content, string button, out bool pressed, out int move, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content);

			move = 0;
			if (GUILayout.Button("\u25B2", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) move--;
			if (GUILayout.Button("\u25BC", EditorStyles.miniButton, GUILayout.ExpandWidth(false))) move++;

			pressed = GUILayout.Button(button, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
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

		public static void ToolbarStyleFoldout(Rect rect, string label, ref bool isExpanded, GUIStyle toolbarStyleOverride = null, GUIStyle labelStyleOverride = null)
		{
			bool dummyHelpExpanded = false;
			ToolbarStyleFoldout(rect, new GUIContent(label), new string[0], ref isExpanded, ref dummyHelpExpanded, toolbarStyleOverride, labelStyleOverride);
		}

		public static void ToolbarStyleFoldout(Rect rect, GUIContent label, ref bool isExpanded, GUIStyle toolbarStyleOverride = null, GUIStyle labelStyleOverride = null)
		{
			bool dummyHelpExpanded = false;
			ToolbarStyleFoldout(rect, label, new string[0], ref isExpanded, ref dummyHelpExpanded, toolbarStyleOverride, labelStyleOverride);
		}

		public static void ToolbarStyleFoldout(Rect rect, string label, string[] help, ref bool isExpanded, ref bool helpExpanded, GUIStyle toolbarStyleOverride = null, GUIStyle labelStyleOverride = null)
		{
			ToolbarStyleFoldout(rect, new GUIContent(label), help, ref isExpanded, ref helpExpanded, toolbarStyleOverride, labelStyleOverride);
		}
		/// <summary>
		/// Draws a ToolBar style foldout with a centered foldout label. Optionally draws a help icon that will show the selected array of 'help' paragraphs
		/// </summary>
		/// <param name="rect"></param>
		/// <param name="label"></param>
		/// <param name="help">An array of help paragpahs. If supplied the help Icon will be shown</param>
		/// <param name="isExpanded"></param>
		/// <param name="helpExpanded"></param>
		/// <param name="toolbarStyleOverride">Overrides EdiorStyles.toolbar as the background</param>
		/// <param name="labelStyleOverride">Overrides EditorStyles.folodout as the label style</param>
		public static void ToolbarStyleFoldout(Rect rect, GUIContent label, string[] help, ref bool isExpanded, ref bool helpExpanded, GUIStyle toolbarStyleOverride = null, GUIStyle labelStyleOverride = null)
		{
			GUIStyle toolbarStyle = toolbarStyleOverride;
			GUIStyle labelStyle = labelStyleOverride;
			if (toolbarStyle == null)
				toolbarStyle = EditorStyles.toolbar;
			if (labelStyle == null)
				labelStyle = EditorStyles.foldout;
			var helpIconRect = new Rect(rect.xMax - 20f, rect.yMin, 20f, rect.height);
			var helpGUI= new GUIContent("", "Show Help");
			helpGUI.image = helpIcon;
			Event current = Event.current;
			if (current.type == EventType.Repaint)
			{
				toolbarStyle.Draw(rect, GUIContent.none, false, false, false, false);
			}
			var labelWidth = labelStyle.CalcSize(label);
			labelWidth.x += 15f;//add the foldout arrow
			var toolbarFoldoutRect = new Rect((rect.xMax / 2f) - (labelWidth.x / 2f) + 30f, rect.yMin, ((rect.width / 2) + (labelWidth.x / 2f)) - 20f - 30f, rect.height);
			isExpanded = EditorGUI.Foldout(toolbarFoldoutRect, isExpanded, label, true, labelStyle);
			if (help.Length > 0)
			{
				helpExpanded = GUI.Toggle(helpIconRect, helpExpanded, helpGUI, iconLabel);
				if (helpExpanded)
				{
					ToolbarStyleHelp(help);
				}
			}
		}
		private static void ToolbarStyleHelp(string[] help)
		{
			BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			for (int i = 0; i < help.Length; i++)
			{
				EditorGUILayout.HelpBox(help[i], MessageType.None);
			}
			EndVerticalPadded(3);
		}
	}
}
#endif