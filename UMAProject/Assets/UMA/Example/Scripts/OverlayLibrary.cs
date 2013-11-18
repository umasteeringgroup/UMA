using UnityEngine;
using System.Collections.Generic;
using UMA;

public class OverlayLibrary : MonoBehaviour {
    public OverlayData[] overlayElementList = new OverlayData[0];
	public Dictionary<string,OverlayData> overlayDictionary = new Dictionary<string,OverlayData>();
	
	public int scaleAdjust = 1;
	public bool readWrite = false;
	public bool compress = false;
	
	void Awake() {
		UpdateDictionary();
	}
	
	public void UpdateDictionary(){
		overlayDictionary.Clear();
		for(int i = 0; i < overlayElementList.Length; i++){			
			if(overlayElementList[i]){				
				if(!overlayDictionary.ContainsKey(overlayElementList[i].overlayName)){
					overlayElementList[i].listID = i;
					overlayDictionary.Add(overlayElementList[i].overlayName,overlayElementList[i]);	
				}
			}
		}
	}

    public void AddOverlay(string name, OverlayData overlay)
    {
        var list = new OverlayData[overlayElementList.Length + 1];
        for (int i = 0; i < overlayElementList.Length; i++)
        {
            if (overlayElementList[i].overlayName == name)
            {
                overlayElementList[i] = overlay;
                return;
            }
            list[i] = overlayElementList[i];
        }
        list[list.Length - 1] = overlay;
        overlayElementList = list;
        overlayDictionary.Add(name, overlay);
    }

	//SPOT
//	public OverlayData GetOverlay(string name){		
//		return new OverlayData(this, name);
//	}
	
	public OverlayData InstantiateOverlay(string name){
		OverlayData source;
        if (!overlayDictionary.TryGetValue(name, out source))
        {
            Debug.LogError("Unable to find " + name);
			return null;
        }else{
			return source.Duplicate();
		}
	}
	
	public OverlayData InstantiateOverlay(string name, Color color){
		OverlayData source;
        if (!overlayDictionary.TryGetValue(name, out source))
        {
            Debug.LogError("Unable to find " + name);
			return null;
        }else{
			source = source.Duplicate();
			source.color = color;
			return source;
		}
	}
}
