#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace UMA
{
	using UnityEditor;
	public class InspectorRect 
	{
		Rect rect;
		float padding;
		
		/// <summary>
		/// Creates an inspector gui rect that can be used to generate gui rects in a simple layout
		/// </summary>
		/// <param name="rect">the source unity rect</param>
		/// <param name="padding">how many pixels of padding, default -1 translates into unity default</param>
		public InspectorRect(Rect rect, float padding = -1)
		{
			if (padding == -1)
				padding = EditorGUIUtility.standardVerticalSpacing;
			this.rect = new Rect(rect.x+padding, rect.y+padding, rect.width-padding*2, rect.height-padding*2);
			this.padding = padding;
		}

		/// <summary>
		/// Allocates a line rect, default height turns into a single line 
		/// </summary>
		/// <param name="height">default of -1 translates into a single line of text</param>
		/// <returns>rect you can pass to GUI rendering</returns>
		public Rect GetLineRect(float height = -1f)
		{
			if (height == -1f)
				height = EditorGUIUtility.singleLineHeight; 
			Rect result = new Rect(rect.x, rect.y, rect.width, height);
			rect.y += height + padding;
			return result;
		}

		/// <summary>
		/// Returns a column rect
		/// </summary>
		/// <param name="column">column we're getting rect for</param>
		/// <param name="columnCount">number of columns</param>
		/// <returns>rect you can pass to GUI rendering</returns>
		public Rect GetColumnRect(int column, int columnCount)
		{
			Rect result = new Rect(rect.x + (rect.width * column) / columnCount, rect.y, rect.width / columnCount, rect.height);
			return result;
		}
	}
}
#endif