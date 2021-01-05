using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace UMA.Editors 
{
    [CustomEditor(typeof(UMAGeneratorGLIB))]
    public class UMAGeneratorGlibEditor : UMAGeneratorBuiltinEditor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
        }
    }
}

