#if UNITY_INCLUDE_TESTS
using System;
using System.Linq;
using NUnit.Framework;
using UMA.TexturePaint.Editor.Tests;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Tests
{
    public sealed class AnatomicalRegionTests
    {
        private static TexturePaintAnatomy Rig(bool bent = false)
        {
            var rig = new TexturePaintAnatomy();
            foreach (bool right in new[] { false, true })
            {
                float x = right ? .8f : -.8f;
                Add(right ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg, new Vector3(x, 1, 0));
                Add(right ? HumanBodyBones.RightLowerLeg : HumanBodyBones.LeftLowerLeg, new Vector3(x, 0, 0));
                Add(right ? HumanBodyBones.RightFoot : HumanBodyBones.LeftFoot, new Vector3(x, -1, bent ? -.6f : 0));
                Add(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm, new Vector3(x, 2, 0));
                Add(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm, new Vector3(x * 2, 2, 0));
                Add(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand, new Vector3(x * 3, 2, bent ? .6f : 0));
            }
            Add(HumanBodyBones.Hips, new Vector3(0, 1.2f, 0));
            Add(HumanBodyBones.Spine, new Vector3(0, 1.8f, 0));
            Add(HumanBodyBones.Chest, new Vector3(0, 2.2f, 0));
            Add(HumanBodyBones.Neck, new Vector3(0, 2.6f, 0));
            Add(HumanBodyBones.Head, new Vector3(0, 2.9f, 0));
            return rig;
            void Add(HumanBodyBones id, Vector3 point) => rig.bones.Add(new TexturePaintAnatomicalBone { name = id.ToString(), humanoid = id, position = point });
        }

        [TestCase(TexturePaintAnatomicalRegion.Knees)]
        [TestCase(TexturePaintAnatomicalRegion.Elbows)]
        public void StraightJointsCoverOuterThirdAndExcludeTheBendSide(TexturePaintAnatomicalRegion region)
        {
            var settings = new TexturePaintAnatomicalSettings { region = region, side = TexturePaintRegionSide.Left, feather = .01f };
            var envelope = TexturePaintAnatomicalMask.Resolve(Rig(), settings, out var error).Single();
            Assert.That(error, Is.Null);
            Assert.That(Vector3.Dot(envelope.outward, region == TexturePaintAnatomicalRegion.Knees ? Vector3.forward : Vector3.back), Is.GreaterThan(.99f));
            Assert.That(envelope.Evaluate(envelope.center + envelope.outward * envelope.radii.z * .5f), Is.GreaterThan(.99f));
            Assert.That(envelope.Evaluate(envelope.center - envelope.outward * envelope.radii.z * .5f), Is.Zero);
            int covered = 0;
            var side = Vector3.Cross(envelope.axis, envelope.outward);
            for (int i = 0; i < 360; i++)
            {
                float angle = (i + .5f) * Mathf.Deg2Rad;
                var point = envelope.center + .5f * (envelope.outward * Mathf.Cos(angle) * envelope.radii.z + side * Mathf.Sin(angle) * envelope.radii.x);
                if (envelope.Evaluate(point) > .5f) covered++;
            }
            Assert.That(covered, Is.InRange(116, 120));
        }

        [TestCase(TexturePaintAnatomicalRegion.Knees)]
        [TestCase(TexturePaintAnatomicalRegion.Elbows)]
        public void BentJointsFaceAwayFromTheBend(TexturePaintAnatomicalRegion region)
        {
            var rig = Rig(true);
            var envelope = TexturePaintAnatomicalMask.Resolve(rig, new TexturePaintAnatomicalSettings { region = region, side = TexturePaintRegionSide.Left }, out _).Single();
            var a = rig.Find(region == TexturePaintAnatomicalRegion.Knees ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.LeftLowerArm).position;
            var p = rig.Find(region == TexturePaintAnatomicalRegion.Knees ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.LeftUpperArm).position;
            var c = rig.Find(region == TexturePaintAnatomicalRegion.Knees ? HumanBodyBones.LeftFoot : HumanBodyBones.LeftHand).position;
            var chord = c - p;
            var outside = (a - p - chord * (Vector3.Dot(a - p, chord) / chord.sqrMagnitude)).normalized;
            Assert.That(Vector3.Dot(envelope.outward, outside), Is.GreaterThan(.99f));
        }

        [Test]
        public void AllDefaultRegionsResolveAndSidesSelectPhysicalBones()
        {
            foreach (TexturePaintAnatomicalRegion region in Enum.GetValues(typeof(TexturePaintAnatomicalRegion)))
            {
                var envelopes = TexturePaintAnatomicalMask.Resolve(Rig(), new TexturePaintAnatomicalSettings { region = region }, out var error);
                Assert.That(envelopes, Is.Not.Empty, region.ToString());
                if (region != TexturePaintAnatomicalRegion.Lips) Assert.That(error, Is.Null, region.ToString());
                foreach (var envelope in envelopes) Assert.That(envelope.radii.x * envelope.radii.y * envelope.radii.z, Is.GreaterThan(0));
            }
            var left = TexturePaintAnatomicalMask.Resolve(Rig(), new TexturePaintAnatomicalSettings { side = TexturePaintRegionSide.Left }, out _);
            var right = TexturePaintAnatomicalMask.Resolve(Rig(), new TexturePaintAnatomicalSettings { side = TexturePaintRegionSide.Right }, out _);
            Assert.That(left.Single().center.x, Is.LessThan(0)); Assert.That(right.Single().center.x, Is.GreaterThan(0));
        }

        [Test]
        public void LipBonesReplaceEstimatedPlacement()
        {
            var rig = Rig(); var center = new Vector3(0, 2.95f, .2f);
            rig.bones.Add(new TexturePaintAnatomicalBone { name = "UpperLip", position = center + Vector3.up * .01f });
            rig.bones.Add(new TexturePaintAnatomicalBone { name = "LowerLip", position = center - Vector3.up * .01f });
            var envelope = TexturePaintAnatomicalMask.Resolve(rig, new TexturePaintAnatomicalSettings { region = TexturePaintAnatomicalRegion.Lips }, out var diagnostic).Single();
            Assert.That(diagnostic, Is.Null); Assert.That(Vector3.Distance(envelope.center, center), Is.LessThan(.0001f));
        }

        [Test]
        public void RotationAndScaleOfRigPreserveCoverage()
        {
            var rig = Rig(true); var s = new TexturePaintAnatomicalSettings { side = TexturePaintRegionSide.Left };
            var first = TexturePaintAnatomicalMask.Resolve(rig, s, out _).Single();
            var point = first.center + first.outward * first.radii.z * .5f;
            var rotation = Quaternion.Euler(47, 81, 19); var translation = new Vector3(14, -6, 27);
            foreach (var bone in rig.bones) { bone.position = rotation * bone.position * 3 + translation; bone.forward = rotation * bone.forward; bone.up = rotation * bone.up; }
            var second = TexturePaintAnatomicalMask.Resolve(rig, s, out _).Single();
            Assert.That(second.Evaluate(rotation * point * 3 + translation), Is.EqualTo(first.Evaluate(point)).Within(.0001));
        }

        [Test]
        public void ExactBoneOverridesAndLegacyUmaNamesResolve()
        {
            var rig = Rig();
            foreach (var bone in rig.bones) { bone.humanoid = HumanBodyBones.LastBone; bone.name = "mixamorig:" + bone.name.Replace("UpperLeg", "Thigh").Replace("LowerLeg", "Calf"); }
            Assert.That(TexturePaintAnatomicalMask.Resolve(rig, new TexturePaintAnatomicalSettings(), out var error).Count, Is.EqualTo(2));
            Assert.That(error, Is.Null);
            rig.bones[0].name = "customHip"; rig.bones[1].name = "customKnee"; rig.bones[2].name = "customFoot";
            var s = new TexturePaintAnatomicalSettings { side = TexturePaintRegionSide.Left, leftAnchor = "customKnee", leftParent = "customHip", leftChild = "customFoot" };
            Assert.That(TexturePaintAnatomicalMask.Resolve(rig, s, out error).Count, Is.EqualTo(1)); Assert.That(error, Is.Null);
        }

        private static Mesh JointQuad(bool overlapping, bool reverse)
        {
            var front = new[] { new Vector3(-1.1f, -.2f, .12f), new Vector3(-.5f, -.2f, .12f), new Vector3(-.5f, .2f, .12f), new Vector3(-1.1f, .2f, .12f) };
            var uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            var indices = new[] { 0, 1, 2, 0, 2, 3 };
            if (overlapping)
            {
                front = front.Concat(front.Select(v => new Vector3(v.x, v.y, -v.z))).ToArray();
                uv = uv.Concat(uv).ToArray();
                var back = new[] { 6, 5, 4, 7, 6, 4 };
                indices = reverse ? back.Concat(indices).ToArray() : indices.Concat(back).ToArray();
            }
            return new Mesh { vertices = front, uv = uv, triangles = indices };
        }

        [TestCase(false, false)] [TestCase(true, false)] [TestCase(true, true)]
        public void UvRasterizationUnionsMirroredOwnersWithoutDependingOnOrder(bool overlapping, bool reverse)
        {
            var mesh = JointQuad(overlapping, reverse); Texture2D texture = null;
            try
            {
                texture = TexturePaintAnatomicalMask.Build(new ReconstructedSurface { mesh = mesh, anatomy = Rig() }, new TexturePaintAnatomicalSettings(), 32, 32, out var error);
                Assert.That(error, Is.Null); Assert.That(texture.GetPixel(16, 16).r, Is.GreaterThan(.9f));
                Assert.That(texture.GetPixel(16, 0).r, Is.GreaterThan(0), "Valid UV border texels stay covered.");
            }
            finally { Object.DestroyImmediate(texture); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void MissingRigProducesBlackMaskAndDiagnostic()
        {
            var mesh = JointQuad(false, false); Texture2D texture = null;
            try
            {
                texture = TexturePaintAnatomicalMask.Build(new ReconstructedSurface { mesh = mesh }, new TexturePaintAnatomicalSettings(), 16, 16, out var error);
                Assert.That(error, Is.Not.Empty); Assert.That(texture.GetPixels().All(p => p.r == 0), Is.True);
            }
            finally { Object.DestroyImmediate(texture); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void SharedProfileChangesInvalidateCpuAndGpuMaskCaches()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using var fixture = new TexturePaintGpuTestFixture(Color.black, size: 32);
            Object.DestroyImmediate(fixture.set.surface.mesh);
            var mesh = JointQuad(false, false); fixture.set.surface.mesh = mesh; fixture.set.surface.anatomy = Rig();
            var profile = ScriptableObject.CreateInstance<TexturePaintRegionProfile>();
            using var compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            fixture.set.compositor = compositor;
            fixture.set.channels[TexturePaintChannel.Albedo].composite = EditableTextureTarget.Create("Composite", 32, 32, RenderTextureFormat.ARGBHalf);
            var layer = fixture.set.AddLayer("Region Test");
            layer.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget("White", 32, 32, RenderTextureFormat.ARGBHalf, null, Color.white);
            var effect = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.AnatomicalRegion); effect.regionProfile = profile;
            fixture.set.AddLayerMask(layer, 1).effects.stack.Add(effect);
            try
            {
                fixture.set.RecomposeAll();
                var first = fixture.set.GetAnatomicalRegionMask(effect, 32, 32);
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(TexturePaintChannel.Albedo))[16 * 32 + 16].r, Is.GreaterThan(.9f));
                profile.Get(TexturePaintAnatomicalRegion.Knees).rotation = 180;
                fixture.set.RecomposeAll();
                Assert.That(fixture.set.GetAnatomicalRegionMask(effect, 32, 32), Is.Not.SameAs(first));
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(TexturePaintChannel.Albedo))[16 * 32 + 16].r, Is.LessThan(.01f));
                Assert.That(fixture.set.proceduralMeshMaps, Is.Null, "Region masks must not force an expensive AO/thickness bake.");
            }
            finally { fixture.set.compositor = null; Object.DestroyImmediate(profile); Object.DestroyImmediate(mesh); }
        }

        [TestCase(TexturePaintLayerKind.Paint)] [TestCase(TexturePaintLayerKind.Fill)] [TestCase(TexturePaintLayerKind.Plugin)] [TestCase(TexturePaintLayerKind.Spline)] [TestCase(TexturePaintLayerKind.Projection)]
        public void AllLayerKindsUseIdenticalRegionCoverageAcrossChannels(TexturePaintLayerKind kind)
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using var fixture = new TexturePaintGpuTestFixture(Color.black, size: 32);
            var mesh = JointQuad(false, false); fixture.set.surface.mesh = mesh; fixture.set.surface.anatomy = Rig();
            using var compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            fixture.set.compositor = compositor;
            var layer = fixture.set.AddLayer("Channels"); layer.kind = kind;
            foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
            {
                if (!fixture.set.channels.ContainsKey(channel)) fixture.set.channels[channel] = new TextureChannelTarget
                { channel = channel, format = RenderTextureFormat.ARGBHalf, editable = new EditableTextureTarget("Base", 32, 32, RenderTextureFormat.ARGBHalf, null,
                    channel == TexturePaintChannel.Normal ? new Color(.5f, .5f, 1, 1) : Color.black) };
                fixture.set.channels[channel].composite = EditableTextureTarget.Create("Composite", 32, 32, RenderTextureFormat.ARGBHalf);
                layer.channels[channel] = new EditableTextureTarget("Layer", 32, 32, RenderTextureFormat.ARGBHalf, null, Color.white);
                layer.GetChannelSettings(channel).blendMode = TexturePaintBlendMode.Normal;
            }
            var mask = fixture.set.AddLayerMask(layer, 1);
            mask.effects.stack.Add(TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.AnatomicalRegion));
            try
            {
                fixture.set.RecomposeAll();
                var albedo = TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(TexturePaintChannel.Albedo));
                foreach (TexturePaintChannel channel in Enum.GetValues(typeof(TexturePaintChannel)))
                {
                    if (channel == TexturePaintChannel.Normal) continue;
                    var actual = TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(channel));
                    Assert.That(albedo.Zip(actual, (a, b) => Mathf.Abs(a.r - b.r)).Max(), Is.LessThan(.01f), channel.ToString());
                }
                Assert.That(albedo[16 * 32 + 16].r, Is.GreaterThan(.9f)); Assert.That(albedo[16 * 32].r, Is.LessThan(.05f));
                var normals = TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(TexturePaintChannel.Normal));
                Assert.That(normals[16 * 32 + 16].r, Is.GreaterThan(.7f), "Normal detail is visible inside the region.");
                Assert.That(normals[16 * 32].r, Is.EqualTo(.5f).Within(.02), "The original normal is preserved outside the region.");
            }
            finally { fixture.set.compositor = null; Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void DocumentSerializationAndUndoClonesRetainIndependentRegionSettings()
        {
            var effect = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.AnatomicalRegion);
            effect.regionSettings.region = TexturePaintAnatomicalRegion.Armpits; effect.regionSettings.offset = new Vector3(.1f, -.2f, .3f);
            var document = ScriptableObject.CreateInstance<TexturePaintDocument>();
            var restored = ScriptableObject.CreateInstance<TexturePaintDocument>();
            try
            {
                var layer = new TexturePaintDocumentLayer(); layer.maskEffects.stack.Add(effect);
                var surface = new TexturePaintDocumentSurface(); surface.layers.Add(layer); document.surfaces.Add(surface);
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(document), restored);
                var saved = restored.surfaces[0].layers[0].maskEffects.stack[0];
                Assert.That(saved.regionSettings.region, Is.EqualTo(TexturePaintAnatomicalRegion.Armpits));
                Assert.That(saved.regionSettings.offset, Is.EqualTo(effect.regionSettings.offset));
                var clone = effect.Clone(); clone.regionSettings.offset = Vector3.zero;
                Assert.That(effect.regionSettings.offset, Is.Not.EqualTo(clone.regionSettings.offset));
                Assert.That((int)TexturePaintMaskEffectKind.Dust, Is.EqualTo(43)); Assert.That((int)TexturePaintMaskEffectKind.AnatomicalRegion, Is.EqualTo(44));
            }
            finally { Object.DestroyImmediate(document); Object.DestroyImmediate(restored); }
        }

        [Test]
        public void CaptureDerivesAnatomicalFrameFromZUpBindPoseAndTransportsItIntoBakedPose()
        {
            var owner = new GameObject("Z-up bind rig"); var mesh = new Mesh();
            var sourceRig = Rig();
            var transforms = sourceRig.bones.Select(b => { var go = new GameObject(b.name); go.transform.SetParent(owner.transform, false); go.transform.localPosition = b.position; return go.transform; }).ToArray();
            var renderer = owner.AddComponent<SkinnedMeshRenderer>();
            var importedRotation = Quaternion.Euler(90, 0, 0);
            mesh.bindposes = transforms.Select(t => Matrix4x4.TRS(importedRotation * t.position, importedRotation, Vector3.one).inverse).ToArray();
            renderer.bones = transforms; renderer.sharedMesh = mesh;
            try
            {
                var rig = TexturePaintAnatomy.Capture(owner.transform, renderer);
                Assert.That(Vector3.Dot(rig.Find(HumanBodyBones.Head).forward, Vector3.forward), Is.GreaterThan(.99f));
                Assert.That(Vector3.Dot(rig.Find(HumanBodyBones.Head).up, Vector3.up), Is.GreaterThan(.99f));
                var envelope = TexturePaintAnatomicalMask.Resolve(rig, new TexturePaintAnatomicalSettings { side = TexturePaintRegionSide.Left }, out _).Single();
                Assert.That(Vector3.Dot(envelope.outward, Vector3.forward), Is.GreaterThan(.99f));
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(owner); }
        }

        [Test]
        public void TightUvScanlinesPreserveAllOriginalSamplesAndPadding()
        {
            const int size = 64; var random = new System.Random(20261007);
            for (int triangle = 0; triangle < 24; triangle++)
            {
                Vector2 Point() => new Vector2((float)random.NextDouble() * 2 - .5f, (float)random.NextDouble() * 2 - .5f);
                Vector2 a = Point(), b = Point(), c = Point();
                var original = Enumerable.Repeat(float.PositiveInfinity, size * size).ToArray();
                var tight = (float[])original.Clone();
                var originalBary = new Vector3[size * size]; var tightBary = new Vector3[size * size];
                ProceduralMeshMapBuilder.RasterizeTriangle(a, b, c, size, size, original, (x, y, bary) => originalBary[y * size + x] = bary);
                ProceduralMeshMapBuilder.RasterizeTriangle(a, b, c, size, size, tight, (x, y, bary) => tightBary[y * size + x] = bary, true);
                Assert.That(tight, Is.EqualTo(original), "Distance samples for triangle " + triangle);
                Assert.That(tightBary, Is.EqualTo(originalBary), "Interpolated positions for triangle " + triangle);
            }
        }

        [Test]
        public void StandaloneBonesUseCanonicalHierarchyAndOnlyApplyAdditionalPreviewRotation()
        {
            const int rootHash = 7781;
            var data = new UMAMeshData { rootBoneHash = rootHash };
            data.umaBones = Rig().bones.Select(b => new UMATransform
            { name = b.name, hash = UMAUtils.StringToHash(b.name), parent = rootHash, position = b.position, rotation = Quaternion.identity, scale = Vector3.one }).Concat(new[]
            { new UMATransform { name = "Root", hash = rootHash, position = new Vector3(30, 20, 10), rotation = Quaternion.Euler(0, 0, 180), scale = Vector3.one } }).ToArray();
            var rotation = Matrix4x4.Rotate(Quaternion.Euler(0, 90, 0));
            var rig = TexturePaintAnatomy.CaptureSlot(data, rotation);
            Assert.That(Vector3.Distance(rig.Find(HumanBodyBones.LeftLowerLeg).position, rotation.MultiplyPoint3x4(new Vector3(-.8f, 0, 0))), Is.LessThan(.0001));
            var envelope = TexturePaintAnatomicalMask.Resolve(rig, new TexturePaintAnatomicalSettings { side = TexturePaintRegionSide.Left }, out _).Single();
            Assert.That(Vector3.Dot(envelope.outward, rotation.MultiplyVector(Vector3.forward)), Is.GreaterThan(.99f));
        }

        [Test]
        public void ArmpitEnvelopeCoversUnderarmAndProtectsShoulderCap()
        {
            var rig = Rig(); var anchor = rig.Find(HumanBodyBones.LeftUpperArm);
            var envelope = TexturePaintAnatomicalMask.Resolve(rig, new TexturePaintAnatomicalSettings
            { region = TexturePaintAnatomicalRegion.Armpits, side = TexturePaintRegionSide.Left }, out _).Single();
            Assert.That(envelope.Evaluate(anchor.position + Vector3.up * .2f), Is.Zero);
            Assert.That(envelope.Evaluate(anchor.position - Vector3.up * .2f + Vector3.forward * .08f), Is.GreaterThan(.25f));
        }

        [TestCase(TexturePaintAnatomicalRegion.Knees)]
        [TestCase(TexturePaintAnatomicalRegion.Elbows)]
        public void FullyFoldedJointsKeepFiniteOrthonormalEnvelopes(TexturePaintAnatomicalRegion region)
        {
            var rig = Rig();
            var parent = rig.Find(region == TexturePaintAnatomicalRegion.Knees ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.LeftUpperArm);
            var child = rig.Find(region == TexturePaintAnatomicalRegion.Knees ? HumanBodyBones.LeftFoot : HumanBodyBones.LeftHand);
            child.position = parent.position;
            var envelope = TexturePaintAnatomicalMask.Resolve(rig, new TexturePaintAnatomicalSettings { region = region, side = TexturePaintRegionSide.Left }, out var issue).Single();
            Assert.That(issue, Is.Null);
            Assert.That(envelope.axis.magnitude, Is.EqualTo(1).Within(.0001));
            Assert.That(envelope.outward.magnitude, Is.EqualTo(1).Within(.0001));
            Assert.That(Vector3.Dot(envelope.axis, envelope.outward), Is.EqualTo(0).Within(.0001));
            Assert.That(envelope.Evaluate(envelope.center + envelope.outward * envelope.radii.z * .5f), Is.GreaterThan(.9));
            Assert.That(float.IsFinite(envelope.Bounds.size.x), Is.True);
        }

        [Test]
        public void SlightReverseBendKeepsEnvelopeFrameInTheJointPlane()
        {
            var rig = Rig(); rig.Find(HumanBodyBones.LeftFoot).position += Vector3.forward * .15f;
            var envelope = TexturePaintAnatomicalMask.Resolve(rig, new TexturePaintAnatomicalSettings { side = TexturePaintRegionSide.Left }, out _).Single();
            Assert.That(Vector3.Dot(envelope.axis, envelope.outward), Is.EqualTo(0).Within(.0001));
            Assert.That(Vector3.Cross(envelope.axis, envelope.outward).magnitude, Is.EqualTo(1).Within(.0001));
        }

        [Test]
        public void InvalidExplicitBoneNamesDoNotSilentlyUseFallbacks()
        {
            var rig = Rig();
            var collar = new TexturePaintAnatomicalSettings { region = TexturePaintAnatomicalRegion.Collar, leftParent = "TypoChest" };
            Assert.That(TexturePaintAnatomicalMask.Resolve(rig, collar, out var issue), Is.Empty);
            Assert.That(issue, Is.Not.Empty);
            var cuff = new TexturePaintAnatomicalSettings { region = TexturePaintAnatomicalRegion.WristCuffs, side = TexturePaintRegionSide.Left, leftChild = "TypoFinger" };
            Assert.That(TexturePaintAnatomicalMask.Resolve(rig, cuff, out issue), Is.Empty);
            Assert.That(issue, Is.Not.Empty);
            Assert.That(rig.Find(HumanBodyBones.LastBone), Is.Null);
        }

        [Test]
        public void RegionUnionPreservesPaintedExclusionsAndInvalidatesWhenAnyMemberChanges()
        {
            TexturePaintGpuTestFixture.RequireComputeShaders();
            using var fixture = new TexturePaintGpuTestFixture(Color.black, size: 32);
            Object.DestroyImmediate(fixture.set.surface.mesh);
            var mesh = JointQuad(false, false); fixture.set.surface.mesh = mesh; fixture.set.surface.anatomy = Rig();
            using var compositor = new TextureLayerCompositor(TexturePaintGpuTestFixture.LoadShader("LayerComposite.compute"));
            fixture.set.compositor = compositor;
            fixture.set.channels[TexturePaintChannel.Albedo].composite = EditableTextureTarget.Create("Composite", 32, 32, RenderTextureFormat.ARGBHalf);
            var layer = fixture.set.AddLayer("Union with painted restriction");
            layer.channels[TexturePaintChannel.Albedo] = new EditableTextureTarget("White", 32, 32, RenderTextureFormat.ARGBHalf, null, Color.white);
            var effect = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.AnatomicalRegion);
            effect.regionSettings.rotation = 180;
            effect.regionUnion.Add(new TexturePaintAnatomicalSettings());
            fixture.set.AddLayerMask(layer, .4f).effects.stack.Add(effect);
            try
            {
                fixture.set.RecomposeAll();
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(TexturePaintChannel.Albedo))[16 * 32 + 16].r, Is.EqualTo(.4f).Within(.01));
                effect.regionUnion[0].rotation = 180;
                fixture.set.RecomposeAll();
                Assert.That(TexturePaintGpuTestFixture.ReadPixels(fixture.set.GetVisibleTexture(TexturePaintChannel.Albedo))[16 * 32 + 16].r, Is.LessThan(.01));
            }
            finally { fixture.set.compositor = null; Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void UnionMembersSurviveSerializationAndHaveIndependentUndoCopies()
        {
            var effect = TexturePaintMaskEffect.Create(TexturePaintMaskEffectKind.AnatomicalRegion);
            effect.regionUnion.Add(new TexturePaintAnatomicalSettings { region = TexturePaintAnatomicalRegion.Elbows, size = Vector3.one * 2 });
            var restored = JsonUtility.FromJson<TexturePaintMaskEffect>(JsonUtility.ToJson(effect));
            Assert.That(restored.regionUnion.Single().region, Is.EqualTo(TexturePaintAnatomicalRegion.Elbows));
            var clone = restored.Clone(); clone.regionUnion[0].size = Vector3.one;
            Assert.That(restored.regionUnion[0].size, Is.EqualTo(Vector3.one * 2));
            var profile = ScriptableObject.CreateInstance<TexturePaintRegionProfile>();
            try
            {
                restored.regionProfile = profile;
                profile.Get(TexturePaintAnatomicalRegion.Elbows).size = Vector3.one * 3;
                restored.regionUnion[0].side = TexturePaintRegionSide.Right;
                var resolved = restored.ResolveRegions();
                Assert.That(resolved[1].size, Is.EqualTo(Vector3.one * 3));
                Assert.That(resolved[1].side, Is.EqualTo(TexturePaintRegionSide.Right));
                resolved[1].size = Vector3.one;
                Assert.That(profile.Get(TexturePaintAnatomicalRegion.Elbows).size, Is.EqualTo(Vector3.one * 3));
            }
            finally { Object.DestroyImmediate(profile); }
        }
    }
}
#endif
