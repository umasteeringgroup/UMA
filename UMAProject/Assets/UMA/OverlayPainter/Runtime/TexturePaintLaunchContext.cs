using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintLaunchKind
    {
        Avatar,
        StandaloneSlot
    }

    public enum TexturePaintStandaloneSourceMode
    {
        UMAMaterial,
        OverlayDataAsset
    }

    [Serializable]
    public sealed class TexturePaintStandaloneMemberContext
    {
        public SlotDataAsset slot;
        public OverlayDataAsset overlay;
        public string slotGuid;
        public string overlayGuid;
        public string sourceFingerprint;
        public int tileNumber;
    }

    /// <summary>
    /// Serializable description of a slot-first Overlay Painter session. Asset references
    /// make an in-flight stage resilient to domain reload; GUIDs and fingerprints make saved and
    /// recovery documents portable and allow their original inputs to be checked on reopen.
    /// </summary>
    [Serializable]
    public sealed class TexturePaintLaunchContext
    {
        public TexturePaintLaunchKind kind = TexturePaintLaunchKind.Avatar;
        public TexturePaintStandaloneSourceMode sourceMode;
        public SlotDataAsset selectedSlot;
        public UMAMaterial umaMaterial;
        public string selectedSlotGuid;
        public string umaMaterialGuid;
        public string udimGroupId;
        public int resolution = 2048;
        public int standaloneMeshTransformVersion = 2;
        public bool fixupRotations;
        public Vector3 slotRotationEuler = Vector3.zero;
        public List<TexturePaintStandaloneMemberContext> members = new List<TexturePaintStandaloneMemberContext>();

        public bool IsStandalone => kind == TexturePaintLaunchKind.StandaloneSlot;

        public TexturePaintLaunchContext Clone()
        {
            TexturePaintLaunchContext copy = (TexturePaintLaunchContext)MemberwiseClone();
            copy.members = new List<TexturePaintStandaloneMemberContext>();
            for (int i = 0; members != null && i < members.Count; i++)
            {
                TexturePaintStandaloneMemberContext member = members[i];
                if (member == null) { copy.members.Add(null); continue; }
                copy.members.Add(new TexturePaintStandaloneMemberContext
                {
                    slot = member.slot,
                    overlay = member.overlay,
                    slotGuid = member.slotGuid,
                    overlayGuid = member.overlayGuid,
                    sourceFingerprint = member.sourceFingerprint,
                    tileNumber = member.tileNumber
                });
            }
            return copy;
        }
    }
}
