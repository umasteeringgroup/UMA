using UMA;
using UMA.CharacterSystem;
using UnityEngine;
using UnityEngine.UI;

namespace UMA
{
    public class SliderController : MonoBehaviour
    {
        public GameObject SearchMe;
        public DynamicCharacterAvatar Avatar;
        public string DnaName = "headSize";    // case matters here. 
        public Slider theSlider;

        public void AvatarGenerated(GameObject Generator, GameObject Character)
        {
            Avatar = SearchMe.GetComponentInChildren<DynamicCharacterAvatar>();
            SetSlider();
        }

        public void SetSlider()
        {
            if (theSlider != null)
            {
                RaceData race = Avatar.activeRace.data;
                foreach (var d in race.dnaRanges)
                {
                    int index = d.IndexForDNAName(DnaName);
                    if (index >= 0)
                    {
                        theSlider.minValue = d.means[index] - d.spreads[index];
                        theSlider.maxValue = d.means[index] + d.spreads[index];
                        //Debug.Log("Min = " + theSlider.minValue + " Max = " + theSlider.maxValue);
                        return;
                    }
                }
            }
        }

        public void SetDNA(float Value)
        {
            if (Avatar == null)
            {
                Avatar = FindAvatar();
            }

            if (Avatar == null)
            {
                return;
            }

            // Set the DNA on the Avatar.
            // Case must match.
            // If you cache DNA, you must do it after the character is completely built
            var MyDNA = Avatar.GetDNA();
            if (MyDNA.ContainsKey(DnaName))
            {
                MyDNA[DnaName].Set(Value);
                Avatar.ForceUpdate(true);
            }
        }

        private DynamicCharacterAvatar FindAvatar()
        {
            return SearchMe.GetComponentInChildren<DynamicCharacterAvatar>();
        }
    }
}