using UnityEngine;
using UMA.Examples;

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

				umaAnimator.Rebind();
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
					UMADnaBase humanoidDna = umaAvatar.umaData.GetDna(UMAUtils.StringToHash("UMADnaHumanoid"));

					if (humanoidDna != null)
					{
						for (int i = 0; i < humanoidDna.Count; i++)
						{
							humanoidDna.SetValue(i, (Random.value + Random.value) / 2f);
						}
						humanoidDna.SetValue("headSize", Random.Range(0.475f, 0.525f));

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