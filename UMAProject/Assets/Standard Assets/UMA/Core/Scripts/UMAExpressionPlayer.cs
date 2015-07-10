//	============================================================
//	Name:		UMAExpressionPlayer
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA.PoseTools
{
	/// <summary>
	/// UMA specific expression player.
	/// </summary>
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
		private bool initialized = false;
		[System.NonSerialized]
		public int SlotUpdateVsCharacterUpdate;

		// Use this for initialization
		void Start()
		{
			Initialize();
		}

		public void Initialize()
		{
			blinkDelay = Random.Range(minBlinkDelay, maxBlinkDelay);

			if (umaData == null)
			{
				// Find the UMAData, which could be up or down the hierarchy
				umaData = gameObject.GetComponentInChildren<UMAData>();
				if (umaData == null)
				{
#if UNITY_4_3
					umaData = transform.root.GetComponentInChildren<UMAData>();
#else
					umaData = gameObject.GetComponentInParent<UMAData>();
#endif
				}
				if (umaData == null)
				{
					Debug.LogError("Couldn't locate UMAData component");
				}
			}

			if ((expressionSet != null) && (umaData != null) && (umaData.skeleton != null))
			{
				if (umaData.animator != null)
				{
					Transform jaw = umaData.animator.GetBoneTransform(HumanBodyBones.Jaw);
					if (jaw != null)
						jawHash = UMAUtils.StringToHash(jaw.name);
				}
				initialized = true;
			}
		}

		void Update()
		{
			if (!initialized)
				return;

			// Need to reset bones here if we want Mecanim animation
			expressionSet.ResetBones(umaData.skeleton);

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
				return;

			if (umaData == null || umaData.skeleton == null)
				return;

			if (enableSaccades)
				UpdateSaccades();

			if (enableBlinking)
				UpdateBlinking();

			float[] values = Values;
			MecanimJoint mecanimMask = MecanimJoint.None;
			if (!overrideMecanimNeck)
				mecanimMask |= MecanimJoint.Neck;
			if (!overrideMecanimHead)
				mecanimMask |= MecanimJoint.Head;
			if (!overrideMecanimJaw)
				mecanimMask |= MecanimJoint.Jaw;
			if (!overrideMecanimEyes)
				mecanimMask |= MecanimJoint.Eye;
			if (overrideMecanimJaw)
			{
				umaData.skeleton.Reset(jawHash);
			}

			for (int i = 0; i < values.Length; i++)
			{
				if ((MecanimAlternate[i] & mecanimMask) != MecanimJoint.None)
				{
					continue;
				}

				float weight = values[i];
				if (weight == 0f)
					continue;

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

		const float eyeMovementRange = 30f;
		const float mutualGazeRange = 0.10f;
		const float MinSaccadeDelay = 0.25f;

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
				
				float saccadeMagnitude = Random.Range(0.01f, 15f);
				float saccadeDistance = (-6.9f / eyeMovementRange) * Mathf.Log(saccadeMagnitude / 15.7f);
				saccadeDuration = 0.021f + 0.0022f * saccadeDistance * eyeMovementRange;
				saccadeProgress = 0f;
				
				switch (gazeMode)
				{
					case GazeMode.Listening:
						if (Mathf.Abs(saccadeDistance) < mutualGazeRange)
							saccadeDelay = UMAUtils.GaussianRandom(237.5f / 30f, 47.1f / 30f);
						else
							saccadeDelay = UMAUtils.GaussianRandom(13f / 30f, 7.1f / 30f);
						break;
					
					default:
						if (Mathf.Abs(saccadeDistance) < mutualGazeRange)
							saccadeDelay = UMAUtils.GaussianRandom(93.9f / 30f, 94.9f / 30f);
						else
							saccadeDelay = UMAUtils.GaussianRandom(27.8f / 30f, 24f / 30f);
						break;
				}
				
				if (saccadeDelay < MinSaccadeDelay)
					saccadeDelay = MinSaccadeDelay;
				
				saccadeTarget *= saccadeDistance;
			}
			
			if (saccadeProgress < 1f)
			{
				float timeProgress = Time.deltaTime / saccadeDuration;
				float progressRate = 1.5f - 3f * Mathf.Pow(saccadeProgress - 0.5f, 2);
				saccadeProgress += timeProgress * progressRate;
				
				leftEyeIn_Out = Mathf.Lerp(saccadeTargetPrev.x, saccadeTarget.x, saccadeProgress);
				leftEyeUp_Down = Mathf.Lerp(saccadeTargetPrev.y, saccadeTarget.y, saccadeProgress);
				rightEyeIn_Out = Mathf.Lerp(-saccadeTargetPrev.x, -saccadeTarget.x, saccadeProgress);
				rightEyeUp_Down = Mathf.Lerp(saccadeTargetPrev.y, saccadeTarget.y, saccadeProgress);
			} else
			{
				leftEyeIn_Out = saccadeTarget.x;
				leftEyeUp_Down = saccadeTarget.y;
				rightEyeIn_Out = -saccadeTarget.x;
				rightEyeUp_Down = saccadeTarget.y;
			}
		}

		protected void UpdateBlinking()
		{
			if (leftEyeOpen_Close < -1f)
				leftEyeOpen_Close = 0f;
			if (rightEyeOpen_Close < -1f)
				rightEyeOpen_Close = 0f;
			
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
						blinkDelay = blinkDuration;
				} else
				{
					leftEyeOpen_Close = -1.01f;
					rightEyeOpen_Close = -1.01f;
				}
			}
		}

	}
}
