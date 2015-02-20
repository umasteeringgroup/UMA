using UnityEngine;
using System.Collections.Generic;
using UMA;
using System;

public class SlotLibrary : SlotLibraryBase
{
	[Obsolete("Internal data, use the helper functions. This field will be marked private in a future version.", false)]
	public SlotData[] slotElementList = new SlotData[0];
	[NonSerialized]
	private Dictionary<int, SlotData> slotDictionary;

	void Awake()
	{
		ValidateDictionary();
	}

#pragma warning disable 618
	override public void UpdateDictionary()
	{
		ValidateDictionary();
		slotDictionary.Clear();
		for (int i = 0; i < slotElementList.Length; i++)
		{
			if (slotElementList[i])
			{
				var hash = UMASkeleton.StringToHash(slotElementList[i].slotName);
				if (!slotDictionary.ContainsKey(hash))
				{
					slotElementList[i].listID = i;
					if (slotElementList[i].meshRenderer != null)
					{
						slotElementList[i].boneWeights = slotElementList[i].meshRenderer.sharedMesh.boneWeights;
					}
					slotDictionary.Add(hash, slotElementList[i]);
				}
			}
		}
	}

	public override void ValidateDictionary()
	{
		if (slotDictionary == null)
		{
			slotDictionary = new Dictionary<int, SlotData>();
			UpdateDictionary();
		}
	}

	public override void AddSlot(SlotData slot)
	{
		ValidateDictionary();
		var hash = UMASkeleton.StringToHash(slot.slotName);
		if (slotDictionary.ContainsKey(hash))
		{
			for (int i = 0; i < slotElementList.Length; i++)
			{
				if (slotElementList[i].slotName == slot.slotName)
				{
					slotElementList[i] = slot;
					break;
				}
			}
		}
		else
		{
			var list = new SlotData[slotElementList.Length + 1];
			for (int i = 0; i < slotElementList.Length; i++)
			{
				list[i] = slotElementList[i];
			}
			list[list.Length - 1] = slot;
			slotElementList = list;
		}
		slotDictionary[hash] = slot;
	}
#pragma warning restore 618

	public override SlotData InstantiateSlot(string name)
	{
		var res = Internal_InstantiateSlot(UMASkeleton.StringToHash(name));
		if (res == null)
		{
			throw new UMAResourceNotFoundException("SlotLibrary: Unable to find: " + name);
		}
		return res;
	}
	public override SlotData InstantiateSlot(int nameHash)
	{
		var res = Internal_InstantiateSlot(nameHash);
		if (res == null)
		{
			throw new UMAResourceNotFoundException("SlotLibrary: Unable to find hash: " + nameHash);
		}
		return res;
	}

	public override SlotData InstantiateSlot(string name, List<OverlayData> overlayList)
	{
		var res = Internal_InstantiateSlot(UMASkeleton.StringToHash(name));
		if (res == null)
		{
			throw new UMAResourceNotFoundException("SlotLibrary: Unable to find: " + name);
		}
		res.SetOverlayList(overlayList);
		return res;
	}

	public override SlotData InstantiateSlot(int nameHash, List<OverlayData> overlayList)
	{
		var res = Internal_InstantiateSlot(nameHash);
		if (res == null)
		{
#if UNITY_EDITOR
			foreach (var path in UnityEditor.AssetDatabase.GetAllAssetPaths())
			{
				if (!path.EndsWith(".asset")) continue;
				var slot = UnityEditor.AssetDatabase.LoadAssetAtPath(path, typeof(SlotData)) as SlotData;
				if (slot == null) continue;
				var hash = UMASkeleton.StringToHash(slot.slotName);
				if (hash == nameHash)
				{
					throw new UMAResourceNotFoundException("SlotLibrary: Unable to find: " + slot.slotName);
				}
			}
#endif
			throw new UMAResourceNotFoundException("SlotLibrary: Unable to find hash: " + nameHash);
		}
		res.SetOverlayList(overlayList);
		return res;
	}

	private SlotData Internal_InstantiateSlot(int nameHash)
	{
		ValidateDictionary();
		SlotData source;
		if (!slotDictionary.TryGetValue(nameHash, out source))
		{
			return null;
		}
		else
		{
			return source.Duplicate();
		}
	}

	public SlotData[] GetAllSlots()
	{
#pragma warning disable 618
		return slotElementList;
#pragma warning restore 618
	}
}
