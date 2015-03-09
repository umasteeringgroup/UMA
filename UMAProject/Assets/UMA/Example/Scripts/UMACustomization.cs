using UnityEngine;
using System.Collections;
using UMA;
using UnityEngine.UI;

public class UMACustomization : MonoBehaviour {
	
	public Transform sliderPrefab;
	
	public UMAData umaData;
	public UMADynamicAvatar umaDynamicAvatar;
	public CameraTrack cameraTrack;
	private UMADnaHumanoid umaDna;
	private UMADnaTutorial umaTutorialDna;
	private Transform sliderParent;

	public Slider[] sliderControlList = new Slider[0];
	
	public SlotLibrary mySlotLibrary;
    public OverlayLibrary myOverlayLibrary;

	protected virtual void Start() {
		sliderParent = GameObject.Find("Canvas").transform;
		if (sliderParent == null)
		{
			Debug.LogError("Couldn't locate default Canvas");
		}

		sliderControlList = new Slider[47];	
		//Changed slider order
		
		sliderControlList[0] = InstantiateSlider("height",0,0);
		sliderControlList[1] = InstantiateSlider("headSize",1,0);
		sliderControlList[43] = InstantiateSlider("headWidth",2,0);
		sliderControlList[32] = InstantiateSlider("forehead size",3,0);
		sliderControlList[33] = InstantiateSlider("forehead position",4,0);
		
		sliderControlList[12] = InstantiateSlider("ears size",0,1);
		sliderControlList[13] = InstantiateSlider("ears position",1,1);
		sliderControlList[14] = InstantiateSlider("ears rotation",2,1);
		
		sliderControlList[28] = InstantiateSlider("cheek size",0,2);
		sliderControlList[29] = InstantiateSlider("cheek position",1,2);
		sliderControlList[30] = InstantiateSlider("lowCheek pronounced",2,2);
		sliderControlList[31] = InstantiateSlider("lowCheek position",3,2);
		
		sliderControlList[15] = InstantiateSlider("nose size",0,3);
		sliderControlList[16] = InstantiateSlider("nose curve",1,3);
		sliderControlList[17] = InstantiateSlider("nose width",2,3);
		
		sliderControlList[18] = InstantiateSlider("nose inclination",0,4);
		sliderControlList[19] = InstantiateSlider("nose position",1,4);
		sliderControlList[20] = InstantiateSlider("nose pronounced",2,4);
		sliderControlList[21] = InstantiateSlider("nose flatten",3,4);
		
		sliderControlList[44] = InstantiateSlider("eye Size",0,5);
		sliderControlList[45] = InstantiateSlider("eye Rotation",1,5);
		sliderControlList[34] = InstantiateSlider("lips size",2,5);
		sliderControlList[35] = InstantiateSlider("mouth size",3,5);
		sliderControlList[25] = InstantiateSlider("mandible size",4,5);
		
		sliderControlList[26] = InstantiateSlider("jaw Size",0,6);
		sliderControlList[27] = InstantiateSlider("jaw Position",1,6);
		sliderControlList[2] = InstantiateSlider("neck",2,6);
		
		sliderControlList[22] = InstantiateSlider("chinSize",0,7);
		sliderControlList[23] = InstantiateSlider("chinPronounced",1,7);
		sliderControlList[24] = InstantiateSlider("chinPosition",2,7);
		
		sliderControlList[7] = InstantiateSlider("upper muscle",0,8);
		sliderControlList[8] = InstantiateSlider("lower muscle",1,8);
		sliderControlList[9] = InstantiateSlider("upper weight",2,8);
		sliderControlList[10] = InstantiateSlider("lower weight",3,8);	
		
		sliderControlList[3] = InstantiateSlider("arm Length",0,9);
		sliderControlList[38] = InstantiateSlider("arm Width",1,9);
		sliderControlList[39] = InstantiateSlider("forearm Length",2,9);
		sliderControlList[40] = InstantiateSlider("forearm Width",3,9);
		sliderControlList[4] = InstantiateSlider("hands Size",4,9);
		
		sliderControlList[5] = InstantiateSlider("feet Size",0,10);
		sliderControlList[6] = InstantiateSlider("leg Separation",1,10);
		sliderControlList[11] = InstantiateSlider("legsSize",2,10);
		sliderControlList[37] = InstantiateSlider("Gluteus Size",3,10);
		
		sliderControlList[36] = InstantiateSlider("breatsSize",0,11);
		sliderControlList[41] = InstantiateSlider("belly",1,11);
		sliderControlList[42] = InstantiateSlider("waist",2,11);

		sliderControlList[46] = InstantiateSlider("Eye Spacing",3,6);
	}

	private void Update () {
		Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
		RaycastHit hit;
		
		if(Input.GetMouseButtonDown(1)){
			if (Physics.Raycast(ray, out hit, 100)){
				Transform tempTransform = hit.collider.transform;

				//Dont want to use an extra layer or specific tag on UMAs, and since UMAData has moved, Ill keep this for now
				if(tempTransform.parent){
					if(tempTransform.parent.parent){
						umaData = tempTransform.parent.parent.GetComponent<UMAData>();
					}
				}

				if(umaData){
					AvatarSetup();
				}
			}
		}
	}

	private void UpdateUMA(float value){
		if(umaData){
			TransferValues();
			UpdateUMAShape();
		}
	}

	private void AvatarSetup(){
		umaDynamicAvatar = umaData.gameObject.GetComponent<UMADynamicAvatar>();
		
		if(cameraTrack){
			cameraTrack.target = umaData.umaRoot.transform;
		}
		
		umaDna = umaData.GetDna<UMADnaHumanoid>();
		umaTutorialDna = umaData.GetDna<UMADnaTutorial>();
		ReceiveValues();
	}
	
	private Slider InstantiateSlider(string name, int X, int Y){
		Transform TempSlider;
		TempSlider = Instantiate(sliderPrefab,Vector3.zero, Quaternion.identity) as Transform;
		TempSlider.SetParent(sliderParent);
		TempSlider.gameObject.name = name;
		Slider tempSlider = TempSlider.GetComponent<Slider>();
		tempSlider.value = 0.5f;

		RectTransform myRect = tempSlider.GetComponent<RectTransform>();

		Vector3 tmpPos = myRect.position;
		tmpPos.x = 60 + X * 100;
		tmpPos.y = -40 - Y * 60;
		myRect.localPosition = tmpPos;

		myRect.anchorMin = new Vector2(0, 1);
		myRect.anchorMax = new Vector2(0, 1);

		tempSlider.transform.FindChild("Text").GetComponent<Text>().text = name;
		tempSlider.onValueChanged.AddListener(UpdateUMA);

		return tempSlider;
	}

	public void UpdateUMAAtlas(){
		umaData.isTextureDirty = true;
		umaData.isAtlasDirty = true;
		umaData.Dirty();	
	}
	
	public void UpdateUMAShape(){
		umaData.isShapeDirty = true;
		umaData.Dirty();
	}

	public virtual void ReceiveValues(){
		if(umaDna != null){
			sliderControlList[0].value = umaDna.height;
			
			sliderControlList[1].value = umaDna.headSize ;
			sliderControlList[43].value = umaDna.headWidth ;
		
			sliderControlList[2].value = umaDna.neckThickness;
			
			sliderControlList[3].value = umaDna.armLength;
			sliderControlList[4].value = umaDna.handsSize;
			sliderControlList[5].value = umaDna.feetSize;
			sliderControlList[6].value = umaDna.legSeparation;
			
			
			sliderControlList[7].value = umaDna.upperMuscle;
			sliderControlList[8].value = umaDna.lowerMuscle;
			sliderControlList[9].value = umaDna.upperWeight;
			sliderControlList[10].value = umaDna.lowerWeight;
		
			sliderControlList[11].value = umaDna.legsSize;
			
			sliderControlList[12].value = umaDna.earsSize;
			sliderControlList[13].value = umaDna.earsPosition;
			sliderControlList[14].value = umaDna.earsRotation;
			
			sliderControlList[15].value = umaDna.noseSize;
			
			sliderControlList[16].value = umaDna.noseCurve;
			sliderControlList[17].value = umaDna.noseWidth;
			sliderControlList[18].value = umaDna.noseInclination;
			sliderControlList[19].value = umaDna.nosePosition;
			sliderControlList[20].value = umaDna.nosePronounced;
			sliderControlList[21].value = umaDna.noseFlatten;
			
			sliderControlList[22].value = umaDna.chinSize;
			sliderControlList[23].value = umaDna.chinPronounced;
			sliderControlList[24].value = umaDna.chinPosition;
			
			sliderControlList[25].value = umaDna.mandibleSize;
			sliderControlList[26].value = umaDna.jawsSize;
			sliderControlList[27].value = umaDna.jawsPosition;
			
			sliderControlList[28].value = umaDna.cheekSize;
			sliderControlList[29].value = umaDna.cheekPosition;
			sliderControlList[30].value = umaDna.lowCheekPronounced;
			sliderControlList[31].value = umaDna.lowCheekPosition;
			
			sliderControlList[32].value = umaDna.foreheadSize;
			sliderControlList[33].value = umaDna.foreheadPosition;
			
			sliderControlList[44].value = umaDna.eyeSize;
			sliderControlList[45].value = umaDna.eyeRotation;
			sliderControlList[34].value = umaDna.lipsSize;
			sliderControlList[35].value = umaDna.mouthSize;
			sliderControlList[36].value = umaDna.breastSize;	
			sliderControlList[37].value = umaDna.gluteusSize;	
			
			sliderControlList[38].value = umaDna.armWidth;
			sliderControlList[39].value = umaDna.forearmLength;
			sliderControlList[40].value = umaDna.forearmWidth;
			
			sliderControlList[41].value = umaDna.belly;
			sliderControlList[42].value = umaDna.waist;
		}

		if (umaTutorialDna != null) {
			sliderControlList[46].enabled = true;
			sliderControlList[46].value = umaTutorialDna.eyeSpacing;
		}
		else {
			sliderControlList[46].enabled = false;
		}
	}
	
	
	public virtual void TransferValues(){
		if(umaDna != null){
			umaDna.height = sliderControlList[0].value;
			umaDna.headSize = sliderControlList[1].value;
			umaDna.headWidth = sliderControlList[43].value;
			
			umaDna.neckThickness = sliderControlList[2].value;
			
			umaDna.armLength = sliderControlList[3].value;
			umaDna.handsSize = sliderControlList[4].value;
			umaDna.feetSize = sliderControlList[5].value;
			umaDna.legSeparation = sliderControlList[6].value;
			
			
			umaDna.upperMuscle = sliderControlList[7].value;
			umaDna.lowerMuscle = sliderControlList[8].value;
			umaDna.upperWeight = sliderControlList[9].value;
			umaDna.lowerWeight = sliderControlList[10].value;
		
			umaDna.legsSize = sliderControlList[11].value;
			
			umaDna.earsSize = sliderControlList[12].value;
			umaDna.earsPosition = sliderControlList[13].value;
			umaDna.earsRotation = sliderControlList[14].value;
			
			umaDna.noseSize = sliderControlList[15].value;
			
			umaDna.noseCurve = sliderControlList[16].value;
			umaDna.noseWidth = sliderControlList[17].value;
			umaDna.noseInclination = sliderControlList[18].value;
			umaDna.nosePosition = sliderControlList[19].value;
			umaDna.nosePronounced = sliderControlList[20].value;
			umaDna.noseFlatten = sliderControlList[21].value;
			
			umaDna.chinSize = sliderControlList[22].value;
			umaDna.chinPronounced = sliderControlList[23].value;
			umaDna.chinPosition = sliderControlList[24].value;
			
			umaDna.mandibleSize = sliderControlList[25].value;
			umaDna.jawsSize = sliderControlList[26].value;
			umaDna.jawsPosition = sliderControlList[27].value;
			
			umaDna.cheekSize = sliderControlList[28].value;
			umaDna.cheekPosition = sliderControlList[29].value;
			umaDna.lowCheekPronounced = sliderControlList[30].value;
			umaDna.lowCheekPosition = sliderControlList[31].value;
			
			umaDna.foreheadSize = sliderControlList[32].value;
			umaDna.foreheadPosition = sliderControlList[33].value;
			
			umaDna.eyeSize = sliderControlList[44].value;
			umaDna.eyeRotation = sliderControlList[45].value;
			umaDna.lipsSize = sliderControlList[34].value;
			umaDna.mouthSize = sliderControlList[35].value;
			umaDna.breastSize = sliderControlList[36].value;	
			umaDna.gluteusSize = sliderControlList[37].value;
			
			umaDna.armWidth = sliderControlList[38].value;
			umaDna.forearmLength = sliderControlList[39].value;
			umaDna.forearmWidth = sliderControlList[40].value;
			
			umaDna.belly = sliderControlList[41].value;
			umaDna.waist = sliderControlList[42].value;
		}

		if (umaTutorialDna != null) {
			umaTutorialDna.eyeSpacing = sliderControlList[46].value;
		}
	}
}
