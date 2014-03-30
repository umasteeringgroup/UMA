using UnityEngine;
using System.Collections.Generic;
using UMA;
using System;

public class OverlayLibrary : OverlayLibraryBase
{
	[Obsolete("Internal data, use the helper functions. This field will be marked private in a future version.", false)]
	public OverlayData[] overlayElementList = new OverlayData[0];
	[NonSerialized]
	private Dictionary<int, OverlayData> overlayDictionary;

	public int scaleAdjust = 1;
	public bool readWrite = false;
	public bool compress = false;

	void Awake()
	{
		ValidateDictionary();
	}

#pragma warning disable 618
	override public void UpdateDictionary()
	{
		ValidateDictionary();
		overlayDictionary.Clear();
		for (int i = 0; i < overlayElementList.Length; i++)
		{
			if (overlayElementList[i])
			{
				var hash = UMASkeleton.StringToHash(overlayElementList[i].overlayName);
				if (!overlayDictionary.ContainsKey(hash))
				{
					overlayElementList[i].listID = i;
					overlayDictionary.Add(hash, overlayElementList[i]);
				}
			}
		}
	}

	public override void AddOverlay(OverlayData overlay)
	{
		ValidateDictionary();
		var hash = UMASkeleton.StringToHash(overlay.overlayName);
		if (overlayDictionary.ContainsKey(hash))
		{
			for (int i = 0; i < overlayElementList.Length; i++)
			{
				if (overlayElementList[i].overlayName == overlay.overlayName)
				{
					overlayElementList[i] = overlay;
					break;
				}
			}
		}
		else
		{
			var list = new OverlayData[overlayElementList.Length + 1];
			for (int i = 0; i < overlayElementList.Length; i++)
			{
				list[i] = overlayElementList[i];
			}
			list[list.Length - 1] = overlay;
			overlayElementList = list;
		}
		overlayDictionary[hash] = overlay;
	}
#pragma warning restore 618

	public override void ValidateDictionary()
	{
		if (overlayDictionary == null)
		{
			overlayDictionary = new Dictionary<int, OverlayData>();
			UpdateDictionary();
		}
	}

	public override OverlayData InstantiateOverlay(string name)
	{
		var res = Internal_InstantiateOverlay(UMASkeleton.StringToHash(name));
		if (res == null)
		{
			throw new UMAResourceNotFoundException("OverlayLibrary: Unable to find: " + name);
		}
		return res;
	}

	public override OverlayData InstantiateOverlay(int nameHash)
	{
		var res = Internal_InstantiateOverlay(nameHash);
		if (res == null)
		{
			throw new UMAResourceNotFoundException("OverlayLibrary: Unable to find hash: " + nameHash);
		}
		return res;
	}

	public override OverlayData InstantiateOverlay(string name, Color color)
	{
		var res = Internal_InstantiateOverlay(UMASkeleton.StringToHash(name));
		if (res == null)
		{
			throw new UMAResourceNotFoundException("OverlayLibrary: Unable to find: " + name);
		}
		res.color = color;
		return res;
	}

	public override OverlayData InstantiateOverlay(int nameHash, Color color)
	{
		var res = Internal_InstantiateOverlay(nameHash);
		if (res == null)
		{
			throw new UMAResourceNotFoundException("OverlayLibrary: Unable to find hash: " + nameHash);
		}
		res.color = color;
		return res;
	}

	private OverlayData Internal_InstantiateOverlay(int nameHash)
	{
		ValidateDictionary();
		OverlayData source;
		if (!overlayDictionary.TryGetValue(nameHash, out source))
		{
			return null;
		}
		else
		{
			source = source.Duplicate();
			return source;
		}
	}

	public OverlayData[]  GetAllOverlays()
	{
#pragma warning disable 618
		return overlayElementList;
#pragma warning restore 618
	}
}
