using UnityEngine;
using System.Collections;

namespace UMA.PoseTools
{
	public class ExpressionSlotScript : MonoBehaviour 
	{
		public void OnDnaApplied(UMAData umaData)
		{
			var expressionSet = umaData.umaRecipe.raceData.expressionSet;
			if (expressionSet == null)
			{
				Debug.LogError("Couldn't add Expressions to Race: " + umaData.umaRecipe.raceData.raceName, umaData.gameObject);
				return;
			}
			var expressionPlayer = umaData.animator.GetComponent<UMAExpressionPlayer>();
			if (expressionPlayer == null)
			{
				expressionPlayer = umaData.animator.gameObject.AddComponent<UMAExpressionPlayer>();
			}
			expressionPlayer.expressionSet = expressionSet;
			expressionPlayer.umaData = umaData;
#pragma warning disable 618
			umaData.animatedBones = expressionSet.GetAnimatedBones(umaData.skeleton);
#pragma warning restore 618
		}
	}
}