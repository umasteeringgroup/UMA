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
	public class UMAExpressionPlayer : ExpressionPlayer
	{
		public UMAExpressionSet expressionSet;

		public float minWeight = 0f;

		[System.NonSerialized]
		public UMAData umaData;
		private int jawHash = 0;
		private bool initialized = false;

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
					if (jaw != null) jawHash = UMASkeleton.StringToHash(jaw.name);
				}
				initialized = true;
			}
		}

		void Update()
		{
			if (!initialized) return;

			// Need to reset bones here if we want Mecanim animation
			expressionSet.ResetBones(umaData.skeleton);

			if (gazeWeight > 0f)
			{
				if (umaData.animator != null) {
					umaData.animator.SetLookAtPosition(gazeTarget);
					umaData.animator.SetLookAtWeight(gazeWeight);
				}
			}
		}

		void LateUpdate()
		{
			if (!initialized) return;

			if (enableSaccades)
			{
				saccadeDelay -= Time.deltaTime;
				if (saccadeDelay < 0f)
				{
					int saccadeDirection = Random.Range(0, 4);
					float saccadeOffset = GaussianRandom(0f, 0.125f);
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
					float saccadeDistance = (-6.9f / 40f) * Mathf.Log(saccadeMagnitude/15.7f);

					const float mutualGazeRange = 0.15f;
					switch (gazeMode)
					{
					case GazeMode.Listening:
						if (Mathf.Abs(saccadeDistance) < mutualGazeRange)
							saccadeDelay = GaussianRandom(237.5f / 30f, 47.1f / 30f);
						else
							saccadeDelay = GaussianRandom(13f / 30f, 7.1f / 30f);
						break;

					default:
						if (Mathf.Abs(saccadeDistance) < mutualGazeRange)
							saccadeDelay = GaussianRandom(93.9f / 30f, 94.9f / 30f);
						else
							saccadeDelay = GaussianRandom(27.8f / 30f, 24f / 30f);
						break;
					}

					if (saccadeDelay < 0.1f) saccadeDelay = 0.1f;

					saccadeTarget *= saccadeDistance;
				}

				leftEyeIn_Out = saccadeTarget.x;
				leftEyeUp_Down = saccadeTarget.y;
				rightEyeIn_Out = -saccadeTarget.x;
				rightEyeUp_Down = saccadeTarget.y;
			}

			if (enableBlinking)
			{
				if (leftEyeOpen_Close < -1f) leftEyeOpen_Close = 0f;
				if (rightEyeOpen_Close < -1f) rightEyeOpen_Close = 0f;

				blinkDelay -= Time.deltaTime;
				if (blinkDelay < blinkDuration)
				{
					if (blinkDelay < 0f)
					{
						blinkDelay = Random.Range(minBlinkDelay, maxBlinkDelay);
					}
					else
					{
						leftEyeOpen_Close = -1.01f;
						rightEyeOpen_Close = -1.01f;
					}
				}
			}
			
			float[] values = Values;
			MecanimJoint mecanimMask = MecanimJoint.None;
			if (!overrideMecanimNeck) mecanimMask |= MecanimJoint.Neck;
			if (!overrideMecanimHead) mecanimMask |= MecanimJoint.Head;
			if (!overrideMecanimJaw) mecanimMask |= MecanimJoint.Jaw;
			if (!overrideMecanimEyes) mecanimMask |= MecanimJoint.Eye;
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
				if (weight == 0f) continue;

				UMABonePose pose = null;
				if (weight > 0)
				{
					pose = expressionSet.posePairs[i].primary;
				}
				else
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
	}
}
