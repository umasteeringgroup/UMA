using UnityEngine;
using System.Collections;

namespace UMA.CharacterSystem.Examples
{
	public class CSColorChangerDD : MonoBehaviour
	{
		public string colorToChange;

		public TestCustomizerDD customizerScript;

		public void ChangeColor(int colorId)
		{
			customizerScript.SetColor(colorToChange, colorId);
		}
	}
}
