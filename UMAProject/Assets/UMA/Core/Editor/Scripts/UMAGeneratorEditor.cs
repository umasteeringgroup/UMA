using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors 
{
    [CustomEditor(typeof(UMAGenerator))]
    public class UMAGeneratorEditor : UMAGeneratorBuiltinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
        }
    }
}

