using UnityEngine;

namespace UMA.Examples
{
    public class UMADnaTweaker : MonoBehaviour
	{
		public string dnaToTweak;
		public UMACustomization customizer;

		public void TweakDNA(float newValue)
		{

			if (string.IsNullOrEmpty(dnaToTweak))
            {
                return;
            }

            if (customizer == null)
            {
                return;
            }

            customizer.PerformDNAChange(dnaToTweak, newValue);
		}
	}
}
