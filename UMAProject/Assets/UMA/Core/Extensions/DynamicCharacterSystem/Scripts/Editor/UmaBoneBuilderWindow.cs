using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Editors
{
    public class UmaBoneBuilderWindow : EditorWindow 
    {
        public DynamicCharacterAvatar avatar;

        private UMAData _umaData;
        private Animator _animator;
        private int _umaBoneCount;
        private UMATransform[] _umaBones;

        [MenuItem("UMA/Bone Builder")]
        public static void OpenUmaTexturePrepareWindow()
        {
            UmaBoneBuilderWindow window = (UmaBoneBuilderWindow)EditorWindow.GetWindow(typeof(UmaBoneBuilderWindow));
            window.titleContent.text = "Bone Builder";
        }

        void OnGUI()
        {
            GUILayout.Label("UMA Bone Builder");
            GUILayout.Space(20);

            avatar = EditorGUILayout.ObjectField ("DynamicCharacterAvatar  ", avatar, typeof(DynamicCharacterAvatar), true) as DynamicCharacterAvatar;

            if (GUILayout.Button("Generate Bones"))
            {
                Debug.Log("Processing...");
                if (avatar == null)
                    Debug.Log ("DynamicCharacterAvatar not set!");
                else 
                {
                    InitializeUMAData ();
                    FindBones ();
                    EnsureRoot ();
                    CreateBoneTransforms ();
                    InitializeAnimator ();
                    Debug.Log ("Completed!");
                }
            }
        }

        private void InitializeUMAData()
        {
            if (avatar == null)
                return;

            if (avatar.activeRace.data == null)
                return;

            //Adds the umaData component
            avatar.Initialize ();

            if (avatar.umaData == null)
                return;

            _umaData = avatar.umaData;

            //Create a new recipe objects
            if ( _umaData.umaRecipe == null)
                _umaData.umaRecipe = new UMAData.UMARecipe ();

            avatar.umaRecipe = avatar.activeRace.racedata.baseRaceRecipe;
            avatar.umaRecipe.Load (_umaData.umaRecipe, avatar.context);
            _umaData.umaRecipe.raceData = avatar.activeRace.racedata;
            Debug.Log ("UMAData initialization successful!");
        }

        private void InitializeAnimator()
        {
            if (avatar == null)
                return;

            _animator = avatar.gameObject.GetComponent<Animator> ();
            if (_animator == null)
                _animator = avatar.gameObject.AddComponent<Animator> ();

            UMAGeneratorBase.SetAvatar (_umaData, _animator);
        }

        private void FindBones()
        {
            //get all the umaBones and umaBoneCount
            Dictionary<string, UMATransform> boneDict = new Dictionary<string, UMATransform> ();
            for (int i = 0; i < avatar.umaData.umaRecipe.slotDataList.Length; i++) 
            {
                if (avatar.umaData.umaRecipe.slotDataList [i] != null) {
                    for (int j = 0; j < avatar.umaData.umaRecipe.slotDataList [i].asset.meshData.umaBoneCount; j++) {
                        UMATransform bone = avatar.umaData.umaRecipe.slotDataList [i].asset.meshData.umaBones [j];
                        if (!boneDict.ContainsKey (bone.name))
                            boneDict.Add (bone.name, bone);
                    }
                }
            }

            _umaBoneCount = boneDict.Values.Count;
            _umaBones = new UMATransform[_umaBoneCount];
            boneDict.Values.CopyTo (_umaBones, 0);
        }

        private void EnsureRoot()
        {
            if (_umaData.umaRoot == null)
            {
                if (_umaData.gameObject.transform.Find ("Root") == null) 
                {
                    GameObject newRoot = new GameObject ("Root");
                    //make root of the UMAAvatar respect the layer setting of the UMAAvatar so cameras can just target this layer
                    newRoot.layer = _umaData.gameObject.layer;
                    newRoot.transform.parent = _umaData.transform;
                    newRoot.transform.localPosition = Vector3.zero;
                    newRoot.transform.localRotation = Quaternion.Euler (270f, 0, 0f);
                    newRoot.transform.localScale = Vector3.one;
                    _umaData.umaRoot = newRoot;
                } 
                else
                    _umaData.umaRoot = _umaData.gameObject.transform.Find ("Root").gameObject;

                if (_umaData.umaRoot.transform.Find ("Global") == null) 
                {
                    GameObject newGlobal = new GameObject ("Global");
                    newGlobal.transform.parent = _umaData.umaRoot.transform;
                    newGlobal.transform.localPosition = Vector3.zero;
                    newGlobal.transform.localRotation = Quaternion.Euler (90f, 90f, 0f);
                }
            }

            if (_umaData.skeleton == null) 
            {
                Transform globalTransform;
                globalTransform = _umaData.umaRoot.transform.FindChild ("Global");
                if (globalTransform != null) 
                {
                    _umaData.skeleton = new UMASkeleton (globalTransform);
                }
            }
        }

        private void CreateBoneTransforms()
        {
            for(int i = 0; i < _umaBoneCount; i++)
            {
                _umaData.skeleton.EnsureBone(_umaBones[i]);
            }
            _umaData.skeleton.EnsureBoneHierarchy();
        }
    }
}
