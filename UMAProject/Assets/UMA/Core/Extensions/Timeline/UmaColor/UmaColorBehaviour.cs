#if UNITY_2017_1_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Playables;

namespace UMA.Timeline
{
    [Serializable]
    public class UmaColorBehaviour : PlayableBehaviour
    {
        public string sharedColorName = "";
        public Color color = Color.white;
    }
}
#endif