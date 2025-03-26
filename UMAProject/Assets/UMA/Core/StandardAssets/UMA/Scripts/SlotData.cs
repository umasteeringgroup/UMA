using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;

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

        public List<MeshModifier.Modifier> meshModifiers = new List<MeshModifier.Modifier>();

        public bool hasAdjustments
        {
            get
            {
                return meshModifiers.Count > 0;
            }
        }

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
        public int expandAlongNormal = 0; // 8 digits of fixed point resolution. Multiply by 0.00001f to get the float value.

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
        public bool isDisabled = false;
        public bool   UVRemapped
        {
            get
            {
                return UVSet != 0;
            }
        }
        public int UVSet;    

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
				tags = asset.tags.Length > 0 ? (string[])asset.tags.Clone() : new string[0];
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
            expandAlongNormal = 0;
        }

        /// <summary>
        /// Gets the blendshape from the MeshData that matches the name
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public UMABlendShape GetBlendshape(string name)
        {
            foreach (UMABlendShape ubs in asset.meshData.blendShapes)
            {
                if (ubs.shapeName == name)
                {
                    return ubs;
                }

            }
            return null;
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
            for (int i = 0; i < tags.Length; i++)
            {
                string s = tags[i];
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
            for (int i1 = 0; i1 < tags.Length; i1++)
            {
                string s = tags[i1];
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

        public Dictionary<string, List<OverlayData>> GetOverlaysByTag(string tag)
        {
            Dictionary<string, List<OverlayData>> res = new Dictionary<string, List<OverlayData>>();
            foreach (OverlayData od in overlayList)
            {
                if (od.HasTag(tag))
                {
                    res.Add(tag, new List<OverlayData>());
                }
                res[tag].Add(od);
            }
            return res;
        }

        public bool HasTag(string tag)
        {
            if (tags == null)
            {
                return false;
            }
            // this feels like it would be better in a dictionary or hashtable
            // but I doubt there will be more than 1 tag, so we will go with this
            for (int i = 0; i < tags.Length; i++)
            {
                string s = tags[i];
                if (s == tag)
                {
                    return true;
                }
            }
            return false;
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
            res.skinnedMeshRenderer = 0;
            res.submeshIndex = 0;
            res.vertexOffset = 0;
            res.UVArea.Set(0, 0, 1.0f, 1.0f);
            res.Races = Races;
			res.tags = tags.Length > 0 ? (string[])tags.Clone() : new string[0]; 
            res.blendShapeTargetSlot = blendShapeTargetSlot;
            res.smooshDistance = smooshDistance;
            res.overlayScale = overlayScale;
            res.overSmoosh = overSmoosh;
            res.swapTag = swapTag;
            res.isSwapSlot = isSwapSlot;
            res.isDisabled = isDisabled;
            res.expandAlongNormal = expandAlongNormal;

            res.smooshInvertX = smooshInvertX;
            res.smooshInvertY = smooshInvertY;
            res.smooshInvertZ = smooshInvertZ;
            res.smooshInvertDist = smooshInvertDist;
            res.meshModifiers = new List<MeshModifier.Modifier>(meshModifiers);
            return res;
        }

        public void RemoveOverlayTags(List<string> HideTags)
        {
            // if we have only one, or no overlays, then we can skip this
            if (overlayList.Count < 2)
            {
                return;
            }
            // if we have no tags to hide, then we can skip this
            if (HideTags.Count < 1)
            {
                return;
            }

            //TODO: Research why recreating the overlayList results in index out of range errors
            //      during the merge process.
            //      This is a workaround to prevent the error. Unfortunately results in only one overlay being able to be removed at one time, which is not ideal but is far, far more likely to be the case than multiples needing to be removed from a single slot.
            // List<OverlayData> newOverlays = new List<OverlayData>(overlayList);

            for (int j = 0; j < HideTags.Count; j++)
            {
                string tag = HideTags[j];
                for (int i = 0; i < overlayList.Count; i++)
                {
                    if (overlayList[i].asset.tags.Contains<string>(tag))
                    {
                        overlayList.RemoveAt(i);
                        break;
                        //newOverlays.Remove(overlayList[i]);
                    }
                }
            }
#if DEBUG
            //if (newOverlays.Count < 1)
            //{
            //    Debug.LogWarning("SANITY: RemoveOverlayTags resulted in no overlays for slot " + slotName);
           // }
#endif
            //overlayList = newOverlays;
        }

        public bool RemoveOverlay(params string[] names)
        {
            bool changed = false;
            for (int j = 0; j < names.Length; j++)
            {
                string name = names[j];
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
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                for (int j = 0; j < overlayList.Count; j++)
                {
                    OverlayData overlay = overlayList[j];
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
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                for (int j = 0; j < overlayList.Count; j++)
                {
                    OverlayData overlay = overlayList[j];
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
            for (int i = 0; i < overlayList.Count; i++)
            {
                OverlayData overlay2 = overlayList[i];
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
            for (int i = 0; i < overlayList.Count; i++)
            {
                OverlayData overlay2 = overlayList[i];
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
            if (overlayList == null)
            {
                overlayList = new List<OverlayData>();
            }
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
                    if (asset.material == null)
                    {
                        Debug.LogError("Unable to load material " + asset.materialName + " for slot " + asset.slotName);
                    }
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
