using UnityEngine;
using System.Collections.Generic;

namespace UMA
{
	/// <summary>
	/// Slot data contains mesh information and overlay references.
	/// </summary>
	[System.Serializable]
	public class SlotData : System.IEquatable<SlotData>, ISerializationCallbackReceiver
	{
		/// <summary>
		/// The asset contains the immutable portions of the slot.
		/// </summary>
		public SlotDataAsset asset;
		/// <summary>
		/// Adjusts the resolution of slot overlays.
		/// </summary>
		public float overlayScale = 1.0f;
		/// <summary>
		/// When serializing this recipe should this slot be skipped, useful for scene specific "additional slots"
		/// </summary>
		public bool dontSerialize;
		public string slotName { get { return asset.slotName; } }
		/// <summary>
		/// list of overlays used to texture the slot.
		/// </summary>
		private List<OverlayData> overlayList = new List<OverlayData>();

		/// <summary>
		/// Constructor for slot using the given asset.
		/// </summary>
		/// <param name="asset">Asset.</param>
		public SlotData(SlotDataAsset asset)
		{
			this.asset = asset;
			overlayScale = asset.overlayScale;
		}

		/// <summary>
		/// Deep copy of the SlotData.
		/// </summary>
		public SlotData Copy()
		{
			var res = new SlotData(asset);

			int overlayCount = overlayList.Count;
			res.overlayList = new List<OverlayData>(overlayCount);
			for (int i = 0; i < overlayCount; i++)
			{
				OverlayData overlay = overlayList[i];
				if (overlay != null)
				{
					res.overlayList.Add(overlay.Duplicate());
				}
			}

			return res;
		}

		public int GetTextureChannelCount(UMAGeneratorBase generator)
		{
			return asset.GetTextureChannelCount(generator);
		}

		public bool RemoveOverlay(params string[] names)
		{
			bool changed = false;
			foreach (var name in names)
			{
				for (int i = 0; i < overlayList.Count; i++)
				{
					if (overlayList[i].overlayName == name)
					{
						overlayList.RemoveAt(i);
						changed = true;
						break;
					}
				}
			}
			return changed;
		}

		public bool SetOverlayColor(Color32 color, params string[] names)
		{
			bool changed = false;
			foreach (var name in names)
			{
				foreach (var overlay in overlayList)
				{
					if (overlay.overlayName == name)
					{
						overlay.colorData.color = color;
						changed = true;
					}
				}
			}
			return changed;
		}

		public OverlayData GetOverlay(params string[] names)
		{
			foreach (var name in names)
			{
				foreach (var overlay in overlayList)
				{
					if (overlay.overlayName == name)
					{
						return overlay;
					}
				}
			}
			return null;
		}

		public void SetOverlay(int index, OverlayData overlay)
		{
			if (index >= overlayList.Count)
			{
				overlayList.Capacity = index + 1;
				while (index >= overlayList.Count)
				{
					overlayList.Add(null);
				}
			}
			overlayList[index] = overlay;
		}

		public OverlayData GetOverlay(int index)
		{
			if (index < 0 || index >= overlayList.Count)
				return null;
			return overlayList[index];
		}

		/// <summary>
		/// Attempts to find an equivalent overlay in the slot.
		/// </summary>
		/// <returns>The equivalent overlay (or null, if no equivalent).</returns>
		/// <param name="overlay">Overlay.</param>
		public OverlayData GetEquivalentOverlay(OverlayData overlay)
		{
			foreach (OverlayData overlay2 in overlayList)
			{
				if (OverlayData.Equivalent(overlay, overlay2))
				{
					return overlay2;
				}
			}

			return null;
		}
		/// <summary>
		/// Attempts to find an equivalent overlay in the slot, based on the overlay rect and its assets properties.
		/// </summary>
		/// <param name="overlay"></param>
		/// <returns></returns>
		public OverlayData GetEquivalentUsedOverlay(OverlayData overlay)
		{
			foreach (OverlayData overlay2 in overlayList)
			{
				if (OverlayData.EquivalentAssetAndUse(overlay, overlay2))
				{
					return overlay2;
				}
			}

			return null;
		}

		public int OverlayCount { get { return overlayList.Count; } }

		/// <summary>
		/// Sets the complete list of overlays.
		/// </summary>
		/// <param name="newOverlayList">The overlay list.</param>
		public void SetOverlayList(List<OverlayData> newOverlayList)
		{
            if (this.overlayList.Count == newOverlayList.Count)
            {
                // keep the list, and just set the overlays so that merging continues to work.
                for (int i = 0; i < this.overlayList.Count; i++)
                {
                    this.overlayList[i] = newOverlayList[i];
                }
            }
            else
            {
                this.overlayList = newOverlayList;
            }
		}

		/// <summary>
		/// Add an overlay to the slot.
		/// </summary>
		/// <param name="overlayData">Overlay.</param>
		public void AddOverlay(OverlayData overlayData)
		{
			if (overlayData)
				overlayList.Add(overlayData);
		}

		/// <summary>
		/// Gets the complete list of overlays.
		/// </summary>
		/// <returns>The overlay list.</returns>
		public List<OverlayData> GetOverlayList()
		{
			return overlayList;
		}

		internal bool Validate()
		{
			bool valid = true;
			if (asset.meshData != null)
			{
				if (asset.material == null)
				{
					Debug.LogError(string.Format("Slot '{0}' has a mesh but no material.", asset.slotName), asset);
					valid = false;
				}
				else
				{
					if (asset.material.material == null)
					{
						Debug.LogError(string.Format("Slot '{0}' has an umaMaterial without a material assigned.", asset.slotName), asset);
						valid = false;
					}
					else
					{
						for (int i = 0; i < asset.material.channels.Length; i++)
						{
							var channel = asset.material.channels[i];
							if (!asset.material.material.HasProperty(channel.materialPropertyName))
							{
								Debug.LogError(string.Format("Slot '{0}' Material Channel {1} refers to material property '{2}' but no such property exists.", asset.slotName, i, channel.materialPropertyName), asset);
								valid = false;
							}
						}
					}
				}
				for (int i = 0; i < overlayList.Count; i++)
				{
					var overlayData = overlayList[i];
					if (overlayData != null)
					{
						if (!overlayData.Validate(asset.material, (i == 0)))
						{
							valid = false;
							Debug.LogError(string.Format("Invalid Overlay '{0}' on Slot '{1}'.", overlayData.overlayName, asset.slotName));
						}
					}
				}
			}
			else
			{
				if (asset.material != null)
				{
					for (int i = 0; i < asset.material.channels.Length; i++)
					{
						var channel = asset.material.channels[i];
						if (!asset.material.material.HasProperty(channel.materialPropertyName))
						{
							Debug.LogError(string.Format("Slot '{0}' Material Channel {1} refers to material property '{2}' but no such property exists.", asset.slotName, i, channel.materialPropertyName), asset);
							valid = false;
						}
					}
				}

			}
			return valid;
		}

		public override string ToString()
		{
			return "SlotData: " + asset.slotName;
		}

		#region operator ==, != and similar HACKS, seriously.....

		public static implicit operator bool(SlotData obj)
		{
			return ((System.Object)obj) != null && obj.asset != null;
		}

		public bool Equals(SlotData other)
		{
			return (this == other);
		}
		public override bool Equals(object other)
		{
			return Equals(other as SlotData);
		}

		public static bool operator ==(SlotData slot, SlotData obj)
		{
			if (slot)
			{
				if (obj)
				{
					return System.Object.ReferenceEquals(slot, obj);
				}
				return false;
			}
			return !((bool)obj);
		}
		public static bool operator !=(SlotData slot, SlotData obj)
		{
			if (slot)
			{
				if (obj)
				{
					return !System.Object.ReferenceEquals(slot, obj);
				}
				return true;
			}
			return ((bool)obj);
		}
		public override int GetHashCode()
		{
			return base.GetHashCode();
		}
		#endregion

		#region ISerializationCallbackReceiver Members

		public void OnAfterDeserialize()
		{
			if (overlayList == null) overlayList = new List<OverlayData>();
		}

		public void OnBeforeSerialize()
		{
		}

		#endregion
	}
}
