using UnityEngine;
using System.Collections;
using UnityEngine.Events;

namespace UMA.PoseTools
{
	/// <summary>
	/// Auxillary slot which adds an UMAExpressionPlayer to a newly created character.
	/// </summary>
	public class ExpressionSlotScript : MonoBehaviour 
	{
		public void OnCharacterBegun(UMAData umaData)
		{
			var expressionPlayer = umaData.GetComponent<UMAExpressionPlayer>();
			if (expressionPlayer != null)
			{
				expressionPlayer.SlotUpdateVsCharacterUpdate++;
			}
		}

		public void OnDnaApplied(UMAData umaData)
		{
			var expressionSet = umaData.umaRecipe.raceData.expressionSet;
			if (expressionSet == null)
			{
				Debug.LogError("Couldn't add Expressions to Race: " + umaData.umaRecipe.raceData.raceName, umaData.gameObject);
				return;
			}
			var expressionPlayer = umaData.GetComponent<UMAExpressionPlayer>();
			if (expressionPlayer == null)
			{
				expressionPlayer = umaData.gameObject.AddComponent<UMAExpressionPlayer>();
				expressionPlayer.SlotUpdateVsCharacterUpdate++;
				umaData.CharacterUpdated.AddListener(new UnityAction<UMAData>(umaData_OnCharacterUpdated));
			}
			expressionPlayer.expressionSet = expressionSet;
			expressionPlayer.umaData = umaData;
			foreach (var hash in expressionSet.GetAnimatedBoneHashes())
			{
				umaData.skeleton.SetAnimatedBoneHierachy(hash);
			}
		}

		void umaData_OnCharacterUpdated(UMAData umaData)
		{
			var expressionPlayer = umaData.GetComponent<UMAExpressionPlayer>();
			if (expressionPlayer.SlotUpdateVsCharacterUpdate-- == 0)
			{
				UMAUtils.DestroySceneObject(expressionPlayer);
				umaData.CharacterUpdated.RemoveListener(new UnityAction<UMAData>(umaData_OnCharacterUpdated));
				return;
			}
		}
	}
}