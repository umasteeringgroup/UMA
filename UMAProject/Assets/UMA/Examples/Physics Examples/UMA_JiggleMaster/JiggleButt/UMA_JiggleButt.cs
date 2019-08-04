//Based on jiggle bone code from Michael Cook (Fishypants), Adapted for UMA by Phil Taylor (DankP3).


using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UMA;
using UMA.CharacterSystem;

namespace UMA.Examples {

	public class UMA_JiggleButt: MonoBehaviour {

		//TODO, need to distinguish between male and female; need to calculate vectors for male rig; need to assign male and female vectors as required.

		//Controls for customisation
		public float _buttStiffness = 0.15f;
		public float _buttMass = 0.9f;
		public float _buttDamping = 0.15f;
		public float _buttGravity = 0.75f;
		public bool _buttSquashAndStretch = true;
		public float _buttFrontStretch = 0.2f;
		public float _buttSideStretch = 0.15f;

		//merely a confirmation that the avatar has been created and jiggle bones are required
		private bool _initialized;

		//Reference to avatar and its componenets
		private DynamicCharacterAvatar _avatar;
		private Dictionary<string, DnaSetter> _dna;
		private SkinnedMeshRenderer _renderer;
		private string _skeleton = "other";
		private string _gender = "female";
		private string _currentAvatar;

		//make a list to store our bones and their custom data
		public List<JiggleElement> _jigglers = new List <JiggleElement>();
		private JiggleElement _jiggler;


		//We have the option to monitor for changes in breast/butt size in the DNA and transfer that to the terminal, since the stretch code overrides and needs to be compensated
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


		//Dynamic settings for jiggle movements
		private float _stiffness;
		private float _mass;
		private float _damping;
		private float _gravity;

		//Dynamic variables for jiggle movements
		private Vector3 _force = new Vector3();
		private Vector3 _acceleration = new Vector3();
		private Vector3 _velocity = new Vector3();

		// Squash and stretch variables
		private float _sideStretch = 0.15f;
		private float _frontStretch = 0.2f;

		void Awake() {
			Init();
		}

		//Subscribe to mailing lists
		void OnEnable() {
			if (_avatar)
				_avatar.CharacterUpdated.AddListener(AvatarUpdated);
		}
		void OnDisable() {
			if (_avatar)
				_avatar.CharacterUpdated.RemoveListener(AvatarUpdated);
		}

		void Init() {
			_avatar = GetComponent<DynamicCharacterAvatar>();
			if (_avatar == null) return;

			_dna = _avatar.GetDNA();
			_initialized = false;
			_skeleton = GetSkeleton(_avatar.activeRace.name);
			//Check if current skeleton is supported by jigglebone recipe and only run this code if the avatar has changed
			if (_skeleton != "other" && _currentAvatar != _avatar.activeRace.name) {
				_jigglers.Clear();
				_currentAvatar = _avatar.activeRace.name;
				_renderer = GetComponentInChildren<SkinnedMeshRenderer>();
				foreach (Transform bone in _renderer.bones) {
					//we are seeking by bone names so need the corresponding bone name from our supported skeletons
					if (bone.name == "LeftGluteus" || bone.name == "RightGluteus" || bone.name == "GluteusAdjust_L" || bone.name == "GluteusAdjust_R") {
						_jiggler = new JiggleElement();
						_jigglers.Add(_jiggler);
						_jiggler.Bone = bone;
						_jiggler.BoneType = "butt";
						_jiggler.BoneAxis = new Vector3(0, 0, -1);
						if (bone.name == "LeftGluteus" || bone.name == "RightGluteus") { 
							_jiggler.UpDirection = new Vector3(1, 0, 0);
							_jiggler.ExtraRotation = _gender == "female" ? new Vector3(-67, 180, -90) : new Vector3(20, 45, -90);//Note male and female need different butt rotations.
						}
						else if (bone.name == "GluteusAdjust_L" || bone.name == "GluteusAdjust_R") {
							_jiggler.UpDirection = new Vector3(-1, 0, 0);
							_jiggler.ExtraRotation = new Vector3(-90, 0, 90);
						}
						UpdateJiggleBone(_jiggler);
					}
				}
				if (_jigglers.Count > 0) {
					_initialized = true;
				}
			}
			else if (_skeleton != "other") {
				for (int i = 0; i < _jigglers.Count; i++) {
					UpdateJiggleBone(_jigglers[i]);
				}
				_initialized = true;
			}
		}

		void AvatarUpdated(UMAData data) {
			Init();
		}

		private string GetSkeleton (string name) {
			//Some skeletons differ between male and female (eg. o3n) so we need to capture sex information with 'butt' code
			switch (name) {
				case "HumanMaleDCS":
					_gender = "male";
					return "Standard";
				case "HumanMale":
					_gender = "male";
					return "Standard";
				case "HumanMaleHighPoly":
					_gender = "male";
					return "Standard";
				case "HumanFemaleDCS":
					_gender = "female";
					return "Standard";
				case "HumanFemale":
					_gender = "female";
					return "Standard";
				case "HumanFemaleHighPoly":
					_gender = "female";
					return "Standard";
				case "HumanFemale2":
					_gender = "female";
					return "Standard";
				case "o3n Male":
					_gender = "male";
					return "o3n";
				case "o3n Female":
					_gender = "female";
					return "o3n";
				default:
					return "other";
			}
		}

		void InitializeBone (JiggleElement jiggler) {
			Vector3 targetPos = jiggler.Bone.position + jiggler.Bone.TransformDirection(new Vector3((jiggler.BoneAxis.x * _targetDistance), (jiggler.BoneAxis.y * _targetDistance), (jiggler.BoneAxis.z * _targetDistance)));
			jiggler.DynamicPosition = targetPos;
		}

		public void UpdateJiggleBone(JiggleElement jiggler) {
			if (jiggler.Bone.name == "LeftGluteus" || jiggler.Bone.name == "RightGluteus" || jiggler.Bone.name == "GluteusAdjust_L" || jiggler.Bone.name == "GluteusAdjust_R") {
				jiggler.Stiffness = _buttStiffness;
				jiggler.Mass = _buttMass;
				jiggler.Damping = _buttDamping;
				jiggler.Gravity = _buttGravity;
				jiggler.SquashAndStretch = _buttSquashAndStretch;
				jiggler.FrontStretch = _buttFrontStretch;
				jiggler.SideStretch = _buttSideStretch;
				jiggler.AnatomyScaleFactor = _dna["gluteusSize"].Get() *2;
				InitializeBone(_jiggler);
			}
		}

		void LateUpdate() {
			if (_initialized) {
				for (int i = 0; i < _jigglers.Count; i++) {
					MonitorJiggling(_jigglers[i]);
				}
				
			}
		}

		private void MonitorJiggling (JiggleElement jiggler) {
			//Get variables - only really need to set these if we have deviated from the defaults
			_monitoredBone = jiggler.Bone;
			_boneAxis = jiggler.BoneAxis;
			_upDirection = jiggler.UpDirection;
			_extraRotation = jiggler.ExtraRotation;
			_stiffness = jiggler.Stiffness;
			_mass = jiggler.Mass;
			_damping = jiggler.Damping;
			_gravity = jiggler.Gravity;
			_force = jiggler.Force;
			_velocity = jiggler.Velocity;
			_acceleration = jiggler.Acceleration;
			_dynamicPos = jiggler.DynamicPosition;


			// Reset the bone rotation so we can recalculate the upVector and forwardVector
			_monitoredBone.rotation = new Quaternion();
			//transform.localRotation = originalQuat;
			

			// Update forwardVector and upVector
			Vector3 upVector = _monitoredBone.TransformDirection(_upDirection);


			// Calculate target position
			_targetPos = _monitoredBone.position + _monitoredBone.TransformDirection(new Vector3((_boneAxis.x * _targetDistance), (_boneAxis.y * _targetDistance), (_boneAxis.z * _targetDistance)));

			// Calculate force, acceleration, and velocity per X, Y and Z
			_force.x = (_targetPos.x - _dynamicPos.x) * _stiffness;
			_acceleration.x = _force.x / _mass;
			_velocity.x += _acceleration.x * (1 - _damping);

			_force.y = (_targetPos.y - _dynamicPos.y) * _stiffness;
			_force.y -= _gravity / 10; // Add some gravity
			_acceleration.y = _force.y / _mass;
			_velocity.y += _acceleration.y * (1 - _damping);

			_force.z = (_targetPos.z - _dynamicPos.z) * _stiffness;
			_acceleration.z = _force.z / _mass;
			_velocity.z += _acceleration.z * (1 - _damping);

			// Update dynamic postion
			_dynamicPos += _velocity + _force;
			jiggler.DynamicPosition = _dynamicPos;
			jiggler.Force = _force;
			jiggler.Acceleration = _acceleration;
			jiggler.Velocity = _velocity;

			// Set bone rotation to look at dynamicPos     
			_monitoredBone.LookAt(_dynamicPos, upVector);

			//Apply extra rotation
			_monitoredBone.Rotate(_extraRotation, Space.Self);


			// ==================================================
			// Squash and Stretch section
			// ==================================================
			if (jiggler.SquashAndStretch) {
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
				if (_boneAxis.x == 0) xStretch = 1 + (-stretchMag * _sideStretch);
				else xStretch = 1 + (stretchMag * _frontStretch);


				if (_boneAxis.y == 0) yStretch = 1 + (-stretchMag * _sideStretch);
				else yStretch = 1 + (stretchMag * _frontStretch);


				if (_boneAxis.z == 0) zStretch = 1 + (-stretchMag * _sideStretch);
				else zStretch = 1 + (stretchMag * _frontStretch);

				// Set the bone scale
				_anatomyScaleFactor = jiggler.AnatomyScaleFactor;
				_monitoredBone.localScale = new Vector3(xStretch, yStretch, zStretch) * _anatomyScaleFactor;
			}
			
		}
		public void OnCharacterComplete(UMAData umaData)
		{
			UMA_JiggleButt ujb = umaData.gameObject.GetComponent<UMA_JiggleButt>();
			if (ujb == null)
			{
				ujb = umaData.gameObject.AddComponent<UMA_JiggleButt>();
			}

			ujb._buttStiffness = _buttStiffness;
			ujb._buttMass = _buttMass;
			ujb._buttDamping = _buttDamping;
			ujb._buttGravity = _buttGravity;
			ujb._buttSquashAndStretch = _buttSquashAndStretch;
			ujb._buttFrontStretch = _buttFrontStretch;
			ujb._buttSideStretch = _buttSideStretch;
		}
	}
}
