using UnityEngine;
using System.Collections.Generic;

namespace UMA.PoseTools
{
    /// <summary>
    /// UMA expression set. Groups poses for expression player channels.
    /// </summary>
    [System.Serializable]
    public abstract class UMADynamicExpression : ScriptableObject
    {
        public string poseName;
        [Range(0.0f, 1.0f)]
        public float value = 0.0f;
        [Range(0.0f, 1.0f)]
        public float defaultValue = 0.5f;

        public abstract void Initialize(UMAData umadata);
        public abstract void PreProcess(UMAData umadata);
        public abstract void Process(UMAData umadata);
    }
}
