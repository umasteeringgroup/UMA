using UnityEngine;
using System.Collections;
using System.Collections.Generic;



namespace UMA
{
	public class CopyTextureRectCoroutine : WorkerCoroutine
	{
		
		Color32[] finalList;
		Color32[] insertList; 
		Rect insertRect;
		Vector2 finalResolution;
		Vector2 textureResolution;
		int maxPixel;
		
	    public void Prepare(Color32[] _finalList, Color32[] _insertList, Rect _insertRect,Vector2 _finalResolution,Vector2 _textureResolution,int _MaxPixel)
	    {
			finalList = _finalList;
			insertList = _insertList; 
			insertRect = _insertRect;
			finalResolution = _finalResolution;
			textureResolution = _textureResolution;
			maxPixel = _MaxPixel;
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
			
			
			for(float y = 0; y < insertRect.height; y++){
				startingIndexAtlas = Vector2ToIndex(insertRect.x,y + insertRect.y,finalResolution);
				startingIndexTexture = Vector2ToIndex(0,y,textureResolution);
				
				for(float x = 0; x < insertRect.width; x++){
					finalList[startingIndexAtlas] = insertList[startingIndexTexture];
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