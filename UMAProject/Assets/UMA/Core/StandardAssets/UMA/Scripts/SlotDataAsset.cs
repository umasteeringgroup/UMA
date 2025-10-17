using System.Collections.Generic;
#if UNITY_EDITOR
using System.Text;
#endif
using UMA.PoseTools;
#if UNITY_EDITOR
using UnityEditorInternal;
#endif
using UnityEngine;
using UnityEngine.Serialization;
using System.Text.RegularExpressions;

namespace UMA
{
    /// <summary>
    /// Contains the immutable data shared between slots of the same type.
    /// </summary>
    [System.Serializable]
    [PreferBinarySerialization]
    public partial class SlotDataAsset : ScriptableObject, ISerializationCallbackReceiver, INameProvider, IUMAIndexOptions
    {
        #region internalClasses
        [System.Serializable]
        public class BonePoseToRace
        {
            public string RaceName;
            public UMABonePose BonePose;
        };
        #endregion
        #region enums
        public enum BlendshapeCopyMode {UpdateAndAdd, ClearAndReplace, AddNewOnly }
        public enum NormalCopyMode {CopyNormals, AverageNormals }
        #endregion

        public string slotName;
        [System.NonSerialized]
        public int nameHash;

        #region IUMAIndexOptions
        public bool forceKeep = false;
        public bool ForceKeep { get { return forceKeep; } set { forceKeep = value; } }

        [Tooltip("If true, this Slot will not be added to the index when adding all")]
        public bool noAutoAdd = false;
        public bool NoAutoAdd { get { return noAutoAdd; } set { noAutoAdd = value; } }
        #endregion

        public List<BonePoseToRace> bonePoseToRaces = new List<BonePoseToRace>();

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

        }

        public void BuildVertexLookups(SlotDataAsset theirsSlot)
        {
            TheirVertexToOurVertex.Clear();
            for (int Thiers = 0; Thiers < theirsSlot.meshData.vertices.Length; Thiers++)
            {
                float Closest = float.MaxValue;
                int ClosestOurs = -1;
                for (int ours = 0; ours < meshData.vertices.Length; ours++)
                {
                    float Len = (theirsSlot.meshData.vertices[Thiers] - meshData.vertices[ours]).magnitude;
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
                for (int Thiers = 0; Thiers < theirsSlot.meshData.vertices.Length; Thiers++)
                {
                    float Len = (theirsSlot.meshData.vertices[Thiers] - meshData.vertices[ours]).magnitude;
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

        public UMARendererAsset RendererAsset { get { return _rendererAsset; } set { _rendererAsset = value; } }
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
        [Tooltip("The animated bones. These are root bones. Add a bone animator (SwayBoneAnimator or UnityJointAnimator) to animate bones for hair, jiggle, etc. Create the Bone Animators from the UMA right-click ment in the project.")]
        public BaseUpdatedObject[] animatedBones = new BaseUpdatedObject[0];

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
        /// <summary>
        /// Index of the submesh in the MeshData. Later versions will always have 1 submesh, but this is kept for 
        /// compatibility with older versions.
        /// </summary>
        public int subMeshIndex;
        /// <summary>
        /// Index of the submesh in the source mesh. This is needed for using the correct submesh when updating existing slots.
        /// Only used in latest version of SlotDataAsset.
        /// </summary>
        public int sourceSubmeshIndex = -1;
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
        /// </summary>
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
            //if (meshData != null)
            //{
            //    meshData.FreeBoneWeights();
            //}
        }

        public int GetTextureChannelCount(UMAGeneratorBase generator)
        {
            return material.channels.Length;
        }

        public override string ToString()
        {
            return "SlotData: " + slotName;
        }

        public void UpdateMeshData(SkinnedMeshRenderer meshRenderer, string rootBoneName, bool udimAdjustment, int submeshIndex, bool clearNormals, bool clearTangents)
        {
            meshData = new UMAMeshData();
            meshData.SlotName = this.slotName;
            meshData.RootBoneName = rootBoneName;
            meshData.RetrieveDataFromUnityMesh(meshRenderer,submeshIndex,udimAdjustment, clearNormals, clearTangents);
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


        [System.Serializable]
        public struct BakeSlotParams
        {
            public List<SlotBurnOptions> burnOptions;
            [Tooltip("If true, any blendshapes not listed in burnOptions will be copied to the new slot")]
            public bool copyUnbakedBlendshapes;
            [Tooltip("These shapes will be included even if not baked, if copyUnbakedBlendshapes is true. If copyUnbakedBlendshapes is true, and this is empty, then all shapes will be included.")]
            public List<string> ShapesToInclude; // Optional: if set, these shapes will be included even if not baked, if copyUnbakedBlendshapes is true
            [Tooltip("If >= 0, recalculate normals and tangents using this smoothing angle (in degrees). If < 0, do not recalculate. Do not set for multi-part models unless you want edges to have sharp normals")]
            public float smoothingAngleDegrees;
            [Tooltip("The new slotname")]
            public string newSlotName;          // Optional: rename baked slot asset
            [Tooltip("if true, this will be added to the indexer. If a slot with the same name already exists, it will be returned instead of creating a new one.")]
            public bool addToIndexer;           // Optional: register with UMAAssetIndexer
        }

        /// <summary>
        /// Bake a new SlotDataAsset from this asset using the provided parameters.
        /// </summary>
        public SlotDataAsset BakeNewSlotData(BakeSlotParams p)
        {
            // If there are bake targets but this asset has none of them, return the current slot unchanged
            if (p.burnOptions != null && p.burnOptions.Count > 0)
            {
                bool hasAny = false;
                if (meshData != null && meshData.blendShapes != null && meshData.blendShapes.Length > 0)
                {
                    // Build a small set of requested shape names
                    // Avoid LINQ to reduce allocs
                    for (int i = 0; i < meshData.blendShapes.Length && !hasAny; i++)
                    {
                        var bs = meshData.blendShapes[i];
                        if (bs == null || string.IsNullOrEmpty(bs.shapeName)) continue;
                        string shapeName = bs.shapeName;
                        for (int j = 0; j < p.burnOptions.Count; j++)
                        {
                            var opt = p.burnOptions[j];
                            if (opt == null || string.IsNullOrEmpty(opt.BlendShape)) continue;
                            if (opt.BlendShape == shapeName)
                            {
                                hasAny = true;
                                break;
                            }
                        }
                    }
                }
                if (!hasAny)
                {
                    return this;
                }
            }

            // If requested, and a slot with this name already exists in the indexer, return it immediately
            if (p.addToIndexer && !string.IsNullOrEmpty(p.newSlotName))
            {
                var indexer = UMAAssetIndexer.Instance;
                if (indexer != null)
                {
                    var existing = indexer.GetAsset<SlotDataAsset>(p.newSlotName, recursionGuard: false, inStartup: false);
                    if (existing != null)
                    {
                        return existing;
                    }
                }
            }

            // Create destination SlotDataAsset and copy metadata
            var newSlotData = ScriptableObject.CreateInstance<SlotDataAsset>();
            newSlotData.Assign(this);

            if (meshData == null)
            {
                newSlotData.meshData = null;
                return newSlotData;
            }

            // Deep copy the mesh so we don't mutate the source asset
            var md = meshData.DeepCopy();
            if (md == null)
            {
                newSlotData.meshData = null;
                return newSlotData;
            }

            // Blendshape bake phase
            if (md.blendShapes != null && md.blendShapes.Length > 0 && p.burnOptions != null && p.burnOptions.Count > 0)
            {
                // Build dictionary for SkinnedMeshCombiner baking API
                var bakeDict = new Dictionary<string, BlendShapeData>(p.burnOptions.Count);
                var bakedNames = new HashSet<string>();
                for (int i = 0; i < p.burnOptions.Count; i++)
                {
                    var opt = p.burnOptions[i];
                    if (opt == null || string.IsNullOrEmpty(opt.BlendShape)) continue;
                    if (!bakeDict.ContainsKey(opt.BlendShape))
                    {
                        bakeDict.Add(opt.BlendShape, new BlendShapeData { value = opt.value, isBaked = true });
                        bakedNames.Add(opt.BlendShape);
                    }
                }

                if (bakeDict.Count > 0)
                {
                    var verts = md.vertices;
                    var norms = md.normals;
                    var tans = md.tangents;
                    bool hasNormals = (norms != null && norms.Length == verts.Length);
                    bool hasTangents = (tans != null && tans.Length == verts.Length);

                    int vertexStart = 0;
                    var shapes = md.blendShapes;
                    for (int s = 0; s < shapes.Length; s++)
                    {
                        UMABlendShape shape = shapes[s];
                        if (shape == null) continue;
                        SkinnedMeshCombiner.BakeBlendShape(bakeDict, shape, ref vertexStart, verts, norms, tans, hasNormals, hasTangents);
                        vertexStart = 0;
                    }

                    // Assign modified arrays back (DeepCopy already gave us owned arrays)
                    md.vertices = verts;
                    if (hasNormals) md.normals = norms;
                    if (hasTangents) md.tangents = tans;

                    // Filter remaining blendshapes: remove those that were baked; optionally keep others
                    if (p.copyUnbakedBlendshapes)
                    {
                        var kept = new List<UMABlendShape>(shapes.Length);
                        bool includeAll = (p.ShapesToInclude == null || p.ShapesToInclude.Count == 0);
                        List<Regex> includePatterns = null;
                        if (!includeAll)
                        {
                            includePatterns = new List<Regex>(p.ShapesToInclude.Count);
                            for (int i = 0; i < p.ShapesToInclude.Count; i++)
                            {
                                var pattern = p.ShapesToInclude[i];
                                if (string.IsNullOrEmpty(pattern)) continue;
                                try
                                {
                                    includePatterns.Add(new Regex(pattern));
                                }
                                catch
                                {
                                    // Fallback to literal match if regex is invalid
                                    includePatterns.Add(new Regex(Regex.Escape(pattern)));
                                }
                            }
                        }

                        for (int i = 0; i < shapes.Length; i++)
                        {
                            var sh = shapes[i];
                            if (sh == null) continue;
                            if (bakedNames.Contains(sh.shapeName)) continue; // skip baked

                            if (includeAll)
                            {
                                kept.Add(sh);
                            }
                            else
                            {
                                bool match = false;
                                string name = sh.shapeName ?? string.Empty;
                                for (int r = 0; r < includePatterns.Count; r++)
                                {
                                    if (includePatterns[r].IsMatch(name))
                                    {
                                        match = true;
                                        break;
                                    }
                                }
                                if (match) kept.Add(sh);
                            }
                        }
                        md.blendShapes = kept.ToArray();
                    }
                    else
                    {
                        md.blendShapes = System.Array.Empty<UMABlendShape>();
                    }
                }
            }

            // Assign mesh to new slot
            newSlotData.meshData = md;

            // Recalculate normals/tangents if requested (angle >= 0)
            float angle = (p.smoothingAngleDegrees == 0f) ? 0f : (p.smoothingAngleDegrees == 0f ? 0f : p.smoothingAngleDegrees);
            if (p.smoothingAngleDegrees >= 0f && md.vertices != null && md.vertices.Length > 0 && md.submeshes != null && md.submeshes.Length > 0)
            {
                int vCount = md.vertices.Length;
                if (md.uv == null || md.uv.Length != vCount) md.uv = new Vector2[vCount];
                if (md.normals == null || md.normals.Length != vCount) md.normals = new Vector3[vCount];
                if (md.tangents == null || md.tangents.Length != vCount) md.tangents = new Vector4[vCount];

                int totalIdx = 0;
                for (int i = 0; i < md.submeshes.Length; i++)
                {
                    var tris = md.submeshes[i].getBaseTriangles();
                    if (tris != null) totalIdx += tris.Length;
                }
                if (totalIdx > 0 && (totalIdx % 3) == 0)
                {
                    int[] allTriangles = new int[totalIdx];
                    int write = 0;
                    for (int i = 0; i < md.submeshes.Length; i++)
                    {
                        var tris = md.submeshes[i].getBaseTriangles();
                        if (tris == null || tris.Length == 0) continue;
                        System.Array.Copy(tris, 0, allTriangles, write, tris.Length);
                        write += tris.Length;
                    }
#if UMA_BURSTCOMPILE
                    {
                        Unity.Collections.NativeArray<UnityEngine.Vector3> v = new Unity.Collections.NativeArray<UnityEngine.Vector3>(md.vertices, Unity.Collections.Allocator.TempJob);
                        Unity.Collections.NativeArray<UnityEngine.Vector3> n = new Unity.Collections.NativeArray<UnityEngine.Vector3>(md.normals, Unity.Collections.Allocator.TempJob);
                        Unity.Collections.NativeArray<UnityEngine.Vector2> uv = new Unity.Collections.NativeArray<UnityEngine.Vector2>(md.uv, Unity.Collections.Allocator.TempJob);
                        Unity.Collections.NativeArray<UnityEngine.Vector4> t = new Unity.Collections.NativeArray<UnityEngine.Vector4>(md.tangents, Unity.Collections.Allocator.TempJob);
                        Unity.Collections.NativeArray<int> tri = new Unity.Collections.NativeArray<int>(allTriangles, Unity.Collections.Allocator.TempJob);

                        var handle = UMA.MeshUtilities.RecalculateNormalsTangentsJobified(v, n, uv, t, tri, (p.smoothingAngleDegrees == 0f ? 180f : p.smoothingAngleDegrees));
                        handle.Complete();

                        md.normals = n.ToArray();
                        md.tangents = t.ToArray();

                        v.Dispose();
                        n.Dispose();
                        uv.Dispose();
                        t.Dispose();
                        tri.Dispose();
                        md.normalsModified = true;
                        md.tangentsModified = true;
                    }
#else
                    {
                        var mesh = new UnityEngine.Mesh();
                        if (UMA.UMAAssetIndexer.Instance != null && UMA.UMAAssetIndexer.Instance.Generator.Use32BitBuffers)
                        {
                            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                        }
                        mesh.vertices = md.vertices;
                        mesh.uv = md.uv;
                        mesh.subMeshCount = md.submeshes.Length;
                        for (int i = 0; i < md.submeshes.Length; i++)
                        {
                            var tris = md.submeshes[i].getBaseTriangles();
                            mesh.SetIndices(tris ?? System.Array.Empty<int>(), UnityEngine.MeshTopology.Triangles, i);
                        }
                        mesh.RecalculateNormals();
                        mesh.RecalculateTangents();
                        md.normals = mesh.normals;
                        md.tangents = mesh.tangents;
                        md.normalsModified = true;
                        md.tangentsModified = true;
                    }
#endif
                }
            }

            // Optional rename
            if (!string.IsNullOrEmpty(p.newSlotName))
            {
                newSlotData.slotName = p.newSlotName;
                newSlotData.nameHash = UMAUtils.StringToHash(newSlotData.slotName);
                if (newSlotData.meshData != null)
                {
                    newSlotData.meshData.SlotName = newSlotData.slotName;
                }
            }

            // Optional indexer registration
            if (p.addToIndexer)
            {
                var indexer = UMAAssetIndexer.Instance;
                if (indexer != null)
                {
                    indexer.ProcessNewItem(newSlotData, false, false);
                }
            }

            return newSlotData;
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
            animatedBones = source.animatedBones;
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
