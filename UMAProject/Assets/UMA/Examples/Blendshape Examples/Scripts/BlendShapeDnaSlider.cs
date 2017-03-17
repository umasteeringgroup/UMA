using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UMA;
using UMACharacterSystem;

public class BlendShapeDnaSlider : MonoBehaviour {

	public DynamicCharacterAvatar avatar;

	Dictionary<string, DnaSetter> dna = new Dictionary<string, DnaSetter>();


	public void OnCharacterCreated(UMAData umaData)
	{
		if (avatar) 
		{
			dna = avatar.GetDNA ();		
			Slider slider = gameObject.GetComponent<Slider> ();
			slider.value = dna ["MaleEarsMorph"].Value;
		}
	}

	public void SetEarMorph(float value)
	{
		dna ["MaleEarsMorph"].Set (value);
		avatar.ForceUpdate (true);
	}
}
