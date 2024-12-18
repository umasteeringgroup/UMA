using System.Collections.Generic;
#if UNITY_EDITOR
using System.Text;
using UnityEditorInternal;
#endif
using UnityEngine;
using UnityEngine.Serialization;

namespace UMA
{
    /// <summary>
    /// Contains the immutable data shared between slots of the same type.
    /// </summary>
    [System.Serializable]
    [PreferBinarySerialization]
    public partial class SlotDataAsset : ScriptableObject, ISerializationCallbackReceiver, INameProvider, IUMAIndexOptions
    {
        public enum BlendshapeCopyMode {UpdateAndAdd, ClearAndReplace, AddNewOnly }
        public enum NormalCopyMode {CopyNormals, AverageNormals }
        public string slotName;
        [System.NonSerialized]
        public int nameHash;

        public bool forceKeep = false;
        public bool ForceKeep { get { return forceKeep; } set { forceKeep = value; } }


#if UNITY_EDITOR
        [Tooltip("This is only used when updating the slot with drag and drop below. It is not used at runtime nor is it included in the build")]
        public SkinnedMeshRenderer normalReferenceMesh;
        [HideInInspector]
        public bool ConvertTangents;

        private StringBuilder errorBuilder = new StringBuilder();

        [System.Serializable]
        public class WeldPoint
        {
            public int ourVertex;
            public int theirVertex;
            public Vector3 newNormal;
            public bool misMatch;
            public WeldPoint(int ours, int theirs, Vector3 newNormal, bool misMatch)
            {
                ourVertex = ours;
                theirVertex = theirs;
                this.newNormal = newNormal;
                this.misMatch = misMatch;
            }
        }

        [System.Serializable]
        public class Welding
        {
            public string WeldedToSlot;
            public int MisMatchCount = 0;
            public List<WeldPoint> WeldPoints = new List<WeldPoint>();
        }

        public List<Welding> Welds = new List<Welding>();

        public Dictionary<int, int> TheirVertexToOurVertex = new Dictionary<int, int>();
        public Dictionary<int,int> OurVertextoTheirVertex = new Dictionary<int, int>();
        public Dictionary<int,int> TheirBonesToOurBones = new Dictionary<int, int>();
        public Dictionary<int, int> OurBonesToTheirBones = new Dictionary<int, int>();
        public Dictionary<int, List<BoneWeight1>> TheirBoneWeights = new Dictionary<int, List<BoneWeight1>>();
        public Dictionary<int, List<BoneWeight1>> OurBoneWeights = new Dictionary<int, List<BoneWeight1>>();

        public int FindOurBone(string boneName)
        {
            for (int i = 0; i < meshData.umaBones.Length; i++)
            {
                if (meshData.umaBones[i].name == boneName)
                {
                    return i;
                }
            }
            return -1;
        }

        /*
        private static void BuildBoneWeights(UMAMeshData data, NativeArray<BoneWeight1> dest, NativeArray<byte> destBonesPerVertex, int destIndex, int destBoneweightIndex, int count, int[] bones, Matrix4x4[] bindPoses, Dictionary<int, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<int> bonesList)
        {
            int[] boneMapping = new int[bones.Length];

            for (int i = 0; i < boneMapping.Length; i++)
            {
                boneMapping[i] = TranslateBoneIndex(i, bones, bindPoses, bonesCollection, bindPosesList, bonesList);
            }
        }

        private static int TranslateBoneIndex(int index, int[] bonesHashes, Matrix4x4[] bindPoses, Dictionary<int, BoneIndexEntry> bonesCollection, List<Matrix4x4> bindPosesList, List<int> bonesList)
        {
            var boneTransform = bonesHashes[index];
            BoneIndexEntry entry;
            if (bonesCollection.TryGetValue(boneTransform, out entry))
            {
                for (int i = 0; i < entry.Count; i++)
                {
                    var res = entry[i];
                    if (CompareSkinningMatrices(bindPosesList[res], ref bindPoses[index]))
                    {
                        return res;
                    }
                }
                var idx = bindPosesList.Count;
                entry.AddIndex(idx);
                bindPosesList.Add(bindPoses[index]);
                bonesList.Add(boneTransform);
                return idx;
            }
            else
            {
                var idx = bindPosesList.Count;
                bonesCollection.Add(boneTransform, new BoneIndexEntry() { index = idx });
                bindPosesList.Add(bindPoses[index]);
                bonesList.Add(boneTransform);
                return idx;
            }
        } */

        public void BuildOurAndTheirBoneWeights(SlotDataAsset theirSlot)
        {
            OurBoneWeights.Clear();
            TheirBoneWeights.Clear();
            // Loop through all the boneweights, and build a dictionary of bone indexes to weights.

            int BoneWeightPos = 0;
            for(int ourVertex=0; ourVertex< meshData.vertices.Length;ourVertex++)
            {
                OurBoneWeights.Add(ourVertex, new List<BoneWeight1>());
                for(int i=0; i < meshData.ManagedBonesPerVertex[ourVertex]; i++)
                {
                    OurBoneWeights[ourVertex].Add(meshData.ManagedBoneWeights[BoneWeightPos]);
                    BoneWeightPos++;
                }
            }

            BoneWeightPos = 0;
            for(int theirVertex = 0; theirVertex < theirSlot.meshData.vertices.Length; theirVertex++)
            {
                TheirBoneWeights.Add(theirVertex, new List<BoneWeight1>());
                for(int i=0; i < theirSlot.meshData.ManagedBonesPerVertex[theirVertex]; i++)
                {
                    TheirBoneWeights[theirVertex].Add(theirSlot.meshData.ManagedBoneWeights[BoneWeightPos]);
                    BoneWeightPos++;
                }
            }
        }
        
        public struct boneInfo
        {
            public int boneIndex;
            public int hash;
            public string name;
        }

        List<boneInfo> ourboneInfos = new List<boneInfo>();
        List<boneInfo> theirboneInfos = new List<boneInfo>();

        public Dictionary<int,boneInfo> ourHashToName = new Dictionary<int, boneInfo>();
        public Dictionary<int, boneInfo> theirHashToName = new Dictionary<int, boneInfo>();


        public string FindName(int hash, UMAMeshData data)
        {
            for(int i=0;i<data.umaBones.Length;i++)
            {
                string name = data.umaBones[i].name;
                if (UMAUtils.StringToHash(name) == hash)
                {
                    return name;
                }
            }
            return "unknown"; 
        }
        public void BuildBoneHashLookups(UMAMeshData data, Dictionary<int, boneInfo> boneInfos, List<boneInfo> boneInfoList)
        {
            boneInfos.Clear();
            boneInfoList.Clear();
            for (int i=0;i<data.boneNameHashes.Length; i++)
            {
                boneInfo bi = new boneInfo();
                bi.boneIndex = i;
                bi.hash = data.boneNameHashes[i];
                bi.name = FindName(bi.hash, data);
                boneInfos.Add(bi.boneIndex, bi);
                boneInfoList.Add(bi);
            }
        }


        public void BuildBoneLookups(SlotDataAsset theirSlot)
        {
            BuildBoneHashLookups(theirSlot.meshData,theirHashToName, theirboneInfos);
            BuildBoneHashLookups(this.meshData, ourHashToName,ourboneInfos);


            TheirBonesToOurBones.Clear();

            for(int i=0;i<ourboneInfos.Count; i++)
            {
                boneInfo ourBone = ourboneInfos[i];
                for(int j=0;j<theirboneInfos.Count;j++)
                {
                    boneInfo theirBone = theirboneInfos[j];
                    if (ourBone.hash == theirBone.hash)
                    {
                        TheirBonesToOurBones.Add(j, i);
                    }
                }
            }

            OurBonesToTheirBones.Clear();
            for (int i = 0; i < theirboneInfos.Count; i++)
            {
                boneInfo theirBone = theirboneInfos[i];
                for (int j = 0; j < ourboneInfos.Count; j++)
                {
                    boneInfo ourBone = ourboneInfos[j];
                    if (ourBone.hash == theirBone.hash)
                    {
                        OurBonesToTheirBones.Add(j, i);
                    }
                }
            }





            /*



            for (int i = 0; i < theirSlot.meshData.umaBones.Length; i++)
            {
                string theirBoneName = theirSlot.meshData.umaBones[i].name;
                int ourBoneIndex = FindOurBone(theirBoneName);
                if (ourBoneIndex == -1)
                {
                    Debug.LogError($"Could not find bone {theirBoneName} in our bones");
                    return;
                }
                TheirBonesToOurBones.Add(i, ourBoneIndex);
            }

            OurBonesToTheirBones.Clear();
            for (int i = 0; i < meshData.umaBones.Length; i++)
            {
                string ourBoneName = meshData.umaBones[i].name;
                int theirBoneIndex = theirSlot.FindOurBone(ourBoneName);
                if (theirBoneIndex == -1)
                {
                    Debug.LogError($"Could not find bone {ourBoneName} in their bones");
                    return;
                }
                OurBonesToTheirBones.Add(i, theirBoneIndex);
            } */
        }

        public void BuildVertexLookups(SlotDataAsset theirSlot)
        {
            TheirVertexToOurVertex.Clear();
            for (int Thiers = 0; Thiers < theirSlot.meshData.vertices.Length; Thiers++)
            {
                float Closest = float.MaxValue;
                int ClosestOurs = -1;
                for (int ours = 0; ours < meshData.vertices.Length; ours++)
                {
                    float Len = (theirSlot.meshData.vertices[Thiers] - meshData.vertices[ours]).magnitude;
                    if (Len < Closest)
                    {
                        Closest = Len;
                        ClosestOurs = ours;
                    }
                }
                TheirVertexToOurVertex.Add(Thiers, ClosestOurs);
            }

            OurVertextoTheirVertex.Clear();
            for (int ours = 0; ours < meshData.vertices.Length; ours++)
            {
                float Closest = float.MaxValue;
                int ClosestTheirs = -1;
                for (int Thiers = 0; Thiers < theirSlot.meshData.vertices.Length; Thiers++)
                {
                    float Len = (theirSlot.meshData.vertices[Thiers] - meshData.vertices[ours]).magnitude;
                    if (Len < Closest)
                    {
                        Closest = Len;
                        ClosestTheirs = Thiers;
                    }
                }
                OurVertextoTheirVertex.Add(ours, ClosestTheirs);
            }

        }


        public string CopyBoneweightsFrom(SlotDataAsset sourceSlot)
        {
            int foundcount = 0;
            int notfoundcount = 0;
            EnsureBoneWeights();
            sourceSlot.EnsureBoneWeights();

            BuildVertexLookups(sourceSlot);
            BuildBoneLookups(sourceSlot);
            BuildOurAndTheirBoneWeights(sourceSlot);

            Dictionary<int, List<BoneWeight1>> NewBoneWeights = new Dictionary<int, List<BoneWeight1>>();

            for (int ourVertex = 0; ourVertex < meshData.ManagedBonesPerVertex.Length; ourVertex++)
            {

                bool found = false;
                int theirVertex = OurVertextoTheirVertex[ourVertex];
                if (theirVertex == 1785)
                {
                    Debug.Log("RightEar hash is " + UMAUtils.StringToHash("RightEar"));
                    Debug.Log("Breakpoint");
                }
                List<BoneWeight1> CurrentWeights = new List<BoneWeight1>();
                if (TheirBoneWeights.ContainsKey(theirVertex))
                {
                    var ourBones = OurBoneWeights[ourVertex];
                    var theirBones = TheirBoneWeights[theirVertex];

                    for (int i = 0; i < theirBones.Count; i++)
                    {
                        BoneWeight1 bw = theirBones[i];
                        if (!TheirBonesToOurBones.ContainsKey(bw.boneIndex))
                        {
                            found = false;
                            break;
                        }
                        found = true;
                        int ourBone = TheirBonesToOurBones[bw.boneIndex];

                        BoneWeight1 newBW = new BoneWeight1();
                        newBW.boneIndex = ourBone;
                        newBW.weight = bw.weight;
                        CurrentWeights.Add(newBW);
                    }
                }

                // if we found all of them, use those boneweights.
                if (found)
                {
                    NewBoneWeights.Add(ourVertex, CurrentWeights);
                    foundcount++;
                }
                else
                {
                    // if we didn't find all of them, use the boneweights we already have.
                    List<BoneWeight1> oldWeights = OurBoneWeights[ourVertex];
                    NewBoneWeights.Add(ourVertex, oldWeights);
                    notfoundcount++;
                }
            }
            List<BoneWeight1> allNewWeights = new List<BoneWeight1>();
            // now save all the boneweights.
            for (int ourVertex = 0; ourVertex < meshData.ManagedBonesPerVertex.Length; ourVertex++)
            {
                int numWeights = meshData.ManagedBonesPerVertex[ourVertex];
                List<BoneWeight1> weights = NewBoneWeights[ourVertex];
                allNewWeights.AddRange(weights);
                meshData.ManagedBonesPerVertex[ourVertex] = (byte)weights.Count;
            }
            meshData.ManagedBoneWeights = allNewWeights.ToArray();
            return $"Old weights {meshData.ManagedBoneWeights.Length} new weights is {allNewWeights.Count} Found {foundcount} boneweights, and {notfoundcount} boneweights were not found.";
        }

        public string CopyBlendshapesFrom(SlotDataAsset sourceSlot,BlendshapeCopyMode bs)
        {
            return CopyBlendShapes(sourceSlot, bs);
        }

        public string CopyNormalsFrom(SlotDataAsset sourceSlot, float weldDistance, NormalCopyMode nm)
        {
            int foundVerts = 0;
            int unfoundVerts = 0;
            int changedVertexes = 0;

            for (int Dest = 0; Dest < sourceSlot.meshData.vertices.Length; Dest++)
            {
                for (int Src = 0; Src < meshData.vertices.Length; Src++)
                {
                    Vector3 TheirVert = sourceSlot.meshData.vertices[Dest];
                    Vector3 ourVert = meshData.vertices[Src];
                    float Len = (TheirVert - ourVert).magnitude;
                    if (Len < weldDistance)
                    {
                        foundVerts++;
                        float Normaldiff = (meshData.normals[Src] - sourceSlot.meshData.normals[Dest]).magnitude;
                        if (Normaldiff != 0)
                        {
                            changedVertexes++;
                            if (nm == NormalCopyMode.CopyNormals)
                            {
                                meshData.normals[Src] = sourceSlot.meshData.normals[Dest];
                                if (meshData.tangents != null && sourceSlot.meshData.tangents != null)
                                {
                                    meshData.tangents[Src] = sourceSlot.meshData.tangents[Dest];
                                }

                            }
                            else
                            {
                                meshData.normals[Src] = (sourceSlot.meshData.normals[Dest] + meshData.normals[Src]).normalized;
                                if (meshData.tangents != null && sourceSlot.meshData.tangents != null)
                                {
                                    meshData.tangents[Src] = (sourceSlot.meshData.tangents[Dest] + meshData.tangents[Src]).normalized;
                                }
                            }
                        }
                    }
                    else
                    {
                        unfoundVerts++;
                    }
                }
            }

            string result = $"Found {foundVerts} verts\n{unfoundVerts} verts were not found\n{changedVertexes} verts had different normals, and were updated.";
            return "";
        }

        /*
        public Welding CalculateWelds(SlotDataAsset sourceSlot, bool CopyNormals, bool CopyBoneWeights, bool AverageNormals, float weldDistance, BlendshapeCopyMode bscopyMode )
        {
            Welding thisWeld = new Welding();


            thisWeld.MisMatchCount = 0;
            thisWeld.WeldedToSlot = sourceSlot.slotName;


            // managed Boneweights 
            // public BoneWeight1[] ManagedBoneWeights;
            // public byte[] ManagedBonesPerVertex;

            // ManagedBonesPerVertex is a byte array that contains the number of bones that affect each vertex.
            // ManagedBoneWeights is a BoneWeight1 array that contains the bone index and weight for each bone that affects each vertex.

            // to convert the boneweights, we need to match each of our vertexes to the source vertexes.
            // and then match the source bones to our bones.
            // then we can copy the boneweights from the source to our boneweights, but using our bone indexes.
            // Each of our vertexes must have a matching set of boneweights.  
            // Any of the bones in the source mesh (not our mesh) that are weighted must have a corresponding bone in our mesh.
            // But non-weighted bones in the source mesh do not need to be in our mesh.

            // So go through, and map our vertexes to the closest vertex in their mesh. Then build a reverse lookup for the vertexes.
            // the go through all the mapped bones, and build a reverse lookup for the bones.
            // Then build a new boneweight array, using our vertexes and bone indexes, but their weights.



            for (int Dest = 0; Dest < sourceSlot.meshData.vertices.Length; Dest++)
            {
                for (int Src = 0; Src < meshData.vertices.Length; Src++)
                {
                    Vector3 TheirVert = sourceSlot.meshData.vertices[Dest];
                    Vector3 ourVert = meshData.vertices[Src];
                    float Len = (TheirVert - ourVert).magnitude;
                    if (Len < weldDistance)
                    {
                        bool misMatch = false;
                        float Normaldiff = (meshData.normals[Src] - sourceSlot.meshData.normals[Dest]).magnitude;
                        if (Normaldiff > Vector3.kEpsilon)
                        {
                            thisWeld.MisMatchCount++;
                            misMatch = true;
                        }
                        if (CopyNormals)
                        {
                            meshData.normals[Src] = sourceSlot.meshData.normals[Dest];
                            if (meshData.tangents != null && sourceSlot.meshData.tangents != null)
                            {
                                meshData.tangents[Src] = sourceSlot.meshData.tangents[Dest];
                            }
                            if (AverageNormals)
                            {
                                meshData.normals[Src] = (sourceSlot.meshData.normals[Dest] + meshData.normals[Src]).normalized;
                            }
                        }

                        WeldPoint wp = new WeldPoint(Src, Dest, sourceSlot.meshData.normals[Dest], misMatch);
                        thisWeld.WeldPoints.Add(wp);
                    }
                }
            }

            if (CopyBoneWeights)
            {
                int foundcount = 0;
                int notfoundcount = 0;
                EnsureBoneWeights();
                sourceSlot.EnsureBoneWeights();

                BuildVertexLookups(sourceSlot);
                BuildBoneLookups(sourceSlot);
                BuildOurAndTheirBoneWeights(sourceSlot);

                Dictionary<int,List<BoneWeight1>> NewBoneWeights = new Dictionary<int,List<BoneWeight1>>();

                for (int ourVertex = 0; ourVertex < meshData.ManagedBonesPerVertex.Length;ourVertex++)
                {

                    bool found = false;
                    int theirVertex = OurVertextoTheirVertex[ourVertex];
                    if (theirVertex == 1785)
                    {
                        Debug.Log("RightEar hash is " + UMAUtils.StringToHash("RightEar"));
                        Debug.Log("Breakpoint");
                    }
                    List<BoneWeight1> CurrentWeights = new List<BoneWeight1>();
                    if (TheirBoneWeights.ContainsKey(theirVertex))
                    {
                        var ourBones = OurBoneWeights[ourVertex];
                        var theirBones = TheirBoneWeights[theirVertex];

                        for (int i = 0; i < theirBones.Count; i++)
                        {
                            BoneWeight1 bw = theirBones[i];
                            if (!TheirBonesToOurBones.ContainsKey(bw.boneIndex))
                            {
                                found = false;
                                break;
                            }
                            found = true;
                            int ourBone = TheirBonesToOurBones[bw.boneIndex];

                            BoneWeight1 newBW = new BoneWeight1();
                            newBW.boneIndex = ourBone;
                            newBW.weight = bw.weight;
                            CurrentWeights.Add(newBW);
                        }
                    }

                    // if we found all of them, use those boneweights.
                    if (found)
                    {
                        NewBoneWeights.Add(ourVertex, CurrentWeights);
                        foundcount++;
                    }
                    else
                    {
                        // if we didn't find all of them, use the boneweights we already have.
                        List<BoneWeight1> oldWeights = OurBoneWeights[ourVertex];
                        NewBoneWeights.Add(ourVertex, oldWeights);
                        notfoundcount++;
                    }
                }
                List<BoneWeight1> allNewWeights = new List<BoneWeight1>();
                // now save all the boneweights.
                for (int ourVertex = 0; ourVertex < meshData.ManagedBonesPerVertex.Length;ourVertex++)
                {
                    int numWeights = meshData.ManagedBonesPerVertex[ourVertex];
                    List<BoneWeight1> weights = NewBoneWeights[ourVertex];
                    allNewWeights.AddRange(weights);
                    meshData.ManagedBonesPerVertex[ourVertex] = (byte)weights.Count;
                }
                Debug.Log($"Old weights {meshData.ManagedBoneWeights.Length} new weights is {allNewWeights.Count} Found {foundcount} boneweights, and {notfoundcount} boneweights were not found.");
                meshData.ManagedBoneWeights = allNewWeights.ToArray();
            }

            if (bscopyMode != BlendshapeCopyMode.None)
            {
                CopyBlendShapes(sourceSlot, bscopyMode);
            }

            return thisWeld;
        } */

        int FindBlendshape(string Name)
        {
            for(int i=0;i< meshData.blendShapes.Length; i++)
            {
                if (meshData.blendShapes[i].shapeName == Name)
                {
                    return i;
                }
            }
            return -1;
        }

        private string CopyBlendShapes(SlotDataAsset slot, BlendshapeCopyMode bscopyMode)
        {
            int updateCount = 0;
            int addedCount = 0;
            int skippedCount = 0;

            BuildVertexLookups(slot);
            if (bscopyMode == BlendshapeCopyMode.ClearAndReplace)
            {
                meshData.blendShapes = new UMABlendShape[0];
            }

            for (int i = 0; i < slot.meshData.blendShapes.Length; i++)
            {
                string newBlendshapeName = slot.meshData.blendShapes[i].shapeName;
                int foundBlendshape = FindBlendshape(newBlendshapeName);
                // if we are only adding new ones, and it already exists, then just skip it.
                if (bscopyMode == BlendshapeCopyMode.AddNewOnly && foundBlendshape != -1)
                {
                    skippedCount++;
                    continue;
                }

                if (foundBlendshape != -1)
                {
                    updateCount++;
                    // if we are updating and adding, then update the existing one if it exists.
                    meshData.blendShapes[foundBlendshape] = slot.meshData.blendShapes[i].DuplicateAndTranslate(OurVertextoTheirVertex);
                }
                else
                {
                    addedCount++;
                    // Doesn't exist, so add it.
                    var shapes = new List<UMABlendShape>();
                    shapes.AddRange(meshData.blendShapes);
                    shapes.Add(slot.meshData.blendShapes[i].DuplicateAndTranslate(OurVertextoTheirVertex));
                    meshData.blendShapes = shapes.ToArray();
                }
            }
            return $"Updated {updateCount} blendshapes, added {addedCount} blendshapes, skipped {skippedCount} blendshapes.";
        }

        public bool HasErrors
        {
            get
            {
                return !string.IsNullOrEmpty(Errors);
            }
        }
        public string Errors;

        /// <summary>
        /// Returns true if meshdata is valid or null (a utility slot).
        /// </summary>
        /// <returns></returns>
        public bool ValidateMeshData()
        {
            Errors = "";
            errorBuilder.Clear();

            if (meshData == null)
            {
                return true;
            }
            if (material == null)
            {
                AddError("material is null. A valid UMAMaterial that matches the overlay should be assigned.");
            }
            Errors = meshData.Validate();
            return true;
        }

        private void AddError(string v)
        {
            if (errorBuilder.Length == 0)
            {
                errorBuilder.Append(v);
            }
            else
            {
                errorBuilder.Append("; ");
                errorBuilder.Append(v);
            }
        }

        public ReorderableList tagList { get; set; }
        public List<string> backingTags { get; set; }
        public bool eventsFoldout { get; set; } = false;
        public bool tagsFoldout { get; set; } = false;
        public bool smooshFoldout { get; set; } = false;
        public bool utilitiesFoldout { get; set; } = false;



#endif

        public UMARendererAsset RendererAsset { get { return _rendererAsset; } }
        [SerializeField] private UMARendererAsset _rendererAsset = null;

        #region INameProvider

        public string GetAssetName()
        {
            return slotName;
        }
        public int GetNameHash()
        {
            return nameHash;
        }

        #endregion
        /// <summary>
        /// The UMA material.
        /// </summary>
        /// <remarks>
        /// The UMA material contains both a reference to the Unity material
        /// used for drawing and information needed for matching the textures
        /// and colors to the various material properties.
        /// </remarks>
        [UMAAssetFieldVisible]
        [SerializeField]
        public UMAMaterial material;

        /// <summary>
        /// materialName is used to save the name of the material, but ONLY if we have cleared the material when building bundles.
        /// You can't count on this field to contain a value unless it was set during the cleanup phase by the indexer!
        /// </summary>
        public string materialName;

        /// <summary>
        /// This SlotDataAsset will not be included after this LOD level.
        /// Set high by default so behavior is the same.
        /// </summary>
        [Tooltip("If you are using an LOD system, this is the maximum LOD that this slot will be displayed. After that, it will be discarded during mesh generation. a value of -1 will never be dropped.")]
        public int maxLOD = -1;

        /// <summary>
        /// 
        /// </summary>
        public bool useAtlasOverlay;

        /// <summary>
        /// Default overlay scale for slots using the asset.
        /// </summary>
        public float overlayScale = 1.0f;
        /// <summary>
        /// The animated bone names.
        /// </summary>
        /// <remarks>
        /// The animated bones array is required for cases where optimizations
        /// could remove transforms from the rig. Animated bones will always
        /// be preserved.
        /// </remarks>
        public string[] animatedBoneNames = new string[0];
        /// <summary>
        /// The animated bone name hashes.
        /// </summary>
        /// <remarks>
        /// The animated bones array is required for cases where optimizations
        /// could remove transforms from the rig. Animated bones will always
        /// be preserved.
        /// </remarks>
        [UnityEngine.HideInInspector]
        public int[] animatedBoneHashes = new int[0];

        [Tooltip("This object is a clipping plane, and is not added to the model.")]
        public bool isClippingPlane = false;

        [Tooltip("You can adjust the corners of the clipping plane here. Do not make the plane non-planar!")]
        public Vector3[] clippingPlaneOffset = new Vector3[4];

        [Tooltip("This object is a smooshable. Any overriden vertexes will be cleared before smooshing.")]
        public bool isSmooshable = false;

        [Tooltip("This is used to offset the slot for some reason")]
        public Vector3 smooshOffset = Vector3.zero;

        [Tooltip("This is used to grow around the center. Negative values subtract. Positive values add.")]
        public Vector3 smooshExpand = Vector3.one;

        [Tooltip("This object can process events ")]
        public GameObject SlotObject;
        private bool SlotObjectHookedUp = false;

#pragma warning disable 649
        //UMA2.8+ we need to use DNAConverterField now because that can contain Behaviours and the new controllers
        //we need this because we need the old data out of it on deserialize
        /// <summary>
        /// Optional DNA converter specific to the slot.
        /// </summary>
        [FormerlySerializedAs("slotDNA")]
        [SerializeField]
        private DnaConverterBehaviour _slotDNALegacy;
#pragma warning restore 649

        //UMA 2.8 FixDNAPrefabs: this is a new field that can take DNAConverter Prefabs *and* DNAConverterControllers
        [SerializeField]
        [Tooltip("Optional DNA converter specific to the slot. Accepts a DNAConverterController asset or a legacy DNAConverterBehaviour prefab.")]
        private DNAConverterField _slotDNA = new DNAConverterField();

        [Tooltip("If isWildCardSlot = true, then the overlays on this slot are applied to any slot or overlay with a matching tag when the recipe is built. This is used in Wardrobe Recipes to apply overlays to other slots.")]
        public bool isWildCardSlot;

        //UMA 2.8 FixDNAPrefabs: I'm putting the required property for this here because theres no properties anywhere else!
        public IDNAConverter slotDNA
        {
            get { return _slotDNA.Value; }
            set { _slotDNA.Value = value; }
        }

        public bool isUtilitySlot
        {
            get
            {
                if (isClippingPlane)
                {
                    return true;
                }

                if (meshData != null || meshData.vertexCount > 0)
                {
                    return false;
                }

                if (material == null)
                {
                    return true;
                }

                if (CharacterBegun != null && CharacterBegun.GetPersistentEventCount() > 0)
                {
                    return true;
                }

                if (SlotAtlassed != null && SlotAtlassed.GetPersistentEventCount() > 0)
                {
                    return true;
                }

                if (DNAApplied != null && DNAApplied.GetPersistentEventCount() > 0)
                {
                    return true;
                }

                if (CharacterCompleted != null && CharacterCompleted.GetPersistentEventCount() > 0)
                {
                    return true;
                }

                return false;
            }
        }

        private bool labelLocalFiles = false;
        public bool LabelLocalFiles { get { return labelLocalFiles; } set { labelLocalFiles = value; } }

        public void LoadFromIndex()
        {
            material = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(materialName);
        }


        /// <summary>
        /// The mesh data.
        /// </summary>
        /// <remarks>
        /// The UMAMeshData contains all of the Unity mesh data and additional
        /// information needed for mesh manipulation while minimizing overhead
        /// from accessing Unity's managed memory.
        /// </remarks>
        public UMAMeshData meshData;
        public int subMeshIndex;
        /// <summary>
        /// Use this to identify slots that serves the same purpose
        /// Eg. ChestArmor, Helmet, etc.
        /// </summary>
        public string slotGroup;
        /// <summary>
        /// This can be used for hiding, matching etc. 
        /// It's used by the DynamicCharacterSystem to hide slots by tag.
        /// </summary>
        public string[] tags;

        // Wildcard slot race matches
        public string[] Races;

        /// <summary>
        /// These are the vertexes in local space to the character mesh.
        /// This can be different from the slot vertexes depending on the modeller, how it
        /// was exported, and whether the transform was applied. What a pain.
        /// This is calculated once and cached. Currently, it is only used for hair smooshing.
        /// but we may find other uses for it, like with decals or the Mesh Hide editor.
        /// This data *could* be serialized, but for now, it is not. TODO: serialize it, and generate it during
        /// the slot build process.
        [System.NonSerialized]
        public Vector3[] TransformedLocalVertexes;

        /// <summary>
        /// Callback event when character update begins.
        /// </summary>
        public UMADataEvent CharacterBegun;
        /// <summary>
        /// Callback event when slot overlays are atlased.
        /// </summary>
        public UMADataSlotMaterialRectEvent SlotAtlassed;
        /// <summary>
        /// Callback event when character DNA is applied.
        /// </summary>
        public UMADataEvent DNAApplied;
        /// <summary>
        /// Callback event when character update is complete.
        /// </summary>
        public UMADataEvent CharacterCompleted;

        public UMADataSlotProcessedEvent SlotProcessed;
        public UMADataSlotProcessedEvent SlotBeginProcessing;

        /// <summary>
        /// This slot was auto generated as a LOD slot based on another slot.
        /// </summary>
        [SerializeField]
        [HideInInspector]
        public bool autoGeneratedLOD;

        public SlotDataAsset()
        {

        }

        private List<IUMAEventHookup> EventHookups = new List<IUMAEventHookup>();

        public void Awake()
        {
        }

        public void Begin(UMAData umaData)
        {
            if (SlotObject != null)
            {
                HookupObjectEvents();
                for (int i = 0; i < EventHookups.Count; i++)
                {
                    IUMAEventHookup ih = EventHookups[i];
                    ih.Begun(umaData);
                }
            }
        }

        public void Completed(UMAData umaData)
        {
            if (SlotObject != null)
            {
                for (int i = 0; i < EventHookups.Count; i++)
                {
                    IUMAEventHookup ih = EventHookups[i];
                    ih.Completed(umaData, this.SlotObject);
                }
            }
        }

        private void HookupObjectEvents()
        {
            if (this.SlotObject != null)
            {
                if (SlotObjectHookedUp && EventHookups.Count > 0)
                {
                    return;
                }

                SlotObjectHookedUp = true;
                var Behaviors = SlotObject.GetComponents<MonoBehaviour>();
                Debug.Log($"There are {Behaviors.Length} components");

                for (int i = 0; i < Behaviors.Length; i++)
                {
                    MonoBehaviour mb = Behaviors[i];
                    if (mb is IUMAEventHookup)
                    {
                        Debug.Log("SDA Hooking up events");
                        EventHookups.Add(mb as IUMAEventHookup);
                        (mb as IUMAEventHookup).HookupEvents(this);
                    }
                }
            }
        }

        public void OnDestroy()
        {
            if (meshData != null)
            {
                meshData.FreeBoneWeights();
            }
        }

        public void OnDisable()
        {
            if (meshData != null)
            {
                meshData.FreeBoneWeights();
            }
        }

        public int GetTextureChannelCount(UMAGeneratorBase generator)
        {
            return material.channels.Length;
        }

        public override string ToString()
        {
            return "SlotData: " + slotName;
        }

        public void UpdateMeshData(SkinnedMeshRenderer meshRenderer, string rootBoneName, bool udimAdjustment, int submeshIndex)
        {
            meshData = new UMAMeshData();
            meshData.SlotName = this.slotName;
            meshData.RootBoneName = rootBoneName;
            meshData.RetrieveDataFromUnityMesh(meshRenderer,submeshIndex,udimAdjustment);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

       /* public void OldpdateMeshData(SkinnedMeshRenderer meshRenderer)
        {
            meshData = new UMAMeshData();
            meshData.SlotName = this.slotName;
            meshData.RetrieveDataFromUnityMesh(meshRenderer.sharedMesh,false);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }*/


        public void OnEnable()
        {
            if (meshData == null)
            {
                return;
            }

            if (meshData.LoadedBoneweights)
            {
                // already loaded. just return.
                return;
            }
            if (meshData.ManagedBoneWeights != null && meshData.ManagedBoneWeights.Length > 0)
            {
                meshData.LoadVariableBoneWeights();
            }
            else if (meshData.boneWeights != null && meshData.boneWeights.Length > 0)
            {
                meshData.LoadBoneWeights();
            }
        }

        public void EnsureBoneWeights()
        {
            if (meshData.ManagedBonesPerVertex == null || meshData.ManagedBonesPerVertex.Length == 0)
            {
                meshData.LoadBoneWeights();
            }
        }


        public void UpdateMeshData()
        {
        }

        public void OnAfterDeserialize()
        {
            nameHash = UMAUtils.StringToHash(slotName);
        }

        public void OnBeforeSerialize()
        {

        }

        public void Assign(SlotDataAsset source)
        {
            slotName = source.slotName;
            nameHash = source.nameHash;
            material = source.material;
            overlayScale = source.overlayScale;
            animatedBoneNames = source.animatedBoneNames;
            animatedBoneHashes = source.animatedBoneHashes;
            meshData = source.meshData;
            subMeshIndex = source.subMeshIndex;
            isClippingPlane = source.isClippingPlane;
            isSmooshable = source.isSmooshable;
            slotGroup = source.slotGroup;
            tags = source.tags;
            Races = source.Races;
        }
    }
}
