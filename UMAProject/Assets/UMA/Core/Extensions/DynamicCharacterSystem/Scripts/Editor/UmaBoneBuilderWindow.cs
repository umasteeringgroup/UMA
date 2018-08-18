using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Editors
{
	/// <summary>
	///  This editor tool is used for generating the bone objects on a race so that they can be referenced in the editor.
	/// </summary>
    public class UmaBoneBuilderWindow : EditorWindow 
    {
        public GameObject umaObject;
        public UMARecipeBase baseRecipe;
        public bool removeUMAData = true;

        private UMAData _umaData;
        private Animator _animator;
        private int _umaBoneCount;
        private UMATransform[] _umaBones;

        private GameObject newUmaObj = null;
        private DynamicCharacterAvatar _avatar = null;

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
           
            newUmaObj = EditorGUILayout.ObjectField ("UMA GameObject  ", umaObject, typeof(GameObject), true) as GameObject;
            if (newUmaObj != umaObject)
            {
                umaObject = newUmaObj;
                if(newUmaObj != null)
                    _avatar = umaObject.GetComponent<DynamicCharacterAvatar>();                    
            }

            if (umaObject != null && _avatar == null)
            {
                EditorGUILayout.HelpBox("This UMA is not a DynamicCharacterAvatar so we need to supply the base recipe.", MessageType.Info);
                baseRecipe = EditorGUILayout.ObjectField("Base Recipe", baseRecipe, typeof(UMARecipeBase), false) as UMARecipeBase;
            }
            else
                baseRecipe = null;
            
            removeUMAData = EditorGUILayout.Toggle(new GUIContent("Remove UMAData", "A recipe and UMAData is created during the bone generation process, checking this will remove it at the end of the process. (Recommended)"), removeUMAData);

            if (GUILayout.Button("Generate Bones"))
            {
                if (umaObject == null)
                {
                    Debug.LogWarning ("UMA GameObject not set!");
                    return;
                }

                if (_avatar != null && _avatar.activeRace.data == null)
                {
                    Debug.LogWarning ("No recipe data found. Make sure the race is added to the library!");
                    return;
                }

                if (_avatar != null)
                    baseRecipe = _avatar.activeRace.data.baseRaceRecipe;

                if (baseRecipe == null)
                {
                    Debug.LogWarning("BaseRecipe not set!");
                    return;
                }

                Debug.Log("Processing...");
                InitializeUMAData ();
                FindBones ();
                EnsureRoot ();
                CreateBoneTransforms ();
                InitializeAnimator ();
                if( removeUMAData ) Cleanup();
                Debug.Log ("Completed!");
            }
        }

        private void InitializeUMAData()
        {
            if (umaObject == null)
                return;

            if (baseRecipe == null)
                return;

            //Adds the umaData component
            if (_umaData == null)
                _umaData = umaObject.AddComponent<UMAData>();

            if (_umaData == null)
                return;

            //Create a new recipe objects
            if ( _umaData.umaRecipe == null)
                _umaData.umaRecipe = new UMAData.UMARecipe ();

            baseRecipe.Load(_umaData.umaRecipe, UMAContext.FindInstance());
            Debug.Log ("UMAData initialization successful!");
        }

        private void InitializeAnimator()
        {
            if (umaObject == null)
                return;

            _animator = umaObject.gameObject.GetComponent<Animator> ();
            if (_animator == null)
                _animator = umaObject.gameObject.AddComponent<Animator> ();

            UMAGeneratorBase.SetAvatar (_umaData, _animator);
        }

        private void FindBones()
        {
            //get all the umaBones and umaBoneCount
            Dictionary<string, UMATransform> boneDict = new Dictionary<string, UMATransform> ();
            for (int i = 0; i < _umaData.umaRecipe.slotDataList.Length; i++) 
            {
                if (_umaData.umaRecipe.slotDataList [i] != null) {
                    for (int j = 0; j < _umaData.umaRecipe.slotDataList [i].asset.meshData.umaBoneCount; j++) {
                        UMATransform bone = _umaData.umaRecipe.slotDataList [i].asset.meshData.umaBones [j];
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
                globalTransform = _umaData.umaRoot.transform.Find ("Global");
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

        private void Cleanup()
        {
            if( _umaData )
                DestroyImmediate(_umaData);
        }
    }
}
