using UnityEngine;
using UnityEditor;

namespace UMA.Editors
{
    //Draws the popup you see whenever a DNAEvaluationGraph field is clicked.
    //calls on DNAEvaluationGraphPresetLibrary to get the tooltips so these dont have to be stored along with the field
    public class DNAEvaluationGraphPopupContent : PopupWindowContent
	{

		private DNAEvaluationGraph _selectedPreset;

		public SerializedProperty property;

		public delegate void OnSelectedDelegate(DNAEvaluationGraph selectedGraph, SerializedProperty property);

		/// <summary>
		/// Called when a selection is made from the popup
		/// </summary>
		public OnSelectedDelegate OnSelected;

		DNAEvaluationGraphPropertyDrawer dnaEvalDrawer = new DNAEvaluationGraphPropertyDrawer();

		public float width = 200f;

		private float minWidth = 200f;
		private float minHeight = 150f;
		//entries show in a scrollRect if the height is bigger than this
		private float maxHeight = 400f;

		private float entryHeight = EditorGUIUtility.singleLineHeight * 2f;

		private float padding = EditorGUIUtility.standardVerticalSpacing * 2f;

		private Vector2 scrollPosition;
		private int _selectedIndex = -1;
		public int _hoveredIndex = -1;

		public DNAEvaluationGraph selectedPreset
		{
			get { return _selectedPreset; }
			set
			{
				_hoveredIndex = -1;
				_selectedPreset = value;
			}
		}

		public override Vector2 GetWindowSize()
		{
			var entrysHeightCalc = 0f;
			if (DNAEvaluationGraphPresetLibrary.AllGraphPresets.Count > 0)
			{
				entrysHeightCalc = (entryHeight * DNAEvaluationGraphPresetLibrary.AllGraphPresets.Count) + (padding * 2f);
				entrysHeightCalc += (EditorGUIUtility.singleLineHeight * 2f) + (padding * 2f);
				if (entrysHeightCalc > maxHeight)
                {
                    entrysHeightCalc = maxHeight;
                }
            }
			else
			{
				entrysHeightCalc = minHeight;
			}
			return new Vector2((width > minWidth ? width : minWidth), entrysHeightCalc);
		}

		public override void OnGUI(Rect position)
		{
			GUI.Label(position, GUIContent.none, GUI.skin.box);
			Event current = Event.current;
			
			//handle the user editing a field whose graph is not longer in any preset libraries
			var allPresets = DNAEvaluationGraphPresetLibrary.AllGraphPresets;
			var allPresetTooltips = DNAEvaluationGraphPresetLibrary.AllGraphTooltips;
			if (_selectedPreset != null && !allPresets.Contains(_selectedPreset))
			{
				allPresets.Insert(0, _selectedPreset);
				allPresetTooltips.Insert(0, _selectedPreset.name + " (Not in Presets Library)");
			}
			scrollPosition = GUILayout.BeginScrollView(scrollPosition);
			if (DNAEvaluationGraphPresetLibrary.AllGraphPresets.Count > 0)
			{
				for (int index = 0; index < allPresets.Count; index++)
				{
					Rect rect = GUILayoutUtility.GetRect(16f, entryHeight + (padding * 2f), new GUILayoutOption[1]
					{
						GUILayout.ExpandWidth(true)
					});
					rect.x += padding;
					rect.width -= padding * 2f;
					Rect swatchRect = new Rect(rect.xMin, rect.yMin + padding, rect.width, rect.height - (padding * 2f));
					bool selectedFlag = false;
					bool hoveredFlag = false;
					if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
					{
						_selectedIndex = index;
						_selectedPreset = allPresets[index];
						this.editorWindow.Close();
					}
					else if (current.type != EventType.MouseDown && (_selectedPreset != null && allPresets[index] == _selectedPreset))
					{
						_selectedIndex = index;
					}
					if (current.type == EventType.MouseMove && rect.Contains(current.mousePosition))
					{
						_hoveredIndex = index;
						this.editorWindow.Repaint();
					}
					if (index == _selectedIndex)
					{
						selectedFlag = true;
					}
					if (index == _hoveredIndex)
					{
						hoveredFlag = true;
					}
					if (current.type == EventType.Repaint)
					{
						dnaEvalDrawer.DrawSwatch(swatchRect, allPresets[index], allPresetTooltips[index], hoveredFlag, selectedFlag);
					}
				}
			}
			else
			{
				EditorGUILayout.HelpBox("There were no DNAEvaluationGraph presets.", MessageType.Warning);
			}
			GUILayout.EndScrollView();

			GUILayout.Space(10f);
		}

		public override void OnOpen()
		{
			//
		}

		public override void OnClose()
		{
			if (OnSelected != null)
            {
                OnSelected(_selectedPreset, property);
            }
        }
	}
}