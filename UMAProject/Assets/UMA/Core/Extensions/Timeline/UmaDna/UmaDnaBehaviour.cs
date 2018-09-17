#if UNITY_2017_1_OR_NEWER
using System;
using UnityEngine;
using UnityEngine.Playables;
using System.Collections.Generic;

namespace UMA.Timeline
{
    [Serializable]
    public class UmaDnaBehaviour : PlayableBehaviour
    {
        [Serializable]
        public struct DnaTuple
        {
            public string Name;
            [Range(0f, 1f)]
            public float Value;
        }

        public bool rebuildImmediately = true;
        public List<DnaTuple> dnaValues = new List<DnaTuple>();
    }
}
#endif