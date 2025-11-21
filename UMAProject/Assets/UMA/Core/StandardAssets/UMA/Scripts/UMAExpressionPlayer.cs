//	============================================================
//	Name:		UMAExpressionPlayer
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.PoseTools
{
    /// <summary>
    /// UMA specific expression player.
    /// </summary>
   // [ExecuteInEditMode]
	public class UMAExpressionPlayer : ExpressionPlayer, ISerializationCallbackReceiver
	{
		/// <summary>
		/// The expression set containing poses used for animation.
		/// </summary>
		public UMAExpressionSet expressionSet;
		public float minWeight = 0f;
		[System.NonSerialized]
		public UMAData umaData;

		private int jawHash = 0;
		private int neckHash = 0;
		private int headHash = 0;

		private bool standAlone = false;
		private bool initialized = false;
		[System.NonSerialized]
		public int SlotUpdateVsCharacterUpdate;
		public bool logResetErrors;

		public bool useDisableDistance = false;
		public bool processing = false;
		private bool EventsAdded = false;
        private bool UmaEventsAdded = false;
		public float disableDistance = 10f;
		private Transform _mainCameraTransform;
		private DynamicCharacterAvatar avatar;

		public float eyeMovementRange = 30f;
		public float mutualGazeRange = 0.10f;
		public float MinSaccadeDelay = 0.25f;
		public float MaxSaccadeMagnitude = 15f;
		public float minSaccade = -0.6f;
		public float maxSaccade = 0.6f;
		public bool allowUpDownSaccades = false;

		public Animator animator;
		private float[] LastValues;

		public UMAExpressionEvent ExpressionChanged;

		// Use this for initialization
		void Start()
		{
            Debug.Log("UMAExpressionPlayer Start called for " + gameObject.name+" Neck_down is " + neckUp_Down);
            Initialize();
		}

#if UNITY_EDITOR
        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                Debug.Log("UMAExpressionPlayer OnEnable called for " + gameObject.name+" Neck_down is " + neckUp_Down);
                Initialize();
            }
        }

        private void OnValidate()
        {
            if (!EditorApplication.isPlaying)
            {
                if (!initialized || umaData == null) 
                {
                    Debug.Log("UMAExpressionPlayer OnValidate called for " + gameObject.name+" Neck_down is " + neckUp_Down);
                    Initialize(); 
                }
                DoUpdate();
                DoLateUpdate();
            }
        }
#endif

		public void Initialize()
        {
            Debug.Log("Initializing UMAExpressionPlayer for " + gameObject.name+" Neck_down is " + neckUp_Down);
            blinkDelay = Random.Range(minBlinkDelay, maxBlinkDelay);

            if (Camera.main != null)
            {
                _mainCameraTransform = Camera.main.transform;
            }

            avatar = GetComponent<DynamicCharacterAvatar>();

			if (avatar != null)
            {
				umaData = avatar.umaData;
				if (!EventsAdded)
				{
					avatar.CharacterBegun.AddListener(CharacterBegun);
					avatar.CharacterUpdated.AddListener(UmaData_OnCharacterUpdated);
					EventsAdded = true;
				}
			}
			else
			{
				if (umaData == null)
				{
					// Find the UMAData, which could be up or down the hierarchy
					umaData = gameObject.GetComponentInChildren<UMAData>();
					if (umaData == null)
					{
						umaData = gameObject.GetComponentInParent<UMAData>();
					}
					if (umaData != null)
                    {
                        if (!UmaEventsAdded)
                        {
							umaData.CharacterBegun.AddListener(CharacterBegun);
							umaData.CharacterUpdated.AddListener(UmaData_OnCharacterUpdated);
                            UmaEventsAdded = true;
                        }
					}
					else
                    {
						standAlone = true;
						animator = gameObject.GetComponentInChildren<Animator>();
						SetupBones();
					}
				}
			}

            Debug.Log("UMAExpressionPlayer found UMAData: " + (umaData != null)+" Neck_down is " + neckUp_Down);
            if (umaData != null)
			{
				animator = gameObject.GetComponentInChildren<Animator>();
				SetupBones();
			}

			processing = true;
			initialized = true;
            Debug.Log("UMAExpressionPlayer init done: " + (umaData != null) + " Neck_down is " + neckUp_Down);
        }

        private void CharacterBegun(UMAData _umaData)
        {
			this.umaData = _umaData;
			processing = false;
        }

		private void SetupBones()
		{
            Debug.Log("Setup bones starting. neck_down is " + neckUp_Down);

            if ((expressionSet != null) /*&& (umaData != null) && (umaData.skeleton != null)*/)
			{
				Transform jaw = null;
				Transform neck = null;
				Transform head = null;

				if (umaData != null && umaData.animator != null && umaData.animator.avatar != null)
				{
					// Initialize and then assign from animator bones
					jawHash = 0;
					neckHash = 0;
					headHash = 0;
					animator = umaData.animator;

					jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
                    if (jaw != null)
                    {
                        jawHash = UMAUtils.StringToHash(jaw.name);
                    }
                    else
                    {
                        // Try unmapped jaw name from expression set
                        jaw = animator.transform.Find(expressionSet.UnmappedJawName);
                        if (jaw != null)
                        {
                            jawHash = UMAUtils.StringToHash(jaw.name);
                        }
                    }

                    neck = animator.GetBoneTransform(HumanBodyBones.Neck);
					if (neck != null)
					{
						neckHash = UMAUtils.StringToHash(neck.name);
					}

					head = animator.GetBoneTransform(HumanBodyBones.Head);
					if (head != null)
					{
						headHash = UMAUtils.StringToHash(head.name);
					}
				}

				// Do NOT change override flags here; just warn in editor if requested
#if UNITY_EDITOR
				if (Debug.isDebugBuild)
				{
					if (overrideMecanimJaw && jaw == null) { Debug.Log("Jaw bone not found; jaw override will be skipped until available."); }
					if (overrideMecanimNeck && neck == null) { Debug.Log("Neck bone not found; neck override will be skipped until available."); }
					if (overrideMecanimHead && head == null) { Debug.Log("Head bone not found; head override will be skipped until available."); }
				}
#endif
			}
            Debug.Log("Setup bones complete. neck_down is " + neckUp_Down);
		}

        private void UmaData_OnCharacterUpdated(UMAData obj)
        {
            Debug.Log("Character updated. Neck_updown is " + neckUp_Down);
			umaData = obj;
			SetupBones();
			animator = umaData.animator;
			processing = true;
        }

		private void saveValues(float[] values)
        {
			for(int i=0;i<PoseCount;i++)
            {
				LastValues[i] = values[i];
            }
        }

        private void DoUpdate()
		{
			if (standAlone != true)
			{
				if (!initialized || umaData == null)
				{
                    Debug.Log("UMAExpressionPlayer DoUpdate calling Initialize for " + gameObject.name+" Neck_down is " + neckUp_Down);
                    Initialize();
					return;
				}
			}

			// In editor, always allow processing to run to reflect inspector changes
			if (Application.isPlaying && !processing)
            {
                return;
            }

            if (_mainCameraTransform != null && useDisableDistance && (_mainCameraTransform.position - transform.position).sqrMagnitude > (disableDistance * disableDistance))
            {
                return;
            }

            if (umaData == null || umaData.skeleton == null || umaData.skeleton.boneHashData.Count == 0)
            {
                return;
            }

            // Effective overrides only apply when corresponding bone hash exists
            bool oHead = overrideMecanimHead && headHash != 0;
            bool oNeck = overrideMecanimNeck && neckHash != 0;
            bool oJaw  = overrideMecanimJaw  && jawHash  != 0;

            // Fix for animation systems which require consistent values frame to frame
            Quaternion headRotation = Quaternion.identity;
			Quaternion neckRotation = Quaternion.identity;

			if (!oHead && headHash != 0)
            {
				headRotation = umaData.skeleton.GetRotation(headHash);
			}
			if (!oNeck && neckHash != 0)
            {
				neckRotation = umaData.skeleton.GetRotation(neckHash);
			}

			// Need to reset bones here if we want Mecanim animation
			if (expressionSet != null)
            {
                expressionSet.RestoreBones(umaData.skeleton, logResetErrors);
            }

            if (!oNeck && neckHash != 0)
            {
                umaData.skeleton.SetRotation(neckHash, neckRotation);
            }

            if (!oHead && headHash != 0)
            {
                umaData.skeleton.SetRotation(headHash, headRotation);
            }

		}

        private void OnAnimatorIK(int layerIndex)
        {
            if (gazeWeight > 0f)
            {
                if (umaData != null && umaData.animator != null)
                {
                    umaData.animator.SetLookAtPosition(gazeTarget);
                    umaData.animator.SetLookAtWeight(gazeWeight);
                }
            }
        }

        private void DoLateUpdate()
		{
			// In editor, always allow processing to run to reflect inspector changes
			if (Application.isPlaying && !processing)
            {
				return;
            }

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
				this.UpdateSaccades();
            }

			if (enableBlinking)
            {
				this.UpdateBlinking();
            }

			float[] values = Values;

			// Effective overrides only apply when corresponding bone hash exists
			bool oHead = overrideMecanimHead && headHash != 0;
			bool oNeck = overrideMecanimNeck && neckHash != 0;
			bool oJaw  = overrideMecanimJaw  && jawHash  != 0;
			bool oEyes = overrideMecanimEyes; // eye hashes not tracked here
			bool oHands = overrideMecanimHands; // hand hashes not tracked here

			MecanimJoint mecanimMask = MecanimJoint.None;
			if (!oNeck) { mecanimMask |= MecanimJoint.Neck; }
			if (!oHead) { mecanimMask |= MecanimJoint.Head; }
			if (!oJaw)  { mecanimMask |= MecanimJoint.Jaw; }
			if (!oEyes) { mecanimMask |= MecanimJoint.Eye; }
			if (!oHands){ mecanimMask |= MecanimJoint.Hands; }

			if (oJaw && jawHash != 0)
			{
				umaData.skeleton.Restore(jawHash);
			}

			if (LastValues == null || LastValues.Length < values.Length)
            {
				LastValues = new float[44];
				saveValues(values);
            }

			for (int i = 0; i < values.Length; i++)
			{
				if (LastValues[i] != values[i])
            	{
					if (ExpressionChanged != null)
                    {
                        ExpressionChanged.Invoke(umaData, PoseNames[i], values[i]);
                    }
                }

				if ((MecanimAlternate[i] & mecanimMask) != MecanimJoint.None)
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
			saveValues(values);
		}

        void Update()
        {
            DoUpdate();
        }

        void LateUpdate()
        {
            DoLateUpdate();
        }

        /// <summary>
        /// Runs a single simulated Update/LateUpdate while in edit mode so inspector changes are reflected.
        /// Safe no-op in play mode.
        /// </summary>
        public void EditorSimulateOnce()
        {
#if UNITY_EDITOR
            if (Application.isPlaying) return;
            if (!initialized || umaData == null) { Initialize(); }
            DoUpdate();
            DoLateUpdate();
#endif
        }

        // Reintroduce local implementations to ensure availability in this type
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
                ClampSaccades();
                leftEyeIn_Out = Mathf.Lerp(saccadeTargetPrev.x, saccadeTarget.x, saccadeProgress);
                rightEyeIn_Out = Mathf.Lerp(-saccadeTargetPrev.x, -saccadeTarget.x, saccadeProgress);
                if (allowUpDownSaccades)
                {
                    leftEyeUp_Down = Mathf.Lerp(saccadeTargetPrev.y, saccadeTarget.y, saccadeProgress);
                    rightEyeUp_Down = Mathf.Lerp(saccadeTargetPrev.y, saccadeTarget.y, saccadeProgress);
                }
            }
            else
            {
                ClampSaccades();
                leftEyeIn_Out = saccadeTarget.x;
                rightEyeIn_Out = -saccadeTarget.x;
                if (allowUpDownSaccades)
                {
                    rightEyeUp_Down = saccadeTarget.y;
                    leftEyeUp_Down = saccadeTarget.y;
                }
            }
        }

        private void ClampSaccades()
        {
            if (saccadeTarget.x > maxSaccade)
            {
                saccadeTarget.x = maxSaccade;
            }

            if (saccadeTarget.x < minSaccade)
            {
                saccadeTarget.x = minSaccade;
            }
        }

        protected void UpdateBlinking()
        {
            if (leftEyeOpen_Close < -1f)
            {
                leftEyeOpen_Close = 0f;
            }

            if (rightEyeOpen_Close < -1f)
            {
                rightEyeOpen_Close = 0f;
            }

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
                }
                else
                {
                    leftEyeOpen_Close = -1.01f;
                    rightEyeOpen_Close = -1.01f;
                }
            }
        }

        public void OnBeforeSerialize()
        {
           // Debug.Log("Before serialize: neck_down is " + neckUp_Down);
        }

        public void OnAfterDeserialize()
        {
            Debug.Log("After deserialize: neck_down is " + neckUp_Down);
        }
    }
}
