using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class UMAAnimationParameterSetter : MonoBehaviour
{
    [Serializable]
    public class AnimatorParam
    {
        public string name;
        public AnimatorControllerParameterType type;
        public float floatValue;
        public int intValue;
        public bool boolValue;
    }

    // Discovered clip names (read-only display in editor)
    [SerializeField] public List<string> controllerClips = new List<string>();

    // Editable parameter values for the attached Animator's controller
    [SerializeField] public List<AnimatorParam> parameters = new List<AnimatorParam>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
