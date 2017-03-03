#if UNITY_EDITOR

using UnityEngine;
using System.Collections;
using UnityEditor;
using UMA;
using System.IO;

public static class ProjectUtility
{
    /*
    [MenuItem("Assets/UMA/Convert to PNG")]
    {
        Find everything that uses this.
        Save to new texture (PNG).
        Refresh asset database.
        iterate through found items
        if they are overlays, update those to use the new texture.  
    }
    */
    public static void SetTextureImporterFormat(Texture2D texture, bool isReadable)
    {
        if (null == texture) return;

        string assetPath = AssetDatabase.GetAssetPath(texture);
        var tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (tImporter != null)
        {
            tImporter.textureFormat = TextureImporterFormat.ARGB32;
            tImporter.textureType = TextureImporterType.Image;
            tImporter.isReadable = isReadable;
            //tImporter.normalmap = isNormalMap;
            tImporter.convertToNormalmap = false;

            AssetDatabase.ImportAsset(assetPath,ImportAssetOptions.ForceUpdate);
        }
    }

    public static bool WritePNG(Texture2D tex, string fileName)
    {
        string NewFileName = fileName;
        SetTextureImporterFormat(tex, true);

        // Encode texture into PNG
        byte[] bytes = tex.EncodeToPNG();


        if (fileName.ToLower().StartsWith("assets"))
        {
            fileName = fileName.Substring(7);
        }
        // For testing purposes, also write to a file in the project folder
        string DestFile = Path.Combine(Application.dataPath, fileName);
        File.WriteAllBytes(DestFile, bytes);

        AssetDatabase.ImportAsset(NewFileName, ImportAssetOptions.ForceUpdate);
        return true;
    }

    /// <summary>
    /// Update an OverlayDataAsset with a new texture
    /// </summary>
    /// <param name="Overlays"></param>
    /// <param name="OldInstanceID"></param>
    /// <param name="NewTex"></param>
    public static void UpdateOverlays(string[] Overlays, int OldInstanceID, Texture2D NewTex)
    {
        foreach (string s in Overlays)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(s);
            OverlayDataAsset oda = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(assetPath);

            for (int i = 0; i < oda.textureList.Length; i++)
            {
                Texture t = oda.textureList[i];
                if (t != null && t.GetInstanceID() == OldInstanceID)
                {
                    
                    oda.textureList[i] = NewTex;
                }
            }
        }
    }

    [MenuItem("Assets/UMA/Convert Overlay Textures to PNG")]
    public static void ConvertTexturesItem()
    {
        string[] Overlays = AssetDatabase.FindAssets("t:OverlayDataAsset");

        foreach(int OldInstanceID in UnityEditor.Selection.instanceIDs)
        {
            Object o = EditorUtility.InstanceIDToObject(OldInstanceID);
            if (o is Texture2D)
            {

                // get the importer for the texture.
                string AssetPath = AssetDatabase.GetAssetPath(OldInstanceID);
                

                // Create new PNG texture
                string NewPath = Path.ChangeExtension(AssetPath, "png");
                Texture2D tex = o as Texture2D;
                WritePNG(tex, NewPath);

                // Make sure the AssetDatabase can see it. Do we need to do this?
               // AssetDatabase.SaveAssets();
               // AssetDatabase.Refresh();

                // Load the new texure
                Texture2D newtex = AssetDatabase.LoadAssetAtPath<Texture2D>(NewPath);
                int NewInstanceID = newtex.GetInstanceID();
                // And update all the Overlays that use it
                UpdateOverlays(Overlays, OldInstanceID, newtex);
                // Get rid of old texture.
                AssetDatabase.DeleteAsset(AssetPath);
            }
        }

        // convert to PNG
        // get texture options
        //int OldInstanceID = UnityEditor.Selection.activeInstanceID;
  


        //if (!(UnityEditor.Selection.activeObject is Texture2D)) return;

        //string NewPath = Path.ChangeExtension(AssetDatabase.GetAssetPath(OldInstanceID), "png");
        //Texture2D tex = Selection.activeObject as Texture2D;

        //WritePNG(tex, NewPath);
        //AssetDatabase.SaveAssets();
        //AssetDatabase.Refresh();

        //Texture2D newtex = AssetDatabase.LoadAssetAtPath<Texture2D>(NewPath);
       // int NewInstanceID = newtex.GetInstanceID();


        // Replace in all overlays


        //UpdateOverlays(Overlays, OldInstanceID, newtex);

        // make sure we're fresh 
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}

#endif
