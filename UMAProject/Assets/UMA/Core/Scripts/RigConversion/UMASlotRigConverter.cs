using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA
{
    /// <summary>How the converter adapts geometry to differences between the two rest skeletons.</summary>
    public enum UMARigCorrectionMode
    {
        /// <summary>
        /// Bake, then rebind only. Vertices stay exactly where the source rig placed them in avatar
        /// space, so the result is exact when both rigs share one armature.
        /// </summary>
        None = 0,
        /// <summary>
        /// Also move each bone's influence from the source joint to the target joint, rotated by the
        /// change in bone direction, blended per vertex by bone weight. Use when the rigs have
        /// different proportions.
        /// </summary>
        JointAnchored = 1,
    }

    /// <summary>How the converter carries normals (and blend-shape normal deltas) to the target rig.</summary>
    public enum UMARigNormalMode
    {
        /// <summary>
        /// Transform normals with the blended skinning matrix, which is what Unity's skinning and UMA's
        /// mesh combiner do. The converted slot is lit exactly as the source slot is on its own rig.
        /// </summary>
        MatchSourceSkinning = 0,
        /// <summary>
        /// Transform normals with the inverse-transpose of the blended skinning matrix. Where a vertex
        /// blends bones that sit far apart in the bind pose this follows the baked surface a little more
        /// closely, at the cost of shading that differs from the source slot.
        /// </summary>
        InverseTranspose = 1,
    }

    /// <summary>Settings for <see cref="UMASlotRigConverter"/>.</summary>
    public sealed class UMASlotRigConversionOptions
    {
        public UMARigCorrectionMode CorrectionMode = UMARigCorrectionMode.JointAnchored;

        public UMARigNormalMode NormalMode = UMARigNormalMode.MatchSourceSkinning;

        /// <summary>Optional per-bone overrides.</summary>
        public UMASlotRigConversionProfile Profile;

        /// <summary>
        /// The slot name used to look up the profile's entries for single slots
        /// (<see cref="UMASlotRigConversionProfile.slotOverrides"/>). When empty, the mesh data's own
        /// <see cref="UMAMeshData.SlotName"/> is used.
        /// </summary>
        public string ProfileSlotName;

        /// <summary>
        /// The target character's skin at rest, for profile effectors that clear the body. Without it
        /// those effectors are skipped and the report says so.
        /// </summary>
        public UMARigBodySurface BodySurface;

        /// <summary>
        /// Reuse a donor slot's bind pose for a bone when it agrees with the computed one, so the
        /// mesh combiner merges the bone instead of duplicating it.
        /// </summary>
        public bool PreferDonorBindPoses = true;

        /// <summary>Largest matrix element difference still treated as the same bind pose.</summary>
        public float BindPoseTolerance = 1e-4f;

        /// <summary>
        /// Keep bones the slot defines itself (in its umaBones) that neither rig knows, re-parented
        /// under their nearest target ancestor. When false their weights move to that ancestor.
        /// </summary>
        public bool CarrySlotBones = true;

        public float WeightEpsilon = 1e-6f;

        /// <summary>
        /// A vertex whose bone weights sum to within this of 1 is baked with the weights as stored,
        /// which is where Unity draws it (Unity does not normalise weights when it skins). Outside
        /// this the weights are treated as damaged and normalised before baking; the report counts
        /// those vertices. Output weights are always normalised.
        /// </summary>
        public float WeightSumTolerance = 1e-3f;

        public static UMASlotRigConversionOptions Default => new UMASlotRigConversionOptions();

        public static UMASlotRigConversionOptions Exact => new UMASlotRigConversionOptions
        {
            CorrectionMode = UMARigCorrectionMode.None,
        };
    }

    /// <summary>What a conversion did, for logs, inspectors and tests.</summary>
    public sealed class UMASlotRigConversionReport
    {
        public string SlotName;
        public int VertexCount;
        public int SourceInfluenceCount;
        public int TargetBoneCount;
        public int WeightsMovedToAncestor;
        public int WeightsMovedToRoot;
        public int CarriedBones;
        public int SourceInfluencesWithoutRestBone;
        public int BlendShapeCount;
        public int TargetMeshSpaceMismatches;
        public float TargetMeshSpaceMaxMismatch;
        /// <summary>Vertices the profile's effectors moved, and the furthest any of them went, in metres.</summary>
        public int EffectorMovedVertices;
        public float EffectorMaxMove;
        /// <summary>Largest distance between a source joint and its target joint, over the bones whose correction the slot uses.</summary>
        public float MaxJointDelta;
        public string MaxJointDeltaBone;
        /// <summary>Vertices with no usable bone weights; they are bound rigidly to the rig root.</summary>
        public int UnweightedVertices;
        /// <summary>Vertices whose weights were further from 1 than <see cref="UMASlotRigConversionOptions.WeightSumTolerance"/> and were normalised before baking.</summary>
        public int VerticesWithRenormalisedWeights;
        /// <summary>
        /// True when the two rig roots have opposite handedness (one is mirrored). Triangle winding
        /// and tangent handedness were reversed so the slot still faces outwards on the target.
        /// </summary>
        public bool HandednessFlipped;
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> RemappedBones = new List<string>();

        public string Summary()
        {
            return $"Converted '{SlotName}': {VertexCount} vertices, {SourceInfluenceCount} source influences -> {TargetBoneCount} target bones. " +
                   $"Weights moved to ancestor: {WeightsMovedToAncestor}, to root: {WeightsMovedToRoot}, carried bones: {CarriedBones}, " +
                   $"influences without a rest bone: {SourceInfluencesWithoutRestBone}. Unweighted vertices: {UnweightedVertices}, " +
                   $"renormalised: {VerticesWithRenormalisedWeights}. Max joint delta {MaxJointDelta:0.####} m at '{MaxJointDeltaBone}'. " +
                   $"Target mesh-space mismatches: {TargetMeshSpaceMismatches} (max {TargetMeshSpaceMaxMismatch:0.#####}). " +
                   (HandednessFlipped ? "Handedness flipped. " : string.Empty) + $"Warnings: {Warnings.Count}.";
        }
    }

    /// <summary>
    /// Converts a slot's mesh data from one rig to another so the slot can be worn by characters of
    /// the target race. The slot is baked to avatar space through its own bind poses, one influence
    /// at a time, optionally corrected per bone for differences between the two rest skeletons, and
    /// rebound against the target rest skeleton in the target rig's canonical mesh space.
    /// </summary>
    /// <remarks>
    /// Baking every vertex through the bind pose of each of its own influences is what makes slots
    /// with several bind poses per bone (clothing exported in a different pose from the body)
    /// convert correctly: no single mesh transform is assumed. Works from data alone; no avatar,
    /// Transform or editor API is needed.
    /// </remarks>
    public static class UMASlotRigConverter
    {
        private struct BoneCorrection
        {
            public Matrix4x4 Affine;        // avatar -> avatar
            public Matrix4x4 Linear;        // how directions transform: rotation, plus any profile scale
            public Matrix4x4 NormalLinear;  // how normals transform; differs from Linear only under profile scale
            public Quaternion Rotation;     // the computed rotation alone, which a leaf bone borrows from its parent

            public static BoneCorrection Identity => new BoneCorrection
            {
                Affine = Matrix4x4.identity,
                Linear = Matrix4x4.identity,
                NormalLinear = Matrix4x4.identity,
                Rotation = Quaternion.identity,
            };
        }

        private struct TargetBone
        {
            public int Hash;
            public Matrix4x4 ToAvatar;      // bone -> avatar rest frame on the target rig
            public bool Carried;            // defined by the slot, not by the target rig
            public int ParentHash;          // parent on the target rig (resolved)
            public UMATransform Definition; // umaBones entry for carried bones
        }

        /// <summary>
        /// Converts <paramref name="slot"/> from <paramref name="sourceRace"/>'s rig to
        /// <paramref name="targetRace"/>'s rig. Returns new mesh data; the slot is not modified.
        /// </summary>
        public static UMAMeshData ConvertSlot(SlotDataAsset slot, RaceData sourceRace, RaceData targetRace,
            UMASlotRigConversionOptions options = null, UMASlotRigConversionReport report = null)
        {
            if (slot == null)
            {
                throw new ArgumentNullException(nameof(slot));
            }
            UMARigRestPose source = UMARigRestPose.FromRace(sourceRace);
            UMARigRestPose target = UMARigRestPose.FromRace(targetRace);
            return Convert(slot.meshData, source, target, options, report);
        }

        /// <summary>
        /// Tells whether mesh data can be converted at all: it must have vertices, bind poses and a
        /// complete set of bone weights. <paramref name="reason"/> says what is missing.
        /// </summary>
        public static bool CanConvert(UMAMeshData source, out string reason)
        {
            if (UMAMeshData.IsNullOrEmptyMeshData(source) || source.vertices == null || source.vertices.Length == 0)
            {
                reason = "Source mesh data has no vertices.";
                return false;
            }
            if (source.bindPoses == null || source.boneNameHashes == null || source.bindPoses.Length == 0 || source.boneNameHashes.Length == 0)
            {
                reason = "Source mesh data has no bind poses or bone hashes; it is not a skinned slot.";
                return false;
            }

            int vertexCount = source.vertices.Length;
            if (source.ManagedBonesPerVertex != null && source.ManagedBonesPerVertex.Length == vertexCount &&
                source.ManagedBoneWeights != null && source.ManagedBoneWeights.Length > 0)
            {
                long needed = 0;
                for (int v = 0; v < vertexCount; v++)
                {
                    needed += source.ManagedBonesPerVertex[v];
                    if (needed > source.ManagedBoneWeights.Length)
                    {
                        reason = $"Bone weight stream ends before vertex {v}.";
                        return false;
                    }
                }
            }
            else if (source.boneWeights == null || source.boneWeights.Length != vertexCount)
            {
                reason = "Source mesh data has no bone weights, or not one set per vertex.";
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Converts like <see cref="Convert"/> but never throws for a slot that cannot be converted:
        /// returns false with the cause in <paramref name="reason"/>. Use this for unattended
        /// conversion, where one unusable slot must not stop the rest.
        /// </summary>
        public static bool TryConvert(UMAMeshData source, UMARigRestPose sourceRig, UMARigRestPose targetRig,
            UMASlotRigConversionOptions options, UMASlotRigConversionReport report, out UMAMeshData result, out string reason)
        {
            result = null;
            if (sourceRig == null || targetRig == null)
            {
                reason = sourceRig == null ? "No source rig was given." : "No target rig was given.";
                return false;
            }
            if (!CanConvert(source, out reason))
            {
                return false;
            }
            try
            {
                result = Convert(source, sourceRig, targetRig, options, report);
                return true;
            }
            catch (Exception ex)
            {
                // Data the checks above did not anticipate (for example bone tables that do not line
                // up). Reported, not rethrown: that is this method's contract.
                reason = "Conversion failed: " + ex.GetType().Name + ": " + ex.Message;
                result = null;
                return false;
            }
        }

        /// <summary>
        /// True when every bone the mesh is skinned to is placed, through its bind pose, in
        /// <paramref name="rig"/>'s canonical mesh space; that is, the slot is already native to the rig
        /// and needs no conversion. A slot made for another rig fails this even when the bone names
        /// match, because its bind poses describe the other rig's skeleton.
        /// </summary>
        /// <param name="maxDifference">Largest matrix element difference found; infinity when a bone is unknown.</param>
        public static bool IsBoundToRigMeshSpace(UMAMeshData mesh, UMARigRestPose rig, float tolerance, out float maxDifference)
        {
            maxDifference = float.PositiveInfinity;
            if (rig == null || UMAMeshData.IsNullOrEmptyMeshData(mesh) || mesh.bindPoses == null || mesh.boneNameHashes == null)
            {
                return false;
            }
            int count = Mathf.Min(mesh.bindPoses.Length, mesh.boneNameHashes.Length);
            if (count == 0)
            {
                return false;
            }

            Matrix4x4 canonical = rig.GetCanonicalMeshToAvatar(tolerance, out _, out _);
            UMARigRestPose effective = rig.WithAdditionalBones(mesh.umaBones);
            float max = 0f;
            for (int i = 0; i < count; i++)
            {
                if (!effective.TryGetAvatarMatrix(mesh.boneNameHashes[i], out Matrix4x4 boneToAvatar))
                {
                    return false;
                }
                float diff = UMARigRestPose.MaxAbsDifference(boneToAvatar * mesh.bindPoses[i], canonical);
                if (diff > max)
                {
                    max = diff;
                }
            }
            maxDifference = max;
            return max <= tolerance;
        }

        /// <summary>
        /// Converts mesh data bound to <paramref name="sourceRig"/> so it is bound to
        /// <paramref name="targetRig"/>. Returns new mesh data; the input is not modified and shares
        /// no arrays with the result. Throws <see cref="ArgumentException"/> for mesh data that
        /// <see cref="CanConvert"/> rejects; use <see cref="TryConvert"/> to avoid that.
        /// </summary>
        public static UMAMeshData Convert(UMAMeshData source, UMARigRestPose sourceRig, UMARigRestPose targetRig,
            UMASlotRigConversionOptions options = null, UMASlotRigConversionReport report = null)
        {
            if (sourceRig == null) throw new ArgumentNullException(nameof(sourceRig));
            if (targetRig == null) throw new ArgumentNullException(nameof(targetRig));
            if (!CanConvert(source, out string blocker))
            {
                throw new ArgumentException(blocker, nameof(source));
            }

            options = options ?? UMASlotRigConversionOptions.Default;
            report = report ?? new UMASlotRigConversionReport();
            report.SlotName = source.SlotName;

            int vertexCount = source.vertices.Length;
            report.VertexCount = vertexCount;

            // The slot may define bones of its own (props, extra helpers) that the source race does
            // not have. They belong to the source rest skeleton for baking, and are carried to the
            // target when nothing else defines them. Race bones listed in the slot are not slot-owned.
            UMARigRestPose effectiveSource = sourceRig.WithAdditionalBones(source.umaBones);
            var slotDefinedBones = new Dictionary<int, UMATransform>();
            if (source.umaBones != null)
            {
                for (int i = 0; i < source.umaBones.Length; i++)
                {
                    UMATransform bone = source.umaBones[i];
                    if (bone != null && !sourceRig.Contains(bone.hash) && !slotDefinedBones.ContainsKey(bone.hash))
                    {
                        slotDefinedBones.Add(bone.hash, bone);
                    }
                }
            }

            GetSourceWeights(source, out byte[] bonesPerVertex, out BoneWeight1[] weights);
            if (bonesPerVertex.Length != vertexCount)
            {
                throw new ArgumentException($"Source mesh data has {vertexCount} vertices but bone weights for {bonesPerVertex.Length}.", nameof(source));
            }

            // UMA renders a character with its rig root as the renderer's root bone, and Unity reverses
            // face culling and the bitangent sign for a renderer whose transform is mirrored. So what
            // decides whether winding and tangent handedness must flip is whether the two rig roots
            // have opposite handedness, not the mesh spaces or any one vertex's blend.
            bool handednessFlipped = effectiveSource.RigRootToAvatar.determinant * targetRig.RigRootToAvatar.determinant < 0f;
            report.HandednessFlipped = handednessFlipped;

            // 1. Skinning matrix per source influence: mesh -> avatar, through that influence's own bind pose.
            int influenceCount = Mathf.Min(source.bindPoses.Length, source.boneNameHashes.Length);
            report.SourceInfluenceCount = influenceCount;
            var skin = new Matrix4x4[influenceCount];
            for (int i = 0; i < influenceCount; i++)
            {
                int hash = source.boneNameHashes[i];
                if (!effectiveSource.TryGetAvatarMatrix(hash, out Matrix4x4 boneToAvatar))
                {
                    // No rest definition anywhere: the generator would parent such a bone under the
                    // root, so treat the bind pose as relative to the rig root.
                    boneToAvatar = effectiveSource.RigRootToAvatar;
                    report.SourceInfluencesWithoutRestBone++;
                    report.Warnings.Add($"Source influence {i} (bone hash {hash}) has no rest definition in the source rig or the slot; baked relative to the rig root.");
                }
                skin[i] = boneToAvatar * source.bindPoses[i];
            }

            // 2. Resolve the target bone for every source influence.
            var targetBones = new List<TargetBone>();
            var targetIndexByHash = new Dictionary<int, int>();
            var influenceToTarget = new int[influenceCount];
            var influenceMovedToAncestor = new bool[influenceCount];
            var influenceMovedToRoot = new bool[influenceCount];
            var carriedParents = new Dictionary<int, int>(); // carried bone -> resolved target parent
            for (int i = 0; i < influenceCount; i++)
            {
                int hash = source.boneNameHashes[i];
                int resolved = ResolveTargetHash(hash, effectiveSource, targetRig, slotDefinedBones, options.CarrySlotBones, carriedParents, out bool movedToAncestor, out bool movedToRoot);
                influenceToTarget[i] = GetOrAddTargetBone(resolved, targetRig, effectiveSource, carriedParents, targetBones, targetIndexByHash, report);
                influenceMovedToAncestor[i] = movedToAncestor;
                influenceMovedToRoot[i] = movedToRoot;
                if (movedToAncestor || movedToRoot)
                {
                    string sourceName = effectiveSource.TryGetBone(hash, out UMATransform def) ? def.name : hash.ToString();
                    string targetName = targetRig.TryGetBone(resolved, out UMATransform tdef) ? tdef.name : (resolved == targetRig.RootBoneHash ? targetRig.RootBoneName : resolved.ToString());
                    report.RemappedBones.Add(sourceName + " -> " + targetName);
                }
            }

            // 3. Per-target-bone correction for rest skeleton differences.
            string profileSlot = string.IsNullOrEmpty(options.ProfileSlotName) ? source.SlotName : options.ProfileSlotName;
            var corrections = new BoneCorrection[targetBones.Count];
            if (options.CorrectionMode == UMARigCorrectionMode.JointAnchored)
            {
                ComputeCorrections(effectiveSource, targetRig, targetBones, options.Profile, profileSlot, corrections, report);
            }
            else
            {
                for (int j = 0; j < corrections.Length; j++)
                {
                    corrections[j] = BoneCorrection.Identity;
                }
            }

            // Carried bones follow their parent's correction so they keep their place on the body.
            for (int j = 0; j < targetBones.Count; j++)
            {
                if (!targetBones[j].Carried)
                {
                    continue;
                }
                TargetBone tb = targetBones[j];
                effectiveSource.TryGetAvatarMatrix(tb.Hash, out Matrix4x4 sourceFrame);
                BoneCorrection parentCorrection = BoneCorrection.Identity;
                if (targetIndexByHash.TryGetValue(tb.ParentHash, out int parentIndex))
                {
                    parentCorrection = corrections[parentIndex];
                }
                else if (options.CorrectionMode == UMARigCorrectionMode.JointAnchored)
                {
                    // The carried bone's vertices move with this parent, so its joint delta counts.
                    parentCorrection = ApplyProfileOffsets(
                        ComputeAnchorCorrection(effectiveSource, targetRig, tb.ParentHash, options.Profile, profileSlot, report, true),
                        tb.ParentHash, targetRig, options.Profile, profileSlot);
                }
                tb.ToAvatar = parentCorrection.Affine * sourceFrame;
                corrections[j] = parentCorrection;
                targetBones[j] = tb;
            }

            // A profile that scales makes normals transform differently from other directions.
            bool correctionsScale = false;
            for (int j = 0; j < corrections.Length && !correctionsScale; j++)
            {
                correctionsScale = corrections[j].NormalLinear != corrections[j].Linear;
            }

            // 4. Target mesh space from the donor slots.
            Matrix4x4 meshToAvatar = targetRig.GetCanonicalMeshToAvatar(options.BindPoseTolerance, out int mismatches, out float maxMismatch);
            report.TargetMeshSpaceMismatches = mismatches;
            report.TargetMeshSpaceMaxMismatch = maxMismatch;
            if (mismatches > 0)
            {
                report.Warnings.Add($"Target donor slots disagree on mesh space for {mismatches} bone(s) (max {maxMismatch:0.#####}); the space most bones share is used.");
            }
            Matrix4x4 avatarToMesh = meshToAvatar.inverse;
            Matrix4x4 avatarToMeshLinear = LinearPart(avatarToMesh);

            // 5. Target bind poses and bone table.
            int targetBoneCount = targetBones.Count;
            report.TargetBoneCount = targetBoneCount;
            var bindPoses = new Matrix4x4[targetBoneCount];
            var boneHashes = new int[targetBoneCount];
            for (int j = 0; j < targetBoneCount; j++)
            {
                TargetBone tb = targetBones[j];
                boneHashes[j] = tb.Hash;
                Matrix4x4 computed = tb.ToAvatar.inverse * meshToAvatar;
                if (options.PreferDonorBindPoses && !tb.Carried && targetRig.TryGetDonorBindPose(tb.Hash, out Matrix4x4 donor) &&
                    UMARigRestPose.MaxAbsDifference(donor, computed) <= options.BindPoseTolerance)
                {
                    computed = donor;
                }
                bindPoses[j] = computed;
            }

            // 6. Per-vertex bake, weight remap, correction and rebind.
            var outVertices = new Vector3[vertexCount];
            bool hasNormals = source.normals != null && source.normals.Length == vertexCount;
            bool hasTangents = source.tangents != null && source.tangents.Length == vertexCount;
            var outNormals = hasNormals ? new Vector3[vertexCount] : null;
            var outTangents = hasTangents ? new Vector4[vertexCount] : null;
            var outBonesPerVertex = new byte[vertexCount];
            var outWeights = new List<BoneWeight1>(weights.Length);
            var vertexLinear = new Matrix4x4[vertexCount];          // total direction transform per vertex (mesh -> mesh)
            // Normal deltas of blend shapes use the same matrix as the normals. That is vertexLinear
            // unless normals take the inverse-transpose, in which case those matrices are kept too.
            bool inverseTransposeNormals = options.NormalMode == UMARigNormalMode.InverseTranspose;
            bool hasBlendShapes = source.blendShapes != null && source.blendShapes.Length > 0;
            var vertexNormalLinear = hasNormals && hasBlendShapes && (inverseTransposeNormals || correctionsScale) ? new Matrix4x4[vertexCount] : null;
            // Base normals and tangents are renormalised after transformation. Blend-shape deltas are
            // added to them before UMA normalises again, so the deltas must be scaled by the same
            // factor, or the summed direction would no longer match the source.
            var normalScale = hasNormals ? new float[vertexCount] : null;
            var tangentScale = hasTangents ? new float[vertexCount] : null;
            var weightAccumulator = new float[targetBoneCount];
            var touched = new int[targetBoneCount];
            var sortedIndices = new int[targetBoneCount];
            bool haveSlotMeshSpace = false;
            Matrix4x4 slotMeshToAvatar = Matrix4x4.identity;
            int firstUnweightedVertex = -1;
            int firstRenormalisedVertex = -1;

            int weightOffset = 0;
            for (int v = 0; v < vertexCount; v++)
            {
                int count = bonesPerVertex[v];
                if (weightOffset + count > weights.Length)
                {
                    throw new ArgumentException($"Bone weight stream ends before vertex {v}.", nameof(source));
                }

                // Blended skinning matrix for this vertex (exact linear blend skinning map).
                Matrix4x4 bake = Zero();
                int touchedCount = 0;
                float total = 0f;
                for (int k = 0; k < count; k++)
                {
                    BoneWeight1 bw = weights[weightOffset + k];
                    if (bw.weight <= options.WeightEpsilon || bw.boneIndex < 0 || bw.boneIndex >= influenceCount)
                    {
                        continue;
                    }
                    AddScaled(ref bake, skin[bw.boneIndex], bw.weight);
                    total += bw.weight;
                    if (influenceMovedToAncestor[bw.boneIndex])
                    {
                        report.WeightsMovedToAncestor++;
                    }
                    else if (influenceMovedToRoot[bw.boneIndex])
                    {
                        report.WeightsMovedToRoot++;
                    }

                    int t = influenceToTarget[bw.boneIndex];
                    if (weightAccumulator[t] == 0f)
                    {
                        touched[touchedCount++] = t;
                    }
                    weightAccumulator[t] += bw.weight;
                }
                weightOffset += count;

                if (touchedCount == 0 || total <= options.WeightEpsilon)
                {
                    // Unweighted vertex: bind it rigidly to the rig root so it still renders.
                    int rootIndex = GetOrAddTargetBone(targetRig.RootBoneHash, targetRig, effectiveSource, carriedParents, targetBones, targetIndexByHash, report);
                    if (rootIndex >= weightAccumulator.Length)
                    {
                        Array.Resize(ref weightAccumulator, targetBones.Count);
                        Array.Resize(ref touched, targetBones.Count);
                        Array.Resize(ref sortedIndices, targetBones.Count);
                        Array.Resize(ref corrections, targetBones.Count);
                        corrections[rootIndex] = BoneCorrection.Identity;
                    }
                    // Unity draws a weightless vertex at the renderer's origin. Keep it where the slot's
                    // mesh puts it instead: in the mesh space most of the slot's bones are bound in.
                    if (!haveSlotMeshSpace)
                    {
                        if (!UMARigRestPose.TryGetMostCommon(skin, options.BindPoseTolerance, out slotMeshToAvatar, out _, out _))
                        {
                            slotMeshToAvatar = effectiveSource.RigRootToAvatar;
                        }
                        haveSlotMeshSpace = true;
                    }
                    bake = slotMeshToAvatar;
                    total = 1f;
                    touched[0] = rootIndex;
                    touchedCount = 1;
                    weightAccumulator[rootIndex] = 1f;
                    if (report.UnweightedVertices++ == 0)
                    {
                        firstUnweightedVertex = v;
                    }
                }
                else if (Mathf.Abs(total - 1f) > options.WeightSumTolerance)
                {
                    // Too far from 1 to be rounding: treat the weights as damaged and normalise them.
                    // Within the tolerance the stored weights are used as they are, which is exactly
                    // where Unity draws the vertex on the source rig.
                    Scale(ref bake, 1f / total);
                    if (report.VerticesWithRenormalisedWeights++ == 0)
                    {
                        firstRenormalisedVertex = v;
                    }
                }

                // Normalised, sorted output weights for this vertex, and the blended correction.
                for (int k = 0; k < touchedCount; k++)
                {
                    sortedIndices[k] = touched[k];
                }
                SortByWeightDescending(sortedIndices, touchedCount, weightAccumulator);
                Matrix4x4 correctionAffine = Zero();
                Matrix4x4 correctionLinear = Zero();
                Matrix4x4 correctionNormalLinear = Zero();
                float invTotal = 1f / total;
                for (int k = 0; k < touchedCount; k++)
                {
                    int t = sortedIndices[k];
                    float w = weightAccumulator[t] * invTotal;
                    outWeights.Add(new BoneWeight1 { boneIndex = t, weight = w });
                    AddScaled(ref correctionAffine, corrections[t].Affine, w);
                    AddScaled(ref correctionLinear, corrections[t].Linear, w);
                    if (correctionsScale)
                    {
                        AddScaled(ref correctionNormalLinear, corrections[t].NormalLinear, w);
                    }
                }
                outBonesPerVertex[v] = (byte)Mathf.Min(touchedCount, 255);
                for (int k = 0; k < touchedCount; k++)
                {
                    weightAccumulator[touched[k]] = 0f;
                }

                // Positions: mesh -> avatar (bake) -> corrected avatar -> target mesh space.
                Vector3 avatarPosition = bake.MultiplyPoint3x4(source.vertices[v]);
                Vector3 corrected = correctionAffine.MultiplyPoint3x4(avatarPosition);
                outVertices[v] = avatarToMesh.MultiplyPoint3x4(corrected);

                // Directions: the linear part of the same composite map.
                Matrix4x4 bakeLinear = LinearPart(bake);
                Matrix4x4 linear = avatarToMeshLinear * correctionLinear * bakeLinear;
                vertexLinear[v] = linear;
                if (hasNormals || hasTangents)
                {
                    if (hasNormals)
                    {
                        // Matched to the source skinning, a normal follows the same map as any other
                        // direction, except through a profile's scale, which it crosses inverted so it
                        // stays perpendicular to the scaled surface.
                        Matrix4x4 normalMatrix = inverseTransposeNormals
                            ? linear.inverse.transpose
                            : (correctionsScale ? avatarToMeshLinear * correctionNormalLinear * bakeLinear : linear);
                        if (vertexNormalLinear != null)
                        {
                            vertexNormalLinear[v] = normalMatrix;
                        }
                        Vector3 n = normalMatrix.MultiplyVector(source.normals[v]);
                        float length = n.magnitude;
                        normalScale[v] = length > 1e-6f ? 1f / length : 1f;
                        outNormals[v] = length > 1e-6f ? n / length : source.normals[v];
                    }
                    if (hasTangents)
                    {
                        Vector4 st = source.tangents[v];
                        Vector3 t = linear.MultiplyVector(new Vector3(st.x, st.y, st.z));
                        float length = t.magnitude;
                        tangentScale[v] = length > 1e-6f ? 1f / length : 1f;
                        if (length > 1e-6f)
                        {
                            t /= length;
                        }
                        else
                        {
                            t = new Vector3(st.x, st.y, st.z);
                        }
                        outTangents[v] = new Vector4(t.x, t.y, t.z, handednessFlipped ? -st.w : st.w);
                    }
                }
            }

            // 6b. Profile effectors: regions that reshape the slot where it now stands on the target.
            if (options.CorrectionMode == UMARigCorrectionMode.JointAnchored && options.Profile != null && options.Profile.HasAnyEffectors(profileSlot))
            {
                ApplyEffectors(source, targetRig, options, profileSlot, meshToAvatar, avatarToMesh, outVertices, outNormals, outTangents, report);
            }

            if (report.UnweightedVertices > 0)
            {
                report.Warnings.Add($"{report.UnweightedVertices} vertices have no usable bone weights (first: vertex {firstUnweightedVertex}); bound to '{targetRig.RootBoneName}'.");
            }
            if (report.VerticesWithRenormalisedWeights > 0)
            {
                report.Warnings.Add($"{report.VerticesWithRenormalisedWeights} vertices have bone weights that do not sum to 1 (first: vertex {firstRenormalisedVertex}); normalised before baking.");
            }

            // Bone table may have grown (root fallback). Rebuild the arrays if so.
            if (targetBones.Count != targetBoneCount)
            {
                targetBoneCount = targetBones.Count;
                report.TargetBoneCount = targetBoneCount;
                Array.Resize(ref bindPoses, targetBoneCount);
                Array.Resize(ref boneHashes, targetBoneCount);
                for (int j = 0; j < targetBoneCount; j++)
                {
                    TargetBone tb = targetBones[j];
                    boneHashes[j] = tb.Hash;
                    if (bindPoses[j] == default)
                    {
                        bindPoses[j] = tb.ToAvatar.inverse * meshToAvatar;
                    }
                }
            }

            // 7. Blend shapes follow the per-vertex direction maps.
            UMABlendShape[] outShapes = ConvertBlendShapes(source.blendShapes, vertexCount, vertexLinear, vertexNormalLinear ?? vertexLinear,
                normalScale, tangentScale, report);

            // 8. Assemble the result. DeepCopy copies the vertex streams but shares the triangle lists,
            // so those are copied here: the result must not alias anything the source asset owns.
            UMAMeshData result = source.DeepCopy();
            result.SlotName = source.SlotName;
            result.vertices = outVertices;
            if (hasNormals)
            {
                result.normals = outNormals;
            }
            if (hasTangents)
            {
                result.tangents = outTangents;
            }
            result.submeshes = CopySubmeshes(source.submeshes, handednessFlipped);
            result.vertexCount = vertexCount;
            result.blendShapes = outShapes;
            result.bindPoses = bindPoses;
            result.boneNameHashes = boneHashes;
            result.umaBones = BuildOutputBones(targetRig, targetBones);
            result.umaBoneCount = result.umaBones.Length;
            result.rootBoneHash = targetRig.RootBoneHash;
            result.RootBoneName = targetRig.RootBoneName;
            result.ManagedBonesPerVertex = outBonesPerVertex;
            result.ManagedBoneWeights = outWeights.ToArray();
            result.boneWeights = null;
            result.bones = null;
            result.rootBone = null;
            result.verticesModified = true;
            result.normalsModified = hasNormals;
            result.tangentsModified = hasTangents;
            return result;
        }

        /// <summary>
        /// Runs the profile's effectors over the converted vertices. They work in avatar space, so the
        /// vertices are taken there from the target's mesh space and brought back.
        /// </summary>
        private static void ApplyEffectors(UMAMeshData source, UMARigRestPose targetRig, UMASlotRigConversionOptions options, string profileSlot,
            Matrix4x4 meshToAvatar, Matrix4x4 avatarToMesh, Vector3[] vertices, Vector3[] normals, Vector4[] tangents, UMASlotRigConversionReport report)
        {
            var effectors = new List<UMARigEffector>();
            options.Profile.CollectEffectors(profileSlot, effectors);
            if (effectors.Count == 0)
            {
                return;
            }

            int count = vertices.Length;
            Matrix4x4 meshToAvatarLinear = LinearPart(meshToAvatar);
            Matrix4x4 avatarToMeshLinear = LinearPart(avatarToMesh);
            var positions = new Vector3[count];
            Vector3[] avatarNormals = normals != null ? new Vector3[count] : null;
            Vector4[] avatarTangents = tangents != null ? new Vector4[count] : null;
            for (int v = 0; v < count; v++)
            {
                positions[v] = meshToAvatar.MultiplyPoint3x4(vertices[v]);
                if (avatarNormals != null)
                {
                    avatarNormals[v] = meshToAvatarLinear.MultiplyVector(normals[v]).normalized;
                }
                if (avatarTangents != null)
                {
                    Vector3 t = meshToAvatarLinear.MultiplyVector(new Vector3(tangents[v].x, tangents[v].y, tangents[v].z)).normalized;
                    avatarTangents[v] = new Vector4(t.x, t.y, t.z, tangents[v].w);
                }
            }

            var triangles = new List<int[]>();
            if (source.submeshes != null)
            {
                for (int s = 0; s < source.submeshes.Length; s++)
                {
                    if (source.submeshes[s] != null)
                    {
                        // Every level of detail, so no vertex is left without neighbours.
                        triangles.Add(source.submeshes[s].getManagedTriangles(-1));
                    }
                }
            }

            // What goes in is kept, so that only what the effectors changed is written back: a vertex
            // they did not touch stays exactly what the conversion made it, to the last bit.
            var positionsBefore = (Vector3[])positions.Clone();
            Vector3[] normalsBefore = avatarNormals != null ? (Vector3[])avatarNormals.Clone() : null;
            Vector4[] tangentsBefore = avatarTangents != null ? (Vector4[])avatarTangents.Clone() : null;

            var effectorReport = new UMARigEffectorReport();
            bool moved = UMARigEffectorDeformer.Apply(effectors, targetRig, options.BodySurface, positions, avatarNormals, avatarTangents, triangles, effectorReport);
            report.Warnings.AddRange(effectorReport.Warnings);
            report.EffectorMovedVertices = effectorReport.MovedVertices;
            report.EffectorMaxMove = effectorReport.MaxMove;
            if (!moved)
            {
                return;
            }
            for (int v = 0; v < count; v++)
            {
                if (!positions[v].Equals(positionsBefore[v]))
                {
                    vertices[v] = avatarToMesh.MultiplyPoint3x4(positions[v]);
                }
                if (avatarNormals != null && !avatarNormals[v].Equals(normalsBefore[v]))
                {
                    normals[v] = avatarToMeshLinear.MultiplyVector(avatarNormals[v]).normalized;
                }
                if (avatarTangents != null && !avatarTangents[v].Equals(tangentsBefore[v]))
                {
                    Vector3 t = avatarToMeshLinear.MultiplyVector(new Vector3(avatarTangents[v].x, avatarTangents[v].y, avatarTangents[v].z)).normalized;
                    tangents[v] = new Vector4(t.x, t.y, t.z, avatarTangents[v].w);
                }
            }
        }

        private static void GetSourceWeights(UMAMeshData source, out byte[] bonesPerVertex, out BoneWeight1[] weights)
        {
            int vertexCount = source.vertices.Length;
            if (source.ManagedBonesPerVertex != null && source.ManagedBonesPerVertex.Length == vertexCount &&
                source.ManagedBoneWeights != null && source.ManagedBoneWeights.Length > 0)
            {
                bonesPerVertex = source.ManagedBonesPerVertex;
                weights = source.ManagedBoneWeights;
                return;
            }

            if (source.boneWeights != null && source.boneWeights.Length == vertexCount)
            {
                // Legacy four-influence weights. Convert without touching the source asset.
                bonesPerVertex = new byte[vertexCount];
                var list = new List<BoneWeight1>(vertexCount * 2);
                for (int v = 0; v < vertexCount; v++)
                {
                    UMABoneWeight bw = source.boneWeights[v];
                    float total = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                    if (total <= 0f)
                    {
                        bonesPerVertex[v] = 0;
                        continue;
                    }
                    byte count = 0;
                    if (bw.weight0 > 0f) { list.Add(new BoneWeight1 { boneIndex = bw.boneIndex0, weight = bw.weight0 / total }); count++; }
                    if (bw.weight1 > 0f) { list.Add(new BoneWeight1 { boneIndex = bw.boneIndex1, weight = bw.weight1 / total }); count++; }
                    if (bw.weight2 > 0f) { list.Add(new BoneWeight1 { boneIndex = bw.boneIndex2, weight = bw.weight2 / total }); count++; }
                    if (bw.weight3 > 0f) { list.Add(new BoneWeight1 { boneIndex = bw.boneIndex3, weight = bw.weight3 / total }); count++; }
                    bonesPerVertex[v] = count;
                }
                weights = list.ToArray();
                return;
            }

            throw new ArgumentException("Source mesh data has no bone weights.", nameof(source));
        }

        private static int ResolveTargetHash(int hash, UMARigRestPose source, UMARigRestPose target,
            Dictionary<int, UMATransform> slotDefinedBones, bool carrySlotBones, Dictionary<int, int> carriedParents,
            out bool movedToAncestor, out bool movedToRoot)
        {
            movedToAncestor = false;
            movedToRoot = false;
            if (target.Contains(hash))
            {
                return hash;
            }

            // Walk up the source hierarchy to the first bone the target knows.
            int ancestor = source.GetParentHash(hash);
            int guard = 0;
            while (ancestor != 0 && !target.Contains(ancestor) && guard++ < 512)
            {
                ancestor = source.GetParentHash(ancestor);
            }
            if (ancestor == 0 || !target.Contains(ancestor))
            {
                ancestor = target.RootBoneHash;
            }

            if (carrySlotBones && slotDefinedBones.ContainsKey(hash))
            {
                // The slot owns this bone: carry it, parented to the ancestor the target knows.
                // Intermediate slot-owned bones are carried too, each under its own parent.
                int current = hash;
                int parent = source.GetParentHash(current);
                while (current != 0 && !target.Contains(current))
                {
                    if (!carriedParents.ContainsKey(current))
                    {
                        int resolvedParent = (parent != 0 && (target.Contains(parent) || slotDefinedBones.ContainsKey(parent))) ? parent : ancestor;
                        carriedParents.Add(current, resolvedParent);
                    }
                    if (parent == 0 || target.Contains(parent) || !slotDefinedBones.ContainsKey(parent))
                    {
                        break;
                    }
                    current = parent;
                    parent = source.GetParentHash(current);
                }
                return hash;
            }

            if (ancestor == target.RootBoneHash)
            {
                movedToRoot = true;
            }
            else
            {
                movedToAncestor = true;
            }
            return ancestor;
        }

        private static int GetOrAddTargetBone(int hash, UMARigRestPose target, UMARigRestPose source, Dictionary<int, int> carriedParents,
            List<TargetBone> targetBones, Dictionary<int, int> indexByHash, UMASlotRigConversionReport report)
        {
            if (indexByHash.TryGetValue(hash, out int index))
            {
                return index;
            }

            var tb = new TargetBone { Hash = hash };
            if (target.TryGetAvatarMatrix(hash, out Matrix4x4 toAvatar))
            {
                tb.ToAvatar = toAvatar;
                tb.ParentHash = target.GetParentHash(hash);
            }
            else
            {
                tb.Carried = true;
                source.TryGetAvatarMatrix(hash, out tb.ToAvatar);
                tb.ParentHash = carriedParents.TryGetValue(hash, out int parent) ? parent : target.RootBoneHash;
                source.TryGetBone(hash, out tb.Definition);
                report.CarriedBones++;
                // Make sure the carried parent chain is in the table too, so umaBones is complete.
                if (tb.ParentHash != target.RootBoneHash && !target.Contains(tb.ParentHash))
                {
                    GetOrAddTargetBone(tb.ParentHash, target, source, carriedParents, targetBones, indexByHash, report);
                }
            }

            index = targetBones.Count;
            targetBones.Add(tb);
            indexByHash.Add(hash, index);
            return index;
        }

        private static void ComputeCorrections(UMARigRestPose source, UMARigRestPose target, List<TargetBone> targetBones,
            UMASlotRigConversionProfile profile, string profileSlot, BoneCorrection[] corrections, UMASlotRigConversionReport report)
        {
            for (int j = 0; j < targetBones.Count; j++)
            {
                if (targetBones[j].Carried)
                {
                    corrections[j] = BoneCorrection.Identity;
                    continue;
                }
                corrections[j] = ApplyProfileOffsets(
                    ComputeAnchorCorrection(source, target, targetBones[j].Hash, profile, profileSlot, report, true),
                    targetBones[j].Hash, target, profile, profileSlot);
            }
        }

        /// <summary>
        /// The computed correction for one bone: rotate about the source joint by the change in bone
        /// direction, then move the joint onto the target joint. Leaves and bones without a usable
        /// direction inherit their parent's rotation, so helper bones follow the limb they belong to.
        /// A profile's modes are honoured here; its offsets are added by <see cref="ApplyProfileOffsets"/>.
        /// </summary>
        /// <param name="recordJointDelta">
        /// Record this bone's joint delta in the report. False when the call only borrows a parent's
        /// rotation: that parent's joint is not applied to the slot, so it must not be reported.
        /// </param>
        private static BoneCorrection ComputeAnchorCorrection(UMARigRestPose source, UMARigRestPose target, int hash,
            UMASlotRigConversionProfile profile, string profileSlot, UMASlotRigConversionReport report, bool recordJointDelta)
        {
            BoneCorrection identity = BoneCorrection.Identity;
            int guard = 0;
            int current = hash;
            UMARigBoneCorrectionMode mode = profile != null ? profile.defaultMode : UMARigBoneCorrectionMode.Full;

            // Find the first bone up the chain whose mode is not InheritParent.
            while (guard++ < 512)
            {
                if (current == 0 || current == target.RootBoneHash)
                {
                    return identity;
                }
                if (!source.Contains(current))
                {
                    // Bone exists only on the target: nothing to anchor against; keep going up.
                    current = target.GetParentHash(current);
                    continue;
                }

                UMARigBoneCorrectionMode currentMode = mode;
                if (profile != null && profile.TryGetCorrection(current, profileSlot, out UMARigBoneCorrection entry))
                {
                    currentMode = entry.mode;
                }

                if (currentMode == UMARigBoneCorrectionMode.None)
                {
                    return identity;
                }
                if (currentMode == UMARigBoneCorrectionMode.InheritParent)
                {
                    current = target.GetParentHash(current);
                    continue;
                }

                source.TryGetAvatarPosition(current, out Vector3 sourceJoint);
                target.TryGetAvatarPosition(current, out Vector3 targetJoint);
                float delta = Vector3.Distance(sourceJoint, targetJoint);
                // "current" is the bone whose joint anchors the correction: the bone itself, or the
                // ancestor it inherits from. Either way this is the joint the slot's vertices follow.
                if (recordJointDelta && delta > report.MaxJointDelta)
                {
                    report.MaxJointDelta = delta;
                    report.MaxJointDeltaBone = target.TryGetBone(current, out UMATransform def) ? def.name : current.ToString();
                }

                Quaternion rotation = Quaternion.identity;
                if (currentMode == UMARigBoneCorrectionMode.Full)
                {
                    if (!TryGetDirectionRotation(source, target, current, sourceJoint, targetJoint, out rotation))
                    {
                        // Leaf or degenerate: inherit the parent's rotation, keep this joint's translation.
                        BoneCorrection parent = ComputeAnchorCorrection(source, target, target.GetParentHash(current), profile, profileSlot, report, false);
                        rotation = parent.Rotation;
                    }
                }

                Matrix4x4 rotate = Matrix4x4.Rotate(rotation);
                return new BoneCorrection
                {
                    Linear = rotate,
                    NormalLinear = rotate,
                    Rotation = rotation,
                    Affine = Matrix4x4.Translate(targetJoint) * rotate * Matrix4x4.Translate(-sourceJoint),
                };
            }
            return identity;
        }

        /// <summary>
        /// Adds a profile's offsets to a bone's computed correction: the bone's own entry, then the
        /// entry of each bone above it that carries its offsets down to its children. Where the slot
        /// has an entry of its own for a bone, that entry stands in for the general one. Each entry acts
        /// about its own bone's target joint, and a parent's acts after its child's, so the result is
        /// what moving, turning or scaling those bones would do to everything below them.
        /// </summary>
        private static BoneCorrection ApplyProfileOffsets(BoneCorrection computed, int hash, UMARigRestPose target,
            UMASlotRigConversionProfile profile, string profileSlot)
        {
            if (profile == null || !profile.HasAnyCorrections(profileSlot))
            {
                return computed;
            }

            Matrix4x4 affine = Matrix4x4.identity;
            Matrix4x4 linear = Matrix4x4.identity;
            Matrix4x4 normalLinear = Matrix4x4.identity;
            bool any = false;
            bool self = true;
            int current = hash;
            int guard = 0;
            while (current != 0 && current != target.RootBoneHash && guard++ < 512)
            {
                if (profile.TryGetCorrection(current, profileSlot, out UMARigBoneCorrection entry) && entry.HasOffsets && (self || entry.includeChildren))
                {
                    BuildOffset(entry, target, current, out Matrix4x4 entryAffine, out Matrix4x4 entryLinear, out Matrix4x4 entryNormalLinear);
                    // Bones further up act last.
                    affine = entryAffine * affine;
                    linear = entryLinear * linear;
                    normalLinear = entryNormalLinear * normalLinear;
                    any = true;
                }
                self = false;
                current = target.GetParentHash(current);
            }
            if (!any)
            {
                return computed;
            }

            computed.Affine = affine * computed.Affine;
            computed.Linear = linear * computed.Linear;
            computed.NormalLinear = normalLinear * computed.NormalLinear;
            return computed;
        }

        /// <summary>
        /// One profile entry as avatar-space matrices: scale along the bone's own rest axes, then
        /// rotate, both about the bone's target joint, then shift.
        /// </summary>
        private static void BuildOffset(UMARigBoneCorrection entry, UMARigRestPose target, int boneHash,
            out Matrix4x4 affine, out Matrix4x4 linear, out Matrix4x4 normalLinear)
        {
            target.TryGetAvatarPosition(boneHash, out Vector3 joint);
            Matrix4x4 rotate = Matrix4x4.Rotate(Quaternion.Euler(entry.rotationOffsetEuler));
            linear = rotate;
            normalLinear = rotate;
            if (entry.HasScale)
            {
                Vector3 scale = entry.SafeScale;
                target.TryGetAvatarMatrix(boneHash, out Matrix4x4 boneToAvatar);
                Matrix4x4 frame = Matrix4x4.Rotate(UMARigRestPose.NormalizeSafe(boneToAvatar.rotation));
                Matrix4x4 frameInverse = frame.transpose;
                linear = rotate * frame * Matrix4x4.Scale(scale) * frameInverse;
                normalLinear = rotate * frame * Matrix4x4.Scale(new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z)) * frameInverse;
            }
            affine = Matrix4x4.Translate(entry.positionOffset) * Matrix4x4.Translate(joint) * linear * Matrix4x4.Translate(-joint);
        }

        private static bool TryGetDirectionRotation(UMARigRestPose source, UMARigRestPose target, int hash,
            Vector3 sourceJoint, Vector3 targetJoint, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            IReadOnlyList<int> children = target.GetChildren(hash);
            Vector3 sourceDirection = Vector3.zero;
            Vector3 targetDirection = Vector3.zero;
            for (int c = 0; c < children.Count; c++)
            {
                int child = children[c];
                if (!source.Contains(child))
                {
                    continue;
                }
                source.TryGetAvatarPosition(child, out Vector3 sourceChild);
                target.TryGetAvatarPosition(child, out Vector3 targetChild);
                sourceDirection += sourceChild - sourceJoint;
                targetDirection += targetChild - targetJoint;
            }

            const float minLength = 1e-4f;
            if (sourceDirection.sqrMagnitude < minLength * minLength || targetDirection.sqrMagnitude < minLength * minLength)
            {
                return false;
            }
            rotation = Quaternion.FromToRotation(sourceDirection.normalized, targetDirection.normalized);
            return true;
        }

        /// <summary>
        /// Deep-copies the triangle lists, including any appended LOD buffers and their ranges.
        /// With <paramref name="reverseWinding"/> every triangle's order is reversed.
        /// </summary>
        private static SubMeshTriangles[] CopySubmeshes(SubMeshTriangles[] source, bool reverseWinding)
        {
            if (source == null)
            {
                return null;
            }
            var result = new SubMeshTriangles[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                SubMeshTriangles submesh = source[i];
                if (submesh == null)
                {
                    continue;
                }

                // A negative LOD number returns the whole managed buffer rather than one LOD's slice.
                int[] triangles = submesh.getManagedTriangles(-1);
                int[] copy = null;
                if (triangles != null)
                {
                    copy = (int[])triangles.Clone();
                }
                else if (submesh.nativeTriangles.IsCreated)
                {
                    copy = submesh.nativeTriangles.ToArray();
                }
                if (reverseWinding && copy != null)
                {
                    // LOD ranges are whole triangles, so swapping within each triple is safe for all of them.
                    for (int t = 0; t + 2 < copy.Length; t += 3)
                    {
                        int swap = copy[t + 1];
                        copy[t + 1] = copy[t + 2];
                        copy[t + 2] = swap;
                    }
                }

                var copied = new SubMeshTriangles(copy);
                if (submesh.lodRanges != null)
                {
                    copied.lodRanges = new List<UMALodRange>(submesh.lodRanges.Count);
                    for (int r = 0; r < submesh.lodRanges.Count; r++)
                    {
                        UMALodRange range = submesh.lodRanges[r];
                        copied.lodRanges.Add(range != null ? new UMALodRange(range.offset, range.count) : null);
                    }
                }
                result[i] = copied;
            }
            return result;
        }

        private static UMABlendShape[] ConvertBlendShapes(UMABlendShape[] shapes, int vertexCount, Matrix4x4[] vertexLinear,
            Matrix4x4[] vertexNormalLinear, float[] normalScale, float[] tangentScale, UMASlotRigConversionReport report)
        {
            if (shapes == null)
            {
                return null;
            }
            report.BlendShapeCount = shapes.Length;
            var result = new UMABlendShape[shapes.Length];
            for (int s = 0; s < shapes.Length; s++)
            {
                UMABlendShape shape = shapes[s];
                if (shape == null)
                {
                    continue;
                }
                var outShape = new UMABlendShape { shapeName = shape.shapeName };
                if (shape.frames != null)
                {
                    outShape.frames = new UMABlendFrame[shape.frames.Length];
                    for (int f = 0; f < shape.frames.Length; f++)
                    {
                        UMABlendFrame frame = shape.frames[f];
                        if (frame == null)
                        {
                            continue;
                        }
                        var outFrame = new UMABlendFrame { frameWeight = frame.frameWeight };
                        outFrame.deltaVertices = TransformDeltas(frame.deltaVertices, vertexCount, vertexLinear, null, shape.shapeName, report);
                        outFrame.deltaNormals = TransformDeltas(frame.deltaNormals, vertexCount, vertexNormalLinear, normalScale, shape.shapeName, report);
                        outFrame.deltaTangents = TransformDeltas(frame.deltaTangents, vertexCount, vertexLinear, tangentScale, shape.shapeName, report);
                        outShape.frames[f] = outFrame;
                    }
                }
                result[s] = outShape;
            }
            return result;
        }

        private static Vector3[] TransformDeltas(Vector3[] deltas, int vertexCount, Matrix4x4[] vertexMatrix, float[] scale,
            string shapeName, UMASlotRigConversionReport report)
        {
            if (deltas == null)
            {
                return null;
            }
            if (deltas.Length == 0)
            {
                return Array.Empty<Vector3>();
            }
            if (deltas.Length != vertexCount)
            {
                report.Warnings.Add($"Blend shape '{shapeName}' has {deltas.Length} deltas for {vertexCount} vertices; copied unchanged.");
                return (Vector3[])deltas.Clone();
            }
            var result = new Vector3[vertexCount];
            for (int v = 0; v < vertexCount; v++)
            {
                Vector3 transformed = vertexMatrix[v].MultiplyVector(deltas[v]);
                result[v] = scale != null ? transformed * scale[v] : transformed;
            }
            return result;
        }

        private static UMATransform[] BuildOutputBones(UMARigRestPose target, List<TargetBone> targetBones)
        {
            var output = new Dictionary<int, UMATransform>();
            for (int j = 0; j < targetBones.Count; j++)
            {
                TargetBone tb = targetBones[j];
                if (tb.Carried)
                {
                    if (output.ContainsKey(tb.Hash))
                    {
                        continue;
                    }
                    // Re-express the carried bone under its target parent, preserving its avatar-space frame.
                    Matrix4x4 parentToAvatar;
                    if (!target.TryGetAvatarMatrix(tb.ParentHash, out parentToAvatar))
                    {
                        int parentIndex = targetBones.FindIndex(b => b.Hash == tb.ParentHash);
                        parentToAvatar = parentIndex >= 0 ? targetBones[parentIndex].ToAvatar : target.RigRootToAvatar;
                    }
                    Matrix4x4 local = parentToAvatar.inverse * tb.ToAvatar;
                    var def = new UMATransform
                    {
                        hash = tb.Hash,
                        parent = tb.ParentHash,
                        name = tb.Definition != null ? tb.Definition.name : tb.Hash.ToString(),
                        position = local.GetColumn(3),
                        rotation = UMARigRestPose.NormalizeSafe(local.rotation),
                        scale = local.lossyScale,
                    };
                    output.Add(tb.Hash, def);
                    AddAncestors(target, tb.ParentHash, output);
                }
                else
                {
                    AddAncestors(target, tb.Hash, output);
                }
            }

            var array = new UMATransform[output.Count];
            output.Values.CopyTo(array, 0);
            Array.Sort(array, UMATransform.TransformComparer);
            return array;
        }

        private static void AddAncestors(UMARigRestPose target, int hash, Dictionary<int, UMATransform> output)
        {
            int guard = 0;
            while (hash != 0 && hash != target.RootBoneHash && guard++ < 512)
            {
                if (output.ContainsKey(hash) || !target.TryGetBone(hash, out UMATransform bone))
                {
                    return;
                }
                output.Add(hash, bone.Duplicate());
                hash = bone.parent;
            }
        }

        private static void SortByWeightDescending(int[] indices, int count, float[] weights)
        {
            // Insertion sort: influence counts per vertex are small.
            for (int i = 1; i < count; i++)
            {
                int key = indices[i];
                float keyWeight = weights[key];
                int j = i - 1;
                while (j >= 0 && (weights[indices[j]] < keyWeight || (weights[indices[j]] == keyWeight && indices[j] > key)))
                {
                    indices[j + 1] = indices[j];
                    j--;
                }
                indices[j + 1] = key;
            }
        }

        private static Matrix4x4 Zero()
        {
            return new Matrix4x4(Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero);
        }

        private static void AddScaled(ref Matrix4x4 accumulator, Matrix4x4 m, float weight)
        {
            for (int i = 0; i < 16; i++)
            {
                accumulator[i] += m[i] * weight;
            }
        }

        private static void Scale(ref Matrix4x4 m, float factor)
        {
            for (int i = 0; i < 16; i++)
            {
                m[i] *= factor;
            }
        }

        private static Matrix4x4 LinearPart(Matrix4x4 m)
        {
            m.m03 = 0f;
            m.m13 = 0f;
            m.m23 = 0f;
            m.m30 = 0f;
            m.m31 = 0f;
            m.m32 = 0f;
            m.m33 = 1f;
            return m;
        }
    }
}
