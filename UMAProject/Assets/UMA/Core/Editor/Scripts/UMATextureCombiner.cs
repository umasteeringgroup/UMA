using UnityEngine;
using UnityEditor;
using UMA.Editors;

namespace UMA
{
    // Modified From Jean Moreno's texture combiner
    // MIT License

    public class UMATextureCombiner : EditorWindow
    {
        public static ComputeShader textureComputeShader;
        public static int kernel = 0;


        [MenuItem("UMA/Texture Channel Combiner...")]
        static void Open()
        {
            // Get the compute shader
            textureComputeShader = Resources.Load<ComputeShader>("Shader/Combiner");
            if (textureComputeShader == null)
            {
                EditorUtility.DisplayDialog("Error!","Compute shader 'Combiner' not found","OK");
                return;
            }
            kernel = textureComputeShader.FindKernel("Combiner");

            var w = GetWindow<UMATextureCombiner>(true, "UMA Texture Combiner");
            w.minSize = new Vector2(800, 420);
            w.maxSize = new Vector2(800, 420);
        }

        public enum Channel { R, G, B, A, Luma, Average, Value }

        Texture2D textureR;
        Texture2D textureG;
        Texture2D textureB;
        Texture2D textureA;
        
        Channel sourceR, sourceG, sourceB, sourceA;

        RenderTexture textureCombined;
        int textureSize = 4;
        string[] textureSizes = new string[] { "64","128", "256", "512", "1024", "2048", "4096", "8192", "Custom" };
        int textureWidth = 1024;
        int textureHeight = 1024;
        Texture2D textureSaved;

        float rColor = 0.0f;
        float gColor = 0.0f;
        float bColor = 0.0f;
        float aColor = 0.0f;

        bool invertR = false;
        bool invertG = false;
        bool invertB = false;
        bool invertA = false;

        float[] rUV = new float[2];
        float[] gUV = new float[2];
        float[] bUV = new float[2];
        float[] aUV = new float[2];

        int viewAlpha = 0;


        private void OnEnable()
        {
            if (textureComputeShader == null)
            {
                textureComputeShader = Resources.Load<ComputeShader>("Shader/Combiner");
                kernel = textureComputeShader.FindKernel("Combiner");
            }
            if (textureR == null)
            {
                textureR = Texture2D.whiteTexture;
            }

            if (textureG == null)
            {
                textureG = Texture2D.whiteTexture;
            }

            if (textureB == null)
            {
                textureB = Texture2D.whiteTexture;
            }

            if (textureA == null)
            {
                textureA = Texture2D.whiteTexture;
            }

            UpdateRenderTextures(false);
        }

        void DrawBox(Rect r)
        {
            Color b = GUI.color;
            if (EditorGUIUtility.isProSkin)
            {
                GUI.color = new Color(1.3f, 1.4f, 1.5f);
            }
            else
            {
                GUI.color = new Color(0.75f, 0.875f, 1f);
            }

            GUIStyle theStyle = EditorStyles.textField;

            GUI.Box(r,"",theStyle);

            GUI.color = b;
        }

void OnGUI()
        {
            // TODO: Add a preset function
            GUILayout.Space(8f);

            float space = 12f;
            float rowHeight = 64f;

            var r = EditorGUILayout.GetControlRect(false, (64f + space) * 4f);
            var lblRect = r;
            lblRect.width = 20f;

            var chanRect = r;
            chanRect.width = 320f;

            var texRect = r;
            texRect.width = 64f;

            Rect outline = r;
            outline.y += 28;
            outline.width = lblRect.width + chanRect.width + texRect.width + 32f;
            outline.height = 72f;



            for (int i = 0;  i < 4; i++)
            {
                DrawBox(outline);
                outline.y += rowHeight+space;
            }

            texRect.height = 32;
            texRect.width = 128;
            EditorGUI.LabelField(texRect,"Input Texture");
            texRect.y += 32;
            texRect.x += 24f;
            texRect.height = 64f;
            texRect.width = 64f;
            textureR = (Texture2D)EditorGUI.ObjectField(texRect, textureR, typeof(Texture2D), false);
            texRect.y += 64f + space;
            textureG = (Texture2D)EditorGUI.ObjectField(texRect, textureG, typeof(Texture2D), false);
            texRect.y += 64f + space;
            textureB = (Texture2D)EditorGUI.ObjectField(texRect, textureB, typeof(Texture2D), false);
            texRect.y += 64f + space;
            textureA = (Texture2D)EditorGUI.ObjectField(texRect, textureA, typeof(Texture2D), false);

            lblRect.x += 4f;
            lblRect.y += 22f + 32f;
            GUI.Label(lblRect, "R", EditorStyles.largeLabel);
            lblRect.y += 64f + space;
            GUI.Label(lblRect, "G", EditorStyles.largeLabel);
            lblRect.y += 64f + space;
            GUI.Label(lblRect, "B", EditorStyles.largeLabel);
            lblRect.y += 64f + space;
            GUI.Label(lblRect, "A", EditorStyles.largeLabel);

            chanRect.x += texRect.x + texRect.width + space;

            chanRect.height = 32;
            EditorGUI.LabelField(chanRect,"Channel Source");

            chanRect.height = 64f;
            chanRect.y += 32;
            sourceR = (Channel)GUI_SourceChannel(chanRect, sourceR, ref rColor, 0);
            chanRect.y += 64f + space;
            sourceG = (Channel)GUI_SourceChannel(chanRect, sourceG, ref gColor, 1);
            chanRect.y += 64f + space;
            sourceB = (Channel)GUI_SourceChannel(chanRect, sourceB, ref bColor, 2);
            chanRect.y += 64f + space;
            sourceA = (Channel)GUI_SourceChannel(chanRect, sourceA, ref aColor, 3);

            var resultRect = r;
            resultRect.x += lblRect.x + lblRect.width + texRect.width + chanRect.width + 64f;
            resultRect.height = 32;
            EditorGUI.LabelField(resultRect, "Output Texture");

            resultRect.height = (64f + space) * 4f;
            resultRect.y += 32;
            resultRect.width = resultRect.height;


            if (textureCombined != null)
            {
                var alphaRect = resultRect;
                alphaRect.width = resultRect.width / 5.0f;
                alphaRect.height += 24.0f;

                if (GUI.Toggle(alphaRect, viewAlpha == 0, "RGB", EditorStyles.miniButton))
                {
                    viewAlpha = 0;
                }

                alphaRect.x += alphaRect.width;
                if (GUI.Toggle(alphaRect, viewAlpha == 2,"Red", EditorStyles.miniButton))
                {
                    viewAlpha = 2;
                }

                alphaRect.x += alphaRect.width;
                if (GUI.Toggle(alphaRect, viewAlpha == 3, "Green", EditorStyles.miniButton))
                {
                    viewAlpha = 3;
                }

                alphaRect.x += alphaRect.width;
                if (GUI.Toggle(alphaRect, viewAlpha == 4, "Blue", EditorStyles.miniButton))
                {
                    viewAlpha = 4;
                }

                alphaRect.x += alphaRect.width;
                if (GUI.Toggle(alphaRect, viewAlpha == 1, "Alpha", EditorStyles.miniButton))
                {
                    viewAlpha = 1;
                }

                resultRect.y += 24.0f;

                GUI.Box(resultRect, GUIContent.none);
                GUI.DrawTexture(resultRect, textureCombined, ScaleMode.StretchToFill, false, 0);
            }
            else
            {
                resultRect.width += resultRect.width + space;
                EditorGUI.HelpBox(resultRect, "texture not generated yet", MessageType.Warning);
            }

            GUILayout.Space(32);
            GUILayout.Label("Output Texture Size");
            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();

            //Texture size
            EditorGUI.BeginChangeCheck();
            textureSize = EditorGUILayout.Popup(textureSize, textureSizes, GUILayout.Width(60f));
            using (new EditorGUI.DisabledScope(textureSize != textureSizes.Length - 1))
            {
                textureWidth = EditorGUILayout.IntField(textureWidth, GUILayout.Width(60f));
            }

            GUILayout.Label("x");
            using (new EditorGUI.DisabledScope(textureSize != textureSizes.Length - 1))
            {
                textureHeight = EditorGUILayout.IntField(textureHeight, GUILayout.Width(60f));
            }

            if (EditorGUI.EndChangeCheck())
            {
                textureWidth = Mathf.Clamp(textureWidth, 1, 16384);
                textureHeight = Mathf.Clamp(textureHeight, 1, 16384);
                TextureSizeUpdated();
            }

            GUILayout.FlexibleSpace();

            //Save button
            if (GUILayout.Button("SAVE AS...", GUILayout.Width(120f)))
            {
                SaveAs();
            }
            GUILayout.Space(18);

            GUILayout.EndHorizontal();

            //Options
            GUILayout.BeginHorizontal();
 
            GUILayout.FlexibleSpace();

            //Reset button
            if (GUILayout.Button("RESET", GUILayout.Width(120f)))
            {
                Reset();
            }
            GUILayout.Space(18);
            GUILayout.EndHorizontal();

            if (GUI.changed && textureComputeShader != null)
            {
                RefreshCombinedTexture(true);
            }
        }

        void TextureSizeUpdated()
        {
            if (textureSize != textureSizes.Length - 1)
            {
                textureWidth = int.Parse(textureSizes[textureSize]);
                textureHeight = textureWidth;
            }
            UpdateRenderTextures(true);
        }

        void Reset()
        {
            OnDestroy();
            sourceR = Channel.R;
            sourceG = Channel.R;
            sourceB = Channel.R;
            sourceA = Channel.R;
            textureR = Texture2D.whiteTexture;
            textureG = Texture2D.whiteTexture;
            textureB = Texture2D.whiteTexture;
            textureA = Texture2D.whiteTexture;
            textureSaved = null;
            rColor = 0.0f;
            gColor = 0.0f;
            bColor = 0.0f;
            aColor = 0.0f;
            invertR = false;
            invertG = false;
            invertB = false;
            invertA = false;

        }

        void UpdateRenderTextures(bool delete)
        {
            if (delete && textureCombined != null)
            {
                ClearRenderTexture(textureCombined);
            }

            if (textureCombined == null)
            {
                textureCombined = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                textureCombined.enableRandomWrite = true;
                textureCombined.Create();
                textureCombined.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        void RefreshCombinedTexture(bool preview)
        {
            UpdateRenderTextures(false);

            textureComputeShader.SetTexture(kernel, "InputR", textureR);
            textureComputeShader.SetTexture(kernel, "InputG", textureG);
            textureComputeShader.SetTexture(kernel, "InputB", textureB);
            textureComputeShader.SetTexture(kernel, "InputA", textureA);

            textureComputeShader.SetInt("rSource", (int)sourceR);
            textureComputeShader.SetInt("gSource", (int)sourceG);
            textureComputeShader.SetInt("bSource", (int)sourceB);
            textureComputeShader.SetInt("aSource", (int)sourceA);

            textureComputeShader.SetInt("invertR", invertR ? 1 : 0);
            textureComputeShader.SetInt("invertG", invertG ? 1 : 0);
            textureComputeShader.SetInt("invertB", invertB ? 1 : 0);
            textureComputeShader.SetInt("invertA", invertA ? 1 : 0);

            textureComputeShader.SetFloat("ColorR", rColor);
            textureComputeShader.SetFloat("ColorG", gColor);
            textureComputeShader.SetFloat("ColorB", bColor);
            textureComputeShader.SetFloat("ColorA", aColor);

            textureComputeShader.SetInt("alphaOnly", viewAlpha);

            textureComputeShader.SetTexture(kernel, "Result", textureCombined);

            textureComputeShader.Dispatch(kernel, textureCombined.width, textureCombined.height, 1);
            RenderTexture.active = textureCombined;
        }

        void OnDestroy()
        {
            if (textureCombined != null)
            {
                ClearRenderTexture(textureCombined);
            }
        }

        void ClearRenderTexture(RenderTexture rt)
        {
            rt.Release();
            DestroyImmediate(rt);
        }

        int GUI_SourceChannel(Rect position, Channel channel, ref float ChannelColor, int outputChannel)
        {
            // I know this looks a bit goofy, but I needed to pack the
            // controls in two columns to make the new ones fit.
            var names = System.Enum.GetNames(typeof(Channel));
            var r = position;
            r.height /= 4;
            r.width /= 2;

            Rect r2 = new Rect(r);
            r2.x = r.x + r.width;

            int i = 0;
            if (GUI.Toggle(r, (int)channel == i, names[i], EditorStyles.miniButton))
            {
                channel = (Channel)i;
            }

            i = 1;
            if (GUI.Toggle(r2, (int)channel == i, names[i], EditorStyles.miniButton))
            {
                channel = (Channel)i;
            }

            r.y += r.height;
            r2.y += r.height;

            i = 2;
            if (GUI.Toggle(r, (int)channel == i, names[i], EditorStyles.miniButton))
            {
                channel = (Channel)i;
            }

            i = 3;
            if (GUI.Toggle(r2, (int)channel == i, names[i], EditorStyles.miniButton))
            {
                channel = (Channel)i;
            }

            r.y += r.height;
            r2.y += r.height;

            i = 4;
            if (GUI.Toggle(r, (int)channel == i, names[i], EditorStyles.miniButton))
            {
                channel = (Channel)i;
            }

            i = 5;
            if (GUI.Toggle(r2, (int)channel == i, names[i], EditorStyles.miniButton))
            {
                channel = (Channel)i;
            }

            r.y += r.height;
            r2.y += r.height;

            i = 6;
            if (GUI.Toggle(r, (int)channel == i, names[i], EditorStyles.miniButton))
            {
                channel = (Channel)i;
            }



            if ((int)channel == i)
            {
                ChannelColor = EditorGUI.Slider(r2, ChannelColor, 0.0f, 1.0f);
                if (outputChannel == 0)
                {
                    invertR = false;
                }

                if (outputChannel == 1)
                {
                    invertG = false;
                }

                if (outputChannel == 2)
                {
                    invertB = false;
                }

                if (outputChannel == 3)
                {
                    invertA = false;
                }
            }
            else
            {
                if (outputChannel == 0)
                {
                    invertR = GUI.Toggle(r2, invertR, "Invert");
                }

                if (outputChannel == 1)
                {
                    invertG = GUI.Toggle(r2, invertG, "Invert");
                }

                if (outputChannel == 2)
                {
                    invertB = GUI.Toggle(r2, invertB, "Invert");
                }

                if (outputChannel == 3)
                {
                    invertA = GUI.Toggle(r2, invertA, "Invert");
                }
            }

            return (int)channel;
        }

        void SaveAs()
        {
            var path = EditorUtility.SaveFilePanelInProject("Save combined texture", "CombinedTexture",  "png" , "Save combined texture as...");
            if (!string.IsNullOrEmpty(path))
            {
                UMAAvatarLoadSaveMenuItems.SaveRenderTexture(textureCombined, path, false);
            }
        }

        bool Load(Texture2D texture)
        {
            if (texture == null)
            {
                return true;
            }

            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture));
            if (importer != null)
            {
                if (importer.userData.StartsWith("texture_combiner"))
                {
                    //no error check here!
                    //may break with different userData
                    var userDataSplit = importer.userData.Split(' ');
                    var rGuid = userDataSplit[1].Split(':')[1];
                    var gGuid = userDataSplit[2].Split(':')[1];
                    var bGuid = userDataSplit[3].Split(':')[1];
                    var aGuid = userDataSplit[4].Split(':')[1];

                    textureR = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(rGuid));
                    textureG = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(gGuid));
                    textureB = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(bGuid));
                    textureA = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(aGuid));

                    string errorGUID = "";
                    if (!string.IsNullOrEmpty(rGuid) && textureR == null)
                    {
                        errorGUID += "Red  ";
                    }
                    if (!string.IsNullOrEmpty(gGuid) && textureG == null)
                    {
                        errorGUID += "Green  ";
                    }
                    if (!string.IsNullOrEmpty(bGuid) && textureB == null)
                    {
                        errorGUID += "Blue  ";
                    }
                    if (!string.IsNullOrEmpty(aGuid) && textureA == null)
                    {
                        errorGUID += "Alpha";
                    }

                    sourceR = (Channel)System.Enum.Parse(typeof(Channel), userDataSplit[5].Split(':')[1]);
                    sourceG = (Channel)System.Enum.Parse(typeof(Channel), userDataSplit[6].Split(':')[1]);
                    sourceB = (Channel)System.Enum.Parse(typeof(Channel), userDataSplit[7].Split(':')[1]);
                    sourceA = (Channel)System.Enum.Parse(typeof(Channel), userDataSplit[8].Split(':')[1]);

                    textureSaved = texture;
                    if (textureCombined != null)
                    {
                        textureCombined.Release();
                        DestroyImmediate(textureCombined);
                    }

                    if (!string.IsNullOrEmpty(errorGUID))
                    {
                        EditorUtility.DisplayDialog("Error", "Source texture(s) couldn't be found in the project:\n\n" + errorGUID + "\n\nMaybe they have been deleted, or they GUID has been updated?", "Ok");
                    }

                    return true;
                }
                else
                {
                    ShowNotification(new GUIContent("This texture doesn't seem to have been generated with the Texture Combiner"));
                }
            }

            return false;
        }
    }
}
