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
    }
}
