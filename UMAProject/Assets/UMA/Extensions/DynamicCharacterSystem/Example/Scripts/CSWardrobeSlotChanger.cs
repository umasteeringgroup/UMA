using UnityEngine;
using System.Collections;

public class CSWardrobeSlotChanger : MonoBehaviour {

	public string wardrobeSlotToChange;

	public TestCustomizer customizerScript;

	public void ChangeWardrobeSlot(float slotId){
		customizerScript.SetSlot(wardrobeSlotToChange, slotId);
	}
}
