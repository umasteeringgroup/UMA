#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMAVisemePoseTests
    {
        const string SlotPath = "Assets/UMA/UMA3/Races/Slots/UMA30_Body/UMA30_Body_UDIM1001_slot.asset";
        static IEnumerable Cases
        {
            get
            {
                foreach (string name in UMAVisemePoseBuilder.Names)
                    foreach (bool mild in new[] { false, true })
                        yield return new TestCaseData(name, mild).SetName("VisemeAsset_" + name + (mild ? "_Mild" : "_Full"));
            }
        }
        static string Stem(string name, bool mild) => "viseme_" + name + (mild ? "_mild" : "");
        static string Folder(bool mild) => UMAVisemePoseBuilder.VisemeFolder + (mild ? "/Mild/" : "/Full/");
        static T Asset<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
        }
        static UMABonePose Pose(string name, bool mild) => Asset<UMABonePose>(Folder(mild) + Stem(name, mild) + "_pose.asset");
        static UMAExpressionGroup Group(bool mild) => Asset<UMAExpressionGroup>(mild ? UMAVisemePoseBuilder.MildGroupPath : UMAVisemePoseBuilder.GroupPath);

        [TestCaseSource(nameof(Cases))]
        [Category("UMA")]
        public void PoseAndDnaHaveValidRuntimeMappings(string name, bool mild)
        {
            var pose = Pose(name, mild);
            var dna = Asset<DNA>(Folder(mild) + Stem(name, mild) + "_DNA.asset");
            Assert.That(pose.name, Is.EqualTo(Stem(name, mild) + "_pose"));
            Assert.That(dna.defaultValue, Is.Zero);
            Assert.That(dna.effects.Count, Is.EqualTo(1));
            var effect = dna.effects[0] as DNAEffect_BonePose;
            Assert.That(effect, Is.Not.Null);
            Assert.That(effect.bonePose, Is.SameAs(pose));
            Assert.That(effect.isBasePose, Is.False);
            Assert.That(effect.RequiresExpressionBuild, Is.False);
            Assert.That(effect.minMapping, Is.Zero);
            Assert.That(effect.maxMapping, Is.EqualTo(1));
            Assert.That(effect.curve.Evaluate(.5f), Is.EqualTo(.5f).Within(1e-6f));
            Assert.That(pose.tweenPoses, Is.Empty);
            var reference = Asset<SlotDataAsset>(SlotPath).meshData.umaBones.ToDictionary(b => b.hash);
            var seen = new HashSet<string>();
            foreach (var bone in pose.poses)
            {
                Assert.That(seen.Add(bone.bone), Is.True, "Duplicate bone");
                Assert.That(bone.hash, Is.EqualTo(UMAUtils.StringToHash(bone.bone)));
                Assert.That(reference.ContainsKey(bone.hash), Is.True, bone.bone);
                Assert.That(bone.bone == "Mandible" || bone.bone.Contains("Lips") || bone.bone.StartsWith("Tongue", StringComparison.Ordinal), Is.True, "Speech must not move eyes, brows, neck, or head");
                Assert.That(bone.enabled, Is.True);
                Assert.That(float.IsNaN(bone.position.sqrMagnitude) || float.IsInfinity(bone.position.sqrMagnitude), Is.False);
                Assert.That(bone.position.magnitude, Is.LessThan(.03f));
                Assert.That(Quaternion.Dot(bone.rotation, bone.rotation), Is.EqualTo(1).Within(1e-5f));
                Assert.That(bone.scale, Is.EqualTo(Vector3.one));
            }
            Assert.That(pose.poses.Length, Is.GreaterThan(0));
            Assert.That(Group(mild).TryGetDefinition("viseme_" + name, out var definition), Is.True);
            Assert.That(definition.dna, Is.SameAs(dna));
            Assert.That(definition.roles, Is.EqualTo(ExpressionRole.Viseme));
        }

        [Test]
        public void GroupsPreserveExistingExpressionsAndRaceAssignments()
        {
            var full = Group(false); var mild = Group(true);
            var messages = new List<ExpressionValidationMessage>();
            Assert.That(full.Validate(messages), Is.True, string.Join("\n", messages.Select(m => m.message)));
            Assert.That(mild.Validate(messages), Is.True, string.Join("\n", messages.Select(m => m.message)));
            Assert.That(full.expressions.Count( e => UMAVisemePoseBuilder.IsGeneratedId(e.id)), Is.EqualTo(15));
            Assert.That(mild.Count, Is.EqualTo(full.Count));
            foreach (var original in full.expressions.Where(e => !UMAVisemePoseBuilder.IsGeneratedId(e.id)))
            {
                Assert.That(mild.TryGetDefinition(original.id, out var copy), Is.True);
                Assert.That(copy.dna, Is.SameAs(original.dna));
                Assert.That(copy.priority, Is.EqualTo(original.priority));
                Assert.That(copy.roles, Is.EqualTo(original.roles));
                Assert.That(copy.affectedJoints, Is.EqualTo(original.affectedJoints));
                Assert.That(copy.blendMode, Is.EqualTo(original.blendMode));
                Assert.That(copy.responseTime, Is.EqualTo(original.responseTime));
                Assert.That(copy.blinkClosedValue, Is.EqualTo(original.blinkClosedValue));
            }
            foreach (string race in new[] { "HumanMale30", "HumanFemale30" })
                Assert.That(Asset<RaceData>("Assets/UMA/UMA3/Races/" + race + ".asset").expressionGroup, Is.SameAs(full));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LipJawAndTongueMoveInAnatomicalDirections(bool mild)
        {
            var root = NewRig(out var data);
            try
            {
                Transform Bone(string n) => data.skeleton.GetBoneTransform(n);
                var up = Bone("Head").up;
                float Width() => Vector3.Distance(Bone("LeftLips").position, Bone("RightLips").position);
                float Aperture() => Vector3.Dot(Bone("LipsSuperior").position - Bone("LipsInferior").position, up);
                float neutralAperture = Aperture(), neutralWidth = Width();
                Pose("aa", mild).ApplyPose(data.skeleton, 1);
                Assert.That(Aperture(), Is.GreaterThan(neutralAperture + .009f), "Jaw must open downward");
                data.skeleton.RestoreAll(); Pose("PP", mild).ApplyPose(data.skeleton, 1);
                Assert.That(Aperture(), Is.LessThan(neutralAperture - .002f), "PP must close lips");
                data.skeleton.RestoreAll(); Pose("U", mild).ApplyPose(data.skeleton, 1);
                Assert.That(Width(), Is.LessThan(neutralWidth * .85f), "U must purse, not widen");
                Assert.That(Aperture(), Is.GreaterThan(neutralAperture + .003f), "U must also open: pursing alone can seal the lips");
                data.skeleton.RestoreAll(); Pose("I", mild).ApplyPose(data.skeleton, 1);
                Assert.That(Width(), Is.GreaterThan(neutralWidth * 1.05f), "I must spread the lips");
                data.skeleton.RestoreAll();
                var tongueLocal = Bone("Tongue01").localPosition;
                Pose("TH", mild).ApplyPose(data.skeleton, 1);
                Assert.That(Vector3.Distance(tongueLocal, Bone("Tongue01").localPosition), Is.GreaterThan(.006f));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SilenceIsRelaxedSlightlyOpenWithoutPursing(bool mild)
        {
            var root = NewRig(out var data);
            try
            {
                Transform Bone(string n) => data.skeleton.GetBoneTransform(n);
                var up = Bone("Head").up;
                float Aperture() => Vector3.Dot(Bone("LipsSuperior").position - Bone("LipsInferior").position, up);
                float Width() => Vector3.Distance(Bone("LeftLips").position, Bone("RightLips").position);
                float restAperture = Aperture(), restWidth = Width();
                var upperRest = Bone("LipsSuperior").position;
                var jawRest = Bone("Mandible").localRotation;
                Pose("sil", mild).ApplyPose(data.skeleton, 1);
                Assert.That(Aperture() - restAperture, Is.InRange(.0008f, .0028f), "Silence needs only a slight jaw release");
                Assert.That(Width(), Is.EqualTo(restWidth).Within(.0001f), "Silence must not purse or spread the lips");
                Assert.That(Vector3.Distance(Bone("LipsSuperior").position, upperRest), Is.LessThan(1e-6f));
                Assert.That(Quaternion.Angle(jawRest, Bone("Mandible").localRotation), Is.InRange(.5f, 1f));
                Assert.That(Pose("sil", mild).poses.Any(p => p.bone.StartsWith("Tongue", StringComparison.Ordinal)), Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void FacialClassificationProtectsJawButNotLipsAndHonorsExplicitMappings()
        {
            var root = NewRig(out var data);
            try
            {
                var player = root.AddComponent<DynamicExpressionPlayer>();
                player.expressionGroupOverride = Group(false);
                player.EnableBlinking = false; player.EnableSaccades = false; player.EnableLookAt = false;
                player.overrideMecanimHead = false; player.overrideMecanimJaw = false;
                player.Rebind(); player.EvaluateExpressionsNow(); player.ApplyRigExpressionsNow();
                var lip = data.skeleton.GetBoneTransform("LipsSuperior");
                var jaw = data.skeleton.GetBoneTransform("Mandible");
                var head = data.skeleton.GetBoneTransform("Head");
                var lipRest = lip.localPosition; var jawRest = jaw.localRotation; var headRest = head.localRotation;
                player.SetExpression("viseme_O", 1); player.EvaluateExpressionsNow(); player.ApplyRigExpressionsNow();
                Assert.That(Vector3.Distance(lip.localPosition, lipRest), Is.GreaterThan(.001f));
                Assert.That(Quaternion.Angle(jaw.localRotation, jawRest), Is.LessThan(.06f));
                Assert.That(Quaternion.Angle(head.localRotation, headRest), Is.LessThan(.06f));
                player.overrideMecanimJaw = true; player.ApplyRigExpressionsNow();
                Assert.That(Quaternion.Angle(jaw.localRotation, jawRest), Is.GreaterThan(5));
                player.genericBoneJoints.Add(new DynamicExpressionPlayer.ExpressionBoneJoint
                    { boneName = "LipsSuperior", joint = ExpressionJoint.Head });
                player.Rebind(); player.SetExpression("viseme_O", 1); player.EvaluateExpressionsNow(); player.ApplyRigExpressionsNow();
                Assert.That(Vector3.Distance(lip.localPosition, lipRest), Is.LessThan(1e-6f), "Explicit mappings must win over UMA defaults");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(false, "HumanFemale30")]
        [TestCase(true, "HumanFemale30")]
        [TestCase(false, "HumanMale30")]
        [TestCase(true, "HumanMale30")]
        public void AllVisemesApplyThroughPlayerAndRestoreWithoutDrift(bool mild, string raceName)
        {
            var root = NewRig(out var data);
            try
            {
                data.umaRecipe = new UMAData.UMARecipe();
                data.umaRecipe.SetRace(Asset<RaceData>("Assets/UMA/UMA3/Races/" + raceName + ".asset"));
                var player = root.AddComponent<DynamicExpressionPlayer>();
                player.expressionGroupOverride = Group(mild);
                player.EnableBlinking = false; player.EnableSaccades = false; player.EnableLookAt = false;
                player.processDistance = 0; player.Rebind();
                player.EvaluateExpressionsNow(); player.ApplyRigExpressionsNow();
                var neutral = Capture(data);
                foreach (string name in UMAVisemePoseBuilder.Names)
                {
                    Pose(name, mild).ApplyPose(data.skeleton, 1);
                    var expected = Capture(data);
                    Assert.That(player.SetExpression("viseme_" + name, 1), Is.True);
                    player.EvaluateExpressionsNow(); player.ApplyRigExpressionsNow();
                    AssertPose(data, expected);
                    for (int frame = 0; frame < 20; frame++) player.ApplyRigExpressionsNow();
                    AssertPose(data, expected);
                    player.SetExpression("viseme_" + name, 0);
                    player.EvaluateExpressionsNow(); player.ApplyRigExpressionsNow();
                    AssertPose(data, neutral);
                }
                player.BeginExpressionBatch();
                player.SetExpression("viseme_aa", .35f); player.SetExpression("viseme_O", .3f); player.SetExpression("viseme_PP", .35f);
                player.EndExpressionBatch(); player.EvaluateExpressionsNow(); player.ApplyRigExpressionsNow();
                var mixed = Capture(data);
                for (int i = 0; i < 60; i++) player.ApplyRigExpressionsNow();
                AssertPose(data, mixed);
                // Moving/rotating an avatar must not change a bone-local expression.
                root.transform.SetPositionAndRotation(new Vector3(4, 2, -3), Quaternion.Euler(23, 127, -14));
                player.ApplyRigExpressionsNow(); AssertPose(data, mixed, skipRoot: true);
                player.ResetAllExpressions(ExpressionSource.Manual); player.EvaluateExpressionsNow(); player.ApplyRigExpressionsNow();
                AssertPose(data, neutral, skipRoot: true);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static GameObject NewRig(out UMAData data)
        {
            var root = new GameObject("Viseme regression rig"); data = root.AddComponent<UMAData>();
            var definitions = Asset<SlotDataAsset>(SlotPath).meshData.umaBones;
            var bones = definitions.ToDictionary(b => b.hash, b => new GameObject(b.name).transform);
            foreach (var b in definitions)
            {
                var t = bones[b.hash]; t.SetParent(bones.TryGetValue(b.parent, out var parent) ? parent : root.transform, false);
                t.localPosition = b.position; t.localRotation = b.rotation; t.localScale = b.scale;
            }
            data.skeleton = new UMASkeleton(root.transform);
            return root;
        }
        struct LocalPose
        {
            public Vector3 position, scale; public Quaternion rotation;
            public LocalPose(Transform t) { position = t.localPosition; scale = t.localScale; rotation = t.localRotation; }
        }
        static Dictionary<int, LocalPose> Capture(UMAData data) => data.skeleton.BoneHashes.ToDictionary(h => h, h => new LocalPose(data.skeleton.GetBoneTransform(h)));
        static void AssertPose(UMAData data, Dictionary<int, LocalPose> expected, bool skipRoot = false)
        {
            foreach (var pair in expected)
            {
                var t = data.skeleton.GetBoneTransform(pair.Key);
                if (skipRoot && t == data.transform) continue;
                Assert.That(Vector3.Distance(t.localPosition, pair.Value.position), Is.LessThan(1e-6f), t.name);
                Assert.That(Quaternion.Angle(t.localRotation, pair.Value.rotation), Is.LessThan(.06f), t.name);
                Assert.That(Vector3.Distance(t.localScale, pair.Value.scale), Is.LessThan(1e-6f), t.name);
            }
        }
    }
}
#endif
