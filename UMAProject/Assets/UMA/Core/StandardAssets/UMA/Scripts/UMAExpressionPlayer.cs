//	============================================================
//	Name:		UMAExpressionPlayer
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================
using UMA.CharacterSystem;
using UnityEngine;

namespace UMA.PoseTools
{
    /// <summary>
    /// UMA specific expression player.
    /// </summary>
    [ExecuteInEditMode]
	public class UMAExpressionPlayer : ExpressionPlayer
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
			Initialize();
		}

		public void Initialize()
        {
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
						umaData.CharacterBegun.AddListener(CharacterBegun);
						umaData.CharacterUpdated.AddListener(UmaData_OnCharacterUpdated);
					}
					else
                    {
						standAlone = true;
						animator = gameObject.GetComponentInChildren<Animator>();
						SetupBones();
					}
				}
			}

			if (umaData != null)
			{
				animator = gameObject.GetComponentInChildren<Animator>();
				SetupBones();
			}

			processing = true;
			initialized = true;
        }

        private void CharacterBegun(UMAData _umaData)
        {
			this.umaData = _umaData;
			processing = false;
        }

		private void SetupBones()
		{
			if ((expressionSet != null) /*&& (umaData != null) && (umaData.skeleton != null)*/)
			{
				Transform jaw = null;
				Transform neck = null;
				Transform head = null;

				if (umaData.animator != null && umaData.animator.avatar != null)
				{
					jawHash = 0;
					neckHash = 0;
					headHash = 0;
					animator = umaData.animator;
					jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
					if (jaw != null)
                    {
                        jawHash = UMAUtils.StringToHash(jaw.name);
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
				if (overrideMecanimJaw && jaw == null)
				{
					if (Debug.isDebugBuild)
					{
						Debug.Log("Jaw bone not found, but jaw override is requested. This will be ignored in a production build.");
					}
					overrideMecanimJaw = false;
				}
				if (overrideMecanimNeck && neck == null)
				{
					if (Debug.isDebugBuild)
					{
						Debug.Log("Neck bone not found, but neck override is requested. This will be ignored in a production build.");
					}
					overrideMecanimNeck = false;
				}
				if (overrideMecanimHead && head == null)
				{
					if (Debug.isDebugBuild)
					{
						Debug.Log("Head bone not found, but head override is requested. This will be ignored in a production build.");
					}
					overrideMecanimHead = false;
				}
			}
		}

        private void UmaData_OnCharacterUpdated(UMAData obj)
        {
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

        void Update()
		{
			if (standAlone != true)
			{
				if (!initialized || umaData == null)
				{
					Initialize();
					return;
				}
			}

			if (!processing)
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

            // Fix for animation systems which require consistent values frame to frame
            Quaternion headRotation = Quaternion.identity;
			Quaternion neckRotation = Quaternion.identity;

			if (!overrideMecanimHead && headHash != 0)
            {
				headRotation = umaData.skeleton.GetRotation(headHash);
			}
			if (!overrideMecanimNeck && neckHash != 0)
            {
				neckRotation = umaData.skeleton.GetRotation(neckHash);
			}

			// Need to reset bones here if we want Mecanim animation
			if (expressionSet != null)
            {
                expressionSet.RestoreBones(umaData.skeleton, logResetErrors);
            }

            if (!overrideMecanimNeck && neckHash != 0)
            {
                umaData.skeleton.SetRotation(neckHash, neckRotation);
            }

            if (!overrideMecanimHead && headHash != 0)
            {
                umaData.skeleton.SetRotation(headHash, headRotation);
            }

		}

        private void OnAnimatorIK(int layerIndex)
        {
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
			if (!processing)
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

			if (!overrideMecanimHands)
            {
				mecanimMask |= MecanimJoint.Hands;
            }

			if (overrideMecanimJaw)
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
                } else
				{
					leftEyeOpen_Close = -1.01f;
					rightEyeOpen_Close = -1.01f;
				}
			}
		}

	}
}
