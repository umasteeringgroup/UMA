using UnityEngine;
using System.Collections;

namespace UMA.PoseTools
{
	public class SliderExpressionTest : MonoBehaviour
	{
		public Camera cam;
		private GameObject head;
		private UMADynamicAvatar umaAvatar = null;
		private UMAExpressionPlayer player;
		public UMAExpressionSet expressionSet = null;
		private bool initialized = false;
		private bool setValues = false;

		// Position variables
		public int xPos = 700;
		public int yPos = 25;
		public int uiWidth = 200;

		public Vector2 scrollPosition; // Position of the scrollview
		public GUIStyle labelStyle;
		float[] guiValues;

		public void characterCreated(UMAData umaCreated)
		{
			head = umaCreated.GetBoneGameObject("Head");

			MoveCamera();

			player = gameObject.GetComponentInChildren<UMAExpressionPlayer>();
			if (player == null)
			{
				Animator umaAnimator = GetComponentInChildren<Animator>();
				if (umaAnimator != null)
				{
					player = umaAnimator.gameObject.AddComponent<UMAExpressionPlayer>();
					player.overrideMecanimNeck = true;
					player.overrideMecanimHead = true;
					player.overrideMecanimJaw = true;
					player.overrideMecanimEyes = true;
				}

#if !UNITY_4_3
				umaAnimator.Rebind();
#endif
			}

			if (player != null)
			{
				if (expressionSet != null)
				{
					player.expressionSet = expressionSet;
				}

				player.Initialize();
				guiValues = new float[UMAExpressionPlayer.PoseCount];
			}
		}

		void MoveCamera()
		{
			if (head != null)
			{
				Vector3 camPos = cam.transform.position;
				camPos.y = head.transform.position.y;
				cam.transform.position = camPos;

				CameraTrack camScript = cam.GetComponent<CameraTrack>();
				if (camScript != null)
				{
					camScript.target = head.transform;
				}
			}
		}

		void Update()
		{
			if (!initialized)
			{
				UMAData umaData = gameObject.GetComponentInChildren<UMAData>();
				if (umaData != null)
				{
					umaData.OnCharacterCreated += characterCreated;

					umaAvatar = gameObject.GetComponentInChildren<UMADynamicAvatar>();
					xPos = Screen.width - uiWidth - 25;
					initialized = true;
				}
			}
			else if ((player != null) && (player.enabled) && setValues)
			{
				player.Values = guiValues;
			}
		}

		void OnEnable()
		{
			MoveCamera();
		}

		public void OnGUI()
		{
			if (!initialized || (player == null) || (!player.enabled))
			{
				return;
			}

			int scrollHeight = Screen.height - 2 * yPos;
			GUILayout.BeginArea(new Rect(xPos, yPos, uiWidth, scrollHeight));

			scrollPosition = GUILayout.BeginScrollView(scrollPosition);

			guiValues = player.Values;
			for (int i = 0; i < guiValues.Length; i++)
			{
				guiValues[i] = TargetSlider(guiValues[i], 1f, UMAExpressionPlayer.PoseNames[i]);
			}

			GUILayout.EndScrollView();

			if (umaAvatar != null)
			{
				Animator animator = umaAvatar.umaData.animator;
				if (animator != null)
				{
					if (animator.enabled)
					{
						if (GUILayout.Button("Pause Anim"))
						{
							animator.enabled = false;
						}
					}
					else if (GUILayout.Button("Play Anim"))
					{
						animator.enabled = true;
					}
				}

				if (GUILayout.Button("Randomize"))
				{
					UMADnaHumanoid humanoidDna = umaAvatar.umaData.GetDna<UMADnaHumanoid>();

					if (humanoidDna != null)
					{
						humanoidDna.headSize = Random.Range(0.475f, 0.525f);
						humanoidDna.headWidth = (Random.value + Random.value) / 2f;
						humanoidDna.neckThickness = (Random.value + Random.value) / 2f;

						humanoidDna.earsSize = (Random.value + Random.value) / 2f;
						humanoidDna.earsPosition = (Random.value + Random.value) / 2f;
						humanoidDna.earsRotation = (Random.value + Random.value) / 2f;
						humanoidDna.noseSize = (Random.value + Random.value) / 2f;
						humanoidDna.noseCurve = (Random.value + Random.value) / 2f;
						humanoidDna.noseWidth = (Random.value + Random.value) / 2f;
						humanoidDna.noseInclination = (Random.value + Random.value) / 2f;
						humanoidDna.nosePosition = (Random.value + Random.value) / 2f;
						humanoidDna.nosePronounced = (Random.value + Random.value) / 2f;
						humanoidDna.noseFlatten = (Random.value + Random.value) / 2f;

						humanoidDna.chinSize = (Random.value + Random.value) / 2f;
						humanoidDna.chinPronounced = (Random.value + Random.value) / 2f;
						humanoidDna.chinPosition = (Random.value + Random.value) / 2f;

						humanoidDna.mandibleSize = (Random.value + Random.value) / 2f;
						humanoidDna.jawsSize = (Random.value + Random.value) / 2f;
						humanoidDna.jawsPosition = (Random.value + Random.value) / 2f;

						humanoidDna.cheekSize = (Random.value + Random.value) / 2f;
						humanoidDna.cheekPosition = (Random.value + Random.value) / 2f;
						humanoidDna.lowCheekPronounced = (Random.value + Random.value) / 2f;
						humanoidDna.lowCheekPosition = (Random.value + Random.value) / 2f;

						humanoidDna.foreheadSize = (Random.value + Random.value) / 2f;
						humanoidDna.foreheadPosition = (Random.value + Random.value) / 2f;

						humanoidDna.lipsSize = (Random.value + Random.value) / 2f;
						humanoidDna.mouthSize = (Random.value + Random.value) / 2f;
						humanoidDna.eyeRotation = (Random.value + Random.value) / 2f;
						humanoidDna.eyeSize = (Random.value + Random.value) / 2f;

						umaAvatar.UpdateSameRace();
					}
				}
			}

#if UNITY_EDITOR
			if (GUILayout.Button("Save Expression"))
			{
				string assetPath = UnityEditor.EditorUtility.SaveFilePanelInProject("Save Expression Clip", "Expression", "anim", null);
				player.SaveExpressionClip(assetPath);
			}
#endif

			setValues = GUI.changed;

			GUILayout.EndArea();
		}

		public float TargetSlider(float sliderValue, float sliderMaxValue, string labelText)
		{
			GUILayout.Label(labelText, labelStyle);
			float sliderVal = GUILayout.HorizontalSlider(sliderValue, -sliderMaxValue, sliderMaxValue);
			return sliderVal;
		}
	}
}