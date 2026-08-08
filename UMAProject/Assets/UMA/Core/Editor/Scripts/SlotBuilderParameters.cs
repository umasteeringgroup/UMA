using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors
{
    public struct SlotBuilderParameters 
    {
        public bool udimAdjustment;
        public bool calculateNormals;
        public bool calculateTangents;
        public bool binarySerialization;
        public bool useRootFolder;
        public bool nameByMaterial;
        public bool keepAllBones;
        public bool alwaysRecreateSlots;
        public bool findAndUpdateExistingSlot;
        public bool createOverlays;
        public bool createRecipe;
        public bool isBaseRaceRecipe;
        public bool addToGlobalLibrary; // new
        public bool appendTypeToName; // new
        public bool addUDIMTileNumbers; // new

        public string stripBones;
        public string rootBone;
        public string assetName;
        public string slotName;
        public string assetFolder;
        public string slotFolder;

        public List<string> keepList;
        public SkinnedMeshRenderer slotMesh;
        public SkinnedMeshRenderer seamsMesh;
        public UMAMaterial material;
        public Quaternion rotation;
        public bool rotationEnabled;
        public bool invertX;
        public bool invertY;
        public bool invertZ;
        public bool clearNormals;
        public bool clearTangents;
        public bool batchMode;

        // UDIM grid dimensions (e.g., 10x10). If 0, defaults will be used.
        public int udimTilesU;
        public int udimTilesV;

        // Optional: persisted asset paths for reference/debugging
        public string slotMaterialPath;
        public string slotFolderPath;

        // Optional: weld UDIM seam normals/tangents across split tiles by averaging
        public bool weldUdimNormals;

        // Optional: generate internal per-slot LOD triangle buffers/ranges using SlotLodGenerator.
        // When enabled, Unity mesh LOD copying is skipped.
        public bool generateSlotLods;
        public bool useUnityLodGenerator;
        public int slotLodMaxLevels;
        public int slotLodMinTriangles;
        public float slotLodTargetReductionPerLevel;
        public bool slotLodPreserveBoundaryEdges;
        public float slotLodBoundaryWeight;
    }
}
