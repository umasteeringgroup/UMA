using System;
using System.Collections.Generic;
using UMA.CharacterSystem;
using UMA.Editors;
using UMA.Editors.Weighting;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// Touchup Weights: Copy Weights from the Body. Gives the slot being weighted (all of it, or the selected
    /// vertices) new weights from the character's other slots with the shared weighting library
    /// (<see cref="UMAWeightTransfer"/>), staged like painted weights: they show at once, are one Undo step, and
    /// are written to the slot only on Save.
    /// </summary>
    public partial class VertexEditorStage
    {
        /// <summary>What the copy uses.</summary>
        internal sealed class TouchupCopyOptions
        {
            public UMAWeightSettings Settings = new UMAWeightSettings();
            /// <summary>Only the selected vertices get new weights; the others keep theirs and the new ones blend into them.</summary>
            public bool SelectedOnly;
            /// <summary>Slot names to copy from. Null copies from every other slot.</summary>
            public HashSet<string> DonorSlots;
            /// <summary>Score the result in the standard test poses (Auto always does).</summary>
            public bool Score = true;
        }

        /// <summary>The other slots of the character, which weights can be copied from.</summary>
        internal List<SlotData> GetTouchupDonorSlots()
        {
            var result = new List<SlotData>();
            if (thisDCA == null || thisDCA.umaData == null || thisDCA.umaData.umaRecipe == null || thisDCA.umaData.umaRecipe.slotDataList == null) return result;
            var seen = new HashSet<string>();
            foreach (SlotData slot in thisDCA.umaData.umaRecipe.slotDataList)
            {
                if (slot == null || slot.asset == null || ReferenceEquals(slot, touchupWeightSlot) || slot.asset.isUtilitySlot) continue;
                if (UMAMeshData.IsNullOrEmptyMeshData(slot.asset.meshData)) continue;
                if (seen.Add(slot.slotName)) result.Add(slot);
            }
            return result;
        }

        /// <summary>The last copy's report, for the window to show.</summary>
        internal UMAWeightReport LastTouchupCopyReport { get; private set; }

        internal bool TryCopyTouchupWeightsFromBody(TouchupCopyOptions options, out string statusMessage)
        {
            statusMessage = string.Empty;
            LastTouchupCopyReport = null;
            if (!touchupWeightsMode || touchupWeightSlot == null || touchupWeightSlot.asset == null) { statusMessage = "Choose a slot first."; return false; }
            if (!TryGetSlotMeshData(touchupWeightSlot, out UMAMeshData meshData, out statusMessage) || !EnsureTouchupPaintData(out statusMessage)) return false;
            if (thisDCA == null || thisDCA.umaData == null) { statusMessage = "The character is no longer available."; return false; }
            options = options ?? new TouchupCopyOptions();

            SlotData[] slots = thisDCA.umaData.umaRecipe.slotDataList;
            int activeIndex = -1;
            for (int i = 0; i < slots.Length; i++) if (ReferenceEquals(slots[i], touchupWeightSlot)) { activeIndex = i; break; }
            if (activeIndex < 0) { statusMessage = "The slot is not part of the character any more."; return false; }

            List<VertexSelection> selection = GetTouchupSelectedVertices();
            if (options.SelectedOnly && selection.Count == 0) { statusMessage = "Select the vertices to weight first, or copy to the whole slot."; return false; }

            // Work in the character's own pose: the bones Pose Mode turned go back for the copy and are turned again after.
            var posed = new List<(Transform bone, Quaternion rotation)>();
            foreach (KeyValuePair<Transform, Quaternion> pair in touchupPoseOriginal)
            {
                if (pair.Key == null) continue;
                posed.Add((pair.Key, pair.Key.localRotation));
                pair.Key.localRotation = pair.Value;
            }
            try
            {
                SkinnedMeshRenderer[] renderers = UMASlotSkinTransfer.GetLiveTargetRenderers(thisDCA);
                int rendererIndex = Mathf.Clamp(touchupWeightSlot.skinnedMeshRenderer, 0, Mathf.Max(0, renderers.Length - 1));
                UMASlotSkinTransfer.TargetData skin = UMASlotSkinTransfer.CaptureTargetData(thisDCA, rendererIndex, false);
                UMASlotSkinTransfer.TargetSurface surface = UMASlotSkinTransfer.BuildTargetSurface(skin);

                // The donor: every other chosen slot's triangles (the slot itself left out).
                HashSet<string> donors = options.DonorSlots;
                UMAWeightDonor donor = UMAWeightDonor.FromSurface(surface, skin, 0, t =>
                {
                    if (t.SlotIndex < 0 || t.SlotIndex >= slots.Length || t.SlotIndex == activeIndex) return false;
                    SlotData s = slots[t.SlotIndex];
                    return s != null && (donors == null || donors.Contains(s.slotName));
                });
                if (donor.TriangleCount == 0) { statusMessage = "None of the chosen slots has a surface to copy from."; return false; }

                // The slot's own vertices where the character shows them, baked as the capture was (vertices whose
                // triangles a mesh hide removed are still in the bake).
                int count = meshData.vertexCount;
                var positions = new Vector3[count];
                var baked = new Mesh();
                try
                {
                    skin.Renderer.BakeMesh(baked);
                    Vector3[] all = baked.vertices;
                    if (touchupWeightSlot.vertexOffset + count > all.Length) { statusMessage = "The slot's vertices could not be found in the character's mesh."; return false; }
                    Array.Copy(all, touchupWeightSlot.vertexOffset, positions, 0, count);
                }
                finally
                {
                    DestroyImmediate(baked);
                }

                var triangles = new List<int>();
                if (meshData.submeshes != null) foreach (var sub in meshData.submeshes) if (sub != null) triangles.AddRange(sub.GetBaseTriangles());

                // Bone tables: the slot's indices to the capture's (by name hash) and back.
                var captureOfHash = new Dictionary<int, int>();
                for (int b = 0; b < skin.BoneNameHashes.Length; b++) if (!captureOfHash.ContainsKey(skin.BoneNameHashes[b])) captureOfHash[skin.BoneNameHashes[b]] = b;
                donor.FallbackBone = FallbackCaptureBone(meshData, captureOfHash);

                var current = new BoneWeight1[count][];
                for (int v = 0; v < count; v++)
                {
                    var list = new List<BoneWeight1>();
                    List<BoneWeight1> working = touchupWorkingWeights[v];
                    if (working != null)
                    {
                        foreach (BoneWeight1 w in working)
                        {
                            if (meshData.boneNameHashes == null || (uint)w.boneIndex >= (uint)meshData.boneNameHashes.Length) continue;
                            if (captureOfHash.TryGetValue(meshData.boneNameHashes[w.boneIndex], out int b)) list.Add(new BoneWeight1 { boneIndex = b, weight = w.weight });
                        }
                    }
                    current[v] = list.ToArray();
                }

                bool[] locked = null;
                if (options.SelectedOnly)
                {
                    var chosen = new HashSet<int>();
                    foreach (VertexSelection s in selection) chosen.Add(s.vertexIndexOnSlot);
                    AddTouchupBuddies(chosen);
                    locked = new bool[count];
                    for (int v = 0; v < count; v++) locked[v] = !chosen.Contains(v);
                }

                var target = new UMAWeightTarget
                {
                    Positions = positions,
                    Triangles = triangles.ToArray(),
                    UVs = meshData.uv != null && meshData.uv.Length == count ? meshData.uv : null,
                    CurrentWeights = current,
                    Locked = locked
                };
                UMAWeightSettings settings = options.Settings.Clone();
                if (settings.MirrorLeftRight) settings.SetMirrorFrom(skin.Renderer.transform, thisDCA.transform);
                if (settings.Method == UMAWeightMethod.ExactMatch || settings.Method == UMAWeightMethod.Auto) donor.SetSlotUVs(skin, surface);
                if (settings.Method == UMAWeightMethod.Auto || options.Score)
                {
                    settings.Poses = UMAWeightPoses.Capture(skin, donor, null, out string poseMessage);
                    if (settings.Poses.Count == 0 && !string.IsNullOrEmpty(poseMessage)) statusMessage = poseMessage + " ";
                }

                UMAWeightResult result = UMAWeightTransfer.Transfer(donor, target, settings);
                LastTouchupCopyReport = result.Report;
                BoneWeight1[][] perVertex = result.PerVertex();

                // Back to the slot's bones: bind any bone the slot does not use yet, then stage the new weights.
                var entries = new List<VertexWeightEntry>[count];
                var allEntries = new List<VertexWeightEntry>();
                var entryOfBone = new Dictionary<int, VertexWeightEntry>();
                for (int v = 0; v < count; v++)
                {
                    if (locked != null && locked[v]) continue;
                    entries[v] = new List<VertexWeightEntry>(perVertex[v].Length);
                    foreach (BoneWeight1 w in perVertex[v])
                    {
                        if ((uint)w.boneIndex >= (uint)skin.Bones.Length) continue;
                        int hash = skin.BoneNameHashes[w.boneIndex];
                        entries[v].Add(new VertexWeightEntry { boneHash = hash, boneName = skin.Bones[w.boneIndex].name, weight = w.weight });
                        if (!entryOfBone.ContainsKey(hash))
                        {
                            var e = new VertexWeightEntry { boneHash = hash, boneName = skin.Bones[w.boneIndex].name, weight = 1f };
                            entryOfBone[hash] = e;
                            allEntries.Add(e);
                        }
                    }
                }
                bool needsBinding = false;
                foreach (VertexWeightEntry e in allEntries) if (GetBoundBoneIndex(meshData, e.boneHash) < 0) { needsBinding = true; break; }
                Undo.RegisterCompleteObjectUndo(this, "Copy Weights from Body");
                if (needsBinding) Undo.RecordObject(touchupWeightSlot.asset, "Bind Copied Weight Bones");
                if (!EnsureEditedWeightBonesAreBound(meshData, allEntries, out statusMessage)) return false;

                var changed = new List<int>();
                for (int v = 0; v < count; v++)
                {
                    if (entries[v] == null || entries[v].Count == 0) continue;
                    foreach (VertexWeightEntry e in entries[v]) e.boneIndex = entryOfBone[e.boneHash].boneIndex;
                    List<BoneWeight1> weights = BuildTargetBoneWeights(meshData, entries[v], out string message);
                    if (weights == null) continue;
                    if (BoneWeightListsApproximatelyEqual(touchupWorkingWeights[v], weights)) continue;
                    touchupWorkingWeights[v] = weights;
                    touchupPendingPaintWeights[v] = CloneBoneWeightList(weights);
                    changed.Add(v);
                }
                touchupPreviewWeights = null;
                touchupPreviewVertexIndices.Clear();
                SyncTouchupPendingPaintUndoState();
                touchupWeightsRevision++;
                statusMessage += result.Report.Summary() + " " + changed.Count + " vertices changed; save to keep them.";
                touchupPaintStatusType = MessageType.Info;
                touchupPaintStatusMessage = statusMessage;
                RecalculateTouchupVertexPositions(changed);
                RefreshTouchupWeightVisualization();
                RepaintLinkedEditors();
                SceneView.RepaintAll();
                return true;
            }
            catch (Exception e)
            {
                statusMessage = "Copy Weights from Body failed: " + e.Message;
                Debug.LogException(e);
                return false;
            }
            finally
            {
                foreach ((Transform bone, Quaternion rotation) in posed) if (bone != null) bone.localRotation = rotation;
            }
        }

        /// <summary>The capture bone the slot is mostly weighted to, for vertices with no body near.</summary>
        private int FallbackCaptureBone(UMAMeshData meshData, Dictionary<int, int> captureOfHash)
        {
            int hash = touchupWeightBoneHash != 0 ? touchupWeightBoneHash : FindDominantTouchupBoneHash(touchupWeightSlot);
            return hash != 0 && captureOfHash.TryGetValue(hash, out int b) ? b : 0;
        }
    }
}
