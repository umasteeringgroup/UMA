using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.TexturePaint
{
    public enum TexturePaintAnatomicalRegion { Knees, Elbows, Armpits, WristCuffs, AnkleCuffs, Collar, Lips, Shoulders, Waist, Seat }
    public enum TexturePaintRegionSide { Both, Left, Right }

    /// <summary>All lengths are relative to the resolved bone segment, independent of UV layout.</summary>
    [Serializable]
    public sealed class TexturePaintAnatomicalSettings
    {
        public TexturePaintAnatomicalRegion region;
        public TexturePaintRegionSide side;
        public Vector3 size = Vector3.one;
        public Vector3 offset;
        public float rotation;
        [Range(0, 1)] public float feather = .25f;
        [Range(1, 360)] public float jointArc = 120;
        // Optional exact bone names for rigs that do not use humanoid/UMA naming.
        public string leftAnchor, leftParent, leftChild, rightAnchor, rightParent, rightChild;
        public TexturePaintAnatomicalSettings Clone() => (TexturePaintAnatomicalSettings)MemberwiseClone();
        public static bool IsPaired(TexturePaintAnatomicalRegion region) =>
            region <= TexturePaintAnatomicalRegion.AnkleCuffs || region == TexturePaintAnatomicalRegion.Shoulders;
        public void Normalize()
        {
            size = new Vector3(Positive(size.x), Positive(size.y), Positive(size.z));
            offset = new Vector3(Finite(offset.x, 0), Finite(offset.y, 0), Finite(offset.z, 0));
            rotation = Finite(rotation, 0); feather = Mathf.Clamp(Finite(feather, .25f), .001f, 1);
            jointArc = Mathf.Clamp(Finite(jointArc, 120), 1, 360);
        }
        private static float Positive(float value) => Mathf.Clamp(Finite(value, 1), .01f, 20);
        private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
    }

    [Serializable]
    public sealed class TexturePaintAnatomicalBone
    {
        public string name;
        public HumanBodyBones humanoid = HumanBodyBones.LastBone;
        public Vector3 position;
        // Reference axes transported from the rest pose into the baked pose.
        public Vector3 forward = Vector3.forward, up = Vector3.up;
    }

    /// <summary>Snapshot in reconstructed mesh coordinates; never retains live avatar transforms.</summary>
    [Serializable]
    public sealed class TexturePaintAnatomy
    {
        public List<TexturePaintAnatomicalBone> bones = new List<TexturePaintAnatomicalBone>();

        public static TexturePaintAnatomy Capture(Transform root, SkinnedMeshRenderer renderer)
        {
            var result = new TexturePaintAnatomy();
            var animator = root.GetComponentInChildren<Animator>();
            var human = new Dictionary<Transform, HumanBodyBones>();
            if (animator != null && animator.isHuman)
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var bone = animator.GetBoneTransform((HumanBodyBones)i);
                    if (bone != null) human[bone] = (HumanBodyBones)i;
                }
            var bind = new Dictionary<Transform, Quaternion>();
            var referenceRig = new TexturePaintAnatomy();
            var skinBones = renderer.bones;
            var poses = renderer.sharedMesh.bindposes;
            Matrix4x4 toRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int i = 0; i < skinBones.Length && i < poses.Length; i++)
                if (skinBones[i] != null)
                {
                    Matrix4x4 restMatrix = toRoot * poses[i].inverse;
                    bind[skinBones[i]] = restMatrix.rotation;
                    referenceRig.bones.Add(new TexturePaintAnatomicalBone
                    {
                        name = skinBones[i].name, position = restMatrix.MultiplyPoint3x4(Vector3.zero),
                        humanoid = human.TryGetValue(skinBones[i], out var id) ? id : HumanBodyBones.LastBone
                    });
                }
            referenceRig.Axes(out Vector3 referenceForward, out Vector3 referenceUp);
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var bone in transforms)
            {
                Quaternion rotation = Quaternion.Inverse(root.rotation) * bone.rotation;
                Quaternion transport = bind.TryGetValue(bone, out var rest) ? rotation * Quaternion.Inverse(rest) : Quaternion.identity;
                result.bones.Add(new TexturePaintAnatomicalBone
                {
                    name = bone.name, position = root.InverseTransformPoint(bone.position),
                    humanoid = human.TryGetValue(bone, out var id) ? id : HumanBodyBones.LastBone,
                    forward = transport * referenceForward, up = transport * referenceUp
                });
            }
            result.Axes(out Vector3 posedForward, out Vector3 posedUp);
            for (int i = 0; i < result.bones.Count; i++)
                if (!bind.ContainsKey(transforms[i]))
                { result.bones[i].forward = posedForward; result.bones[i].up = posedUp; }
            return result;
        }

        /// <param name="previewFromRoot">Additional preview rotation only. Bone hierarchy is already in canonical root space.</param>
        public static TexturePaintAnatomy CaptureSlot(UMAMeshData data, Matrix4x4 previewFromRoot)
        {
            var result = new TexturePaintAnatomy();
            if (data.umaBones == null) return result;
            var byHash = new Dictionary<int, UMATransform>();
            foreach (var bone in data.umaBones) if (bone != null) byHash[bone.hash] = bone;
            int rootHash = data.rootBoneHash != 0 ? data.rootBoneHash : UMAUtils.StringToHash(data.RootBoneName ?? string.Empty);
            Matrix4x4 Resolve(UMATransform bone, HashSet<int> visiting)
            {
                if (rootHash != 0 && bone.hash == rootHash) return Matrix4x4.identity;
                if (!visiting.Add(bone.hash)) return Matrix4x4.identity;
                var local = Matrix4x4.TRS(bone.position, bone.rotation, bone.scale);
                var matrix = bone.parent != bone.hash && byHash.TryGetValue(bone.parent, out var parent) ? Resolve(parent, visiting) * local : local;
                visiting.Remove(bone.hash); return matrix;
            }
            foreach (var bone in data.umaBones)
                if (bone != null) result.bones.Add(new TexturePaintAnatomicalBone
                {
                    name = bone.name, position = (previewFromRoot * Resolve(bone, new HashSet<int>())).MultiplyPoint3x4(Vector3.zero),
                    forward = previewFromRoot.MultiplyVector(Vector3.forward).normalized,
                    up = previewFromRoot.MultiplyVector(Vector3.up).normalized
                });
            result.Axes(out Vector3 forward, out Vector3 up);
            foreach (var bone in result.bones) { bone.forward = forward; bone.up = up; }
            return result;
        }

        private void Axes(out Vector3 forward, out Vector3 up)
        {
            var hips = Find(HumanBodyBones.Hips); var head = Find(HumanBodyBones.Head);
            head ??= Find(HumanBodyBones.Neck) ?? Find(HumanBodyBones.Chest) ?? Find(HumanBodyBones.Spine);
            var left = Find(HumanBodyBones.LeftUpperLeg); var right = Find(HumanBodyBones.RightUpperLeg);
            var kneeLeft = Find(HumanBodyBones.LeftLowerLeg); var kneeRight = Find(HumanBodyBones.RightLowerLeg);
            up = hips != null && head != null ? (head.position - hips.position).normalized :
                hips != null && kneeLeft != null && kneeRight != null ? (hips.position - (kneeLeft.position + kneeRight.position) * .5f).normalized : Vector3.up;
            left ??= Find(HumanBodyBones.LeftUpperArm); right ??= Find(HumanBodyBones.RightUpperArm);
            var lateral = left != null && right != null ? (right.position - left.position).normalized : Vector3.right;
            forward = Vector3.Cross(lateral, up).normalized;
            if (forward.sqrMagnitude < .5f) forward = Vector3.forward;
            up = Vector3.Cross(forward, lateral).normalized;
            if (up.sqrMagnitude < .5f) up = Vector3.up;
        }

        public TexturePaintAnatomicalBone Find(HumanBodyBones id, string exact = null)
        {
            if (!string.IsNullOrWhiteSpace(exact))
                return bones.Find(b => b != null && string.Equals(b.name, exact.Trim(), StringComparison.OrdinalIgnoreCase));
            if (id == HumanBodyBones.LastBone) return null;
            var human = bones.Find(b => b != null && b.humanoid == id);
            if (human != null) return human;
            string name = id.ToString();
            string alias = name.Replace("UpperLeg", "Thigh").Replace("LowerLeg", "Calf")
                .Replace("LowerArm", "ForeArm");
            string legacy = name.Replace("UpperLeg", "UpLeg").Replace("LowerLeg", "Leg")
                .Replace("UpperArm", "Arm").Replace("LowerArm", "ForeArm");
            return bones.Find(b => b != null && (Clean(b.name) == Clean(name) || Clean(b.name) == Clean(alias) || Clean(b.name) == Clean(legacy)));
        }
        private static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            int colon = name.LastIndexOf(':'); if (colon >= 0) name = name.Substring(colon + 1);
            return name.Replace("_", "").Replace(" ", "").Replace("-", "").ToLowerInvariant();
        }
    }

    public struct TexturePaintRegionEnvelope
    {
        public Vector3 center, axis, outward, radii;
        public bool cylindrical;
        public float arc, feather;
        public float Evaluate(Vector3 point)
        {
            Vector3 side = Vector3.Cross(axis, outward);
            Vector3 delta = point - center;
            float y = Vector3.Dot(delta, axis), x = Vector3.Dot(delta, side), z = Vector3.Dot(delta, outward);
            if (Mathf.Abs(y) >= radii.y) return 0;
            float radialSquared = x * x / (radii.x * radii.x) + z * z / (radii.z * radii.z);
            if (radialSquared >= 1) return 0;
            float radial = Mathf.Sqrt(radialSquared);
            float distance = cylindrical ? Mathf.Max(Mathf.Abs(y) / radii.y, radial)
                : Mathf.Sqrt(radial * radial + y * y / (radii.y * radii.y));
            if (distance >= 1) return 0;
            float coverage = distance <= 1 - feather ? 1 : 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1 - feather, 1, distance));
            if (arc < 359.99f)
            {
                float angle = Mathf.Abs(Mathf.Atan2(x, z)) * Mathf.Rad2Deg;
                float half = arc * .5f;
                coverage *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(half * (1 - feather), half, angle));
            }
            return coverage;
        }
        public Bounds Bounds
        {
            get
            {
                Vector3 side = Vector3.Cross(axis, outward);
                Vector3 extent = Abs(side) * radii.x + Abs(axis) * radii.y + Abs(outward) * radii.z;
                return new Bounds(center, extent * 2);
            }
        }
        private static Vector3 Abs(Vector3 value) => new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }

    public static class TexturePaintAnatomicalMask
    {
        public static List<TexturePaintRegionEnvelope> Resolve(TexturePaintAnatomy rig,
            TexturePaintAnatomicalSettings settings, out string diagnostic)
        {
            var result = new List<TexturePaintRegionEnvelope>();
            diagnostic = null;
            if (rig?.bones == null || rig.bones.Count == 0) { diagnostic = "No skeleton is available for this surface."; return result; }
            var s = (settings ?? new TexturePaintAnatomicalSettings()).Clone(); s.Normalize();
            if (!Enum.IsDefined(typeof(TexturePaintAnatomicalRegion), s.region) || !Enum.IsDefined(typeof(TexturePaintRegionSide), s.side))
            { diagnostic = "Unknown anatomical region or side. Choose a supported region and side."; return result; }
            bool paired = TexturePaintAnatomicalSettings.IsPaired(s.region);
            int expected = paired && s.side == TexturePaintRegionSide.Both ? 2 : 1;
            if (!paired) Add(false);
            else
            {
                if (s.side != TexturePaintRegionSide.Right) Add(false);
                if (s.side != TexturePaintRegionSide.Left) Add(true);
            }
            if (result.Count < expected) diagnostic = s.region + ": required bones are missing or have zero-length segments. Check exact bone-name overrides or use UMA/humanoid bone names. Unresolved sides remain black.";
            else if (s.region == TexturePaintAnatomicalRegion.Lips && !HasLipBones(rig))
                diagnostic = "No lip bones were found. This is a head-relative estimate; tune the envelope offset and size for this face.";
            return result;

            void Add(bool right)
            {
                HumanBodyBones anchor, parent, child;
                float radius, length; bool cylinder = true; float arc = 360;
                switch (s.region)
                {
                    case TexturePaintAnatomicalRegion.Knees:
                        anchor = right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg;
                        parent = right ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg;
                        child = right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot;
                        radius = .38f; length = .28f; arc = s.jointArc; cylinder = false; break;
                    case TexturePaintAnatomicalRegion.Elbows:
                        anchor = right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
                        parent = right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
                        child = right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
                        radius = .33f; length = .28f; arc = s.jointArc; cylinder = false; break;
                    case TexturePaintAnatomicalRegion.Armpits:
                    case TexturePaintAnatomicalRegion.Shoulders:
                        anchor = right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm;
                        parent = HumanBodyBones.Chest;
                        child = right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
                        radius = s.region == TexturePaintAnatomicalRegion.Armpits ? .32f : .48f;
                        length = s.region == TexturePaintAnatomicalRegion.Armpits ? .28f : .40f;
                        cylinder = false; break;
                    case TexturePaintAnatomicalRegion.WristCuffs:
                        anchor = right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
                        parent = right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
                        child = HumanBodyBones.LastBone; radius = .32f; length = .17f; break;
                    case TexturePaintAnatomicalRegion.AnkleCuffs:
                        anchor = right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot;
                        parent = right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg;
                        child = HumanBodyBones.LastBone; radius = .36f; length = .17f; break;
                    case TexturePaintAnatomicalRegion.Collar:
                        anchor = HumanBodyBones.Neck; parent = HumanBodyBones.Chest; child = HumanBodyBones.Head;
                        radius = .9f; length = .25f; break;
                    case TexturePaintAnatomicalRegion.Lips:
                        anchor = HumanBodyBones.Head; parent = HumanBodyBones.Neck; child = HumanBodyBones.LastBone;
                        radius = .65f; length = .16f; cylinder = false; break;
                    default:
                        anchor = HumanBodyBones.Hips; parent = HumanBodyBones.Spine; child = HumanBodyBones.LastBone;
                        radius = 1.8f; length = .28f; cylinder = s.region == TexturePaintAnatomicalRegion.Waist; break;
                }
                var a = rig.Find(anchor, right ? s.rightAnchor : s.leftAnchor);
                var p = rig.Find(parent, right ? s.rightParent : s.leftParent);
                if (p == null && parent == HumanBodyBones.Chest && string.IsNullOrWhiteSpace(right ? s.rightParent : s.leftParent))
                    p = rig.Find(HumanBodyBones.UpperChest) ?? rig.Find(HumanBodyBones.Spine);
                bool needsChild = child != HumanBodyBones.LastBone || !string.IsNullOrWhiteSpace(right ? s.rightChild : s.leftChild);
                var c = !needsChild
                    ? null : rig.Find(child, right ? s.rightChild : s.leftChild);
                if (a == null || p == null || (needsChild && c == null)) return;
                float scale = Vector3.Distance(a.position, p.position);
                if (c != null) scale = Mathf.Min(scale, Vector3.Distance(a.position, c.position));
                if (s.region == TexturePaintAnatomicalRegion.Waist || s.region == TexturePaintAnatomicalRegion.Seat)
                {
                    var leftHip = rig.Find(HumanBodyBones.LeftUpperLeg); var rightHip = rig.Find(HumanBodyBones.RightUpperLeg);
                    if (leftHip != null && rightHip != null) scale = Mathf.Max(scale, Vector3.Distance(leftHip.position, rightHip.position) * .7f);
                    radius = 1.3f;
                }
                if (scale < .00001f) return;
                Vector3 chord = c != null ? c.position - p.position : a.position - p.position;
                // A fully folded joint can put parent and child at the same position.
                Vector3 axis = chord.sqrMagnitude > scale * scale * 1e-8f ? chord.normalized : (a.position - p.position).normalized;
                Vector3 outside = Vector3.ProjectOnPlane(a.forward, axis).normalized;
                if (s.region == TexturePaintAnatomicalRegion.Elbows) outside = -outside;
                if (s.region == TexturePaintAnatomicalRegion.Knees || s.region == TexturePaintAnatomicalRegion.Elbows)
                {
                    Vector3 projection = p.position + axis * Vector3.Dot(a.position - p.position, axis);
                    Vector3 bend = a.position - projection;
                    // Below 5 degrees use the transported reference axes instead of amplifying pose noise.
                    float weight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, .12f, bend.magnitude / scale));
                    if (bend.sqrMagnitude > 1e-10f)
                    {
                        Vector3 target = bend.normalized;
                        // Rotate in the joint plane; Slerp between opposite vectors can leave it.
                        if (outside.sqrMagnitude < .5f) outside = target;
                        else outside = Quaternion.AngleAxis(Vector3.SignedAngle(outside, target, axis) * weight, axis) * outside;
                    }
                }
                if (outside.sqrMagnitude < .5f) outside = Vector3.ProjectOnPlane(a.up, axis).normalized;
                if (outside.sqrMagnitude < .5f) outside = Vector3.Cross(axis, Mathf.Abs(axis.x) < .9f ? Vector3.right : Vector3.up).normalized;
                outside = Quaternion.AngleAxis(s.rotation, axis) * outside;
                Vector3 center = a.position;
                if (s.region == TexturePaintAnatomicalRegion.Armpits)
                    center += (p.position - a.position).normalized * scale * .12f - p.up * scale * .32f;
                if (s.region == TexturePaintAnatomicalRegion.Shoulders) center += a.up * scale * .12f;
                if (s.region == TexturePaintAnatomicalRegion.WristCuffs || s.region == TexturePaintAnatomicalRegion.AnkleCuffs)
                    center -= axis * scale * .06f;
                if (s.region == TexturePaintAnatomicalRegion.Lips)
                {
                    Vector3 lips = Vector3.zero; int count = 0;
                    foreach (var b in rig.bones) if (b != null && IsLip(b.name)) { lips += b.position; count++; }
                    center = count > 0 ? lips / count : a.position + a.forward * scale * .65f + a.up * scale * .15f;
                    axis = a.up.normalized; outside = a.forward.normalized;
                    outside = Quaternion.AngleAxis(s.rotation, axis) * outside;
                }
                if (s.region == TexturePaintAnatomicalRegion.Waist) center += a.up * scale * .5f;
                if (s.region == TexturePaintAnatomicalRegion.Seat) { outside = -outside; center += outside * scale * .65f - a.up * scale * .25f; length = .8f; }
                Vector3 lateral = Vector3.Cross(axis, outside).normalized;
                center += (lateral * s.offset.x + axis * s.offset.y + outside * s.offset.z) * scale;
                result.Add(new TexturePaintRegionEnvelope
                {
                    center = center, axis = axis, outward = outside, cylindrical = cylinder, arc = arc, feather = s.feather,
                    radii = Vector3.Scale(new Vector3(radius, length, s.region == TexturePaintAnatomicalRegion.Lips ? .35f : radius), s.size) * scale
                });
            }
        }
        private static bool IsLip(string name) => !string.IsNullOrEmpty(name) && (name.IndexOf("lip", StringComparison.OrdinalIgnoreCase) >= 0 || name.Equals("mouth", StringComparison.OrdinalIgnoreCase))
            && name.IndexOf("adjust", StringComparison.OrdinalIgnoreCase) < 0 && !name.EndsWith("_end", StringComparison.OrdinalIgnoreCase);
        private static bool HasLipBones(TexturePaintAnatomy rig) => rig.bones.Exists(b => b != null && IsLip(b.name));

        /// <summary>Union all UV owners. A shared/mirrored texel cannot encode different physical sides.</summary>
        public static Texture2D Build(ReconstructedSurface surface, TexturePaintAnatomicalSettings settings,
            int width, int height, out string diagnostic)
            => BuildRegions(surface, new[] { settings }, width, height, out diagnostic);

        public static Texture2D BuildRegions(ReconstructedSurface surface, IEnumerable<TexturePaintAnatomicalSettings> settings,
            int width, int height, out string diagnostic)
        {
            var envelopes = new List<TexturePaintRegionEnvelope>();
            var diagnostics = new List<string>();
            foreach (var entry in settings)
            {
                envelopes.AddRange(Resolve(surface?.anatomy, entry, out string issue));
                if (!string.IsNullOrEmpty(issue) && !diagnostics.Contains(issue)) diagnostics.Add(issue);
            }
            diagnostic = diagnostics.Count == 0 ? null : string.Join("\n", diagnostics);
            width = Mathf.Clamp(width, 1, 4096); height = Mathf.Clamp(height, 1, 4096);
            var pixels = new byte[width * height];
            if (surface?.mesh != null && envelopes.Count > 0)
            {
                Vector3[] vertices = surface.mesh.vertices; Vector2[] uv = surface.mesh.uv; int[] triangles = surface.mesh.triangles;
                if (uv.Length != vertices.Length) diagnostic = "This surface has no usable UV0.";
                else
                {
                    // Determine real UV coverage before accumulating owners. Otherwise a later
                    // real sample could retain a region value from an earlier island's padding.
                    var distances = surface.GetAnatomicalUvDistances(width, height, uv, triangles);
                    var bounds = envelopes.ConvertAll(envelope => envelope.Bounds);
                    for (int t = 0; t + 2 < triangles.Length; t += 3)
                    {
                        int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                        var triangleBounds = new Bounds(vertices[a], Vector3.zero);
                        triangleBounds.Encapsulate(vertices[b]); triangleBounds.Encapsulate(vertices[c]);
                        bool candidate = false;
                        foreach (var regionBounds in bounds) if (triangleBounds.Intersects(regionBounds)) { candidate = true; break; }
                        if (!candidate) continue;
                        ProceduralMeshMapBuilder.RasterizeTriangle(uv[a], uv[b], uv[c], width, height, distances,
                            (x, y, bary) =>
                            {
                                Vector3 point = vertices[a] * bary.x + vertices[b] * bary.y + vertices[c] * bary.z;
                                float coverage = 0;
                                foreach (var envelope in envelopes) coverage = Mathf.Max(coverage, envelope.Evaluate(point));
                                int i = y * width + x;
                                pixels[i] = (byte)Mathf.Max(pixels[i], Mathf.RoundToInt(coverage * 255));
                            }, true);
                    }
                }
            }
            var texture = new Texture2D(width, height, TextureFormat.R8, false, true)
            { name = "Anatomical Regions", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixelData(pixels, 0); texture.Apply(false, false); return texture;
        }
    }
}
