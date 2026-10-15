using System;
using System.Reflection;
using NUnit.Framework;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMABoneGroupEditorTests
    {
        private UMABonePose pose;

        [SetUp]
        public void SetUp()
        {
            pose = ScriptableObject.CreateInstance<UMABonePose>();
            pose.poses = new[] { Bone("UpperArm", new Vector3(1, 2, 3)), Bone("Forearm", new Vector3(4, 5, 6)), Bone("Other", Vector3.zero) };
            pose.poses[0].rotation = Quaternion.Euler(10, 20, 30);
            pose.poses[1].rotation = Quaternion.Euler(70, -20, 5);
            pose.poses[1].scale = new Vector3(2, 3, 4);
        }

        private static UMABonePose.PoseBone Bone(string name, Vector3 position) => new UMABonePose.PoseBone
        { bone = name, hash = UMAUtils.StringToHash(name), position = position, rotation = Quaternion.identity, scale = Vector3.one };

        [TearDown]
        public void TearDown()
        {
            if (pose != null)
            {
                Undo.ClearUndo(pose);
                Object.DestroyImmediate(pose);
            }
        }

        [Test]
        public void CreatesNamedGroupAndReplacesMembershipWithoutChangingBones()
        {
            var group = UMABoneGroupEditing.CreateOrReplace(pose, " Arms ", new[] { "Forearm", "UpperArm", "UpperArm", "Missing" });
            Assert.That(group.name, Is.EqualTo("Arms"));
            Assert.That(group.bones, Is.EqualTo(new[] { "UpperArm", "Forearm" }));
            UMABoneGroupEditing.Apply(pose, group, Vector3.right, Quaternion.identity, Vector3.one);
            string before = JsonUtility.ToJson(pose.poses[0]);
            var replacement = UMABoneGroupEditing.CreateOrReplace(pose, "arms", new[] { "Forearm" });
            Assert.That(pose.boneGroups.Length, Is.EqualTo(1));
            Assert.That(replacement.bones, Is.EqualTo(new[] { "Forearm" }));
            Assert.That(replacement.position, Is.EqualTo(Vector3.zero));
            Assert.That(JsonUtility.ToJson(pose.poses[0]), Is.EqualTo(before));
            Assert.Throws<ArgumentException>(() => UMABoneGroupEditing.CreateOrReplace(pose, " ", new[] { "UpperArm" }));
            Assert.Throws<ArgumentException>(() => UMABoneGroupEditing.CreateOrReplace(pose, "Empty", new[] { "Missing" }));
        }

        [Test]
        public void IndividualLocalDeltasPreserveDifferencesAndDoNotOrbitPivots()
        {
            var group = UMABoneGroupEditing.CreateOrReplace(pose, "Arms", new[] { "UpperArm", "Forearm" });
            var firstTurn = pose.poses[0].rotation;
            var secondTurn = pose.poses[1].rotation;
            var turn = Quaternion.Euler(15, 40, -10);
            Assert.That(UMABoneGroupEditing.Apply(pose, group, new Vector3(2, -1, 3), turn, new Vector3(2, 1, 3)), Is.EqualTo(2));
            Assert.That(pose.poses[0].position, Is.EqualTo(new Vector3(3, 1, 6)));
            Assert.That(pose.poses[1].position, Is.EqualTo(new Vector3(6, 4, 9)));
            Assert.That(Quaternion.Angle(pose.poses[0].rotation, firstTurn * turn), Is.LessThan(.01));
            Assert.That(Quaternion.Angle(pose.poses[1].rotation, secondTurn * turn), Is.LessThan(.01));
            Assert.That(pose.poses[1].scale, Is.EqualTo(new Vector3(4, 3, 12)));
            Assert.That(pose.poses[2].position, Is.EqualTo(Vector3.zero));
            UMABoneGroupEditing.Apply(pose, group, group.position, group.rotation, group.scale);
            Assert.That(pose.poses[1].position, Is.EqualTo(new Vector3(6, 4, 9)), "Unchanged controls must not accumulate transforms.");
            UMABoneGroupEditing.Apply(pose, group, Vector3.zero, Quaternion.identity, Vector3.one);
            Assert.That(pose.poses[1].position, Is.EqualTo(new Vector3(4, 5, 6)));
            Assert.That(pose.poses[1].scale, Is.EqualTo(new Vector3(2, 3, 4)));
            Assert.That(Quaternion.Angle(pose.poses[1].rotation, secondTurn), Is.LessThan(.01));
        }

        [Test]
        public void MembershipUsesNamesAfterSortingAndIgnoresDuplicatesAndMissingBones()
        {
            var group = UMABoneGroupEditing.CreateOrReplace(pose, "Arms", new[] { "UpperArm", "Forearm" });
            group.bones = new[] { "UpperArm", "UpperArm", "Forearm", "Missing" };
            Array.Reverse(pose.poses);
            pose.poses[1].enabled = false;
            Assert.That(UMABoneGroupEditing.Apply(pose, group, Vector3.right, Quaternion.identity, Vector3.one), Is.EqualTo(2));
            Assert.That(pose.poses[2].position.x, Is.EqualTo(2));
            Assert.That(pose.poses[1].position.x, Is.EqualTo(5));
            Assert.That(pose.poses[1].enabled, Is.False);
            Assert.That(pose.poses[0].position.x, Is.Zero);
        }

        [Test]
        public void InvalidInputCannotPartiallyModifyMembers()
        {
            var group = UMABoneGroupEditing.CreateOrReplace(pose, "Arms", new[] { "UpperArm", "Forearm" });
            string before = JsonUtility.ToJson(pose);
            Assert.Throws<ArgumentException>(() => UMABoneGroupEditing.Apply(pose, group, Vector3.right, Quaternion.identity, Vector3.zero));
            Assert.Throws<ArgumentException>(() => UMABoneGroupEditing.Apply(pose, group, new Vector3(float.NaN, 0, 0), Quaternion.identity, Vector3.one));
            Assert.That(JsonUtility.ToJson(pose), Is.EqualTo(before));
            pose.poses[1].rotation = new Quaternion(0, 0, 0, 0);
            before = JsonUtility.ToJson(pose);
            Assert.Throws<ArgumentException>(() => UMABoneGroupEditing.Apply(pose, group, Vector3.right, Quaternion.identity, Vector3.one));
            Assert.That(JsonUtility.ToJson(pose), Is.EqualTo(before));
        }

        [Test]
        public void UndoRedoRestoresControlsAndMembersTogether()
        {
            var group = UMABoneGroupEditing.CreateOrReplace(pose, "Arms", new[] { "UpperArm", "Forearm" });
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            string before = JsonUtility.ToJson(pose);
            UMABoneGroupEditing.Apply(pose, group, Vector3.right, Quaternion.Euler(5, 25, 15), Vector3.one * 2);
            Undo.FlushUndoRecordObjects();
            string after = JsonUtility.ToJson(pose);
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Assert.That(JsonUtility.ToJson(pose), Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(JsonUtility.ToJson(pose), Is.EqualTo(after));
            Undo.IncrementCurrentGroup();
            UMABoneGroupEditing.Remove(pose, pose.boneGroups[0]);
            Undo.FlushUndoRecordObjects();
            Assert.That(pose.boneGroups, Is.Empty);
            Undo.PerformUndo();
            Assert.That(JsonUtility.ToJson(pose), Is.EqualTo(after));
        }

        [Test]
        public void AssetSaveReloadAndDuplicateRetainGroupsIndependently()
        {
            string path = "Assets/__BoneGroupsTest_" + Guid.NewGuid().ToString("N") + ".asset";
            string copyPath = path.Replace(".asset", "_Copy.asset");
            try
            {
                var group = UMABoneGroupEditing.CreateOrReplace(pose, "Arms", new[] { "UpperArm", "Forearm" });
                UMABoneGroupEditing.Apply(pose, group, Vector3.up, Quaternion.Euler(20, 5, 0), Vector3.one * 1.5f);
                AssetDatabase.CreateAsset(pose, path);
                AssetDatabase.SaveAssetIfDirty(pose);
                Assert.That(AssetDatabase.CopyAsset(path, copyPath), Is.True);
                var copy = AssetDatabase.LoadAssetAtPath<UMABonePose>(copyPath);
                Assert.That(copy.boneGroups[0].bones, Is.EqualTo(group.bones));
                Assert.That(copy.boneGroups[0].position, Is.EqualTo(group.position));
                UMABoneGroupEditing.Apply(copy, copy.boneGroups[0], Vector3.zero, Quaternion.identity, Vector3.one);
                Assert.That(pose.boneGroups[0].position, Is.EqualTo(Vector3.up));
                Undo.ClearUndo(copy);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Assert.That(AssetDatabase.LoadAssetAtPath<UMABonePose>(path).boneGroups[0].scale, Is.EqualTo(Vector3.one * 1.5f));
            }
            finally
            {
                Undo.ClearUndo(pose);
                AssetDatabase.DeleteAsset(copyPath);
                AssetDatabase.DeleteAsset(path);
            }
        }

        [Test]
        public void ParentAndChildRuntimeTransformsApplyOnceInTheirOwnLocalSpace()
        {
            var root = new GameObject("Root");
            try
            {
                var upper = new GameObject("UpperArm").transform;
                upper.SetParent(root.transform, false);
                var lower = new GameObject("Forearm").transform;
                lower.SetParent(upper, false);
                lower.localPosition = Vector3.up;
                var skeleton = new UMASkeleton(root.transform);
                var group = UMABoneGroupEditing.CreateOrReplace(pose, "Arms", new[] { "UpperArm", "Forearm" });
                UMABoneGroupEditing.Apply(pose, group, Vector3.right, Quaternion.Euler(10, 30, 20), Vector3.one * 2);
                pose.ApplyPose(skeleton, 1);
                Assert.That(upper.localPosition, Is.EqualTo(pose.poses[0].position));
                Assert.That(lower.localPosition, Is.EqualTo(Vector3.up + pose.poses[1].position));
                Assert.That(Quaternion.Angle(lower.localRotation, pose.poses[1].rotation), Is.LessThan(.01));
                Assert.That(lower.localScale, Is.EqualTo(pose.poses[1].scale));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void SelectingGroupExitsIKAndSelectingBoneClearsGroup()
        {
            var previousTool = Tools.current;
            var previousSource = UMABonePoseEditor.saveUMAData;
            UMABonePoseEditor editor = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                UMABonePoseEditor.saveUMAData = null;
                UMABoneGroupEditing.CreateOrReplace(pose, "Arms", new[] { "UpperArm", "Forearm" });
                editor = (UMABonePoseEditor)Editor.CreateEditor(pose, typeof(UMABonePoseEditor));
                editor.dynamicDNAConverterMode = true;
                var type = typeof(UMABonePoseEditor);
                type.GetField("useIKEditor", flags).SetValue(editor, true);
                type.GetMethod("SelectBoneGroup", flags).Invoke(editor, new object[] { "Arms", Tool.Rotate });
                Assert.That(type.GetField("useIKEditor", flags).GetValue(editor), Is.False);
                Assert.That(type.GetProperty("SelectedBoneGroup", flags).GetValue(editor), Is.SameAs(pose.boneGroups[0]));
                Assert.That(Tools.current, Is.EqualTo(Tool.Rotate));
                Assert.That(editor.haveEditTarget, Is.False, "Groups must not masquerade as a single member.");
                type.GetMethod("SelectBoneTool", flags).Invoke(editor, new object[] { 0, Tool.Move });
                Assert.That(type.GetProperty("SelectedBoneGroup", flags).GetValue(editor), Is.Null);
                Assert.That(editor.haveEditTarget, Is.True);
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                UMABonePoseEditor.saveUMAData = previousSource;
                Tools.current = previousTool;
            }
        }
    }
}
