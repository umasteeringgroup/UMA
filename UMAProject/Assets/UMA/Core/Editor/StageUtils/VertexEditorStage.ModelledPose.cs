using System.Collections.Generic;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA
{
    /// <summary>
    /// The pose of the stage's own temporary character.
    /// </summary>
    /// <remarks>
    /// A generated character does not stand in the pose its meshes were modelled and weighted in. The
    /// generator puts it into the race's T-pose and then, when the T-pose asset holds a stored human
    /// pose, applies that through Unity's humanoid system. The limbs end up where they were modelled,
    /// but Unity spreads their twist over the bones its own way: a thigh can be rolled by 20 degrees
    /// or more about its length. Skin weighted to one limb barely shows it. Clothing weighted to both
    /// legs is pulled apart and folds between them, which looks like damage but is only the pose, and
    /// it is in the way of sculpting a blendshape, whose movement is stored for the modelled pose.
    /// The character the stage builds for itself (see ShowStageForSlots) is therefore put into the
    /// modelled pose, the pose the Transfer Blend Shapes preview draws: it is generated without the
    /// stored human pose, and the bones the T-pose turns are turned back. Characters from the scene
    /// are left as they are.
    /// </remarks>
    public partial class VertexEditorStage
    {
        // The race's T-pose without its stored human pose, for the temporary character alone.
        private UmaTPose temporaryTPose;

        /// <summary>
        /// A copy of a T-pose asset that does not carry a stored human pose, so the generator leaves the
        /// skeleton in the T-pose itself. Null when the asset has no stored pose to leave out.
        /// </summary>
        internal static UmaTPose CreateTPoseWithoutHumanPose(UmaTPose source)
        {
            if (source == null || !source.HasExtractedHumanPose())
            {
                return null;
            }
            UmaTPose copy = Instantiate(source);
            copy.name = source.name + " (modelled pose)";
            copy.hideFlags = HideFlags.HideAndDontSave;
            var serialized = new SerializedObject(copy);
            SerializedProperty flag = serialized.FindProperty("hasExtractedHumanPose");
            if (flag == null)
            {
                DestroyImmediate(copy);
                return null;
            }
            flag.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return copy;
        }

        /// <summary>
        /// Turns the bones the race's T-pose moved back to the rotation they were modelled with. What
        /// DNA did to a bone on top of the T-pose is kept. A bone that already stands as modelled is
        /// left alone, so this can be called after every rebuild.
        /// </summary>
        /// <returns>How many bones were turned.</returns>
        internal static int ApplyModelledPose(DynamicCharacterAvatar avatar)
        {
            UMAData data = avatar != null ? avatar.umaData : null;
            if (data == null || data.skeleton == null || data.umaRecipe == null || data.umaRecipe.slotDataList == null)
            {
                return 0;
            }
            UmaTPose tpose = data.GetTPose();
            if (tpose == null)
            {
                return 0;
            }
            if (tpose.boneInfo == null || tpose.boneInfo.Length == 0)
            {
                tpose.DeSerialize();
            }
            if (tpose.boneInfo == null)
            {
                return 0;
            }

            // The modelled pose of each bone, from the slots the character is built from.
            var modelled = new Dictionary<int, UMATransform>();
            foreach (SlotData slot in data.umaRecipe.slotDataList)
            {
                UMAMeshData mesh = slot != null && slot.asset != null ? slot.asset.meshData : null;
                if (mesh == null || mesh.umaBones == null)
                {
                    continue;
                }
                foreach (UMATransform bone in mesh.umaBones)
                {
                    if (bone != null && !modelled.ContainsKey(bone.hash))
                    {
                        modelled.Add(bone.hash, bone);
                    }
                }
            }

            int turned = 0;
            foreach (SkeletonBone poseBone in tpose.boneInfo)
            {
                int hash = UMAUtils.StringToHash(poseBone.name);
                if (!modelled.TryGetValue(hash, out UMATransform bind) || !data.skeleton.HasBone(hash))
                {
                    continue;
                }
                Transform bone = data.skeleton.GetBoneTransform(hash);
                if (bone == null || Quaternion.Angle(poseBone.rotation, bind.rotation) < 0.01f)
                {
                    continue;
                }
                // Already as modelled (a rebuild that did not reset the skeleton)?
                if (Quaternion.Angle(bone.localRotation, bind.rotation) <= Quaternion.Angle(bone.localRotation, poseBone.rotation))
                {
                    continue;
                }
                // Now = T-pose, then whatever DNA turned. Take the T-pose out and put the modelled rotation in.
                bone.localRotation = bind.rotation * Quaternion.Inverse(poseBone.rotation) * bone.localRotation;
                if ((bone.localPosition - poseBone.position).sqrMagnitude < 1e-10f)
                {
                    bone.localPosition = bind.position;
                }
                turned++;
            }
            return turned;
        }

        /// <summary>
        /// Before the temporary character is generated again: keeps it on the T-pose without the stored
        /// human pose. A recipe rebuild makes a new UMAData, which does not know about it.
        /// </summary>
        private void KeepTemporaryCharacterPose()
        {
            if (ownsTemporaryCharacter && temporaryTPose != null && thisDCA != null && thisDCA.umaData != null)
            {
                thisDCA.umaData.OverrideTpose = temporaryTPose;
            }
        }

        private void DestroyTemporaryTPose()
        {
            if (temporaryTPose != null)
            {
                DestroyImmediate(temporaryTPose);
            }
            temporaryTPose = null;
        }
    }
}
