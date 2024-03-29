//Based on jiggle bone code from Michael Cook (Fishypants), Adapted for UMA by Phil Taylor (DankP3).


using System.Collections.Generic;
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
		public float _bellyGravity = 0.75f;
		public bool _bellySquashAndStretch = true;
		public float _bellyFrontStretch = 0.2f;
		public float _bellySideStretch = 0.15f;


		//merely a confirmation that the avatar has been created and jiggle bones are required
		private bool _initialized;

		//Reference to avatar and its componenets
		private DynamicCharacterAvatar _avatar;
		private Dictionary<string, DnaSetter> _dna;
		private SkinnedMeshRenderer _renderer;
		private string _skeleton = "other";
		private string _currentAvatar;

		//We have the option to monitor for changes in belly size in the DNA and transfer that to the terminal, since the stretch code overrides and needs to be compensated
		private float _anatomyScaleFactor = 1;

		// Target and dynamic positions
		private Vector3 _targetPos;
		private Vector3 _dynamicPos;

		// Bone settings
		private Transform _monitoredBone;
		private Vector3 _boneAxis;
		private float _targetDistance = 2.0f;
		private Vector3 _upDirection;
		private Vector3 _extraRotation;

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

            _dna = _avatar.GetDNA();
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
						_upDirection = new Vector3(-1, 0, 0);
						_extraRotation = new Vector3(0, 0, -90);
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
				_anatomyScaleFactor = _dna["belly"].Get() * 2;
				_initialized = true;
			}

		}

		void AvatarUpdated(UMAData data)
		{
			Init();
		}

		private string GetSkeleton(string name)
		{
			switch (name)
			{
				case "HumanMaleDCS":
					return "Standard";
				case "HumanMale":
					return "Standard";
				case "HumanMaleHighPoly":
					return "Standard";
				case "HumanFemaleDCS":
					return "Standard";
				case "HumanFemale":
					return "Standard";
				case "HumanFemaleHighPoly":
					return "Standard";
				case "HumanFemale2":
					return "Standard";
				case "o3n Male":
					return "o3n";
				case "o3n Female":
					return "o3n";
				default:
					return "other";
			}
		}

		void InitializeBone()
		{
			Vector3 targetPos = _monitoredBone.position + _monitoredBone.TransformDirection(new Vector3((_boneAxis.x * _targetDistance), (_boneAxis.y * _targetDistance), (_boneAxis.z * _targetDistance)));
			_dynamicPos = targetPos;
		}

		public void UpdateJiggleBone()
		{

			//_anatomyScaleFactor = _dna["belly"].Get() *2;
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

			// Reset the bone rotation so we can recalculate the upVector and forwardVector
			_monitoredBone.rotation = new Quaternion();
			//transform.localRotation = originalQuat;


			// Update forwardVector and upVector
			Vector3 upVector = _monitoredBone.TransformDirection(_upDirection);


			// Calculate target position
			_targetPos = _monitoredBone.position + _monitoredBone.TransformDirection(new Vector3((_boneAxis.x * _targetDistance), (_boneAxis.y * _targetDistance), (_boneAxis.z * _targetDistance)));

			// Calculate force, acceleration, and velocity per X, Y and Z
			_force.x = (_targetPos.x - _dynamicPos.x) * _bellyStiffness;
			_acceleration.x = _force.x / _bellyMass;
			_velocity.x += _acceleration.x * (1 - _bellyDamping);

			_force.y = (_targetPos.y - _dynamicPos.y) * _bellyStiffness;
			_force.y -= _bellyGravity / 10; // Add some gravity
			_acceleration.y = _force.y / _bellyMass;
			_velocity.y += _acceleration.y * (1 - _bellyDamping);

			_force.z = (_targetPos.z - _dynamicPos.z) * _bellyStiffness;
			_acceleration.z = _force.z / _bellyMass;
			_velocity.z += _acceleration.z * (1 - _bellyDamping);

			// Update dynamic postion
			_dynamicPos += _velocity + _force;

			// Set bone rotation to look at dynamicPos     
			_monitoredBone.LookAt(_dynamicPos, upVector);

			//Apply extra rotation
			_monitoredBone.Rotate(_extraRotation, Space.Self);


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
                _monitoredBone.localScale = new Vector3(xStretch, yStretch, zStretch) * _anatomyScaleFactor;
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
			ujb._bellySquashAndStretch = _bellySquashAndStretch;
			ujb._bellyFrontStretch = _bellyFrontStretch;
			ujb._bellySideStretch = _bellySideStretch;
		}
	}
}
