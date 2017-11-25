// ==========================================================
// Updated to use UGUI, March 2015
// Dennis Trevillyan - WatreGames
//
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UMA;

namespace UMA.Examples
{
    public class UMACustomization : MonoBehaviour
    {
        public UMAData umaData;
        public UMADynamicAvatar umaDynamicAvatar;
        public CameraTrack cameraTrack;
        public MouseOrbitImproved orbitor;

		private UMADnaBase umaHumanoidDna;
		private UMADnaBase umaTutorialDna;

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

		private Dictionary<Slider, UMADnaBase.UMADnaAccessor> accessors = new Dictionary<Slider, UMADnaBase.UMADnaAccessor>();

        // Get the sliders and store for later use
        void Awake()
        {
			List<Slider> foundSliders = new List<Slider>();

			HeightSlider = GameObject.Find("HeightSlider").GetComponent<Slider>(); foundSliders.Add(HeightSlider);
			UpperMuscleSlider = GameObject.Find("UpperMuscleSlider").GetComponent<Slider>(); foundSliders.Add(UpperMuscleSlider);
			UpperWeightSlider = GameObject.Find("UpperWeightSlider").GetComponent<Slider>(); foundSliders.Add(UpperWeightSlider);
			LowerMuscleSlider = GameObject.Find("LowerMuscleSlider").GetComponent<Slider>(); foundSliders.Add(LowerMuscleSlider);
			LowerWeightSlider = GameObject.Find("LowerWeightSlider").GetComponent<Slider>(); foundSliders.Add(LowerWeightSlider);
			ArmLengthSlider = GameObject.Find("ArmLengthSlider").GetComponent<Slider>(); foundSliders.Add(ArmLengthSlider);
			ForearmLengthSlider = GameObject.Find("ForearmLengthSlider").GetComponent<Slider>(); foundSliders.Add(ForearmLengthSlider);
			LegSeparationSlider = GameObject.Find("LegSepSlider").GetComponent<Slider>(); foundSliders.Add(LegSeparationSlider);
			HandSizeSlider = GameObject.Find("HandSizeSlider").GetComponent<Slider>(); foundSliders.Add(HandSizeSlider);
			FeetSizeSlider = GameObject.Find("FeetSizeSlider").GetComponent<Slider>(); foundSliders.Add(FeetSizeSlider);
			LegSizeSlider = GameObject.Find("LegSizeSlider").GetComponent<Slider>(); foundSliders.Add(LegSizeSlider);
			ArmWidthSlider = GameObject.Find("ArmWidthSlider").GetComponent<Slider>(); foundSliders.Add(ArmWidthSlider);
			ForearmWidthSlider = GameObject.Find("ForearmWidthSlider").GetComponent<Slider>(); foundSliders.Add(ForearmWidthSlider);
			BreastSlider = GameObject.Find("BreastSizeSlider").GetComponent<Slider>(); foundSliders.Add(BreastSlider);
			BellySlider = GameObject.Find("BellySlider").GetComponent<Slider>(); foundSliders.Add(BellySlider);
			WaistSizeSlider = GameObject.Find("WaistSizeSlider").GetComponent<Slider>(); foundSliders.Add(WaistSizeSlider);
			GlueteusSizeSlider = GameObject.Find("GluteusSlider").GetComponent<Slider>(); foundSliders.Add(GlueteusSizeSlider);
			HeadSizeSlider = GameObject.Find("HeadSizeSlider").GetComponent<Slider>(); foundSliders.Add(HeadSizeSlider);
			HeadWidthSlider = GameObject.Find("HeadWidthSlider").GetComponent<Slider>(); foundSliders.Add(HeadWidthSlider);
			NeckThickSlider = GameObject.Find("NeckSlider").GetComponent<Slider>(); foundSliders.Add(NeckThickSlider);
			EarSizeSlider = GameObject.Find("EarSizeSlider").GetComponent<Slider>(); foundSliders.Add(EarSizeSlider);
			EarPositionSlider = GameObject.Find("EarPosSlider").GetComponent<Slider>(); foundSliders.Add(EarPositionSlider);
			EarRotationSlider = GameObject.Find("EarRotSlider").GetComponent<Slider>(); foundSliders.Add(EarRotationSlider);
			NoseSizeSlider = GameObject.Find("NoseSizeSlider").GetComponent<Slider>(); foundSliders.Add(NoseSizeSlider);
			NoseCurveSlider = GameObject.Find("NoseCurveSlider").GetComponent<Slider>(); foundSliders.Add(NoseCurveSlider);
			NoseWidthSlider = GameObject.Find("NoseWidthSlider").GetComponent<Slider>(); foundSliders.Add(NoseWidthSlider);
			NoseInclinationSlider = GameObject.Find("NoseInclineSlider").GetComponent<Slider>(); foundSliders.Add(NoseInclinationSlider);
			NosePositionSlider = GameObject.Find("NosePosSlider").GetComponent<Slider>(); foundSliders.Add(NosePositionSlider);
			NosePronuncedSlider = GameObject.Find("NosePronSlider").GetComponent<Slider>(); foundSliders.Add(NosePronuncedSlider);
			NoseFlattenSlider = GameObject.Find("NoseFlatSlider").GetComponent<Slider>(); foundSliders.Add(NoseFlattenSlider);
			ChinSizeSlider = GameObject.Find("ChinSizeSlider").GetComponent<Slider>(); foundSliders.Add(ChinSizeSlider);
			ChinPronouncedSlider = GameObject.Find("ChinPronSlider").GetComponent<Slider>(); foundSliders.Add(ChinPronouncedSlider);
			ChinPositionSlider = GameObject.Find("ChinPosSlider").GetComponent<Slider>(); foundSliders.Add(ChinPositionSlider);
			MandibleSizeSlider = GameObject.Find("MandibleSizeSlider").GetComponent<Slider>(); foundSliders.Add(MandibleSizeSlider);
			JawSizeSlider = GameObject.Find("JawSizeSlider").GetComponent<Slider>(); foundSliders.Add(JawSizeSlider);
			JawPositionSlider = GameObject.Find("JawPosSlider").GetComponent<Slider>(); foundSliders.Add(JawPositionSlider);
			CheekSizeSlider = GameObject.Find("CheekSizeSlider").GetComponent<Slider>(); foundSliders.Add(CheekSizeSlider);
			CheekPositionSlider = GameObject.Find("CheekPosSlider").GetComponent<Slider>(); foundSliders.Add(CheekPositionSlider);
			lowCheekPronSlider = GameObject.Find("LowCheekPronSlider").GetComponent<Slider>(); foundSliders.Add(lowCheekPronSlider);
			ForeHeadSizeSlider = GameObject.Find("ForeheadSizeSlider").GetComponent<Slider>(); foundSliders.Add(ForeHeadSizeSlider);
			ForeHeadPositionSlider = GameObject.Find("ForeheadPosSlider").GetComponent<Slider>(); foundSliders.Add(ForeHeadPositionSlider);
			LipSizeSlider = GameObject.Find("LipSizeSlider").GetComponent<Slider>(); foundSliders.Add(LipSizeSlider);
			MouthSlider = GameObject.Find("MouthSizeSlider").GetComponent<Slider>(); foundSliders.Add(MouthSlider);
			EyeSizeSlider = GameObject.Find("EyeSizeSlider").GetComponent<Slider>(); foundSliders.Add(EyeSizeSlider);
			EyeRotationSlider = GameObject.Find("EyeRotSlider").GetComponent<Slider>(); foundSliders.Add(EyeRotationSlider);
			EyeSpacingSlider = GameObject.Find("EyeSpaceSlider").GetComponent<Slider>(); foundSliders.Add(EyeSpacingSlider);
			LowCheekPosSlider = GameObject.Find("LowCheekPosSlider").GetComponent<Slider>(); foundSliders.Add(LowCheekPosSlider);

			sliders = foundSliders.ToArray();

            // Find the panels and hide for now
            DnaPanel = GameObject.Find("DnaEditorPanel");
            //        DnaScrollPanel = GameObject.Find("ScrollPanel");
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
            baseTarget = GameObject.Find("UMACrowd").transform;             // Get the transform of the UMA Crown GO to use when retargeting the camera
        }

        void Update()
        {
            if (umaTutorialDna != null)
                EyeSpacingSlider.interactable = true;
            else
                EyeSpacingSlider.interactable = false;

            // Don't raycast if the editor is open
            if (umaData != null)
                return;

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

			umaHumanoidDna = umaData.GetDna(UMAUtils.StringToHash("UMADnaHumanoid"));
			umaTutorialDna = umaData.GetDna(UMAUtils.StringToHash("UMADnaTutorial"));

			accessors.Clear();
			if (umaHumanoidDna != null)
			{
				accessors.Add(HeightSlider, umaHumanoidDna.GetAccessor("height"));
				accessors.Add(UpperMuscleSlider, umaHumanoidDna.GetAccessor("upperMuscle"));
				accessors.Add(UpperWeightSlider, umaHumanoidDna.GetAccessor("upperWeight"));
				accessors.Add(LowerMuscleSlider, umaHumanoidDna.GetAccessor("lowerMuscle"));
				accessors.Add(LowerWeightSlider, umaHumanoidDna.GetAccessor("lowerWeight"));
				accessors.Add(ArmLengthSlider, umaHumanoidDna.GetAccessor("armLength"));
				accessors.Add(ForearmLengthSlider, umaHumanoidDna.GetAccessor("forearmLength"));
				accessors.Add(LegSeparationSlider, umaHumanoidDna.GetAccessor("legSeparation"));
				accessors.Add(HandSizeSlider, umaHumanoidDna.GetAccessor("handsSize"));
				accessors.Add(FeetSizeSlider, umaHumanoidDna.GetAccessor("feetSize"));
				accessors.Add(LegSizeSlider, umaHumanoidDna.GetAccessor("legsSize"));
				accessors.Add(ArmWidthSlider, umaHumanoidDna.GetAccessor("armWidth"));
				accessors.Add(ForearmWidthSlider, umaHumanoidDna.GetAccessor("forearmWidth"));
				accessors.Add(BreastSlider, umaHumanoidDna.GetAccessor("breastSize"));
				accessors.Add(BellySlider, umaHumanoidDna.GetAccessor("belly"));
				accessors.Add(WaistSizeSlider, umaHumanoidDna.GetAccessor("waist"));
				accessors.Add(GlueteusSizeSlider, umaHumanoidDna.GetAccessor("gluteusSize"));
				accessors.Add(HeadSizeSlider, umaHumanoidDna.GetAccessor("headSize"));
				accessors.Add(HeadWidthSlider, umaHumanoidDna.GetAccessor("headWidth"));
				accessors.Add(NeckThickSlider, umaHumanoidDna.GetAccessor("neckThickness"));
				accessors.Add(EarSizeSlider, umaHumanoidDna.GetAccessor("earsSize"));
				accessors.Add(EarPositionSlider, umaHumanoidDna.GetAccessor("earsPosition"));
				accessors.Add(EarRotationSlider, umaHumanoidDna.GetAccessor("earsRotation"));
				accessors.Add(NoseSizeSlider, umaHumanoidDna.GetAccessor("noseSize"));
				accessors.Add(NoseCurveSlider, umaHumanoidDna.GetAccessor("noseCurve"));
				accessors.Add(NoseWidthSlider, umaHumanoidDna.GetAccessor("noseWidth"));
				accessors.Add(NoseInclinationSlider, umaHumanoidDna.GetAccessor("noseInclination"));
				accessors.Add(NosePositionSlider, umaHumanoidDna.GetAccessor("nosePosition"));
				accessors.Add(NosePronuncedSlider, umaHumanoidDna.GetAccessor("nosePronounced"));
				accessors.Add(NoseFlattenSlider, umaHumanoidDna.GetAccessor("noseFlatten"));
				accessors.Add(ChinSizeSlider, umaHumanoidDna.GetAccessor("chinSize"));
				accessors.Add(ChinPronouncedSlider, umaHumanoidDna.GetAccessor("chinPronounced"));
				accessors.Add(ChinPositionSlider, umaHumanoidDna.GetAccessor("chinPosition"));
				accessors.Add(MandibleSizeSlider, umaHumanoidDna.GetAccessor("mandibleSize"));
				accessors.Add(JawSizeSlider, umaHumanoidDna.GetAccessor("jawsSize"));
				accessors.Add(JawPositionSlider, umaHumanoidDna.GetAccessor("jawsPosition"));
				accessors.Add(CheekSizeSlider, umaHumanoidDna.GetAccessor("cheekSize"));
				accessors.Add(CheekPositionSlider, umaHumanoidDna.GetAccessor("cheekPosition"));
				accessors.Add(lowCheekPronSlider, umaHumanoidDna.GetAccessor("lowCheekPronounced"));
				accessors.Add(ForeHeadSizeSlider, umaHumanoidDna.GetAccessor("foreheadSize"));
				accessors.Add(ForeHeadPositionSlider, umaHumanoidDna.GetAccessor("foreheadPosition"));
				accessors.Add(LipSizeSlider, umaHumanoidDna.GetAccessor("lipsSize"));
				accessors.Add(MouthSlider, umaHumanoidDna.GetAccessor("mouthSize"));
				accessors.Add(EyeSizeSlider, umaHumanoidDna.GetAccessor("eyeSize"));
				accessors.Add(EyeRotationSlider, umaHumanoidDna.GetAccessor("eyeRotation"));
				accessors.Add(LowCheekPosSlider, umaHumanoidDna.GetAccessor("lowCheekPosition"));
			}

			if (umaTutorialDna != null)
			{
				accessors.Add(EyeSpacingSlider, umaTutorialDna.GetAccessor("eyeSpacing"));
			}

            SetSliders();
            SetCamera(true);
        }

        // Set the camera target and viewport
        private void SetCamera(bool show)
        {
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
                    cameraTrack.target = baseTarget;
                if (orbitor != null)
                    orbitor.target = baseTarget;
                DnaPanel.SetActive(false);
                DnaHide.gameObject.SetActive(false);
                umaData = null;
                umaHumanoidDna = null;
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
			UMADnaBase.UMADnaAccessor accessor;
			foreach (Slider slider in sliders)
			{
				if (slider == null) continue;
				if (accessors.TryGetValue(slider, out accessor))
				{
					if (accessor != null)
					{
						slider.value = accessor.Get();
					}
				}
			}
        }

        // Slider callback
		public void OnSliderChange()
		{
			GameObject selectedGO = EventSystem.current.currentSelectedGameObject;
			if (selectedGO == null) return;

			Slider slider = selectedGO.GetComponent<Slider>();
			if (slider == null) return;

			UMADnaBase.UMADnaAccessor accessor;
			if (accessors.TryGetValue(slider, out accessor))
			{
				if (accessor != null)
				{
					accessor.Set(slider.value);
					UpdateUMAShape();
				}
			}
		}
    }
}
