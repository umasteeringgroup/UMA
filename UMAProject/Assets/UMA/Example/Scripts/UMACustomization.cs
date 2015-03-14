// ==========================================================
// Updated to use UGUI, March 2015
// Dennis Trevillyan - WatreGames
//
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
    private Slider[] sliders;
    private Rect ViewPortFull = new Rect(0, 0, 1, 1);
    private Rect ViewPortReduced;
    private Transform baseTarget;
    private Button DnaHide;

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
        DnaPanel = GameObject.Find("DnaEditorPanel");
        DnaScrollPanel = GameObject.Find("ScrollPanel");
        DnaPanel.SetActive(false);

        // Find the DNA hide button and hide it for now
        DnaHide = GameObject.Find("MessagePanel").GetComponentInChildren<Button>();
        DnaHide.active = false;
    }

	protected virtual void Start()
	{
        float vpWidth;

        sliders = DnaScrollPanel.GetComponentsInChildren<Slider>();     // Create an array of the sliders to use for initialization
        vpWidth = ((float)Screen.width - 175) / (float)Screen.width;    // Get the width of the screen so that we can adjust the viewport
        ViewPortReduced = new Rect(0, 0, vpWidth, 1);
        Camera.main.rect = ViewPortFull;
        baseTarget = GameObject.Find("UMACrowd").transform;             // Get the transform of the UMA Crown GO to use when retargeting the camera
	}

	void Update () 
    {
        if (umaTutorialDna != null) EyeSpacingSlider.interactable = true;
        else EyeSpacingSlider.interactable = false;

		Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
		RaycastHit hit;
		
		if(Input.GetMouseButtonDown(1))                
        {
			if (Physics.Raycast(ray, out hit, 100))
            {
				Transform tempTransform = hit.collider.transform;
				umaData = tempTransform.GetComponent<UMAData>();

				if(umaData)
                {
					AvatarSetup();
				}
			}
		}
		
        if(umaData)
        {
			UpdateUMAShape();
		}
	}

    // An avatar has been selected, setup the camera, sliders, retarget the camera, and adjust the viewport
    // to account for the DNA slider scroll panel
	public void AvatarSetup(){
		umaDynamicAvatar = umaData.gameObject.GetComponent<UMADynamicAvatar>();
		
		if(cameraTrack){
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
        Camera cam = cameraTrack.GetComponent<Camera>();
        if(show)
        {
            Camera.main.rect = ViewPortReduced;
            DnaPanel.SetActive(true);
            DnaHide.active = true;
        }

        else
        {
            cameraTrack.target = baseTarget;
            Camera.main.rect = ViewPortFull;
            DnaPanel.SetActive(false);
            DnaHide.active = false;
        }
    }

    // Button callback to hide slider scroll panel
    public void HideDnaSlider()
    {
        SetCamera(false);
    }
	
	public void UpdateUMAAtlas(){
		umaData.isTextureDirty = true;
		umaData.Dirty();	
	}
	
	public void UpdateUMAShape(){
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
        if (umaTutorialDna != null) EyeSpacingSlider.value = umaTutorialDna.eyeSpacing;
    }

    // Slider callbacks 
    public void OnHeightChange() { umaDna.height = HeightSlider.value; }
    public void OnUpperMuscleChange() { umaDna.upperMuscle = UpperMuscleSlider.value; }
    public void OnUpperWeightChange() { umaDna.upperWeight = UpperWeightSlider.value; }
    public void OnLowerMuscleChange() { umaDna.lowerMuscle = LowerMuscleSlider.value; }
    public void OnLowerWeightChange() { umaDna.lowerWeight = LowerWeightSlider.value; }
    public void OnArmLengthChange() { umaDna.armLength = ArmLengthSlider.value; }
    public void OnForearmLengthChange() { umaDna.forearmLength = ForearmLengthSlider.value; }
    public void OnLegSeparationChange() { umaDna.legSeparation = LegSeparationSlider.value; }
    public void OnHandSizeChange() { umaDna.handsSize = HandSizeSlider.value; }
    public void OnFootSizeChange() { umaDna.feetSize = FeetSizeSlider.value; }
    public void OnLegSizeChange() { umaDna.legsSize = LegSizeSlider.value; }
    public void OnArmWidthChange() { umaDna.armWidth = ArmWidthSlider.value; }
    public void OnForearmWidthChange() { umaDna.forearmWidth = ForearmWidthSlider.value; }
    public void OnBreastSizeChange() { umaDna.breastSize = BreastSlider.value; }
    public void OnBellySizeChange() { umaDna.belly = BellySlider.value; }
    public void OnWaistSizeChange() { umaDna.waist = WaistSizeSlider.value; }
    public void OnGluteusSizeChange() { umaDna.gluteusSize = GlueteusSizeSlider.value; }
    public void OnHeadSizeChange() { umaDna.headSize = HeadSizeSlider.value; }
    public void OnHeadWidthChange() { umaDna.headWidth = HeadWidthSlider.value; }
    public void OnNeckThicknessChange() { umaDna.neckThickness = NeckThickSlider.value; }
    public void OnEarSizeChange() { umaDna.earsSize = EarSizeSlider.value; }
    public void OnEarPositionChange() { umaDna.earsPosition = EarPositionSlider.value; }
    public void OnEarRotationChange() { umaDna.earsRotation = EarRotationSlider.value; }
    public void OnNoseSizeChange() { umaDna.noseSize = NoseSizeSlider.value; }
    public void OnNoseCurveChange() { umaDna.noseCurve = NoseCurveSlider.value; }
    public void OnNoseWidthChange() { umaDna.noseWidth = NoseWidthSlider.value; }
    public void OnNoseInclinationChange() { umaDna.noseInclination = NoseInclinationSlider.value; }
    public void OnNosePositionChange() { umaDna.nosePosition = NosePositionSlider.value; }
    public void OnNosePronouncedChange() { umaDna.nosePronounced = NosePronuncedSlider.value; }
    public void OnNoseFlattenChange() { umaDna.noseFlatten = NoseFlattenSlider.value; }
    public void OnChinSizeChange() { umaDna.chinSize = ChinSizeSlider.value; }
    public void OnChinPronouncedChange() { umaDna.chinPronounced = ChinPronouncedSlider.value; }
    public void OnChinPositionChange() { umaDna.chinPosition = ChinPositionSlider.value; }
    public void OnMandibleSizeChange() { umaDna.mandibleSize = MandibleSizeSlider.value; }
    public void OnJawSizeChange() { umaDna.jawsSize = JawSizeSlider.value; }
    public void OnJawPositionChange() { umaDna.jawsPosition = JawPositionSlider.value; }
    public void OnCheekSizeChange() { umaDna.cheekSize = CheekSizeSlider.value; }
    public void OnCheekPositionChange() { umaDna.cheekPosition = CheekPositionSlider.value; }
    public void OnCheekLowPronouncedChange() { umaDna.lowCheekPronounced = lowCheekPronSlider.value; }
    public void OnForeheadSizeChange() { umaDna.foreheadSize = ForeHeadSizeSlider.value; }
    public void OnForeheadPositionChange() { umaDna.foreheadPosition = ForeHeadPositionSlider.value; }
    public void OnLipSizeChange() { umaDna.lipsSize = LipSizeSlider.value; }
    public void OnMouthSizeChange() { umaDna.mouthSize = MouthSlider.value; }
    public void OnEyeSizechange() { umaDna.eyeSize = EyeSizeSlider.value; }
    public void OnEyeRotationChange() { umaDna.eyeRotation = EyeRotationSlider.value; }
    public void OnLowCheekPositionChange() { umaDna.lowCheekPosition = LowCheekPosSlider.value; }
    public void OnEyeSpacingChange() { if (umaTutorialDna != null) umaTutorialDna.eyeSpacing = EyeSpacingSlider.value; }
}
