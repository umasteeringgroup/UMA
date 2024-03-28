//	============================================================
//	Name:		UMAExpressionPlayer
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================
#if UNITY_EDITOR
#endif
namespace UMA.PoseTools
{
    /*
	/// <summary>
	/// UMA specific expression player.
	/// </summary>
	public class UMADynamicExpressionPlayer : MonoBehaviour
	{
		public enum MecanimJoint : int
		{
			None = 0,
			Head = 1,
			Neck = 2,
			Jaw = 4,
			Eye = 8,
		};

		[System.Serializable]
		public class Expression
		{
			public string poseName;
			public MecanimJoint overrideBone;
			[Range(0.0f, 1.0f)]
			public float value = 0.0f;
			[Range(0.0f, 1.0f)]
			public float defaultValue = 0.5f;
			bool isBlink;
			bool isLeftEyeInOut;
			bool isLeftEyeUpDown;
			bool isRightEyeInOut;
			bool isRightEyeUpDown;
		}

		public List<Expression> Expressions;

		/// <summary>
		/// The expression set containing poses used for animation.
		/// </summary>
		public UMADynamicExpressionSet expressionSet;
		public float minWeight = 0f;
		public DynamicCharacterAvatar avatar;

		#region Bone Setup
		/// <summary>
		/// Bone setup not relying up mecanim humanid
		/// </summary>
		public bool GenericRig = false;
		public string GenericJawBone = "jaw";
		public string GenericHeadBone = "head";
		public string GenericNeckBone = "neck";
		private int jawHash = 0;
		private int neckHash = 0;
		private int headHash = 0;
        #endregion

        #region Eyes
        /// <summary>
        /// Enable procedural blinking.
        /// </summary>
        /// <remarks>
        /// Randomly blink at intervals ranging between minBlinkDelay
        /// and maxBlinkDelay. Only recommended if the animation does
        /// not already contain blink data.
        /// </remarks>
        public bool enableBlinking = false;
		public float blinkDuration = 0.15f;
		public float minBlinkDelay = 3f;
		public float maxBlinkDelay = 15f;
		protected float blinkDelay = 0f;


		/// <summary>
		/// Enable procedural saccades.
		/// </summary>
		/// <remarks>
		/// Saccades (tiny rapid eye movements) will be procedurally
		/// generated. Only recommended if the eyes are being controlled
		/// by IK or tracking rather than high resolution animated data.
		/// </remarks>
		public bool enableSaccades = false;
		protected float saccadeDelay = 5f;
		protected float saccadeDuration = 0f;
		protected float saccadeProgress = 1f;
		protected Vector2 saccadeTarget;
		protected Vector2 saccadeTargetPrev;

		/// <summary>
		/// Procedural eye mode
		/// </summary>
		public enum GazeMode : int
		{
			None = 0,
			Acquiring = 1,
			Following = 2,
			Speaking = 3,
			Listening = 4
		};

		/// <summary>
		/// The position which the eyes are focused on or moving toward.
		/// </summary>
		public Vector3 gazeTarget;
		public float gazeWeight = 0f;
		public GazeMode gazeMode = GazeMode.None;
		#endregion

		#region Animation Overrides
		public bool overrideMecanimEyes = true;
		public bool overrideMecanimJaw = true;
		public bool overrideMecanimNeck = false;
		public bool overrideMecanimHead = false;
        #endregion

        [System.NonSerialized]
		public UMAData umaData;


		private bool initialized = false;
		[System.NonSerialized]
		public int SlotUpdateVsCharacterUpdate;

		public bool logResetErrors;

		public bool useDisableDistance = false;
		public float disableDistance = 10f;
		private Transform _mainCameraTransform;

		// Use this for initialization
		void Start()
		{
			if (avatar == null)
            {
				avatar = gameObject.GetComponent<DynamicCharacterAvatar>();
            }
			if (avatar != null)
			{
				avatar.CharacterUpdated.AddListener(CharacterUpdated);
			}
		}

		public void CharacterUpdated(UMAData data)
        {
			umaData = data;
			if (expressionSet == null)
            {
				if (avatar.activeRace.data != null)
				{
					//expressionSet = avatar.activeRace.racedata.expressionSet;
				}
            }
			if (expressionSet != null)
			{
				Initialize();
			}
        }

		public void Initialize()
		{
			blinkDelay = Random.Range(minBlinkDelay, maxBlinkDelay);

			if(Camera.main != null)
            {
                _mainCameraTransform = Camera.main.transform;
            }

            if ((expressionSet != null) && (umaData != null) && (umaData.skeleton != null))
			{
				Transform jaw = null;
				Transform neck = null;
				Transform head = null;



				if (GenericRig)
				{
					if (overrideMecanimJaw)
					{
						jawHash = UMAUtils.StringToHash(GenericJawBone);
						jaw = umaData.skeleton.GetBoneTransform(jawHash);
					}

					if (overrideMecanimNeck)
					{
						neckHash = UMAUtils.StringToHash(GenericNeckBone);
						neck = umaData.skeleton.GetBoneTransform(neckHash);
					}

					if (overrideMecanimHead)
                    {
						headHash = UMAUtils.StringToHash(GenericHeadBone);
						head = umaData.skeleton.GetBoneTransform(headHash);
                    }
				}
				else
				{
					if (umaData.animator != null)
					{
						jaw = umaData.animator.GetBoneTransform(HumanBodyBones.Jaw);
						if (jaw != null)
                        {
                            jawHash = UMAUtils.StringToHash(jaw.name);
                        }

                        neck = umaData.animator.GetBoneTransform(HumanBodyBones.Neck);
						if (neck != null)
                        {
                            neckHash = UMAUtils.StringToHash(neck.name);
                        }

                        head = umaData.animator.GetBoneTransform(HumanBodyBones.Head);
						if (head != null)
                        {
                            headHash = UMAUtils.StringToHash(head.name);
                        }
                    }
				}
				if (overrideMecanimJaw && jaw == null)
                {
					if (Debug.isDebugBuild)
                    {
						Debug.Log("Jaw bone not found, but jaw override is requested.");
                    }
					return;
                }
				if (overrideMecanimNeck && neck == null)
				{
					if (Debug.isDebugBuild)
					{
						Debug.Log("Neck bone not found, but neck override is requested.");
					}
					return;
				}
				if (overrideMecanimHead && head == null)
				{
					if (Debug.isDebugBuild)
					{
						Debug.Log("Head bone not found, but head override is requested.");
					}
					return;
				}
				if (overrideMecanimJaw && jaw == null)
				{
					if (Debug.isDebugBuild)
					{
						Debug.Log("Jaw bone not found, but jaw override is requested.");
					}
					return;
				}
				initialized = true;
			}
		}

		void Update()
		{
			if (!initialized || umaData == null)
			{
				return;
			}

			if (_mainCameraTransform != null && useDisableDistance && (_mainCameraTransform.position - transform.position).sqrMagnitude > (disableDistance * disableDistance))
            {
                return;
            }

            // Fix for animation systems which require consistent values frame to frame
            Quaternion headRotation = Quaternion.identity;
			Quaternion neckRotation = Quaternion.identity;

			try { headRotation = umaData.skeleton.GetRotation(headHash); }
			catch(System.Exception) { Debug.LogError("GetRotation: Head Bone not found!"); }

			try { neckRotation = umaData.skeleton.GetRotation(neckHash); }
			catch(System.Exception) { Debug.LogError("GetRotation: Neck Bone not found!"); }

			// Need to reset bones here if we want Mecanim animation
			expressionSet.RestoreBones(umaData.skeleton, logResetErrors);

			if (!overrideMecanimNeck)
            {
                umaData.skeleton.SetRotation(neckHash, neckRotation);
            }

            if (!overrideMecanimHead)
            {
                umaData.skeleton.SetRotation(headHash, headRotation);
            }

            if (gazeWeight > 0f)
			{
				if (umaData.animator != null)
				{
					umaData.animator.SetLookAtPosition(gazeTarget);
					umaData.animator.SetLookAtWeight(gazeWeight);
				}
			}
		}

		void LateUpdate()
		{
			if (!initialized)
            {
                return;
            }

            if (umaData == null || umaData.skeleton == null)
            {
                return;
            }

            if (_mainCameraTransform != null && useDisableDistance && (_mainCameraTransform.position - transform.position).sqrMagnitude > (disableDistance * disableDistance))
            {
                return;
            }

            if (enableSaccades)
            {
                UpdateSaccades();
            }

            if (enableBlinking)
            {
                UpdateBlinking();
            }

            float[] values = Values;
			MecanimJoint mecanimMask = MecanimJoint.None;
			if (!overrideMecanimNeck)
            {
                mecanimMask |= MecanimJoint.Neck;
            }

            if (!overrideMecanimHead)
            {
                mecanimMask |= MecanimJoint.Head;
            }

            if (!overrideMecanimJaw)
            {
                mecanimMask |= MecanimJoint.Jaw;
            }

            if (!overrideMecanimEyes)
            {
                mecanimMask |= MecanimJoint.Eye;
            }

            if (overrideMecanimJaw)
			{
				umaData.skeleton.Restore(jawHash);
			}

			for (int i = 0; i < values.Length; i++)
			{
				if ((Expressions[i].overrideBone & mecanimMask) != MecanimJoint.None)
				{
					continue;
				}

				float weight = values[i];
				if (weight == 0f)
                {
                    continue;
                }

                UMABonePose pose = null;
				if (weight > 0)
				{
					pose = expressionSet.posePairs[i].primary;
				} else
				{
					weight = -weight;
					pose = expressionSet.posePairs[i].inverse;
				}

				if ((weight > minWeight) && (pose != null))
				{
					pose.ApplyPose(umaData.skeleton, weight);
				}
			}
		}

		public float eyeMovementRange = 30f;
		public float mutualGazeRange = 0.10f;
		public float MinSaccadeDelay = 0.25f;
		public float MaxSaccadeMagnitude = 15f;

		protected void UpdateSaccades()
		{
			saccadeDelay -= Time.deltaTime;
			if (saccadeDelay < 0f)
			{
				saccadeTargetPrev = saccadeTarget;

				int saccadeDirection = Random.Range(0, 4);
				float saccadeOffset = UMAUtils.GaussianRandom(0f, 0.125f);
				switch (saccadeDirection)
				{
					case 0:
						saccadeTarget.Set(1f - Mathf.Abs(saccadeOffset), saccadeOffset);
						break;
					case 1:
						saccadeTarget.Set(-1f + Mathf.Abs(saccadeOffset), saccadeOffset);
						break;
					case 2:
						saccadeTarget.Set(saccadeOffset, 1f - Mathf.Abs(saccadeOffset));
						break;
					default:
						saccadeTarget.Set(saccadeOffset, -1f + Mathf.Abs(saccadeOffset));
						break;
				}

				float saccadeMagnitude = Random.Range(0.01f, MaxSaccadeMagnitude);
				float saccadeDistance = (-6.9f / eyeMovementRange) * Mathf.Log(saccadeMagnitude / 15.7f);
				saccadeDuration = 0.021f + 0.0022f * saccadeDistance * eyeMovementRange;
				saccadeProgress = 0f;

				switch (gazeMode)
				{
					case GazeMode.Listening:
						if (Mathf.Abs(saccadeDistance) < mutualGazeRange)
                        {
                            saccadeDelay = UMAUtils.GaussianRandom(237.5f / 30f, 47.1f / 30f);
                        }
                        else
                        {
                            saccadeDelay = UMAUtils.GaussianRandom(13f / 30f, 7.1f / 30f);
                        }

                        break;

					default:
						if (Mathf.Abs(saccadeDistance) < mutualGazeRange)
                        {
                            saccadeDelay = UMAUtils.GaussianRandom(93.9f / 30f, 94.9f / 30f);
                        }
                        else
                        {
                            saccadeDelay = UMAUtils.GaussianRandom(27.8f / 30f, 24f / 30f);
                        }

                        break;
				}

				if (saccadeDelay < MinSaccadeDelay)
                {
                    saccadeDelay = MinSaccadeDelay;
                }

                saccadeTarget *= saccadeDistance;
			}

			if (saccadeProgress < 1f)
			{
				float timeProgress = Time.deltaTime / saccadeDuration;
				float progressRate = 1.5f - 3f * Mathf.Pow(saccadeProgress - 0.5f, 2);
				saccadeProgress += timeProgress * progressRate;

				//leftEyeIn_Out = Mathf.Lerp(saccadeTargetPrev.x, saccadeTarget.x, saccadeProgress);
				//leftEyeUp_Down = Mathf.Lerp(saccadeTargetPrev.y, saccadeTarget.y, saccadeProgress);
				//rightEyeIn_Out = Mathf.Lerp(-saccadeTargetPrev.x, -saccadeTarget.x, saccadeProgress);
				//rightEyeUp_Down = Mathf.Lerp(saccadeTargetPrev.y, saccadeTarget.y, saccadeProgress);
			} else
			{
				//leftEyeIn_Out = saccadeTarget.x;
				//leftEyeUp_Down = saccadeTarget.y;
				//rightEyeIn_Out = -saccadeTarget.x;
				//rightEyeUp_Down = saccadeTarget.y;
			}
		}

		protected void UpdateBlinking()
		{
			//if (leftEyeOpen_Close < -1f)
			//	leftEyeOpen_Close = 0f;
			//if (rightEyeOpen_Close < -1f)
			//	rightEyeOpen_Close = 0f;

			blinkDelay -= Time.deltaTime;
			if (blinkDelay < blinkDuration)
			{
				if (blinkDelay < 0f)
				{
					switch (gazeMode)
					{
						case GazeMode.Speaking:
						case GazeMode.Listening:
							blinkDelay = UMAUtils.GaussianRandom(2.3f, 1.1f);
							break;

						case GazeMode.Following:
							blinkDelay = UMAUtils.GaussianRandom(15.4f, 8.2f);
							break;

						default:
							blinkDelay = UMAUtils.GaussianRandom(3.8f, 1.2f);
							break;
					}

					if (blinkDelay < blinkDuration)
                    {
                        blinkDelay = blinkDuration;
                    }
                } else
				{
					//leftEyeOpen_Close = -1.01f;
					//rightEyeOpen_Close = -1.01f;
				}
			}
		}

		private float[] valueArray = new float[0];

		public float[] Values
		{
			get
			{
				if (valueArray.Length != Expressions.Count)
				{
					valueArray = new float[Expressions.Count];
				}

				for (int i = 0; i < Expressions.Count; i++)
				{
					valueArray[i] = Expressions[i].value;
				}
				return valueArray;
			}
			set
			{
				if (value.Length > Expressions.Count)
                {
                    return;
                }

                for (int i = 0; i < value.Length; i++)
				{
					Expressions[i].value = value[i];
				}
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// Name of the primary (positive values) pose. Editor only.
		/// </summary>
		/// <returns>The pose name.</returns>
		/// <param name="index">Index.</param>
		public string PrimaryPoseName(int index)
		{
			string name = ObjectNames.NicifyVariableName(Expressions[index].poseName);
			int underscore = name.IndexOf('_');

			if (underscore < 0)
            {
                return name;
            }

            return name.Substring(0, underscore);
		}

		/// <summary>
		/// Name of the inverse (negative values) pose. Editor only.
		/// </summary>
		/// <returns>The pose name.</returns>
		/// <param name="index">Index.</param>
		public string InversePoseName(int index)
		{
			string name = ObjectNames.NicifyVariableName(Expressions[index].poseName);
			int underscore = name.IndexOf('_');

			if (underscore < 0)
            {
                return null;
            }

            int space = name.LastIndexOf(' ', underscore);
			return name.Substring(0, space + 1) + name.Substring(underscore + 1);
		}

		/// <summary>
		/// Saves the expression to an animation clip.
		/// </summary>
		/// <param name="assetPath">Path for the new animation clip.</param>
		public void SaveExpressionClip(string assetPath)
		{
			AnimationClip clip = new AnimationClip();

			Animation anim = gameObject.GetComponent<Animation>();
			bool legacyAnimation = (anim != null);
			if (legacyAnimation)
			{
				clip.legacy = true;
			}
			float[] values = Values;
			for (int i = 0; i < Expressions.Count; i++)
			{
				string pose = Expressions[i].poseName;
				float value = values[i];
				if (value != 0f)
				{
					AnimationCurve curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, value), new Keyframe(2f, 0f));

					EditorCurveBinding binding = new EditorCurveBinding();
					binding.propertyName = pose;
					binding.type = typeof(ExpressionPlayer);
					AnimationUtility.SetEditorCurve(clip, binding, curve);
				}
			}

			if ((assetPath != null) && (assetPath.EndsWith(".anim")))
			{
				AssetDatabase.CreateAsset(clip, assetPath);

				if (legacyAnimation)
				{
					anim.AddClip(clip, clip.name);
					anim.clip = clip;
				}

				AssetDatabase.SaveAssets();
			}
		}
#endif
	} */
}