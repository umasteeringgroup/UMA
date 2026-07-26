//Based on jiggle bone code from Michael Cook (Fishypants), Adapted for UMA by Phil Taylor (DankP3).


using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Examples
{

    public class UMA_JiggleBelly : MonoBehaviour
	{

		//TODO, need to distinguish between male and female; need to calculate vectors for male rig; need to assign male and female vectors as required.

		//Controls for customisation
		public float _bellyStiffness = 0.15f;
		public float _bellyMass = 0.9f;
		public float _bellyDamping = 0.15f;
		public float _bellyGravity = 0f;
		public float _bellyInertia = 0.65f;
		public float _bellyMaxJiggleDistance = 0.2f;
		public float _bellyTargetDistance = 0.35f;
		public float _bellyPositionWeight = 1f;
		public float _bellyRotationWeight = 0.15f;
		public bool _bellySquashAndStretch = true;
		public float _bellyFrontStretch = 0.2f;
		public float _bellySideStretch = 0.15f;


		//merely a confirmation that the avatar has been created and jiggle bones are required
		private bool _initialized;

		//Reference to avatar and its componenets
		private DynamicCharacterAvatar _avatar;
		private SkinnedMeshRenderer _renderer;
		private string _skeleton = "other";
		private string _currentAvatar;

		// Scale is applied relative to the cached rest pose, which already includes DNA.
		private float _anatomyScaleFactor = 1;

		// Rest-pose state (cached once per character)
		private Vector3 _restLocalPosition = Vector3.zero;
		private Quaternion _restLocalRotation = Quaternion.identity;
		private Vector3 _restLocalScale = Vector3.one;
		private bool _restPoseInitialized;
		private bool _dynamicPositionInitialized;
		private Vector3 _previousTargetPosition = Vector3.zero;

		// Target and dynamic positions
		private Vector3 _targetPos;
		private Vector3 _dynamicPos;

		// Bone settings
		private Transform _monitoredBone;
		private Vector3 _boneAxis;

		//Dynamic variables for jiggle movements
		private Vector3 _force = new Vector3();
		private Vector3 _acceleration = new Vector3();
		private Vector3 _velocity = new Vector3();

		void Awake()
		{
			Init();
		}

		//Subscribe to mailing lists
		void OnEnable()
		{
			if (_avatar != null)
            {
                _avatar.CharacterUpdated.AddListener(AvatarUpdated);
            }
        }
		void OnDisable()
		{
			if (_avatar != null)
            {
                _avatar.CharacterUpdated.RemoveListener(AvatarUpdated);
            }
        }

		void Init()
		{
			_avatar = GetComponent<DynamicCharacterAvatar>();
			if (_avatar == null)
            {
                return;
            }

			_initialized = false;
			_skeleton = GetSkeleton(_avatar.activeRace.name);
			//Check if current skeleton is supported by jigglebone recipe and only run this code if the avatar has changed
			if (_skeleton != "other" && _avatar.activeRace.name != _currentAvatar)
			{
				_currentAvatar = _avatar.activeRace.name;
				_renderer = GetComponentInChildren<SkinnedMeshRenderer>();
				foreach (Transform bone in _renderer.bones)
				{
					//we are seeking by bone names so need the corresponding bone name from our supported skeletons
					if (bone.name == "LowerBackBelly" || bone.name == "BellyAdjust")
					{
						_monitoredBone = bone;
						_boneAxis = new Vector3(0, 0, 1);

						UpdateJiggleBone();
					}
				}
				if (_monitoredBone != null)
				{
					_initialized = true;
				}
			}
			else if (_skeleton != "other")
			{
				_anatomyScaleFactor = 1f;
				_restPoseInitialized = false;
				_dynamicPositionInitialized = false;
				_initialized = true;
			}

		}

		void AvatarUpdated(UMAData data)
		{
			Init();
		}

		private string GetSkeleton(string name)
		{
			if (name.Contains("o3n"))
			{
				return "o3n";
			}
			else
			{
				return "Standard";
			}
		}

		void InitializeBone()
		{
			InitializeRestPose();

			float targetDistance = Mathf.Max(_bellyTargetDistance, 0.001f);
			Vector3 restPosition = GetRestWorldPosition();
			Vector3 targetPos = restPosition + GetWorldBoneAxis(GetRestWorldRotation(), _boneAxis) * targetDistance;
			_dynamicPos = targetPos;
			_previousTargetPosition = targetPos;
			_force = Vector3.zero;
			_acceleration = Vector3.zero;
			_velocity = Vector3.zero;
			_dynamicPositionInitialized = true;
		}

		private void InitializeRestPose()
		{
			if (_restPoseInitialized || _monitoredBone == null)
			{
				return;
			}

			_restLocalPosition = _monitoredBone.localPosition;
			_restLocalRotation = _monitoredBone.localRotation;
			_restLocalScale = _monitoredBone.localScale;
			_restPoseInitialized = true;
		}

		private Vector3 GetRestWorldPosition()
		{
			Transform parent = _monitoredBone.parent;
			return parent != null ? parent.TransformPoint(_restLocalPosition) : _restLocalPosition;
		}

		private Quaternion GetRestWorldRotation()
		{
			Transform parent = _monitoredBone.parent;
			return parent != null ? parent.rotation * _restLocalRotation : _restLocalRotation;
		}

		private static Vector3 GetWorldBoneAxis(Quaternion boneRotation, Vector3 boneAxis)
		{
			Vector3 localAxis = boneAxis.sqrMagnitude > 0.000001f ? boneAxis.normalized : Vector3.forward;
			Vector3 worldAxis = boneRotation * localAxis;
			return worldAxis.sqrMagnitude > 0.000001f ? worldAxis.normalized : Vector3.forward;
		}

		public void UpdateJiggleBone()
		{
			_anatomyScaleFactor = 1f;
			InitializeBone();
		}

		void LateUpdate()
		{
			if (_initialized)
			{

				MonitorJiggling();


			}
		}

		private void MonitorJiggling()
		{
			//Get variables - only really need to set these if we have deviated from the defaults
			if (_monitoredBone == null)
			{
				return;
			}

			InitializeRestPose();

			Quaternion restRotation = GetRestWorldRotation();
			Vector3 bonePosition = GetRestWorldPosition();
			Vector3 worldAxis = GetWorldBoneAxis(restRotation, _boneAxis);
			float targetDistance = Mathf.Max(_bellyTargetDistance, 0.001f);
			_targetPos = bonePosition + worldAxis * targetDistance;
			_monitoredBone.localRotation = _restLocalRotation;

			if (!_dynamicPositionInitialized)
			{
				_dynamicPos = _targetPos;
				_velocity = Vector3.zero;
				_force = Vector3.zero;
				_acceleration = Vector3.zero;
				_previousTargetPosition = _targetPos;
				_dynamicPositionInitialized = true;
			}

			float stiffness = _bellyStiffness;
			float mass = Mathf.Max(_bellyMass, 0.0001f);
			float damping = _bellyDamping;
			float gravity = _bellyGravity;
			float inertia = _bellyInertia;
			float maxJiggleDistance = Mathf.Max(_bellyMaxJiggleDistance, 0.001f);
			float positionWeight = Mathf.Max(0f, _bellyPositionWeight);
			float rotationWeight = Mathf.Clamp01(_bellyRotationWeight);

			float simulationStep = Mathf.Clamp(Time.deltaTime > 0f ? Time.deltaTime * 60f : 1f, 0f, 2f);
			float dampingFactor = Mathf.Pow(Mathf.Clamp01(1f - damping), simulationStep);
			Vector3 targetDelta = _targetPos - _previousTargetPosition;
			if (targetDelta.sqrMagnitude > 0.000001f)
			{
				_velocity -= targetDelta * Mathf.Clamp01(inertia) * simulationStep;
			}

			_force = (_targetPos - _dynamicPos) * stiffness;
			_force += Vector3.down * (gravity / 10f);
			_acceleration = _force / mass;
			_velocity += _acceleration * simulationStep;
			_velocity *= dampingFactor;

			// Update dynamic position from velocity only. Force should not be applied directly to position.
			_dynamicPos += _velocity * simulationStep;
			Vector3 targetOffset = _dynamicPos - _targetPos;
			if (targetOffset.sqrMagnitude > maxJiggleDistance * maxJiggleDistance)
			{
				Vector3 clampedOffset = targetOffset.normalized * maxJiggleDistance;
				_dynamicPos = _targetPos + clampedOffset;
				_velocity = Vector3.ProjectOnPlane(_velocity, clampedOffset.normalized);
				targetOffset = clampedOffset;
			}

			_previousTargetPosition = _targetPos;

			Vector3 worldPositionOffset = targetOffset * positionWeight;
			Transform parent = _monitoredBone.parent;
			if (parent != null)
			{
				_monitoredBone.localPosition = _restLocalPosition + parent.InverseTransformVector(worldPositionOffset);
			}
			else
			{
				_monitoredBone.position = bonePosition + worldPositionOffset;
			}

			Vector3 movedBonePosition = parent != null ? parent.TransformPoint(_monitoredBone.localPosition) : _monitoredBone.position;
			Vector3 dynamicDirection = _dynamicPos - movedBonePosition;
			if (rotationWeight > 0f && dynamicDirection.sqrMagnitude > 0.000001f)
			{
				Quaternion jiggleRotation = Quaternion.FromToRotation(worldAxis, dynamicDirection.normalized);
				_monitoredBone.rotation = Quaternion.Slerp(Quaternion.identity, jiggleRotation, rotationWeight) * restRotation;
			}
			else
			{
				_monitoredBone.rotation = restRotation;
			}


			// ==================================================
			// Squash and Stretch section
			// ==================================================
			if (_bellySquashAndStretch)
			{
				// Create a vector from target position to dynamic position
				// We will measure the magnitude of the vector to determine
				// how much squash and stretch we will apply
				Vector3 dynamicVec = _dynamicPos - _targetPos;

				// Get the magnitude of the vector
				float stretchMag = dynamicVec.magnitude;

				// Here we determine the amount of squash and stretch based on stretchMag
				// and the direction the Bone Axis is pointed in. Ideally there should be
				// a vector with two values at 0 and one at 1. Like Vector3(0,0,1)
				// for the 0 values, we assume those are the sides, and 1 is the direction
				// the bone is facing
				float xStretch;
				float yStretch;
				float zStretch;
				if (_boneAxis.x == 0)
                {
                    xStretch = 1 + (-stretchMag * _bellySideStretch);
                }
                else
                {
                    xStretch = 1 + (stretchMag * _bellyFrontStretch);
                }

                if (_boneAxis.y == 0)
                {
                    yStretch = 1 + (-stretchMag * _bellySideStretch);
                }
                else
                {
                    yStretch = 1 + (stretchMag * _bellyFrontStretch);
                }

                if (_boneAxis.z == 0)
                {
                    zStretch = 1 + (-stretchMag * _bellySideStretch);
                }
                else
                {
                    zStretch = 1 + (stretchMag * _bellyFrontStretch);
                }

                // Set the bone scale
                _monitoredBone.localScale = Vector3.Scale(_restLocalScale, new Vector3(xStretch, yStretch, zStretch)) * _anatomyScaleFactor;
			}
			else
			{
				_monitoredBone.localScale = _restLocalScale * _anatomyScaleFactor;
			}

		}
		public void OnCharacterComplete(UMAData umaData)
		{
			UMA_JiggleBelly ujb = umaData.gameObject.GetComponent<UMA_JiggleBelly>();
			if (ujb == null)
			{
				ujb = umaData.gameObject.AddComponent<UMA_JiggleBelly>();
			}
			ujb._bellyStiffness = _bellyStiffness;
			ujb._bellyMass = _bellyMass;
			ujb._bellyDamping = _bellyDamping;
			ujb._bellyGravity = _bellyGravity;
			ujb._bellyInertia = _bellyInertia;
			ujb._bellyMaxJiggleDistance = _bellyMaxJiggleDistance;
			ujb._bellyTargetDistance = _bellyTargetDistance;
			ujb._bellyPositionWeight = _bellyPositionWeight;
			ujb._bellyRotationWeight = _bellyRotationWeight;
			ujb._bellySquashAndStretch = _bellySquashAndStretch;
			ujb._bellyFrontStretch = _bellyFrontStretch;
			ujb._bellySideStretch = _bellySideStretch;
		}
	}
}
