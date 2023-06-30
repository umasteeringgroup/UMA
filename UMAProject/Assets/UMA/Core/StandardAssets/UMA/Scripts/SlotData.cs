using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;

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
        /// This instance specific tags. Loaded from the recipe, or from the asset at assignment time.
        /// </summary>
        public string[] tags;

        public string[] Races;

        public bool isBlendShapeSource
        {
            get { return !string.IsNullOrEmpty(blendShapeTargetSlot); }
        }
        // This only appears in recipes
        public string blendShapeTargetSlot;
#if UNITY_EDITOR
        public bool BlendshapeFoldout;
        public bool ClipPlaneFoldout;
        public bool isDeleted;
#endif

#if !NOSMOOSH

        /// <summary> Defines how the slot is smooshed </summary>
        public float overSmoosh = 0.01f;
        public float smooshDistance = 0.001f;
        public bool smooshInvertX;
        public bool smooshInvertY = true;
        public bool smooshInvertZ;
        public bool smooshInvertDist = true;
        public string smooshTargetTag;
        public string smooshableTag;
        /// <summary>
        /// If true, this slot will replace any existing slot that has replaceTag defined.
        /// </summary>
        public bool isSwapSlot;
        /// <summary>
        /// Find a slot with this tag, and replace it with this one.
        /// </summary>
        public string swapTag = "LongHair";
#endif

        // These are set by the combiner
        // so we can later determine where this is in the end SMR
        public int skinnedMeshRenderer;
        public int submeshIndex;
        public int vertexOffset;
        public Rect UVArea;
        public bool tempHidden;

        /// <summary>
        /// 
        /// </summary>
        public bool useAtlasOverlay
        {
            get
            {
                if (asset != null)
                {
                    return asset.useAtlasOverlay;
                }

                return false;
            }
        }

        public Vector2 ConvertToAtlasUV(Vector2 uvIn)
        {
            return new Vector2(UVArea.x + (UVArea.width * uvIn.x), UVArea.y + (UVArea.height * uvIn.y));
        }

        /// <summary>
        /// The Maximum LOD that this is displayed on.
        /// </summary>
        public int MaxLod
        {
            get
            {
                return asset.maxLOD;
            }
        }

        public UMAMaterial altMaterial;
        public UMAMaterial material
        {
            get
            {
                if (altMaterial != null)
                {
                    return altMaterial;
                }

                return asset.material;
            }
        }

        // Slots to copy blendshapes from as needed...
        public List<string> BlendshapeSlotNames = new List<string>();

        public bool Suppressed;

        /// <summary>
        /// When serializing this recipe should this slot be skipped, useful for scene specific "additional slots"
        /// </summary>
        public bool dontSerialize;
        public string slotName
        {
            get
            {
                if (asset != null)
                {
                    return asset.slotName;
                }

                return "";
            }
        }
        /// <summary>
        /// list of overlays used to texture the slot.
        /// </summary>
        private List<OverlayData> overlayList = new List<OverlayData>();

        //For MeshHide system, this can get added at runtime and is the filtered HideMask that the combiner uses.
        public BitArray[] meshHideMask;

        //Mutable version pulled off the immutable asset.  This is so we can modify it at runtime if needed.
        public UMARendererAsset rendererAsset;

        /// <summary>
        /// Constructor for slot using the given asset.
        /// </summary>
        /// <param name="asset">Asset.</param>
        public SlotData(SlotDataAsset asset)
        {
            this.asset = asset;
            if (asset)
            {
                tags = asset.tags;
                Races = asset.Races;
                overlayScale = asset.overlayScale;
                rendererAsset = asset.RendererAsset;
            }
            else
            {
                tags = new string[0];
                overlayScale = 1.0f;
            }
            if (Races == null)
            {
                Races = new string[0];
            }
        }

        public SlotData()
        {
            overlayScale = 1.0f;
            rendererAsset = null;

            overSmoosh = 0.01f;
            smooshDistance = 0.001f;
            smooshInvertY = true;
            smooshInvertDist = true;
        }


        public bool HasRace(string raceName)
        {
            // Null always matches.
            if (Races == null || Races.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < Races.Length; i++)
            {
                if (Races[i] == raceName)
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasTag(List<string> tagList)
        {
            if (tagList == null || tags == null)
            {
                return false;
            }
            // this feels like it would be better in a dictionary or hashtable
            // but I doubt there will be more than 1 tag, so we will go with this
            foreach (string s in tags)
            {
                if (tagList.Contains(s))
                {
                    return true;
                }
            }
            return false;
        }

        public bool HasTag(string[] tagList)
        {
            if (tagList == null || tags == null)
            {
                return false;
            }
            // this feels like it would be better in a dictionary or hashtable
            // but I doubt there will be more than 1 tag, so we will go with this
            foreach (string s in tags)
            {
                for (int i = 0; i < tagList.Length; i++)
                {

                    if (tagList[i] == s)
                    {
                        return true;
                    }
                }
            }
            return false;
        }


        public bool HasTag(string tag)
        {
            if (tags == null)
            {
                return false;
            }
            // this feels like it would be better in a dictionary or hashtable
            // but I doubt there will be more than 1 tag, so we will go with this
            foreach (string s in tags)
            {
                if (s == tag)
                {
                    return true;
                }
            }
            return false;
        }

        /*
				private Int64 overlayHash;

				public void CalculateOverlayHash()
				{
					overlayHash = 0;

					foreach(OverlayData od in overlayList)
					{
						var toverlayHash = od.asset.GetHashCode();
						var trecthash = od.rect.GetHashCode();
						var tcolorhash = od.colorData.GetHashCode();

						return ((overlay1.asset == overlay2.asset) &&
								(overlay1.rect == overlay2.rect) &&
								(overlay1.colorData == overlay2.colorData));
					}
				} 

		/// <summary>
		/// Property to return overlay hash so it is visible in debugger.
		/// </summary>
		public int OverlayHash
        {
            get
            {
				return (int) overlayHash;//GetOverlayList().GetHashCode();
            }
        }*/

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
            res.skinnedMeshRenderer = 0;
            res.submeshIndex = 0;
            res.vertexOffset = 0;
            res.UVArea.Set(0, 0, 1.0f, 1.0f);
            res.Races = Races;
            res.tags = tags;
            res.blendShapeTargetSlot = blendShapeTargetSlot;
            res.smooshDistance = smooshDistance;
            res.overlayScale = overlayScale;
            res.overSmoosh = overSmoosh;
            res.swapTag = swapTag;
            res.isSwapSlot = isSwapSlot;

            res.smooshInvertX = smooshInvertX;
            res.smooshInvertY = smooshInvertY;
            res.smooshInvertZ = smooshInvertZ;
            res.smooshInvertDist = smooshInvertDist;

            return res;
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
            {
                return null;
            }

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
            this.overlayList = newOverlayList;
        }

        /// <summary>
        /// Sets the complete list of overlays.
        /// Reuses the overlay list if it exists.
        /// </summary>
        /// <param name="newOverlayList">The overlay list.</param>
        public void UpdateOverlayList(List<OverlayData> newOverlayList)
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
            {
                overlayList.Add(overlayData);
            }
        }

        public void AddOverlayList(List<OverlayData> newOverlays)
        {
            if (overlayList == null)
            {
                overlayList = new List<OverlayData>();
            }
            if (newOverlays != null)
            {
                overlayList.AddRange(newOverlays);
            }
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

            if (tags == null)
            {
                tags = new string[0];
            }

            if (asset == null)
            {
                return true;
            }

            if (asset.meshData != null)
            {
                if (asset.material == null)
                {
                    asset.material = UMAAssetIndexer.Instance.GetAsset<UMAMaterial>(asset.materialName);
                }

                if (material == null)
                {
                    if (Debug.isDebugBuild)
                    {
                        Debug.LogError(string.Format("Slot '{0}' has a mesh but no material.", asset.slotName), asset);
                    }

                    valid = false;
                }
                else
                {
                    if (material.material == null)
                    {
                        if (Debug.isDebugBuild)
                        {
                            Debug.LogError(string.Format("Slot '{0}' has an umaMaterial without a material assigned.", asset.slotName), asset);
                        }

                        valid = false;
                    }
                    else
                    {
                        for (int i = 0; i < material.channels.Length; i++)
                        {
                            var channel = material.channels[i];
                            if (!channel.NonShaderTexture && !material.material.HasProperty(channel.materialPropertyName))
                            {
                                if (Debug.isDebugBuild)
                                {
                                    Debug.LogWarning(string.Format("Slot '{0}' Material Channel {1} on UMAMaterial {3} refers to material property '{2}' but no such property exists.", asset.slotName, i, channel.materialPropertyName, material.name), asset);
                                }
                                //valid = false;
                            }
                        }
                    }
                }
                for (int i = 0; i < overlayList.Count; i++)
                {
                    var overlayData = overlayList[i];
#if false
					if (overlayData != null)
					{
						if (!overlayData.Validate(material, (i == 0)))
						{
							valid = false;
							if (Debug.isDebugBuild)
								Debug.LogError(string.Format("Invalid Overlay '{0}' on Slot '{1}'.", overlayData.overlayName, asset.slotName));
						}
					}
#endif
                }
            }
            else
            {
                if (material != null)
                {
                    for (int i = 0; i < material.channels.Length; i++)
                    {
                        var channel = material.channels[i];
                        if (!channel.NonShaderTexture && !material.material.HasProperty(channel.materialPropertyName))
                        {
                            if (Debug.isDebugBuild)
                            {
                                Debug.LogWarning(string.Format("Slot '{0}' Material Channel {1} refers to material property '{2}' but no such property exists.", asset.slotName, i, channel.materialPropertyName), asset);
                            }
                            //valid = false;
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
            if (overlayList == null)
            {
                overlayList = new List<OverlayData>();
            }
        }

        public void OnBeforeSerialize()
        {
        }

        #endregion
    }
}
