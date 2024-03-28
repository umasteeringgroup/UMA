// ==========================================================
// Updated to use UGUI, March 2015
// Dennis Trevillyan - WatreGames
//
using UnityEngine;
using UnityEngine.UI;

namespace UMA.Examples
{
    public class UMACustomization : MonoBehaviour
    {
        public UMAData umaData;
        public UMADynamicAvatar umaDynamicAvatar;
        public CameraTrack cameraTrack;
        public UMAMouseOrbitImproved orbitor;

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
        private Rect ViewPortReduced;
        private Transform baseTarget;
        private Button DnaHide;

        // get the sliders and store for later use
        void Awake()
        {
			// Find the panels and hide for now
			DnaPanel = GameObject.Find("DnaEditorPanel");

			if (DnaPanel == null || DnaPanel.activeSelf == false)
            {
                return;
            }

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

			DnaPanel.SetActive(false);

			var oldUIMask = DnaPanel.GetComponent<Mask>();
            if (oldUIMask != null)
            {
                DestroyImmediate(oldUIMask);
                DnaPanel.AddComponent<RectMask2D>();
            }

            // Find the DNA hide button and hide it for now
            DnaHide = GameObject.Find("MessagePanel").GetComponentInChildren<Button>();
            DnaHide.gameObject.SetActive(false);
        }

        protected virtual void Start()
        {
            //float vpWidth;

            //        sliders = DnaScrollPanel.GetComponentsInChildren<Slider>();     // Create an array of the sliders to use for initialization
            //vpWidth = ((float)Screen.width - 175) / (float)Screen.width;    // Get the width of the screen so that we can adjust the viewport
            //ViewPortReduced = new Rect(0, 0, vpWidth, 1);
            //Camera.main.rect = ViewPortFull;
            baseTarget = GameObject.Find("UMACrowd").transform;             // Get the transform of the UMA Crown GO to use when retargeting the camera
        }

        void Update()
        {
            /*if (umaTutorialDna != null)
                EyeSpacingSlider.interactable = true;
            else
                EyeSpacingSlider.interactable = false;*/

            // Don't raycast if the editor is open
            if (umaData != null)
            {
                return;
            }

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
            {
                if (Physics.Raycast(ray, out hit, 100))
                {
                    Transform tempTransform = hit.collider.transform;
                    umaData = tempTransform.GetComponent<UMAData>();

                    if (umaData)
                    {
                        AvatarSetup();
                    }
                }
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
            if (orbitor)
            {
                orbitor.target = umaData.umaRoot.transform;
            }

            umaDna = umaData.GetDna<UMADnaHumanoid>();
            umaTutorialDna = umaData.GetDna<UMADnaTutorial>();

			if (umaDna != null)
			{
				SetSliders();
			}
			//SetUpDNASliders();

			SetCamera(true);
        }

        // Set the camera target and viewport
        private void SetCamera(bool show)
        {
			if (DnaPanel == null)
            {
                return;
            }

            if (show)
            {
                DnaPanel.SetActive(true);
                DnaHide.gameObject.SetActive(true);

                // really Unity? Yes we change the value and set it back to trigger a ui recalculation... 
                // because setting the damn game object active doesn't do that!
                var rt = DnaPanel.GetComponent<RectTransform>();
                var pos = rt.offsetMin;
                rt.offsetMin = new Vector2(pos.x + 1, pos.y);
                rt.offsetMin = pos;
            }
            else
            {
                if (cameraTrack != null)
                {
                    cameraTrack.target = baseTarget;
                }

                if (orbitor != null)
                {
                    orbitor.target = baseTarget;
                }

                DnaPanel.SetActive(false);
                DnaHide.gameObject.SetActive(false);
                umaData = null;
                umaDna = null;
                umaTutorialDna = null;
            }
        }

        // Button callback to hide slider scroll panel
        public void HideDnaSlider()
        {
            SetCamera(false);
        }


        public void UpdateUMAAtlas()
        {
            if (umaData != null)
            {
                umaData.isTextureDirty = true;
                umaData.Dirty();
            }
        }

        public void UpdateUMAShape()
        {
            if (umaData != null)
            {
                umaData.isShapeDirty = true;
                umaData.Dirty();
            }
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
            {
                EyeSpacingSlider.value = umaTutorialDna.eyeSpacing;
            }
        }

        // Slider callbacks 
        public void OnHeightChange() { if (umaDna != null) { umaDna.height = HeightSlider.value; } UpdateUMAShape(); }
        public void OnUpperMuscleChange() { if (umaDna != null) { umaDna.upperMuscle = UpperMuscleSlider.value; } UpdateUMAShape(); }
        public void OnUpperWeightChange() { if (umaDna != null) { umaDna.upperWeight = UpperWeightSlider.value; } UpdateUMAShape(); }
        public void OnLowerMuscleChange() { if (umaDna != null) { umaDna.lowerMuscle = LowerMuscleSlider.value; } UpdateUMAShape(); }
        public void OnLowerWeightChange() { if (umaDna != null) { umaDna.lowerWeight = LowerWeightSlider.value; } UpdateUMAShape(); }
        public void OnArmLengthChange() { if (umaDna != null) { umaDna.armLength = ArmLengthSlider.value; } UpdateUMAShape(); }
        public void OnForearmLengthChange() { if (umaDna != null) { umaDna.forearmLength = ForearmLengthSlider.value; } UpdateUMAShape(); }
        public void OnLegSeparationChange() { if (umaDna != null) { umaDna.legSeparation = LegSeparationSlider.value; } UpdateUMAShape(); }
        public void OnHandSizeChange() { if (umaDna != null) { umaDna.handsSize = HandSizeSlider.value; } UpdateUMAShape(); }
        public void OnFootSizeChange() { if (umaDna != null) { umaDna.feetSize = FeetSizeSlider.value; } UpdateUMAShape(); }
        public void OnLegSizeChange() { if (umaDna != null) { umaDna.legsSize = LegSizeSlider.value; } UpdateUMAShape(); }
        public void OnArmWidthChange() { if (umaDna != null) { umaDna.armWidth = ArmWidthSlider.value; } UpdateUMAShape(); }
        public void OnForearmWidthChange() { if (umaDna != null) { umaDna.forearmWidth = ForearmWidthSlider.value; } UpdateUMAShape(); }
        public void OnBreastSizeChange() { if (umaDna != null) { umaDna.breastSize = BreastSlider.value; } UpdateUMAShape(); }
        public void OnBellySizeChange() { if (umaDna != null) { umaDna.belly = BellySlider.value; } UpdateUMAShape(); }
        public void OnWaistSizeChange() { if (umaDna != null) { umaDna.waist = WaistSizeSlider.value; } UpdateUMAShape(); }
        public void OnGluteusSizeChange() { if (umaDna != null) { umaDna.gluteusSize = GlueteusSizeSlider.value; } UpdateUMAShape(); }
        public void OnHeadSizeChange() { if (umaDna != null) { umaDna.headSize = HeadSizeSlider.value; } UpdateUMAShape(); }
        public void OnHeadWidthChange() { if (umaDna != null) { umaDna.headWidth = HeadWidthSlider.value; } UpdateUMAShape(); }
        public void OnNeckThicknessChange() { if (umaDna != null) { umaDna.neckThickness = NeckThickSlider.value; } UpdateUMAShape(); }
        public void OnEarSizeChange() { if (umaDna != null) { umaDna.earsSize = EarSizeSlider.value; } UpdateUMAShape(); }
        public void OnEarPositionChange() { if (umaDna != null) { umaDna.earsPosition = EarPositionSlider.value; } UpdateUMAShape(); }
        public void OnEarRotationChange() { if (umaDna != null) { umaDna.earsRotation = EarRotationSlider.value; } UpdateUMAShape(); }
        public void OnNoseSizeChange() { if (umaDna != null) { umaDna.noseSize = NoseSizeSlider.value; } UpdateUMAShape(); }
        public void OnNoseCurveChange() { if (umaDna != null) { umaDna.noseCurve = NoseCurveSlider.value; } UpdateUMAShape(); }
        public void OnNoseWidthChange() { if (umaDna != null) { umaDna.noseWidth = NoseWidthSlider.value; } UpdateUMAShape(); }
        public void OnNoseInclinationChange() { if (umaDna != null) { umaDna.noseInclination = NoseInclinationSlider.value; } UpdateUMAShape(); }
        public void OnNosePositionChange() { if (umaDna != null) { umaDna.nosePosition = NosePositionSlider.value; } UpdateUMAShape(); }
        public void OnNosePronouncedChange() { if (umaDna != null) { umaDna.nosePronounced = NosePronuncedSlider.value; } UpdateUMAShape(); }
        public void OnNoseFlattenChange() { if (umaDna != null) { umaDna.noseFlatten = NoseFlattenSlider.value; } UpdateUMAShape(); }
        public void OnChinSizeChange() { if (umaDna != null) { umaDna.chinSize = ChinSizeSlider.value; } UpdateUMAShape(); }
        public void OnChinPronouncedChange() { if (umaDna != null) { umaDna.chinPronounced = ChinPronouncedSlider.value; } UpdateUMAShape(); }
        public void OnChinPositionChange() { if (umaDna != null) { umaDna.chinPosition = ChinPositionSlider.value; } UpdateUMAShape(); }
        public void OnMandibleSizeChange() { if (umaDna != null) { umaDna.mandibleSize = MandibleSizeSlider.value; } UpdateUMAShape(); }
        public void OnJawSizeChange() { if (umaDna != null) { umaDna.jawsSize = JawSizeSlider.value; } UpdateUMAShape(); }
        public void OnJawPositionChange() { if (umaDna != null) { umaDna.jawsPosition = JawPositionSlider.value; } UpdateUMAShape(); }
        public void OnCheekSizeChange() { if (umaDna != null) { umaDna.cheekSize = CheekSizeSlider.value; } UpdateUMAShape(); }
        public void OnCheekPositionChange() { if (umaDna != null) { umaDna.cheekPosition = CheekPositionSlider.value; } UpdateUMAShape(); }
        public void OnCheekLowPronouncedChange() { if (umaDna != null) { umaDna.lowCheekPronounced = lowCheekPronSlider.value; } UpdateUMAShape(); }
        public void OnForeheadSizeChange() { if (umaDna != null) { umaDna.foreheadSize = ForeHeadSizeSlider.value; } UpdateUMAShape(); }
        public void OnForeheadPositionChange() { if (umaDna != null) { umaDna.foreheadPosition = ForeHeadPositionSlider.value; } UpdateUMAShape(); }
        public void OnLipSizeChange() { if (umaDna != null) { umaDna.lipsSize = LipSizeSlider.value; } UpdateUMAShape(); }
        public void OnMouthSizeChange() { if (umaDna != null) { umaDna.mouthSize = MouthSlider.value; } UpdateUMAShape(); }
        public void OnEyeSizechange() { if (umaDna != null) { umaDna.eyeSize = EyeSizeSlider.value; } UpdateUMAShape(); }
        public void OnEyeRotationChange() { if (umaDna != null) { umaDna.eyeRotation = EyeRotationSlider.value; } UpdateUMAShape(); }
        public void OnLowCheekPositionChange() { if (umaDna != null) { umaDna.lowCheekPosition = LowCheekPosSlider.value; } UpdateUMAShape(); }
        public void OnEyeSpacingChange() { if (umaTutorialDna != null) { umaTutorialDna.eyeSpacing = EyeSpacingSlider.value; } UpdateUMAShape(); }

		public void PerformDNAChange(string dnaName, float dnaValue)
		{
			if(umaData != null)
			{
				foreach(UMADnaBase dna in umaData.umaRecipe.GetAllDna())
				{
					if(System.Array.IndexOf(dna.Names, dnaName) > -1)
					{
						int index = System.Array.IndexOf(dna.Names, dnaName);
						dna.SetValue(index, dnaValue);
					}
				}
			}
		}
    }
}
