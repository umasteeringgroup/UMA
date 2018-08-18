using UnityEngine;
using System.Collections;

namespace UMA.PoseTools
{
	[ExecuteInEditMode]
	public class EditModeExpressionPreview : MonoBehaviour
	{
		public ExpressionPlayer expressionPlayer;
		public UMAExpressionSet expressionSet;
		public Transform skeletonRoot;

		protected UMASkeleton skeleton;

		void OnRenderObject()
		{
			if (expressionSet == null) return;
			if (skeleton == null) return;

			expressionSet.RestoreBones(skeleton);
		}

		void Update()
		{
			if (expressionSet == null) return;
			if (skeletonRoot == null) return;
			if (expressionPlayer == null)
			{
				expressionPlayer = gameObject.GetComponent<ExpressionPlayer>();
				if (expressionPlayer == null)
				{
					if (Debug.isDebugBuild)
						Debug.LogWarning("Couldn't find expression player to preview!");
					return;
				}
			}

			if (skeleton == null)
			{
				skeleton = new UMASkeleton(skeletonRoot);
			}

			expressionSet.RestoreBones(skeleton);

			float[] values = expressionPlayer.Values;

			for (int i = 0; i < values.Length; i++)
			{
				float weight = values[i];

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
				if (pose == null) continue;

				pose.ApplyPose(skeleton, weight);
			}
		}
	}
}