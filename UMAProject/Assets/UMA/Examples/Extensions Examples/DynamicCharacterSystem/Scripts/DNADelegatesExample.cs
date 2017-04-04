using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UMA;

namespace UMA.CharacterSystem.Examples
{
	public class DNADelegatesExample : MonoBehaviour
	{

		public DynamicCharacterAvatar targetAvatar;
		[Tooltip("Optional. Specify a DynamicDNAConverter to target. If none set the script will target all converters. It will only affect the ones that operate on the dna names you specify in the code below.")]
		public DynamicDNAConverterBehaviour targetDNAConverter;
		public DNAPanel delegateDNAEditor;

		RaceData lastRace;
		Color startingSkinColor = Color.black;

		void Awake()
		{
			if (targetAvatar)
			{
				targetAvatar.CharacterCreated.AddListener(SetUpDNADelegates);
				targetAvatar.CharacterUpdated.AddListener(CheckRaceChange);
			}
		}
		// Use this for initialization
		void Start()
		{

		}

		public void CheckRaceChange(UMAData umaData)
		{
			if (umaData.umaRecipe.raceData)
			{
				if (umaData.umaRecipe.raceData != lastRace)
					SetUpDNADelegates(umaData);
			}
		}

		public void SetUpDNADelegates(UMAData umaData)
		{
			targetAvatar.RecipeUpdated.RemoveListener(SetUpDNADelegates);
			if (umaData.umaRecipe.raceData)
			{
				lastRace = umaData.umaRecipe.raceData;
                foreach (DnaConverterBehaviour dcb in umaData.umaRecipe.raceData.dnaConverterList)
				{
					if(dcb.GetType() == typeof(DynamicDNAConverterBehaviour))
					{
						(dcb as DynamicDNAConverterBehaviour).AddDnaCallbackDelegate(ChangeCharacterRedness, "skinRedness");
						(dcb as DynamicDNAConverterBehaviour).AddDnaCallbackDelegate(ChangeCharacterGreenness, "skinGreenness");
						(dcb as DynamicDNAConverterBehaviour).AddDnaCallbackDelegate(ChangeCharacterBlueness, "skinBlueness");
					}
				}
				if (delegateDNAEditor)
				{
					delegateDNAEditor.Initialize(targetAvatar);
				}
				startingSkinColor = new Color(targetAvatar.GetColor("Skin").color.r, targetAvatar.GetColor("Skin").color.g, targetAvatar.GetColor("Skin").color.b);
			}
		}
		// the following methods recieve the raw dna value from the DynamicDNAConverterBehaviour (i.e. unchanged dna has a value of 0.5, with a maximum of 1 and a minimum of 0)
		public void ChangeCharacterRedness(string affectedDNA, float currentDNAVal)
		{
			var currenSkinColor = targetAvatar.GetColor("Skin");
			var newRed = Mathf.Clamp((float)(((currentDNAVal - 0.5) * 2) + startingSkinColor.r),0,1);
            var newSkinColor = new Color(newRed, currenSkinColor.color.g, currenSkinColor.color.b);
			targetAvatar.SetColor("Skin", newSkinColor, currenSkinColor.channelAdditiveMask[2], currenSkinColor.channelAdditiveMask[2].a , true);
        }
		public void ChangeCharacterGreenness(string affectedDNA, float currentDNAVal)
		{
			var currenSkinColor = targetAvatar.GetColor("Skin");
			var newGreen = Mathf.Clamp((float)(((currentDNAVal - 0.5) * 2) + startingSkinColor.g), 0, 1);
			var newSkinColor = new Color(currenSkinColor.color.r, newGreen, currenSkinColor.color.b);
			targetAvatar.SetColor("Skin", newSkinColor, currenSkinColor.channelAdditiveMask[2], currenSkinColor.channelAdditiveMask[2].a, true);
		}
		public void ChangeCharacterBlueness(string affectedDNA, float currentDNAVal)
		{
			var currenSkinColor = targetAvatar.GetColor("Skin");
			var newBlue = Mathf.Clamp((float)(((currentDNAVal - 0.5) * 2) + startingSkinColor.b), 0, 1);
			var newSkinColor = new Color(currenSkinColor.color.r, currenSkinColor.color.g, newBlue);
			targetAvatar.SetColor("Skin", newSkinColor, currenSkinColor.channelAdditiveMask[2], currenSkinColor.channelAdditiveMask[2].a, true);
		}
	}
}
