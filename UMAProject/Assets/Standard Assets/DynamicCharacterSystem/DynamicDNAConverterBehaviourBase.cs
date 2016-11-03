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
                }
                if (currentDNA[i].GetType().ToString() == "UMA.UMADnaTutorial")
                {
                    thisUMADnaTutorial = i;
                }
            }
            if (thisUMADnaHumanoid != -1)
            {
                needsHumanoidDnaUpdate = true;
                needsTutorialDnaUpdate = true;
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
                int dnaImported = 0;
                int dnaToImport = 0;
                if (thisUMADnaHumanoid > -1)
                    dnaToImport += currentDNA[thisUMADnaHumanoid].Values.Length;
                if (thisUMADnaTutorial > -1)
                    dnaToImport += currentDNA[thisUMADnaTutorial].Values.Length;
                for (int i = 0; i < currentDNA.Length; i++)
                {
                    if (currentDNA[i].GetType().ToString().IndexOf("DynamicUMADna") > -1)
                    {
                        //keep trying to find a new home for dnavalues until they have all been set
                        if (thisUMADnaHumanoid > -1)
                            dnaImported += ((DynamicUMADnaBase)_recipe.GetAllDna()[i]).ImportUMADnaValues(currentDNA[thisUMADnaHumanoid]);
                        if (thisUMADnaTutorial > -1)
                            dnaImported += ((DynamicUMADnaBase)_recipe.GetAllDna()[i]).ImportUMADnaValues(currentDNA[thisUMADnaTutorial]);
                        if (dnaImported >= dnaToImport)
                            break;
                    }
                }
                if (dnaImported > 0)//we say greater than 0 because we want to get rid of Humanoid even if all the values did not cross over
                {
                    Debug.Log("UMADnaHumanoid/Tutorial imported successfully");
                    //remove the UMADnaHumanoid/Tutorial from the recipe
                    if (thisUMADnaHumanoid > -1)
                        _recipe.RemoveDna(UMAUtils.StringToHash("UMADnaHumanoid"));
                    if (thisUMADnaTutorial > -1)
                        _recipe.RemoveDna(UMAUtils.StringToHash("UMADnaTutorial"));
                }
                else
                {
                    Debug.Log("UMADnaHumanoid/Tutorial Import Failed.");
                }
            }

        }
    }
}
