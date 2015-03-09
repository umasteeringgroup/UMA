using UnityEngine;
using System.Collections;
using UMA;
using System.Collections.Generic;
using UnityEngine.UI;

public class UMAGenericCustomization : MonoBehaviour {
	
	public Transform sliderPrefab;
	
	public UMAData umaData;
	public UMADynamicAvatar umaDynamicAvatar;
	public CameraTrack cameraTrack;

	public Slider[] sliderControlList;
	public UMADnaBase[] allDna;
	
	public SlotLibrary mySlotLibrary;
    public OverlayLibrary myOverlayLibrary;

	private Transform sliderParent;

	void Start()
	{
		sliderParent = GameObject.Find("Canvas").transform;

		if (umaDynamicAvatar != null){
			umaDynamicAvatar.umaData.OnCharacterCreated += new System.Action<UMAData>(umaData_OnCharacterCreated);
		}
	}

	void umaData_OnCharacterCreated(UMAData obj){
		SelectUMA(obj);
		obj.OnCharacterCreated -= new System.Action<UMAData>(umaData_OnCharacterCreated);
	}
	

	void Update () {
		Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
		RaycastHit hit;

		if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)){
			if (Physics.Raycast(ray, out hit, 100)){
				Transform tempTransform = hit.collider.transform;

				//Dont want to use an extra layer or specific tag on UMAs, and since UMAData has moved, Ill keep this for now
				if(tempTransform.parent){
					if(tempTransform.parent.parent){
						SelectUMA(tempTransform.parent.parent.GetComponent<UMAData>());
					}
				}
			}
		}
	}

	private void SelectUMA(UMAData umaData){
		if (sliderControlList != null){
			foreach (var slider in sliderControlList){
				GameObject.Destroy(slider.gameObject);
			}
			sliderControlList = new Slider[0];
		}
		this.umaData = umaData;
		if (umaData){
			AvatarSetup();
		}
	}

	private void UpdateUMA(float value){
		if(umaData){
			TransferValues();
			UpdateUMAShape();
		}
	}

	public void AvatarSetup(){
		umaDynamicAvatar = umaData.gameObject.GetComponent<UMADynamicAvatar>();
		
		if(cameraTrack){
			cameraTrack.target = umaData.umaRoot.transform;
		}

		allDna = umaData.GetAllDna();
		BuildSliders();
		ReceiveValues();
	}

	private void BuildSliders(){
		var sliders = new List<Slider>();
		int row = 0;
		foreach (var dna in allDna){
			if (dna is UMADnaHumanoid) continue;
			int column = 0;
			var fields = dna.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			foreach (var field in fields){
				sliders.Add(InstantiateSlider(field.Name, column++, row));
				if (column > 8){
					column = 0;
					row++;
				}
			}
			row++;
		}
		sliderControlList = sliders.ToArray();
	}
	
	public Slider InstantiateSlider(string name, int X, int Y){
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
	
	public Slider InstantiateStepSlider(string name, int X, int Y){
		Slider tempSlider = InstantiateSlider(name,X,Y);
		tempSlider.wholeNumbers = true;
		
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

	public virtual void ReceiveValues()
	{
		var index = 0;
		foreach (var dna in allDna){
			if (dna is UMADnaHumanoid) continue;
			var fields = dna.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			foreach (var field in fields){
				sliderControlList[index++].value = (float)field.GetValue(dna);
			}
		}		
	}
	
	public virtual void TransferValues(){
		var index = 0;
		foreach (var dna in allDna){
			if (dna is UMADnaHumanoid) continue;
			var fields = dna.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			foreach (var field in fields){
				field.SetValue(dna, sliderControlList[index++].value);
			}
		}
	}
}
