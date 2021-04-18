using System.Collections.Generic;
using System.Threading;
using UMA;
using UMA.CharacterSystem;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;


using System.IO;
using System.Collections;

public class AsyncCapture : MonoBehaviour
{
    IEnumerator Start()
    {
        while (true)
        {
            yield return new WaitForSeconds(1);
            yield return new WaitForEndOfFrame();

            var rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
            AsyncGPUReadback.Request(rt, 0, TextureFormat.ARGB32, OnCompleteReadback);
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            Debug.Log("GPU readback error detected.");
            return;
        }

        var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.ARGB32, false);
        tex.LoadRawTextureData(request.GetData<uint>());
        tex.Apply();
        File.WriteAllBytes("test.png", ImageConversion.EncodeToPNG(tex));
        Destroy(tex);
    }
}
public class UMARenderTextureMover : MonoBehaviour
{

    private DynamicCharacterAvatar avatar;

    public struct DestinationTextureHolder
    {
        public int mipCount; // number of mipmaps.
        public NativeArray<uint> CompleteArray;
        public bool[] MipConverted;
        public bool isDisposed;
    }

    public struct RenderTextureSource
    {
        public int atlasIndex;
        public int textureIndex;
        public RenderTexture renderTexture;
        public Material smm;
        public SkinnedMeshRenderer skinnedMeshRenderer;
        public int materialIndex;
    };

    public struct ReadBackTracker
    {
        public AsyncGPUReadbackRequest request;
        public RenderTextureSource texSource;
        public UMAData umaData;
        public DestinationTextureHolder texAccumulator;
        public int mipNumber;
    };

    public bool TriggerNow = false; // temporary test item.

    Dictionary<AsyncGPUReadbackRequest, ReadBackTracker> trackedItems = new Dictionary<AsyncGPUReadbackRequest, ReadBackTracker>();

    // Start is called before the first frame update
    void Start()
    {
        // hook into "updated" on DCA
        avatar = GetComponent<DynamicCharacterAvatar>();
        if (avatar == null)
        {
            Debug.LogError("This component requires a DynamicCharacterAvatar with an UMAData");
            return;
        }
        avatar.CharacterUpdated.AddListener(TextureMover);
    }

    void Update()
    {
        if (TriggerNow)
        {
            TriggerNow = false;
            TextureMover(avatar.umaData);
        }
    }

    public List<RenderTextureSource> GetRenderTextures(UMAData uMAData)
    {
        SkinnedMeshRenderer[] renderers = uMAData.GetRenderers();

        List<RenderTextureSource> textureList = new List<RenderTextureSource>();

        for (int atlasIndex = 0; atlasIndex < uMAData.generatedMaterials.materials.Count; atlasIndex++)
        {
            var atlas = uMAData.generatedMaterials.materials[atlasIndex];

            if (uMAData.generatedMaterials.materials[atlasIndex] != null && uMAData.generatedMaterials.materials[atlasIndex].resultingAtlasList != null)
            {
                SkinnedMeshRenderer smr = uMAData.generatedMaterials.materials[atlasIndex].skinnedMeshRenderer;

                for (int textureIndex = 0; textureIndex < uMAData.generatedMaterials.materials[atlasIndex].resultingAtlasList.Length; textureIndex++)
                {

                    if (uMAData.generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex] != null)
                    {
                        Texture tempTexture = uMAData.generatedMaterials.materials[atlasIndex].resultingAtlasList[textureIndex];

                        
                        if (tempTexture is RenderTexture)
                        {
                           // var atlas = umaData.generatedMaterials.materials[atlasIndex];

                            RenderTextureSource rts = new RenderTextureSource();
                            rts.atlasIndex = atlasIndex;
                            rts.textureIndex = textureIndex;
                            rts.renderTexture = tempTexture as RenderTexture;
                            rts.skinnedMeshRenderer = smr;
                            rts.materialIndex = uMAData.generatedMaterials.materials[atlasIndex].materialIndex;
                            
                            textureList.Add(rts);
                        }
                    }
                }
            }
        }
        return textureList;
    }


    private void TextureMover(UMAData umaData)
    {

        trackedItems.Clear();
        List<RenderTextureSource> textureSources = GetRenderTextures(umaData);
        Debug.Log("RenderTextures found: " + textureSources.Count); // get rid of this after testing.
        foreach (RenderTextureSource rts in textureSources)
        {
                MoveRenderTexture(umaData, rts);
        }
    }


    public void MoveRenderTexture(UMAData umaData, RenderTextureSource rts)
    {
        // Thread p = new Thread(new ParameterizedThreadStart(CopyRenderTexture(rts))

        int arrayLen = 0;

        int width = rts.renderTexture.width;
        int height = rts.renderTexture.height;
        /*
        for (int i=0; i< rts.renderTexture.mipmapCount; i++)
        {
            arrayLen += width * height * 4;
            width >>= 1;
            height >>= 1;
        }*/

        DestinationTextureHolder dtex = new DestinationTextureHolder();

        dtex.CompleteArray = new NativeArray<uint>(arrayLen, Allocator.Persistent);
/*        dtex.mipCount = rts.renderTexture.mipmapCount; */
        dtex.MipConverted = new bool[dtex.mipCount + 1];
        dtex.isDisposed = false;
 
        for (int i = 0; i < rts.renderTexture.mipmapCount; i++)
        {
            AsyncGPUReadbackRequest agr = AsyncGPUReadback.Request(rts.renderTexture, i, TextureFormat.ARGB32, OnCompleteReadback);
            ReadBackTracker rbt = new ReadBackTracker();
            rbt.request = agr;
            rbt.texSource = rts;
            rbt.umaData = umaData;
            rbt.texAccumulator = dtex;
            trackedItems.Add(agr, rbt);
        }  
    }

   void Cleanup(ReadBackTracker rbt)
    {
        if (!rbt.texAccumulator.isDisposed)
        {
            if (rbt.texAccumulator.CompleteArray.IsCreated)
            {
                rbt.texAccumulator.CompleteArray.Dispose();
                rbt.texAccumulator.isDisposed = true;
            }
        }
        trackedItems.Remove(rbt.request);
    }

    void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {/*
        if (request.hasError)
        {
            Debug.Log("GPU readback error detected.");
            return;
        }

        if (trackedItems.ContainsKey(request))
        {
            ReadBackTracker rbt = trackedItems[request];

            UMAData umaData = rbt.umaData;
            int atlasIndex = rbt.texSource.atlasIndex;
            int textureIndex = rbt.texSource.textureIndex;
            RenderTexture originalTexture = rbt.texSource.renderTexture;


            // first verify we're not out of bounds, just in case the generatedMaterials have changed from another build
            if (atlasIndex < umaData.generatedMaterials.materials.Count && textureIndex < umaData.generatedMaterials.materials[atlasIndex].resultingAtlasList.Length)
            {
                SkinnedMeshRenderer smr = rbt.texSource.skinnedMeshRenderer;

                // not sure how this would happen
                if (smr == null)
                {
                    // get out if we can't see the renderer
                    Cleanup(rbt);
                    return;
                }

                UMAData.GeneratedMaterial gm = umaData.generatedMaterials.materials[atlasIndex];
                Texture currentTexture = gm.resultingAtlasList[textureIndex];
                // make sure we are replacing the same texture we converted!
                // has it already been converted or replaced?
                if (currentTexture is RenderTexture)
                {
                    // check if the name is the same (name contains the framecount it was generated on).
                    if (currentTexture.name == originalTexture.name)
                    {
                        var tex = new Texture2D(request.width, request.height, TextureFormat.ARGB32,originalTexture.mipmapCount,true);
                        tex.filterMode = FilterMode.Point;
                        // wtf? Why you not give back a pointer to all mipmaps so I don't have to copy??
                        // tex.LoadRawTextureData(request.GetData<uint>());
                        Graphics.CopyTexture(originalTexture, tex);
                        tex.Apply();
                        gm.resultingAtlasList[textureIndex] = tex;
                        Material[] mats = smr.materials;
                        Material src = umaData.generatedMaterials.materials[atlasIndex].material;
                        src.SetTexture(gm.umaMaterial.channels[textureIndex].materialPropertyName,tex);
                        mats[rbt.texSource.materialIndex] = src;

                        // verify this is correct. update the texture here...
                        smr.materials = mats;
                        Destroy(currentTexture);
                    }
                }
            }
        }
        // File.WriteAllBytes("test.png", ImageConversion.EncodeToPNG(tex));
        // Destroy(tex);
        */
    }
}