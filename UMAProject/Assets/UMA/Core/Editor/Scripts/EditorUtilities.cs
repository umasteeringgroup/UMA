using System.Collections;
using System.Collections.Generic;
using UMA;
using UnityEditor;
using UnityEngine;

public class EditorUtilities
{
    // Start is called before the first frame update

    [MenuItem("UMA/SRP/Convert to URP (LWRP)")]
    static void ConvertToURP()
    {
        if (EditorUtility.DisplayDialog("Convert?","Convert UMA Materials from Standard to URP. You should run the Unity option to convert your project to URP/LWRP in addition to running this option. Continue?","OK","Cancel"))
        {
            if (ConvertUMAMaterials("_MainTex", "_BaseMap"))
            {
                EditorUtility.DisplayDialog("Convert", 
                    "UMAMaterials converted. You will need to run the unity URP (LWRP) conversion utility to convert your materials if you have not already done this.", "OK");
            } 
            else
            {
                EditorUtility.DisplayDialog("Convert", "No UMAMaterials needed to be converted.", "OK");
            }

        }
    }

    [MenuItem("UMA/SRP/Convert to Standard from URP (LWRP)")]
    static void ConvertToStandard()
    {
        if (EditorUtility.DisplayDialog("Convert?", "Convert UMAMaterials to Standard from URP. You will need to manually fix the template materials. Continue?", "OK", "Cancel"))
        {
            if (ConvertUMAMaterials("_BaseMap", "_MainTex"))
            {
                EditorUtility.DisplayDialog("Convert", "UMAMaterials converted. You will need to manually fix the template materials by changing them to use the correct shaders if you modified them.", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Convert", "No UMAMaterials needed to be converted.", "OK");
            }
        }
    }

    /// <summary>
    /// Convertes all UMAMaterial channel Material Property names if they match.
    /// </summary>
    /// <param name="From"></param>
    /// <param name="To"></param>
    /// <returns></returns>
    static bool ConvertUMAMaterials(string From, string To)
    {
        string[] guids = AssetDatabase.FindAssets("t:UMAMaterial");

        int dirtycount = 0;
        foreach (string guid in guids)
        {
            bool matModified = false;
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UMAMaterial umat = AssetDatabase.LoadAssetAtPath<UMAMaterial>(path);
            for(int i=0;i < umat.channels.Length;i++)
            {
                if (umat.channels[i].materialPropertyName == From)
                {
                    umat.channels[i].materialPropertyName = To;
                    matModified = true;
                }
            }
            if (matModified)
            {
                dirtycount++;
                EditorUtility.SetDirty(umat);
            }
        }
        if (dirtycount > 0)
        {
            AssetDatabase.SaveAssets();
            return true;
        }
        return false;
    }
}
