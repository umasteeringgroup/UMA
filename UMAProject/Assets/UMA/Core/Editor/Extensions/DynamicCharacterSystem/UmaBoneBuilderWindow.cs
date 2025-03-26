using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
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
		public bool saveAvatar = false;

        private UMAData _umaData;
        private Animator _animator;
        private int _umaBoneCount; 
        private UMATransform[] _umaBones;

        private GameObject newUmaObj = null;
        private DynamicCharacterAvatar _avatar = null;


		[UnityEditor.MenuItem("GameObject/UMA/Create Rig using Bone Builder", false,10)]
		[UnityEditor.MenuItem("CONTEXT/DynamicCharacterAvatar/Bone Builder")]
		public static void RunBoneBuilder()
		{
			UmaBoneBuilderWindow window = (UmaBoneBuilderWindow)EditorWindow.GetWindow(typeof(UmaBoneBuilderWindow));
			window.titleContent.text = "Bone Builder";
		}

		[UnityEditor.MenuItem("GameObject/UMA/Bone Builder",true, 10)]
		public static bool BoneBuilderValidate()
		{
			GameObject go = Selection.activeGameObject;
			if (go != null)
			{
				DynamicCharacterAvatar dca = go.GetComponent<DynamicCharacterAvatar>();
				if (dca != null)
				{
					return true;
				}
			}
			return false;
		}

		[MenuItem("UMA/Bone Builder", priority = 20)]
        public static void OpenUmaTexturePrepareWindow()
        {
            UmaBoneBuilderWindow window = (UmaBoneBuilderWindow)EditorWindow.GetWindow(typeof(UmaBoneBuilderWindow));
            window.titleContent.text = "Bone Builder";
        }

		void Awake()
		{
			GameObject go = Selection.activeGameObject;


			if (go != null)
			{
				DynamicCharacterAvatar dca = go.GetComponent<DynamicCharacterAvatar>();
				if (dca != null)
				{
					umaObject = go;
					_avatar = go.GetComponent<DynamicCharacterAvatar>();
				}
			}
			else
			{
			}
		}

		void OnGUI()
        {
            GUILayout.Label("UMA Bone Builder");
            GUILayout.Space(20);

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox("The bone builder should not be used while the scene is running.", MessageType.Info);
                return;
            }
           
            newUmaObj = EditorGUILayout.ObjectField ("UMA GameObject  ", umaObject, typeof(GameObject), true) as GameObject;

            //prevent being able to set set a prefab in the bone builder.  It will not work.
            if (newUmaObj != null && EditorUtility.IsPersistent(newUmaObj))
            {
                newUmaObj = null;
            }

            if (newUmaObj != umaObject)
            {
                umaObject = newUmaObj;
                if(newUmaObj != null)
                {
                    _avatar = umaObject.GetComponent<DynamicCharacterAvatar>();
                }
            }

            if (umaObject != null && _avatar == null)
            {
                EditorGUILayout.HelpBox("This UMA is not a DynamicCharacterAvatar so we need to supply the base recipe.", MessageType.Info);
                baseRecipe = EditorGUILayout.ObjectField("Base Recipe", baseRecipe, typeof(UMARecipeBase), false) as UMARecipeBase;
            }
            else
            {
                baseRecipe = null;
            }

            removeUMAData = EditorGUILayout.Toggle(new GUIContent("Remove UMAData", "A recipe and UMAData is created during the bone generation process, checking this will remove it at the end of the process. (Recommended)"), removeUMAData);

			// Currently, this produces an avatar with it's arms twisted. 
			// saveAvatar = EditorGUILayout.Toggle(new GUIContent("Save Mecanim Avatar", "This will save the Mecanim Avatar generated as an asset."), saveAvatar);
			GUILayout.Label("You can save the avatar at runtime using the option on the UMA/Runtime menu.", EditorStyles.wordWrappedMiniLabel);
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
                {
                    baseRecipe = _avatar.activeRace.data.baseRaceRecipe;
                }

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
                if( removeUMAData )
                {
                    Cleanup();
                }

                Debug.Log ("Completed!");
				this.Close();
            }
        }

        private void InitializeUMAData()
        {
            if (umaObject == null)
            {
                return;
            }

            if (baseRecipe == null)
            {
                return;
            }

            //Adds the umaData component
            if (_umaData == null)
            {
                _umaData = umaObject.AddComponent<UMAData>();
            }

            if (_umaData == null)
            {
                return;
            }

            //Create a new recipe objects
            if ( _umaData.umaRecipe == null)
            {
                _umaData.umaRecipe = new UMAData.UMARecipe ();
            }

            baseRecipe.Load(_umaData.umaRecipe, UMAContextBase.Instance);
            Debug.Log ("UMAData initialization successful!");
        }

        private void InitializeAnimator()
        {
            if (umaObject == null)
            {
                return;
            }

            UMAContextBase uc = UMAContextBase.Instance;

			if (uc == null)
			{
				return;
			}
			UMAGeneratorBase ugb = uc.gameObject.GetComponentInChildren<UMAGeneratorBase>();

			_animator = umaObject.gameObject.GetComponent<Animator> ();
            if (_animator == null)
            {
                _animator = umaObject.gameObject.AddComponent<Animator> ();
            }

            var umaTransform = umaObject.transform;
			var oldParent = umaTransform.parent;
			var originalRot = umaTransform.localRotation;
			var originalPos = umaTransform.localPosition;

			umaTransform.SetParent(null, false);
			umaTransform.localRotation = Quaternion.identity;
			umaTransform.localPosition = Vector3.zero;
			_umaData.KeepAvatar = false;

			UMAGeneratorBase.SetAvatar(_umaData, _animator);
			if (ugb != null)
			{ 
				ugb.UpdateAvatar(_umaData);
			}
			

			umaTransform.SetParent(oldParent, false);
			umaTransform.localRotation = originalRot;
			umaTransform.localPosition = originalPos;

			//if (saveAvatar)
			//	AssetDatabase.CreateAsset(_animator.avatar, "Assets/CreatedAvatar.asset");
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
                        {
                            boneDict.Add (bone.name, bone);
                        }
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
                {
                    _umaData.umaRoot = _umaData.gameObject.transform.Find ("Root").gameObject;
                }

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
                    UMAGeneratorBase umaGenerator = UMAContextBase.Instance.gameObject.GetComponentInChildren<UMAGeneratorBase>();
                    _umaData.skeleton = new UMASkeleton (globalTransform,umaGenerator);
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
            {
                DestroyImmediate(_umaData);
            }
        }
    }
}
