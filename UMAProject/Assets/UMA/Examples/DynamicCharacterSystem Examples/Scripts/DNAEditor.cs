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
		DNARangeAsset _dnr;

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
            _InitialValue = currentval;

			DNARangeAsset[] dnaRangeAssets = avatar.activeRace.data.dnaRanges;
			foreach (DNARangeAsset d in dnaRangeAssets) 
			{
				if (d.ContainsDNARange (_Index, _DNAName)) {
					_dnr = d;
					return;
				}
			}
        }

        public void ChangeValue(float value)
        {
			if (_dnr == null) //No specified DNA Range Asset for this DNA
			{ 
				_Owner.SetValue (_Index, value);
				_Avatar.ForceUpdate (true, false, false);				
				return;
			}
			
			if (_dnr.ValueInRange (_Index, value))
			{
				_Owner.SetValue (_Index, value);
				_Avatar.ForceUpdate(true, false, false);
				return;
			}
			else
			{
				//Debug.LogWarning ("DNA Value out of range!");
			}
        }
    }
}
