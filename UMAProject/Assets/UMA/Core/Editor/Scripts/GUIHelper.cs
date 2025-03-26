#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Events;
using System.IO;
using System.Collections.Generic;

namespace UMA.Editors
{
    public static class GUIHelper
	{

		private static Texture _helpIcon;

		private static GUIStyle _iconLabel;

		private static Texture _inspectIcon;
		private static GUIContent _inspectContent;
		private static GUIStyle _inspectStyle;
		private static float inspectBtnWidth = 25f;

		private static Texture helpIcon
		{
			get {
				if (_helpIcon != null)
                {
                    return _helpIcon;
                }
                //Sometimes editor styles is not set up when we ask for this
                if (EditorStyles.label == null)
                {
                    return new Texture2D(16,16);
                }

                _helpIcon = EditorGUIUtility.FindTexture("_Help");
				return _helpIcon;
			}
		}

		private static GUIStyle iconLabel
		{
			get
			{
				if (_iconLabel != null)
                {
                    return _iconLabel;
                }

                if (EditorStyles.label == null)
                {
                    return new GUIStyle();
                }

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
                {
                    return _inspectIcon;
                }
                //Check EditorStyles has been set up
                if (EditorStyles.label == null)
                {
                    return new Texture2D(16, 16);
                }

                _inspectIcon = EditorGUIUtility.FindTexture("ViewToolOrbit");
				return _inspectIcon;
			}
		}

		private static GUIContent inspectContent
		{
			get
			{
				if (_inspectContent != null && _inspectIcon != null)
                {
                    return _inspectContent;
                }

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
                {
                    return _inspectStyle;
                }
                //Check EditorStyles is set up
                if (EditorStyles.miniButton == null)
                {
                    return new GUIStyle();
                }

                _inspectStyle = new GUIStyle(EditorStyles.miniButton);
				_inspectStyle.contentOffset = new Vector2(0f, 0f);
				_inspectStyle.padding = new RectOffset(0, 0, 0, 0);
				_inspectStyle.margin = new RectOffset(0, 0, 0, 0);
				return _inspectStyle;
			}
		}

		public static void BeginVerticalPadded()
		{
			if (EditorGUIUtility.isProSkin)
			{
				GUIHelper.BeginVerticalPadded(10, new Color(1.3f, 1.4f, 1.5f));
			}
			else
			{
				GUIHelper.BeginVerticalPadded(10, new Color(0.75f, 0.875f, 1f));
			}
		} 

		public static void EndVerticalPadded()
		{
			GUIHelper.EndVerticalPadded(10);
		}
        public static Rect ShrinkRect(Rect rect, float inset, float xinset = -1)
        {
            if (xinset < 0)
            {
                xinset = inset;
            }
            return new Rect(rect.x + xinset, rect.y + inset, rect.width - xinset * 2, rect.height - inset * 2);
        }

        public static void BeginInsetArea(Color backgroundColor, Rect rect, float inset, float vertpadding, float xinset = -1, GUIStyle theStyle = null)
        {
            if (theStyle == null)
            {
                theStyle = EditorStyles.textField;
            }

            if (xinset < 0)
            {
                xinset = inset;
            }
            //EditorGUI.DrawRect(ShrinkRect(rect, inset, xinset), backgroundColor);
            GUILayout.BeginArea(ShrinkRect(rect, inset/*2*/, xinset),theStyle);
            GUILayout.Space(vertpadding);
        }

        public static void BeginInsetArea(Color backgroundColor, Rect rect, float inset)
        {
            EditorGUI.DrawRect(ShrinkRect(rect, inset, inset), backgroundColor);
            GUILayout.BeginArea(ShrinkRect(rect, inset*2, inset));
        }

        public static void EndInsetArea()
        {
            GUILayout.EndArea();
        }

		public static void BeginVerticalPadded(float padding, Color backgroundColor, GUIStyle theStyle = null)
		{
			if (theStyle == null)
            {
                theStyle = EditorStyles.textField;
            }

            GUI.color = backgroundColor;
			GUILayout.BeginHorizontal(theStyle);
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

		public static void FoldoutBar(ref bool foldout, string content, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content,true);
			delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
		}

        public static bool FoldoutBarWithDelete(bool foldout, string content, out bool delete)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            GUILayout.Space(10);
            foldout = EditorGUILayout.Foldout(foldout, content, true);
            delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
            return foldout;
        }

        public static bool FoldoutBar(bool foldout, string content)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			bool nfoldout = EditorGUILayout.Foldout(foldout, content,true);
			GUILayout.EndHorizontal();
			return nfoldout;
		}
		public static void FoldoutBarButton(ref bool foldout, string content, string button, out bool pressed, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content,true);
			pressed = GUILayout.Button(button, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
		}

        public static void FoldoutBarButton(ref bool foldout, string content, List<string> buttons, out List<bool> pressed, out int move, out bool delete)
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
            GUILayout.Space(10);
            foldout = EditorGUILayout.Foldout(foldout, content, true);

            pressed = new List<bool>();
            for (int i = 0; i < buttons.Count; i++)
            {
                pressed.Add(GUILayout.Button(buttons[i], EditorStyles.miniButton, GUILayout.ExpandWidth(false)));
            }
            //            pressed = GUILayout.Button(button, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            //            pressed2 = GUILayout.Button(button2, EditorStyles.miniButton, GUILayout.ExpandWidth(false));

            move = 0;
            if (GUILayout.Button("\u25B2", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                move--;
            }

            if (GUILayout.Button("\u25BC", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                move++;
            }

            delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();
        }

        public static void FoldoutBarButton(ref bool foldout, string content, string button, out bool pressed, out int move, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content, true);

			move = 0;
			if (GUILayout.Button("\u25B2", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                move--;
            }

            if (GUILayout.Button("\u25BC", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                move++;
            }

            pressed = GUILayout.Button(button, EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			delete = GUILayout.Button("\u0078", EditorStyles.miniButton, GUILayout.ExpandWidth(false));
			GUILayout.EndHorizontal();
		}

		public static void FoldoutBar(ref bool foldout, string content, out int move, out bool delete)
		{
			GUILayout.BeginHorizontal(EditorStyles.toolbarButton);
			GUILayout.Space(10);
			foldout = EditorGUILayout.Foldout(foldout, content,true);

			move = 0;
			if (GUILayout.Button("\u25B2", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                move--;
            }

            if (GUILayout.Button("\u25BC", EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
            {
                move++;
            }

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
            {
                toolbarStyle = EditorStyles.toolbar;
            }

            if (labelStyle == null)
            {
                labelStyle = EditorStyles.foldout;
            }

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

		public static void ToolbarStyleHeader(Rect rect, GUIContent label, string[] help, ref bool helpExpanded, GUIStyle toolbarStyleOverride = null, GUIStyle labelStyleOverride = null)
		{
			var toolbarStyle = toolbarStyleOverride != null ? toolbarStyleOverride : EditorStyles.toolbar;
			var labelStyle = labelStyleOverride != null ? labelStyleOverride : EditorStyles.label;
			var helpIconRect = new Rect(rect.xMax - 20f, rect.yMin, 20f, rect.height);
			var helpGUI = new GUIContent("", "Show Help");
			helpGUI.image = helpIcon;
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
                    {
                        onCreateWindowCallback(inspectorWindow);
                    }
                }
			}
			EditorGUI.EndProperty();
		}

        private static string[] defaultTags;
        private static string[] basetagnames;
		private static int selectedTag = 0;

        private static void GetTags()
        {
            if (defaultTags == null)
            {
                defaultTags = UMAEditorUtilities.GetDefaultTags();
                basetagnames = UMAEditorUtilities.GetDefaultBaseTags();
            }
        }

		public static UnityEditorInternal.ReorderableList InitGenericTagsList(List<string> tags)
		{
            GetTags();

            var tagsList = new UnityEditorInternal.ReorderableList(tags, typeof(string), true, true, false, false);
            tagsList.drawHeaderCallback = (Rect rect) =>
            {
                float width1 = 56;
                float width3 = 44;  // Add button
                float width4 = 20;  // + button
                float width5 = 44;  // Clear button
                float width6 = 44;  // load button
                float width7 = 44;  // save button
                float width2 = rect.width - (width1 + width3 + width4 + width5 + width6 + width7);

                float pos1 = 0;
                float pos2 = pos1 + width1;
                float pos3 = pos2 + width2;
                float pos4 = pos3 + width3;
                float pos5 = pos4 + width4;
                float pos6 = pos5 + width5;
                float pos7 = pos6 + width6;

                Rect ctrl = new Rect(rect.x, rect.y, rect.width - 20, rect.height);

                Rect labelrect = new Rect(rect.x + pos1, rect.y, width1, rect.height);
                Rect dropdownrect = new Rect(rect.x + pos2, rect.y, width2, rect.height);
                Rect adddropdownrect = new Rect(rect.x + pos3, rect.y, width3, rect.height);
                Rect addBlankrect = new Rect(rect.x + pos4, rect.y, width4, rect.height);
                Rect clearRect = new Rect(rect.x + pos5, rect.y, width5, rect.height);
                Rect loadRect = new Rect(rect.x + pos6, rect.y, width6, rect.height);
                Rect saveRect = new Rect(rect.x + pos7, rect.y, width7, rect.height);

                EditorGUI.LabelField(labelrect, "Select");

                if (defaultTags.Length > 0)
                {
                    selectedTag = Mathf.Clamp(selectedTag, 0, defaultTags.Length - 1);
                    selectedTag = EditorGUI.Popup(dropdownrect, selectedTag, defaultTags);

                    if (GUI.Button(adddropdownrect, "Add", EditorStyles.miniButton))
                    {
                        string newTag = basetagnames[selectedTag];
                        bool found = false;
                        for (int i = 0; i < tagsList.list.Count; i++)
                        {
                            if ((string)tagsList.list[i] == newTag)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            tagsList.list.Add(newTag);
                        }
                    }

                    if (GUI.Button(clearRect, "Clear", EditorStyles.miniButton))
                    {
                        tagsList.list.Clear();
                    }

                    if (GUI.Button(loadRect, "Load", EditorStyles.miniButton))
                    {
                        string fname = EditorUtility.OpenFilePanel("Load", "", "txt");
                        {
                            if (!string.IsNullOrEmpty(fname))
                            {
                                var tags = File.ReadAllLines(fname);
                                tagsList.list.Clear();
                                for (int i = 0; i < tags.Length; i++)
                                {
                                    tagsList.list.Add(tags[i]);
                                }
                            }
                        }
                        GUIUtility.ExitGUI(); // This is necessary to prevent a crash in Unity
                    }
                    if (GUI.Button(saveRect, "Save", EditorStyles.miniButton))
                    {
                        string fname = EditorUtility.SaveFilePanel("Save", "", "Tags", "txt");
                        {
                            if (!string.IsNullOrEmpty(fname))
                            {
                                string[] tags = new string[tagsList.list.Count];
                                for (int i = 0; i < tagsList.list.Count; i++)
                                {
                                    tags[i] = tagsList.list[i].ToString();
                                }
                                File.WriteAllLines(fname, tags);
                            }
                        }
                        GUIUtility.ExitGUI(); // This is necessary to prevent a crash in Unity
                    }
                    if (GUI.Button(addBlankrect, "+", EditorStyles.miniButton))
                    {
                        tagsList.list.Add("");
                    }
                }
                else
                {
                    GUI.Label(ctrl, "Add defaults in prefs");
                    if (GUI.Button(addBlankrect, "+", EditorStyles.miniButton))
                    {
                        tagsList.list.Add("");
                    }
                }
            };

            tagsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (tagsList.list.Count <= index)
                    return;
                var element = tagsList.list[index].ToString();
                rect.y += 2;
                tagsList.list[index]= EditorGUI.DelayedTextField(new Rect(rect.x, rect.y, rect.width - 22, EditorGUIUtility.singleLineHeight), element);
                // draw the remove button
                if (GUI.Button(new Rect(rect.x + rect.width - 20, rect.y, 20, EditorGUIUtility.singleLineHeight), "x"))
                {
                    tagsList.list.RemoveAt(index);
                }
            };
            tagsList.footerHeight = 0;
            return tagsList;
        }
    

        public static UnityEditorInternal.ReorderableList InitTagsList(string fieldName, SerializedObject serializedObject)
        {
            GetTags();

            var arrayProperty = serializedObject.FindProperty(fieldName);
            //var HideTagsProperty = serializedObject.FindProperty("HideTags");
            var tagsList = new UnityEditorInternal.ReorderableList(serializedObject, arrayProperty, true, true, false, false);
            tagsList.drawHeaderCallback = (Rect rect) =>
            {
                // You know what would be nice?
                // if GUILayout.BeginArea worked here. It doesn't, and while reported and acknowledged,
                // it's not going to be fixed. 
                // So we have to do this the hard way.

                float width1 = 56;
                float width3 = 44;  // Add button
                float width4 = 20;  // + button
                float width5 = 44;  // Clear button
                float width6 = 44;  // load button
                float width7 = 44;  // save button
                float width2 = rect.width - (width1 + width3 + width4 + width5 + width6 + width7);

                float pos1 = 0;
                float pos2 = pos1 + width1;
                float pos3 = pos2 + width2;
                float pos4 = pos3 + width3;
                float pos5 = pos4 + width4;
                float pos6 = pos5 + width5;
                float pos7 = pos6 + width6;

                Rect ctrl = new Rect(rect.x, rect.y, rect.width - 20, rect.height);

                Rect labelrect = new Rect(rect.x + pos1, rect.y, width1, rect.height);
                Rect dropdownrect = new Rect(rect.x + pos2, rect.y, width2, rect.height);
                Rect adddropdownrect = new Rect(rect.x + pos3, rect.y, width3, rect.height);
                Rect addBlankrect = new Rect(rect.x + pos4, rect.y, width4, rect.height);
                Rect clearRect = new Rect(rect.x + pos5, rect.y, width5, rect.height);
                Rect loadRect = new Rect(rect.x + pos6, rect.y, width6, rect.height);
                Rect saveRect = new Rect(rect.x + pos7, rect.y, width7, rect.height);

                EditorGUI.LabelField(labelrect, "Select");
                //ctrl.x += rect.width - 20;
                //ctrl.width = 20;

                if (defaultTags.Length > 0)
                {
                    selectedTag = Mathf.Clamp(selectedTag, 0, defaultTags.Length - 1);
                    selectedTag = EditorGUI.Popup(dropdownrect, selectedTag, defaultTags);

                    if (GUI.Button(adddropdownrect, "Add", EditorStyles.miniButton))
                    {
                        string currentTag = basetagnames[selectedTag];
                        var arraySizeProp = arrayProperty.FindPropertyRelative("Array.size");

                        bool found = false;
                        for (var i = 0; i < arraySizeProp.intValue; i++)
                        {
                            string val = arrayProperty.GetArrayElementAtIndex(i).stringValue;
                            if (val == currentTag)
                            {
                                found = true;
                                break;
                            }
                        }

                        // Add the tag to the list if it's not already there
                        if (!found)
                        {
                            // 
                            arrayProperty.InsertArrayElementAtIndex(arrayProperty.arraySize);
                            arrayProperty.GetArrayElementAtIndex(arrayProperty.arraySize - 1).stringValue = currentTag;
                        }
                    }

                    if (GUI.Button(clearRect, "Clear", EditorStyles.miniButton))
                    {
                        arrayProperty.ClearArray();
                    }
                    if (GUI.Button(loadRect, "Load", EditorStyles.miniButton))
                    {
                        string fname = EditorUtility.OpenFilePanel("Load", "", "txt");
                        {
                            if (!string.IsNullOrEmpty(fname))
                            {
                                var tags = File.ReadAllLines(fname);
                                arrayProperty.ClearArray();
                                for (int i = 0; i < tags.Length; i++)
                                {
                                    arrayProperty.InsertArrayElementAtIndex(i);
                                    arrayProperty.GetArrayElementAtIndex(i).stringValue = tags[i];
                                }
                                GUI.changed = true;
                            }
                        }
                        serializedObject.ApplyModifiedProperties();
                        GUIUtility.ExitGUI(); // This is necessary to prevent a crash in Unity
                    }
                    if (GUI.Button(saveRect, "Save", EditorStyles.miniButton))
                    {
                        string fname = EditorUtility.SaveFilePanel("Save", "", "Tags", "txt");
                        {
                            if (!string.IsNullOrEmpty(fname))
                            {
                                string[] tags = new string[arrayProperty.arraySize];
                                for (int i = 0; i < arrayProperty.arraySize; i++)
                                {
                                    tags[i] = arrayProperty.GetArrayElementAtIndex(i).stringValue;
                                }
                                File.WriteAllLines(fname, tags);
                            }
                        }
                        serializedObject.ApplyModifiedProperties();
                        GUIUtility.ExitGUI(); // This is necessary to prevent a crash in Unity
                    }
                    if (GUI.Button(addBlankrect, "+", EditorStyles.miniButton))
                    {
                        arrayProperty.InsertArrayElementAtIndex(arrayProperty.arraySize);
                        // Sometimes, Unity will set the value to the last added element, so we need to set it to an empty string
                        arrayProperty.GetArrayElementAtIndex(arrayProperty.arraySize - 1).stringValue = "";
                    }
                }
                else
                {
                    GUI.Label(ctrl, "Add defaults in prefs");
                    if (GUI.Button(addBlankrect, "+", EditorStyles.miniButton))
                    {
                        arrayProperty.InsertArrayElementAtIndex(arrayProperty.arraySize);
                        // Sometimes, Unity will set the value to the last added element, so we need to set it to an empty string
                        arrayProperty.GetArrayElementAtIndex(arrayProperty.arraySize - 1).stringValue = "";
                    }
                }
            };

            tagsList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                if (tagsList.serializedProperty.arraySize <= index)
                    return;
                var element = tagsList.serializedProperty.GetArrayElementAtIndex(index);
                if (element == null)
                    return;
                rect.y += 2;
                element.stringValue = EditorGUI.DelayedTextField(new Rect(rect.x, rect.y, rect.width - 22, EditorGUIUtility.singleLineHeight), element.stringValue);
                // draw the remove button
                if (GUI.Button(new Rect(rect.x + rect.width - 20, rect.y, 20, EditorGUIUtility.singleLineHeight), "x"))
                {
                    tagsList.serializedProperty.DeleteArrayElementAtIndex(index);
                }
            };
            tagsList.footerHeight = 0;
            return tagsList;
        }

    }
}
#endif
