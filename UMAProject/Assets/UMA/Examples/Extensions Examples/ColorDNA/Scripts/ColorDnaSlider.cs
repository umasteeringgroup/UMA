using UnityEngine;
using System.Collections;
using UnityEngine.UI;
using UMA;
using UMA.CharacterSystem;

public class ColorDnaSlider : MonoBehaviour 
{
    public int dnaTypeHash = 1566835252;
    public int dnaEntryIndex = 0;

    [SerializeField]
    private DynamicCharacterAvatar avatar;
    protected UMAData data;
    protected UMADnaBase dna;

    private void Start()
    {
        if (avatar != null)
            avatar.CharacterBegun.AddListener(OnCharacterCreated);
    }

    public void OnCharacterCreated(UMAData umaData)
    {
        this.data = umaData;
        Slider slider = gameObject.GetComponent<Slider>();

        dna = umaData.GetDna(dnaTypeHash);      
        if (dna != null)
        {
            umaData.ApplyDNA();
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
            data.ApplyDNA();
            data.Dirty(true, true, false);
        }
    }
}
