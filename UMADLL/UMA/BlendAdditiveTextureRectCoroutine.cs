using UnityEngine;
using System.Collections;
using System.Collections.Generic;


namespace UMA
{
	public class BlendAdditiveTextureRectCoroutine : WorkerCoroutine
	{
		
		Color32[] finalList;
		Color32[] insertList; 
		Rect insertRect;
		Vector2 finalResolution;
		Vector2 textureResolution;
		int maxPixel;
	    Color32 colorAddtive;
		Color32[] maskList;

	    public void Prepare(Color32[] _finalList, Color32[] _insertList, Color32[] _maskList, Color32 _colorAddtive, Rect _insertRect, Vector2 _finalResolution, Vector2 _textureResolution, int _MaxPixel)
	    {
			finalList = _finalList;
			insertList = _insertList; 
			maskList = _maskList;
			insertRect = _insertRect;
			finalResolution = _finalResolution;
			textureResolution = _textureResolution;
			maxPixel = _MaxPixel;
	        colorAddtive = _colorAddtive;
	    }

	    protected override void Start()
	    {

		}

	    protected override IEnumerator workerMethod()
	    {	

			if(insertList.Length != (insertRect.width*insertRect.height)){
				//Wrong texture or rect!
				Debug.LogWarning("Wrong texture or rect");
			}
			
			int startingIndexAtlas = 0;
			int startingIndexTexture = 0;
			float pixelCount = 0;

	        int ColorAddR = colorAddtive.r;
	        int ColorAddG = colorAddtive.g;
	        int ColorAddB = colorAddtive.b;
	        int ColorAddA = colorAddtive.a;
			
			for(float y = 0; y < insertRect.height; y++){
				startingIndexAtlas = Vector2ToIndex(insertRect.x,y + insertRect.y,finalResolution);
				startingIndexTexture = Vector2ToIndex(0,y,textureResolution);
				
				for(float x = 0; x < insertRect.width; x++){
					
					//To correct value change, might be temporary
					finalList[startingIndexAtlas] = Color32.Lerp(finalList[startingIndexAtlas],new Color32(
					ConvertToByte(insertList[startingIndexTexture].r + ColorAddR),
					ConvertToByte(insertList[startingIndexTexture].g + ColorAddG),
					ConvertToByte(insertList[startingIndexTexture].b + ColorAddB),
					ConvertToByte(insertList[startingIndexTexture].a + ColorAddA)
					),maskList[startingIndexTexture].a/255.0f);				
					
	//				int mask = maskList[startingIndexTexture].a;
	//	            int inverseRatio = 255 - mask;
	//
	//                finalList[startingIndexAtlas] = new Color32(
	//                ConvertToByte(((finalList[startingIndexAtlas].r * inverseRatio + insertList[startingIndexTexture].r * mask) >> 8) + ColorAddR),
	//                ConvertToByte(((finalList[startingIndexAtlas].g * inverseRatio + insertList[startingIndexTexture].g * mask) >> 8) + ColorAddG),
	//                ConvertToByte(((finalList[startingIndexAtlas].b * inverseRatio + insertList[startingIndexTexture].b * mask) >> 8) + ColorAddB),
	//                ConvertToByte(((finalList[startingIndexAtlas].a * inverseRatio + insertList[startingIndexTexture].a * mask) >> 8) + ColorAddA));
	              
					
					startingIndexAtlas ++;
					startingIndexTexture ++;
				}
				pixelCount += insertRect.width;
				
				//if inside InnerLoop
				if(pixelCount > maxPixel){
					yield return null;
					pixelCount = 0;
				}
			}
		
			yield return null;	
	    }

	    private byte ConvertToByte(int value)
	    {
	        return value >= 255 ? (byte)255 : (byte)value;
	    }
		
		public Vector2 IndexToVector2(int index,Vector2 gridSize){
			if(index > (gridSize.x*gridSize.y)-1 || index < 0){
				return new Vector2(-1,-1);
			}

			float tempVectorX = index % gridSize.x;
			float tempVectorY = (index - tempVectorX) / gridSize.x;
			
			return new Vector2(tempVectorX,tempVectorY);
		}
		
		public int Vector2ToIndex(float nodePositionX,float nodePositionY,Vector2 gridSize){
			if(nodePositionX >= gridSize.x || nodePositionX < 0 || nodePositionY >= gridSize.y || nodePositionY < 0){
				return -1;
			}
		
			int tempIndex;
			tempIndex = Mathf.FloorToInt((nodePositionY * gridSize.x) + nodePositionX);
		
			return tempIndex;
		}
		
		
	    protected override void Stop()
	    {
			finalList = null;
			insertList = null; 
	    }
	}
}