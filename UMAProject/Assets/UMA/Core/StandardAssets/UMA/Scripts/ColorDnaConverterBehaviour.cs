using UnityEngine;
using System.Collections;

namespace UMA
{
    public class ColorDnaConverterBehaviour : DynamicDNAConverterBehaviourBase
    {
        public ColorDnaAsset colorSet;

        public ColorDnaConverterBehaviour()
        {
            ApplyDnaAction = ApplyDNA;
            DNAType = typeof(DynamicUMADna);
        }

        public override void Prepare()
        {
        }

        public override int DNATypeHash
        {
            get {
                if (colorSet != null)
                    return colorSet.dnaTypeHash;

                return dnaTypeHash;
            }
        }

        public void ApplyDNA(UMAData data, UMASkeleton skeleton)
        {
            if (colorSet == null)
            {
                Debug.LogError("Missing color set asset for: " + this.name);
                return;
            }

            UMADnaBase activeDNA = data.GetDna(this.dnaTypeHash);
            if (activeDNA == null)
            {
                Debug.LogError("Could not get DNA values for: "+ this.name);
                return;
            }

            float[] dnaValues = activeDNA.Values; 
            string[] dnaNames = activeDNA.Names;

            if (activeDNA.Count == colorSet.colorSets.Length)
            {
                for (int i = 0; i < dnaValues.Length; i++)
                {
                    bool found = false;

                    foreach (SlotData slot in data.umaRecipe.slotDataList)
                    {
                        OverlayData overlay = slot.GetOverlay(colorSet.colorSets[i].overlayEntryName);
                        if (overlay != null)
                        {
                            found = true;
                            Color c = Color.Lerp(colorSet.colorSets[i].minColor, colorSet.colorSets[i].maxColor, dnaValues[i]);
                            overlay.SetColor(colorSet.colorSets[i].colorChannel, c);
                        }
                    }

                    if (!found)
                        Debug.LogWarning("Overlay not found!");
                }
            }
            else
            {
                //TODO
                Debug.LogWarning("ColorDNA: activeDNA count and colorSets length do not match!");
            }

            //Should this be here or require the use to set it after setting DNA?
            data.isTextureDirty = true;
            data.isAtlasDirty = true;
        }
    }
}
