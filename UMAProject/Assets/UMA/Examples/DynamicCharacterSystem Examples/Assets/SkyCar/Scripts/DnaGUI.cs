using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UMA.CharacterSystem;

namespace UMA.Examples
{
    public class DnaGUI : MonoBehaviour
    {
        public DynamicCharacterAvatar avatar;
        public string dnaName;

        private DnaSetter DNA;
        private Slider slider;

        // Use this for initialization
        void Start()
        {
            avatar.CharacterCreated.AddListener(Initialize);
        }

        private void Initialize(UMAData umaData)
        {
            Dictionary<string, DnaSetter> allDNA = avatar.GetDNA();
            if (allDNA.ContainsKey(dnaName))
            {
                DNA = allDNA[dnaName];
            }
            else
            {
                if (Debug.isDebugBuild)
                {
                    Debug.Log("dnaName not in dna name list!");
                }
            }

            slider = GetComponent<Slider>();
            slider.onValueChanged.AddListener(ValueChanged);

            if(DNA != null)
            {
                slider.value = DNA.Get();
            }
        }

        public void ValueChanged(float val)
        {
            if (DNA != null)
            {
                DNA.Set(val);
                avatar.ForceUpdate(true, false, false);
            }
            else
            {
                if (Debug.isDebugBuild)
                {
                    Debug.Log("DNA is null!");
                }
            }
        }

    }
}
