using System.Collections;
using System.Collections.Generic;
using UMA;

#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class LoadTester : MonoBehaviour
{
    public string LoadFile;
    public MeshModifier meshModifier;
    // Start is called before the first frame update

    private void OnGUI()
    {
#if UNITY_EDITOR
        if (GUI.Button(new Rect(10, 10, 100, 50), "Load"))
        {
            object[] objs = AssetDatabase.LoadAllAssetsAtPath(LoadFile);
        }
#endif
    }
}
