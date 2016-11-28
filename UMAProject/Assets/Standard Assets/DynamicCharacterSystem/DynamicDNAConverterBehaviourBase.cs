using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
    /// <summary>
    /// Base class for DNA converters.
    /// </summary>
    public abstract class DynamicDNAConverterBehaviourBase : DnaConverterBehaviour
    {
        public DynamicUMADnaAsset dnaAsset;

        public override int GetDnaTypeHash()
        {
            return base.GetDnaTypeHash();
        }

        public static void FixUpUMADnaToDynamicUMADna(UMAData.UMARecipe _recipe)
        {
            Debug.Log("Converting UMADnaHumanoid/Tutorial to DynamicUMADna");
			//I think the reason this is not working right is that the Tutorial values are getting removed when the converter that takes the Humanoid values does its thing...
			 int thisUMADnaHumanoid = -1;
			 int thisUMADnaTutorial = -1;
			 bool needsHumanoidDnaUpdate = false;
			 bool needsTutorialDnaUpdate = false;
			 var currentDNA = _recipe.GetAllDna();
			 for (int i = 0; i < currentDNA.Length; i++)
			 {
				 if (currentDNA[i].GetType().ToString() == "UMA.UMADnaHumanoid")
				 {
					 thisUMADnaHumanoid = i;
					 needsHumanoidDnaUpdate = true;
				 }
				 if (currentDNA[i].GetType().ToString() == "UMA.UMADnaTutorial")
				 {
					 thisUMADnaTutorial = i;
					 needsTutorialDnaUpdate = true;
				 }
			 }
			 if (thisUMADnaHumanoid != -1 || thisUMADnaTutorial != -1)
			 {       
				 foreach (DnaConverterBehaviour DnaConverter in _recipe.raceData.dnaConverterList)
				 {
					 if (DnaConverter.DNAType.ToString() == "UMA.UMADnaHumanoid")
					 {
						 needsHumanoidDnaUpdate = false;
					 }
					 if (DnaConverter.DNAType.ToString() == "UMA.UMADnaTutorial")
					 {
						 needsTutorialDnaUpdate = false;
					}
				 }
			 }
			 if(needsHumanoidDnaUpdate || needsTutorialDnaUpdate)
			 {
				 //find each DynamicUMADna and try adding the UMADnaHumnoid values to it
				 int humanoidDnaImported = 0;
				 int humanoidDnaToImport = 0;
				 int tutorialDnaImported = 0;
				 int tutorialDnaToImport = 0;
				 if (thisUMADnaHumanoid > -1)
					 humanoidDnaToImport += currentDNA[thisUMADnaHumanoid].Values.Length;
				 if (thisUMADnaTutorial > -1)
					 tutorialDnaToImport += currentDNA[thisUMADnaTutorial].Values.Length;
				 if (humanoidDnaToImport > 0)
				 {
					 for (int i = 0; i < currentDNA.Length; i++)
					 {
						 if (currentDNA[i].GetType().ToString().IndexOf("DynamicUMADna") > -1)
						 {
							 //keep trying to find a new home for dnavalues until they have all been set
							 if (thisUMADnaHumanoid > -1)
								 humanoidDnaImported += ((DynamicUMADnaBase)_recipe.GetAllDna()[i]).ImportUMADnaValues(currentDNA[thisUMADnaHumanoid]);
							 if (humanoidDnaImported >= humanoidDnaToImport)
								 break;
						 }
					 }
				 }
				 if (tutorialDnaToImport > 0)
				 {
					 for (int i = 0; i < currentDNA.Length; i++)
					 {
						 if (currentDNA[i].GetType().ToString().IndexOf("DynamicUMADna") > -1)
						 {
							//keep trying to find a new home for dnavalues until they have all been set
							 if (thisUMADnaTutorial > -1)
								 tutorialDnaImported += ((DynamicUMADnaBase)_recipe.GetAllDna()[i]).ImportUMADnaValues(currentDNA[thisUMADnaTutorial]);
							 if (tutorialDnaImported >= tutorialDnaToImport)
								 break;
						 }
					 }
				 }
				if (thisUMADnaHumanoid > -1)
				{
					//remove the UMADnaHumanoid from the recipe
					if (humanoidDnaImported > 0)
					{//we say greater than 0 because we want to get rid of Humanoid even if all the values did not cross over
						Debug.Log("UMADnaHumanoid imported successfully");
						_recipe.RemoveDna(UMAUtils.StringToHash("UMADnaHumanoid"));
					}
					//else
						//Debug.Log("UMADnaHumanoid Import Failed.");
				}
				//For some bloody reason bloody Tutorial WILL NOT work!
				if (thisUMADnaTutorial > -1)
				 {
					 //remove the UMATutorial from the recipe
					 if (tutorialDnaImported > 0)//we say greater than 0 because we want to get rid of Tutorial even if all the values did not cross over
					 {
						 _recipe.RemoveDna(UMAUtils.StringToHash("UMADnaTutorial"));
						 Debug.Log("UMATutorial imported successfully");
					 }
					 //else
						 //Debug.Log("UMATutorial Import Failed.");
				 }
			 }
		}
    }
}
