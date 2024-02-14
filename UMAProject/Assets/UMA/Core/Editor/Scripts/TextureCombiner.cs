using UnityEngine;

namespace UMA
{
    public class TextureCombiner
    {
        public enum Channel { R, G, B, A, Luma, Average, Value }

        private ComputeShader textureComputeShader;
        private int kernel = 0;

        public Texture2D textureR;
        public Texture2D textureG;
        public Texture2D textureB;
        public Texture2D textureA;

        public int viewAlpha = 0;
        public int textureWidth = 1024;
        public int textureHeight = 1024;

        public float rColor = 0.0f;
        public float gColor = 0.0f;
        public float bColor = 0.0f;
        public float aColor = 0.0f;

        public bool invertR = false;
        public bool invertG = false;
        public bool invertB = false;
        public bool invertA = false;

        public Channel sourceR, sourceG, sourceB, sourceA;

        private RenderTexture _textureCombined;
        public RenderTexture textureCombined => _textureCombined;
        public TextureCombiner(string shaderName)
        {
            textureComputeShader = Resources.Load<ComputeShader>(shaderName);
            if (textureComputeShader == null)
            {
                throw new System.Exception($"Compute shader '{shaderName}' not found");
            }
            kernel = textureComputeShader.FindKernel("Combiner");

            SolveTextures();
        }
        ~TextureCombiner()
        {
            OnDestroy();
        }
        private void SolveTextures()
        {
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
        public void UpdateRenderTextures(bool delete)
        {
            if (delete && textureCombined != null)
            {
                ClearRenderTexture(textureCombined);
            }
            if (textureCombined == null)
            {
                _textureCombined = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                _textureCombined.enableRandomWrite = true;
                _textureCombined.Create();
                _textureCombined.hideFlags = HideFlags.HideAndDontSave;
            }
        }
        private void ClearRenderTexture(RenderTexture rt)
        {
            rt.Release();
            GameObject.DestroyImmediate(rt);
        }
        private void OnDestroy()
        {
            if (textureCombined != null)
            {
                ClearRenderTexture(textureCombined);
            }
        }
        public void Reset()
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
            rColor = 0.0f;
            gColor = 0.0f;
            bColor = 0.0f;
            aColor = 0.0f;
            invertR = false;
            invertG = false;
            invertB = false;
            invertA = false;
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
        public void ClearRenderTexture()
        {
            _textureCombined.Release();
            GameObject.DestroyImmediate(_textureCombined);
        }
        public void Destroy()
        {
            OnDestroy();
        }
    }
}
