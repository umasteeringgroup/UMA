using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UMA;

public class UMACustomization : MonoBehaviour
{
	
	public UMAData umaData;
	public UMADynamicAvatar umaDynamicAvatar;
	public CameraTrack cameraTrack;
	private UMADnaHumanoid umaDna;
	private UMADnaTutorial umaTutorialDna;
	private GameObject MessagePanel;        // This is the instruction panel
	private GameObject DnaPanel;            // This is the parent panel
	private GameObject DnaScrollPanel;      // This is the scrollable panel that holds the sliders

	// Slider objects 
	private Slider HeightSlider;
	private Slider UpperMuscleSlider;
	private Slider UpperWeightSlider;
	private Slider LowerMuscleSlider;
	private Slider LowerWeightSlider;
	private Slider ArmLengthSlider;
	private Slider ForearmLengthSlider;
	private Slider LegSeparationSlider;
	private Slider HandSizeSlider;
	private Slider FeetSizeSlider;
	private Slider LegSizeSlider;
	private Slider ArmWidthSlider;
	private Slider ForearmWidthSlider;
	private Slider BreastSlider;
	private Slider BellySlider;
	private Slider WaistSizeSlider;
	private Slider GlueteusSizeSlider;
	private Slider HeadSizeSlider;
	private Slider NeckThickSlider;
	private Slider EarSizeSlider;
	private Slider EarPositionSlider;
	private Slider EarRotationSlider;
	private Slider NoseSizeSlider;
	private Slider NoseCurveSlider;
	private Slider NoseWidthSlider;
	private Slider NoseInclinationSlider;
	private Slider NosePositionSlider;
	private Slider NosePronuncedSlider;
	private Slider NoseFlattenSlider;
	private Slider ChinSizeSlider;
	private Slider ChinPronouncedSlider;
	private Slider ChinPositionSlider;
	private Slider MandibleSizeSlider;
	private Slider JawSizeSlider;
	private Slider JawPositionSlider;
	private Slider CheekSizeSlider;
	private Slider CheekPositionSlider;
	private Slider lowCheekPronSlider;
	private Slider ForeHeadSizeSlider;
	private Slider ForeHeadPositionSlider;
	private Slider LipSizeSlider;
	private Slider MouthSlider;
	private Slider EyeSizeSlider;
	private Slider EyeRotationSlider;
	private Slider EyeSpacingSlider;
	private Slider LowCheekPosSlider;
	private Slider HeadWidthSlider;
	private Rect ViewPortFull = new Rect(0, 0, 1, 1);
	private Rect ViewPortReduced;
	private Transform baseTarget;

	// get the sliders and store for later use
	void Awake()
	{
		HeightSlider = GameObject.Find("HeightSlider").GetComponent<Slider>();
		UpperMuscleSlider = GameObject.Find("UpperMuscleSlider").GetComponent<Slider>();
		UpperWeightSlider = GameObject.Find("UpperWeightSlider").GetComponent<Slider>();
		LowerMuscleSlider = GameObject.Find("LowerMuscleSlider").GetComponent<Slider>();
		LowerWeightSlider = GameObject.Find("LowerWeightSlider").GetComponent<Slider>();
		ArmLengthSlider = GameObject.Find("ArmLengthSlider").GetComponent<Slider>();
		ForearmLengthSlider = GameObject.Find("ForearmLengthSlider").GetComponent<Slider>();
		LegSeparationSlider = GameObject.Find("LegSepSlider").GetComponent<Slider>();
		HandSizeSlider = GameObject.Find("HandSizeSlider").GetComponent<Slider>();
		FeetSizeSlider = GameObject.Find("FeetSizeSlider").GetComponent<Slider>();
		LegSizeSlider = GameObject.Find("LegSizeSlider").GetComponent<Slider>();
		ArmWidthSlider = GameObject.Find("ArmWidthSlider").GetComponent<Slider>();
		ForearmWidthSlider = GameObject.Find("ForearmWidthSlider").GetComponent<Slider>();
		BreastSlider = GameObject.Find("BreastSizeSlider").GetComponent<Slider>();
		BellySlider = GameObject.Find("BellySlider").GetComponent<Slider>();
		WaistSizeSlider = GameObject.Find("WaistSizeSlider").GetComponent<Slider>();
		GlueteusSizeSlider = GameObject.Find("GluteusSlider").GetComponent<Slider>();
		HeadSizeSlider = GameObject.Find("HeadSizeSlider").GetComponent<Slider>();
		HeadWidthSlider = GameObject.Find("HeadWidthSlider").GetComponent<Slider>();
		NeckThickSlider = GameObject.Find("NeckSlider").GetComponent<Slider>();
		EarSizeSlider = GameObject.Find("EarSizeSlider").GetComponent<Slider>();
		EarPositionSlider = GameObject.Find("EarPosSlider").GetComponent<Slider>();
		EarRotationSlider = GameObject.Find("EarRotSlider").GetComponent<Slider>();
		NoseSizeSlider = GameObject.Find("NoseSizeSlider").GetComponent<Slider>();
		NoseCurveSlider = GameObject.Find("NoseCurveSlider").GetComponent<Slider>();
		NoseWidthSlider = GameObject.Find("NoseWidthSlider").GetComponent<Slider>();
		NoseInclinationSlider = GameObject.Find("NoseInclineSlider").GetComponent<Slider>();
		NosePositionSlider = GameObject.Find("NosePosSlider").GetComponent<Slider>();
		NosePronuncedSlider = GameObject.Find("NosePronSlider").GetComponent<Slider>();
		NoseFlattenSlider = GameObject.Find("NoseFlatSlider").GetComponent<Slider>();
		ChinSizeSlider = GameObject.Find("ChinSizeSlider").GetComponent<Slider>();
		ChinPronouncedSlider = GameObject.Find("ChinPronSlider").GetComponent<Slider>();
		ChinPositionSlider = GameObject.Find("ChinPosSlider").GetComponent<Slider>();
		MandibleSizeSlider = GameObject.Find("MandibleSizeSlider").GetComponent<Slider>();
		JawSizeSlider = GameObject.Find("JawSizeSlider").GetComponent<Slider>();
		JawPositionSlider = GameObject.Find("JawPosSlider").GetComponent<Slider>();
		CheekSizeSlider = GameObject.Find("CheekSizeSlider").GetComponent<Slider>();
		CheekPositionSlider = GameObject.Find("CheekPosSlider").GetComponent<Slider>();
		lowCheekPronSlider = GameObject.Find("LowCheekPronSlider").GetComponent<Slider>();
		ForeHeadSizeSlider = GameObject.Find("ForeheadSizeSlider").GetComponent<Slider>();
		ForeHeadPositionSlider = GameObject.Find("ForeheadPosSlider").GetComponent<Slider>();
		LipSizeSlider = GameObject.Find("LipSizeSlider").GetComponent<Slider>();
		MouthSlider = GameObject.Find("MouthSizeSlider").GetComponent<Slider>();
		EyeSizeSlider = GameObject.Find("EyeSizeSlider").GetComponent<Slider>();
		EyeRotationSlider = GameObject.Find("EyeRotSlider").GetComponent<Slider>();
		EyeSpacingSlider = GameObject.Find("EyeSpaceSlider").GetComponent<Slider>();
		LowCheekPosSlider = GameObject.Find("LowCheekPosSlider").GetComponent<Slider>();

		// Find the panels and hide for now
		MessagePanel = GameObject.Find("MessagePanel");
		DnaPanel = GameObject.Find("DnaEditorPanel");
		DnaScrollPanel = GameObject.Find("ScrollPanel");
		if ((MessagePanel == null) || (DnaPanel == null) || (DnaScrollPanel == null))
		{
			Debug.LogError("Could not locate required UI element!");
		}
		DnaPanel.SetActive(false);
	}

	protected virtual void Start()
	{
		float vpWidth;

		vpWidth = ((float)Screen.width - 175) / (float)Screen.width;    // Get the width of the screen so that we can adjust the viewport
		ViewPortReduced = new Rect(0, 0, vpWidth, 1);
		Camera.main.rect = ViewPortFull;
		baseTarget = GameObject.Find("UMACrowd").transform;             // Get the transform of the UMA Crown GO to use when retargeting the camera
	}

	void Update()
	{
		if (umaTutorialDna != null)
			EyeSpacingSlider.interactable = true;
		else
			EyeSpacingSlider.interactable = false;

		Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
		RaycastHit hit;
		
		if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
		{
			if (Physics.Raycast(ray, out hit, 100))
			{
				Transform tempTransform = hit.collider.transform;

				//Dont want to use an extra layer or specific tag on UMAs, and since UMAData has moved, Ill keep this for now
				if (tempTransform.parent)
				{
					if (tempTransform.parent.parent)
					{
						umaData = tempTransform.parent.parent.GetComponent<UMAData>();
					}
				}

				if (umaData)
				{
					AvatarSetup();
				}
			}
		}
	}

	private void UpdateShape()
	{
		if (umaData)
		{
			UpdateUMAShape();
		}
	}

	// An avatar has been selected, setup the camera, sliders, retarget the camera, and adjust the viewport
	// to account for the DNA slider scroll panel
	public void AvatarSetup()
	{
		umaDynamicAvatar = umaData.gameObject.GetComponent<UMADynamicAvatar>();
		
		if (cameraTrack)
		{
			cameraTrack.target = umaData.umaRoot.transform;
		}
		
		umaDna = umaData.GetDna<UMADnaHumanoid>();
		umaTutorialDna = umaData.GetDna<UMADnaTutorial>();

		SetSliders();
		SetCamera(true);
	}

	// Set the camera target and viewport
	private void SetCamera(bool show)
	{
		if (show)
		{
			Camera.main.rect = ViewPortReduced;
			MessagePanel.SetActive(false);
			DnaPanel.SetActive(true);
		} else
		{
			Camera.main.rect = ViewPortFull;
			cameraTrack.target = baseTarget;
			DnaPanel.SetActive(false);
			MessagePanel.SetActive(true);
		}
	}

	// Button callback to hide slider scroll panel
	public void HideDnaSlider()
	{
		SetCamera(false);
	}
	
	public void UpdateUMAAtlas()
	{
		umaData.isTextureDirty = true;
		umaData.Dirty();	
	}
	
	public void UpdateUMAShape()
	{
		umaData.isShapeDirty = true;
		umaData.Dirty();
	}

	// Set all of the sliders to the values contained in the UMA Character
	public void SetSliders()
	{
		HeightSlider.value = umaDna.height;
		UpperMuscleSlider.value = umaDna.upperMuscle;
		UpperWeightSlider.value = umaDna.upperWeight;
		LowerMuscleSlider.value = umaDna.lowerMuscle;
		LowerWeightSlider.value = umaDna.lowerWeight;
		ArmLengthSlider.value = umaDna.armLength;
		ForearmLengthSlider.value = umaDna.forearmLength;
		LegSeparationSlider.value = umaDna.legSeparation;
		HandSizeSlider.value = umaDna.handsSize;
		FeetSizeSlider.value = umaDna.feetSize;
		LegSizeSlider.value = umaDna.legsSize;
		ArmWidthSlider.value = umaDna.armWidth;
		ForearmWidthSlider.value = umaDna.forearmWidth;
		BreastSlider.value = umaDna.breastSize;
		BellySlider.value = umaDna.belly;
		WaistSizeSlider.value = umaDna.waist;
		GlueteusSizeSlider.value = umaDna.gluteusSize;
		HeadSizeSlider.value = umaDna.headSize;
		HeadWidthSlider.value = umaDna.headWidth;
		NeckThickSlider.value = umaDna.neckThickness;
		EarSizeSlider.value = umaDna.earsSize;
		EarPositionSlider.value = umaDna.earsPosition;
		EarRotationSlider.value = umaDna.earsRotation;
		NoseSizeSlider.value = umaDna.noseSize;
		NoseCurveSlider.value = umaDna.noseCurve;
		NoseWidthSlider.value = umaDna.noseWidth;
		NoseInclinationSlider.value = umaDna.noseInclination;
		NosePositionSlider.value = umaDna.nosePosition;
		NosePronuncedSlider.value = umaDna.nosePronounced;
		NoseFlattenSlider.value = umaDna.noseFlatten;
		ChinSizeSlider.value = umaDna.chinSize;
		ChinPronouncedSlider.value = umaDna.chinPronounced;
		ChinPositionSlider.value = umaDna.chinPosition;
		MandibleSizeSlider.value = umaDna.mandibleSize;
		JawSizeSlider.value = umaDna.jawsSize;
		JawPositionSlider.value = umaDna.jawsPosition;
		CheekSizeSlider.value = umaDna.cheekSize;
		CheekPositionSlider.value = umaDna.cheekPosition;
		lowCheekPronSlider.value = umaDna.lowCheekPronounced;
		ForeHeadSizeSlider.value = umaDna.foreheadSize;
		ForeHeadPositionSlider.value = umaDna.foreheadPosition;
		LipSizeSlider.value = umaDna.lipsSize;
		MouthSlider.value = umaDna.mouthSize;
		EyeSizeSlider.value = umaDna.eyeSize;
		EyeRotationSlider.value = umaDna.eyeRotation;
		LowCheekPosSlider.value = umaDna.lowCheekPosition;
		if (umaTutorialDna != null)
			EyeSpacingSlider.value = umaTutorialDna.eyeSpacing;
	}

	// Slider callbacks 
	public void OnHeightChange()
	{
		umaDna.height = HeightSlider.value;
		UpdateShape();
	}

	public void OnUpperMuscleChange()
	{
		umaDna.upperMuscle = UpperMuscleSlider.value;
		UpdateShape();
	}

	public void OnUpperWeightChange()
	{
		umaDna.upperWeight = UpperWeightSlider.value;
		UpdateShape();
	}

	public void OnLowerMuscleChange()
	{
		umaDna.lowerMuscle = LowerMuscleSlider.value;
		UpdateShape();
	}

	public void OnLowerWeightChange()
	{
		umaDna.lowerWeight = LowerWeightSlider.value;
		UpdateShape();
	}

	public void OnArmLengthChange()
	{
		umaDna.armLength = ArmLengthSlider.value;
		UpdateShape();
	}

	public void OnForearmLengthChange()
	{
		umaDna.forearmLength = ForearmLengthSlider.value;
		UpdateShape();
	}

	public void OnLegSeparationChange()
	{
		umaDna.legSeparation = LegSeparationSlider.value;
		UpdateShape();
	}

	public void OnHandSizeChange()
	{
		umaDna.handsSize = HandSizeSlider.value;
		UpdateShape();
	}

	public void OnFootSizeChange()
	{
		umaDna.feetSize = FeetSizeSlider.value;
		UpdateShape();
	}

	public void OnLegSizeChange()
	{
		umaDna.legsSize = LegSizeSlider.value;
		UpdateShape();
	}

	public void OnArmWidthChange()
	{
		umaDna.armWidth = ArmWidthSlider.value;
		UpdateShape();
	}

	public void OnForearmWidthChange()
	{
		umaDna.forearmWidth = ForearmWidthSlider.value;
		UpdateShape();
	}

	public void OnBreastSizeChange()
	{
		umaDna.breastSize = BreastSlider.value;
		UpdateShape();
	}

	public void OnBellySizeChange()
	{
		umaDna.belly = BellySlider.value;
		UpdateShape();
	}

	public void OnWaistSizeChange()
	{
		umaDna.waist = WaistSizeSlider.value;
		UpdateShape();
	}

	public void OnGluteusSizeChange()
	{
		umaDna.gluteusSize = GlueteusSizeSlider.value;
		UpdateShape();
	}

	public void OnHeadSizeChange()
	{
		umaDna.headSize = HeadSizeSlider.value;
		UpdateShape();
	}

	public void OnHeadWidthChange()
	{
		umaDna.headWidth = HeadWidthSlider.value;
		UpdateShape();
	}

	public void OnNeckThicknessChange()
	{
		umaDna.neckThickness = NeckThickSlider.value;
		UpdateShape();
	}

	public void OnEarSizeChange()
	{
		umaDna.earsSize = EarSizeSlider.value;
		UpdateShape();
	}

	public void OnEarPositionChange()
	{
		umaDna.earsPosition = EarPositionSlider.value;
		UpdateShape();
	}

	public void OnEarRotationChange()
	{
		umaDna.earsRotation = EarRotationSlider.value;
		UpdateShape();
	}

	public void OnNoseSizeChange()
	{
		umaDna.noseSize = NoseSizeSlider.value;
		UpdateShape();
	}

	public void OnNoseCurveChange()
	{
		umaDna.noseCurve = NoseCurveSlider.value;
		UpdateShape();
	}

	public void OnNoseWidthChange()
	{
		umaDna.noseWidth = NoseWidthSlider.value;
		UpdateShape();
	}

	public void OnNoseInclinationChange()
	{
		umaDna.noseInclination = NoseInclinationSlider.value;
		UpdateShape();
	}

	public void OnNosePositionChange()
	{
		umaDna.nosePosition = NosePositionSlider.value;
		UpdateShape();
	}

	public void OnNosePronouncedChange()
	{
		umaDna.nosePronounced = NosePronuncedSlider.value;
		UpdateShape();
	}

	public void OnNoseFlattenChange()
	{
		umaDna.noseFlatten = NoseFlattenSlider.value;
		UpdateShape();
	}

	public void OnChinSizeChange()
	{
		umaDna.chinSize = ChinSizeSlider.value;
		UpdateShape();
	}

	public void OnChinPronouncedChange()
	{
		umaDna.chinPronounced = ChinPronouncedSlider.value;
		UpdateShape();
	}

	public void OnChinPositionChange()
	{
		umaDna.chinPosition = ChinPositionSlider.value;
		UpdateShape();
	}

	public void OnMandibleSizeChange()
	{
		umaDna.mandibleSize = MandibleSizeSlider.value;
		UpdateShape();
	}

	public void OnJawSizeChange()
	{
		umaDna.jawsSize = JawSizeSlider.value;
		UpdateShape();
	}

	public void OnJawPositionChange()
	{
		umaDna.jawsPosition = JawPositionSlider.value;
		UpdateShape();
	}

	public void OnCheekSizeChange()
	{
		umaDna.cheekSize = CheekSizeSlider.value;
		UpdateShape();
	}

	public void OnCheekPositionChange()
	{
		umaDna.cheekPosition = CheekPositionSlider.value;
		UpdateShape();
	}

	public void OnCheekLowPronouncedChange()
	{
		umaDna.lowCheekPronounced = lowCheekPronSlider.value;
		UpdateShape();
	}

	public void OnForeheadSizeChange()
	{
		umaDna.foreheadSize = ForeHeadSizeSlider.value;
		UpdateShape();
	}

	public void OnForeheadPositionChange()
	{
		umaDna.foreheadPosition = ForeHeadPositionSlider.value;
		UpdateShape();
	}

	public void OnLipSizeChange()
	{
		umaDna.lipsSize = LipSizeSlider.value;
		UpdateShape();
	}

	public void OnMouthSizeChange()
	{
		umaDna.mouthSize = MouthSlider.value;
		UpdateShape();
	}

	public void OnEyeSizechange()
	{
		umaDna.eyeSize = EyeSizeSlider.value;
		UpdateShape();
	}

	public void OnEyeRotationChange()
	{
		umaDna.eyeRotation = EyeRotationSlider.value;
		UpdateShape();
	}

	public void OnLowCheekPositionChange()
	{
		umaDna.lowCheekPosition = LowCheekPosSlider.value;
		UpdateShape();
	}

	public void OnEyeSpacingChange()
	{
		if (umaTutorialDna != null)
			umaTutorialDna.eyeSpacing = EyeSpacingSlider.value;
		
		UpdateShape();
	}
}
