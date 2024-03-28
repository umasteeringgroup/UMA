using UnityEngine;

namespace UMA.PoseTools
{
    public class ActiveObjectSwitcher : MonoBehaviour
	{
		public GameObject[] objects = new GameObject[0];
		public GameObject activeObj = null;

		private int selected = 0;
		private string[] names = null;

		// Position variables
		public int xPos = 25;
		public int yPos = 25;

		// Use this for initialization
		void Start()
		{
			if ((activeObj == null) && (objects.Length > 0))
			{
				activeObj = objects[0];
			}

			selected = 0;
			names = new string[objects.Length];
			for (int i = 0; i < objects.Length; i++)
			{
				names[i] = objects[i].name;
				if (activeObj == objects[i])
				{
					selected = i;
				}
			}
		}

		void OnGUI()
		{
			GUILayout.BeginArea(new Rect(xPos, yPos, 80, 400));

			int newSelected = GUILayout.SelectionGrid(selected, names, 1);
			if (newSelected != selected)
			{
				activeObj.SetActive(false);
				selected = newSelected;
				activeObj = objects[newSelected];
				activeObj.SetActive(true);
			}

			GUILayout.EndArea();
		}
	}
}