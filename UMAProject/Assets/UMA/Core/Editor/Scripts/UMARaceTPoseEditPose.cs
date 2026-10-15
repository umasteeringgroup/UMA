using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.PoseTools
{
    // Marker for the existing bone-pose inspector: never rebuild or persist this preview.
    public sealed class UMARaceTPoseEditPose : UMABonePose
    {
        [NonSerialized] public UMAData preview;
        [NonSerialized] public Dictionary<string, Vector3> baselineScales;

        public void RefreshPreview()
        {
            if (preview == null || preview.skeleton == null) return;
            preview.skeleton.ResetAll();
            ApplyPose(preview.skeleton, 1f);
        }

        public Vector3 ScaleDelta(string bone, Vector3 absoluteScale)
        {
            if (baselineScales == null || !baselineScales.TryGetValue(bone, out var baseline)) return absoluteScale;
            return new Vector3(Divide(absoluteScale.x, baseline.x),
                Divide(absoluteScale.y, baseline.y), Divide(absoluteScale.z, baseline.z));
        }

        private static float Divide(float value, float baseline) => Mathf.Abs(baseline) > 1e-8f ? value / baseline : 1f;
    }
}
