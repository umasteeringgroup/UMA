using System;
using System.Collections.Generic;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// Converts the UMA 2 "MB1 Bard refit" slots (authored for HumanMale) to the UMA 3 HumanMale30
    /// rig and checks the results against the real races: baked positions, weights, serialization,
    /// and a generated HumanMale30 character wearing the converted jacket.
    /// </summary>
    public sealed class UMASlotRigConverterBardTests
    {
        private const string SourceRacePath = "Assets/UMA2/Races/HumanMale/RaceData/Human Male.asset";
        private const string TargetRacePath = "Assets/UMA/UMA3/Races/HumanMale30.asset";
        private const string BardFolder = "Assets/Content/UMA RPG Starter 1/MB1 Bard refit";
        private const string TempFolder = "Assets/UMA_RigConversionTestTemp";
        private const float ExactTolerance = 1e-4f;      // metres, over real data with ~2 m coordinates
        // Metres. Joints differ by up to ~7.5 cm between the two bodies, but no Bard slot's vertices
        // move further than 5.4 cm with them (measured: Gloves 5.38, Jacket 5.25, Pants 4.92).
        // Anything larger means the correction misfired.
        private const float CorrectedTolerance = 0.06f;
        // A bone whose direction changed rotates everything it carries about its joint, so a prop held
        // far from its bone moves further: the sword's blade tip, about 1 m from the hand, moves
        // 10.4 cm for a 6 degree change.
        private const float LongPropTolerance = 0.12f;

        private static float CorrectedToleranceFor(SlotDataAsset slot)
        {
            return slot.slotName.Contains("Sword") ? LongPropTolerance : CorrectedTolerance;
        }

        private RaceData sourceRace;
        private RaceData targetRace;
        private UMARigRestPose sourceRig;
        private UMARigRestPose targetRig;
        private List<SlotDataAsset> bardSlots;

        [SetUp]
        public void SetUp()
        {
            sourceRace = AssetDatabase.LoadAssetAtPath<RaceData>(SourceRacePath);
            targetRace = AssetDatabase.LoadAssetAtPath<RaceData>(TargetRacePath);
            Assert.IsNotNull(sourceRace, SourceRacePath);
            Assert.IsNotNull(targetRace, TargetRacePath);
            sourceRig = UMARigRestPose.FromRace(sourceRace);
            targetRig = UMARigRestPose.FromRace(targetRace);

            bardSlots = new List<SlotDataAsset>();
            foreach (string guid in AssetDatabase.FindAssets("t:SlotDataAsset", new[] { BardFolder }))
            {
                var slot = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (slot != null && !UMAMeshData.IsNullOrEmptyMeshData(slot.meshData))
                {
                    bardSlots.Add(slot);
                }
            }
            bardSlots.Sort((a, b) => string.CompareOrdinal(a.slotName, b.slotName));
            Assert.That(bardSlots.Count, Is.GreaterThan(0), "no Bard slots with mesh data under " + BardFolder);
            Assert.IsNotNull(bardSlots.Find(s => s.slotName.Contains("Jacket")), "the Bard jacket is used by several tests");
        }

        [TearDown]
        public void TearDown()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                return;
            }

            // Drop any index entries that point into the temp folder before the assets go away, so
            // the global library never keeps a dangling reference to a test asset.
            UMAAssetIndexer index = UMAAssetIndexer.Instance;
            if (index != null)
            {
                bool removedAny = false;
                foreach (string guid in AssetDatabase.FindAssets("", new[] { TempFolder }))
                {
                    var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset == null || !index.IsIndexedType(asset.GetType()))
                    {
                        continue;
                    }
                    string evilName = AssetItem.GetEvilName(asset);
                    AssetItem item = index.GetAssetItem(asset.GetType(), evilName);
                    if (item != null && item._Path != null && item._Path.StartsWith(TempFolder + "/", StringComparison.Ordinal))
                    {
                        index.RemoveAsset(asset.GetType(), evilName, false);
                        removedAny = true;
                    }
                }
                if (removedAny)
                {
                    index.RemoveAssetsComplete();
                }
            }

            AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void RestPosesMatchTheGeneratorsRigPlacement()
        {
            // The two rigs stand upright at the origin in avatar space: hips around 1 m up, head around 1.8 m.
            Assert.IsTrue(sourceRig.TryGetAvatarPosition(UMAUtils.StringToHash("Hips"), out Vector3 sourceHips));
            Assert.IsTrue(targetRig.TryGetAvatarPosition(UMAUtils.StringToHash("Hips"), out Vector3 targetHips));
            Assert.IsTrue(sourceRig.TryGetAvatarPosition(UMAUtils.StringToHash("Head"), out Vector3 sourceHead));
            Assert.IsTrue(targetRig.TryGetAvatarPosition(UMAUtils.StringToHash("Head"), out Vector3 targetHead));
            Assert.That(sourceHips.y, Is.InRange(0.9f, 1.2f), "source hips height " + sourceHips);
            Assert.That(targetHips.y, Is.InRange(0.9f, 1.2f), "target hips height " + targetHips);
            Assert.That(sourceHead.y, Is.InRange(1.6f, 1.9f), "source head height " + sourceHead);
            Assert.That(targetHead.y, Is.InRange(1.6f, 1.9f), "target head height " + targetHead);
            Assert.That(Mathf.Abs(sourceHips.x), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(targetHips.x), Is.LessThan(0.01f));
            Assert.AreEqual(155, sourceRig.BoneCount);
            Assert.AreEqual(155, targetRig.BoneCount);

            // The target body slots agree on one mesh space, and so do the source body slots.
            targetRig.GetCanonicalMeshToAvatar(1e-4f, out int targetMismatches, out float targetMax);
            sourceRig.GetCanonicalMeshToAvatar(1e-3f, out int sourceMismatches, out float sourceMax);
            Assert.AreEqual(0, targetMismatches, "target donors max mismatch " + targetMax);
            Assert.That(sourceMismatches, Is.LessThanOrEqualTo(1), "source donors max mismatch " + sourceMax);
        }

        [Test]
        public void EveryBardSlotConvertsExactlyWithoutLosingWeights()
        {
            var summary = new System.Text.StringBuilder();
            foreach (SlotDataAsset slot in bardSlots)
            {
                var report = new UMASlotRigConversionReport();
                UMAMeshData converted = UMASlotRigConverter.Convert(slot.meshData, sourceRig, targetRig, UMASlotRigConversionOptions.Exact, report);

                Assert.AreEqual(0, report.WeightsMovedToAncestor, slot.slotName + ": every Bard bone exists on HumanMale30");
                Assert.AreEqual(0, report.WeightsMovedToRoot, slot.slotName);
                Assert.AreEqual(0, report.CarriedBones, slot.slotName);
                Assert.AreEqual(0, report.SourceInfluencesWithoutRestBone, slot.slotName);
                Assert.AreEqual(slot.meshData.vertexCount, converted.vertexCount, slot.slotName);
                Assert.AreEqual(targetRig.RootBoneHash, converted.rootBoneHash, slot.slotName);
                Assert.IsNull(converted.boneWeights, slot.slotName);
                Assert.AreEqual(converted.boneNameHashes.Length, converted.bindPoses.Length, slot.slotName);
                Assert.AreEqual(new HashSet<int>(converted.boneNameHashes).Count, converted.boneNameHashes.Length, slot.slotName + ": one bind pose per bone");
                foreach (int hash in converted.boneNameHashes)
                {
                    Assert.IsTrue(targetRig.Contains(hash), slot.slotName + ": bone not on target rig " + hash);
                    Assert.IsTrue(Array.Exists(converted.umaBones, b => b.hash == hash) || hash == targetRig.RootBoneHash, slot.slotName + ": umaBones must define every skinned bone");
                }

                float maxVertexDelta = CompareBaked(slot.meshData, sourceRig, converted, targetRig, out Bounds original, out Bounds result);
                Assert.That(maxVertexDelta, Is.LessThanOrEqualTo(ExactTolerance), slot.slotName + ": exact conversion must keep every vertex in place");
                Assert.That(Vector3.Distance(original.min, result.min), Is.LessThanOrEqualTo(ExactTolerance), slot.slotName + " bounds min");
                Assert.That(Vector3.Distance(original.max, result.max), Is.LessThanOrEqualTo(ExactTolerance), slot.slotName + " bounds max");
                summary.AppendLine($"{slot.slotName}: {report.SourceInfluenceCount} influences -> {report.TargetBoneCount} bones, max vertex delta {maxVertexDelta:E2}, bounds {original.min:F3}..{original.max:F3}");
            }
            Debug.Log("[UMASlotRigConverterBardTests] Exact conversion\n" + summary);
        }

        [Test]
        public void EveryBardSlotConvertsWithJointCorrectionWithinTolerance()
        {
            var summary = new System.Text.StringBuilder();
            foreach (SlotDataAsset slot in bardSlots)
            {
                var report = new UMASlotRigConversionReport();
                UMAMeshData converted = UMASlotRigConverter.Convert(slot.meshData, sourceRig, targetRig, UMASlotRigConversionOptions.Default, report);

                Assert.AreEqual(0, report.WeightsMovedToAncestor, slot.slotName);
                Assert.AreEqual(0, report.Warnings.Count, slot.slotName + ": " + string.Join(" | ", report.Warnings));
                float maxVertexDelta = CompareBaked(slot.meshData, sourceRig, converted, targetRig, out Bounds original, out Bounds result);
                float tolerance = CorrectedToleranceFor(slot);
                Assert.That(maxVertexDelta, Is.LessThanOrEqualTo(tolerance), slot.slotName + ": corrected vertices stay near the original");
                Assert.That(Vector3.Distance(original.min, result.min), Is.LessThanOrEqualTo(tolerance), slot.slotName + " bounds min");
                Assert.That(Vector3.Distance(original.max, result.max), Is.LessThanOrEqualTo(tolerance), slot.slotName + " bounds max");
                Assert.That(report.MaxJointDelta, Is.LessThan(0.1f), slot.slotName + " joint delta at " + report.MaxJointDeltaBone);
                summary.AppendLine($"{slot.slotName}: max vertex move {maxVertexDelta:F4} m, max joint delta {report.MaxJointDelta:F4} m at {report.MaxJointDeltaBone}");
            }
            Debug.Log("[UMASlotRigConverterBardTests] Joint-anchored conversion\n" + summary);
        }

        [Test]
        public void ConvertedSlotSavesAndReloadsAsANewAsset()
        {
            SlotDataAsset jacket = bardSlots.Find(s => s.slotName.Contains("Jacket"));
            Assert.IsNotNull(jacket);
            string sourcePath = AssetDatabase.GetAssetPath(jacket);
            long sourceTicks = System.IO.File.GetLastWriteTimeUtc(sourcePath).Ticks;

            SlotDataAsset saved = UMASlotRigConverterEditorUtility.ConvertAndSave(jacket, sourceRig, targetRig, UMASlotRigConversionOptions.Default,
                "_HumanMale30", TempFolder, addToIndex: false, out UMASlotRigConversionReport report);
            AssetDatabase.SaveAssets();

            string savedPath = AssetDatabase.GetAssetPath(saved);
            StringAssert.StartsWith(TempFolder + "/", savedPath);
            Assert.AreEqual(jacket.slotName + "_HumanMale30", saved.slotName);
            Assert.AreEqual(jacket.slotName + "_HumanMale30", report.SlotName);
            Assert.IsFalse(saved.isLegacySlot);
            Assert.IsTrue(jacket.isLegacySlot, "the source stays a legacy slot");
            Assert.AreEqual(sourceTicks, System.IO.File.GetLastWriteTimeUtc(sourcePath).Ticks, "the source asset file must not be rewritten");

            var reloaded = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(savedPath);
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(jacket.meshData.vertexCount, reloaded.meshData.vertexCount);
            Assert.AreEqual(report.TargetBoneCount, reloaded.meshData.bindPoses.Length);
            Assert.AreEqual(targetRig.RootBoneName, reloaded.meshData.RootBoneName);
            Assert.AreEqual(jacket.slotGroup, reloaded.slotGroup, "slot settings carry over");
            Assert.AreEqual(jacket.overlayScale, reloaded.overlayScale, "slot settings carry over");
            CollectionAssert.AreEqual(jacket.tags ?? Array.Empty<string>(), reloaded.tags ?? Array.Empty<string>(), "slot settings carry over");
            Assert.That(reloaded.meshData.ManagedBoneWeights.Length, Is.GreaterThan(0));
        }

        [Test]
        public void ConvertingAgainReplacesTheEarlierAssetInPlace()
        {
            SlotDataAsset jacket = bardSlots.Find(s => s.slotName.Contains("Jacket"));

            SlotDataAsset first = UMASlotRigConverterEditorUtility.ConvertAndSave(jacket, sourceRig, targetRig, UMASlotRigConversionOptions.Exact,
                "_HumanMale30", TempFolder, addToIndex: false, out _);
            AssetDatabase.SaveAssets();
            string firstPath = AssetDatabase.GetAssetPath(first);
            string firstGuid = AssetDatabase.AssetPathToGUID(firstPath);
            Vector3 exactVertex = first.meshData.vertices[0];

            // A second run with other settings writes into the same asset: one file, one GUID, one slot name.
            SlotDataAsset second = UMASlotRigConverterEditorUtility.ConvertAndSave(jacket, sourceRig, targetRig, UMASlotRigConversionOptions.Default,
                "_HumanMale30", TempFolder, addToIndex: false, out UMASlotRigConversionReport secondReport);
            AssetDatabase.SaveAssets();
            Assert.AreEqual(firstPath, AssetDatabase.GetAssetPath(second));
            Assert.AreEqual(firstGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(second)));
            Assert.AreEqual(1, AssetDatabase.FindAssets("t:SlotDataAsset", new[] { TempFolder }).Length);
            Assert.AreEqual(0, secondReport.Warnings.Count, string.Join(" | ", secondReport.Warnings));
            var reloaded = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(firstPath);
            Assert.AreEqual(jacket.slotName + "_HumanMale30", reloaded.slotName);
            Assert.IsFalse(reloaded.isLegacySlot);
            Assert.That(Vector3.Distance(exactVertex, reloaded.meshData.vertices[0]), Is.GreaterThan(1e-4f), "the asset now holds the second, joint-corrected conversion");
            UMAMeshData expected = UMASlotRigConverter.Convert(jacket.meshData, sourceRig, targetRig, UMASlotRigConversionOptions.Default);
            Assert.That(Vector3.Distance(expected.vertices[0], reloaded.meshData.vertices[0]), Is.LessThan(1e-6f));
            Assert.AreEqual(expected.bindPoses.Length, reloaded.meshData.bindPoses.Length);

            // Asked not to replace, it creates a second asset and says that the slot name is now duplicated.
            SlotDataAsset third = UMASlotRigConverterEditorUtility.ConvertAndSave(jacket, sourceRig, targetRig, UMASlotRigConversionOptions.Default,
                "_HumanMale30", TempFolder, addToIndex: false, out UMASlotRigConversionReport thirdReport, replaceExisting: false);
            AssetDatabase.SaveAssets();
            Assert.AreNotEqual(firstPath, AssetDatabase.GetAssetPath(third));
            Assert.AreEqual(2, AssetDatabase.FindAssets("t:SlotDataAsset", new[] { TempFolder }).Length);
            Assert.That(thirdReport.Warnings, Has.Some.Contains("same slot name"));
        }

        [Test]
        public void SlotsAlreadyOnTheTargetRigAreRecognisedAndRefused()
        {
            SlotDataAsset nativeSlot = null;
            foreach (SlotData slotData in targetRace.baseRaceRecipe.GetCachedRecipe(loadSlots: true).GetAllSlots())
            {
                if (slotData != null && slotData.asset != null && UMASlotRigConverter.CanConvert(slotData.asset.meshData, out _))
                {
                    nativeSlot = slotData.asset;
                    break;
                }
            }
            Assert.IsNotNull(nativeSlot, "HumanMale30 has no skinned base slot");

            Assert.IsTrue(UMASlotRigConverterEditorUtility.IsAlreadyOnRig(nativeSlot, targetRig), nativeSlot.slotName + " is a HumanMale30 body slot");
            Assert.Throws<InvalidOperationException>(() => UMASlotRigConverterEditorUtility.ConvertAndSave(nativeSlot, sourceRig, targetRig,
                UMASlotRigConversionOptions.Default, "_HumanMale30", TempFolder, addToIndex: false, out _));
            Assert.IsFalse(AssetDatabase.IsValidFolder(TempFolder) && AssetDatabase.FindAssets("t:SlotDataAsset", new[] { TempFolder }).Length > 0, "nothing may be written for a refused slot");

            foreach (SlotDataAsset slot in bardSlots)
            {
                Assert.IsFalse(UMASlotRigConverterEditorUtility.IsAlreadyOnRig(slot, targetRig), slot.slotName + " is a HumanMale slot and must not pass as HumanMale30");
                UMAMeshData converted = UMASlotRigConverter.Convert(slot.meshData, sourceRig, targetRig, UMASlotRigConversionOptions.Default);
                Assert.IsTrue(UMASlotRigConverter.IsBoundToRigMeshSpace(converted, targetRig, 1e-4f, out float difference), slot.slotName + " converted, max difference " + difference);
            }
        }

        // --- against Unity's own skinning on generated skeletons ---------------------------------

        /// <summary>
        /// Generates a base character of <paramref name="race"/>, checks that its skeleton is the one
        /// the rest-pose model describes, and leaves its bones in the base slots' rest pose (the
        /// generator itself leaves them in the race T-pose).
        /// </summary>
        private static DynamicCharacterAvatar BuildRestPoseAvatar(RaceData race, UMARigRestPose rig, List<Object> cleanup)
        {
            var avatarObject = new GameObject("UMA_RigConversionTest_" + race.raceName) { hideFlags = HideFlags.HideAndDontSave };
            cleanup.Add(avatarObject);
            var avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
            avatar.hideFlags = HideFlags.HideAndDontSave;
            avatar.editorTimeGeneration = true;
            avatar.activeRace.name = race.raceName;
            avatar.activeRace.data = race;
            avatar.GenerateNow();
            Assert.IsNotNull(avatar.umaData, race.raceName + ": GenerateNow produced no UMAData");
            UMASkeleton skeleton = avatar.umaData.skeleton;

            // The rig root is where the model says the generator puts it.
            Matrix4x4 toAvatar = avatarObject.transform.worldToLocalMatrix;
            Assert.IsTrue(skeleton.HasBone(rig.RootBoneHash), race.raceName + ": no rig root bone");
            Transform rigRoot = skeleton.GetBoneTransform(rig.RootBoneHash);
            Assert.That(UMARigRestPose.MaxAbsDifference(toAvatar * rigRoot.localToWorldMatrix, rig.RigRootToAvatar), Is.LessThan(1e-5f),
                race.raceName + ": generated rig root differs from UMARigRestPose.RigRootToAvatar");

            // Every bone exists under the parent the model expects; then take the rest pose.
            foreach (KeyValuePair<int, UMATransform> entry in rig.Bones)
            {
                Assert.IsTrue(skeleton.HasBone(entry.Key), race.raceName + ": generated skeleton lacks " + entry.Value.name);
                Transform bone = skeleton.GetBoneTransform(entry.Key);
                string expectedParent = rig.Bones.TryGetValue(entry.Value.parent, out UMATransform parent) ? parent.name : rig.RootBoneName;
                Assert.AreEqual(expectedParent, bone.parent != null ? bone.parent.name : null, race.raceName + ": parent of " + entry.Value.name);
                bone.localPosition = entry.Value.position;
                bone.localRotation = entry.Value.rotation;
                bone.localScale = entry.Value.scale;
            }
            float worst = 0f;
            foreach (KeyValuePair<int, UMATransform> entry in rig.Bones)
            {
                rig.TryGetAvatarMatrix(entry.Key, out Matrix4x4 model);
                worst = Mathf.Max(worst, UMARigRestPose.MaxAbsDifference(toAvatar * skeleton.GetBoneTransform(entry.Key).localToWorldMatrix, model));
            }
            Assert.That(worst, Is.LessThan(1e-4f), race.raceName + ": rest pose on the generated skeleton differs from the model");
            return avatar;
        }

        /// <summary>Skins mesh data with a plain SkinnedMeshRenderer bound to the avatar's generated bones; results are in avatar space.</summary>
        private static void SkinOnAvatar(DynamicCharacterAvatar avatar, UMAMeshData md, List<Object> cleanup,
            out Vector3[] positions, out Vector3[] normals, out Vector4[] tangents)
        {
            UMASkeleton skeleton = avatar.umaData.skeleton;
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            cleanup.Add(mesh);
            mesh.vertices = md.vertices;
            if (md.normals != null && md.normals.Length == md.vertexCount) mesh.normals = md.normals;
            if (md.tangents != null && md.tangents.Length == md.vertexCount) mesh.tangents = md.tangents;
            mesh.SetIndices(md.submeshes[0].getManagedTriangles(0), MeshTopology.Triangles, 0);
            var perVertex = new Unity.Collections.NativeArray<byte>(md.ManagedBonesPerVertex, Unity.Collections.Allocator.Temp);
            var weights = new Unity.Collections.NativeArray<BoneWeight1>(md.ManagedBoneWeights, Unity.Collections.Allocator.Temp);
            mesh.SetBoneWeights(perVertex, weights);
            perVertex.Dispose();
            weights.Dispose();
            mesh.bindposes = md.bindPoses;

            var bones = new Transform[md.boneNameHashes.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                Assert.IsTrue(skeleton.HasBone(md.boneNameHashes[i]), md.SlotName + ": generated skeleton lacks the bone of influence " + i);
                bones[i] = skeleton.GetBoneTransform(md.boneNameHashes[i]);
            }

            var holder = new GameObject("RigConversionTestRenderer") { hideFlags = HideFlags.HideAndDontSave };
            cleanup.Add(holder);
            holder.transform.SetParent(avatar.transform, false);
            var renderer = holder.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = bones;
            renderer.quality = SkinQuality.Bone4;
            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            cleanup.Add(baked);
            renderer.BakeMesh(baked, true);
            positions = baked.vertices;
            normals = baked.normals;
            tangents = baked.tangents;
        }

        [Test]
        public void ConvertedSlotsSkinLikeTheOriginalsOnGeneratedSkeletons()
        {
            // Nothing here uses the converter's own rig model to decide what is right: Unity skins the
            // original on a generated HumanMale skeleton and the converted slot on a generated
            // HumanMale30 skeleton, and the two results are compared.
            var cleanup = new List<Object>();
            DynamicCharacterAvatar sourceAvatar = null;
            DynamicCharacterAvatar targetAvatar = null;
            var summary = new System.Text.StringBuilder();
            try
            {
                sourceAvatar = BuildRestPoseAvatar(sourceRace, sourceRig, cleanup);
                targetAvatar = BuildRestPoseAvatar(targetRace, targetRig, cleanup);

                foreach (SlotDataAsset slot in bardSlots)
                {
                    UMAMeshData original = slot.meshData;
                    int maxInfluences = 0;
                    foreach (byte count in original.ManagedBonesPerVertex) maxInfluences = Mathf.Max(maxInfluences, count);
                    Assert.That(maxInfluences, Is.LessThanOrEqualTo(4), slot.slotName + ": the comparison skins with four bones per vertex");

                    UMAMeshData exact = UMASlotRigConverter.Convert(original, sourceRig, targetRig, UMASlotRigConversionOptions.Exact);
                    UMAMeshData corrected = UMASlotRigConverter.Convert(original, sourceRig, targetRig, UMASlotRigConversionOptions.Default);

                    SkinOnAvatar(sourceAvatar, original, cleanup, out Vector3[] p0, out Vector3[] n0, out Vector4[] t0);
                    SkinOnAvatar(targetAvatar, exact, cleanup, out Vector3[] p1, out Vector3[] n1, out Vector4[] t1);
                    SkinOnAvatar(targetAvatar, corrected, cleanup, out Vector3[] p2, out _, out _);
                    Vector3[] correctedModel = BakeToAvatar(corrected, targetRig);

                    float maxPosition = 0f, maxNormal = 0f, maxTangent = 0f, maxCorrectedVsModel = 0f;
                    int handednessChanges = 0;
                    bool hasNormals = n0.Length == p0.Length && n1.Length == p1.Length;
                    bool hasTangents = t0.Length == p0.Length && t1.Length == p1.Length;
                    for (int v = 0; v < p0.Length; v++)
                    {
                        maxPosition = Mathf.Max(maxPosition, Vector3.Distance(p0[v], p1[v]));
                        maxCorrectedVsModel = Mathf.Max(maxCorrectedVsModel, Vector3.Distance(p2[v], correctedModel[v]));
                        if (hasNormals) maxNormal = Mathf.Max(maxNormal, Vector3.Angle(n0[v], n1[v]));
                        if (hasTangents)
                        {
                            maxTangent = Mathf.Max(maxTangent, Vector3.Angle((Vector3)t0[v], (Vector3)t1[v]));
                            if (t0[v].w != t1[v].w) handednessChanges++;
                        }
                    }
                    summary.AppendLine($"{slot.slotName}: {p0.Length} verts, position {maxPosition:E2} m, normal {maxNormal:F4} deg, tangent {maxTangent:F4} deg, corrected vs model {maxCorrectedVsModel:E2} m");

                    Assert.That(maxPosition, Is.LessThanOrEqualTo(ExactTolerance), slot.slotName + ": Unity draws the converted slot somewhere else");
                    Assert.That(maxNormal, Is.LessThanOrEqualTo(0.1f), slot.slotName + ": Unity lights the converted slot differently (degrees)");
                    Assert.That(maxTangent, Is.LessThanOrEqualTo(0.1f), slot.slotName + ": tangents differ (degrees)");
                    Assert.AreEqual(0, handednessChanges, slot.slotName + ": tangent handedness changed");
                    Assert.That(maxCorrectedVsModel, Is.LessThanOrEqualTo(ExactTolerance), slot.slotName + ": the rig model disagrees with Unity for the joint-corrected slot");
                }
            }
            finally
            {
                foreach (DynamicCharacterAvatar avatar in new[] { sourceAvatar, targetAvatar })
                {
                    if (avatar != null)
                    {
                        try { avatar.CleanupGeneratedData(); } catch { }
                    }
                }
                foreach (Object o in cleanup)
                {
                    if (o != null) Object.DestroyImmediate(o);
                }
            }
            Debug.Log("[UMASlotRigConverterBardTests] Unity-skinned on generated skeletons, original (HumanMale) vs converted (HumanMale30)\n" + summary);
        }

        [Test]
        public void ConvertedJacketWearsOnHumanMale30WithoutBoneWarnings()
        {
            UMATestReport smoke = UMARaceSmokeTestRunner.Run(targetRace);
            Assert.IsFalse(smoke.HasErrors, "HumanMale30 must build cleanly before wearing converted clothing: " + Describe(smoke));

            SlotDataAsset jacket = bardSlots.Find(s => s.slotName.Contains("Jacket"));
            SlotDataAsset converted = Object.Instantiate(jacket);
            converted.hideFlags = HideFlags.HideAndDontSave;
            converted.name = jacket.name + "_HumanMale30";
            converted.isLegacySlot = false;
            converted.meshData = UMASlotRigConverter.Convert(jacket.meshData, sourceRig, targetRig, UMASlotRigConversionOptions.Default);
            converted.meshData.SlotName = converted.name;

            OverlayDataAsset overlay = FindBardOverlay("Jacket") ?? FindBardOverlay(null);
            Assert.IsNotNull(overlay, "a Bard overlay is needed to render the jacket");

            UMAGenerator generator = UMAAssetIndexer.Instance.Generator;
            Assert.IsNotNull(generator, "UMAAssetIndexer.Generator");

            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type == LogType.Warning || type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    logs.Add(type + ": " + message);
                }
            };

            GameObject avatarObject = null;
            DynamicCharacterAvatar avatar = null;
            try
            {
                avatarObject = new GameObject("UMA_RigConversionTest_HumanMale30") { hideFlags = HideFlags.HideAndDontSave };
                avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
                avatar.hideFlags = HideFlags.HideAndDontSave;
                avatar.editorTimeGeneration = true;
                avatar.activeRace.name = targetRace.raceName;
                avatar.activeRace.data = targetRace;
                avatar.GenerateNow();

                UMAData umaData = avatar.umaData;
                Assert.IsNotNull(umaData, "GenerateNow produced no UMAData");
                SkinnedMeshRenderer[] renderers = umaData.GetRenderers();
                Assert.IsNotNull(renderers);
                Assert.That(renderers.Length, Is.GreaterThan(0));
                int bodyVertexCount = 0;
                foreach (SkinnedMeshRenderer r in renderers) bodyVertexCount += r.sharedMesh != null ? r.sharedMesh.vertexCount : 0;

                var slot = new SlotData(converted);
                slot.AddOverlay(new OverlayData(overlay));
                int index = umaData.umaRecipe.slotDataList.Length;
                umaData.umaRecipe.SetSlot(index, slot);
                umaData.Dirty(true, true, true);

                Application.logMessageReceived += capture;
                try
                {
                    generator.GenerateSingleUMA(umaData, false);
                }
                finally
                {
                    Application.logMessageReceived -= capture;
                }

                renderers = umaData.GetRenderers();
                int dressedVertexCount = 0;
                int bindPoseCount = 0;
                foreach (SkinnedMeshRenderer r in renderers)
                {
                    if (r == null || r.sharedMesh == null) continue;
                    dressedVertexCount += r.sharedMesh.vertexCount;
                    bindPoseCount += r.sharedMesh.bindposes.Length;
                    Assert.AreEqual(r.sharedMesh.bindposes.Length, r.bones.Length, "renderer bones and bind poses must line up");
                    foreach (Transform bone in r.bones) Assert.IsNotNull(bone, "every renderer bone must exist");
                }
                Assert.AreEqual(bodyVertexCount + converted.meshData.vertexCount, dressedVertexCount, "the jacket's vertices are in the generated mesh");
                Assert.That(bindPoseCount, Is.GreaterThan(0));
                foreach (int hash in converted.meshData.boneNameHashes)
                {
                    Assert.IsTrue(umaData.skeleton.HasBone(hash), "skeleton lacks a jacket bone " + hash);
                }

                // The mesh combiner kept the jacket intact: in the character's current pose, every
                // jacket vertex is in the generated mesh where Unity puts the converted slot when it
                // skins that slot alone on the same skeleton.
                var temporary = new List<Object>();
                try
                {
                    SkinOnAvatar(avatar, converted.meshData, temporary, out Vector3[] expected, out _, out _);
                    var generated = new List<Vector3>(dressedVertexCount);
                    foreach (SkinnedMeshRenderer r in renderers)
                    {
                        if (r == null || r.sharedMesh == null) continue;
                        var bakedRenderer = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        temporary.Add(bakedRenderer);
                        r.BakeMesh(bakedRenderer, true);
                        Matrix4x4 toAvatar = avatarObject.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                        foreach (Vector3 vertex in bakedRenderer.vertices) generated.Add(toAvatar.MultiplyPoint3x4(vertex));
                    }
                    float worst = 0f;
                    for (int v = 0; v < expected.Length; v++)
                    {
                        float nearest = float.MaxValue;
                        for (int g = 0; g < generated.Count; g++)
                        {
                            float d = (generated[g] - expected[v]).sqrMagnitude;
                            if (d < nearest) nearest = d;
                        }
                        worst = Mathf.Max(worst, Mathf.Sqrt(nearest));
                    }
                    Assert.That(worst, Is.LessThanOrEqualTo(ExactTolerance), "a jacket vertex is not where the converted slot puts it in the generated mesh (metres)");
                }
                finally
                {
                    foreach (Object o in temporary)
                    {
                        if (o != null) Object.DestroyImmediate(o);
                    }
                }

                List<string> boneLogs = logs.FindAll(l =>
                    l.IndexOf("bone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    l.IndexOf("bind", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.IsEmpty(boneLogs, "generation logged bone or bind pose problems:\n" + string.Join("\n", boneLogs));
                Assert.IsEmpty(logs.FindAll(l => l.StartsWith("Error") || l.StartsWith("Exception") || l.StartsWith("Assert")),
                    "generation logged errors:\n" + string.Join("\n", logs));
            }
            finally
            {
                if (avatar != null)
                {
                    try { avatar.CleanupGeneratedData(); } catch { }
                }
                if (avatarObject != null) Object.DestroyImmediate(avatarObject);
                Object.DestroyImmediate(converted);
            }
        }

        // --- automatic conversion while a character is built ---------------------------------------

        [Test]
        public void HumanMale30NamesTheUma2RigAndItsBakedRestPoseIsCurrent()
        {
            Assert.IsNotNull(targetRace.legacyRig, "HumanMale30 must name the UMA 2 HumanMale rig as its legacy rig");
            Assert.IsNull(sourceRace.legacyRig, "the UMA 2 race replaces no older rig");
            UMARigRestPose baked = targetRace.legacyRig.GetRestPose();

            // The baked asset must describe the same skeleton the UMA 2 race's own slots do today.
            Assert.AreEqual(sourceRig.BoneCount, baked.BoneCount);
            Assert.AreEqual(sourceRig.RootBoneName, baked.RootBoneName);
            Assert.That(UMARigRestPose.MaxAbsDifference(sourceRig.RigRootToAvatar, baked.RigRootToAvatar), Is.LessThan(1e-6f));
            foreach (KeyValuePair<int, UMATransform> entry in sourceRig.Bones)
            {
                Assert.IsTrue(baked.TryGetAvatarMatrix(entry.Key, out Matrix4x4 bakedMatrix), "baked rest pose lacks " + entry.Value.name);
                sourceRig.TryGetAvatarMatrix(entry.Key, out Matrix4x4 raceMatrix);
                Assert.That(UMARigRestPose.MaxAbsDifference(raceMatrix, bakedMatrix), Is.LessThan(1e-5f), entry.Value.name + ": rebake the legacy rig asset");
            }
        }

        [Test]
        public void TheSlotsOwnBonesTellWhichRigItWasBuiltFor()
        {
            foreach (SlotDataAsset slot in bardSlots)
            {
                Assert.IsTrue(UMALegacySlotConverter.IsBuiltForLegacyRig(slot.meshData, sourceRig, targetRig), slot.slotName + " was built for the UMA 2 rig");
                Assert.IsFalse(UMALegacySlotConverter.IsBuiltForLegacyRig(slot.meshData, targetRig, sourceRig), slot.slotName);
            }

            // HumanMale30's own body slots still carry the legacy flag, which is why the flag alone
            // cannot decide: their bones say they belong to HumanMale30.
            int flaggedLegacy = 0;
            foreach (SlotData slotData in targetRace.baseRaceRecipe.GetCachedRecipe(loadSlots: true).GetAllSlots())
            {
                if (slotData == null || slotData.asset == null || !UMASlotRigConverter.CanConvert(slotData.asset.meshData, out _)) continue;
                if (slotData.asset.isLegacySlot) flaggedLegacy++;
                Assert.IsFalse(UMALegacySlotConverter.IsBuiltForLegacyRig(slotData.asset.meshData, sourceRig, targetRig), slotData.asset.slotName + " is a HumanMale30 slot");
                Assert.That(UMALegacySlotConverter.GetBoneMatch(slotData.asset.meshData, targetRig, out _), Is.GreaterThanOrEqualTo(UMALegacySlotConverter.LegacyRigMatch), slotData.asset.slotName);
            }
            Debug.Log("[UMASlotRigConverterBardTests] HumanMale30 base slots still flagged legacy: " + flaggedLegacy);
        }

        [Test]
        public void ALegacySlotIsConvertedWhenAHumanMale30CharacterIsBuilt()
        {
            Assert.IsNotNull(targetRace.legacyRig, "HumanMale30 must name its legacy rig for automatic conversion");
            SlotDataAsset jacket = bardSlots.Find(s => s.slotName.Contains("Jacket"));
            Assert.IsTrue(jacket.isLegacySlot, "the Bard jacket is still flagged as a legacy slot");
            UMAMeshData jacketMesh = jacket.meshData;
            Vector3 jacketFirstVertex = jacketMesh.vertices[0];
            OverlayDataAsset overlay = FindBardOverlay("Jacket") ?? FindBardOverlay(null);
            Assert.IsNotNull(overlay, "a Bard overlay is needed to render the jacket");
            UMAGenerator generator = UMAAssetIndexer.Instance.Generator;
            Assert.IsNotNull(generator, "UMAAssetIndexer.Generator");

            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) =>
            {
                if (type != LogType.Log) logs.Add(type + ": " + message);
            };

            var temporary = new List<Object>();
            GameObject avatarObject = null;
            DynamicCharacterAvatar avatar = null;
            SlotDataAsset convertedCopy = null;
            UMALegacySlotConverter.ClearCache();
            try
            {
                avatarObject = new GameObject("UMA_RigConversionTest_AutoConvert") { hideFlags = HideFlags.HideAndDontSave };
                avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
                avatar.hideFlags = HideFlags.HideAndDontSave;
                avatar.editorTimeGeneration = true;
                avatar.activeRace.name = targetRace.raceName;
                avatar.activeRace.data = targetRace;
                avatar.GenerateNow();
                UMAData umaData = avatar.umaData;
                Assert.IsNotNull(umaData, "GenerateNow produced no UMAData");

                // HumanMale30's own slots carry the legacy flag but belong to its rig: none may be swapped.
                var ownSlots = new List<SlotData>();
                var ownAssets = new List<SlotDataAsset>();
                foreach (SlotData baseSlot in targetRace.baseRaceRecipe.GetCachedRecipe(loadSlots: true).GetAllSlots())
                {
                    if (baseSlot == null || baseSlot.asset == null) continue;
                    ownSlots.Add(new SlotData(baseSlot.asset));
                    ownAssets.Add(baseSlot.asset);
                }
                Assert.That(ownAssets.FindAll(a => a.isLegacySlot).Count, Is.GreaterThan(0), "the check needs HumanMale30 slots that are flagged legacy");
                Assert.AreEqual(0, UMALegacySlotConverter.ConvertLegacySlots(ownSlots.ToArray(), targetRace), "HumanMale30's own slots must be left alone");
                for (int i = 0; i < ownSlots.Count; i++) Assert.AreSame(ownAssets[i], ownSlots[i].asset);

                // The body was built with the hook active (some of its slots are copies UMA baked
                // itself). Remember them: wearing the jacket must not change any of them.
                var bodyAssets = new List<SlotDataAsset>();
                foreach (SlotData bodySlot in umaData.umaRecipe.slotDataList)
                {
                    if (bodySlot == null || bodySlot.asset == null) continue;
                    Assert.IsFalse(UMALegacySlotConverter.IsBuiltForLegacyRig(bodySlot.asset.meshData, targetRace.legacyRig.GetRestPose(), targetRig),
                        bodySlot.asset.slotName + " is in the HumanMale30 body but looks like a UMA 2 slot");
                    bodyAssets.Add(bodySlot.asset);
                }

                // Wear the jacket exactly as it is in the project: UMA 2 rig, legacy flag set.
                var slot = new SlotData(jacket);
                slot.AddOverlay(new OverlayData(overlay));
                umaData.umaRecipe.SetSlot(umaData.umaRecipe.slotDataList.Length, slot);
                umaData.Dirty(true, true, true);
                Application.logMessageReceived += capture;
                try
                {
                    generator.GenerateSingleUMA(umaData, false);
                }
                finally
                {
                    Application.logMessageReceived -= capture;
                }

                // The recipe now holds a converted copy; the project asset is untouched.
                convertedCopy = slot.asset;
                Assert.AreNotSame(jacket, convertedCopy, "the legacy jacket was not swapped for a converted copy");
                Assert.IsFalse(EditorUtility.IsPersistent(convertedCopy));
                Assert.IsFalse(convertedCopy.isLegacySlot);
                Assert.AreEqual(jacket.slotName, convertedCopy.slotName);
                Assert.AreEqual(jacket.name, convertedCopy.name);
                Assert.IsTrue(UMASlotRigConverter.IsBoundToRigMeshSpace(convertedCopy.meshData, targetRig, 1e-4f, out float difference), "copy is not on the HumanMale30 rig, max difference " + difference);
                Assert.IsTrue(jacket.isLegacySlot, "the project asset must keep its flag");
                Assert.AreSame(jacketMesh, jacket.meshData, "the project asset must keep its mesh data");
                Assert.AreEqual(jacketFirstVertex, jacket.meshData.vertices[0]);
                foreach (SlotData bodySlot in umaData.umaRecipe.slotDataList)
                {
                    if (bodySlot == null || bodySlot == slot || bodySlot.asset == null) continue;
                    CollectionAssert.Contains(bodyAssets, bodySlot.asset, bodySlot.asset.slotName + " changed during the second build");
                }
                List<string> boneLogs = logs.FindAll(l =>
                    l.IndexOf("bone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    l.IndexOf("bind", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    l.IndexOf("legacy", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.IsEmpty(boneLogs, "building with a legacy slot logged bone, bind pose or conversion problems:\n" + string.Join("\n", boneLogs));
                Assert.IsEmpty(logs.FindAll(l => l.StartsWith("Error") || l.StartsWith("Exception") || l.StartsWith("Assert")),
                    "building with a legacy slot logged errors:\n" + string.Join("\n", logs));

                // The character wears it where an explicit conversion with the same settings puts it.
                UMAMeshData expectedMesh = UMASlotRigConverter.Convert(jacket.meshData, targetRace.legacyRig.GetRestPose(), targetRig,
                    new UMASlotRigConversionOptions { Profile = targetRace.legacyRigProfile });
                SkinOnAvatar(avatar, expectedMesh, temporary, out Vector3[] expected, out _, out _);
                var generated = new List<Vector3>();
                foreach (SkinnedMeshRenderer r in umaData.GetRenderers())
                {
                    if (r == null || r.sharedMesh == null) continue;
                    var bakedRenderer = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    temporary.Add(bakedRenderer);
                    r.BakeMesh(bakedRenderer, true);
                    Matrix4x4 toAvatar = avatarObject.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                    foreach (Vector3 vertex in bakedRenderer.vertices) generated.Add(toAvatar.MultiplyPoint3x4(vertex));
                }
                float worst = 0f;
                for (int v = 0; v < expected.Length; v++)
                {
                    float nearest = float.MaxValue;
                    for (int g = 0; g < generated.Count; g++)
                    {
                        float d = (generated[g] - expected[v]).sqrMagnitude;
                        if (d < nearest) nearest = d;
                    }
                    worst = Mathf.Max(worst, Mathf.Sqrt(nearest));
                }
                Assert.That(worst, Is.LessThanOrEqualTo(ExactTolerance), "a jacket vertex is not where the converted slot belongs in the generated mesh (metres)");

                // A second wearer gets the same copy rather than a second conversion.
                var again = new[] { new SlotData(jacket) };
                Assert.AreEqual(1, UMALegacySlotConverter.ConvertLegacySlots(again, targetRace));
                Assert.AreSame(convertedCopy, again[0].asset, "the converted copy must be reused");
                // Already converted slots are left alone.
                Assert.AreEqual(0, UMALegacySlotConverter.ConvertLegacySlots(again, targetRace));

                // A race without a legacy rig converts nothing, and the switch turns it all off.
                var onSourceRace = new[] { new SlotData(jacket) };
                Assert.AreEqual(0, UMALegacySlotConverter.ConvertLegacySlots(onSourceRace, sourceRace));
                Assert.AreSame(jacket, onSourceRace[0].asset);
                UMALegacySlotConverter.Enabled = false;
                var whileOff = new[] { new SlotData(jacket) };
                Assert.AreEqual(0, UMALegacySlotConverter.ConvertLegacySlots(whileOff, targetRace));
                Assert.AreSame(jacket, whileOff[0].asset);
            }
            finally
            {
                UMALegacySlotConverter.Enabled = true;
                UMALegacySlotConverter.ClearCache();
                if (avatar != null)
                {
                    try { avatar.CleanupGeneratedData(); } catch { }
                }
                if (avatarObject != null) Object.DestroyImmediate(avatarObject);
                foreach (Object o in temporary)
                {
                    if (o != null) Object.DestroyImmediate(o);
                }
                if (convertedCopy != null && !EditorUtility.IsPersistent(convertedCopy)) Object.DestroyImmediate(convertedCopy);
            }
        }

        private static OverlayDataAsset FindBardOverlay(string nameContains)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:OverlayDataAsset", new[] { BardFolder }))
            {
                var overlay = AssetDatabase.LoadAssetAtPath<OverlayDataAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (overlay == null) continue;
                if (nameContains == null || overlay.name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return overlay;
                }
            }
            return null;
        }

        private static string Describe(UMATestReport report)
        {
            var sb = new System.Text.StringBuilder();
            foreach (UMATestMessage m in report.Messages)
            {
                if (m.Severity == UMATestSeverity.Error) sb.AppendLine(m.Category + ": " + m.Message);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Skins both meshes into avatar space with their own rigs and returns the largest per-vertex
        /// distance, plus each mesh's avatar-space bounds.
        /// </summary>
        private static float CompareBaked(UMAMeshData original, UMARigRestPose originalRig, UMAMeshData converted, UMARigRestPose convertedRig,
            out Bounds originalBounds, out Bounds convertedBounds)
        {
            Vector3[] a = BakeToAvatar(original, originalRig);
            Vector3[] b = BakeToAvatar(converted, convertedRig);
            Assert.AreEqual(a.Length, b.Length);
            originalBounds = new Bounds(a[0], Vector3.zero);
            convertedBounds = new Bounds(b[0], Vector3.zero);
            float max = 0f;
            for (int v = 0; v < a.Length; v++)
            {
                originalBounds.Encapsulate(a[v]);
                convertedBounds.Encapsulate(b[v]);
                float d = Vector3.Distance(a[v], b[v]);
                if (d > max) max = d;
            }
            return max;
        }

        private static Vector3[] BakeToAvatar(UMAMeshData md, UMARigRestPose rig)
        {
            var skin = new Matrix4x4[md.boneNameHashes.Length];
            UMARigRestPose effective = rig.WithAdditionalBones(md.umaBones);
            for (int i = 0; i < skin.Length; i++)
            {
                Assert.IsTrue(effective.TryGetAvatarMatrix(md.boneNameHashes[i], out Matrix4x4 boneToAvatar), "rig lacks bone for influence " + i);
                skin[i] = boneToAvatar * md.bindPoses[i];
            }
            byte[] perVertex = md.ManagedBonesPerVertex;
            BoneWeight1[] weights = md.ManagedBoneWeights;
            Assert.IsNotNull(perVertex);
            Assert.IsNotNull(weights);
            var result = new Vector3[md.vertexCount];
            int offset = 0;
            for (int v = 0; v < md.vertexCount; v++)
            {
                Vector3 p = Vector3.zero;
                int count = perVertex[v];
                for (int k = 0; k < count; k++)
                {
                    BoneWeight1 bw = weights[offset + k];
                    p += skin[bw.boneIndex].MultiplyPoint3x4(md.vertices[v]) * bw.weight;
                }
                offset += count;
                result[v] = p;
            }
            return result;
        }
    }
}
