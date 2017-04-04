using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;
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
					if (overlayList[i].asset.overlayName == name)
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
					if (overlay.asset.overlayName == name)
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
					if (overlay.asset.overlayName == name)
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
		/// <param name="overlayList">The overlay list.</param>
		public void SetOverlayList(List<OverlayData> overlayList)
		{
			this.overlayList = overlayList;
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
						if (overlayData.asset.material != asset.material)
						{
							if (!overlayData.asset.material.Equals(asset.material))
							{
                                
								Debug.LogError(string.Format("Slot '{0} ({2})' and Overlay '{1} ({3})' don't have the same UMA Material", asset.slotName, overlayData.asset.overlayName, asset.material.name, overlayData.asset.material.name));
								valid = false;
							}
						}

						if ((overlayData.asset.textureList == null) || (overlayData.asset.textureList.Length != asset.material.channels.Length))
						{
							Debug.LogError(string.Format("Overlay '{0}' doesn't have the right number of channels", overlayData.asset.overlayName));
							valid = false;
						}
						else
						{
							if (i == 0)
							{
								// all channels must be initialized by the base overlay
								for (int j = 0; j < asset.material.channels.Length; j++)
								{
									if ((overlayData.asset.textureList[j] == null) && (asset.material.channels[j].channelType != UMAMaterial.ChannelType.MaterialColor))
									{
										Debug.LogError(string.Format("Base Overlay '{0}' missing required texture in channel {1}", overlayData.asset.overlayName, j));
										valid = false;
									}
								}
							}
							else
							{
								// the following overlays just need a main texture
								int j = 0;
								if ((overlayData.asset.textureList[j] == null) && (asset.material.channels[j].channelType != UMAMaterial.ChannelType.MaterialColor))
								{
									Debug.LogError(string.Format("Overlay '{0}' missing a main texture", overlayData.asset.overlayName, j));
									valid = false;
								}
							}
						}

						if (overlayData.colorData.channelMask.Length < asset.material.channels.Length)
						{
							// Fixup colorData if moving from Legacy to PBR materials
							int oldsize = overlayData.colorData.channelMask.Length;

							System.Array.Resize(ref overlayData.colorData.channelMask, asset.material.channels.Length);
							System.Array.Resize(ref overlayData.colorData.channelAdditiveMask, asset.material.channels.Length);

							for (int j = oldsize; j > asset.material.channels.Length; j++)
							{
								overlayData.colorData.channelMask[j] = Color.white;
								overlayData.colorData.channelAdditiveMask[j] = Color.black;
							}


							Debug.LogWarning(string.Format("Overlay '{0}' missing required color data on Asset: " + asset.name + " Resizing and adding defaults", overlayData.asset.overlayName));
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
