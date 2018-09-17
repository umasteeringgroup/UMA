using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UMA;
using UMA.CharacterSystem;
using System.Collections.Generic;

[Serializable]
public class UmaDnaBehaviour : PlayableBehaviour
{
    [Serializable]
    public struct DnaTuple
    {
        public string Name;
        [Range(0f,1f)]
        public float Value;
    }

    public bool rebuildImmediately = true;
    public List<DnaTuple> dnaValues = new List<DnaTuple>();
}
