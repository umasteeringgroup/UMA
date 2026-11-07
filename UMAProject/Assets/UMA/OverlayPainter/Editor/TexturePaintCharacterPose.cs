using System;
using System.Collections.Generic;
using System.Linq;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor
{
    internal enum TexturePaintCharacterPoseMode { Current, APose, Default }

    /// <summary>Preserves visible pose across generation, including skeleton replacement.
    /// The caller bakes the painter preview inside the scope; disposal restores the scene pose.</summary>
    internal sealed class TexturePaintCharacterPose : IDisposable
    {
        private readonly GameObject root;
        private readonly Dictionary<string, (Vector3 position, Quaternion rotation, Vector3 scale)> transforms = new();
        private readonly Dictionary<string, Dictionary<string, float>> shapes = new();
        private bool disposed;
        private DynamicCharacterAvatar suspendedAvatar;
        private Animator suspendedAnimator;
        private bool wasAnimationDisabled, wasAnimatorEnabled;
        private RuntimeAnimatorController previousController;
        private UMAGeneratorBase.AnimatorState animatorState;

        internal TexturePaintCharacterPose(GameObject root)
        {
            this.root = root;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                transforms[AnimationUtility.CalculateTransformPath(transform, root.transform)] =
                    (transform.localPosition, transform.localRotation, transform.localScale);
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null) continue;
                var values = new Dictionary<string, float>();
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    values[renderer.sharedMesh.GetBlendShapeName(i)] = renderer.GetBlendShapeWeight(i);
                shapes[AnimationUtility.CalculateTransformPath(renderer.transform, root.transform)] = values;
            }
        }

        internal static AnimationClip FindAPose()
        {
            return AssetDatabase.FindAssets("UMA_APose t:AnimationClip")
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>)
                .FirstOrDefault(clip => clip != null && clip.name == "UMA_APose");
        }

        internal static TexturePaintCharacterPose Build(DynamicCharacterAvatar avatar, TexturePaintCharacterPoseMode mode)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            AnimationClip clip = mode == TexturePaintCharacterPoseMode.APose ? FindAPose() : null;
            if (mode == TexturePaintCharacterPoseMode.APose && clip == null)
                throw new InvalidOperationException("UMA_APose animation is not installed. Install the UMA3 animation content or choose Build in Current Pose.");
            var pose = new TexturePaintCharacterPose(avatar.gameObject);
            try
            {
                RestoreEditorWardrobe(avatar);
                if (mode == TexturePaintCharacterPoseMode.Default) pose.SuspendAnimation(avatar);
                avatar.GenerateNow();
                // GenerateNow can replace bones and reset expressions. Match stable hierarchy
                // paths and blendshape names instead of holding references to the old skeleton.
                if (mode != TexturePaintCharacterPoseMode.Default) pose.Restore();
                else pose.RestoreTransform(avatar.transform, string.Empty);
                if (mode == TexturePaintCharacterPoseMode.APose)
                {
                    var animator = avatar.GetComponent<Animator>();
                    if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid)
                        throw new InvalidOperationException("Build in A-Pose requires a generated Humanoid avatar. Choose Build in Current Pose for this rig.");
                    clip.SampleAnimation(avatar.gameObject, 0);
                    pose.RestoreTransform(avatar.transform, string.Empty);
                }
                return pose;
            }
            catch { pose.Dispose(); throw; }
        }

        internal static void RestoreEditorWardrobe(DynamicCharacterAvatar avatar)
        {
            // The runtime dictionaries do not survive a domain reload. GenerateNow does not
            // reload the serialized editor wardrobe (unlike BuildNow), and would replace a
            // dressed scene preview with just its base race. Only initialize an empty editor
            // cache: loading defaults over a live wardrobe can replace deliberate selections.
            // An empty wardrobe in play mode may intentionally mean an undressed character.
            if (Application.isPlaying || avatar.WardrobeRecipes.Count != 0 ||
                avatar.AdditiveRecipes.Count != 0 || avatar.WardrobeCollections.Count != 0) return;
            avatar.activeRace.SetRaceData();
            avatar.LoadDefaultWardrobe();
        }

        internal void SuspendAnimation(DynamicCharacterAvatar avatar)
        {
            suspendedAvatar = avatar;
            wasAnimationDisabled = avatar.disableAnimation;
            previousController = avatar.animationController;
            suspendedAnimator = avatar.GetComponent<Animator>();
            if (suspendedAnimator != null)
            {
                wasAnimatorEnabled = suspendedAnimator.enabled;
                animatorState = new UMAGeneratorBase.AnimatorState { FreezeTime = true };
                animatorState.SaveAnimatorState(suspendedAnimator, avatar);
                suspendedAnimator.enabled = false;
            }
            // Both are required: UMA's generator evaluates an assigned controller even when
            // disableAnimation is true. Keep the component itself so external references survive.
            avatar.disableAnimation = true;
            avatar.animationController = null;
        }

        private void RestoreTransform(Transform transform, string path)
        {
            if (!transforms.TryGetValue(path, out var value)) return;
            transform.SetLocalPositionAndRotation(value.position, value.rotation);
            transform.localScale = value.scale;
        }

        internal void Restore()
        {
            if (root == null) return;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                RestoreTransform(transform, AnimationUtility.CalculateTransformPath(transform, root.transform));
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null || !shapes.TryGetValue(
                    AnimationUtility.CalculateTransformPath(renderer.transform, root.transform), out var values)) continue;
                for (int i = 0; i < renderer.sharedMesh.blendShapeCount; i++)
                    if (values.TryGetValue(renderer.sharedMesh.GetBlendShapeName(i), out float weight))
                        renderer.SetBlendShapeWeight(i, weight);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (suspendedAvatar != null)
            {
                suspendedAvatar.disableAnimation = wasAnimationDisabled;
                suspendedAvatar.animationController = previousController;
            }
            try
            {
                if (suspendedAnimator != null)
                {
                    // Generation may replace the skeleton even though UpdateAvatar is skipped.
                    // Rebind to the new bones before restoring state and the captured visible pose.
                    suspendedAnimator.enabled = true;
                    suspendedAnimator.Rebind();
                }
                animatorState?.RestoreAnimatorState(suspendedAnimator, suspendedAvatar, false);
            }
            finally
            {
                if (suspendedAnimator != null) suspendedAnimator.enabled = wasAnimatorEnabled;
                Restore();
            }
        }
    }
}
