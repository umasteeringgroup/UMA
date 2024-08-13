#define USING_BAKEMESH
using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEngine;


namespace UMA
{
    public class UMAUVAttachedItemLauncher : MonoBehaviour
    {
        // TODO:
        // Should take an array of UV, and save an array of vertex indexes.
        // and a array of prefabs...

        public DynamicCharacterAvatar avatar;
        public Vector2 uVLocation;
        public Vector2 uVUp;
        public string slotName;
        public Quaternion rotation;
        public Vector3 normalAdjust;
        public Vector3 translation;
        public GameObject prefab;
        public string boneName;
        public SlotData sourceSlot;
        public bool useMostestBone;
        [Tooltip("The UV set to use for the attached item")]
        [Range(0, 3)]
        public int UVSet = 0;

        private GameObject prefabInstance;
        public int VertexNumber;
        public int subMeshNumber;
        public List<int> triangle = new List<int>();
        public SkinnedMeshRenderer skin;
        private Mesh tempMesh;
        private UMAUVAttachedItem bootStrapper;
        private UMAData umaData;
        private Transform mostestBone;
        
		public List<UMAUVAttachedItemBlendshapeAdjuster> blendshapeAdjusters = new List<UMAUVAttachedItemBlendshapeAdjuster>();

        public bool worldTransform;

        public void Start()
        {
            Debug.Log($"Start {GetInstanceID()}");
        }

        public void OnSlotProcessed(UMAData umaData, SlotData slotData)
        {
            Debug.Log("SlotProcessed: " + slotData.slotName);
            this.sourceSlot = slotData;
        }


        public void Setup(UMAData umaData, bool Activate)
        {
#if UNITY_EDITOR
            if (!UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }
#endif
            Debug.Log($"DNA Applied {GetInstanceID()}");

            var uvam = umaData.gameObject.GetComponent<UMAUVAttachedItemManager>();
            if (!uvam)
            {
                uvam = umaData.gameObject.AddComponent<UMAUVAttachedItemManager>();
                uvam.Setup(umaData);
            }
            uvam.AddAttachedItem(umaData, this, Activate);
        }

        public void OnDnaAppliedBootstrapper(UMAData umaData)
        {
            Setup(umaData, true);
        }
    }
}

