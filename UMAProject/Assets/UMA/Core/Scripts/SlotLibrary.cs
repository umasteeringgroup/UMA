using UnityEngine;
using System.Collections.Generic;
using System;

namespace UMA
{
	public class SlotLibrary : SlotLibraryBase
	{
		[SerializeField]
		protected SlotDataAsset[] slotElementList = new SlotDataAsset[0];
		[NonSerialized]
		private Dictionary<int, SlotDataAsset> slotDictionary;

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
					var hash = slotElementList[i].nameHash;
					if (!slotDictionary.ContainsKey(hash))
					{
						slotDictionary.Add(hash, slotElementList[i]);
					}
				}
			}
		}

		public override void ValidateDictionary()
		{
			if (slotDictionary == null)
			{
				slotDictionary = new Dictionary<int, SlotDataAsset>();
				UpdateDictionary();
			}
		}

		public override void AddSlotAsset(SlotDataAsset slot)
		{
			ValidateDictionary();
			if (slotDictionary.ContainsKey(slot.nameHash))
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
				var list = new SlotDataAsset[slotElementList.Length + 1];
				for (int i = 0; i < slotElementList.Length; i++)
				{
					list[i] = slotElementList[i];
				}
				list[list.Length - 1] = slot;
				slotElementList = list;
			}
			slotDictionary[slot.nameHash] = slot;
		}
	#pragma warning restore 618

		public override bool HasSlot(string name)
		{
			ValidateDictionary();
			return slotDictionary.ContainsKey(UMAUtils.StringToHash(name));
		}

		public override bool HasSlot(int nameHash)
		{
			ValidateDictionary();
			return slotDictionary.ContainsKey(nameHash);
		}

		public override SlotData InstantiateSlot(string name)
		{
#if SUPER_LOGGING
			Debug.Log("Instantiating slot: " + name);
#endif
			var res = Internal_InstantiateSlot(UMAUtils.StringToHash(name));
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
#if SUPER_LOGGING
			Debug.Log("Instantiating slot: " + name);
#endif
			var res = Internal_InstantiateSlot(UMAUtils.StringToHash(name));
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
					if (!path.EndsWith(".asset"))
                    {
                        continue;
                    }

                    var slot = UnityEditor.AssetDatabase.LoadAssetAtPath(path, typeof(SlotDataAsset)) as SlotDataAsset;
					if (slot == null)
                    {
                        continue;
                    }

                    if (slot.nameHash == nameHash)
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
			SlotDataAsset source;
			if (!slotDictionary.TryGetValue(nameHash, out source))
			{
				return null;
			}
			else
			{
				return new SlotData(source);
			}
		}

		public override SlotDataAsset[] GetAllSlotAssets()
		{
	#pragma warning disable 618
			return slotElementList;
	#pragma warning restore 618
		}
	}
}
