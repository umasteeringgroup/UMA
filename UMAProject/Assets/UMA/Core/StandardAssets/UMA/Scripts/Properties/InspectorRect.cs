using UnityEngine;

#if UNITY_EDITOR
namespace UMA
{
	public class InspectorRect 
	{
		Rect rect;
		float padding;
		
		public InspectorRect(Rect rect, float padding = 0)
		{
			this.rect = new Rect(rect.x+padding, rect.y+padding, rect.width-padding*2, rect.height-padding*2);
			this.padding = padding;
		}

		public Rect GetLineRect(float height = 17f)
		{
			Rect result = new Rect(rect.x, rect.y, rect.width, height);
			rect.y += height + padding;
			return result;
		}
		
		public Rect GetColumnRect(int column, int columnCount)
		{
			Rect result = new Rect(rect.x + (rect.width * column) / columnCount, rect.y, rect.width / columnCount, rect.height);
			return result;
		}
	}
}
#endif