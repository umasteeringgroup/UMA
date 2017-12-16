using UnityEngine;
using UnityEngine.UI;

namespace UMA.CharacterSystem
{
    public class DNAEditor : MonoBehaviour
    {
        string _DNAName;
        int _Index;
        UMADnaBase _Owner;   // different DNA 
        DynamicCharacterAvatar _Avatar;
        float _InitialValue;

        public Slider ValueSlider;
        public Text Label;

        // Use this for initialization
        void Start()
        {
            ValueSlider.value = _InitialValue;
            Label.text = _DNAName;
        }

        public void Initialize(string name, int index, UMADnaBase owner, DynamicCharacterAvatar avatar, float currentval)
        {
            _DNAName = name;
            _Index = index;
            _Owner = owner;
            _Avatar = avatar;

            //not used?
            //DNARangeAsset[] dnr = avatar.RaceData.dnaRanges;
            //dnr[0].
            //             values[i] = means[i] + (Random.value - 0.5f) * spreads[i];
            _InitialValue = currentval;
        }

        public void ChangeValue(float value)
        {
            _Owner.SetValue(_Index, value);
            _Avatar.ForceUpdate(true, false, false);
        }
    }
}
