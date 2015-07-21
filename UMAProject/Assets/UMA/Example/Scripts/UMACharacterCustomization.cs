using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UMA;

public class UMACharacterCustomization : MonoBehaviour 
{	
	public UMAData umaData;
	private UMADnaHumanoid umaDna;
	private UMACrowd crowdHandle;

	private Slider[] sliderControlList;
	private Slider[] sliderControlList2;
	private Slider[] sliderControlList3;

	private byte bodyArea = 0;
	private Transform myCamera;

	private Transform bodyGO;
	private Transform topHeadGO;
	private Transform lowHeadGO;
	/*private Color[] skinColors;
	private Color[] eyeColors;
	private Color[] hairColors;
	private string[] femaleHairStyles;
	private string[] maleHairStyles;
	private string[,] facialHairStyles;*/

	//private byte hairColor = 0;
	private Transform panel;

	private Vector3 lastPos = new Vector3(0, 1.2f, -2.4f);
	private Quaternion lastRot;
	
	private void Start () 
	{
		panel = GameObject.Find("Canvas/Panel").transform;
		bodyGO = panel.FindChild("Body");
		topHeadGO = panel.FindChild("UpperHead");
		lowHeadGO = panel.FindChild("LowerHead");

		/*skinColors = new Color[]{
			Color.white,
			new Color32(233, 233, 233, 255),
			new Color32(211, 211, 211, 255),
			new Color32(189, 189, 189, 255),
			new Color32(167, 167, 167, 255),
			new Color32(145, 145, 145, 255),
			new Color32(123, 123, 123, 255),
			new Color32(101, 101, 101, 255),
			new Color32(079, 079, 079, 255),
			new Color32(057, 057, 057, 255),
		};
		
		eyeColors = new Color[]{
			new Color32(165, 188, 255, 255),
			new Color32(111, 147, 255, 255),
			new Color32(122, 088, 045, 255),
			new Color32(054, 135, 011, 255),
			new Color32(127, 152, 79, 255),
		};
		
		hairColors = new Color[]{
			Color.white,
			new Color32(255, 246, 172, 255),
			new Color32(255, 211, 103, 255),
			new Color32(255, 204, 094, 255),
			new Color32(191, 061, 039, 255),
			new Color32(109, 031, 018, 255),
			new Color32(159, 109, 000, 255),
			new Color32(086, 048, 000, 255),
			new Color32(039, 022, 000, 255),
			Color.black,
		};
		
		femaleHairStyles = new string[]{
			"FemaleShortHair01",
			"FemaleLongHair01",
		};
		
		maleHairStyles = new string[]{
			"MaleHair01",
			"MaleHair02",
		};
		
		facialHairStyles = new string[,]{
			{"",""},
			{"MaleBeard01",""},
			{"MaleBeard02",""},
			{"MaleBeard03",""},
			{"MaleBeard01","MaleBeard02"},
			{"MaleBeard02","MaleBeard03"},
			{"MaleBeard03","MaleBeard01"},
		};*/
		
		myCamera = GameObject.Find("NormalCamera").transform;
		crowdHandle = GameObject.Find("UMACrowd").GetComponent<UMACrowd>();
		
		sliderControlList = new Slider[19];
		sliderControlList2 = new Slider[18];
		sliderControlList3 = new Slider[14];

		InitSliders();

    	int randomResult = Random.Range(0, 2);
		GameObject myUma = crowdHandle.GenerateOneUMA(randomResult);
		myUma.transform.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));
		GetUMAData(myUma);
	}

	private void GetUMAData(GameObject myUma)
	{
		UMAData tempUMA = myUma.GetComponent<UMAData>();
		if(tempUMA)
		{
			umaData = tempUMA;
			umaDna = umaData.umaRecipe.GetDna<UMADnaHumanoid>();
			ReceiveValues();
    	}
	}

	private void Update()
	{
		if(Input.GetKey(KeyCode.E))
		{
			myCamera.transform.RotateAround(Vector3.zero, Vector3.up, -70 * Time.deltaTime);
		}
		if(Input.GetKey(KeyCode.Q))
		{
			myCamera.transform.RotateAround(Vector3.zero, Vector3.up, 70 * Time.deltaTime);
		}
	}

	public void ButtonMale()
	{
		Destroy(GameObject.Find("UMACrowd").transform.GetChild(0).gameObject);
		crowdHandle.ResetSpawnPos();
		GameObject myUma = crowdHandle.GenerateOneUMA(0);
		myUma.transform.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));

		GetUMAData(myUma);

		if(bodyArea != 0)
		{
			float yPos = GameObject.Find("NoseMiddle").transform.position.y;
			myCamera.transform.position = new Vector3(0, yPos + 0.05f, -0.62f);

			if(bodyArea == 2)
				panel.FindChild("LowerHead/FacialHair").gameObject.SetActive(true);
		}
	}

	public void ButtonFemale()
	{
		Destroy(GameObject.Find("UMACrowd").transform.GetChild(0).gameObject);
		crowdHandle.ResetSpawnPos();
		GameObject myUma = crowdHandle.GenerateOneUMA(1);
		myUma.transform.localRotation = Quaternion.Euler(new Vector3(0, 180, 0));

		GetUMAData(myUma);

		if(bodyArea != 0)
		{
			float yPos = GameObject.Find("NoseMiddle").transform.position.y;
			myCamera.transform.position = new Vector3(0, yPos + 0.05f, -0.62f);

			if(bodyArea == 2)
				panel.FindChild("LowerHead/FacialHair").gameObject.SetActive(false);
		}
	}
	
	public void ButtonBody()
	{
		if(bodyArea != 0)
		{
			bodyArea = 0;
			bodyGO.gameObject.SetActive(true);
			topHeadGO.gameObject.SetActive(false);
			lowHeadGO.gameObject.SetActive(false);
			ReceiveValues();
			myCamera.position = lastPos;
			myCamera.rotation = lastRot;
		}
	}

	public void ButtonHead()
	{
		float yPos = GameObject.Find("NoseMiddle").transform.position.y;
		lastPos = myCamera.position;
		lastRot = myCamera.rotation;
		myCamera.position = new Vector3(0, yPos + 0.05f, -0.62f);
		myCamera.rotation = Quaternion.Euler(new Vector3(0,0,0));

		if(bodyArea == 0)
		{
			bodyArea = 1;
			bodyGO.gameObject.SetActive(false);
			topHeadGO.gameObject.SetActive(true);
			ReceiveValues();
		}
	}
	
	public void ButtonHeadUpper()
	{
		if(bodyArea != 1)
		{
			bodyArea = 1;
			topHeadGO.gameObject.SetActive(true);
			lowHeadGO.gameObject.SetActive(false);
			ReceiveValues();
		}
	}

	public void ButtonHeadLower()
	{
		if(bodyArea != 2)
		{
			bodyArea = 2;
			topHeadGO.gameObject.SetActive(false);
			lowHeadGO.gameObject.SetActive(true);
			ReceiveValues();
			if(umaData.umaRecipe.raceData.raceName == "umaDnaMale")
				panel.FindChild("LowerHead/FacialHair").gameObject.SetActive(true);
			else
				panel.FindChild("LowerHead/FacialHair").gameObject.SetActive(false);
		}
	}

	private void InitSliders()
	{
		sliderControlList[0] = bodyGO.FindChild("Height").GetComponent<Slider>();
		sliderControlList[1] = bodyGO.FindChild("HeadSize").GetComponent<Slider>();
		
		sliderControlList[2] = bodyGO.FindChild("UpperMuscle").GetComponent<Slider>();
		sliderControlList[3] = bodyGO.FindChild("LowerMuscle").GetComponent<Slider>();
		sliderControlList[4] = bodyGO.FindChild("UpperWeight").GetComponent<Slider>();
		sliderControlList[5] = bodyGO.FindChild("LowerWeight").GetComponent<Slider>();	
		
		sliderControlList[6] = bodyGO.FindChild("ArmLength").GetComponent<Slider>();
		sliderControlList[7] = bodyGO.FindChild("ArmWidth").GetComponent<Slider>();
		
		sliderControlList[8] = bodyGO.FindChild("ForearmLength").GetComponent<Slider>();
		sliderControlList[9] = bodyGO.FindChild("ForearmWidth").GetComponent<Slider>();
		sliderControlList[10] = bodyGO.FindChild("HandSize").GetComponent<Slider>();
		
		sliderControlList[11] = bodyGO.FindChild("FeetSize").GetComponent<Slider>();
		sliderControlList[12] = bodyGO.FindChild("LegSeparation").GetComponent<Slider>();
		sliderControlList[13] = bodyGO.FindChild("LegSize").GetComponent<Slider>();
		
		sliderControlList[14] = bodyGO.FindChild("BumSize").GetComponent<Slider>();
		sliderControlList[15] = bodyGO.FindChild("BreastSize").GetComponent<Slider>();
		sliderControlList[16] = bodyGO.FindChild("Belly").GetComponent<Slider>();
		sliderControlList[17] = bodyGO.FindChild("Waist").GetComponent<Slider>();
		
		//sliderControlList[18] = bodyGO.FindChild("SkinColor").GetComponent<Slider>();
		//sliderControlList[18].maxValue = skinColors.Length - 1;


		sliderControlList2[0] = topHeadGO.FindChild("HeadWidth").GetComponent<Slider>();
		sliderControlList2[1] = topHeadGO.FindChild("ForeheadSize").GetComponent<Slider>();
		sliderControlList2[2] = topHeadGO.FindChild("ForeheadPosition").GetComponent<Slider>();
		
		sliderControlList2[3] = topHeadGO.FindChild("EarSize").GetComponent<Slider>();
		sliderControlList2[4] = topHeadGO.FindChild("EarPosition").GetComponent<Slider>();
		sliderControlList2[5] = topHeadGO.FindChild("EarRotation").GetComponent<Slider>();
		
		sliderControlList2[6] = topHeadGO.FindChild("NoseSize").GetComponent<Slider>();
		sliderControlList2[7] = topHeadGO.FindChild("NoseCurve").GetComponent<Slider>();
		sliderControlList2[8] = topHeadGO.FindChild("NoseWidth").GetComponent<Slider>();
		
		sliderControlList2[9] = topHeadGO.FindChild("NoseInclination").GetComponent<Slider>();
		sliderControlList2[10] = topHeadGO.FindChild("NosePosition").GetComponent<Slider>();
		sliderControlList2[11] = topHeadGO.FindChild("NoseFlatten").GetComponent<Slider>();
		sliderControlList2[12] = topHeadGO.FindChild("NosePronounce").GetComponent<Slider>();
		
		sliderControlList2[13] = topHeadGO.FindChild("EyeSize").GetComponent<Slider>();
		sliderControlList2[14] = topHeadGO.FindChild("EyeRotation").GetComponent<Slider>();
		
		//sliderControlList2[15] = topHeadGO.FindChild("HairColor").GetComponent<Slider>();
		//sliderControlList2[15].maxValue = hairColors.Length - 1;

		//sliderControlList2[16] = topHeadGO.FindChild("EyeColor").GetComponent<Slider>();
		//sliderControlList2[16].maxValue = eyeColors.Length - 1;
		
		//sliderControlList2[17] = topHeadGO.FindChild("HairType").GetComponent<Slider>();
		//sliderControlList2[17].maxValue = femaleHairStyles.Length - 1;

		topHeadGO.gameObject.SetActive(false);

		sliderControlList3[0] = lowHeadGO.FindChild("CheekSize").GetComponent<Slider>();
		sliderControlList3[1] = lowHeadGO.FindChild("CheekPosition").GetComponent<Slider>();

		sliderControlList3[2] = lowHeadGO.FindChild("LowCheekPosition").GetComponent<Slider>();
		sliderControlList3[3] = lowHeadGO.FindChild("LowCheekPronounce").GetComponent<Slider>();

		sliderControlList3[4] = lowHeadGO.FindChild("LipSize").GetComponent<Slider>();
		sliderControlList3[5] = lowHeadGO.FindChild("MouthSize").GetComponent<Slider>();

		sliderControlList3[6] = lowHeadGO.FindChild("JawLength").GetComponent<Slider>();
		sliderControlList3[7] = lowHeadGO.FindChild("JawWidth").GetComponent<Slider>();
		sliderControlList3[8] = lowHeadGO.FindChild("JawPosition").GetComponent<Slider>();

		sliderControlList3[9] = lowHeadGO.FindChild("Neck").GetComponent<Slider>();
		
		sliderControlList3[10] = lowHeadGO.FindChild("ChinSize").GetComponent<Slider>();
		sliderControlList3[11] = lowHeadGO.FindChild("ChinPronounce").GetComponent<Slider>();
		sliderControlList3[12] = lowHeadGO.FindChild("ChinPosition").GetComponent<Slider>();

		//sliderControlList3[13] = lowHeadGO.FindChild("FacialHair").GetComponent<Slider>();
		//sliderControlList3[13].maxValue = facialHairStyles.Length - 1;

		lowHeadGO.gameObject.SetActive(false);
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
	
	private void ReceiveValues()
	{
		if(umaDna != null)
		{
			if(bodyArea == 0)
			{
				sliderControlList[0].value = umaDna.height;
				sliderControlList[1].value = umaDna.headSize;
				
				sliderControlList[2].value = umaDna.upperMuscle;
				sliderControlList[3].value = umaDna.lowerMuscle;
				sliderControlList[4].value = umaDna.upperWeight;
				sliderControlList[5].value = umaDna.lowerWeight;
				
				sliderControlList[6].value = umaDna.armLength;
				sliderControlList[7].value = umaDna.armWidth;
				sliderControlList[8].value = umaDna.forearmLength;
				sliderControlList[9].value = umaDna.forearmWidth;
				sliderControlList[10].value = umaDna.handsSize;
				
				sliderControlList[11].value = umaDna.feetSize;
				sliderControlList[12].value = umaDna.legSeparation;
				sliderControlList[13].value = umaDna.legsSize;
				sliderControlList[14].value = umaDna.gluteusSize;	
				
				sliderControlList[15].value = umaDna.breastSize;	
				sliderControlList[16].value = umaDna.belly;
				sliderControlList[17].value = umaDna.waist;

				//sliderControlList[18].value = (float)umaDna.Body[(int)Positions.BuildSlots.Body_Torso].colors[0] - 1;
				
			}
			else if(bodyArea == 1)
			{
				sliderControlList2[0].value = umaDna.headWidth;
				sliderControlList2[1].value = umaDna.foreheadSize;
				sliderControlList2[2].value = umaDna.foreheadPosition;
				
				sliderControlList2[3].value = umaDna.earsSize;
				sliderControlList2[4].value = umaDna.earsPosition;
				sliderControlList2[5].value = umaDna.earsRotation;
				
				sliderControlList2[6].value = umaDna.noseSize;
				sliderControlList2[7].value = umaDna.noseCurve;
				sliderControlList2[8].value = umaDna.noseWidth;
				
				sliderControlList2[9].value = umaDna.noseInclination;
				sliderControlList2[10].value = umaDna.nosePosition;
				sliderControlList2[11].value = umaDna.nosePronounced;
				sliderControlList2[12].value = umaDna.noseFlatten;
				
				sliderControlList2[13].value = umaDna.eyeSize;
				sliderControlList2[14].value = umaDna.eyeRotation;

				/*if(umaDna.gender == 'F')
				{
					for(int i = 0; i < femaleHairStyles.Length; i++)
					{
						if(umaDna.Body[(int)Positions.BuildSlots.Body_Hair].element.Name == femaleHairStyles[i])
						{
							sliderControlList2[17].value = i;
							break;
						}
					}
				}
				else
				{
					for(int i = 0; i < maleHairStyles.Length; i++)
					{
						if(umaDna.Body[(int)Positions.BuildSlots.Body_Hair].element.Name == maleHairStyles[i])
						{
							sliderControlList2[17].value = i;
							break;
						}
					}
				}*/

				//sliderControlList2[15].value = (float)umaDna.Body[(int)Positions.BuildSlots.Body_Hair].colors[0] - 12;
				//sliderControlList2[16].value = (float)umaDna.Body[(int)Positions.BuildSlots.Body_HeadEyes].colors[0] - 22;
			}
			else
			{
				sliderControlList3[0].value = umaDna.cheekSize;
				sliderControlList3[1].value = umaDna.cheekPosition;
				sliderControlList3[2].value = umaDna.lowCheekPronounced;
				sliderControlList3[3].value = umaDna.lowCheekPosition;

				sliderControlList3[4].value = umaDna.lipsSize;
				sliderControlList3[5].value = umaDna.mouthSize;
				sliderControlList3[6].value = umaDna.mandibleSize;
				
				sliderControlList3[7].value = umaDna.jawsSize;
				sliderControlList3[8].value = umaDna.jawsPosition;
				sliderControlList3[9].value = umaDna.neckThickness;
				
				sliderControlList3[10].value = umaDna.chinSize;
				sliderControlList3[11].value = umaDna.chinPronounced;
				sliderControlList3[12].value = umaDna.chinPosition;

				/*if(umaDna.gender == 'M')
				{
					for(int i = 0; i < facialHairStyles.Length; i++)
					{
						if(umaDna.Body[(int)Positions.BuildSlots.Body_Beard].element.Name == facialHairStyles[i])
						{
							sliderControlList3[17].value = i;
							break;
						}
					}
				}*/
			}
		}
	}

	// Slider callbacks 
	public void OnHeightChange()
	{
		umaDna.height = sliderControlList[0].value;
		UpdateUMAShape();
	}
	
	public void OnUpperMuscleChange()
	{
		umaDna.upperMuscle = sliderControlList[2].value;
		UpdateUMAShape();
	}
	
	public void OnUpperWeightChange()
	{
		umaDna.upperWeight = sliderControlList[4].value;
		UpdateUMAShape();
	}
	
	public void OnLowerMuscleChange()
	{
		umaDna.lowerMuscle = sliderControlList[3].value;
		UpdateUMAShape();
	}
	
	public void OnLowerWeightChange()
	{
		umaDna.lowerWeight = sliderControlList[5].value;
		UpdateUMAShape();
	}
	
	public void OnArmLengthChange()
	{
		umaDna.armLength = sliderControlList[6].value;
		UpdateUMAShape();
	}
	
	public void OnForearmLengthChange()
	{
		umaDna.forearmLength = sliderControlList[8].value;
		UpdateUMAShape();
	}
	
	public void OnLegSeparationChange()
	{
		umaDna.legSeparation = sliderControlList[12].value;
		UpdateUMAShape();
	}
	
	public void OnHandSizeChange()
	{
		umaDna.handsSize = sliderControlList[10].value;
		UpdateUMAShape();
	}
	
	public void OnFootSizeChange()
	{
		umaDna.feetSize = sliderControlList[11].value;
		UpdateUMAShape();
	}
	
	public void OnLegSizeChange()
	{
		umaDna.legsSize = sliderControlList[13].value;
		UpdateUMAShape();
	}
	
	public void OnArmWidthChange()
	{
		umaDna.armWidth = sliderControlList[7].value;
		UpdateUMAShape();
	}
	
	public void OnForearmWidthChange()
	{
		umaDna.forearmWidth = sliderControlList[9].value;
		UpdateUMAShape();
	}
	
	public void OnBreastSizeChange()
	{
		umaDna.breastSize = sliderControlList[15].value;
		UpdateUMAShape();
	}
	
	public void OnBellySizeChange()
	{
		umaDna.belly = sliderControlList[16].value;
		UpdateUMAShape();
	}
	
	public void OnWaistSizeChange()
	{
		umaDna.waist = sliderControlList[17].value;
		UpdateUMAShape();
	}
	
	public void OnGluteusSizeChange()
	{
		umaDna.gluteusSize = sliderControlList[14].value;
		UpdateUMAShape();
	}
	
	public void OnHeadSizeChange()
	{
		umaDna.headSize = sliderControlList[1].value;
		UpdateUMAShape();
	}
	
	public void OnHeadWidthChange()
	{
		umaDna.headWidth = sliderControlList2[0].value;
		UpdateUMAShape();
	}
	
	public void OnNeckThicknessChange()
	{
		umaDna.neckThickness = sliderControlList3[9].value;
		UpdateUMAShape();
	}
	
	public void OnEarSizeChange()
	{
		umaDna.earsSize = sliderControlList2[3].value;
		UpdateUMAShape();
	}
	
	public void OnEarPositionChange()
	{
		umaDna.earsPosition = sliderControlList2[4].value;
		UpdateUMAShape();
	}
	
	public void OnEarRotationChange()
	{
		umaDna.earsRotation = sliderControlList2[5].value;
		UpdateUMAShape();
	}
	
	public void OnNoseSizeChange()
	{
		umaDna.noseSize = sliderControlList2[6].value;
		UpdateUMAShape();
	}
	
	public void OnNoseCurveChange()
	{
		umaDna.noseCurve = sliderControlList2[7].value;
		UpdateUMAShape();
	}
	
	public void OnNoseWidthChange()
	{
		umaDna.noseWidth = sliderControlList2[8].value;
		UpdateUMAShape();
	}
	
	public void OnNoseInclinationChange()
	{
		umaDna.noseInclination = sliderControlList2[9].value;
		UpdateUMAShape();
	}
	
	public void OnNosePositionChange()
	{
		umaDna.nosePosition = sliderControlList2[10].value;
		UpdateUMAShape();
	}
	
	public void OnNosePronouncedChange()
	{
		umaDna.nosePronounced = sliderControlList2[11].value;
		UpdateUMAShape();
	}
	
	public void OnNoseFlattenChange()
	{
		umaDna.noseFlatten = sliderControlList2[12].value;
		UpdateUMAShape();
	}
	
	public void OnChinSizeChange()
	{
		umaDna.chinSize = sliderControlList3[10].value;
		UpdateUMAShape();
	}
	
	public void OnChinPronouncedChange()
	{
		umaDna.chinPronounced = sliderControlList3[11].value;
		UpdateUMAShape();
	}
	
	public void OnChinPositionChange()
	{
		umaDna.chinPosition = sliderControlList3[12].value;
		UpdateUMAShape();
	}
	
	public void OnMandibleSizeChange()
	{
		umaDna.mandibleSize = sliderControlList3[6].value;
		UpdateUMAShape();
	}
	
	public void OnJawSizeChange()
	{
		umaDna.jawsSize = sliderControlList3[7].value;
		UpdateUMAShape();
	}
	
	public void OnJawPositionChange()
	{
		umaDna.jawsPosition = sliderControlList3[8].value;
		UpdateUMAShape();
	}
	
	public void OnCheekSizeChange()
	{
		umaDna.cheekSize = sliderControlList3[0].value;
		UpdateUMAShape();
	}
	
	public void OnCheekPositionChange()
	{
		umaDna.cheekPosition = sliderControlList3[1].value;
		UpdateUMAShape();
	}
	
	public void OnCheekLowPronouncedChange()
	{
		umaDna.lowCheekPronounced = sliderControlList3[2].value;
		UpdateUMAShape();
	}
	
	public void OnForeheadSizeChange()
	{
		umaDna.foreheadSize = sliderControlList2[1].value;
		UpdateUMAShape();
	}
	
	public void OnForeheadPositionChange()
	{
		umaDna.foreheadPosition = sliderControlList2[2].value;
		UpdateUMAShape();
	}
	
	public void OnLipSizeChange()
	{
		umaDna.lipsSize = sliderControlList3[4].value;
		UpdateUMAShape();
	}
	
	public void OnMouthSizeChange()
	{
		umaDna.mouthSize = sliderControlList3[5].value;
		UpdateUMAShape();
	}
	
	public void OnEyeSizechange()
	{
		umaDna.eyeSize = sliderControlList2[13].value;
		UpdateUMAShape();
	}
	
	public void OnEyeRotationChange()
	{
		umaDna.eyeRotation = sliderControlList2[14].value;
		UpdateUMAShape();
	}
	
	public void OnLowCheekPositionChange()
	{
		umaDna.lowCheekPosition = sliderControlList3[3].value;
		UpdateUMAShape();
	}

	/*private void TransferValues()
	{
		if(umaDna != null)
		{	
			if(bodyArea == 0)
			{
				//byte skinColor = (byte)(sliderControlList[18].value);

				//AddParts(umaDna.gender, skinColors[skinColor]);
			}
			else if(bodyArea == 1)
			{
				//hairColor = (byte)(sliderControlList2[15].value);
				//byte hairStyle = (byte)(sliderControlList2[17].value);	

				//byte eyeColor = (byte)(sliderControlList2[16].value);
				if(umaDna.gender == 'F')
				{
					umaDnaoidStructure.BodyAdd(umaDna, "umaDna Female HeadEyes 01", eyeColors[eyeColor]);
					umaDnaoidStructure.BodyAdd(umaDna, femaleHairStyles[hairStyle], hairColors[hairColor]);
				}
				else
				{
					umaDnaoidStructure.BodyAdd(umaDna, "umaDna Male HeadEyes 01", eyeColors[eyeColor]);
					umaDnaoidStructure.BodyAdd(umaDna, maleHairStyles[hairStyle], hairColors[hairColor]);
				}
			}
			else
			{
				if(umaData.umaRecipe.raceData.raceName == "umaDnaMale")
				{
					byte beardStyle = (byte)(sliderControlList3[13].value);
					
					if(facialHairStyles[beardStyle] != "")
						umaDnaoidStructure.BodyAdd(umaDna, facialHairStyles[beardStyle], hairColors[hairColor]);
					else
						umaDnaoidStructure.BodyRemove(umaDna, (int)Positions.BuildSlots.Body_Beard);
				}
			}
		}
	}*/
}
