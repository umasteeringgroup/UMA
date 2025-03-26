namespace UMA
{
#if Projected_Decals
// List<RendererFragment> 

// TODO:  Calculate slot vertex offset in mesh, store in materialfragment
// TODO:  Generate DecalChunk at runtime. Pull the triangles that contain the 
//        captured vertexes from the sample scene.
//        Make sure we can do this all at runtime.

public class Decal
{
    private class RendererFragment
    {
        // public GeneratedMaterial generatedMaterial;
        public SkinnedMeshRenderer renderer;
        public List<DecalChunk> chunks;

        public RendererFragment(SkinnedMeshRenderer ren)
        {
            renderer = ren;
            chunks = new List<DecalChunk>();
        }
    }

    private class MaterialPacket
    {
        public MaterialFragment materialFragment;
        public GeneratedMaterial generatedMaterial;
        public MaterialPacket(GeneratedMaterial gMat, MaterialFragment mFrag)
        {
            materialFragment = mFrag;
            generatedMaterial = gMat;
        }
    }


    public string uniqueName;
    public List<DecalChunk> DecalChunks;
    public Material DecalMaterial; // Decal textures must be clamped. 

    private void ApplyChunks(RendererFragment renderFragment)
    {
        SkinnedMeshRenderer smr = renderFragment.renderer;

        int MatIndex = -1;
        for (int i = 0; i < smr.materials.Length; i++)
        {
            Material m = smr.materials[i];
            if (m.name == uniqueName)
            {
                MatIndex = i;
                break;
            }
        }

        if (MatIndex == -1)
        {
            Material newMaterial = new Material(DecalMaterial);
            newMaterial.name = uniqueName;
            List<Material> mats = new List<Material>();
            smr.GetMaterials(mats);
            mats.Add(newMaterial);
            MatIndex = mats.Count - 1;
            smr.sharedMesh.subMeshCount++;
        }

        List<int> decalTris = new List<int>();



        foreach(DecalChunk chunk in DecalChunks)
        {
            decalTris.AddRange(chunk.TriangleList);
        }

        smr.sharedMesh.SetTriangles(decalTris, MatIndex);
    }

    public void ApplySubmesh(UMAData uMAData)
    {
        Dictionary<string, MaterialPacket> SlotsToPacket = new Dictionary<string, MaterialPacket>();
        Dictionary<string, RendererFragment> RendererFragments = new Dictionary<string, RendererFragment>();

        // todo: build a dictionary of the slots, and the material fragment.  
        foreach(var mat in uMAData.generatedMaterials.materials)
        {
            foreach(var slotmat in mat.materialFragments)
            {
                SlotsToPacket.Add(slotmat.slotData.slotName,new MaterialPacket(mat,slotmat));
            }
        }

        foreach(DecalChunk chunk in DecalChunks)
        {
            if (SlotsToPacket.ContainsKey(chunk.slotName))
            {
                var materialPacket = SlotsToPacket[chunk.slotName];
                chunk.fragment = materialPacket.materialFragment;
                string rendererName = materialPacket.generatedMaterial.skinnedMeshRenderer.name;
                if (!RendererFragments.ContainsKey(rendererName))
                {
                    RendererFragments.Add(rendererName, new RendererFragment(materialPacket.generatedMaterial.skinnedMeshRenderer));
                }
                RendererFragments[rendererName].chunks.Add(chunk);
            }
        }

        foreach(RendererFragment rf in RendererFragments.Values)
        {
            ApplyChunks(rf);
        }
    }
}

public class DecalChunk
{
    public string slotName;

    public int[] TriangleList;
    public MaterialFragment fragment;

    // index of vertexes in the slot. 
    // These need to be translate d to the mesh index after the build is complete.
    // To do this, we will need to track for each slot in the UMAData (during the build process)
    //    What SMR the slot is actually in, in case there are multiples
    //    what vertex position the slot starts at in the SMR
}
#endif
}