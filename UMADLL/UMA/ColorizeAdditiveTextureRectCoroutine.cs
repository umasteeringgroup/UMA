using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace UMA
{
	public class ColorizeAdditiveTextureRectCoroutine : WorkerCoroutine
	{

	    Color32[] finalList;
	    Color32[] insertList;
	    Color32[] maskList;
	    Color32 colorAdjust;
	    Color32 colorAddtive;
	    Rect insertRect;
	    Vector2 finalResolution;
	    Vector2 textureResolution;
	    int maxPixel;

	    public void Prepare(Color32[] _finalList, Color32[] _insertList, Color32[] _maskList, Color32 _ColorAdjust, Color32 _ColorAdditive, Rect _insertRect, Vector2 _finalResolution, Vector2 _textureResolution, int _MaxPixel)
	    {
	        finalList = _finalList;
	        insertList = _insertList;
	        maskList = _maskList;
	        insertRect = _insertRect;
	        finalResolution = _finalResolution;
	        textureResolution = _textureResolution;
	        maxPixel = _MaxPixel;
	        colorAdjust = _ColorAdjust;
	        colorAddtive = _ColorAdditive;
	    }


	    protected override void Start()
	    {

		}

	    protected override IEnumerator workerMethod()
	    {			
			int startingIndexAtlas = 0;
			int startingIndexTexture = 0;
			float pixelCount = 0;
			
			int ColorAdjR = colorAdjust.r;
	        int ColorAdjG = colorAdjust.g;
	        int ColorAdjB = colorAdjust.b;
	        int ColorAdjA = colorAdjust.a;

	        int ColorAddR = colorAddtive.r;
	        int ColorAddG = colorAddtive.g;
	        int ColorAddB = colorAddtive.b;
	        int ColorAddA = colorAddtive.a;
			
			//To correct value change, might be temporary
	//		int ColorAddR = colorAddtive.r << 8;
	//        int ColorAddG = colorAddtive.g << 8;
	//        int ColorAddB = colorAddtive.b << 8;
	//        int ColorAddA = colorAddtive.a << 8;

			
			Debug.Log(000);
			for(float y = 0; y < insertRect.height; y++){
				startingIndexAtlas = Vector2ToIndex(insertRect.x,y + insertRect.y,finalResolution);
				startingIndexTexture = Vector2ToIndex(0,y,textureResolution);		
				
				for(float x = 0; x < insertRect.width; x++){
					
					//To correct value change, might be temporary
					finalList[startingIndexAtlas] = Color32.Lerp(finalList[startingIndexAtlas],new Color32(
					ConvertToByte((insertList[startingIndexTexture].r * ColorAdjR >> 8) + ColorAddR ),
					ConvertToByte((insertList[startingIndexTexture].g * ColorAdjG >> 8) + ColorAddG ),
					ConvertToByte((insertList[startingIndexTexture].b * ColorAdjB >> 8) + ColorAddB ),
					ConvertToByte((insertList[startingIndexTexture].a * ColorAdjA >> 8) + ColorAddA )
					),maskList[startingIndexTexture].a/255.0f);
				
	//				int mask = maskList[startingIndexTexture].a;
	//	            int inverseRatio = 255 - mask;
	//				
	//	            finalList[startingIndexAtlas] = new Color32(
	//                ConvertToByte((finalList[startingIndexAtlas].r * inverseRatio >> 8) + ((insertList[startingIndexTexture].r * ColorAdjR + ColorAddR) * mask >> 16)),
	//                ConvertToByte((finalList[startingIndexAtlas].g * inverseRatio >> 8) + ((insertList[startingIndexTexture].g * ColorAdjG + ColorAddG) * mask >> 16)),
	//                ConvertToByte((finalList[startingIndexAtlas].b * inverseRatio >> 8) + ((insertList[startingIndexTexture].b * ColorAdjB + ColorAddB) * mask >> 16)),
	//                ConvertToByte((finalList[startingIndexAtlas].a * inverseRatio >> 8) + ((insertList[startingIndexTexture].a * ColorAdjA + ColorAddA) * mask >> 16)));
					
					startingIndexAtlas ++;
					startingIndexTexture ++;
				}
				pixelCount += insertRect.width;
				
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