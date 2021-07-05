#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Events;
using System;

namespace UMA.Editors
{
	public static class GUIHelper
	{
		#region ----- Private Vars -----
		private static Texture _helpIcon;
		private static GUIStyle _iconLabel;
		private static Texture _inspectIcon;
		private static GUIContent _inspectContent;
		private static GUIStyle _inspectStyle;
		private static float inspectBtnWidth = 25f;

		private static Texture helpIcon
		{
			get
			{
				if (_helpIcon != null)
					return _helpIcon;
				//Sometimes editor styles is not set up when we ask for this
				if (EditorStyles.label == null)
					return new Texture2D(16, 16);
				_helpIcon = EditorGUIUtility.FindTexture("_Help");
				return _helpIcon;
			}
		}

		private static GUIStyle iconLabel
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

		private static Texture inspectIcon
		{
			get
			{
				if (_inspectIcon != null)
					return _inspectIcon;
				//Check EditorStyles has been set up
				if (EditorStyles.label == null)
					return new Texture2D(16, 16);
				_inspectIcon = EditorGUIUtility.FindTexture("ViewToolOrbit");
				return _inspectIcon;
			}
		}

		private static GUIContent inspectContent
		{
			get
			{
				if (_inspectContent != null && _inspectIcon != null)
					return _inspectContent;
				_inspectContent = new GUIContent("", "Inspect");
				_inspectContent.image = inspectIcon;
				return _inspectContent;
			}
		}

		private static GUIStyle inspectStyle
		{
			get
			{
				if (_inspectStyle != null)
					return _inspectStyle;
				//Check EditorStyles is set up
				if (EditorStyles.miniButton == null)
					return new GUIStyle();
				_inspectStyle = new GUIStyle(EditorStyles.miniButton);
				_inspectStyle.contentOffset = new Vector2(0f, 0f);
				_inspectStyle.padding = new RectOffset(0, 0, 0, 0);
				_inspectStyle.margin = new RectOffset(0, 0, 0, 0);
				return _inspectStyle;
			}
		}
		#endregion

		/// <summary>
		/// Common UMA Inspectors Colors
		/// </summary>
		public static class Colors
		{
			private static Color grey = new Color(0.75f, 0.75f, 0.75f);
			private static Color blue = new Color(0.75f, 0.875f, 1f, 0.7f);

			public static Color Grey => grey;
			public static Color Blue => blue;
		}

		/// <summary>
		/// Common UMA Inspectors Styles
		/// </summary>
		public static class Styles
		{
			private static GUIStyle toggleButtonNormal = null;
			private static GUIStyle toggleButtonToggled = null;
			internal static GUIStyle ToggleButtonNormal
			{
				get
				{
					if (toggleButtonNormal == null) toggleButtonNormal = new GUIStyle("Button");
					toggleButtonNormal.normal.background = MakeTex(4, 4, GUIHelper.Colors.Grey);
					return toggleButtonNormal;
				}
			}
			internal static GUIStyle ToggleButtonToggled
			{
				get
				{
					if (toggleButtonToggled == null)
					{
						toggleButtonToggled = new GUIStyle("Button");
						toggleButtonToggled.normal.background = MakeTex(4, 4, GUIHelper.Colors.Blue);
						// toggleButtonStyleToggled.active.background;
					}
					return toggleButtonToggled;
				}
			}

			private static Texture2D MakeTex(int width, int height, Color col)
			{
				Color[] pix = new Color[width * height];
				for (int i = 0; i < pix.Length; ++i)
				{
					pix[i] = col;
				}
				Texture2D result = new Texture2D(width, height);
				result.SetPixels(pix);
				result.Apply();
				return result;
			}
		}

		public static void BeginVerticalPadded(float padding = 10f, Color backgroundColor = default, GUIStyle theStyle = null)
		{
			if (backgroundColor == default)
				backgroundColor = EditorGUIUtility.isProSkin ? new Color(1.3f, 1.4f, 1.5f) : new Color(0.75f, 0.875f, 1f);

			if (theStyle == null)
				theStyle = EditorStyles.textField;

			GUI.color = backgroundColor;
			GUILayout.BeginHorizontal(theStyle);
			GUI.color = Color.white;

			GUILayout.Space(padding);
			GUILayout.BeginVertical();
			GUILayout.Space(padding);
		}

		public static void EndVerticalPadded(float padding = 10f)
		{
			GUILayout.Space(padding);
			GUILayout.EndVertical();
			GUILayout.Space(padding);
			GUILayout.EndHorizontal();
		}

		public static void BeginHorizontalPadded(float padding = 10f, Color backgroundColor = default, GUIStyle theStyle = null)
		{
			if (backgroundColor == default)
				backgroundColor = EditorGUIUtility.isProSkin ? new Color(1.3f, 1.4f, 1.5f) : new Color(0.75f, 0.875f, 1f);

			if (theStyle == null)
				theStyle = EditorStyles.textField;

			GUI.color = backgroundColor;
			GUILayout.BeginVertical(theStyle);
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

		public static void BeginVerticalIndented(float indentation = 3f, Color backgroundColor = default)
		{
			if (backgroundColor == default)
				backgroundColor = EditorGUIUtility.isProSkin ? new Color(1.3f, 1.4f, 1.5f) : new Color(0.75f, 0.875f, 1f);

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
			GUILayout.Label(text, EditorStyles.toolbarButton, GUILayout.ExpandWidth(false));
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

		/// <summary>
		/// Adds a clickable Label to foldout any following GUI
		/// </summary>
		/// <param name="foldout"> In Foldout State (False : Folded) </param>
		/// <param name="content"> Label Text </param>
		/// <returns> Out Foldout State (False : Folded) </returns>
		public static bool FoldoutBar(bool foldout, string content)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			bool nfoldout = EditorGUILayout.Foldout(foldout, content, true);
			GUILayout.EndHorizontal();
			return nfoldout;
		}

		/// <summary>
		/// <see cref="FoldoutBar"/>, with delete option
		/// </summary>
		/// <param name="delete"> Out : Has foldout been deleted ? </param>
		public static void FoldoutBar(ref bool foldout, string content, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content, true);
			delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
		}

		/// <summary>
		/// <see cref="FoldoutBar"/>, with Tooltip option
		/// </summary>
		/// <param name="tooltip"> GUIContent to use as tooltip - for ex new GUIContent("","tooltip")</param>
		public static bool FoldoutBar(bool foldout, string content, GUIContent tooltip)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			bool nfoldout = EditorGUILayout.Foldout(foldout, content, true);
			if (tooltip != null)
			{
				Rect position = GUILayoutUtility.GetLastRect();
				EditorGUI.LabelField(position, tooltip);
			}
			GUILayout.EndHorizontal();
			return nfoldout;
		}

		public static void FoldoutBarButton(ref bool foldout, string content, string button, out bool pressed, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content, true);
			pressed = GUILayout.Button(button, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
		}


		public static void FoldoutBarButton(ref bool foldout, string content, string button, out bool pressed, out int move, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content, true);

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
			foldout = EditorGUILayout.Foldout(foldout, content, true);

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
				helpExpanded = HelpGUI(helpIconRect, help, helpExpanded);
			}
		}

		public static void ToolbarStyleHeader(Rect rect, GUIContent label, string[] help, ref bool helpExpanded, GUIStyle toolbarStyleOverride = null, GUIStyle labelStyleOverride = null)
		{
			var toolbarStyle = toolbarStyleOverride != null ? toolbarStyleOverride : EditorStyles.toolbar;
			var labelStyle = labelStyleOverride != null ? labelStyleOverride : EditorStyles.label;
			var helpIconRect = new Rect(rect.xMax - 20f, rect.yMin, 20f, rect.height);

			Event current = Event.current;
			if (current.type == EventType.Repaint)
			{
				toolbarStyle.Draw(rect, GUIContent.none, false, false, false, false);
			}
			var labelWidth = labelStyle.CalcSize(label);
			var toolbarFoldoutRect = new Rect((rect.xMax / 2f) - (labelWidth.x / 2f) + 30f, rect.yMin, ((rect.width / 2) + (labelWidth.x / 2f)) - 20f - 30f, rect.height);
			EditorGUI.LabelField(toolbarFoldoutRect, label, labelStyle);

			if (help.Length > 0)
			{
				helpExpanded = HelpGUI(helpIconRect, help, helpExpanded);
			}
		}

		internal static bool HelpGUI(Rect helpIconRect, string[] help, bool helpExpanded)
		{
			var helpGUI = new GUIContent("", "Show Help");
			helpGUI.image = helpIcon;

			helpExpanded = GUI.Toggle(helpIconRect, helpExpanded, helpGUI, iconLabel);

			if (!helpExpanded) return helpExpanded;

			BeginVerticalPadded(3, new Color(0.75f, 0.875f, 1f, 0.3f));
			for (int i = 0; i < help.Length; i++)
			{
				EditorGUILayout.HelpBox(help[i], MessageType.None);
			}
			EndVerticalPadded(3);

			return helpExpanded;
		}

		/// <summary>
		/// Draws an object field with an 'inspect' button next to it which opens up the editor for the assigned object in a popup window
		/// </summary>
		public static void InspectableObjectField(Rect position, SerializedProperty property, GUIContent label, UnityAction<EditorWindow> onCreateWindowCallback = null)
		{
			label = EditorGUI.BeginProperty(position, label, property);
			var objectFieldRect = position;
			var inspectBtnRect = Rect.zero;
			if (property.objectReferenceValue)
			{
				objectFieldRect = new Rect(position.xMin, position.yMin, position.width - inspectBtnWidth - 4f, position.height);
				inspectBtnRect = new Rect(objectFieldRect.xMax + 4f, objectFieldRect.yMin, inspectBtnWidth, objectFieldRect.height);
			}
			EditorGUI.ObjectField(objectFieldRect, property, label);
			if (property.objectReferenceValue)
			{
				if (GUI.Button(inspectBtnRect, inspectContent, inspectStyle))
				{
					var inspectorWindow = InspectorUtlity.InspectTarget(property.objectReferenceValue);
					if (onCreateWindowCallback != null)
						onCreateWindowCallback(inspectorWindow);
				}
			}
			EditorGUI.EndProperty();
		}

		/// <summary>
		/// Creates a Drag & Drop area and manages basic editor events.
		/// </summary>
		/// <param name="processItem"> Processing method per Item dropped. Returns True if anything was successfully processed </param>
		/// <param name="dropArea"> Rect of the Drop Area </param>
		/// <param name="label"> Optional : Drop Area Label and tooltip. "Drop Area" by default </param>
		/// <returns> True if any Item was processed </returns>
		public static bool DropAreaGUI(Func<UnityEngine.Object, bool> processItem, Rect dropArea, GUIContent label = default, GUIStyle style = default)
		{
			if (style == default) style = new GUIStyle(GUI.skin.box);
			GUI.Box(dropArea, label == default ? new GUIContent("Drop Area") : label, style);

			bool processed = false;
			var evt = Event.current;

			if (evt.type == EventType.DragUpdated)
			{
				if (dropArea.Contains(evt.mousePosition))
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

				return false;
			}

			// Exit if Editor event is not the end of a Drag in the Drop Area
			if (evt.type != EventType.DragPerform || !dropArea.Contains(evt.mousePosition)) return false;

			DragAndDrop.AcceptDrag();

			UnityEngine.Object[] draggedObjects = DragAndDrop.objectReferences as UnityEngine.Object[];
			for (int i = 0; i < draggedObjects.Length; i++)
			{
				if (!draggedObjects[i]) continue;

				processed |= processItem(draggedObjects[i]);
			}
			return processed;
		}

		/// <summary>
		/// Creates a Drag & Drop area and manages basic editor events.
		/// </summary>
		/// <param name="processItem"> Processing method per Item dropped. Returns True if anything was successfully processed </param>
		/// <param name="height"> Optional : The Height of the Drag & Drop Area. 50f by default. Area takes all the inspector width. </param>
		/// <param name="label"> Optional : Drop Area Label and tooltip. "Drop Area" by default </param>
		/// <returns> True if any Item was processed </returns>
		public static bool DropAreaGUI(Func<UnityEngine.Object, bool> processItem, float height = 50f, GUIContent label = default, GUIStyle style = default)
		{
			Rect dropArea = GUILayoutUtility.GetRect(0.0f, height, GUILayout.ExpandWidth(true));
			return DropAreaGUI(processItem, dropArea, label, style);
		}
	}
}
#endif