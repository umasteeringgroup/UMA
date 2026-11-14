using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// A "clear the body" effector on real data: the UMA 2 Bard pants on HumanMale30, whose seat lies
    /// under the UMA 3 skin after conversion. Nothing in the project is changed: the profile and the
    /// copy of the race live in memory only.
    /// </summary>
    public sealed class UMARigEffectorBardTests
    {
        private const string RacePath = "Assets/UMA/UMA3/Races/HumanMale30.asset";
        private const string PantsRecipePath = "Assets/Content/UMA RPG Starter 1/Wardrobe/Bard/BardPants.asset";

        private readonly List<Object> temp = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            UMALegacySlotConverter.ClearCache();
            for (int i = 0; i < temp.Count; i++)
            {
                if (temp[i] != null) Object.DestroyImmediate(temp[i]);
            }
            temp.Clear();
        }

        /// <summary>A sphere behind the pelvis that brings cloth out to 8 mm over the skin.</summary>
        private static UMARigEffector Seat()
        {
            return new UMARigEffector
            {
                name = "Seat",
                boneName = "Hips",
                shape = UMARigEffectorShape.Sphere,
                position = new Vector3(0f, -0.06f, -0.09f),
                radius = 0.2f,
                falloff = 0.5f,
                action = UMARigEffectorAction.ClearBody,
                amount = 0.008f,
                reach = 0.09f,
                smoothing = 2,
            };
        }

        private static SlotDataAsset FindPants(UMARigRestPose legacy, UMARigRestPose target)
        {
            var recipe = AssetDatabase.LoadAssetAtPath<UMATextRecipe>(PantsRecipePath);
            Assert.IsNotNull(recipe, PantsRecipePath);
            foreach (SlotData slot in recipe.GetCachedRecipe(true).slotDataList)
            {
                if (slot != null && slot.asset != null && UMALegacySlotConverter.IsBuiltForLegacyRig(slot.asset.meshData, legacy, target))
                {
                    return slot.asset;
                }
            }
            Assert.Fail("The pants recipe holds no slot built for the legacy rig.");
            return null;
        }

        [Test]
        public void ClearTheBodyBringsTheSeatOfThePantsOutAndLeavesTheFrontAlone()
        {
            var race = AssetDatabase.LoadAssetAtPath<RaceData>(RacePath);
            Assert.IsNotNull(race, RacePath);
            Assert.IsNotNull(race.legacyRig, "HumanMale30 needs its Legacy Rig for this test");
            Assert.IsTrue(UMARigRestPose.TryFromRace(race, out UMARigRestPose rig, out string reason), reason);
            UMARigRestPose legacy = race.legacyRig.GetRestPose();
            SlotDataAsset pants = FindPants(legacy, rig);

            List<UMAMeshHideAutoGenerator.BodySlot> bodySlots = UMAMeshHideAutoGenerator.CollectBodySlots(race, rig);
            UMARigBodySurface body = UMAMeshHideAutoGenerator.BuildBodySurface(bodySlots);
            Assert.That(body.TriangleCount, Is.GreaterThan(10000), "the HumanMale30 body");

            var profile = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            temp.Add(profile);
            UMARigEffector seat = Seat();
            profile.GetEffectorList(pants.slotName, true).Add(seat);

            UMAMeshData plainMesh = UMASlotRigConverter.Convert(pants.meshData, legacy, rig, new UMASlotRigConversionOptions { BodySurface = body });
            var report = new UMASlotRigConversionReport();
            UMAMeshData fittedMesh = UMASlotRigConverter.Convert(pants.meshData, legacy, rig,
                new UMASlotRigConversionOptions { Profile = profile, ProfileSlotName = pants.slotName, BodySurface = body }, report);
            Assert.IsTrue(UMARigBodySurface.TryBakeToAvatar(plainMesh, rig, out Vector3[] plain, out _));
            Assert.IsTrue(UMARigBodySurface.TryBakeToAvatar(fittedMesh, rig, out Vector3[] fitted, out Vector3[] fittedNormals));

            rig.TryGetAvatarPosition(UMAUtils.StringToHash("Hips"), out Vector3 hips);
            seat.GetFrame(rig, false, out Vector3 centre, out _);
            float core = seat.radius * (1f - seat.falloff);

            int buriedBefore = 0, buriedAfter = 0, moved = 0, frontVertices = 0, outsideVertices = 0;
            float frontMove = 0f, outsideMove = 0f, deepestBefore = 0f, largestMove = 0f;
            for (int v = 0; v < plain.Length; v++)
            {
                float move = Vector3.Distance(plain[v], fitted[v]);
                largestMove = Mathf.Max(largestMove, move);
                if (move > 1e-6f) moved++;
                if (plain[v].z > hips.z + 0.02f)
                {
                    frontVertices++;
                    frontMove = Mathf.Max(frontMove, move);
                }
                if (Vector3.Distance(plain[v], centre) >= seat.radius)
                {
                    outsideVertices++;
                    outsideMove = Mathf.Max(outsideMove, move);
                }
                if (Vector3.Distance(plain[v], centre) > core)
                {
                    continue;
                }
                // Inside the effector's core, where it acts at full strength.
                if (body.TryGetClosest(plain[v], seat.reach, out Vector3 closest, out Vector3 normal))
                {
                    float gap = Vector3.Dot(plain[v] - closest, normal);
                    if (gap < 0f)
                    {
                        buriedBefore++;
                        deepestBefore = Mathf.Max(deepestBefore, -gap);
                        if (body.TryGetClosest(fitted[v], seat.reach, out closest, out normal) && Vector3.Dot(fitted[v] - closest, normal) < 0f)
                        {
                            buriedAfter++;
                        }
                    }
                }
            }
            Debug.Log($"[UMARigEffectorBardTests] '{pants.slotName}': {buriedBefore} vertices under the skin in the effector's core (deepest {deepestBefore * 100f:F1} cm), " +
                      $"{buriedAfter} after; {moved} of {plain.Length} vertices moved, the furthest {largestMove * 100f:F1} cm; in front of the hips: {frontVertices} vertices, largest move {frontMove * 1000f:F3} mm; " +
                      $"outside the effector: {outsideVertices} vertices, largest move {outsideMove * 1000f:F3} mm");

            Assert.That(buriedBefore, Is.GreaterThanOrEqualTo(5), "the test relies on the seat of these pants lying under the UMA 3 skin");
            Assert.AreEqual(0, buriedAfter, "every seat vertex the effector reaches at full strength is out of the body");
            Assert.That(outsideVertices, Is.GreaterThan(100));
            Assert.AreEqual(0f, outsideMove, "nothing outside the effector's radius is touched");
            // The cloth between the vertices is clear too: a coarse garment can have every vertex over
            // the skin and still cut through a rounded body with the flat of its triangles.
            int middlesUnderBefore = 0, middlesUnderAfter = 0;
            foreach (SubMeshTriangles submesh in pants.meshData.submeshes)
            {
                int[] list = submesh.getManagedTriangles(0);
                for (int t = 0; t + 2 < list.Length; t += 3)
                {
                    Vector3 middleBefore = (plain[list[t]] + plain[list[t + 1]] + plain[list[t + 2]]) / 3f;
                    if (Vector3.Distance(middleBefore, centre) > core)
                    {
                        continue;
                    }
                    if (body.TryGetClosest(middleBefore, 0.04f, out Vector3 closest, out Vector3 normal) && Vector3.Dot(middleBefore - closest, normal) < 0f)
                    {
                        middlesUnderBefore++;
                    }
                    Vector3 middleAfter = (fitted[list[t]] + fitted[list[t + 1]] + fitted[list[t + 2]]) / 3f;
                    if (body.TryGetClosest(middleAfter, 0.04f, out closest, out normal) && Vector3.Dot(middleAfter - closest, normal) < 0f)
                    {
                        middlesUnderAfter++;
                    }
                }
            }
            Debug.Log($"[UMARigEffectorBardTests] triangle middles under the skin in the core: {middlesUnderBefore} before, {middlesUnderAfter} after");
            Assert.That(middlesUnderBefore, Is.GreaterThan(0));
            Assert.AreEqual(0, middlesUnderAfter, "no triangle in the effector's core has its middle under the skin");
            Assert.That(largestMove, Is.LessThan(seat.reach + seat.amount + 0.07f), "nothing is thrown far from where it was");
            Assert.AreEqual(moved, report.EffectorMovedVertices);
            Assert.That(report.EffectorMaxMove, Is.GreaterThan(0.01f));
            Assert.AreEqual(largestMove, report.EffectorMaxMove, 1e-4f);
            for (int v = 0; v < fittedNormals.Length; v++)
            {
                Assert.AreEqual(1f, fittedNormals[v].magnitude, 1e-3f, "normal " + v);
            }

            // Automatic conversion gives a character of the race the same pants: it builds the body
            // surface itself, from the body slots the character wears.
            RaceData raceCopy = Object.Instantiate(race);
            raceCopy.hideFlags = HideFlags.HideAndDontSave;
            raceCopy.legacyRigProfile = profile;
            temp.Add(raceCopy);
            Assume.That(pants.isLegacySlot, "automatic conversion only takes slots flagged as legacy");

            var avatarObject = new GameObject("EffectorTestAvatar") { hideFlags = HideFlags.HideAndDontSave };
            temp.Add(avatarObject);
            var avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
            avatar.editorTimeGeneration = false;
            avatar.showPlaceholder = false;
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;
            avatar.predefinedDNA = new UMAPredefinedDNA();
            avatar.GenerateNow();
            avatar.enabled = false;
            try
            {
                var worn = new List<SlotData>(avatar.umaData.umaRecipe.slotDataList) { new SlotData(pants) };
                SlotData[] slots = worn.ToArray();
                Assert.AreEqual(1, UMALegacySlotConverter.ConvertLegacySlots(slots, raceCopy));
                SlotDataAsset converted = slots[slots.Length - 1].asset;
                Assert.AreNotSame(pants, converted);
                temp.Add(converted);
                Assert.IsTrue(UMARigBodySurface.TryBakeToAvatar(converted.meshData, rig, out Vector3[] automatic, out _));
                float worst = 0f;
                for (int v = 0; v < fitted.Length; v++)
                {
                    worst = Mathf.Max(worst, Vector3.Distance(fitted[v], automatic[v]));
                }
                Assert.That(worst, Is.LessThan(1e-4f), "automatic conversion applies the same effector against the same body");
            }
            finally
            {
                try { avatar.CleanupGeneratedData(); } catch { }
            }
        }
    }
}
