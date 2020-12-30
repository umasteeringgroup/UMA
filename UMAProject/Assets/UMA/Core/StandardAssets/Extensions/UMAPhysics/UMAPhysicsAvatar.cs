using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UMA.PoseTools;
using UMA.CharacterSystem;

namespace UMA.Dynamics
{
	public class UMAPhysicsAvatar : MonoBehaviour
	{
		// property to activate/deactivate ragdoll mode (exposed in editor by script "UMAPhysicsAvatarEditor.cs")
		public bool ragdolled
		{
			get	{ return _ragdolled; }
			set	{ SetRagdolled (value);	}
		}
		// Variable to store ragdoll state
		private bool _ragdolled = false;

		[Tooltip("Set this to true if you know the player will use a capsule collider and rigidbody")]
		public bool simplePlayerCollider = true;
		[Tooltip("Set this to have your body collider act as triggers when not ragdolled")]
		public bool enableColliderTriggers = false;

		[Tooltip("Experimental, for blending animations with physics")]
		[HideInInspector]
		[Range(0,1f)]
		public float ragdollBlendAmount;

		[Tooltip("Set this to snap the Avatar to the position of it's hip after ragdoll is finished")]
		public bool UpdateTransformAfterRagdoll = true;
		[Tooltip("Check this to set the player layer to the current layer, and read the 'ragdoll' layer from the settings")]
		public bool AutoSetLayers = false;
		[Tooltip("Layer to set the ragdoll colliders on. See layer based collision")]
		public int ragdollLayer = 8;
		[Tooltip("Layer to set the player collider on. See layer based collision")]
		public int playerLayer = 9;

		[Tooltip("List of Physics Elements, see UMAPhysicsElement class")]
		public List<UMAPhysicsElement> elements = new List<UMAPhysicsElement>();

		public UnityEvent onRagdollStarted;
		public UnityEvent onRagdollEnded;

		private DynamicCharacterAvatar _avatar;
		private UMAData _umaData;
		private GameObject _rootBone;
		private List<Rigidbody> _rigidbodies = new List<Rigidbody> ();
		private bool[] SaveRagdollStates = new bool[0];


		public List<BoxCollider> BoxColliders { get { return _BoxColliders; } }
		private List<BoxCollider> _BoxColliders = new List<BoxCollider> ();

		public List<ClothSphereColliderPair> SphereColliders { get { return _SphereColliders; }}
		private List<ClothSphereColliderPair> _SphereColliders = new List<ClothSphereColliderPair>();
		
		public List<CapsuleCollider> CapsuleColliders { get { return _CapsuleColliders; }}
		private List<CapsuleCollider> _CapsuleColliders = new List<CapsuleCollider>();

	
		private CapsuleCollider _playerCollider;
		private Rigidbody _playerRigidbody;

		struct CachedBone
		{
			public Transform boneTransform;
			public Vector3 localPosition;
			public Quaternion localRotation;
			public Vector3 localScale;

			public CachedBone(Transform transform)
			{
				boneTransform = transform;
				localPosition = transform.localPosition;
				localRotation = transform.localRotation;
				localScale = transform.localScale;
			}
		}
		private List<CachedBone> cachedBones = new List<CachedBone>();

		// Use this for initialization
		void Start () 
		{
			if (AutoSetLayers)
			{
				playerLayer = gameObject.layer;
				ragdollLayer = LayerMask.NameToLayer("Ragdoll");
			}
			else
			{
				gameObject.layer = playerLayer;
			}

			_avatar = GetComponent<DynamicCharacterAvatar>();
			//Using DCS
			if (_avatar != null)
			{
				_avatar.CharacterCreated.AddListener(OnCharacterCreatedCallback);
				_avatar.CharacterBegun.AddListener(OnCharacterBegunCallback);
				_avatar.CharacterUpdated.AddListener(OnCharacterUpdatedCallback);
			}
			else
			{
				//if we're not using the DCS then this will be created through a recipe
				_umaData = gameObject.GetComponent<UMAData>();

				if (_umaData != null)
				{
					_umaData.CharacterCreated.AddListener(OnCharacterCreatedCallback);
					_umaData.CharacterBegun.AddListener(OnCharacterBegunCallback);
					_umaData.CharacterUpdated.AddListener(OnCharacterUpdatedCallback);
				}
			}

			if (!Physics.GetIgnoreLayerCollision(ragdollLayer, playerLayer))
			{
				if (Debug.isDebugBuild)
					Debug.LogWarning("RagdollLayer and PlayerLayer are not ignoring each other! This will cause collision issues. Please update the collision matrix or 'Add Default Layers' in the Physics Slot Definition");
			}
		}

		void OnDestroy()
		{
			if (_avatar != null)
			{
				_avatar.CharacterCreated.RemoveListener(OnCharacterCreatedCallback);
				_avatar.CharacterBegun.RemoveListener(OnCharacterBegunCallback);
				_avatar.CharacterUpdated.RemoveListener(OnCharacterUpdatedCallback);
			}
			else
			{
				if (_umaData != null)
				{
					_umaData.CharacterCreated.RemoveListener(OnCharacterCreatedCallback);
					_umaData.CharacterBegun.RemoveListener(OnCharacterBegunCallback);
					_umaData.CharacterUpdated.RemoveListener(OnCharacterUpdatedCallback);
				}
			}
		}

		void FixedUpdate()
		{
			if (ragdollBlendAmount > 0) 
			{
				foreach (Rigidbody rigidbody in _rigidbodies) 
				{
					if (_rootBone && rigidbody.gameObject.name != _rootBone.name)
					{ //this if is to prevent us from modifying the root of the character, only the actual body parts
						//rotation is interpolated for all body parts
						rigidbody.transform.rotation = Quaternion.Slerp (rigidbody.transform.rotation, Quaternion.identity, ragdollBlendAmount);
					}
				}
			}
		}

		public void OnCharacterCreatedCallback(UMAData umaData)
		{
			CreatePhysicsObjects();
		}

		public void OnCharacterBegunCallback(UMAData umaData)
		{
			if (_ragdolled)
			{
				cachedBones.Clear();
				foreach (int hash in umaData.skeleton.BoneHashes)
				{
					Transform boneTransform = umaData.skeleton.GetBoneTransform(hash);
					if(boneTransform != null)
					{
						CachedBone cachedBone = new CachedBone(boneTransform);
						cachedBones.Add(cachedBone);
					}
				}
			}
		}

		public void OnCharacterUpdatedCallback(UMAData umaData)
		{
			if (_ragdolled)
			{
				foreach (CachedBone cachedbone in cachedBones)
				{
					cachedbone.boneTransform.localPosition = cachedbone.localPosition;
					cachedbone.boneTransform.localRotation = cachedbone.localRotation;
					cachedbone.boneTransform.localScale = cachedbone.localScale;
				}
				cachedBones.Clear();
			}
		}

		public void CreatePhysicsObjects()
		{
			if( _umaData == null )
				_umaData = gameObject.GetComponent<UMAData> ();	

			if (_umaData == null) 
			{
				if (Debug.isDebugBuild)
					Debug.LogError ("CreatePhysicsObjects: umaData is null!");
				return;
			}

			SetRendereroffscreenStates();

			//Don't update if we already have a rigidbody on the root bone?
			if ( _rootBone && _rootBone.GetComponent<Rigidbody> () )
				return;

			if (simplePlayerCollider) 
			{
				_playerCollider = gameObject.GetComponent<CapsuleCollider> ();
				_playerRigidbody = gameObject.GetComponent<Rigidbody> ();
				if (_playerCollider == null || _playerRigidbody == null)
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("PlayerCollider or PlayerRigidBody is null, try putting the collider recipe before the PhysicsRecipe, or turn off SimplePlayerCollider.");
				}
			}

			foreach (UMAPhysicsElement element in elements) 
			{
				if (element != null) 
				{
					// add Generic Info
					GameObject bone = _umaData.GetBoneGameObject (element.boneName);

                    if (bone == null)
                    {
						if (Debug.isDebugBuild)
							Debug.LogWarning("UMAPhysics: " + element.boneName + " not found!");
                        continue; //if we don't find the bone then go to the next iteration
                    }
                
                    if (!bone.GetComponent<Rigidbody>())
                    {
                        Rigidbody rigidBody = bone.AddComponent<Rigidbody>();
                        rigidBody.isKinematic = true;
                        rigidBody.mass = element.mass;
                        _rigidbodies.Add(rigidBody);
                    }

                    bone.layer = ragdollLayer;

                    foreach (ColliderDefinition collider in element.colliders)
                    {
                        // Add Appropriate Collider
                        if (collider.colliderType == ColliderDefinition.ColliderType.Box)
                        {
                            BoxCollider boxCollider = bone.AddComponent<BoxCollider>();
                            boxCollider.center = collider.colliderCentre;
                            boxCollider.size = collider.boxDimensions;
                            boxCollider.isTrigger = false; //Set initially to false;
                            _BoxColliders.Add(boxCollider);
                        }
                        else if (collider.colliderType == ColliderDefinition.ColliderType.Sphere)
                        {
                            SphereCollider sphereCollider = bone.AddComponent<SphereCollider>();
                            sphereCollider.center = collider.colliderCentre;
                            sphereCollider.radius = collider.sphereRadius;
                            sphereCollider.isTrigger = false; //Set initially to false;

                            _SphereColliders.Add(new ClothSphereColliderPair(sphereCollider));
                        }
                        else if (collider.colliderType == ColliderDefinition.ColliderType.Capsule)
                        {
                            CapsuleCollider capsuleCollider = bone.AddComponent<CapsuleCollider>();
                            capsuleCollider.center = collider.colliderCentre;
                            capsuleCollider.radius = collider.capsuleRadius;
                            capsuleCollider.height = collider.capsuleHeight;
                            capsuleCollider.isTrigger = false; //Set initially to false;
                            switch (collider.capsuleAlignment)
                            {
                                case(ColliderDefinition.Direction.X):
                                    capsuleCollider.direction = 0;
                                    break;
                                case(ColliderDefinition.Direction.Y):
                                    capsuleCollider.direction = 1;
                                    break;
                                case(ColliderDefinition.Direction.Z):
                                    capsuleCollider.direction = 2;
                                    break;
                                default:
                                    capsuleCollider.direction = 0;
                                    break;
                            }
                            _CapsuleColliders.Add(capsuleCollider);
                        }
                    }
				}
			}

			//Second pass to make sure Rigidbodies are all created
			foreach (UMAPhysicsElement element in elements) 
			{
				if (element != null) 
				{
					// Make Temp SoftJoint
					SoftJointLimit tempLimit = new SoftJointLimit ();

					GameObject bone = _umaData.GetBoneGameObject (element.boneName);

                    if (bone == null)
                        continue; //if we don't find the bone then go to the next iteration
                    
					// Add Character Joint
					if (!element.isRoot) {
						CharacterJoint joint = bone.AddComponent<CharacterJoint> ();
						_rootBone = bone;
						joint.connectedBody = _umaData.GetBoneGameObject(element.parentBone).GetComponent<Rigidbody> (); // possible error if parent not yet created.
						joint.axis = element.axis;
						joint.swingAxis = element.swingAxis;	
						tempLimit.limit = element.lowTwistLimit;
						joint.lowTwistLimit = tempLimit;
						tempLimit.limit = element.highTwistLimit;
						joint.highTwistLimit = tempLimit;
						tempLimit.limit = element.swing1Limit;
						joint.swing1Limit = tempLimit;
						tempLimit.limit = element.swing2Limit;
						joint.swing2Limit = tempLimit;
						joint.enablePreprocessing = element.enablePreprocessing;
					}
				}
			}

			UpdateClothColliders ();
			SetRagdolled (_ragdolled);
		}

		//Update all cloth components
		public void UpdateClothColliders()
		{
			if (_umaData) 
			{
				foreach (Renderer renderer in _umaData.GetRenderers()) 
				{
					Cloth cloth = renderer.GetComponent<Cloth> ();
					if (cloth) 
					{
                        cloth.sphereColliders = SphereColliders.ToArray();
                        cloth.capsuleColliders = CapsuleColliders.ToArray();
						if ((cloth.capsuleColliders.Length + cloth.sphereColliders.Length) > 10)
						{
							if (Debug.isDebugBuild)
								Debug.LogWarning("Cloth Collider count is high. You might experience strange behavior with the cloth simulation.");
						}
					}
				}
			}
		}

		private void SetRagdolled(bool ragdollState)
		{
            if (!Application.isPlaying)
            {
                _ragdolled = false;
                return;
            }
            
			//Player Collider stuff
			//Call Player Collider enable/disable event here
			if (ragdollState) 
			{
				if (onRagdollStarted != null )
					onRagdollStarted.Invoke ();
			}
			else 
			{
				if ( onRagdollEnded != null )
					onRagdollEnded.Invoke ();
			}
				
			if (simplePlayerCollider) 
			{
				if( _playerRigidbody )
					_playerRigidbody.isKinematic = ragdollState;

				if( _playerCollider )
					_playerCollider.enabled = !ragdollState;
			}

			// iterate through all rigidbodies and switch kinematic mode on/off
			//Set all rigidbodies.isKinematic to opposite of ragdolled state
			SetAllKinematic( !ragdollState );

			if( enableColliderTriggers ) //Change the trigger state on collider if we enable this flag.
				SetBodyColliders( !ragdollState );
				
			// switch animator on/off
			Animator animator = GetComponent<Animator>();
			if( animator != null )
				animator.enabled = !ragdollState;	
			// switch expression player (locks head if left on)
			ExpressionPlayer expressionPlayer = GetComponent<ExpressionPlayer>();
			if( expressionPlayer != null )
				expressionPlayer.enabled = !ragdollState;
				
			// Prevent Mismatched Culling
			// Skinned mesh renderers cull based on their origonal position before ragdolling.
			// We use this property to prevent ragdolled meshes from popping in and out unexpectedly.
			SetUpdateWhenOffscreen( ragdollState );

			if (_ragdolled && !ragdollState) 
			{
				//We were ragdolled and now we're not
				if (UpdateTransformAfterRagdoll) 
				{
					gameObject.transform.position = _rootBone.transform.position;
				}
			}

			_ragdolled = ragdollState;
		}

		private void SetAllKinematic(bool flag)
		{
			foreach (Rigidbody rigidbody in _rigidbodies)
			{
				if (rigidbody != null)
				{
					rigidbody.isKinematic = flag;
				}
				//rigidbody.detectCollisions = !flag;
			}
		}

		private void SetBodyColliders(bool flag)
		{
			foreach (BoxCollider collider in _BoxColliders) 
			{
				collider.isTrigger = flag;
				//collider.enabled = flag;
			}

            foreach (ClothSphereColliderPair collider in _SphereColliders) 
			{
                collider.first.isTrigger = flag;
                //collider.second.isTrigger = flag;
				//collider.first.enabled = flag;
                //collider.second.enabled = flag;
			}
			
			foreach (CapsuleCollider collider in _CapsuleColliders) 
			{
				collider.isTrigger = flag;
				//collider.enabled = flag;
			}
		}

		private void SetRendereroffscreenStates()
		{
			if (_umaData != null)
			{
				SkinnedMeshRenderer[] renderers = _umaData.GetRenderers();
				if (renderers != null)
				{
					if (SaveRagdollStates.Length != renderers.Length)
					{
						SaveRagdollStates = new bool[renderers.Length];
					}

					for(int i = 0; i < renderers.Length; i++)
					{
						SkinnedMeshRenderer smr = renderers[i];
						SaveRagdollStates[i] = smr.updateWhenOffscreen;
					}
				}
			}
		}

		private void SetUpdateWhenOffscreen(bool flag)
		{
			if (_umaData != null) 
			{
				SkinnedMeshRenderer[] renderers = _umaData.GetRenderers ();
				if (renderers != null) 
				{
					// if we've saved the states, we can't just turn it off. might be on by default.
					if (SaveRagdollStates.Length == renderers.Length && !flag)
					{
						for (int i = 0; i < renderers.Length; i++)
						{
							SkinnedMeshRenderer smr = renderers[i];
							smr.updateWhenOffscreen = SaveRagdollStates[i];
						}
					}
					else
					{
						foreach (SkinnedMeshRenderer renderer in renderers)
							renderer.updateWhenOffscreen = flag;
					}
				}
			}
		}
	}
}
