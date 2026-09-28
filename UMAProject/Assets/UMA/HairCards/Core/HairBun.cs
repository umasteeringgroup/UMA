using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.HairCards
{
    public enum HairBunPart { WrappedVolume, CenterTuck, SurroundingBraid }

    [Serializable]
    public sealed class HairBunSettings
    {
        public string gatherHelperId;
        public Vector3 gatherOffset = new Vector3(0, .025f, 0);
        [Min(.001f)] public float radius = .037f;
        [Min(.001f)] public float tubeRadius = .029f;
        [Min(.001f)] public float height = .037f;
        [Range(0, 1)] public float tuck = .7f;
        [Range(-180, 180)] public float sweep = 20;
        [Range(0, .01f)] public float irregularity = .002f;
        [Range(4, 48)] public int sectors = 24;
        [Min(.001f)] public float braidRadius = .059f;
        public float braidHeight = -.02f;
        [Range(0, .15f)] public float braidOverlap = .025f;
        public void EnsureIntegrity()
        {
            radius = HairGatherSettings.Finite(radius, .001f, 1);
            tubeRadius = HairGatherSettings.Finite(tubeRadius, .001f, 1);
            height = HairGatherSettings.Finite(height, .001f, 1);
            tuck = HairGatherSettings.Finite(tuck, 0, 1); sweep = HairGatherSettings.Finite(sweep, -180, 180);
            irregularity = HairGatherSettings.Finite(irregularity, 0, .01f); sectors = Mathf.Clamp(sectors, 4, 48);
            braidRadius = HairGatherSettings.Finite(braidRadius, .001f, 1);
            braidHeight = HairGatherSettings.Finite(braidHeight, -1, 1); braidOverlap = HairGatherSettings.Finite(braidOverlap, 0, .15f);
            if (!float.IsFinite(gatherOffset.sqrMagnitude)) gatherOffset = Vector3.zero;
        }
    }

    public static class HairBunUtility
    {
        public static Matrix4x4 Matrix(HairGroomAsset groom, HairHelper helper)
        {
            var parent = groom.FindHelper(helper.bun.gatherHelperId);
            return parent?.type == HairHelperType.Gather
                ? parent.LocalToSource * Matrix4x4.TRS(helper.bun.gatherOffset, helper.rotation, helper.scale)
                : helper.LocalToSource;
        }
        public static Vector3 Point(HairBunSettings settings, float angle, float t, bool tuck)
        {
            float phi = Mathf.Lerp(-1.35f, tuck ? Mathf.Lerp(3.8f, 4.9f, settings.tuck) : 3.7f, t);
            float theta = angle + settings.sweep * Mathf.Deg2Rad * t;
            float minor = settings.tubeRadius * (tuck ? .74f : 1);
            float radial = settings.radius + minor * Mathf.Cos(phi);
            float y = Mathf.Sin(phi) * settings.height * (tuck ? .73f : 1) - (tuck ? .004f : 0);
            float ripple = settings.irregularity * (Mathf.Sin(angle * 7 + t * 2) + .4f * Mathf.Sin(angle * 13 + t * 3)) * Mathf.Sin(t * Mathf.PI);
            return new Vector3(Mathf.Sin(theta) * (radial + ripple), y, -Mathf.Cos(theta) * (radial + ripple));
        }
    }

    internal sealed class HairBunWorkspace
    {
        private readonly Dictionary<string, HairHelper> forms = new Dictionary<string, HairHelper>();
        internal void Clear() => forms.Clear();
        internal void Append(HairGroomAsset groom, HairHelper helper, HairBunPart part, List<HairHelper> output)
        {
            var settings = helper.bun;
            Matrix4x4 matrix = HairBunUtility.Matrix(groom, helper);
            int sectors = part == HairBunPart.SurroundingBraid ? 1 : part == HairBunPart.CenterTuck ? Mathf.Max(4, settings.sectors / 2) : settings.sectors;
            for (int i = 0; i < sectors; i++)
            {
                string id = helper.Id + ":" + part + ":" + i;
                if (!forms.TryGetValue(id, out var form)) { form = HairHelper.Derived(id); forms.Add(id, form); }
                form.name = helper.name + " " + part + " " + i;
                form.points.Clear(); form.gridColumns = 3; form.gridRows = 24; form.braidScale = helper.braidScale;
                form.type = part == HairBunPart.SurroundingBraid ? HairHelperType.BraidRail : HairHelperType.GuideGrid;
                if (part == HairBunPart.SurroundingBraid)
                {
                    for (int j = 0; j < 65; j++)
                    {
                        float a = (j / 64f * (1 + settings.braidOverlap) + .5f) * Mathf.PI * 2;
                        form.points.Add(matrix.MultiplyPoint3x4(new Vector3(Mathf.Sin(a) * settings.braidRadius, settings.braidHeight, -Mathf.Cos(a) * settings.braidRadius)));
                    }
                    form.rotation = matrix.rotation;
                }
                else for (int row = 0; row < form.gridRows; row++) for (int column = 0; column < form.gridColumns; column++)
                {
                    float angle = (i + column / (form.gridColumns - 1f)) * Mathf.PI * 2 / sectors;
                    form.points.Add(matrix.MultiplyPoint3x4(HairBunUtility.Point(settings, angle, row / (form.gridRows - 1f), part == HairBunPart.CenterTuck)));
                }
                output.Add(form);
            }
        }
    }
}
