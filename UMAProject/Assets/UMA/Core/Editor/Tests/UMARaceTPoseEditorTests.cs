using System;
using NUnit.Framework;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class UMARaceTPoseEditorTests
    {
        private UMAData source;
        private GameObject character;
        private Transform hip;
        private RaceData race;
        private UmaTPose tpose;
        private UMARaceTPoseEditSession session;
        private string assetPath;
        private Mesh sourceMesh;
        private Material sourceMaterial;
        private RaceData donorRace;
        private UmaTPose donorPose;

        [SetUp]
        public void SetUp()
        {
            character = new GameObject("T-pose editor fixture");
            source = character.AddComponent<UMAData>();
            source.staticCharacter = true;
            var root = new GameObject("Root").transform;
            root.SetParent(character.transform, false);
            source.umaRoot = root.gameObject;
            hip = new GameObject("Hips").transform;
            hip.SetParent(root, false);
            hip.localPosition = new Vector3(0.1f, 0.8f, 0.2f);
            hip.localRotation = Quaternion.Euler(12, 34, 56);
            source.skeleton = new UMASkeleton(root);
            race = ScriptableObject.CreateInstance<RaceData>();
            race.name = "T-pose fixture race";
            source.umaRecipe = new UMAData.UMARecipe { raceData = race };
            tpose = ScriptableObject.CreateInstance<UmaTPose>();
            tpose.boneInfo = new[]
            {
                new SkeletonBone { name = "Hips", position = Vector3.up, rotation = Quaternion.Euler(0, 30, 0), scale = new Vector3(2, 3, 4) },
                new SkeletonBone { name = "AbsentBone", position = new Vector3(1, 2, 3), rotation = Quaternion.identity, scale = Vector3.one }
            };
            tpose.humanInfo = new[] { new HumanBone { boneName = "Hips", humanName = "Hips", limit = new HumanLimit { useDefaultValues = true } } };
            tpose.extendedInfo = true;
            tpose.armStretch = 0.12f;
            tpose.upperArmTwist = 0.6f;
            tpose.mapJaw = false;
            tpose.SetHumanPose(new HumanPose { bodyPosition = new Vector3(1, 2, 3), bodyRotation = Quaternion.identity, muscles = new float[HumanTrait.MuscleCount] });
            tpose.Serialize();
            assetPath = "Assets/__RaceTPoseEditorTest_" + Guid.NewGuid().ToString("N") + ".asset";
            AssetDatabase.CreateAsset(tpose, assetPath);
            race.TPose = tpose;
        }

        [TearDown]
        public void TearDown()
        {
            session?.Dispose();
            if (tpose != null) Undo.ClearUndo(tpose);
            if (character != null) Object.DestroyImmediate(character);
            if (race != null) Object.DestroyImmediate(race);
            if (donorRace != null) Object.DestroyImmediate(donorRace);
            if (donorPose != null) Object.DestroyImmediate(donorPose);
            if (sourceMesh != null) Object.DestroyImmediate(sourceMesh);
            if (sourceMaterial != null) Object.DestroyImmediate(sourceMaterial);
            if (!string.IsNullOrEmpty(assetPath)) AssetDatabase.DeleteAsset(assetPath);
        }

        [Test]
        public void ExtractUsesRaceBaselineAndNeverMovesOrAnimatesSource()
        {
            var animator = character.AddComponent<Animator>();
            var position = hip.localPosition;
            var rotation = hip.localRotation;
            var bytes = (byte[])tpose.serializedChunk.Clone();
            session = UMARaceTPoseEditSession.Create(source);
            Assert.That(session.MatchedBones, Is.EqualTo(1));
            Assert.That(session.TotalBones, Is.EqualTo(2));
            Assert.That(session.Pose.poses[0].position, Is.EqualTo(Vector3.zero));
            var previewHip = session.Pose.preview.skeleton.GetBoneTransform("Hips");
            Assert.That(previewHip.localPosition, Is.EqualTo(Vector3.up));
            Assert.That(previewHip.localScale, Is.EqualTo(new Vector3(2, 3, 4)));
            session.Pose.poses[0].position = Vector3.right;
            session.Pose.RefreshPreview();
            Assert.That(previewHip.localPosition, Is.EqualTo(Vector3.up + Vector3.right));
            session.Pose.RefreshPreview();
            Assert.That(previewHip.localPosition, Is.EqualTo(Vector3.up + Vector3.right), "Preview must not accumulate offsets.");
            Assert.That(hip.localPosition, Is.EqualTo(position));
            Assert.That(hip.localRotation, Is.EqualTo(rotation));
            Assert.That(animator.enabled, Is.True);
            Assert.That(tpose.serializedChunk, Is.EqualTo(bytes));
        }

        [Test]
        public void SaveRoundTripsOffsetsPreservesMetadataAndDoesNotAccumulate()
        {
            session = UMARaceTPoseEditSession.Create(source);
            var delta = session.Pose.poses[0];
            delta.position = Vector3.right * 0.2f;
            delta.rotation = Quaternion.Euler(15, 0, 0);
            delta.scale = new Vector3(1.1f, 0.9f, 1.2f);
            session.Save();
            Assert.That(race.TPose, Is.SameAs(tpose));
            Assert.That(tpose.boneInfo[0].position, Is.EqualTo(new Vector3(0.2f, 1, 0)));
            Assert.That(Quaternion.Angle(tpose.boneInfo[0].rotation, Quaternion.Euler(0, 30, 0) * delta.rotation), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(tpose.boneInfo[0].scale, new Vector3(2.2f, 2.7f, 4.8f)), Is.LessThan(0.00001f));
            Assert.That(tpose.boneInfo[1].position, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(tpose.humanInfo[0].humanName, Is.EqualTo("Hips"));
            Assert.That(tpose.extendedInfo, Is.True);
            Assert.That(tpose.armStretch, Is.EqualTo(0.12f));
            Assert.That(tpose.upperArmTwist, Is.EqualTo(0.6f));
            Assert.That(tpose.mapJaw, Is.False);
            Assert.That(tpose.HasExtractedHumanPose(), Is.True);
            Assert.That(tpose.GetHumanPose().bodyPosition, Is.EqualTo(new Vector3(1, 2, 3)));
            var saved = (byte[])tpose.serializedChunk.Clone();
            session.Save();
            Assert.That(tpose.serializedChunk, Is.EqualTo(saved));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<UmaTPose>(assetPath);
            reloaded.boneInfo = null;
            reloaded.DeSerialize();
            Assert.That(reloaded.boneInfo[0].position, Is.EqualTo(new Vector3(0.2f, 1, 0)));
        }

        [Test]
        public void SaveUndoRedoRefreshesDecodedBoneArrays()
        {
            session = UMARaceTPoseEditSession.Create(source);
            Undo.IncrementCurrentGroup();
            session.Pose.poses[0].position = Vector3.right;
            session.Save();
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Assert.That(tpose.boneInfo[0].position, Is.EqualTo(Vector3.up));
            Undo.PerformRedo();
            Assert.That(tpose.boneInfo[0].position, Is.EqualTo(Vector3.up + Vector3.right));
        }

        [Test]
        public void TemporaryInspectorDoesNotUseNormalCharacterRebuildLifecycle()
        {
            session = UMARaceTPoseEditSession.Create(source);
            var editor = (UMABonePoseEditor)Editor.CreateEditor(session.Pose, typeof(UMABonePoseEditor));
            try
            {
                Assert.That(editor.dynamicDNAConverterMode, Is.True);
                Assert.That(editor.sourceUMA, Is.SameAs(session.Pose.preview));
                Assert.That(editor.poseTarget, Is.Null);
                Assert.That(editor.autoUpdatePreview, Is.False);
                session.Pose.poses[0].position = Vector3.forward;
                session.Pose.RefreshPreview();
                Assert.That(session.Pose.ScaleDelta("Hips", new Vector3(4, 6, 8)), Is.EqualTo(Vector3.one * 2));
                Assert.That(session.Pose.preview.skeleton.GetBoneTransform("Hips").localPosition, Is.EqualTo(Vector3.up + Vector3.forward));
            }
            finally { Object.DestroyImmediate(editor); }
            Assert.That(source.skeleton.GetBoneTransform("Hips"), Is.SameAs(hip));
        }

        [Test]
        public void CancelDisposesPreviewResourcesWithoutDestroyingSourceMeshOrMaterial()
        {
            var renderer = character.AddComponent<SkinnedMeshRenderer>();
            sourceMesh = new Mesh();
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            sourceMesh.triangles = new[] { 0, 1, 2 };
            sourceMesh.boneWeights = new[] { Weight(), Weight(), Weight() };
            sourceMesh.bindposes = new[] { Matrix4x4.identity };
            sourceMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            renderer.sharedMesh = sourceMesh;
            renderer.sharedMaterial = sourceMaterial;
            renderer.bones = new[] { hip };
            renderer.rootBone = hip;
            session = UMARaceTPoseEditSession.Create(source);
            var pose = session.Pose;
            var preview = pose.preview.gameObject;
            var copy = preview.GetComponent<SkinnedMeshRenderer>();
            var mesh = copy.sharedMesh;
            var material = copy.sharedMaterial;
            Assert.That(mesh, Is.Not.SameAs(sourceMesh));
            Assert.That(material, Is.Not.SameAs(sourceMaterial));
            var chunk = (byte[])tpose.serializedChunk.Clone();
            pose.poses[0].position = Vector3.one;
            session.Dispose();
            session.Dispose();
            Assert.That(pose == null && preview == null && mesh == null && material == null, Is.True);
            Assert.That(sourceMesh != null && sourceMaterial != null, Is.True);
            Assert.That(renderer.sharedMesh, Is.SameAs(sourceMesh));
            Assert.That(tpose.serializedChunk, Is.EqualTo(chunk));
        }

        private static BoneWeight Weight() => new BoneWeight { boneIndex0 = 0, weight0 = 1 };

        [Test]
        public void SaveRefusesExternalEditsAndInvalidTransforms()
        {
            session = UMARaceTPoseEditSession.Create(source);
            var original = (byte[])tpose.serializedChunk.Clone();
            session.Pose.poses[0].scale = new Vector3(float.NaN, 1, 1);
            Assert.Throws<InvalidOperationException>(() => session.Save());
            Assert.That(tpose.serializedChunk, Is.EqualTo(original));
            session.Pose.poses[0].scale = Vector3.one;
            tpose.boneInfo[0].position = Vector3.forward;
            tpose.Serialize();
            Assert.Throws<InvalidOperationException>(() => session.Save());
            Assert.That(tpose.boneInfo[0].position, Is.EqualTo(Vector3.forward));
        }

        [Test]
        public void DisabledOrRemovedOffsetsLeaveOriginalBoneUnchanged()
        {
            session = UMARaceTPoseEditSession.Create(source);
            session.Pose.poses[0].position = Vector3.one;
            session.Pose.poses[0].enabled = false;
            session.Save();
            Assert.That(tpose.boneInfo[0].position, Is.EqualTo(Vector3.up));
            session.Pose.poses = Array.Empty<UMABonePose.PoseBone>();
            session.Save();
            Assert.That(tpose.boneInfo[0].position, Is.EqualTo(Vector3.up));
        }

        [Test]
        public void DraftRestoresAfterReloadWithoutApplyingSavedOffsetsTwice()
        {
            session = UMARaceTPoseEditSession.Create(source);
            session.Pose.poses[0].position = Vector3.right;
            session.Save();
            session.Pose.poses[0].position += Vector3.forward;
            var baseline = session.BaselineChunk;
            var expected = session.ExpectedChunk;
            var json = EditorJsonUtility.ToJson(session.Pose);
            session.Dispose();
            session = UMARaceTPoseEditSession.Create(source, baseline, expected);
            EditorJsonUtility.FromJsonOverwrite(json, session.Pose);
            session.Pose.RefreshPreview();
            Assert.That(session.Pose.preview.skeleton.GetBoneTransform("Hips").localPosition, Is.EqualTo(Vector3.one));
            session.Save();
            Assert.That(tpose.boneInfo[0].position, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void MissingTPoseFailsWithoutCreatingPreview()
        {
            race.TPose = null;
            int before = Resources.FindObjectsOfTypeAll<UMARaceTPoseEditPose>().Length;
            Assert.Throws<InvalidOperationException>(() => UMARaceTPoseEditSession.Create(source));
            Assert.That(Resources.FindObjectsOfTypeAll<UMARaceTPoseEditPose>().Length, Is.EqualTo(before));
        }

        private RaceData CreateDonor()
        {
            donorRace = ScriptableObject.CreateInstance<RaceData>();
            donorPose = ScriptableObject.CreateInstance<UmaTPose>();
            donorRace.TPose = donorPose;
            donorPose.boneInfo = new[]
            {
                new SkeletonBone { name = "Hips", position = new Vector3(2, 4, 6), rotation = Quaternion.Euler(35, 15, -20), scale = new Vector3(3, 2, 1) },
                new SkeletonBone { name = "AbsentBone", position = new Vector3(7, 8, 9), rotation = Quaternion.Euler(5, 10, 15), scale = Vector3.one * 2 },
                new SkeletonBone { name = "DonorOnly", position = Vector3.right, rotation = Quaternion.identity, scale = Vector3.one }
            };
            donorPose.humanInfo = Array.Empty<HumanBone>();
            donorPose.Serialize();
            return donorRace;
        }

        [Test]
        public void CopyFromRacePreviewsThenSavesToOriginalWithOriginalMetadata()
        {
            session = UMARaceTPoseEditSession.Create(source);
            CreateDonor();
            var donorBone = donorPose.boneInfo[0];
            var destinationBytes = (byte[])tpose.serializedChunk.Clone();
            var donorBytes = (byte[])donorPose.serializedChunk.Clone();
            var sourcePosition = hip.localPosition;
            var result = session.CopyFromRace(donorRace);
            Assert.That(result.Copied, Is.EqualTo(2));
            Assert.That(result.Reset, Is.Zero);
            Assert.That(result.Ignored, Is.EqualTo(1));
            Assert.That(result.WithoutPreview, Is.EqualTo(1));
            Assert.That(session.MatchedBones, Is.EqualTo(1));
            var preview = session.Pose.preview.skeleton.GetBoneTransform("Hips");
            Assert.That(preview.localPosition, Is.EqualTo(donorBone.position));
            Assert.That(Quaternion.Angle(preview.localRotation, donorBone.rotation), Is.LessThan(.01f));
            Assert.That(Vector3.Distance(preview.localScale, donorBone.scale), Is.LessThan(.00001f));
            Assert.That(tpose.serializedChunk, Is.EqualTo(destinationBytes), "Copy only edits the draft.");
            Assert.That(hip.localPosition, Is.EqualTo(sourcePosition));
            session.Pose.poses[0].position += Vector3.forward;
            session.Save();
            Assert.That(session.Race, Is.SameAs(race));
            Assert.That(session.Destination, Is.SameAs(tpose));
            Assert.That(tpose.boneInfo[0].position, Is.EqualTo(donorBone.position + Vector3.forward));
            Assert.That(tpose.boneInfo[1].position, Is.EqualTo(donorPose.boneInfo[1].position), "Matching bones absent from the preview must still be copied on save.");
            Assert.That(tpose.boneInfo.Length, Is.EqualTo(2), "Donor-only bones must not extend the destination skeleton.");
            Assert.That(tpose.humanInfo[0].humanName, Is.EqualTo("Hips"));
            Assert.That(tpose.armStretch, Is.EqualTo(.12f));
            Assert.That(tpose.mapJaw, Is.False);
            Assert.That(donorPose.serializedChunk, Is.EqualTo(donorBytes));
            var once = (byte[])tpose.serializedChunk.Clone();
            session.Save();
            Assert.That(tpose.serializedChunk, Is.EqualTo(once), "Repeated saves must not accumulate copied offsets.");
        }

        [Test]
        public void CopyResetsMissingBonesAndGroupControlsAndUndoRestoresTheDraft()
        {
            session = UMARaceTPoseEditSession.Create(source);
            CreateDonor();
            donorPose.boneInfo = new[] { donorPose.boneInfo[1] };
            donorPose.Serialize();
            var group = UMABoneGroupEditing.CreateOrReplace(session.Pose, "Hips", new[] { "Hips" });
            UMABoneGroupEditing.Apply(session.Pose, group, Vector3.right, Quaternion.Euler(5, 20, 0), Vector3.one * 2);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            var before = EditorJsonUtility.ToJson(session.Pose);
            var result = session.CopyFromRace(donorRace);
            Assert.That(result.Reset, Is.EqualTo(1));
            Assert.That(session.Pose.preview.skeleton.GetBoneTransform("Hips").localPosition, Is.EqualTo(Vector3.up));
            Assert.That(session.Pose.boneGroups[0].position, Is.EqualTo(Vector3.zero));
            Assert.That(session.Pose.boneGroups[0].scale, Is.EqualTo(Vector3.one));
            var copied = EditorJsonUtility.ToJson(session.Pose);
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Assert.That(EditorJsonUtility.ToJson(session.Pose), Is.EqualTo(before));
            Undo.PerformRedo();
            Assert.That(EditorJsonUtility.ToJson(session.Pose), Is.EqualTo(copied));
        }

        [Test]
        public void CopiedDraftSurvivesReloadAndRepeatedCopyDoesNotAccumulate()
        {
            session = UMARaceTPoseEditSession.Create(source);
            CreateDonor();
            session.CopyFromRace(donorRace);
            var copied = EditorJsonUtility.ToJson(session.Pose);
            session.CopyFromRace(donorRace);
            Assert.That(EditorJsonUtility.ToJson(session.Pose), Is.EqualTo(copied));
            var baseline = session.BaselineChunk;
            var expected = session.ExpectedChunk;
            session.Dispose();
            session = UMARaceTPoseEditSession.Create(source, baseline, expected);
            EditorJsonUtility.FromJsonOverwrite(copied, session.Pose);
            session.Pose.RefreshPreview();
            Assert.That(session.Pose.preview.skeleton.GetBoneTransform("Hips").localPosition, Is.EqualTo(donorPose.boneInfo[0].position));
            session.Save();
            Assert.That(tpose.boneInfo[1].position, Is.EqualTo(donorPose.boneInfo[1].position));
        }

        [TestCase("missing")]
        [TestCase("unrelated")]
        [TestCase("duplicate")]
        [TestCase("invalid")]
        public void InvalidDonorLeavesDraftAndAssetsUntouchedAndDisposesItsCopy(string failure)
        {
            session = UMARaceTPoseEditSession.Create(source);
            CreateDonor();
            if (failure == "missing") donorRace.TPose = null;
            if (failure == "unrelated") donorPose.boneInfo = new[] { donorPose.boneInfo[2] };
            if (failure == "duplicate") donorPose.boneInfo = new[] { donorPose.boneInfo[0], donorPose.boneInfo[0] };
            if (failure == "invalid") donorPose.boneInfo[1].scale = Vector3.zero;
            donorPose.Serialize();
            string before = EditorJsonUtility.ToJson(session.Pose);
            var bytes = (byte[])tpose.serializedChunk.Clone();
            int resources = Resources.FindObjectsOfTypeAll<UmaTPose>().Length;
            Assert.Throws<InvalidOperationException>(() => session.CopyFromRace(donorRace));
            Assert.That(EditorJsonUtility.ToJson(session.Pose), Is.EqualTo(before));
            Assert.That(tpose.serializedChunk, Is.EqualTo(bytes));
            Assert.That(Resources.FindObjectsOfTypeAll<UmaTPose>().Length, Is.EqualTo(resources));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RotationToolSelectsBoneImmediatelyAndExitsIK(bool temporaryPose)
        {
            session = UMARaceTPoseEditSession.Create(source);
            var previousTool = Tools.current;
            var previousSource = UMABonePoseEditor.saveUMAData;
            UMABonePose ordinary = null;
            UMABonePoseEditor editor = null;
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(UMABonePoseEditor);
            try
            {
                UMABonePoseEditor.saveUMAData = null;
                if (!temporaryPose)
                {
                    ordinary = ScriptableObject.CreateInstance<UMABonePose>();
                    ordinary.poses = session.Pose.poses;
                }
                editor = (UMABonePoseEditor)Editor.CreateEditor(temporaryPose ? session.Pose : ordinary, type);
                editor.dynamicDNAConverterMode = true;
                editor.sourceUMA = session.Pose.preview;
                editor.context = new UMABonePoseEditorContext { activeUMA = session.Pose.preview };
                type.GetField("useIKEditor", flags).SetValue(editor, true);
                // This bone may previously have been only the mirror, not the active bone.
                type.GetField("mirrorBoneIndex", flags).SetValue(editor, 0);
                type.GetMethod("SelectBoneTool", flags).Invoke(editor, new object[] { 0, Tool.Rotate });
                Assert.That(type.GetField("useIKEditor", flags).GetValue(editor), Is.EqualTo(false));
                Assert.That(type.GetField("editBoneIndex", flags).GetValue(editor), Is.EqualTo(0));
                Assert.That(type.GetField("activeBoneIndex", flags).GetValue(editor), Is.EqualTo(0));
                Assert.That(editor.context.activeTransform.name, Is.EqualTo("Hips"));
                Assert.That(editor.context.activeTool, Is.EqualTo(UMABonePoseEditorContext.EditorTool.Tool_Rotation));
                Assert.That(Tools.current, Is.EqualTo(Tool.Rotate));

                foreach (var tool in new[] { Tool.Move, Tool.Scale, Tool.Rotate })
                {
                    Tools.current = tool;
                    type.GetMethod("SyncBoneToolWithUnity", flags).Invoke(editor, null);
                    var expected = tool == Tool.Move ? UMABonePoseEditorContext.EditorTool.Tool_Position :
                        tool == Tool.Scale ? UMABonePoseEditorContext.EditorTool.Tool_Scale : UMABonePoseEditorContext.EditorTool.Tool_Rotation;
                    Assert.That(editor.context.activeTool, Is.EqualTo(expected));
                }
            }
            finally
            {
                if (editor != null) Object.DestroyImmediate(editor);
                if (ordinary != null) Object.DestroyImmediate(ordinary);
                Tools.current = previousTool;
                UMABonePoseEditor.saveUMAData = previousSource;
            }
        }
    }
}
