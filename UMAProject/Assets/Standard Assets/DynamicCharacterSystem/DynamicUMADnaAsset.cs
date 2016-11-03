using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
    [System.Serializable]
    public class DynamicUMADnaAsset : ScriptableObject
    {
        //falls back to UMADnaHumanoid
        public string[] Names = new string[]{
            "height",
            "headSize",
            "headWidth",
            "neckThickness",
            "armLength",
            "forearmLength",
            "armWidth",
            "forearmWidth",
            "handsSize",
            "feetSize",
            "legSeparation",
            "upperMuscle",
            "lowerMuscle",
            "upperWeight",
            "lowerWeight",
            "legsSize",
            "belly",
            "waist",
            "gluteusSize",
            "earsSize",
            "earsPosition",
            "earsRotation",
            "noseSize",
            "noseCurve",
            "noseWidth",
            "noseInclination",
            "nosePosition",
            "nosePronounced",
            "noseFlatten",
            "chinSize",
            "chinPronounced",
            "chinPosition",
            "mandibleSize",
            "jawsSize",
            "jawsPosition",
            "cheekSize",
            "cheekPosition",
            "lowCheekPronounced",
            "lowCheekPosition",
            "foreheadSize",
            "foreheadPosition",
            "lipsSize",
            "mouthSize",
            "eyeRotation",
            "eyeSize",
            "breastSize"
            };

        public DynamicUMADnaAsset() { }

    }
}
