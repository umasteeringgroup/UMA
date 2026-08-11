//Based on jiggle bone code from Michael Cook (Fishypants), Adapted for UMA by Phil Taylor (DankP3).


using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA.Examples
{

    public class UMA_JiggleBreasts : MonoBehaviour
	{

		//TODO, need to distinguish between male and female; need to calculate vectors for male rig; need to assign male and female vectors as required.

		//Controls for customisation
		public float _breastStiffness = 0.15f;
		public float _breastMass = 0.9f;
		public float _breastDamping = 0.15f;
		public float _breastGravity = 0f;
		public float _breastInertia = 0.65f;
		public float _breastMaxJiggleDistance = 0.2f;
		public float _breastTargetDistance = 0.35f;
		public float _breastPositionWeight = 1f;
		public float _breastRotationWeight = 0.15f;
		public bool _breastSquashAndStretch = true;
		public float _breastFrontStretch = 0.2f;
		public float _breastSideStretch = 0.15f;

		//merely a confirmation that the avatar has been created and jiggle bones are required
		private bool _initialized;

		//Reference to avatar and its componenets
		private DynamicCharacterAvatar _avatar;
		private SkinnedMeshRenderer _renderer;
		private string _skeleton = "other";
		private string _currentAvatar;

		//make a list to store our bones and their custom data
		[System.NonSerialized]
		public List<JiggleElement> _jigglers = new List<JiggleElement>();
		private JiggleElement _jiggler;

		// Scale is applied relative to the cached rest pose, which already includes DNA.
		private float _anatomyScaleFactor = 1;

		// Target and dynamic positions
		private Vector3 _targetPos;
		private Vector3 _dynamicPos;

		// Bone settings
		private Transform _monitoredBone;
		private Vector3 _boneAxis;
		private float _targetDistance;

		//Dynamic settings for jiggle movements
		private float _stiffness;
		private float _mass;
		private float _damping;
		private float _gravity;
		private float _inertia;
		private float _maxJiggleDistance;
		private float _positionWeight;
		private float _rotationWeight;

		//Dynamic variables for jiggle movements
		private Vector3 _force = new Vector3();
		private Vector3 _acceleration = new Vector3();
		private Vector3 _velocity = new Vector3();

		// Squash and stretch variables
		private float _sideStretch = 0.15f;
		private float _frontStretch = 0.2f;

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
				Debug.LogWarning("UMA_JiggleBreasts: No DynamicCharacterAvatar found on " + gameObject.name + ". JiggleBreasts will not work.");
                return;
            }

			_initialized = false;
			_skeleton = GetSkeleton(_avatar.activeRace.name);
			//Check if current skeleton is supported by jigglebone recipe and only run this code if the avatar has changed
			if (_skeleton != "other" && _currentAvatar != _avatar.activeRace.name)
			{
				_jigglers.Clear();
				_currentAvatar = _avatar.activeRace.name;
				_renderer = GetComponentInChildren<SkinnedMeshRenderer>();
				foreach (Transform bone in _renderer.bones)
				{
					//we are seeking by bone names so need the corresponding bone name from our supported skeletons
					if (bone.name == "LeftOuterBreast" || bone.name == "RightOuterBreast" || bone.name == "PectoralAdjust_L" || bone.name == "PectoralAdjust_R")
					{
						_jiggler = new JiggleElement();
						_jigglers.Add(_jiggler);
						_jiggler.Bone = bone;
						_jiggler.BoneType = "breast";
						//_jiggler.ExtraRotation.y = _jiggler.Bone.name == "LeftInnerBreast" ? 5 : -5;
						if (_skeleton == "Standard")
						{
							_jiggler.ExtraRotation = new Vector3(70, 0, -104);
							_jiggler.ExtraRotation.z = _jiggler.Bone.name == "LeftOuterBreast" ? -76 : -104;
						}
						else if (_skeleton == "o3n")
						{
							_jiggler.ExtraRotation = bone.name == "PectoralAdjust_L" ? new Vector3(45, 0, -67) : new Vector3(45, 0, -113);
						}
						UpdateJiggleBone(_jiggler);
					}
				}
				if (_jigglers.Count > 0)
				{
					_initialized = true;
				}
			}
			else if (_skeleton != "other")
			{
				for (int i = 0; i < _jigglers.Count; i++)
				{
					_jigglers[i].RestPoseInitialized = false;
					_jigglers[i].DynamicPositionInitialized = false;
					UpdateJiggleBone(_jigglers[i]);
				}
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

		void InitializeBone(JiggleElement jiggler)
		{
			InitializeRestPose(jiggler);

			float targetDistance = Mathf.Max(jiggler.TargetDistance, 0.001f);
			Vector3 restPosition = GetRestWorldPosition(jiggler);
			Vector3 targetPos = restPosition + GetWorldBoneAxis(GetRestWorldRotation(jiggler), jiggler.BoneAxis) * targetDistance;
			jiggler.DynamicPosition = targetPos;
			jiggler.PreviousTargetPosition = targetPos;
			jiggler.Force = Vector3.zero;
			jiggler.Acceleration = Vector3.zero;
			jiggler.Velocity = Vector3.zero;
			jiggler.DynamicPositionInitialized = true;
		}

		private static void InitializeRestPose(JiggleElement jiggler)
		{
			if (jiggler.RestPoseInitialized || jiggler.Bone == null)
			{
				return;
			}

			jiggler.RestLocalPosition = jiggler.Bone.localPosition;
			jiggler.RestLocalRotation = jiggler.Bone.localRotation;
			jiggler.RestLocalScale = jiggler.Bone.localScale;
			jiggler.RestPoseInitialized = true;
		}

		private static Vector3 GetRestWorldPosition(JiggleElement jiggler)
		{
			Transform parent = jiggler.Bone.parent;
			return parent != null ? parent.TransformPoint(jiggler.RestLocalPosition) : jiggler.RestLocalPosition;
		}

		private static Quaternion GetRestWorldRotation(JiggleElement jiggler)
		{
			Transform parent = jiggler.Bone.parent;
			return parent != null ? parent.rotation * jiggler.RestLocalRotation : jiggler.RestLocalRotation;
		}

		private static Vector3 GetWorldBoneAxis(Quaternion boneRotation, Vector3 boneAxis)
		{
			Vector3 localAxis = boneAxis.sqrMagnitude > 0.000001f ? boneAxis.normalized : Vector3.forward;
			Vector3 worldAxis = boneRotation * localAxis;
			return worldAxis.sqrMagnitude > 0.000001f ? worldAxis.normalized : Vector3.forward;
		}

		public void UpdateJiggleBone(JiggleElement jiggler)
		{
			if (jiggler.Bone.name == "LeftOuterBreast" || jiggler.Bone.name == "RightOuterBreast" || jiggler.Bone.name == "PectoralAdjust_L" || jiggler.Bone.name == "PectoralAdjust_R")
			{
				jiggler.Stiffness = _breastStiffness;
				jiggler.Mass = _breastMass;
				jiggler.Damping = _breastDamping;
				jiggler.Gravity = _breastGravity;
				jiggler.Inertia = _breastInertia;
				jiggler.MaxJiggleDistance = _breastMaxJiggleDistance;
				jiggler.TargetDistance = _breastTargetDistance;
				jiggler.PositionWeight = _breastPositionWeight;
				jiggler.RotationWeight = _breastRotationWeight;
				jiggler.SquashAndStretch = _breastSquashAndStretch;
				jiggler.FrontStretch = _breastFrontStretch;
				jiggler.SideStretch = _breastSideStretch;
				jiggler.AnatomyScaleFactor = 1f;
				InitializeBone(jiggler);
			}
		}

		void LateUpdate()
		{
			if (_initialized)
			{
				for (int i = 0; i < _jigglers.Count; i++)
				{
					MonitorJiggling(_jigglers[i]);
				}

			}
		}

		private void MonitorJiggling(JiggleElement jiggler)
		{
			//Get variables - only really need to set these if we have deviated from the defaults
			_monitoredBone = jiggler.Bone;
			if (_monitoredBone == null)
			{
				return;
			}

			_boneAxis = jiggler.BoneAxis;
			_stiffness = jiggler.Stiffness;
			_mass = Mathf.Max(jiggler.Mass, 0.0001f);
			_damping = jiggler.Damping;
			_gravity = jiggler.Gravity;
			_inertia = jiggler.Inertia;
			_maxJiggleDistance = Mathf.Max(jiggler.MaxJiggleDistance, 0.001f);
			_targetDistance = Mathf.Max(jiggler.TargetDistance, 0.001f);
			_positionWeight = Mathf.Max(0f, jiggler.PositionWeight);
			_rotationWeight = Mathf.Clamp01(jiggler.RotationWeight);
			_force = jiggler.Force;
			_velocity = jiggler.Velocity;
			_acceleration = jiggler.Acceleration;
			_dynamicPos = jiggler.DynamicPosition;

			InitializeRestPose(jiggler);

			Quaternion restRotation = GetRestWorldRotation(jiggler);
			Vector3 bonePosition = GetRestWorldPosition(jiggler);
			Vector3 worldAxis = GetWorldBoneAxis(restRotation, _boneAxis);
			_targetPos = bonePosition + worldAxis * _targetDistance;
			_monitoredBone.localRotation = jiggler.RestLocalRotation;

			if (!jiggler.DynamicPositionInitialized)
			{
				_dynamicPos = _targetPos;
				_velocity = Vector3.zero;
				_force = Vector3.zero;
				_acceleration = Vector3.zero;
				jiggler.PreviousTargetPosition = _targetPos;
				jiggler.DynamicPositionInitialized = true;
			}

			float simulationStep = Mathf.Clamp(Time.deltaTime > 0f ? Time.deltaTime * 60f : 1f, 0f, 2f);
			float dampingFactor = Mathf.Pow(Mathf.Clamp01(1f - _damping), simulationStep);
			Vector3 targetDelta = _targetPos - jiggler.PreviousTargetPosition;
			if (targetDelta.sqrMagnitude > 0.000001f)
			{
				_velocity -= targetDelta * Mathf.Clamp01(_inertia) * simulationStep;
			}

			_force = (_targetPos - _dynamicPos) * _stiffness;
			_force += Vector3.down * (_gravity / 10f);
			_acceleration = _force / _mass;
			_velocity += _acceleration * simulationStep;
			_velocity *= dampingFactor;

			// Update dynamic position from velocity only. Force should not be applied directly to position.
			_dynamicPos += _velocity * simulationStep;
			Vector3 targetOffset = _dynamicPos - _targetPos;
			if (targetOffset.sqrMagnitude > _maxJiggleDistance * _maxJiggleDistance)
			{
				Vector3 clampedOffset = targetOffset.normalized * _maxJiggleDistance;
				_dynamicPos = _targetPos + clampedOffset;
				_velocity = Vector3.ProjectOnPlane(_velocity, clampedOffset.normalized);
				targetOffset = clampedOffset;
			}

			jiggler.DynamicPosition = _dynamicPos;
			jiggler.Force = _force;
			jiggler.Acceleration = _acceleration;
			jiggler.Velocity = _velocity;
			jiggler.PreviousTargetPosition = _targetPos;

			Vector3 worldPositionOffset = targetOffset * _positionWeight;
			Transform parent = _monitoredBone.parent;
			if (parent != null)
			{
				_monitoredBone.localPosition = jiggler.RestLocalPosition + parent.InverseTransformVector(worldPositionOffset);
			}
			else
			{
				_monitoredBone.position = bonePosition + worldPositionOffset;
			}

			Vector3 movedBonePosition = parent != null ? parent.TransformPoint(_monitoredBone.localPosition) : _monitoredBone.position;
			Vector3 dynamicDirection = _dynamicPos - movedBonePosition;
			if (_rotationWeight > 0f && dynamicDirection.sqrMagnitude > 0.000001f)
			{
				Quaternion jiggleRotation = Quaternion.FromToRotation(worldAxis, dynamicDirection.normalized);
				_monitoredBone.rotation = Quaternion.Slerp(Quaternion.identity, jiggleRotation, _rotationWeight) * restRotation;
			}
			else
			{
				_monitoredBone.rotation = restRotation;
			}


			// ==================================================
			// Squash and Stretch section
			// ==================================================
			if (jiggler.SquashAndStretch)
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
                    xStretch = 1 + (-stretchMag * _sideStretch);
                }
                else
                {
                    xStretch = 1 + (stretchMag * _frontStretch);
                }

                if (_boneAxis.y == 0)
                {
                    yStretch = 1 + (-stretchMag * _sideStretch);
                }
                else
                {
                    yStretch = 1 + (stretchMag * _frontStretch);
                }

                if (_boneAxis.z == 0)
                {
                    zStretch = 1 + (-stretchMag * _sideStretch);
                }
                else
                {
                    zStretch = 1 + (stretchMag * _frontStretch);
                }

                // Set the bone scale
                _anatomyScaleFactor = jiggler.AnatomyScaleFactor;
				_monitoredBone.localScale = Vector3.Scale(jiggler.RestLocalScale, new Vector3(xStretch, yStretch, zStretch)) * _anatomyScaleFactor;
			}
			else
			{
				_monitoredBone.localScale = jiggler.RestLocalScale * jiggler.AnatomyScaleFactor;
			}

		}
		public void OnCharacterComplete(UMAData umaData)
		{
			//Debug.Log("UMA_JiggleBreasts: OnCharacterComplete called for " + umaData.name);

			UMA_JiggleBreasts ujb = umaData.gameObject.GetComponent<UMA_JiggleBreasts>();
			if (ujb == null)
			{
				ujb = umaData.gameObject.AddComponent<UMA_JiggleBreasts>();
			}

			ujb._breastStiffness = _breastStiffness;
			ujb._breastMass = _breastMass;
			ujb._breastDamping = _breastDamping;
			ujb._breastGravity = _breastGravity;
			ujb._breastInertia = _breastInertia;
			ujb._breastMaxJiggleDistance = _breastMaxJiggleDistance;
			ujb._breastTargetDistance = _breastTargetDistance;
			ujb._breastPositionWeight = _breastPositionWeight;
			ujb._breastRotationWeight = _breastRotationWeight;
			ujb._breastSquashAndStretch = _breastSquashAndStretch;
			ujb._breastFrontStretch = _breastFrontStretch;
			ujb._breastSideStretch = _breastSideStretch;
		}
	}
}
