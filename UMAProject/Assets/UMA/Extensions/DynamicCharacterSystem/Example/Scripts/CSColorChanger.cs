using UnityEngine;
using System.Collections;

public class CSColorChanger : MonoBehaviour {

	public string colorToChange;

	public TestCustomizer customizerScript;

	public void ChangeColor(float colorId){
		customizerScript.SetColor(colorToChange, colorId);
	}
}
