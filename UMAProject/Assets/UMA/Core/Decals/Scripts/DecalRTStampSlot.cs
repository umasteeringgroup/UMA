using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;
using UnityEditor;

namespace UMA
{
    /// <summary>
    /// Attach this component to your avatar (via a slot) and wire its OnCharacterBegun handler
    /// to the SlotDataAsset CharacterBegun event. It subscribes to UMAData.OnAtlasUpdated and,
    /// when a matching overlay channel render target is ready, applies configured DecalRTStampAssets
    /// into the generated atlas using DecalRenderTexture.ApplyStampAsset.
    /// </summary>
    [DisallowMultipleComponent]
    public class DecalRTStampSlot : MonoBehaviour
    {
        [Serializable]
        public class OverlayStampSet
        {
            [Tooltip("The name of this overlay stamp set (for your reference).")]
            public string name;
            [Tooltip("Overlays that will trigger this stamp set. Match occurs if ANY of these overlays (by asset) is present in the AtlasUpdated event.")]
            public List<OverlayDataAsset> overlays = new List<OverlayDataAsset>();

            [Tooltip("Optional overlay names to trigger this stamp set (legacy/fallback). Match occurs if ANY name equals OverlayData.overlayName.")]
            public List<string> overlayNames = new List<string>();

            [Tooltip("Stamp assets to apply for matching overlays (applied for each matching atlas update event).")]
            public DecalRTStampAsset[] stamps;

            public bool Matches(OverlayData overlayData)
            {
                if (overlayData == null) return false;

                if (overlays != null && overlays.Count > 0)
                {
                    var odAsset = overlayData.asset;
                    for (int i = 0; i < overlays.Count; i++)
                    {
                        var a = overlays[i];
                        if (a == null) continue;
                        if (ReferenceEquals(a, odAsset)) return true;
                        if (odAsset != null && !string.IsNullOrEmpty(odAsset.overlayName) &&
                            string.Equals(a.overlayName, odAsset.overlayName, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }

                if (overlayNames != null && overlayNames.Count > 0)
                {
                    string oname = overlayData.overlayName;
                    if (!string.IsNullOrEmpty(oname))
                    {
                        for (int i = 0; i < overlayNames.Count; i++)
                        {
                            var cfg = overlayNames[i];
                            if (!string.IsNullOrEmpty(cfg) && string.Equals(cfg, oname, StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
        }

        [Tooltip("Map overlay sets to stamp assets. If ANY overlay in a set matches, the set's stamps will be applied.")]
        public List<OverlayStampSet> overlayStamps = new List<OverlayStampSet>();

        [Tooltip("Log warnings/errors during stamping.")]
        public bool enableDebug = false;

        private UMAData _umaData;
        private DynamicCharacterAvatar _avatar;
        private bool _subscribed;

        // Dilation material cache for final-RT bleed (to kill seams)
        private static Material _dilateMat;
        [Header("Dilation")]
        [Tooltip("If true, dilate RGB colors across padding regardless of alpha (fixes seams across opaque islands).")]
        public bool rgbOnlyDilation = true;

		private Dictionary<string, int> alreadyProcessed = new Dictionary<string, int>();
		// Called from SlotDataAsset.CharacterBegun (UMADataEvent) in the slot that owns this script.
		public void OnCharacterBegun(UMAData umaData)
        {
			Debug.Log("[DecalRTStampSlot] OnCharacterBegun called.");
			if (umaData == null) return;

            _avatar = _avatar ?? GetComponentInParent<DynamicCharacterAvatar>();
            if (_avatar == null && enableDebug)
            {
                Debug.LogWarning("[DecalRTStampSlot] No DynamicCharacterAvatar found in parents.");
            }

            // Avoid double subscription and handle avatar rebuilds
            if (_subscribed && _umaData != null)
            {
                _umaData.OnAtlasUpdated -= HandleAtlasUpdated;
                _subscribed = false;
            }
			alreadyProcessed.Clear();

            _umaData = umaData;
            _umaData.OnAtlasUpdated += HandleAtlasUpdated;
            _subscribed = true;
        }

		bool AlreadProcessed(string overlayName, string propertyName, int frame) {
			string key = overlayName + "|" + propertyName;
			if (alreadyProcessed.ContainsKey(key)) {
				if(alreadyProcessed[key] == frame) {
					return false;
				} else {
					alreadyProcessed[key] = frame;
					return false;
				}
			} else {
				alreadyProcessed[key] = frame;
				return false;
			}
		}



		private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Unsubscribe()
        {
            if (_subscribed && _umaData != null)
            {
                _umaData.OnAtlasUpdated -= HandleAtlasUpdated;
            }
            _subscribed = false;
            _umaData = null;
        }


        private void HandleAtlasUpdated(UMAData umaData, TextureEventParms parms)
        {
            try
            {
				if (umaData == null || parms == null || parms.overlayData == null) return;

				if(AlreadProcessed(parms.overlayName, parms.materialPropertyName, Time.frameCount)) {
					//Debug.Log("[DecalRTStampSlot] Already processed this overlay/property this frame, skipping.");
					return;
				}

					if (_avatar == null) _avatar = umaData as DynamicCharacterAvatar;
                if (_avatar == null) return;
                if (overlayStamps == null || overlayStamps.Count == 0) return;


                bool anyApplied = false;
                int maxBleedPixels = 0;
                int currenttime = Time.frameCount;

				//DecalRenderTexture.SaveRenderTexturePNG(parms.renderTexture, null, parms.overlayName, currenttime, "Before Iterating Stamps");

			// Iterate ALL sets; do not early exit so multiple sets can react to the same overlay
			for(int si = 0; si < overlayStamps.Count; si++)
                {
                    var set = overlayStamps[si];
                    if (set == null || set.stamps == null || set.stamps.Length == 0) continue;
                    if (!set.Matches(parms.overlayData)) continue;

                    // Matching set
                    for (int st = 0; st < set.stamps.Length; st++)
                    {
                        var stamp = set.stamps[st];
                        if (stamp == null) continue;
                          
						// Debug.Log("DecalRT: Calling ApplySlotStamps");
                        bool ok = DecalRenderTexture.ApplySlotStamps(_avatar, umaData, stamp, parms.materialPropertyName, parms.renderTexture, parms.overlayData.asset.nameHash);
                        if (ok)
                        {
                            anyApplied = true;
                            if (stamp.bleedPixels > maxBleedPixels) maxBleedPixels = stamp.bleedPixels;
                        }
                    }
                }

				//DecalRenderTexture.SaveRenderTexturePNG(parms.renderTexture, null, parms.overlayName, currenttime, "After Iterating Stamps");


				// If we applied any stamps to this final RT, run a dilation pass that expands color into transparent padding
				if(anyApplied && parms.renderTexture != null)
                {
                    // Guard against uncreated or invalid RT
                    if (!parms.renderTexture.IsCreated())
                    {
                        if (enableDebug || Debug.isDebugBuild)
                        {
                            Debug.LogWarning("[DecalRTStampSlot] RenderTexture not created, skipping dilation.");
                        }
                    }
                    else
                    {
                    // Use the largest bleed requested by any applied stamp. Clamp to shader range [1..16].
                    int bleed = Mathf.Clamp(maxBleedPixels <= 0 ? 2 : maxBleedPixels, 1, 64); // allow multiple rounds if >16
						//DecalRenderTexture.SaveRenderTexturePNG(parms.renderTexture, null, parms.overlayName, currenttime, "Before final dilation");

						//RunFinalDilation(parms.renderTexture, bleed, rgbOnlyDilation);
						//DecalRenderTexture.SaveRenderTexturePNG(parms.renderTexture, null, parms.overlayName, currenttime, "After final dilation");

					}
				}
            }
            catch (Exception ex)
            {
                if (enableDebug || Debug.isDebugBuild) Debug.LogException(ex, this);
            }
        }

        private static void EnsureDilateMat()
        {
            var shader = Shader.Find("Hidden/UMA/DecalRTDilate");
            if (shader == null)
            {
                Debug.LogWarning("[DecalRTStampSlot] Dilation shader 'Hidden/UMA/DecalRTDilate' not found.");
                return;
            }
            if (_dilateMat == null || _dilateMat.shader != shader)
            {
                if (_dilateMat != null)
                {
                    if (Application.isPlaying) Destroy(_dilateMat); else DestroyImmediate(_dilateMat);
                }
                _dilateMat = new Material(shader) { name = "UMA_DecalRT_DilateMat" };
                _dilateMat.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        private static void RunFinalDilation(RenderTexture rt, int bleedPixels, bool rgbOnly)
        {
            if (rt == null || bleedPixels <= 0) return;
            if (!rt.IsCreated()) return;
            EnsureDilateMat();
            if (_dilateMat == null) return;


            _dilateMat.SetFloat("_PreserveAlpha", rgbOnly ? 1.0f : 0.0f);   // in RGB-only we keep alpha
            _dilateMat.SetFloat("_MinNeighborAlpha", rgbOnly ? 0.0f : 0.0f);// allow any neighbor
            _dilateMat.SetFloat("_RGBOnly", rgbOnly ? 1.0f : 0.0f);
            // Ensure the stamp sampling LOD is locked during the final pass to avoid introducing fresh mip transitions
            if (_dilateMat.HasProperty("_UseFixedLOD"))
            {
                _dilateMat.SetFloat("_UseFixedLOD", 1.0f);
                _dilateMat.SetFloat("_FixedLOD", 0.0f);
            }

            //Debug.Log($"[DecalRTStampSlot] Running final dilation pass, bleedPixels={bleedPixels}, rgbOnly={rgbOnly}, _MinNeighborAlpha={_dilateMat.GetFloat("_MinNeighborAlpha")}");


            int remaining = Mathf.Clamp(bleedPixels, 1, 256);
            // Create a single temporary RT (no depth, no mips, no MSAA) reused for all passes
            var tmp = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            try
            {
                while (remaining > 0)
                {
                    int step = Mathf.Min(remaining, 16);
                    _dilateMat.SetFloat("_Radius", step);
                    Graphics.Blit(rt, tmp);
                    Graphics.Blit(tmp, rt, _dilateMat);
                    remaining -= step;
                }
            }
            finally
            {
                RenderTexture.ReleaseTemporary(tmp);
            }
        }

        internal void ClearAllStamps()
        {
            // clear the stamps
            overlayStamps.Clear();
        }
    }
}