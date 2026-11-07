using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.PoseTools
{
    /// <summary>Owns an isolated character preview and offsets from a frozen race T-pose.</summary>
    public sealed class UMARaceTPoseEditSession : IDisposable
    {
        public UMAData Source { get; private set; }
        public RaceData Race { get; private set; }
        public UmaTPose Destination { get; private set; }
        public UMARaceTPoseEditPose Pose { get; private set; }
        public int MatchedBones => Pose?.preview?.skeleton == null || baseline == null ? 0 :
            baseline.boneInfo.Count(b => Pose.preview.skeleton.GetBoneTransform(b.name) != null);
        public int TotalBones => baseline == null ? 0 : baseline.boneInfo.Length;
        public byte[] BaselineChunk => (byte[])baseline.serializedChunk.Clone();
        public byte[] ExpectedChunk => (byte[])originalChunk.Clone();
        private UmaTPose baseline;
        private GameObject previewRoot;
        private byte[] originalChunk;
        private SceneView framedView;
        private Vector3 previousPivot;
        private Quaternion previousRotation;
        private float previousSize;
        private bool previousOrthographic;
        private readonly List<Object> ownedResources = new List<Object>();

        public static RaceData GetRace(UMAData source)
        {
            if (source is CharacterSystem.DynamicCharacterAvatar avatar && avatar.activeRace != null)
            {
                if (avatar.activeRace.racedata == null) avatar.activeRace.SetRaceData();
                if (avatar.activeRace.racedata != null) return avatar.activeRace.racedata;
            }
            return source == null ? null : source.umaRecipe?.raceData;
        }

        public static UMARaceTPoseEditSession Create(UMAData source, byte[] baselineChunk = null, byte[] expectedChunk = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Edit a race T-pose outside Play mode.");
            var race = GetRace(source);
            if (source == null || source.skeleton == null || source.umaRoot == null)
                throw new InvalidOperationException("Select a built UMA character with a skeleton.");
            if (race == null || race.TPose == null || race.TPose.serializedChunk == null || race.TPose.serializedChunk.Length == 0)
                throw new InvalidOperationException("The character's race has no serialized T-pose to edit.");
            if (expectedChunk != null && !expectedChunk.SequenceEqual(race.TPose.serializedChunk))
                throw new InvalidOperationException("The saved T-pose changed while the editor was reloading. The previous draft cannot be restored onto a different baseline.");

            var session = new UMARaceTPoseEditSession { Source = source, Race = race, Destination = race.TPose };
            try
            {
                session.originalChunk = (byte[])race.TPose.serializedChunk.Clone();
                session.baseline = Object.Instantiate(race.TPose);
                session.baseline.hideFlags = HideFlags.HideAndDontSave;
                if (baselineChunk != null) session.baseline.serializedChunk = (byte[])baselineChunk.Clone();
                session.baseline.boneInfo = null;
                session.baseline.DeSerialize();
                if (session.baseline.boneInfo.Length == 0)
                    throw new InvalidOperationException("The race T-pose contains no bones.");
                session.BuildPreview();
                return session;
            }
            catch { session.Dispose(); throw; }
        }

        private void BuildPreview()
        {
            // Copy transforms and renderers explicitly: cloning a DCA would execute its build lifecycle.
            var map = new Dictionary<Transform, Transform>();
            previewRoot = new GameObject(Source.name + " - Race T-Pose Preview") { hideFlags = HideFlags.HideAndDontSave };
            previewRoot.transform.SetPositionAndRotation(Source.transform.position, Source.transform.rotation);
            previewRoot.transform.localScale = Source.transform.lossyScale;
            map.Add(Source.transform, previewRoot.transform);
            foreach (Transform child in Source.transform) CopyHierarchy(child, previewRoot.transform, map);
            var skeletonRoot = Source.skeleton.GetBoneTransform(Source.skeleton.rootBoneHash);
            if (skeletonRoot == null || !map.TryGetValue(skeletonRoot, out var previewSkeletonRoot))
                throw new InvalidOperationException("The character uses an external skeleton. Build a character with its own skeleton to edit this T-pose.");

            var matching = new List<UMABonePose.PoseBone>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bone in baseline.boneInfo)
            {
                if (string.IsNullOrEmpty(bone.name) || !names.Add(bone.name))
                    throw new InvalidOperationException("The race T-pose contains an empty or duplicate bone name.");
                var original = Source.skeleton.GetBoneTransform(bone.name);
                if (original == null || !map.TryGetValue(original, out var copy)) continue;
                copy.localPosition = bone.position;
                copy.localRotation = bone.rotation;
                copy.localScale = bone.scale;
                matching.Add(new UMABonePose.PoseBone { bone = bone.name, hash = UMAUtils.StringToHash(bone.name),
                    position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one, enabled = true });
            }
            if (matching.Count == 0) throw new InvalidOperationException("No race T-pose bones match this character's skeleton.");

            var textures = new Dictionary<Texture, Texture>();
            foreach (var renderer in Source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null || !map.TryGetValue(renderer.transform, out var copy)) continue;
                var preview = copy.gameObject.AddComponent<SkinnedMeshRenderer>();
                var mesh = Object.Instantiate(renderer.sharedMesh);
                mesh.hideFlags = HideFlags.HideAndDontSave;
                ownedResources.Add(mesh);
                preview.sharedMesh = mesh;
                preview.sharedMaterials = renderer.sharedMaterials.Select(material =>
                {
                    if (material == null) return null;
                    var clone = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                    ownedResources.Add(clone);
                    foreach (var property in material.GetTexturePropertyNames())
                        clone.SetTexture(property, CopyPreviewTexture(material.GetTexture(property), textures));
                    return clone;
                }).ToArray();
                preview.bones = renderer.bones.Select(b => b != null && map.TryGetValue(b, out var mapped) ? mapped : null).ToArray();
                preview.rootBone = renderer.rootBone != null && map.TryGetValue(renderer.rootBone, out var root) ? root : previewSkeletonRoot;
                preview.localBounds = renderer.localBounds;
                preview.updateWhenOffscreen = true;
                preview.enabled = renderer.enabled;
                for (int i = 0; i < mesh.blendShapeCount; i++) preview.SetBlendShapeWeight(i, renderer.GetBlendShapeWeight(i));
            }
            // The preview owns its mesh copies; UMAData must never clean shared source resources.
            var data = previewRoot.AddComponent<UMAData>();
            data.enabled = false;
            data.staticCharacter = true;
            data.umaRoot = map[Source.umaRoot.transform].gameObject;
            data.skeleton = new UMASkeleton(previewSkeletonRoot);
            Pose = ScriptableObject.CreateInstance<UMARaceTPoseEditPose>();
            Pose.name = Race.raceName + " - Temporary T-Pose";
            Pose.hideFlags = HideFlags.HideAndDontSave;
            Pose.preview = data;
            Pose.poses = matching.ToArray();
            Pose.baselineScales = matching.ToDictionary(p => p.bone, p => data.skeleton.GetScale(p.hash), StringComparer.Ordinal);
            // Show the editable copy beside the untouched source character.
            var bounds = new Bounds(Source.transform.position, Vector3.one);
            foreach (var renderer in Source.GetComponentsInChildren<SkinnedMeshRenderer>()) bounds.Encapsulate(renderer.bounds);
            previewRoot.transform.position += Source.transform.right * (bounds.size.x + 0.5f);
        }

        private Texture CopyPreviewTexture(Texture source, Dictionary<Texture, Texture> textures)
        {
            // Asset textures can be shared. Generated atlases cannot: rebuilding the source
            // releases them even though the preview's cloned materials still reference them.
            if (source == null || EditorUtility.IsPersistent(source)) return source;
            if (textures.TryGetValue(source, out var existing)) return existing;

            Texture copy;
            if (source is RenderTexture atlas)
            {
                if (!atlas.IsCreated())
                    throw new InvalidOperationException("The character's generated atlas has been released. Rebuild the character before editing its T-pose.");
                var snapshot = new RenderTexture(atlas.descriptor);
                ownedResources.Add(snapshot);
                snapshot.Create();
                Graphics.CopyTexture(atlas, snapshot);
                copy = snapshot;
            }
            else
            {
                copy = Object.Instantiate(source);
                ownedResources.Add(copy);
            }
            copy.name = source.name + " - T-Pose Preview";
            copy.hideFlags = HideFlags.HideAndDontSave;
            copy.filterMode = source.filterMode;
            copy.wrapModeU = source.wrapModeU;
            copy.wrapModeV = source.wrapModeV;
            copy.wrapModeW = source.wrapModeW;
            copy.anisoLevel = source.anisoLevel;
            copy.mipMapBias = source.mipMapBias;
            textures.Add(source, copy);
            return copy;
        }

        private static void CopyHierarchy(Transform source, Transform parent, Dictionary<Transform, Transform> map)
        {
            var go = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave, layer = source.gameObject.layer };
            var copy = go.transform;
            copy.SetParent(parent, false);
            copy.localPosition = source.localPosition;
            copy.localRotation = source.localRotation;
            copy.localScale = source.localScale;
            go.SetActive(source.gameObject.activeSelf);
            map.Add(source, copy);
            foreach (Transform child in source) CopyHierarchy(child, copy, map);
        }

        public void FramePreview()
        {
            if (previewRoot == null || SceneView.lastActiveSceneView == null) return;
            if (framedView == null)
            {
                framedView = SceneView.lastActiveSceneView;
                previousPivot = framedView.pivot;
                previousRotation = framedView.rotation;
                previousSize = framedView.size;
                previousOrthographic = framedView.orthographic;
            }
            var bounds = new Bounds(previewRoot.transform.position + Vector3.up, Vector3.one);
            foreach (var renderer in previewRoot.GetComponentsInChildren<SkinnedMeshRenderer>()) bounds.Encapsulate(renderer.bounds);
            SceneView.lastActiveSceneView.Frame(bounds, false);
        }

        public readonly struct CopyResult
        {
            public readonly int Copied, Reset, Ignored, WithoutPreview;
            public CopyResult(int copied, int reset, int ignored, int withoutPreview)
            { Copied = copied; Reset = reset; Ignored = ignored; WithoutPreview = withoutPreview; }
        }

        /// <summary>Replaces the draft's transforms, while retaining the destination and frozen baseline.</summary>
        public CopyResult CopyFromRace(RaceData donor)
        {
            if (Pose == null || baseline == null)
                throw new InvalidOperationException("This T-pose editing session has ended.");
            if (donor == null || donor.TPose == null || donor.TPose.serializedChunk == null || donor.TPose.serializedChunk.Length == 0)
                throw new InvalidOperationException("Choose a race with a saved T-pose to copy.");

            // Decode a private copy: UmaTPose's decoded fields are caches, not serialized asset data.
            var copy = Object.Instantiate(donor.TPose);
            copy.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                copy.boneInfo = null;
                copy.humanInfo = null;
                copy.DeSerialize();
                var donorBones = new Dictionary<string, SkeletonBone>(StringComparer.Ordinal);
                foreach (var bone in copy.boneInfo ?? Array.Empty<SkeletonBone>())
                {
                    if (string.IsNullOrEmpty(bone.name) || donorBones.ContainsKey(bone.name))
                        throw new InvalidOperationException("The source T-pose contains an empty or duplicate bone name.");
                    donorBones.Add(bone.name, bone);
                }
                var offsets = new List<UMABonePose.PoseBone>();
                int copied = 0, withoutPreview = 0;
                foreach (var original in baseline.boneInfo)
                {
                    bool visible = Pose.preview?.skeleton?.GetBoneTransform(original.name) != null;
                    bool found = donorBones.TryGetValue(original.name, out var incoming);
                    if (!found && !visible) continue;
                    var offset = new UMABonePose.PoseBone { bone = original.name, hash = UMAUtils.StringToHash(original.name),
                        position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one, enabled = true };
                    if (found)
                    {
                        if (!ValidTransform(original) || !ValidTransform(incoming))
                            throw new InvalidOperationException("Bone '" + original.name + "' has an invalid T-pose transform. Use finite values, positive scales and a nonzero rotation.");
                        offset.position = incoming.position - original.position;
                        offset.rotation = (Quaternion.Inverse(original.rotation.normalized) * incoming.rotation.normalized).normalized;
                        offset.scale = new Vector3(incoming.scale.x / original.scale.x,
                            incoming.scale.y / original.scale.y, incoming.scale.z / original.scale.z);
                        if (!Finite(offset.position) || !Finite(offset.scale) || offset.scale.x <= 0 || offset.scale.y <= 0 || offset.scale.z <= 0)
                            throw new InvalidOperationException("Bone '" + original.name + "' cannot be represented as a finite T-pose adjustment.");
                        copied++;
                        if (!visible) withoutPreview++;
                    }
                    offsets.Add(offset);
                }
                if (copied == 0)
                    throw new InvalidOperationException("The selected race has no T-pose bone names in common with the destination race.");

                // Validate everything before replacing anything. Stored offsets also cover destination
                // bones absent from the preview, so saving still copies those matching transforms.
                Undo.RegisterCompleteObjectUndo(Pose, "Copy T-Pose from Race");
                Pose.poses = offsets.ToArray();
                foreach (var group in Pose.boneGroups ?? Array.Empty<UMABonePose.BoneGroup>())
                {
                    if (group == null) continue;
                    group.position = Vector3.zero;
                    group.rotation = Quaternion.identity;
                    group.scale = Vector3.one;
                }
                EditorUtility.SetDirty(Pose);
                Pose.RefreshPreview();
                SceneView.RepaintAll();
                return new CopyResult(copied, baseline.boneInfo.Length - copied, donorBones.Count - copied, withoutPreview);
            }
            finally { Object.DestroyImmediate(copy); }
        }

        private static bool ValidTransform(SkeletonBone bone)
        {
            float length = Quaternion.Dot(bone.rotation, bone.rotation);
            return Finite(bone.position) && Finite(bone.scale) && bone.scale.x > 0 && bone.scale.y > 0 && bone.scale.z > 0 &&
                float.IsFinite(length) && length > 1e-12f;
        }

        public void Save()
        {
            if (Pose == null || Source == null || GetRace(Source) != Race || Race == null || Race.TPose != Destination)
                throw new InvalidOperationException("The source character or race T-pose changed. Close and extract it again.");
            if (!AssetDatabase.Contains(Destination))
                throw new InvalidOperationException("The race T-pose must be a saved project asset.");
            if (!originalChunk.SequenceEqual(Destination.serializedChunk ?? Array.Empty<byte>()))
                throw new InvalidOperationException("The T-pose was changed outside this session. Close and extract it again to avoid overwriting those changes.");
            if (!AssetDatabase.IsOpenForEdit(Destination, StatusQueryOptions.ForceUpdate))
                throw new InvalidOperationException("The T-pose asset is read-only. Check it out before saving.");

            var output = (SkeletonBone[])baseline.boneInfo.Clone();
            var byName = Pose.poses.Where(p => p != null && p.enabled).ToDictionary(p => p.bone, StringComparer.Ordinal);
            var boneNames = new HashSet<string>(output.Select(b => b.name), StringComparer.Ordinal);
            foreach (var name in byName.Keys)
                if (!boneNames.Contains(name))
                    throw new InvalidOperationException("Bone '" + name + "' is not in this race T-pose. Remove its offset before saving.");
            for (int i = 0; i < output.Length; i++)
            {
                if (!byName.TryGetValue(output[i].name, out var delta)) continue;
                output[i].position += delta.position;
                output[i].rotation = (output[i].rotation * delta.rotation).normalized;
                output[i].scale = Vector3.Scale(output[i].scale, delta.scale);
                if (!Finite(output[i].position) || !Finite(output[i].scale) ||
                    output[i].scale.x <= 0 || output[i].scale.y <= 0 || output[i].scale.z <= 0 ||
                    !float.IsFinite(delta.rotation.x) || !float.IsFinite(delta.rotation.y) ||
                    !float.IsFinite(delta.rotation.z) || !float.IsFinite(delta.rotation.w) || Quaternion.Dot(delta.rotation, delta.rotation) < 1e-12f)
                    throw new InvalidOperationException("Bone '" + output[i].name + "' has an invalid transform. Use finite values, positive scales and a nonzero rotation.");
            }
            var serialized = Object.Instantiate(baseline);
            try
            {
                // Instantiate does not copy nonserialized humanoid metadata; deserialize it first.
                serialized.boneInfo = null;
                serialized.DeSerialize();
                serialized.boneInfo = output;
                serialized.Serialize();
                Undo.RegisterCompleteObjectUndo(Destination, "Save Race T-Pose");
                Destination.serializedChunk = (byte[])serialized.serializedChunk.Clone();
                Reload(Destination);
                EditorUtility.SetDirty(Destination);
                TrackUndo(Destination);
                AssetDatabase.SaveAssetIfDirty(Destination);
                originalChunk = (byte[])Destination.serializedChunk.Clone();
            }
            finally { Object.DestroyImmediate(serialized); }
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        public void Dispose()
        {
            if (framedView != null)
            {
                framedView.LookAt(previousPivot, previousRotation, previousSize, previousOrthographic, true);
                framedView = null;
            }
            if (Pose != null) { Undo.ClearUndo(Pose); Object.DestroyImmediate(Pose); }
            if (previewRoot != null)
            {
                foreach (var transform in previewRoot.GetComponentsInChildren<Transform>(true)) Undo.ClearUndo(transform);
                Object.DestroyImmediate(previewRoot);
            }
            foreach (var resource in ownedResources) if (resource != null) Object.DestroyImmediate(resource);
            ownedResources.Clear();
            if (baseline != null) Object.DestroyImmediate(baseline);
        }

        // T-pose's decoded arrays are not serialized. Refresh them after Undo/Redo too.
        private const string UndoAssetsKey = "UMA.RaceTPoseEditor.UndoAssets";
        private static void TrackUndo(UmaTPose pose)
        {
            var guids = new HashSet<string>(SessionState.GetString(UndoAssetsKey, "").Split(';'));
            guids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pose)));
            SessionState.SetString(UndoAssetsKey, string.Join(";", guids));
        }
        [InitializeOnLoadMethod]
        private static void RegisterUndo()
        {
            Undo.undoRedoPerformed -= RefreshSavedTPoseCaches;
            Undo.undoRedoPerformed += RefreshSavedTPoseCaches;
        }
        private static void RefreshSavedTPoseCaches()
        {
            foreach (var guid in SessionState.GetString(UndoAssetsKey, "").Split(';'))
            {
                if (string.IsNullOrEmpty(guid)) continue;
                var pose = AssetDatabase.LoadAssetAtPath<UmaTPose>(AssetDatabase.GUIDToAssetPath(guid));
                if (pose != null) Reload(pose);
            }
        }
        private static void Reload(UmaTPose pose)
        {
            pose.boneInfo = null;
            pose.humanInfo = null;
            pose.DeSerialize();
        }
    }
}
