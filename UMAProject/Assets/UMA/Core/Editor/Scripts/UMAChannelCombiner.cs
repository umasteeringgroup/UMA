using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    public class UMAChannelCombiner
    {
        public ComputeShader textureComputeShader;
        public int kernel = 0;

        public enum Channel { R, G, B, A, Luma, Average, Value }

        public Texture2D textureR;
        public Texture2D textureG;
        public Texture2D textureB;
        public Texture2D textureA;

        public Channel sourceR, sourceG, sourceB, sourceA;

        public RenderTexture textureCombined;
        private int textureSize = 4;
        private string[] textureSizes = new string[] { "64", "128", "256", "512", "1024", "2048", "4096", "8192", "Custom" };
        private int textureWidth = 1024;
        private int textureHeight = 1024;
        private Texture2D textureSaved;

        private float rColor = 0.0f;
        private float gColor = 0.0f;
        private float bColor = 0.0f;
        private float aColor = 0.0f;

        private float[] rUV = new float[2];
        private float[] gUV = new float[2];
        private float[] bUV = new float[2];
        private float[] aUV = new float[2];

        private int viewAlpha = 0;


        public UMAChannelCombiner()
        {
            // Get the compute shader
            textureComputeShader = Resources.Load<ComputeShader>("Shader/Combiner");
            kernel = textureComputeShader.FindKernel("Combiner");

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

        ~UMAChannelCombiner()
        {
            OnDestroy();
        }

        private void OnDestroy()
        {
            if (textureCombined != null)
            {
                ClearRenderTexture(textureCombined);
            }
        }

        public void TextureSizeUpdated()
        {
            if (textureSize != textureSizes.Length - 1)
            {
                textureWidth = int.Parse(textureSizes[textureSize]);
                textureHeight = textureWidth;
            }
            UpdateRenderTextures(true);
        }

        public void Reset()
        {
            OnDestroy();
            sourceR = Channel.R;
            sourceG = Channel.R;
            sourceB = Channel.R;
            sourceA = Channel.R;
            textureR = null;
            textureG = null;
            textureB = null;
            textureA = null;
            textureSaved = null;
            rColor = 0.0f;
            gColor = 0.0f;
            bColor = 0.0f;
            aColor = 0.0f;
        }

        private void UpdateRenderTextures(bool delete)
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

        public void RefreshCombinedTexture(bool preview)
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


            textureComputeShader.SetFloat("ColorR", rColor);
            textureComputeShader.SetFloat("ColorG", gColor);
            textureComputeShader.SetFloat("ColorB", bColor);
            textureComputeShader.SetFloat("ColorA", aColor);

            textureComputeShader.SetInt("alphaOnly", viewAlpha);

            textureComputeShader.SetTexture(kernel, "Result", textureCombined);

            textureComputeShader.Dispatch(kernel, textureCombined.width, textureCombined.height, 1);
            RenderTexture.active = textureCombined;
        }
      
        private void ClearRenderTexture(RenderTexture rt)
        {
            rt.Release();
            GameObject.DestroyImmediate(rt);
        }

        private int GUI_SourceChannel(Rect position, Channel channel, ref float ChannelColor)
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
            }

            return (int)channel;
        }

        public void SaveAs(string path)
        {
            UMAAvatarLoadSaveMenuItems.SaveRenderTexture(textureCombined, path, false);
        }

        private bool Load(Texture2D texture)
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
                        GameObject.DestroyImmediate(textureCombined);
                    }

                    if (!string.IsNullOrEmpty(errorGUID))
                    {
                        EditorUtility.DisplayDialog("Error", "Source texture(s) couldn't be found in the project:\n\n" + errorGUID + "\n\nMaybe they have been deleted, or they GUID has been updated?", "Ok");
                    }

                    return true;
                }
            }

            return false;
        }
    }
}
