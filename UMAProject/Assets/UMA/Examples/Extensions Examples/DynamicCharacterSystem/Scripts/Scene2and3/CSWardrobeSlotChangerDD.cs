using UnityEngine;
using UnityEngine.UI;

public class CSWardrobeSlotChangerDD : MonoBehaviour {

	public string wardrobeSlotToChange;

	public TestCustomizerDD customizerScript;

	public void ChangeWardrobeSlot(int slotId){
		customizerScript.SetSlot(wardrobeSlotToChange, slotId -1);
	}
}
