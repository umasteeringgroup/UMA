using UnityEngine;
using System.Collections.Generic;
using System;

namespace UMA
{
	public class OverlayLibrary : OverlayLibraryBase
	{
		[SerializeField]
		protected OverlayDataAsset[] overlayElementList = new OverlayDataAsset[0];
		[NonSerialized]
		private Dictionary<int, OverlayDataAsset> overlayDictionary;

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
					var hash = UMAUtils.StringToHash(overlayElementList[i].overlayName);
					if (!overlayDictionary.ContainsKey(hash))
					{
						overlayDictionary.Add(hash, overlayElementList[i]);
					}
				}
			}
		}

		public override bool HasOverlay(string Name)
		{
			ValidateDictionary();
			var hash = UMAUtils.StringToHash(Name);
			return overlayDictionary.ContainsKey(hash);
		}

		public override bool HasOverlay(int NameHash)
		{
			ValidateDictionary();
			return overlayDictionary.ContainsKey(NameHash);
		}

		public override void AddOverlayAsset(OverlayDataAsset overlay)
		{
			ValidateDictionary();
			var hash = UMAUtils.StringToHash(overlay.overlayName);
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
				var list = new OverlayDataAsset[overlayElementList.Length + 1];
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
				overlayDictionary = new Dictionary<int, OverlayDataAsset>();
				UpdateDictionary();
			}
		}

		public override OverlayData InstantiateOverlay(string name)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating overlay: " + name);
#endif
			var res = Internal_InstantiateOverlay(UMAUtils.StringToHash(name));
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
#if SUPER_LOGGING
			Debug.Log("Instantiating overlay: " + name);
#endif
			var res = Internal_InstantiateOverlay(UMAUtils.StringToHash(name));
			if (res == null)
			{
				throw new UMAResourceNotFoundException("OverlayLibrary: Unable to find: " + name);
			}
			res.colorData.color = color;
			return res;
		}

		public override OverlayData InstantiateOverlay(int nameHash, Color color)
		{
			var res = Internal_InstantiateOverlay(nameHash);
			if (res == null)
			{
				throw new UMAResourceNotFoundException("OverlayLibrary: Unable to find hash: " + nameHash);
			}
			res.colorData.color = color;
			return res;
		}

		private OverlayData Internal_InstantiateOverlay(int nameHash)
		{
			ValidateDictionary();
			OverlayDataAsset source;
			if (!overlayDictionary.TryGetValue(nameHash, out source))
			{
				return null;
			}
			else
			{
				return new OverlayData(source);
			}
		}

		public override OverlayDataAsset[] GetAllOverlayAssets()
		{
	#pragma warning disable 618
			return overlayElementList;
	#pragma warning restore 618
		}
	}
}
