using UnityEngine;
using System.Collections;

public class SliderControl : MonoBehaviour {
	public bool pressed;

	public float actualValue;
	public Vector2 sliderOffset;
	
	public bool stepSlider;
	public byte actualStepValue;
	public int stepSize;
	
	public GUIText descriptionText;
	public GUIText valueText;
	public GUITexture sliderBar;
	public GUITexture sliderBarCollision;
	public GUITexture sliderMark;
	
	public Vector2 descriptionTextOriginalPos;
	public Vector2 sliderBarOriginalPos;
	public Vector2 sliderBarCollisionOriginalPos;
	public Vector2 sliderMarkOriginalPos;
	
	

	void Start () {
		descriptionTextOriginalPos = descriptionText.pixelOffset;
		sliderBarOriginalPos.x = sliderBar.pixelInset.x;
		sliderBarOriginalPos.y = sliderBar.pixelInset.y;
		sliderBarCollisionOriginalPos.x = sliderBarCollision.pixelInset.x;
		sliderBarCollisionOriginalPos.y = sliderBarCollision.pixelInset.y;
		
		sliderMarkOriginalPos.x = sliderMark.pixelInset.x;
		sliderMarkOriginalPos.y = sliderMark.pixelInset.y;
		name = descriptionText.text;
	}
	
	void Update () {
		descriptionText.pixelOffset = descriptionTextOriginalPos + sliderOffset;
		
		sliderBar.pixelInset = new Rect(sliderBarOriginalPos.x + sliderOffset.x, sliderBarOriginalPos.y + sliderOffset.y, sliderBar.pixelInset.width, sliderBar.pixelInset.height);
		
		sliderBarCollision.pixelInset = new Rect(sliderBarCollisionOriginalPos.x + sliderOffset.x, sliderBarCollisionOriginalPos.y + sliderOffset.y, sliderBarCollision.pixelInset.width,sliderBarCollision.pixelInset.height);
		
		sliderMark.pixelInset = new Rect((sliderBarCollision.pixelInset.width * actualValue) + sliderOffset.x - sliderMark.pixelInset.width/2, sliderMarkOriginalPos.y + sliderOffset.y, sliderMark.pixelInset.width, sliderMark.pixelInset.height);
		valueText.pixelOffset = new Vector2(sliderMark.pixelInset.x + 16,sliderMark.pixelInset.y + 18);
			
		if(Input.GetMouseButtonDown(0)){
			if(sliderBarCollision.HitTest(Input.mousePosition)){
				pressed = true;
			}
		}
		
		
		if(pressed){
			actualValue = (Input.mousePosition.x - sliderBarCollision.pixelInset.x)/sliderBarCollision.pixelInset.width;
			
			if(actualValue > 1){
				actualValue = 1;
			}else if(actualValue < 0){
				actualValue = 0;
			}
			
			if(stepSlider){
				actualStepValue = (byte)Mathf.RoundToInt(actualValue * stepSize);
			}
			
			if(Input.GetMouseButtonUp(0)){
				pressed = false;
			}
		}
		
		valueText.text = actualValue.ToString("F2");
	}
	
	public void ForceUpdate(){
		//for stepSlider first update
		actualValue = (float)actualStepValue/stepSize;
	}
}