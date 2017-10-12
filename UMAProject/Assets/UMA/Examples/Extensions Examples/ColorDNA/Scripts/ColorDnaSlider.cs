using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UMA;

public class ColorDnaSlider : MonoBehaviour 
{
    public int dnaTypeHash = 1566835252;
    public int dnaEntryIndex = 0;

    protected UMAData data;
    protected UMADnaBase dna;

    public void OnCharacterCreated(UMAData umaData)
    {
        this.data = umaData;
        Slider slider = gameObject.GetComponent<Slider>();

        dna = umaData.GetDna(dnaTypeHash);      
        if (dna != null)
        {
            slider.value = dna.GetValue(dnaEntryIndex);
        }
    }

    public void SetColorDNA(float value)
    {
        if (dna == null)
        {
            dna = data.GetDna(dnaTypeHash);     
        }

        if (dna != null)
        {
            dna.SetValue(dnaEntryIndex, value);
            data.Dirty(true, true, false);
        }
    }
}
