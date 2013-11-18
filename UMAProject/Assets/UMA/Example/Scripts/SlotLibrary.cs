using UnityEngine;
using System.Collections.Generic;
using UMA;

public class SlotLibrary : MonoBehaviour {
	public SlotData[] slotElementList = new SlotData[0];
	public Dictionary<string,SlotData> slotDictionary = new Dictionary<string,SlotData>();
	
	void Awake() {
		UpdateDictionary();
	}
	
	public void UpdateDictionary(){
		slotDictionary.Clear();
		for(int i = 0; i < slotElementList.Length; i++){			
			if(slotElementList[i]){	
				if(!slotDictionary.ContainsKey(slotElementList[i].slotName)){
					slotElementList[i].listID = i;
					slotElementList[i].boneWeights = slotElementList[i].meshRenderer.sharedMesh.boneWeights;
					slotDictionary.Add(slotElementList[i].slotName,slotElementList[i]);	
				}
			}
		}
	}

    public void AddSlot(string name, SlotData slot)
    {
        var list = new SlotData[slotElementList.Length + 1];
        for (int i = 0; i < slotElementList.Length; i++)
        {
            if (slotElementList[i].slotName == name)
            {
                slotElementList[i] = slot;
                return;
            }
            list[i] = slotElementList[i];
        }
        list[list.Length - 1] = slot;
        slotElementList = list;
        slotDictionary.Add(name, slot);
    }
	
	public SlotData InstantiateSlot(string name){
		SlotData source;
        if (!slotDictionary.TryGetValue(name, out source))
        {
            Debug.LogError("Unable to find " + name);
			return null;
        }else{
			return source.Duplicate();
		}
	}
	
	public SlotData InstantiateSlot(string name, List<OverlayData> overlayList){
		SlotData source;
        if (!slotDictionary.TryGetValue(name, out source))
        {
            Debug.LogError("Unable to find " + name);
			return null;
        }else{
			source = source.Duplicate();
			source.SetOverlayList(overlayList);
			return source;
		}
	}
}
