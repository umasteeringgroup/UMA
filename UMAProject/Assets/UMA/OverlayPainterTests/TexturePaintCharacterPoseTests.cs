#if UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintCharacterPoseTests
    {
        [Test]
        public void EditorLaunchRestoresLostWardrobeCacheWithoutReplacingLiveSelections()
        {
            var root = new GameObject("Painter wardrobe test");
            root.SetActive(false);
            var avatar = root.AddComponent<UMA.CharacterSystem.DynamicCharacterAvatar>();
            var race = ScriptableObject.CreateInstance<RaceData>();
            var defaultRecipe = ScriptableObject.CreateInstance<UMA.CharacterSystem.UMAWardrobeRecipe>();
            var customRecipe = ScriptableObject.CreateInstance<UMA.CharacterSystem.UMAWardrobeRecipe>();
            try
            {
                race.name = "Painter wardrobe test race";
                avatar.activeRace.name = race.raceName;
                avatar.activeRace.data = race;
                defaultRecipe.name = "Default shirt";
                defaultRecipe.wardrobeSlot = "Chest";
                defaultRecipe.compatibleRaces.Add(race.raceName);
                customRecipe.name = "Custom shirt";
                customRecipe.wardrobeSlot = "Chest";
                customRecipe.compatibleRaces.Add(race.raceName);
                avatar.preloadWardrobeRecipes.recipes.Add(
                    new UMA.CharacterSystem.DynamicCharacterAvatar.WardrobeRecipeListItem(defaultRecipe));

                // Generated meshes can survive reload while these runtime dictionaries do not.
                Assert.That(avatar.WardrobeRecipes, Is.Empty);
                TexturePaintCharacterPose.RestoreEditorWardrobe(avatar);
                Assert.That(avatar.WardrobeRecipes["Chest"], Is.SameAs(defaultRecipe));
                TexturePaintCharacterPose.RestoreEditorWardrobe(avatar);
                Assert.That(avatar.WardrobeRecipes.Count, Is.EqualTo(1));

                avatar.SetSlot(customRecipe);
                TexturePaintCharacterPose.RestoreEditorWardrobe(avatar);
                Assert.That(avatar.WardrobeRecipes["Chest"], Is.SameAs(customRecipe));

                avatar.WardrobeRecipes.Clear();
                avatar.AdditiveRecipes["Chest"] = new System.Collections.Generic.List<UMATextRecipe> { customRecipe };
                TexturePaintCharacterPose.RestoreEditorWardrobe(avatar);
                Assert.That(avatar.WardrobeRecipes, Is.Empty, "An additive-only live outfit must not reload defaults.");
                Assert.That(avatar.AdditiveRecipes["Chest"][0], Is.SameAs(customRecipe));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(race);
                UnityEngine.Object.DestroyImmediate(defaultRecipe);
                UnityEngine.Object.DestroyImmediate(customRecipe);
            }
        }

        [Test]
        public void DefaultPoseSuspendsAnimationAndRestoresSettingsOnFailure()
        {
            var root = new GameObject("Default pose test");
            root.SetActive(false);
            var avatar = root.AddComponent<UMA.CharacterSystem.DynamicCharacterAvatar>();
            var animator = root.AddComponent<Animator>();
            animator.enabled = false;
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using var pose = new TexturePaintCharacterPose(root);
                    pose.SuspendAnimation(avatar);
                    Assert.That(avatar.disableAnimation, Is.True);
                    Assert.That(avatar.animationController, Is.Null);
                    Assert.That(animator.enabled, Is.False);
                    throw new InvalidOperationException("Simulated build failure");
                });
                Assert.That(avatar.disableAnimation, Is.False);
                Assert.That(root.GetComponent<Animator>(), Is.SameAs(animator));
                Assert.That(animator.enabled, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PoseRestoresReplacementBonesByHierarchyPath()
        {
            var root = new GameObject("Pose test");
            try
            {
                var parent = new GameObject("Skeleton").transform; parent.SetParent(root.transform);
                var bone = new GameObject("Arm").transform; bone.SetParent(parent);
                bone.localPosition = new Vector3(.2f,.3f,.4f);
                bone.localRotation = Quaternion.Euler(10,20,30);
                bone.localScale = new Vector3(1.1f,.9f,1.2f);
                var position = bone.localPosition; var rotation = bone.localRotation; var scale = bone.localScale;
                using (var pose = new TexturePaintCharacterPose(root))
                {
                    UnityEngine.Object.DestroyImmediate(parent.gameObject);
                    parent = new GameObject("Skeleton").transform; parent.SetParent(root.transform);
                    bone = new GameObject("Arm").transform; bone.SetParent(parent);
                    pose.Restore();
                    Assert.That(bone.localPosition, Is.EqualTo(position));
                    Assert.That(Quaternion.Angle(bone.localRotation,rotation), Is.LessThan(.001f));
                    Assert.That(bone.localScale, Is.EqualTo(scale));
                    bone.localPosition = Vector3.zero;
                }
                Assert.That(bone.localPosition, Is.EqualTo(position), "Disposal restores the original pose after preview baking.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PoseRestoresBlendshapeWeightsAndRootAfterFailure()
        {
            var root = new GameObject("Pose test");
            var mesh = new Mesh {vertices = new[]{Vector3.zero,Vector3.right,Vector3.up},triangles = new[]{0,1,2}};
            mesh.AddBlendShapeFrame("Expression",100,new[]{Vector3.forward,Vector3.zero,Vector3.zero},new Vector3[3],new Vector3[3]);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();renderer.sharedMesh = mesh;
            renderer.SetBlendShapeWeight(0,37);
            root.transform.localPosition = Vector3.one;
            try
            {
                Assert.Throws<InvalidOperationException>(()=>
                {
                    using var pose = new TexturePaintCharacterPose(root);
                    renderer.SetBlendShapeWeight(0,0);root.transform.localPosition = Vector3.zero;
                    throw new InvalidOperationException("Simulated reconstruction failure");
                });
                Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(37));
                Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.one));
            }
            finally { UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh); }
        }
    }
}
#endif
