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

        // Called from SlotDataAsset.CharacterBegun (UMADataEvent) in the slot that owns this script.
        public void OnCharacterBegun(UMAData umaData)
        {
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

        private static void DebugSkip(string reason, UnityEngine.Object ctx)
        {
            if (Debug.isDebugBuild)
            {
                Debug.Log("[DecalRTStampSlot][Skip] " + reason, ctx);
            }
        }

        private void HandleAtlasUpdated(UMAData umaData, TextureEventParms parms)
        {
            try
            {
                if (umaData == null) { DebugSkip("UMAData null in HandleAtlasUpdated", this); return; }
                if (parms == null) { DebugSkip("TextureEventParms null", this); return; }
                if (parms.overlayData == null) { DebugSkip("Event overlayData null (nothing to match)", this); return; }

                if (_avatar == null)
                    _avatar = umaData as DynamicCharacterAvatar;
                if (_avatar == null) { DebugSkip("No DynamicCharacterAvatar found (cannot stamp)", this); return; }

                if (overlayStamps == null || overlayStamps.Count == 0) { DebugSkip("overlayStamps list empty", this); return; }

                bool anyAttempted = false;
                bool anyApplied = false;

                // Iterate ALL sets; do not early exit so multiple sets can react to the same overlay
                for (int si = 0; si < overlayStamps.Count; si++)
                {
                    var set = overlayStamps[si];
                    if (set == null)
                    {
                        DebugSkip($"OverlayStampSet index {si} is null", this);
                        continue;
                    }
                    if (set.stamps == null || set.stamps.Length == 0)
                    {
                        DebugSkip($"OverlayStampSet '{set.name}' has no stamps", this);
                        continue;
                    }
                    if (!set.Matches(parms.overlayData))
                    {
                        DebugSkip($"OverlayStampSet '{set.name}' did not match overlay '{parms.overlayData.overlayName}'", this);
                        continue;
                    }

                    // Matching set
                    for (int st = 0; st < set.stamps.Length; st++)
                    {
                        anyAttempted = true;
                        var stamp = set.stamps[st];
                        if (stamp == null)
                        {
                            DebugSkip($"Stamp index {st} in set '{set.name}' is null", this);
                            continue;
                        }
                        bool ok = DecalRenderTexture.ApplySlotStamps(_avatar, umaData, stamp, parms.materialPropertyName, parms.renderTexture);
                        if (!ok)
                        {
                            DebugSkip($"ApplyStampAsset returned false for set '{set.name}' stamp '{stamp.name}'", this);
                        }
                        else
                        {
                            anyApplied = true;
                        }
                    }
                }

                if (!anyAttempted)
                {
                    DebugSkip("No stamps attempted (no matching sets)", this);
                }
                else if (!anyApplied)
                {
                    DebugSkip("Stamps attempted but none applied successfully", this);
                }
            }
            catch (Exception ex)
            {
                if (enableDebug || Debug.isDebugBuild) Debug.LogException(ex, this);
            }
        }

        // Legacy helper kept for backward compatibility (no longer used in new logic)
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