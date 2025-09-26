using System;
using System.Collections.Generic;
using UnityEngine;
using UMA.CharacterSystem;

namespace UMA
{
    /// <summary>
    /// Attach this component to your avatar (via a slot) and wire its OnCharacterBegun handler
    /// to the SlotDataAsset CharacterBegun event. It subscribes to UMAData.OnAtlasUpdated and,
    /// when a matching overlay channel render target is ready, applies configured DecalRTStampAssets
    /// into the generated atlas using DecalRenderTexture.ApplyStampToUMA.
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

            [Tooltip("Stamp assets to apply for matching overlays (applied for each channel event emitted).")]
            public DecalRTStampAsset[] stamps;

            public bool Matches(OverlayData overlayData)
            {
                if (overlayData == null) return false;

                // Prefer direct asset match when available
                if (overlays != null && overlays.Count > 0)
                {
                    var odAsset = overlayData.asset;
                    for (int i = 0; i < overlays.Count; i++)
                    {
                        var a = overlays[i];
                        if (a == null) continue;

                        // Match by reference or by overlayName for robustness
                        if (ReferenceEquals(a, odAsset)) return true;
                        if (odAsset != null && !string.IsNullOrEmpty(odAsset.overlayName) &&
                            string.Equals(a.overlayName, odAsset.overlayName, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }

                // Fallback: match by configured names (case-sensitive to match UMA overlayName usage)
                if (overlayNames != null && overlayNames.Count > 0)
                {
                    string name = overlayData.overlayName;
                    if (!string.IsNullOrEmpty(name))
                    {
                        for (int i = 0; i < overlayNames.Count; i++)
                        {
                            var cfg = overlayNames[i];
                            if (!string.IsNullOrEmpty(cfg) && string.Equals(cfg, name, StringComparison.Ordinal))
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

        // Called from SlotDataAsset.CharacterBegun (UMADataEvent) in the slot that owns this script.
        public void OnCharacterBegun(UMAData umaData)
        {
            if (umaData == null) return;

            _avatar = _avatar ?? GetComponentInParent<DynamicCharacterAvatar>();
            if (_avatar == null)
            {
                if (enableDebug) Debug.LogWarning("[DecalRTStampSlot] No DynamicCharacterAvatar found in parents.");
            }

            // Avoid double subscription and handle avatar rebuilds
            if (_subscribed && _umaData != null)
            {
                _umaData.OnAtlasUpdated -= HandleAtlasUpdated;
                _subscribed = false;
            }

            _umaData = umaData;
            _umaData.OnAtlasUpdated += HandleAtlasUpdated;
            _subscribed = true;
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
                if (umaData == null || parms == null) return;
                if (parms.overlayData == null || parms.slotData == null || parms.renderTexture == null) return;

                // Locate the first set that matches ANY overlay in its list
                var set = FindStampSet(parms.overlayData);
                if (set == null || set.stamps == null || set.stamps.Length == 0) return;

                // Material property name maps to the UMAMaterial channel for this overlay
                string propName = parms.materialPropertyName;
                string slotName = parms.slotData.slotName;
                RenderTexture target = parms.renderTexture;

                for (int i = 0; i < set.stamps.Length; i++)
                {
                    var stamp = set.stamps[i];
                    if (stamp == null) continue;

                    bool ok = DecalRenderTexture.ApplyStampToUMA(
                        _avatar,
                        umaData,
                        stamp,
                        slotName,
                        propName,
                        target
                    );

                    if (enableDebug && !ok)
                    {
                        var ovName = parms.overlayData.overlayName;
                        Debug.LogWarning($"[DecalRTStampSlot] ApplyStampToUMA returned false. overlay='{ovName}', slot='{slotName}', prop='{propName}', stamp='{stamp.name}'.");
                    }
                }
            }
            catch (Exception ex)
            {
                if (enableDebug) Debug.LogException(ex, this);
            }
        }

        private OverlayStampSet FindStampSet(OverlayData overlayData)
        {
            if (overlayStamps == null) return null;
            for (int i = 0; i < overlayStamps.Count; i++)
            {
                var s = overlayStamps[i];
                if (s != null && s.Matches(overlayData))
                    return s;
            }
            return null;
        }
    }
}