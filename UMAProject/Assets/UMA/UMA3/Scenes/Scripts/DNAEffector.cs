using UnityEngine;
using UnityEngine.UI;

namespace UMA
{

    public class DNAEffector : MonoBehaviour
    {
        public IDNASelector dNAEffector;
        public string dnaName;

        public void Setup(IDNASelector dNAEffector, string dnaName, string label, float value)
        {
            this.dNAEffector = dNAEffector;
            this.dnaName = dnaName;
            Text t = GetComponentInChildren<Text>();
            t.text = label;
            Reset(value);
        }

        public void Reset(float value)
        {
            Slider slider = GetComponentInChildren<Slider>();
            slider.value = value;
        }

        public void DNAChanged(float value)
        {
            // The slider can report a value before Setup has given it a character (a UI rebuild, also in Edit mode).
            if (dNAEffector == null)
            {
                return;
            }
            dNAEffector.SetDNA(dnaName, value);
        }
    }
}