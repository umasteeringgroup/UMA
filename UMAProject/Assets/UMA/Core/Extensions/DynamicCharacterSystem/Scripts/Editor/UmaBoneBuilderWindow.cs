using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Editors
{
    public class UMABoneBuilderWindow : EditorWindow 
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
            UMABoneBuilderWindow window = (UMABoneBuilderWindow)EditorWindow.GetWindow(typeof(UMABoneBuilderWindow));
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

                if (_avatar.activeRace.data == null)
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

            baseRecipe.Load(_umaData.umaRecipe, UMAContextBase.FindInstance());
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
                if (_umaData.umaRecipe.slotDataList [i] != null)
				{
					foreach (UMATransform umaBone in _umaData.umaRecipe.slotDataList[i].asset.meshData.umaBones)
					{
						if (!boneDict.ContainsKey(umaBone.name))
							boneDict.Add(umaBone.name, umaBone);
                    }
                }
            }

            _umaBoneCount = boneDict.Values.Count;
            _umaBones = new UMATransform[_umaBoneCount];
            boneDict.Values.CopyTo (_umaBones, 0);
        }

        private void EnsureRoot()
        {
            if (_umaData.skeleton == null) 
            {
				_umaData.skeleton = new UMASkeleton(_umaData);
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
