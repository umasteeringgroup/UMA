using UnityEngine;
using System.Collections;
using UMA;

public class UMACustomization : MonoBehaviour {
	
	public Transform sliderPrefab;
	
	public UMAData umaData;
	public UMADynamicAvatar umaDynamicAvatar;
	public CameraTrack cameraTrack;
	private UMADnaHumanoid umaDna;
	private UMADnaTutorial umaTutorialDna;

	public SliderControl[] sliderControlList = new SliderControl[0];
	
	public SlotLibrary mySlotLibrary;
    public OverlayLibrary myOverlayLibrary;

	public bool editing = false;

	protected virtual void Start()
	{
		sliderControlList = new SliderControl[47];	
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
	

	void Update () {
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
		
		if(umaData){
			TransferValues();
			editing = false;
			for(int i = 0; i < sliderControlList.Length; i++){
				if(sliderControlList[i].pressed == true){
					editing = true;
					UpdateUMAShape();
				}
			}
		}
	}

	public void AvatarSetup(){
		umaDynamicAvatar = umaData.gameObject.GetComponent<UMADynamicAvatar>();
		
		if(cameraTrack){
			cameraTrack.target = umaData.umaRoot.transform;
		}
		
		umaDna = umaData.GetDna<UMADnaHumanoid>();
		umaTutorialDna = umaData.GetDna<UMADnaTutorial>();
		ReceiveValues();
	}
	
	public SliderControl InstantiateSlider(string name, int X, int Y){
		Transform TempSlider;
		TempSlider = Instantiate(sliderPrefab,Vector3.zero, Quaternion.identity) as Transform;
		TempSlider.parent = transform;
		TempSlider.gameObject.name = name;
		SliderControl tempSlider = TempSlider.GetComponent("SliderControl") as SliderControl;
		tempSlider.actualValue = 0.5f;
		tempSlider.descriptionText.text = name;
		tempSlider.sliderOffset.x = 20 + X*100;
		tempSlider.sliderOffset.y = -20 - Y*60;
		return tempSlider;
	}
	
	public SliderControl InstantiateStepSlider(string name, int X, int Y){
		SliderControl tempSlider = InstantiateSlider(name,X,Y);
		tempSlider.stepSlider = true;
		
		return tempSlider;
	}
	
	
	public void UpdateUMAAtlas(){
		umaData.isTextureDirty = true;
		umaData.Dirty();	
	}
	
	public void UpdateUMAShape(){
		umaData.isShapeDirty = true;
		umaData.Dirty();
	}

	public virtual void ReceiveValues()
	{
		if(umaDna != null){
			sliderControlList[0].actualValue = umaDna.height;
			
			sliderControlList[1].actualValue = umaDna.headSize ;
			sliderControlList[43].actualValue = umaDna.headWidth ;
		
			sliderControlList[2].actualValue = umaDna.neckThickness;
			
			sliderControlList[3].actualValue = umaDna.armLength;
			sliderControlList[4].actualValue = umaDna.handsSize;
			sliderControlList[5].actualValue = umaDna.feetSize;
			sliderControlList[6].actualValue = umaDna.legSeparation;
			
			
			sliderControlList[7].actualValue = umaDna.upperMuscle;
			sliderControlList[8].actualValue = umaDna.lowerMuscle;
			sliderControlList[9].actualValue = umaDna.upperWeight;
			sliderControlList[10].actualValue = umaDna.lowerWeight;
		
			sliderControlList[11].actualValue = umaDna.legsSize;
			
			sliderControlList[12].actualValue = umaDna.earsSize;
			sliderControlList[13].actualValue = umaDna.earsPosition;
			sliderControlList[14].actualValue = umaDna.earsRotation;
			
			sliderControlList[15].actualValue = umaDna.noseSize;
			
			sliderControlList[16].actualValue = umaDna.noseCurve;
			sliderControlList[17].actualValue = umaDna.noseWidth;
			sliderControlList[18].actualValue = umaDna.noseInclination;
			sliderControlList[19].actualValue = umaDna.nosePosition;
			sliderControlList[20].actualValue = umaDna.nosePronounced;
			sliderControlList[21].actualValue = umaDna.noseFlatten;
			
			sliderControlList[22].actualValue = umaDna.chinSize;
			sliderControlList[23].actualValue = umaDna.chinPronounced;
			sliderControlList[24].actualValue = umaDna.chinPosition;
			
			sliderControlList[25].actualValue = umaDna.mandibleSize;
			sliderControlList[26].actualValue = umaDna.jawsSize;
			sliderControlList[27].actualValue = umaDna.jawsPosition;
			
			sliderControlList[28].actualValue = umaDna.cheekSize;
			sliderControlList[29].actualValue = umaDna.cheekPosition;
			sliderControlList[30].actualValue = umaDna.lowCheekPronounced;
			sliderControlList[31].actualValue = umaDna.lowCheekPosition;
			
			sliderControlList[32].actualValue = umaDna.foreheadSize;
			sliderControlList[33].actualValue = umaDna.foreheadPosition;
			
			sliderControlList[44].actualValue = umaDna.eyeSize;
			sliderControlList[45].actualValue = umaDna.eyeRotation;
			sliderControlList[34].actualValue = umaDna.lipsSize;
			sliderControlList[35].actualValue = umaDna.mouthSize;
			sliderControlList[36].actualValue = umaDna.breastSize;	
			sliderControlList[37].actualValue = umaDna.gluteusSize;	
			
			sliderControlList[38].actualValue = umaDna.armWidth;
			sliderControlList[39].actualValue = umaDna.forearmLength;
			sliderControlList[40].actualValue = umaDna.forearmWidth;
			
			sliderControlList[41].actualValue = umaDna.belly;
			sliderControlList[42].actualValue = umaDna.waist;
			
//			for(int i = 0; i < sliderControlList.Length; i++){
//				sliderControlList[i].ForceUpdate();
//			}
		}

		if (umaTutorialDna != null) {
			sliderControlList[46].enabled = true;
			sliderControlList[46].actualValue = umaTutorialDna.eyeSpacing;
		}
		else {
			sliderControlList[46].enabled = false;
		}
	}
	
	
	public virtual void TransferValues(){
		if(umaDna != null){
			umaDna.height = sliderControlList[0].actualValue;
			umaDna.headSize = sliderControlList[1].actualValue;
			umaDna.headWidth = sliderControlList[43].actualValue;
			
			umaDna.neckThickness = sliderControlList[2].actualValue;
			
			umaDna.armLength = sliderControlList[3].actualValue;
			umaDna.handsSize = sliderControlList[4].actualValue;
			umaDna.feetSize = sliderControlList[5].actualValue;
			umaDna.legSeparation = sliderControlList[6].actualValue;
			
			
			umaDna.upperMuscle = sliderControlList[7].actualValue;
			umaDna.lowerMuscle = sliderControlList[8].actualValue;
			umaDna.upperWeight = sliderControlList[9].actualValue;
			umaDna.lowerWeight = sliderControlList[10].actualValue;
		
			umaDna.legsSize = sliderControlList[11].actualValue;
			
			umaDna.earsSize = sliderControlList[12].actualValue;
			umaDna.earsPosition = sliderControlList[13].actualValue;
			umaDna.earsRotation = sliderControlList[14].actualValue;
			
			umaDna.noseSize = sliderControlList[15].actualValue;
			
			umaDna.noseCurve = sliderControlList[16].actualValue;
			umaDna.noseWidth = sliderControlList[17].actualValue;
			umaDna.noseInclination = sliderControlList[18].actualValue;
			umaDna.nosePosition = sliderControlList[19].actualValue;
			umaDna.nosePronounced = sliderControlList[20].actualValue;
			umaDna.noseFlatten = sliderControlList[21].actualValue;
			
			umaDna.chinSize = sliderControlList[22].actualValue;
			umaDna.chinPronounced = sliderControlList[23].actualValue;
			umaDna.chinPosition = sliderControlList[24].actualValue;
			
			umaDna.mandibleSize = sliderControlList[25].actualValue;
			umaDna.jawsSize = sliderControlList[26].actualValue;
			umaDna.jawsPosition = sliderControlList[27].actualValue;
			
			umaDna.cheekSize = sliderControlList[28].actualValue;
			umaDna.cheekPosition = sliderControlList[29].actualValue;
			umaDna.lowCheekPronounced = sliderControlList[30].actualValue;
			umaDna.lowCheekPosition = sliderControlList[31].actualValue;
			
			umaDna.foreheadSize = sliderControlList[32].actualValue;
			umaDna.foreheadPosition = sliderControlList[33].actualValue;
			
			umaDna.eyeSize = sliderControlList[44].actualValue;
			umaDna.eyeRotation = sliderControlList[45].actualValue;
			umaDna.lipsSize = sliderControlList[34].actualValue;
			umaDna.mouthSize = sliderControlList[35].actualValue;
			umaDna.breastSize = sliderControlList[36].actualValue;	
			umaDna.gluteusSize = sliderControlList[37].actualValue;
			
			umaDna.armWidth = sliderControlList[38].actualValue;
			umaDna.forearmLength = sliderControlList[39].actualValue;
			umaDna.forearmWidth = sliderControlList[40].actualValue;
			
			umaDna.belly = sliderControlList[41].actualValue;
			umaDna.waist = sliderControlList[42].actualValue;
		}

		if (umaTutorialDna != null) {
			umaTutorialDna.eyeSpacing = sliderControlList[46].actualValue;
		}
	}
}
