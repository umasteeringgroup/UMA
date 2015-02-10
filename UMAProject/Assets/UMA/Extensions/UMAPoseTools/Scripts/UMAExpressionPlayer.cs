//	============================================================
//	Name:		UMAExpressionPlayer
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

using UMA;

namespace UMA.PoseTools
{
	public class UMAExpressionPlayer : ExpressionPlayer
	{
		public UMAExpressionSet expressionSet;

		public float minWeight = 0f;

		private UMAData umaData;
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
		}

		void LateUpdate()
		{
			if (!initialized) return;

			if (enableBlinking)
			{
				leftEyeOpen_Close = 0f;
				rightEyeOpen_Close = 0f;

				blinkDelay -= Time.deltaTime;
				if (blinkDelay < blinkDuration)
				{
					if (blinkDelay < 0f)
					{
						blinkDelay = Random.Range(minBlinkDelay, maxBlinkDelay);
					}
					else
					{
						leftEyeOpen_Close = -1f;
						rightEyeOpen_Close = -1f;
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
