using UnityEngine;
using System.Collections;

namespace UMA
{
	/// <summary>
	/// Humanoid DNA.
	/// </summary>
	/// <remarks>
	/// Contains a large number of variables for the ways that the
	/// basic UMA human models can be adjusted to create different
	/// body shapes and sizes.
	/// </remarks>
	[System.Serializable]
	public partial class UMADnaHumanoid : UMADna
	{
		public float height = 0.5f;
		public float headSize = 0.5f;
		public float headWidth = 0.5f;
		public float neckThickness = 0.5f;
		public float armLength = 0.5f;
		public float forearmLength = 0.5f;
		public float armWidth = 0.5f;
		public float forearmWidth = 0.5f;
		
		public float handsSize = 0.5f;
		public float feetSize = 0.5f;
		public float legSeparation = 0.5f;
		public float upperMuscle = 0.5f;
		public float lowerMuscle = 0.5f;
		public float upperWeight = 0.5f;
		public float lowerWeight = 0.5f;
		public float legsSize = 0.5f;
		public float belly = 0.5f;
		public float waist = 0.5f;
		public float gluteusSize = 0.5f;
		
		public float earsSize = 0.5f;
		public float earsPosition = 0.5f;
		public float earsRotation = 0.5f;
		public float noseSize = 0.5f;
		public float noseCurve = 0.5f;
		public float noseWidth = 0.5f;
		public float noseInclination = 0.5f;
		public float nosePosition = 0.5f;
		public float nosePronounced = 0.5f;
		public float noseFlatten = 0.5f;
		
		public float chinSize = 0.5f;
		public float chinPronounced = 0.5f;
		public float chinPosition = 0.5f;
		
		public float mandibleSize = 0.5f;
		public float jawsSize = 0.5f;
		public float jawsPosition = 0.5f;
		
		public float cheekSize = 0.5f;
		public float cheekPosition = 0.5f;
		public float lowCheekPronounced = 0.5f;
		public float lowCheekPosition = 0.5f;
		
		public float foreheadSize = 0.5f;
		public float foreheadPosition = 0.5f;
		
		public float lipsSize = 0.5f;
		public float mouthSize = 0.5f;
		public float eyeRotation = 0.5f;
		public float eyeSize = 0.5f;
		
		public float breastSize = 0.5f;
	}
}