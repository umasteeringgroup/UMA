using UnityEngine;
using System.Collections;
using UMA;
using System.Collections.Generic;

public class UMAGenericCustomization : MonoBehaviour {
	
	public Transform sliderPrefab;
	
	public UMAData umaData;
	public UMADynamicAvatar umaDynamicAvatar;
	public CameraTrack cameraTrack;

	public SliderControl[] sliderControlList;
	public UMADnaBase[] allDna;
	
	public SlotLibrary mySlotLibrary;
    public OverlayLibrary myOverlayLibrary;

	public bool editing = false;

	protected virtual void Start()
	{
	}
	

	void Update () {
		Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
		RaycastHit hit;

		if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
		{
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

	private void SelectUMA(UMAData umaData)
	{
		if (sliderControlList != null)
		{
			foreach (var slider in sliderControlList)
			{
				GameObject.Destroy(slider.gameObject);
			}
			sliderControlList = new SliderControl[0];
		}
		this.umaData = umaData;
		if (umaData)
		{
			AvatarSetup();
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

	private void BuildSliders()
	{
		var sliders = new List<SliderControl>();
		int row = 0;
		foreach (var dna in allDna)
		{
			int column = 0;
			var fields = dna.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			foreach (var field in fields)
			{
				sliders.Add(InstantiateSlider(field.Name, column++, row));
				if (column > 8)
				{
					column = 0;
					row++;
				}
			}
			row++;
		}
		sliderControlList = sliders.ToArray();
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
		var index = 0;
		foreach (var dna in allDna)
		{
			var fields = dna.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			foreach (var field in fields)
			{
				sliderControlList[index++].actualValue = (float)field.GetValue(dna);
			}
		}		
	}
	
	
	public virtual void TransferValues()
	{
		var index = 0;
		foreach (var dna in allDna)
		{
			var fields = dna.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
			foreach (var field in fields)
			{
				field.SetValue(dna, sliderControlList[index++].actualValue);
			}
		}
	}
}
