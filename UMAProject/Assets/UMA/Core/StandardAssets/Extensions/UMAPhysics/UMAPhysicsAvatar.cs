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
		[Tooltip("Move each skinned renderer's existing bounds with the ragdoll root bone. Keep this enabled when ragdoll bones can move away from the avatar root.")]
		public bool UpdateRendererBoundsWhileRagdolled = true;
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

		private sealed class RagdollRendererState
		{
			public SkinnedMeshRenderer renderer;
			public bool updateWhenOffscreen;
			public Bounds localBounds;
			public Bounds ragdollBounds;
		}

		private readonly List<RagdollRendererState> _ragdollRendererStates =
			new List<RagdollRendererState>();


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
#if UNITY_EDITOR
                if (Debug.isDebugBuild)
                {
                    Debug.LogWarning("RagdollLayer and PlayerLayer are not ignoring each other! This will cause collision issues. Please update the collision matrix or 'Add Default Layers' in the Physics Slot Definition");
                }
#endif
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

			_ragdollRendererStates.Clear();
		}

		void FixedUpdate()
		{
			if (ragdollBlendAmount > 0) 
			{
                for (int i = 0; i < _rigidbodies.Count; i++) 
				{
                    Rigidbody rigidbody = _rigidbodies[i];
                    if (_rootBone && rigidbody.gameObject.name != _rootBone.name)
					{ //this if is to prevent us from modifying the root of the character, only the actual body parts
						//rotation is interpolated for all body parts
						rigidbody.transform.rotation = Quaternion.Slerp (rigidbody.transform.rotation, Quaternion.identity, ragdollBlendAmount);
					}
				}
			}
		}

		void LateUpdate()
		{
			if (_ragdolled && UpdateRendererBoundsWhileRagdolled)
			{
				UpdateRagdollRendererBounds();
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
                for (int i = 0; i < cachedBones.Count; i++)
				{
                    CachedBone cachedbone = cachedBones[i];
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
            {
                _umaData = gameObject.GetComponent<UMAData> ();
            }

            if (_umaData == null) 
			{
				if (Debug.isDebugBuild)
                {
                    Debug.LogError ("CreatePhysicsObjects: umaData is null!");
                }

                return;
			}

			SetRendereroffscreenStates();

			//Don't update if we already have a rigidbody on the root bone?
			if ( _rootBone && _rootBone.GetComponent<Rigidbody> () )
            {
                return;
            }

            if (simplePlayerCollider) 
			{
				_playerCollider = gameObject.GetComponent<CapsuleCollider> ();
				_playerRigidbody = gameObject.GetComponent<Rigidbody> ();
				if (_playerCollider == null || _playerRigidbody == null)
				{
#if UNITY_EDITOR					
					if (Debug.isDebugBuild)
                    {
                        Debug.Log("Information: PlayerCollider or PlayerRigidBody is null, and SimplePlayerCollider is enabled. " +
						 "Try putting the collider recipe before the PhysicsRecipe, or turn off SimplePlayerCollider. This message is editor-only.");
                    }
#endif
                }
			}

            for (int i = 0; i < elements.Count; i++) 
			{
                UMAPhysicsElement element = elements[i];
                if (element != null) 
				{
					// add Generic Info
					GameObject bone = _umaData.GetBoneGameObject (element.boneName);

                    if (bone == null)
                    {
#if UNITY_EDITOR
						if (Debug.isDebugBuild)
                        {
                            Debug.LogWarning("UMAPhysics: " + element.boneName + " not found!");
                        }
#endif

						continue; //if we don't find the bone then go to the next iteration
                    }

					if (element.isRoot)
					{
						_rootBone = bone;
					}
                
                    if (!bone.GetComponent<Rigidbody>())
                    {
                        Rigidbody rigidBody = bone.AddComponent<Rigidbody>();
                        rigidBody.isKinematic = true;
                        rigidBody.mass = element.mass;
                        _rigidbodies.Add(rigidBody);
                    }

                    bone.layer = ragdollLayer;

                    for (int i1 = 0; i1 < element.colliders.Length; i1++)
                    {
                        ColliderDefinition collider = element.colliders[i1];
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
            for (int i = 0; i < elements.Count; i++) 
			{
                UMAPhysicsElement element = elements[i];
                if (element != null) 
				{
					// Make Temp SoftJoint
					SoftJointLimit tempLimit = new SoftJointLimit ();

					GameObject bone = _umaData.GetBoneGameObject (element.boneName);

                    if (bone == null)
                    {
                        continue; //if we don't find the bone then go to the next iteration
                    }

                    // Add Character Joint
                    if (!element.isRoot) {
						CharacterJoint joint = bone.AddComponent<CharacterJoint> ();
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
                SkinnedMeshRenderer[] array = _umaData.GetRenderers();
                for (int i = 0; i < array.Length; i++) 
				{
                    Renderer renderer = array[i];
                    Cloth cloth = renderer.GetComponent<Cloth> ();
					if (cloth) 
					{
                        cloth.sphereColliders = SphereColliders.ToArray();
                        cloth.capsuleColliders = CapsuleColliders.ToArray();
#if UNITY_EDITOR
						if ((cloth.capsuleColliders.Length + cloth.sphereColliders.Length) > 10)
						{
							if (Debug.isDebugBuild)
                            {
                                Debug.LogWarning("Cloth Collider count is high. You might experience strange behavior with the cloth simulation.");
                            }
                        }
#endif
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
            
			// Capture the generated/manual renderer settings immediately before
			// entering ragdoll. UMA assigns localBounds during generation, which
			// makes them a custom override until ResetLocalBounds is called.
			if (ragdollState && !_ragdolled)
			{
				SetRendereroffscreenStates();
			}

			//Player Collider stuff
			//Call Player Collider enable/disable event here
			if (ragdollState) 
			{
				if (onRagdollStarted != null )
                {
                    onRagdollStarted.Invoke ();
                }
            }
			else 
			{
				if ( onRagdollEnded != null )
                {
                    onRagdollEnded.Invoke ();
                }
            }
				
			if (simplePlayerCollider) 
			{
				if( _playerRigidbody )
                {
                    _playerRigidbody.isKinematic = ragdollState;
                }

                if ( _playerCollider )
                {
                    _playerCollider.enabled = !ragdollState;
                }
            }

			// iterate through all rigidbodies and switch kinematic mode on/off
			//Set all rigidbodies.isKinematic to opposite of ragdolled state
			SetAllKinematic( !ragdollState );

			if( enableColliderTriggers ) //Change the trigger state on collider if we enable this flag.
            {
                SetBodyColliders( !ragdollState );
            }

            // switch animator on/off
            Animator animator = GetComponent<Animator>();
			if( animator != null )
            {
                animator.enabled = !ragdollState;
            }
            // switch expression player (locks head if left on)
            ExpressionPlayer expressionPlayer = GetComponent<ExpressionPlayer>();
			if( expressionPlayer != null )
            {
                expressionPlayer.enabled = !ragdollState;
            }

			// Keep a conservative custom bound centered on the physics root while
			// ragdolled, then restore UMA's exact generated/manual renderer state.
			SetRagdollRendererState(ragdollState);

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
            for (int i = 0; i < _rigidbodies.Count; i++)
			{
                Rigidbody rigidbody = _rigidbodies[i];
                if (rigidbody != null)
				{
					rigidbody.isKinematic = flag;
				}
				//rigidbody.detectCollisions = !flag;
			}
		}

		private void SetBodyColliders(bool flag)
		{
            for (int i = 0; i < _BoxColliders.Count; i++) 
			{
                BoxCollider collider = _BoxColliders[i];
                collider.isTrigger = flag;
				//collider.enabled = flag;
			}

            for (int i = 0; i < _SphereColliders.Count; i++) 
			{
                ClothSphereColliderPair collider = _SphereColliders[i];
                collider.first.isTrigger = flag;
                //collider.second.isTrigger = flag;
				//collider.first.enabled = flag;
                //collider.second.enabled = flag;
			}

            for (int i = 0; i < _CapsuleColliders.Count; i++) 
			{
                CapsuleCollider collider = _CapsuleColliders[i];
                collider.isTrigger = flag;
				//collider.enabled = flag;
			}
		}

		private void SetRendereroffscreenStates()
		{
			if (_umaData == null)
			{
				return;
			}

			SkinnedMeshRenderer[] renderers = _umaData.GetRenderers();
			if (renderers == null)
			{
				return;
			}

			// UMA can replace its renderers when a character is regenerated. Remove
			// stale entries by object reference so renderer order and count changes
			// cannot restore one renderer's settings onto another renderer.
			for (int i = _ragdollRendererStates.Count - 1; i >= 0; i--)
			{
				RagdollRendererState state = _ragdollRendererStates[i];
				if (state.renderer == null || !ContainsRenderer(renderers, state.renderer))
				{
					_ragdollRendererStates.RemoveAt(i);
				}
			}

			for (int i = 0; i < renderers.Length; i++)
			{
				SkinnedMeshRenderer renderer = renderers[i];
				if (renderer == null)
				{
					continue;
				}

				RagdollRendererState state = FindRendererState(renderer);
				bool created = state == null;
				if (state == null)
				{
					state = new RagdollRendererState
					{
						renderer = renderer
					};
					_ragdollRendererStates.Add(state);
				}

				// Do not replace the pre-ragdoll values if regeneration reports a
				// renderer that was already being tracked during the ragdoll.
				if (!_ragdolled || created)
				{
					state.updateWhenOffscreen = renderer.updateWhenOffscreen;
					state.localBounds = renderer.localBounds;
					state.ragdollBounds = CubifyBounds(state.localBounds);
				}
			}
		}

		private void SetRagdollRendererState(bool active)
		{
			if (_umaData == null)
			{
				return;
			}

			SkinnedMeshRenderer[] renderers = _umaData.GetRenderers ();
			if (renderers == null)
			{
				return;
			}

			for (int i = 0; i < renderers.Length; i++)
			{
				SkinnedMeshRenderer renderer = renderers[i];
				if (renderer == null)
				{
					continue;
				}

				RagdollRendererState state = FindRendererState(renderer);
				if (active)
				{
					if (UpdateRendererBoundsWhileRagdolled)
					{
						// updateWhenOffscreen lets Unity replace a custom localBounds with
						// its internally calculated mesh bounds. The hip-centered bound is
						// maintained explicitly, so forced offscreen skinning is unnecessary.
						renderer.updateWhenOffscreen = false;
					}
					else
					{
						// Preserve the legacy behavior when explicit ragdoll bounds have
						// been disabled by the user.
						renderer.ResetLocalBounds();
						renderer.updateWhenOffscreen = true;
					}
				}
				else if (state != null)
				{
					renderer.updateWhenOffscreen = state.updateWhenOffscreen;
					// Changing updateWhenOffscreen can reset localBounds, so restore
					// the flag first and UMA's exact authored bounds last.
					renderer.localBounds = state.localBounds;
				}
				else
				{
					// A renderer created after state capture has no authored state to
					// restore. Return it to Unity's automatic bounds behavior.
					renderer.ResetLocalBounds();
					renderer.updateWhenOffscreen = false;
				}
			}

			if (active && UpdateRendererBoundsWhileRagdolled)
			{
				UpdateRagdollRendererBounds();
			}
		}

		private void UpdateRagdollRendererBounds()
		{
			if (_umaData == null)
			{
				return;
			}

			SkinnedMeshRenderer[] renderers = _umaData.GetRenderers();
			if (renderers == null)
			{
				return;
			}

			for (int i = 0; i < renderers.Length; i++)
			{
				SkinnedMeshRenderer renderer = renderers[i];
				if (renderer == null)
				{
					continue;
				}

				RagdollRendererState state = FindRendererState(renderer);
				if (state == null)
				{
					// A regeneration can replace renderers while physics remains active.
					// Capture the new renderer so it participates immediately and can be
					// returned to its current generated settings on ragdoll exit.
					state = new RagdollRendererState
					{
						renderer = renderer,
						updateWhenOffscreen = renderer.updateWhenOffscreen,
						localBounds = renderer.localBounds,
						ragdollBounds = CubifyBounds(renderer.localBounds)
					};
					_ragdollRendererStates.Add(state);
				}

				Transform referenceBone = GetBoundsReferenceBone(renderer);
				if (referenceBone == null)
				{
					continue;
				}

				Bounds movedBounds = state.ragdollBounds;
				movedBounds.center = renderer.transform.InverseTransformPoint(
					referenceBone.position);
				if (!IsFinite(movedBounds))
				{
					continue;
				}
				renderer.localBounds = movedBounds;
			}
		}

		private Transform GetBoundsReferenceBone(
			SkinnedMeshRenderer renderer)
		{
			if (_rootBone != null)
			{
				return _rootBone.transform;
			}
			return renderer.rootBone;
		}

		private static Bounds CubifyBounds(Bounds source)
		{
			float cubeSize = Mathf.Max(
				source.size.x,
				Mathf.Max(source.size.y, source.size.z));
			Bounds cube = source;
			cube.size = Vector3.one * cubeSize;
			return cube;
		}

		private RagdollRendererState FindRendererState(
			SkinnedMeshRenderer renderer)
		{
			for (int i = 0; i < _ragdollRendererStates.Count; i++)
			{
				if (_ragdollRendererStates[i].renderer == renderer)
				{
					return _ragdollRendererStates[i];
				}
			}
			return null;
		}

		private static bool ContainsRenderer(
			SkinnedMeshRenderer[] renderers,
			SkinnedMeshRenderer renderer)
		{
			for (int i = 0; i < renderers.Length; i++)
			{
				if (renderers[i] == renderer)
				{
					return true;
				}
			}
			return false;
		}

		private static bool IsFinite(Bounds bounds)
		{
			return IsFinite(bounds.center.x) &&
				IsFinite(bounds.center.y) &&
				IsFinite(bounds.center.z) &&
				IsFinite(bounds.size.x) &&
				IsFinite(bounds.size.y) &&
				IsFinite(bounds.size.z);
		}

		private static bool IsFinite(float value)
		{
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}

	}
}
