using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.PoseTools
{
    /// <summary>Edits flattened pose values; groups never add a second runtime transform.</summary>
    public static class UMABoneGroupEditing
    {
        public static UMABonePose.BoneGroup CreateOrReplace(UMABonePose pose, string name, IEnumerable<string> selectedBones)
        {
            if (pose == null) throw new ArgumentNullException(nameof(pose));
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Enter a bone group name.");
            var selected = new HashSet<string>(selectedBones ?? Array.Empty<string>(), StringComparer.Ordinal);
            // Preserve pose order, not checkbox click order. Names survive sorting and deletion/Undo.
            var members = (pose.poses ?? Array.Empty<UMABonePose.PoseBone>())
                .Where(p => p != null && !string.IsNullOrEmpty(p.bone) && selected.Contains(p.bone))
                .Select(p => p.bone).Distinct(StringComparer.Ordinal).ToArray();
            if (members.Length == 0) throw new ArgumentException("Select at least one bone in the upper bone list.");
            var groups = (pose.boneGroups ?? Array.Empty<UMABonePose.BoneGroup>()).ToList();
            int index = groups.FindIndex(g => g != null && string.Equals(g.name, name, StringComparison.OrdinalIgnoreCase));
            var group = new UMABonePose.BoneGroup { name = name, bones = members };
            Undo.RecordObject(pose, index >= 0 ? "Replace Bone Group" : "Create Bone Group");
            if (index >= 0) groups[index] = group;
            else groups.Add(group);
            pose.boneGroups = groups.ToArray();
            EditorUtility.SetDirty(pose);
            return group;
        }

        public static int Apply(UMABonePose pose, UMABonePose.BoneGroup group, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (pose == null || group == null || pose.boneGroups == null || !pose.boneGroups.Contains(group))
                throw new ArgumentException("Select a group belonging to this bone pose.");
            if (!Finite(position) || !Finite(group.position) || !Finite(scale) || scale.x <= 0 || scale.y <= 0 || scale.z <= 0 ||
                !Finite(group.scale) || group.scale.x <= 0 || group.scale.y <= 0 || group.scale.z <= 0 ||
                !Valid(rotation) || !Valid(group.rotation))
                throw new ArgumentException("Group controls require finite values, positive scale factors, and a valid rotation.");
            rotation = rotation.normalized;
            Vector3 move = position - group.position;
            Quaternion turn = Quaternion.Inverse(group.rotation.normalized) * rotation;
            Vector3 resize = new Vector3(scale.x / group.scale.x, scale.y / group.scale.y, scale.z / group.scale.z);
            var names = new HashSet<string>(group.bones ?? Array.Empty<string>(), StringComparer.Ordinal);
            // Visit each named bone once, even if a stale serialized group repeats its name.
            var members = new List<UMABonePose.PoseBone>();
            foreach (var member in pose.poses ?? Array.Empty<UMABonePose.PoseBone>())
                if (member != null && names.Remove(member.bone)) members.Add(member);
            // Validate the entire operation before modifying any member.
            foreach (var member in members)
                if (!Finite(member.position + move) || !Finite(Vector3.Scale(member.scale, resize)) || !Valid(member.rotation))
                    throw new ArgumentException("Bone '" + member.bone + "' has an invalid transform.");
            if (members.Count == 0) return 0;
            Undo.RecordObject(pose, "Edit Bone Group " + group.name);
            foreach (var member in members)
            {
                member.position += move;
                member.rotation = (member.rotation * turn).normalized;
                member.scale = Vector3.Scale(member.scale, resize);
            }
            group.position = position;
            group.rotation = rotation;
            group.scale = scale;
            EditorUtility.SetDirty(pose);
            return members.Count;
        }

        public static void Remove(UMABonePose pose, UMABonePose.BoneGroup group)
        {
            if (pose == null || group == null || pose.boneGroups == null) return;
            Undo.RecordObject(pose, "Remove Bone Group");
            pose.boneGroups = pose.boneGroups.Where(g => g != group).ToArray();
            EditorUtility.SetDirty(pose);
        }

        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        private static bool Valid(Quaternion value) => float.IsFinite(value.x) && float.IsFinite(value.y) &&
            float.IsFinite(value.z) && float.IsFinite(value.w) && float.IsFinite(Quaternion.Dot(value, value)) && Quaternion.Dot(value, value) > 1e-12f;
    }
}
