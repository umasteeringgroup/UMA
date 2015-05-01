//	============================================================
//	Name:		ExpressionPlayer
//	Author: 	Eli Curtz
//	Copyright:	(c) 2013 Eli Curtz
//	============================================================

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections;

namespace UMA.PoseTools
{
	/// <summary>
	/// Base class for expression player. Defines animated channels and gaze variables.
	/// </summary>
	/// <remarks>
	/// The expression player channels are based loosely upon those from Jason Osipa's
	/// Stop Staring: Facial Modeling and Animation Done Right
	/// ISBN-13: 978-0470609903
	/// They could be implemented with either bone animation or blendshapes.
	/// </remarks>
	public class ExpressionPlayer : MonoBehaviour
	{
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
		/// The position which the eyes are focused on or moving toward.
		/// </summary>
		public Vector3 gazeTarget;
		public float gazeWeight = 0f;
		public GazeMode gazeMode = GazeMode.None;

		public bool overrideMecanimEyes = true;
		public bool overrideMecanimJaw = true;
		public bool overrideMecanimNeck = false;
		public bool overrideMecanimHead = false;

		public enum GazeMode : int
		{
			None = 0,
			Acquiring = 1,
			Following = 2,
			Speaking = 3,
			Listening = 4
		};

		public const int PoseCount = 36;
		/// <summary>
		/// Poses names as they appear for animations.
		/// </summary>
		static public readonly string[] PoseNames = 
		{
			"neckUp_Down",
			"neckLeft_Right",
			"neckTiltLeft_Right",
			"headUp_Down",
			"headLeft_Right",
			"headTiltLeft_Right",
			"jawOpen_Close",
			"jawForward_Back",
			"jawLeft_Right",
			"mouthLeft_Right",
			"mouthUp_Down",
			"mouthNarrow_Pucker",
			"tongueOut",
			"tongueCurl",
			"tongueUp_Down",
			"tongueLeft_Right",
			"tongueWide_Narrow",
			"leftMouthSmile_Frown",
			"rightMouthSmile_Frown",
			"leftLowerLipUp_Down",
			"rightLowerLipUp_Down",
			"leftUpperLipUp_Down",
			"rightUpperLipUp_Down",
			"leftCheekPuff_Squint",
			"rightCheekPuff_Squint",
			"noseSneer",
			"leftEyeOpen_Close",
			"rightEyeOpen_Close",
			"leftEyeUp_Down",
			"rightEyeUp_Down",
			"leftEyeIn_Out",
			"rightEyeIn_Out",
			"browsIn",
			"leftBrowUp_Down",
			"rightBrowUp_Down",
			"midBrowUp_Down"
		};

		public enum MecanimJoint : int
		{
			None = 0,
			Head = 1,
			Neck = 2,
			Jaw = 4,
			Eye = 8,
		};

		/// <summary>
		/// The Mecanim bone equivalent of each expression channel.
		/// </summary>
		static public readonly MecanimJoint[] MecanimAlternate = 
		{
			MecanimJoint.Neck, // neckUp_Down
			MecanimJoint.Neck, // neckLeft_Right
			MecanimJoint.Neck, // neckTiltLeft_Right
			MecanimJoint.Head, // headUp_Down
			MecanimJoint.Head, // headLeft_Right
			MecanimJoint.Head, // headTiltLeft_Right
			MecanimJoint.Jaw, // jawOpen_Close
			MecanimJoint.Jaw, // jawForward_Back
			MecanimJoint.Jaw, // jawLeft_Right
			MecanimJoint.None, // mouthLeft_Right
			MecanimJoint.None, // mouthUp_Down
			MecanimJoint.None, // mouthNarrow_Pucker
			MecanimJoint.None, // tongueOut
			MecanimJoint.None, // tongueCurl
			MecanimJoint.None, // tongueUp_Down
			MecanimJoint.None, // tongueLeft_Right
			MecanimJoint.None, // tongueWide_Narrow
			MecanimJoint.None, // leftMouthSmile_Frown
			MecanimJoint.None, // rightMouthSmile_Frown
			MecanimJoint.None, // leftLowerLipUp_Down
			MecanimJoint.None, // rightLowerLipUp_Down
			MecanimJoint.None, // leftUpperLipUp_Down
			MecanimJoint.None, // rightUpperLipUp_Down
			MecanimJoint.None, // leftCheekPuff_Squint
			MecanimJoint.None, // rightCheekPuff_Squint
			MecanimJoint.None, // noseSneer
			MecanimJoint.None, // leftEyeOpen_Close
			MecanimJoint.None, // rightEyeOpen_Close
			MecanimJoint.Eye, // leftEyeUp_Down
			MecanimJoint.Eye, // rightEyeUp_Down
			MecanimJoint.Eye, // leftEyeIn_Out
			MecanimJoint.Eye, // rightEyeIn_Out
			MecanimJoint.None, // browsIn
			MecanimJoint.None, // leftBrowUp_Down
			MecanimJoint.None, // rightBrowUp_Down
			MecanimJoint.None, // midBrowUp_Down"
		};

		// Pose values
		[Range(-1f, 1f)]
		public float neckUp_Down = 0f;
		[Range(-1f, 1f)]
		public float neckLeft_Right = 0f;
		[Range(-1f, 1f)]
		public float neckTiltLeft_Right = 0f;
		[Range(-1f, 1f)]
		public float headUp_Down = 0f;
		[Range(-1f, 1f)]
		public float headLeft_Right = 0f;
		[Range(-1f, 1f)]
		public float headTiltLeft_Right = 0f;
		[Range(-1f, 1f)]
		public float jawOpen_Close = 0f;
		[Range(-1f, 1f)]
		public float jawForward_Back = 0f;
		[Range(-1f, 1f)]
		public float jawLeft_Right = 0f;
		[Range(-1f, 1f)]
		public float mouthLeft_Right = 0f;
		[Range(-1f, 1f)]
		public float mouthUp_Down = 0f;
		[Range(-1f, 1f)]
		public float mouthNarrow_Pucker = 0f;
		[Range(-1f, 1f)]
		public float tongueOut = 0f;
		[Range(0f, 1f)]
		public float tongueCurl = 0f;
		[Range(-1f, 1f)]
		public float tongueUp_Down = 0f;
		[Range(-1f, 1f)]
		public float tongueLeft_Right = 0f;
		[Range(-1f, 1f)]
		public float tongueWide_Narrow = 0f;
		[Range(-1f, 1f)]
		public float leftMouthSmile_Frown = 0f;
		[Range(-1f, 1f)]
		public float rightMouthSmile_Frown = 0f;
		[Range(-1f, 1f)]
		public float leftLowerLipUp_Down = 0f;
		[Range(-1f, 1f)]
		public float rightLowerLipUp_Down = 0f;
		[Range(-1f, 1f)]
		public float leftUpperLipUp_Down = 0f;
		[Range(-1f, 1f)]
		public float rightUpperLipUp_Down = 0f;
		[Range(-1f, 1f)]
		public float leftCheekPuff_Squint = 0f;
		[Range(-1f, 1f)]
		public float rightCheekPuff_Squint = 0f;
		[Range(0f, 1f)]
		public float noseSneer = 0f;
		[Range(-1f, 1f)]
		public float leftEyeOpen_Close = 0f;
		[Range(-1f, 1f)]
		public float rightEyeOpen_Close = 0f;
		[Range(-1f, 1f)]
		public float leftEyeUp_Down = 0f;
		[Range(-1f, 1f)]
		public float rightEyeUp_Down = 0f;
		[Range(-1f, 1f)]
		public float leftEyeIn_Out = 0f;
		[Range(-1f, 1f)]
		public float rightEyeIn_Out = 0f;
		[Range(0f, 1f)]
		public float browsIn = 0f;
		[Range(-1f, 1f)]
		public float leftBrowUp_Down = 0f;
		[Range(-1f, 1f)]
		public float rightBrowUp_Down = 0f;
		[Range(-1f, 1f)]
		public float midBrowUp_Down = 0f;

		private float[] valueArray = new float[PoseCount];
		public float[] Values
		{
			get
			{
				valueArray[0] = neckUp_Down;
				valueArray[1] = neckLeft_Right;
				valueArray[2] = neckTiltLeft_Right;
				valueArray[3] = headUp_Down;
				valueArray[4] = headLeft_Right;
				valueArray[5] = headTiltLeft_Right;
				valueArray[6] = jawOpen_Close;
				valueArray[7] = jawForward_Back;
				valueArray[8] = jawLeft_Right;
				valueArray[9] = mouthLeft_Right;
				valueArray[10] = mouthUp_Down;
				valueArray[11] = mouthNarrow_Pucker;
				valueArray[12] = tongueOut;
				valueArray[13] = tongueCurl;
				valueArray[14] = tongueUp_Down;
				valueArray[15] = tongueLeft_Right;
				valueArray[16] = tongueWide_Narrow;
				valueArray[17] = leftMouthSmile_Frown;
				valueArray[18] = rightMouthSmile_Frown;
				valueArray[19] = leftLowerLipUp_Down;
				valueArray[20] = rightLowerLipUp_Down;
				valueArray[21] = leftUpperLipUp_Down;
				valueArray[22] = rightUpperLipUp_Down;
				valueArray[23] = leftCheekPuff_Squint;
				valueArray[24] = rightCheekPuff_Squint;
				valueArray[25] = noseSneer;
				valueArray[26] = leftEyeOpen_Close;
				valueArray[27] = rightEyeOpen_Close;
				valueArray[28] = leftEyeUp_Down;
				valueArray[29] = rightEyeUp_Down;
				valueArray[30] = leftEyeIn_Out;
				valueArray[31] = rightEyeIn_Out;
				valueArray[32] = browsIn;
				valueArray[33] = leftBrowUp_Down;
				valueArray[34] = rightBrowUp_Down;
				valueArray[35] = midBrowUp_Down;

				return valueArray;
			}
			set
			{
				if (value.Length != PoseCount) return;

				int i = 0;
				neckUp_Down = value[i++];
				neckLeft_Right = value[i++];
				neckTiltLeft_Right = value[i++];
				headUp_Down = value[i++];
				headLeft_Right = value[i++];
				headTiltLeft_Right = value[i++];
				jawOpen_Close = value[i++];
				jawForward_Back = value[i++];
				jawLeft_Right = value[i++];
				mouthLeft_Right = value[i++];
				mouthUp_Down = value[i++];
				mouthNarrow_Pucker = value[i++];
				tongueOut = value[i++];
				tongueCurl = value[i++];
				tongueUp_Down = value[i++];
				tongueLeft_Right = value[i++];
				tongueWide_Narrow = value[i++];
				leftMouthSmile_Frown = value[i++];
				rightMouthSmile_Frown = value[i++];
				leftLowerLipUp_Down = value[i++];
				rightLowerLipUp_Down = value[i++];
				leftUpperLipUp_Down = value[i++];
				rightUpperLipUp_Down = value[i++];
				leftCheekPuff_Squint = value[i++];
				rightCheekPuff_Squint = value[i++];
				noseSneer = value[i++];
				leftEyeOpen_Close = value[i++];
				rightEyeOpen_Close = value[i++];
				leftEyeUp_Down = value[i++];
				rightEyeUp_Down = value[i++];
				leftEyeIn_Out = value[i++];
				rightEyeIn_Out = value[i++];
				browsIn = value[i++];
				leftBrowUp_Down = value[i++];
				rightBrowUp_Down = value[i++];
				midBrowUp_Down = value[i++];
			}
		}

#if UNITY_EDITOR
		/// <summary>
		/// Name of the primary (positive values) pose. Editor only.
		/// </summary>
		/// <returns>The pose name.</returns>
		/// <param name="index">Index.</param>
		static public string PrimaryPoseName(int index)
		{
			string name = ObjectNames.NicifyVariableName(PoseNames[index]);
			int underscore = name.IndexOf('_');

			if (underscore < 0)
				return name;

			return name.Substring(0, underscore);
		}

		/// <summary>
		/// Name of the inverse (negative values) pose. Editor only.
		/// </summary>
		/// <returns>The pose name.</returns>
		/// <param name="index">Index.</param>
		static public string InversePoseName(int index)
		{
			string name = ObjectNames.NicifyVariableName(PoseNames[index]);
			int underscore = name.IndexOf('_');

			if (underscore < 0)
				return null;

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
#if UNITY_4_6
			if (legacyAnimation)
			{
				AnimationUtility.SetAnimationType(clip, ModelImporterAnimationType.Legacy);
			}
			else
			{
				AnimationUtility.SetAnimationType(clip, ModelImporterAnimationType.Generic);
			}
#else
			if (legacyAnimation)
			{
				clip.legacy = true;
			}
#endif
			float[] values = Values;
			for (int i = 0; i < ExpressionPlayer.PoseCount; i++)
			{
				string pose = ExpressionPlayer.PoseNames[i];
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

	}
}