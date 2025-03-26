using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System;

namespace UMA
{

    public class ConvertToSubstance : EditorWindow
    {
        private MonoScript targetMonoScript;
        private Vector2 scrollPos;
        Texture2D texture;

        [MenuItem("UMA/Tools/Convert ZB Alpha to Substance Alpha")]
        public static void ShowWindow()
        {
            GetWindow<ConvertToSubstance>(true, "Convert Grayscale to Subtance Alpha", true);
        }

        void OnGUI()
        {
            texture = (Texture2D)EditorGUILayout.ObjectField("Texture (2D)", texture, typeof(Texture2D), false);
            if (GUILayout.Button("Convert"))
            {
                if (texture == null)
                {
                    EditorUtility.DisplayDialog("Error", "Please select a texture", "OK");
                    return;
                }
                string path = EditorUtility.SaveFilePanel("Save Texture", "", "file.png", "png");

                if (!string.IsNullOrEmpty(path))
                {
                    var newTex = ConvertTexture(texture);
                    if (newTex == null)
                    {
                        EditorUtility.DisplayDialog("Error", "Texture is not readable", "OK");
                        return;
                    }
                    byte[] bytes = newTex.EncodeToPNG();
                    System.IO.File.WriteAllBytes(path, bytes);
                    GameObject.DestroyImmediate(newTex);
                }
            }
        }

        private static Texture2D ConvertTexture(Texture TextureToConvert)
        {
            if (TextureToConvert != null)
            {
                Texture2D tex = (Texture2D)TextureToConvert;
                Color32[] pixels = tex.GetPixels32();
                byte alphabase = pixels[0].r;

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    float alpha = 1.0f;
                    float dist = Mathf.Abs(pixel.r - alphabase);
                    if (pixel.r > alphabase)
                    {

                        float upperRange = 255 - alphabase;
                        if (upperRange == 0)
                        {
                            alpha = 1;
                        }
                        else
                        {
                            alpha = dist / upperRange;
                        }
                    }
                    else
                    {
                        float lowerRange = alphabase;
                        if (lowerRange == 0)
                        {
                            alpha = 0;
                        }
                        else
                        {
                            alpha = dist / lowerRange;
                        }
                    }
                    pixels[i].a = (byte)(alpha * 255);
                }
                Texture2D newTex = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                newTex.SetPixels32(pixels);
                newTex.Apply();
            }
            return null;
        }
    }
}