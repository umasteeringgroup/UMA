using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The Rig Profile Editor's live preview: a HumanMale30 character wearing the UMA 2 Bard jacket,
    /// converted with the profile being edited and rebuilt when that profile changes.
    /// </summary>
    public sealed class UMARigProfilePreviewTests
    {
        private const string SourceRacePath = "Assets/UMA2/Races/HumanMale/RaceData/Human Male.asset";
        private const string TargetRacePath = "Assets/UMA/UMA3/Races/HumanMale30.asset";
        private const string BardFolder = "Assets/Content/UMA RPG Starter 1/MB1 Bard refit";

        private RaceData targetRace;
        private SlotDataAsset jacket;
        private UMASlotRigConversionProfile profile;
        private UMARigProfilePreview preview;

        [SetUp]
        public void SetUp()
        {
            targetRace = AssetDatabase.LoadAssetAtPath<RaceData>(TargetRacePath);
            Assert.IsNotNull(targetRace, TargetRacePath);
            Assert.IsNotNull(targetRace.legacyRig, "HumanMale30 must name its legacy rig");
            foreach (string guid in AssetDatabase.FindAssets("t:SlotDataAsset", new[] { BardFolder }))
            {
                var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (slot != null && slot.slotName.Contains("Jacket"))
                {
                    jacket = slot;
                    break;
                }
            }
            Assert.IsNotNull(jacket, "the Bard jacket under " + BardFolder);
            profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            preview = new UMARigProfilePreview();
        }

        [TearDown]
        public void TearDown()
        {
            preview?.Dispose();
            if (profile != null) Object.DestroyImmediate(profile);
        }

        private static List<Vector3> GeneratedVertices(UMARigProfilePreview built)
        {
            var vertices = new List<Vector3>();
            foreach (SkinnedMeshRenderer renderer in built.Avatar.umaData.GetRenderers())
            {
                if (renderer != null && renderer.sharedMesh != null) vertices.AddRange(renderer.sharedMesh.vertices);
            }
            return vertices;
        }

        private static float MaxDifference(Vector3[] a, Vector3[] b)
        {
            Assert.AreEqual(a.Length, b.Length);
            float max = 0f;
            for (int i = 0; i < a.Length; i++) max = Mathf.Max(max, Vector3.Distance(a[i], b[i]));
            return max;
        }

        [Test]
        public void ChangingTheProfileConvertsTheSlotAgainAndRebuildsTheCharacter()
        {
            Assert.IsTrue(preview.Build(targetRace, profile, null, new[] { jacket }), string.Join(" | ", preview.Messages));
            Assert.IsTrue(preview.IsBuilt);
            Assert.IsTrue(preview.RestPose);
            Assert.AreEqual(1, preview.Slots.Count, string.Join(" | ", preview.Messages));
            UMARigProfilePreview.PreviewSlot worn = preview.Slots[0];
            Assert.IsTrue(worn.Converted, worn.Note);
            Assert.AreSame(jacket, worn.Original);
            Assert.AreNotSame(jacket, worn.Copy);
            Assert.AreSame(worn.Copy, worn.SlotData.asset);
            Assert.IsTrue(jacket.isLegacySlot, "the project asset is left alone");
            Assert.That(System.Array.IndexOf(preview.Avatar.umaData.umaRecipe.slotDataList, worn.SlotData), Is.GreaterThanOrEqualTo(0), "the character wears the jacket");

            // With an empty profile the copy is what a plain conversion gives.
            UMARigRestPose legacyRig = targetRace.legacyRig.GetRestPose();
            UMAMeshData plain = UMASlotRigConverter.Convert(jacket.meshData, legacyRig, preview.TargetRig, new UMASlotRigConversionOptions { Profile = profile });
            Assert.That(MaxDifference(plain.vertices, worn.Copy.meshData.vertices), Is.LessThan(1e-6f));
            List<Vector3> before = GeneratedVertices(preview);
            Assert.That(before.Count, Is.GreaterThan(jacket.meshData.vertexCount));

            // Push the lower back forward by 5 cm, as dragging its handle would.
            UMARigBoneCorrection entry = profile.GetOrAddCorrection("LowerBackAdjust");
            entry.positionOffset = new Vector3(0f, 0f, 0.05f);
            Assert.IsTrue(preview.Refresh());

            UMAMeshData adjusted = UMASlotRigConverter.Convert(jacket.meshData, legacyRig, preview.TargetRig, new UMASlotRigConversionOptions { Profile = profile });
            Assert.That(MaxDifference(adjusted.vertices, worn.Copy.meshData.vertices), Is.LessThan(1e-6f), "the copy holds the conversion with the edited profile");
            Assert.That(MaxDifference(plain.vertices, adjusted.vertices), Is.GreaterThan(0.01f), "the test edit must move the jacket");
            List<Vector3> after = GeneratedVertices(preview);
            Assert.AreEqual(before.Count, after.Count, "the rebuilt character has the same vertices");
            float moved = 0f;
            for (int i = 0; i < before.Count; i++) moved = Mathf.Max(moved, Vector3.Distance(before[i], after[i]));
            Assert.That(moved, Is.GreaterThan(0.01f), "the character's mesh must show the change");
            Assert.That(preview.LastBuildMilliseconds, Is.GreaterThan(0d));

            // The editor's helpers.
            CollectionAssert.Contains(preview.GetSkinnedBoneHashes(), UMAUtils.StringToHash("LowerBackAdjust"));
            Assert.IsNotNull(preview.GetBone(UMAUtils.StringToHash("Hips")));
            Assert.IsTrue(preview.TryGetBounds(out Bounds bounds));
            Assert.That(bounds.size.y, Is.GreaterThan(1f));
            preview.SetPose(false);
            Assert.IsFalse(preview.RestPose);
            preview.SetPose(true);

            // Closing the preview leaves nothing behind.
            GameObject avatarObject = preview.AvatarObject;
            SlotDataAsset copy = worn.Copy;
            preview.Dispose();
            Assert.IsFalse(preview.IsBuilt);
            Assert.IsTrue(avatarObject == null, "the preview character is destroyed");
            Assert.IsTrue(copy == null, "the converted copy is destroyed");
            Assert.IsFalse(preview.Refresh());
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator ThePreviewSurvivesTheEditorTicking()
        {
            // The preview character is an avatar component in the scene. Left to itself, an avatar
            // starts its own edit-time rebuild on the Editor's next tick, which would clear the
            // preview. Let the Editor tick, as it does while the window is open, and check that the
            // preview is untouched. An error or exception logged meanwhile fails the test.
            Assert.IsTrue(preview.Build(targetRace, profile, null, new[] { jacket }), string.Join(" | ", preview.Messages));
            UMARigProfilePreview.PreviewSlot worn = preview.Slots[0];
            int vertexCount = GeneratedVertices(preview).Count;
            Assert.That(vertexCount, Is.GreaterThan(jacket.meshData.vertexCount));

            for (int frame = 0; frame < 8; frame++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }

            Assert.IsTrue(preview.IsBuilt, "the preview character is gone after a few Editor frames");
            Assert.AreEqual(vertexCount, GeneratedVertices(preview).Count, "the character no longer wears the jacket");
            Assert.That(System.Array.IndexOf(preview.Avatar.umaData.umaRecipe.slotDataList, worn.SlotData), Is.GreaterThanOrEqualTo(0), "the jacket left the recipe");

            // A change made after those frames still shows, and still survives more frames.
            List<Vector3> before = GeneratedVertices(preview);
            profile.GetOrAddCorrection("LowerBackAdjust").positionOffset = new Vector3(0f, 0f, 0.05f);
            Assert.IsTrue(preview.Refresh());
            for (int frame = 0; frame < 4; frame++)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
            List<Vector3> after = GeneratedVertices(preview);
            Assert.AreEqual(before.Count, after.Count);
            float moved = 0f;
            for (int i = 0; i < before.Count; i++) moved = Mathf.Max(moved, Vector3.Distance(before[i], after[i]));
            Assert.That(moved, Is.GreaterThan(0.01f), "the change must still be visible after more Editor frames");
        }

        [Test]
        public void AnEffectorsReachIsMeasuredOnEverySlotWhateverItIsKeptTo()
        {
            Assert.IsTrue(preview.Build(targetRace, profile, null, new[] { jacket }), string.Join(" | ", preview.Messages));
            UMARigProfilePreview.PreviewSlot worn = preview.Slots[0];
            Assert.IsTrue(worn.Converted, worn.Note);
            Vector3[] plain = (Vector3[])worn.Copy.meshData.vertices.Clone();

            // A sphere around the left shoulder that throws what it holds 30 cm back, kept to a slot the jacket is not.
            var sphere = new UMARigEffector
            {
                name = "shoulder", boneName = "LeftArm", radius = 0.12f, falloff = 0.5f,
                action = UMARigEffectorAction.Move, move = new Vector3(0f, 0f, -0.3f),
            };
            profile.GetEffectorList("Some other slot", true).Add(sphere);
            Assert.IsTrue(preview.Refresh());
            Assert.That(MaxDifference(plain, worn.Copy.meshData.vertices), Is.LessThan(1e-6f), "kept to another slot, the sphere leaves the jacket alone");

            var reach = new List<UMARigProfilePreview.EffectorReach>();
            preview.MeasureEffector(sphere, reach);
            Assert.AreEqual(1, reach.Count, "one entry for each slot the preview converts");
            Assert.AreEqual(jacket.slotName, reach[0].SlotName);
            int inside = reach[0].Inside;
            Assert.That(inside, Is.GreaterThan(0), "the jacket passes through the sphere, and the measurement says so though the sphere does not act on it");

            // Let it act on all slots: the jacket moves, and the count stays what it was, because it
            // is taken before any effector has moved anything.
            Assert.IsTrue(profile.SetEffectorSlot(sphere, null));
            Assert.IsTrue(preview.Refresh());
            Assert.That(MaxDifference(plain, worn.Copy.meshData.vertices), Is.GreaterThan(0.25f), "acting on all slots, the sphere moves the jacket");
            preview.MeasureEffector(sphere, reach);
            Assert.AreEqual(inside, reach[0].Inside);

            sphere.mirror = true;
            preview.MeasureEffector(sphere, reach);
            Assert.That(reach[0].Inside, Is.GreaterThan(inside), "the mirror image holds the other shoulder");
            int both = reach[0].Inside;

            // A bone entry that lifts the left arm half a metre takes it out of the sphere. (Up, not
            // sideways: sideways would carry the sleeve through the mirror image on the other shoulder.)
            profile.GetOrAddCorrection("LeftShoulder").positionOffset = new Vector3(0f, 0.5f, 0f);
            preview.MeasureEffector(sphere, reach);
            Assert.That(reach[0].Inside, Is.LessThan(both), "the measurement follows the bone entries");

            preview.Dispose();
            preview.MeasureEffector(sphere, reach);
            Assert.AreEqual(0, reach.Count, "nothing to measure without a built preview");
        }

        [Test]
        public void TheVerticesAnEffectorHoldsAreListedWhereThePreviewShowsThem()
        {
            Assert.IsTrue(preview.Build(targetRace, profile, null, new[] { jacket }), string.Join(" | ", preview.Messages));
            int converted = preview.ConvertCount;

            // A sphere around the left shoulder that throws what it holds 30 cm back, acting on all slots.
            var sphere = new UMARigEffector
            {
                name = "shoulder", boneName = "LeftArm", radius = 0.12f, falloff = 0.5f,
                action = UMARigEffectorAction.Move, move = new Vector3(0f, 0f, -0.3f),
            };
            profile.effectors.Add(sphere);
            Assert.IsTrue(preview.Refresh());
            Assert.That(preview.ConvertCount, Is.GreaterThan(converted), "a refresh is a new conversion");
            sphere.GetFrame(preview.TargetRig, false, out Vector3 centre, out _);

            var reach = new List<UMARigProfilePreview.EffectorReach>();
            preview.MeasureEffector(sphere, reach);
            var held = new List<UMARigProfilePreview.EffectorVertex>();
            preview.CollectEffectorVertices(sphere, held);
            Assert.That(held.Count, Is.GreaterThan(0));
            Assert.AreEqual(reach[0].Inside, held.Count, "the list and the count are the same vertices");
            int full = 0;
            foreach (UMARigProfilePreview.EffectorVertex vertex in held)
            {
                Assert.IsTrue(vertex.ActedOn);
                Assert.That(vertex.Strength, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
                if (vertex.Strength >= 0.999f)
                {
                    full++;
                    // Held at full strength, it was within 6 cm of the centre and now stands 30 cm behind that.
                    Assert.That(Vector3.Distance(vertex.Position, centre), Is.GreaterThan(0.2f), "listed where the preview shows it, after the move");
                }
            }
            Assert.That(full, Is.GreaterThan(0), "the test sphere must hold some of the jacket at full strength");

            // Kept to another slot: the same vertices are listed, marked as left alone, back where they were.
            Assert.IsTrue(profile.SetEffectorSlot(sphere, "Some other slot"));
            Assert.IsTrue(preview.Refresh());
            var leftAlone = new List<UMARigProfilePreview.EffectorVertex>();
            preview.CollectEffectorVertices(sphere, leftAlone);
            Assert.AreEqual(held.Count, leftAlone.Count);
            foreach (UMARigProfilePreview.EffectorVertex vertex in leftAlone)
            {
                Assert.IsFalse(vertex.ActedOn);
                Assert.That(Vector3.Distance(vertex.Position, centre), Is.LessThan(0.12f + 1e-4f), "unmoved, so still inside the sphere");
            }

            // The mirror image's vertices are listed too.
            sphere.mirror = true;
            preview.CollectEffectorVertices(sphere, leftAlone);
            Assert.That(leftAlone.Count, Is.GreaterThan(held.Count));

            preview.Dispose();
            preview.CollectEffectorVertices(sphere, leftAlone);
            Assert.AreEqual(0, leftAlone.Count, "nothing to list without a built preview");
        }

        [Test]
        public void ARaceWithoutALegacyRigIsExplainedInsteadOfBuilt()
        {
            var sourceRace = AssetDatabase.LoadAssetAtPath<RaceData>(SourceRacePath);
            Assert.IsNotNull(sourceRace, SourceRacePath);

            Assert.IsFalse(preview.Build(sourceRace, profile, null, new[] { jacket }));
            Assert.IsFalse(preview.IsBuilt);
            Assert.That(preview.Messages, Has.Some.Contains("Legacy Rig"));

            Assert.IsFalse(preview.Build(null, profile, null, null));
            Assert.That(preview.Messages.Count, Is.GreaterThan(0));
        }
    }
}
