#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using UMA.HairCards.Runtime;
using Unity.Profiling;

namespace UMA.HairCards.Editor.Tests
{
    public sealed class HairGuideDepthTestWindow : EditorWindow
    {
        internal System.Action draw;
        private void OnGUI() { if (Event.current.type == EventType.Repaint) draw?.Invoke(); }
    }

    // Explicit two-process smoke test. Intentionally restricted to the disposable test project.
    public static class HairPreferencesRestartProbe
    {
        private const string Folder = "Assets/HairPreferencesRestartProbe";
        private static void RequireIsolatedProject()
        {
            if (!Application.dataPath.Replace('\\', '/').Contains("/tmp/HairGroomPreviewTests/"))
                throw new System.InvalidOperationException("Run this probe only in the isolated HairGroomPreviewTests project.");
        }
        public static void Write()
        {
            RequireIsolatedProject();
            if (AssetDatabase.IsValidFolder(Folder)) throw new System.InvalidOperationException("Probe folder already exists.");
            AssetDatabase.CreateFolder("Assets", "HairPreferencesRestartProbe");
            HairCardStage stage = ScriptableObject.CreateInstance<HairCardStage>();
            stage.BrushRadius = 0.237f; stage.BrushRootInfluence = 0.42f; stage.ShowCardWireframe = false;
            stage.GuideGeneration.guideCount = 431;
            HairEditorPreferences.instance.Remember(stage, "restart-probe", "source");
            Object.DestroyImmediate(stage);
            HairGroomAsset groom = ScriptableObject.CreateInstance<HairGroomAsset>(); groom.EnsureIntegrity();
            AssetDatabase.CreateAsset(groom, Folder + "/Source.asset");
            groom.Groups[0].profile = HairCardMenu.CreateDefaultProfileNear(groom);
            HairAtlasProfileAsset atlas = HairCardMenu.CreateDefaultAtlasNear(groom); groom.Groups[0].atlas = atlas;
            Texture2D texture = new Texture2D(2, 2); AssetDatabase.CreateAsset(texture, Folder + "/Albedo.asset");
            Material material = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(material, Folder + "/Material.mat");
            atlas.albedo = texture; atlas.material = material;
            groom.Groups[0].rootEmbedDepth = 0.007f; groom.Groups[0].children.childrenPerGuide = 9;
            EditorUtility.SetDirty(atlas); EditorUtility.SetDirty(groom);
            AssetDatabase.SaveAssetIfDirty(atlas); AssetDatabase.SaveAssetIfDirty(groom);
            HairEditorPreferences.instance.RememberSetup(groom, groom.Groups[0]);
            Debug.Log("HAIR_PREFERENCES_RESTART_WRITE_PASSED");
        }
        public static void Read()
        {
            RequireIsolatedProject();
            HairCardStage stage = ScriptableObject.CreateInstance<HairCardStage>();
            HairEditorPreferences.instance.Restore(stage, "restart-probe", "source");
            Assert.That(stage.BrushRadius, Is.EqualTo(0.237f));
            Assert.That(stage.BrushRootInfluence, Is.EqualTo(0.42f));
            Assert.That(stage.ShowCardWireframe, Is.False);
            Assert.That(stage.GuideGeneration.guideCount, Is.EqualTo(431));
            Object.DestroyImmediate(stage);
            HairGroomAsset next = ScriptableObject.CreateInstance<HairGroomAsset>(); next.EnsureIntegrity();
            AssetDatabase.CreateAsset(next, Folder + "/Next.asset");
            HairEditorPreferences.instance.ApplySetupToNewGroom(next);
            Assert.That(next.Groups[0].atlas.albedo == AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/Albedo.asset"), Is.True);
            Assert.That(next.Groups[0].atlas.material == AssetDatabase.LoadAssetAtPath<Material>(Folder + "/Material.mat"), Is.True);
            Assert.That(next.Groups[0].rootEmbedDepth, Is.EqualTo(0.007f));
            Assert.That(next.Groups[0].children.childrenPerGuide, Is.EqualTo(9));
            HairEditorPreferences.instance.ClearSetupDefaults();
            AssetDatabase.DeleteAsset(Folder);
            Debug.Log("HAIR_PREFERENCES_RESTART_READ_PASSED");
        }
    }

    public sealed class HairCoreTests
    {
        private Mesh sourceMesh;
        private HairGroomAsset groom;
        private HairCardProfileAsset profile;
        private bool preferencesWereSuspended;

        [SetUp]
        public void SetUp()
        {
            preferencesWereSuspended = HairEditorPreferences.Suspended;
            HairEditorPreferences.Suspended = true;
            sourceMesh = new Mesh { name = "Test Scalp" };
            sourceMesh.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0f, 0f, 0.5f)
            };
            sourceMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
            sourceMesh.triangles = new[] { 0, 1, 2 };

            profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            profile.Configure(HairCardShape.Ribbon, 0.04f, 0f, 4, generateBackfaces: true);

            groom = ScriptableObject.CreateInstance<HairGroomAsset>();
            groom.SetSource(sourceMesh, "mesh:test", "TestRace", "TestScalp");
            groom.Lods[0].samplesPerCard = 4;
            HairGroup group = groom.Groups[0];
            group.profile = profile;
            group.children.childrenPerGuide = 0;
            group.guides.Add(CreateGuide("Guide A", 11, new Vector3(0f, 0f, 0f)));
            groom.EnsureIntegrity();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(groom);
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(sourceMesh);
            HairEditorPreferences.Suspended = preferencesWereSuspended;
        }

        [Test]
        public void MapClipboardCopiesSnapshotsAndPastesAcrossTypesWithoutReplacingMetadata()
        {
            HairGroup group = groom.Groups[0];
            HairGrowthMap source = group.FindMap(HairMapKind.GrowthArea), target = group.FindMap(HairMapKind.Length);
            source.values = new[] { 0.1f, 0.4f, 0.9f };
            var clipboard = new HairGrowthMapClipboard();
            string original = EditorJsonUtility.ToJson(groom);
            Assert.That(clipboard.TryCopy(groom, group, source, false, out _), Is.True);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original), "Copy is read-only.");
            source.values[1] = 0.8f;
            string targetId = target.Id, targetName = target.name;
            target.visible = false;
            Assert.That(clipboard.TryPaste(groom, group, target, out _), Is.True);
            Assert.That(target.values, Is.EqualTo(new[] { 0.1f, 0.4f, 0.9f }));
            Assert.That(target.Id, Is.EqualTo(targetId)); Assert.That(target.name, Is.EqualTo(targetName));
            Assert.That(target.kind, Is.EqualTo(HairMapKind.Length)); Assert.That(target.visible, Is.False);
            Assert.That(target.defaultValue, Is.EqualTo(1f));
            target.values[0] = 0.7f;
            Assert.That(clipboard.TryPaste(groom, group, target, out _), Is.True);
            Assert.That(target.values[0], Is.EqualTo(0.1f), "Pasting does not consume or alias the clipboard.");
        }

        [TestCase(HairMapKind.GrowthArea, 0f)]
        [TestCase(HairMapKind.Density, 1f)]
        public void MapClipboardCutRestoresDefaultsAndSupportsUndoRedo(HairMapKind kind, float reset)
        {
            HairGroup group = groom.Groups[0]; HairGrowthMap source = group.FindMap(kind);
            source.values = new[] { 0.2f, 0.5f, 0.8f };
            string id = source.Id;
            var clipboard = new HairGrowthMapClipboard();
            Undo.ClearUndo(groom); Undo.IncrementCurrentGroup();
            Assert.That(clipboard.TryCopy(groom, group, source, true, out _), Is.True);
            Assert.That(source.values, Is.EqualTo(new[] { reset, reset, reset }));
            Assert.That(group.maps.Contains(source), Is.True, "Cut moves values, not the map definition.");
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            source = groom.Groups[0].maps.Find(map => map.Id == id);
            Assert.That(source.values, Is.EqualTo(new[] { 0.2f, 0.5f, 0.8f }));
            Undo.PerformRedo();
            source = groom.Groups[0].maps.Find(map => map.Id == id);
            Assert.That(source.values, Is.EqualTo(new[] { reset, reset, reset }));
            Assert.That(clipboard.TryPaste(groom, groom.Groups[0], source, out _), Is.True);
            Assert.That(source.values, Is.EqualTo(new[] { 0.2f, 0.5f, 0.8f }));
            Undo.ClearUndo(groom);
        }

        [Test]
        public void MapClipboardPasteClampsRangeAndUndoRestoresDestination()
        {
            HairGroup group = groom.Groups[0]; HairGrowthMap source = group.FindMap(HairMapKind.GrowthArea);
            source.valueRange = new Vector2(-2f, 2f); source.values = new[] { -1f, 0.25f, 2f };
            HairGrowthMap target = group.FindMap(HairMapKind.Density);
            target.values = new[] { 0.3f, 0.6f, 0.7f };
            var clipboard = new HairGrowthMapClipboard();
            Assert.That(clipboard.TryCopy(groom, group, source, false, out _), Is.True);
            Undo.ClearUndo(groom); Undo.IncrementCurrentGroup();
            Assert.That(clipboard.TryPaste(groom, group, target, out _), Is.True);
            Assert.That(target.values, Is.EqualTo(new[] { 0f, 0.25f, 1f }));
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(groom.Groups[0].FindMap(HairMapKind.Density).values, Is.EqualTo(new[] { 0.3f, 0.6f, 0.7f }));
            Undo.PerformRedo();
            Assert.That(groom.Groups[0].FindMap(HairMapKind.Density).values, Is.EqualTo(new[] { 0f, 0.25f, 1f }));
            Undo.ClearUndo(groom);
        }

        [Test]
        public void MapClipboardLocksRejectWritesButAllowCopyAndPreserveClipboardOnFailure()
        {
            HairGroup group = groom.Groups[0]; HairGrowthMap source = group.FindMap(HairMapKind.GrowthArea);
            source.values = new[] { 0.2f, 0.5f, 0.8f };
            var clipboard = new HairGrowthMapClipboard();
            source.locked = true;
            Assert.That(clipboard.TryCopy(groom, group, source, false, out _), Is.True);
            string snapshot = clipboard.Serialize(), original = EditorJsonUtility.ToJson(groom);
            Assert.That(clipboard.TryCopy(groom, group, source, true, out _), Is.False);
            Assert.That(clipboard.TryPaste(groom, group, source, out _), Is.False);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original));
            source.locked = false; group.locked = true;
            Assert.That(clipboard.TryCopy(groom, group, source, false, out _), Is.True);
            Assert.That(clipboard.TryCopy(groom, group, source, true, out _), Is.False);
            Assert.That(clipboard.TryPaste(groom, group, source, out _), Is.False);
            Assert.That(clipboard.Serialize(), Is.EqualTo(snapshot));
        }

        [Test]
        public void MapClipboardSurvivesReloadAndSupportsOtherGroupsAndGroomsOnTheSameSource()
        {
            HairGroup group = groom.Groups[0]; HairGrowthMap source = group.FindMap(HairMapKind.GrowthArea);
            source.values = new[] { 0.1f, 0.2f, 0.3f };
            var clipboard = new HairGrowthMapClipboard();
            Assert.That(clipboard.TryCopy(groom, group, source, true, out _), Is.True);
            var restored = new HairGrowthMapClipboard(clipboard.Serialize());
            HairGroup otherGroup = HairGroomCommands.AddGroup(groom, HairGroupRole.Coverage);
            Assert.That(restored.TryPaste(groom, otherGroup, otherGroup.FindMap(HairMapKind.Density), out _), Is.True);
            Assert.That(otherGroup.FindMap(HairMapKind.Density).values, Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f }));
            HairGroomAsset other = ScriptableObject.CreateInstance<HairGroomAsset>();
            try
            {
                other.SetSource(sourceMesh, groom.SourceMeshId);
                Assert.That(restored.TryPaste(other, other.Groups[0], other.Groups[0].FindMap(HairMapKind.Length), out _), Is.True);
                Assert.That(other.Groups[0].FindMap(HairMapKind.Length).values, Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f }));
                other.SetSource(sourceMesh, "different-source");
                Assert.That(restored.CanPaste(other, other.Groups[0], other.Groups[0].FindMap(HairMapKind.Length), out _), Is.False);
            }
            finally { Undo.ClearUndo(other); Object.DestroyImmediate(other); Undo.ClearUndo(groom); }
        }

        [Test]
        public void MapClipboardRejectsChangedTopologyInvalidDataAndUnownedMaps()
        {
            HairGroup group = groom.Groups[0]; HairGrowthMap source = group.FindMap(HairMapKind.GrowthArea);
            source.values = new[] { 0.2f, 0.5f, 0.8f };
            var clipboard = new HairGrowthMapClipboard();
            Assert.That(clipboard.CanPaste(groom, group, source, out _), Is.False);
            Assert.That(clipboard.TryCopy(groom, group, source, false, out _), Is.True);
            string saved = clipboard.Serialize(), original = EditorJsonUtility.ToJson(groom);
            sourceMesh.triangles = new[] { 0, 2, 1 };
            Assert.That(clipboard.TryPaste(groom, group, source, out _), Is.False, "Same vertex count is not enough.");
            Assert.That(clipboard.TryCopy(groom, group, source, true, out _), Is.False);
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original));
            sourceMesh.triangles = new[] { 0, 1, 2 };
            source.values[1] = float.NaN;
            Assert.That(clipboard.TryCopy(groom, group, source, true, out _), Is.False);
            Assert.That(clipboard.Serialize(), Is.EqualTo(saved));
            var unowned = new HairGrowthMap(); unowned.EnsureIntegrity(3);
            Assert.That(clipboard.TryPaste(groom, group, unowned, out _), Is.False);
            source.values = new[] { 0f, 0f };
            Assert.That(clipboard.CanPaste(groom, group, source, out _), Is.False);
            var invalid = new HairGrowthMapClipboard("{\"version\":999,\"values\":[0.2,0.5,0.8]}");
            Assert.That(invalid.Serialize(), Is.Empty);
        }

        [Test]
        public void GroomCreationRemembersItsLastValidFolderAndRepairsMissingLocations()
        {
            bool hadExistingPreference = EditorPrefs.HasKey(HairGroomCreationLocation.LastFolderEditorPrefKey);
            string existingPreference = EditorPrefs.GetString(HairGroomCreationLocation.LastFolderEditorPrefKey);
            try
            {
                const string validFolder = "Assets/UMA/HairCards/Editor";
                HairGroomCreationLocation.RememberAssetPath(validFolder + "/TestGroom.asset");

                Assert.That(HairGroomCreationLocation.GetLastFolder(), Is.EqualTo(validFolder));

                EditorPrefs.SetString(HairGroomCreationLocation.LastFolderEditorPrefKey,
                    "Assets/UMA/HairCards/__FolderThatDoesNotExist__");

                Assert.That(HairGroomCreationLocation.GetLastFolder(),
                    Is.EqualTo(HairGroomCreationLocation.DefaultFolder));
                Assert.That(EditorPrefs.GetString(HairGroomCreationLocation.LastFolderEditorPrefKey),
                    Is.EqualTo(HairGroomCreationLocation.DefaultFolder));
            }
            finally
            {
                if (hadExistingPreference)
                    EditorPrefs.SetString(HairGroomCreationLocation.LastFolderEditorPrefKey, existingPreference);
                else
                    EditorPrefs.DeleteKey(HairGroomCreationLocation.LastFolderEditorPrefKey);
            }
        }

        [Test]
        public void GroomIntegrityCreatesStableSerializableIdentitiesAndDefaultData()
        {
            Assert.That(groom.GroomId, Is.Not.Null.And.Not.Empty);
            Assert.That(groom.Groups, Has.Count.EqualTo(1));
            Assert.That(groom.Groups[0].Id, Is.Not.Null.And.Not.Empty);
            Assert.That(groom.Groups[0].guides[0].Id, Is.Not.Null.And.Not.Empty);
            Assert.That(groom.Groups[0].FindMap(HairMapKind.GrowthArea), Is.Not.Null);
            Assert.That(groom.Groups[0].FindMap(HairMapKind.GrowthArea).values,
                Has.Length.EqualTo(sourceMesh.vertexCount));
            Assert.That(groom.SourceTopologyMatches(), Is.True);
        }

        [Test]
        public void SurfaceAnchorEvaluatesBarycentricPositionAndNormal()
        {
            HairSurfaceAnchor anchor = HairSurfaceAnchor.Create("mesh:test", 0, 0,
                new Vector3(0.25f, 0.25f, 0.5f), 0.1f, Vector3.zero, Vector3.up);

            bool success = HairMeshUtility.TryEvaluateAnchor(sourceMesh, anchor,
                out Vector3 position, out Vector3 normal);

            Assert.That(success, Is.True);
            Assert.That(position, Is.EqualTo(new Vector3(0f, 0.1f, 0f)).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(normal, Is.EqualTo(Vector3.up).Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void AuthoringPoseMapsGuidesFromSourceTriangleOntoBakedCharacterTriangle()
        {
            Mesh posedMesh = new Mesh { name = "Posed Test Scalp" };
            Matrix4x4 expectedTransform = Matrix4x4.TRS(new Vector3(2f, 3f, -1f),
                Quaternion.Euler(90f, 0f, 35f), Vector3.one);
            Vector3[] posedVertices = new Vector3[sourceMesh.vertexCount];
            for (int vertex = 0; vertex < posedVertices.Length; vertex++)
                posedVertices[vertex] = expectedTransform.MultiplyPoint3x4(sourceMesh.vertices[vertex]);
            posedMesh.vertices = posedVertices;
            posedMesh.triangles = sourceMesh.triangles;
            try
            {
                HairSurfaceAnchor anchor = HairSurfaceAnchor.Create("mesh:test", 0, 0,
                    new Vector3(0.2f, 0.3f, 0.5f), 0f, Vector3.zero, Vector3.up);
                HairAuthoringPose pose = new HairAuthoringPose(sourceMesh, posedMesh);

                bool found = pose.TryGetMatrix(anchor, out Matrix4x4 actualTransform);
                bool posedPointFound = pose.TryPoseTrianglePoint(0, anchor.Barycentric,
                    out Vector3 posedPoint, out _);
                Vector3 guidePoint = new Vector3(0.15f, 0.4f, -0.1f);
                Vector3 sourcePoint = sourceMesh.vertices[0] * anchor.Barycentric.x +
                                      sourceMesh.vertices[1] * anchor.Barycentric.y +
                                      sourceMesh.vertices[2] * anchor.Barycentric.z;

                Assert.That(found, Is.True);
                Assert.That(posedPointFound, Is.True);
                Assert.That(posedPoint, Is.EqualTo(expectedTransform.MultiplyPoint3x4(sourcePoint))
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(actualTransform.MultiplyPoint3x4(guidePoint),
                    Is.EqualTo(expectedTransform.MultiplyPoint3x4(guidePoint))
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
            }
            finally
            {
                Object.DestroyImmediate(posedMesh);
            }
        }

        [Test]
        public void ChildGenerationIsDeterministicAndReportsExpectedCardCount()
        {
            groom.Groups[0].children.childrenPerGuide = 3;

            HairEvaluationResult first = HairGroomEvaluator.Evaluate(groom);
            HairEvaluationResult second = HairGroomEvaluator.Evaluate(groom);

            Assert.That(first.CardCount, Is.EqualTo(4));
            Assert.That(first.guideCurveCount, Is.EqualTo(1));
            Assert.That(first.childCurveCount, Is.EqualTo(3));
            Assert.That(second.CardCount, Is.EqualTo(first.CardCount));
            for (int curveIndex = 0; curveIndex < first.curves.Count; curveIndex++)
            {
                Assert.That(second.curves[curveIndex].curveId, Is.EqualTo(first.curves[curveIndex].curveId));
                Assert.That(second.curves[curveIndex].points.Count, Is.EqualTo(first.curves[curveIndex].points.Count));
                for (int pointIndex = 0; pointIndex < first.curves[curveIndex].points.Count; pointIndex++)
                {
                    Assert.That(second.curves[curveIndex].points[pointIndex].position,
                        Is.EqualTo(first.curves[curveIndex].points[pointIndex].position)
                            .Using(Vector3ComparerWithEqualsOperator.Instance));
                }
            }
        }

        [Test]
        public void WeightedChildrenBlendTheGuidesClosestToEachGeneratedRoot()
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear();
            HairGuide straightGuide = CreateLinearGuide("Straight", 101, Vector3.zero,
                Vector3.up * 2f);
            HairGuide sweptGuide = CreateLinearGuide("Swept", 202, Vector3.right * 0.12f,
                Vector3.up * 2f + Vector3.right * 0.6f);
            group.guides.Add(straightGuide);
            group.guides.Add(sweptGuide);
            group.children.childrenPerGuide = 64;
            group.children.includeGuideCard = false;
            group.children.rootSpread = 0.2f;
            group.children.clump = 0f;
            group.children.lengthVariation = 0f;
            group.children.widthVariation = 0f;
            group.children.rollVariation = 0f;
            groom.Lods[0].samplesPerCard = 3;
            groom.EnsureIntegrity();
            HairEvaluationOptions options = new HairEvaluationOptions
            {
                includeGuideCards = false,
                evaluateSurfaceAnchors = false,
                applySculptLayers = false,
                applyModifiers = false,
                applyConstraints = false
            };

            group.children.interpolation = HairGuideInterpolationMode.WeightedNearest;
            HairEvaluationResult blended = HairGroomEvaluator.Evaluate(groom, options);
            group.children.interpolation = HairGuideInterpolationMode.ExplicitParent;
            HairEvaluationResult parentOnly = HairGroomEvaluator.Evaluate(groom, options);

            HairEvaluatedCurve bestChild = null;
            float bestNeighborAdvantage = float.NegativeInfinity;
            foreach (HairEvaluatedCurve child in blended.curves)
            {
                if (!child.isChild || child.parentGuideId != straightGuide.Id) continue;
                Vector3 root = child.points[0].position;
                float advantage = Vector3.Distance(root, straightGuide.points[0].position) -
                                  Vector3.Distance(root, sweptGuide.points[0].position);
                if (advantage <= bestNeighborAdvantage) continue;
                bestNeighborAdvantage = advantage;
                bestChild = child;
            }

            Assert.That(bestChild, Is.Not.Null);
            Assert.That(bestNeighborAdvantage, Is.GreaterThan(0.01f),
                "The deterministic child population should include a root closer to the surrounding guide.");
            HairEvaluatedCurve matchingParentOnly = parentOnly.curves.Find(
                child => child.curveId == bestChild.curveId);
            Assert.That(matchingParentOnly, Is.Not.Null);
            Assert.That(bestChild.points[0].position,
                Is.EqualTo(matchingParentOnly.points[0].position)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));

            float blendedSweep = bestChild.points[^1].position.x - bestChild.points[0].position.x;
            float parentSweep = matchingParentOnly.points[^1].position.x -
                                matchingParentOnly.points[0].position.x;
            Assert.That(parentSweep, Is.EqualTo(0f).Within(0.00001f));
            Assert.That(blendedSweep, Is.GreaterThan(0.3f),
                "A child closer to the swept guide should inherit more than half of its swept shape.");
        }

        [Test]
        public void RibbonMesherBuildsExpectedTopologyAndValidAttributes()
        {
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Ribbon Test");

            Assert.That(build.cardCount, Is.EqualTo(1));
            Assert.That(build.vertexCount, Is.EqualTo(8));
            Assert.That(build.triangleCount, Is.EqualTo(10));
            Assert.That(build.mesh.vertexCount, Is.EqualTo(build.vertexCount));
            Assert.That(build.mesh.uv, Has.Length.EqualTo(build.vertexCount));
            Assert.That(build.mesh.normals, Has.Length.EqualTo(build.vertexCount));
            Assert.That(build.degenerateTriangleCount, Is.Zero);
        }

        [Test]
        public void AtlasSelectionUsesOnlyChosenNumberedAreasAndMapsCardUvsIntoTheirRectangles()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            try
            {
                HairAtlasRegion[] areas = new HairAtlasRegion[7];
                for (int areaIndex = 0; areaIndex < areas.Length; areaIndex++)
                {
                    areas[areaIndex] = atlas.CreateRegion($"Area {areaIndex + 1}",
                        new Rect(areaIndex / 8f, 0f, 0.1f, 1f), areaIndex + 1f);
                }

                List<string> selected = new List<string>
                {
                    areas[1].Id,
                    areas[2].Id,
                    areas[6].Id
                };
                HashSet<string> observed = new HashSet<string>();
                for (int sample = 0; sample < 512; sample++)
                {
                    uint randomValue = (uint)((ulong)uint.MaxValue * (uint)sample / 511u);
                    HairAtlasRegion chosen = atlas.GetWeightedRegion(randomValue,
                        HairAtlasRegionSelectionMode.Selected, selected);
                    Assert.That(chosen, Is.Not.Null);
                    Assert.That(selected, Does.Contain(chosen.Id));
                    observed.Add(chosen.Id);
                }
                Assert.That(observed, Is.EquivalentTo(selected));
                Assert.That(atlas.GetWeightedRegion(123u, HairAtlasRegionSelectionMode.Selected,
                    new[] { "missing-area" }), Is.Null);

                HairGroup group = groom.Groups[0];
                group.atlas = atlas;
                group.atlasRegionSelection = HairAtlasRegionSelectionMode.Selected;
                group.atlasRegionIds.Clear();
                group.atlasRegionIds.Add(areas[2].Id);
                group.children.childrenPerGuide = 2;
                HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
                using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Selected UV Area");
                Assert.That(build.cardCount, Is.EqualTo(3));
                foreach (Vector2 uv in build.mesh.uv)
                {
                    Assert.That(uv.x, Is.InRange(areas[2].uvRect.x, areas[2].uvRect.xMax));
                    Assert.That(uv.y, Is.InRange(areas[2].uvRect.y, areas[2].uvRect.yMax));
                }
            }
            finally
            {
                groom.Groups[0].atlas = null;
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void ValidatorBlocksSelectedAtlasModeWhenNoSelectedAreaExists()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            try
            {
                atlas.CreateRegion("Area 1", new Rect(0f, 0f, 1f, 1f));
                HairGroup group = groom.Groups[0];
                group.atlas = atlas;
                group.atlasRegionSelection = HairAtlasRegionSelectionMode.Selected;
                group.atlasRegionIds.Clear();
                group.atlasRegionIds.Add("deleted-area");

                HairValidationReport report = HairValidator.Validate(groom);

                Assert.That(report.issues.Exists(issue => issue.code == HairValidationCode.MissingAtlasRegion),
                    Is.True);
            }
            finally
            {
                groom.Groups[0].atlas = null;
                Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void InlineUvSetsAddAssignRemoveAndUndoAcrossSharedGroups()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasProfileAsset otherAtlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            try
            {
                HairGroup active = groom.Groups[0];
                active.atlas = atlas;
                active.atlasRegionSelection = HairAtlasRegionSelectionMode.Selected;
                HairGroup shared = new HairGroup { atlas = atlas };
                HairGroup unrelated = new HairGroup { atlas = otherAtlas };
                groom.Groups.Add(shared);
                groom.Groups.Add(unrelated);
                groom.EnsureIntegrity();

                Rect rectangle = new Rect(0.25f, 0.125f, 0.5f, 0.75f);
                HairAtlasRegion added = HairAtlasEditingUtility.AddSet(atlas, groom, active, rectangle);
                string setId = added.Id;
                shared.atlasRegionIds.Add(setId);
                unrelated.atlasRegionIds.Add(setId);
                Assert.That(active.atlasRegionIds, Does.Contain(setId));
                Assert.That(HairAtlasEditingUtility.AddSet(atlas, groom, active, rectangle), Is.SameAs(added));
                Assert.That(atlas.regions.Count, Is.EqualTo(1), "Drawing the same set twice should select the original.");
                Undo.FlushUndoRecordObjects();
                Undo.IncrementCurrentGroup();

                HairAtlasEditingUtility.RemoveSet(atlas, groom, setId);
                Undo.FlushUndoRecordObjects();
                Assert.That(atlas.regions, Is.Empty);
                Assert.That(active.atlasRegionIds, Does.Not.Contain(setId));
                Assert.That(shared.atlasRegionIds, Does.Not.Contain(setId));
                Assert.That(unrelated.atlasRegionIds, Does.Contain(setId));

                Undo.PerformUndo();
                Assert.That(atlas.regions.Count, Is.EqualTo(1));
                Assert.That(atlas.regions[0].Id, Is.EqualTo(setId));
                Assert.That(atlas.regions[0].uvRect, Is.EqualTo(rectangle));
                Assert.That(groom.Groups[0].atlasRegionIds, Does.Contain(setId));
                Assert.That(groom.Groups[1].atlasRegionIds, Does.Contain(setId));
            }
            finally
            {
                Undo.ClearUndo(atlas);
                Undo.ClearUndo(groom);
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(otherAtlas);
            }
        }

        [Test]
        public void InlineUvSetEditingPreservesBoundsAndMapsBottomLeftOnNonSquareAtlases()
        {
            Rect uv = new Rect(0.2f, 0.3f, 0.25f, 0.4f);
            Rect canvas = new Rect(12f, 34f, 200f, 800f);
            Rect display = HairAtlasEditingUtility.UvToCanvas(uv, canvas);
            Assert.That(display.x, Is.EqualTo(52f).Within(0.0001f));
            Assert.That(display.y, Is.EqualTo(274f).Within(0.0001f));
            Assert.That(display.width, Is.EqualTo(50f).Within(0.0001f));
            Assert.That(display.height, Is.EqualTo(320f).Within(0.0001f));

            Rect moved = HairAtlasEditingUtility.MoveRect(uv, new Vector2(2f, -2f));
            Assert.That(moved, Is.EqualTo(new Rect(0.75f, 0f, uv.width, uv.height)));
            Rect resized = HairAtlasEditingUtility.ResizeRect(uv, 0, new Vector2(0.1f, 0.9f));
            Assert.That(resized.xMin, Is.EqualTo(0.1f).Within(0.00001f));
            Assert.That(resized.xMax, Is.EqualTo(uv.xMax).Within(0.00001f));
            Assert.That(resized.yMin, Is.EqualTo(uv.yMin).Within(0.00001f));
            Assert.That(resized.yMax, Is.EqualTo(0.9f).Within(0.00001f));
            Rect noCrossing = HairAtlasEditingUtility.ResizeRect(uv, 0, new Vector2(1f, 0f));
            Assert.That(noCrossing.width, Is.GreaterThan(0f));
            Assert.That(noCrossing.height, Is.GreaterThan(0f));
            Vector2 snapped = HairAtlasEditingUtility.SnapUv(new Vector2(0.12345f, 0.67891f), 200, 800);
            Assert.That(snapped.x * 200f, Is.EqualTo(25f).Within(0.0001f));
            Assert.That(snapped.y * 800f, Is.EqualTo(543f).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator InlineUvEditorDrawsAlphaAndCheckerPreviewAtCompactAndWideSizes()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Texture2D texture = new Texture2D(16, 32);
            Material previewMaterial = new Material(Shader.Find("Unlit/Transparent"));
            HairAtlasRegionEditorWindow window = ScriptableObject.CreateInstance<HairAtlasRegionEditorWindow>();
            try
            {
                Color[] pixels = new Color[16 * 32];
                for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(0.4f, 0.1f, 0.05f, (i % 16) / 15f);
                texture.SetPixels(pixels);
                texture.Apply();
                atlas.albedo = texture;
                // The atlas, not the shared material, owns the texture (including its alpha).
                atlas.material = previewMaterial;
                atlas.CreateRegion("Fine strands", new Rect(0f, 0f, 0.4f, 1f));
                atlas.CreateRegion("Broad strands", new Rect(0.5f, 0f, 0.5f, 1f));
                typeof(HairAtlasRegionEditorWindow).GetField("atlas", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(window, atlas);
                window.position = new Rect(0f, 0f, 560f, 900f);
                window.Show();
                yield return null;
                HairAtlasEditorPanel panel = (HairAtlasEditorPanel)typeof(HairAtlasRegionEditorWindow)
                    .GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
                FieldInfo channel = typeof(HairAtlasEditorPanel).GetField("channel",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                foreach (float width in new[] { 560f, 960f })
                {
                    window.position = new Rect(0f, 0f, width, 900f);
                    for (int mode = 0; mode < 3; mode++)
                    {
                        channel.SetValue(panel, System.Enum.ToObject(channel.FieldType, mode));
                        window.Repaint();
                        yield return null;
                        yield return null;
                        LogAssert.NoUnexpectedReceived();
                    }
                }
                Assert.That(panel.lastRenderedCard, Is.Not.Null, "The inline card must render using the assigned material.");
                RenderTexture render = panel.lastRenderedCard as RenderTexture;
                Assert.That(render, Is.Not.Null);
                RenderTexture previous = RenderTexture.active;
                Texture2D capture = new Texture2D(render.width, render.height, TextureFormat.RGBA32, false);
                try
                {
                    RenderTexture.active = render;
                    capture.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0); capture.Apply();
                    System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, "../inline-card-preview.png"), capture.EncodeToPNG());
                    Color[] renderedPixels = capture.GetPixels();
                    bool hasStrandColor = System.Array.Exists(renderedPixels, pixel => pixel.r > pixel.g * 1.5f && pixel.r > 0.1f);
                    Assert.That(hasStrandColor, Is.True, "Preview should contain the reddish alpha-textured card, not just an empty background.");
                }
                finally { RenderTexture.active = previous; Object.DestroyImmediate(capture); }
                Assert.That(atlas.regions.Count, Is.EqualTo(2), "Preview changes must not edit UV sets.");
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(previewMaterial);
            }
        }

        [Test]
        public void AtlasPreviewBindingsAreIndependentReusableAndDoNotChangeSharedMaterials()
        {
            HairAtlasProfileAsset first = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasProfileAsset second = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material source = new Material(Shader.Find("Standard"));
            Texture2D texture = new Texture2D(2, 2);
            using HairPreviewMaterialSet previews = new HairPreviewMaterialSet();
            try
            {
                source.mainTexture = Texture2D.blackTexture;
                source.mainTextureScale = new Vector2(2f, 3f);
                source.mainTextureOffset = new Vector2(0.1f, 0.2f);
                source.EnableKeyword("_ALPHATEST_ON");
                source.SetFloat("_Cutoff", 0.37f);
                source.renderQueue = 2450;
                first.material = second.material = source;
                first.albedo = texture;
                first.normal = texture;
                second.albedo = Texture2D.whiteTexture;
                HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
                evaluation.curves[0].atlas = first;
                HairEvaluatedCurve other = new HairEvaluatedCurve { atlas = second, rootNormal = Vector3.forward };
                other.points.Add(new HairCurvePoint(Vector3.zero, 1f, 0f));
                other.points.Add(new HairCurvePoint(Vector3.up, 1f, 0f));
                evaluation.curves.Add(other);
                using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation);
                Assert.That(build.mesh.subMeshCount, Is.EqualTo(2), "Sharing a material must not merge different atlas bindings.");
                Assert.That(build.atlases, Is.EqualTo(new[] { first, second }));
                Material[] bound = previews.Update(build);
                Material retained = bound[0];
                Assert.That(bound[0], Is.Not.SameAs(source));
                Assert.That(bound[1], Is.Not.SameAs(bound[0]));
                Assert.That(bound[0].mainTexture, Is.SameAs(texture));
                Assert.That(bound[0].GetTexture("_BumpMap"), Is.SameAs(texture));
                Assert.That(bound[1].mainTexture, Is.SameAs(Texture2D.whiteTexture));
                Assert.That(bound[0].mainTextureScale, Is.EqualTo(Vector2.one));
                Assert.That(bound[0].mainTextureOffset, Is.EqualTo(Vector2.zero));
                Assert.That(bound[0].IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(bound[0].GetFloat("_Cutoff"), Is.EqualTo(0.37f));
                Assert.That(bound[0].renderQueue, Is.EqualTo(2450));
                Assert.That(previews.SourcesChanged(build), Is.False);
                source.SetFloat("_Cutoff", 0.6f);
                EditorUtility.SetDirty(source);
                Assert.That(previews.SourcesChanged(build), Is.True, "Shared-material Inspector changes must refresh private previews.");
                previews.Update(build);
                Assert.That(bound[0].GetFloat("_Cutoff"), Is.EqualTo(0.6f));
                first.albedo = null;
                first.normal = null;
                Assert.That(previews.Update(build), Is.SameAs(bound));
                Assert.That(bound[0], Is.SameAs(retained));
                Assert.That(bound[0].mainTexture, Is.SameAs(Texture2D.blackTexture));
                Assert.That(bound[0].mainTextureScale, Is.EqualTo(source.mainTextureScale));
                Assert.That(bound[0].GetTexture("_BumpMap"), Is.SameAs(source.GetTexture("_BumpMap")));
                Assert.That(source.mainTexture, Is.SameAs(Texture2D.blackTexture));
                Assert.That(source.mainTextureScale, Is.EqualTo(new Vector2(2f, 3f)));
                Assert.That(source.mainTextureOffset, Is.EqualTo(new Vector2(0.1f, 0.2f)));
                previews.Dispose();
                Assert.That(retained == null, Is.True, "Owned preview materials must be destroyed on cleanup.");
                Assert.That(source != null, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(first); Object.DestroyImmediate(second);
                Object.DestroyImmediate(source); Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void AtlasPreviewBindsShaderGraphBaseMapAndPreservesAlphaProperties()
        {
            // This shader exposes the same _BaseMap property as UMA3_HairShader_URP.
            Material source = new Material(Shader.Find("Hidden/UMA/HairCards/Tests/AtlasAlpha"));
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            using HairPreviewMaterialSet previews = new HairPreviewMaterialSet();
            using HairCardMeshBuildResult build = new HairCardMeshBuildResult();
            try
            {
                source.SetFloat("_Cutoff", 0.4f);
                atlas.material = source;
                atlas.albedo = Texture2D.blackTexture;
                build.atlases.Add(atlas);
                build.materials.Add(source);
                Material rendered = previews.Update(build)[0];
                Assert.That(rendered.GetTexture("_BaseMap"), Is.SameAs(Texture2D.blackTexture));
                Assert.That(rendered.GetFloat("_Cutoff"), Is.EqualTo(0.4f));
                Assert.That(source.GetTexture("_BaseMap"), Is.Null);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(atlas); }
        }

        [Test]
        public void TwoPassCardsShareGeometryAndUpdateWithoutStalePasses()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material first = new Material(Shader.Find("Standard")), second = new Material(Shader.Find("Standard"));
            using HairPreviewMaterialSet previews = new HairPreviewMaterialSet();
            using HairCardMeshBuildResult build = new HairCardMeshBuildResult();
            HairMeshBuildWorkspace workspace = new HairMeshBuildWorkspace();
            try
            {
                atlas.material = first; atlas.albedo = Texture2D.whiteTexture;
                groom.Groups[0].atlas = atlas;
                HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
                HairCardMeshGenerator.Update(evaluation, "Two passes", build, workspace);
                Vector3[] vertices = build.mesh.vertices;
                int triangles = build.triangleCount, cards = build.cardCount;
                atlas.secondPassMaterial = second;
                second.SetFloat("_Cutoff", 0.12f); second.renderQueue = 3000;
                Assert.That(build.MaterialPassLayoutMatches(), Is.False);
                HairCardMeshGenerator.Update(evaluation, "Two passes", build, workspace);
                Assert.That(build.MaterialPassLayoutMatches(), Is.True);
                Assert.That(build.mesh.subMeshCount, Is.EqualTo(2));
                Assert.That(build.materials, Is.EqualTo(new[] { first, second }));
                Assert.That(build.secondPasses, Is.EqualTo(new[] { false, true }));
                Assert.That(build.mesh.GetTriangles(1), Is.EqualTo(build.mesh.GetTriangles(0)));
                Assert.That(build.mesh.GetSubMesh(1).indexStart, Is.EqualTo(build.mesh.GetSubMesh(0).indexStart), "Reuse the index buffer as well as the vertices.");
                Assert.That(build.mesh.vertices, Is.EqualTo(vertices));
                Assert.That(build.triangleCount, Is.EqualTo(triangles), "Geometry budgets must not count an extra draw as new geometry.");
                Assert.That(build.cardCount, Is.EqualTo(cards));
                Material[] bound = previews.Update(build);
                Assert.That(bound.Length, Is.EqualTo(2));
                Assert.That(bound[1].mainTexture, Is.SameAs(Texture2D.whiteTexture));
                Assert.That(bound[1].GetFloat("_Cutoff"), Is.EqualTo(0.12f));
                Assert.That(bound[1].renderQueue, Is.EqualTo(3000));
                Assert.That(bound[1], Is.Not.SameAs(second));
                second.SetFloat("_Cutoff", 0.3f); EditorUtility.SetDirty(second);
                Assert.That(previews.SourcesChanged(build), Is.True);
                Assert.That(previews.Update(build)[1].GetFloat("_Cutoff"), Is.EqualTo(0.3f));
                Material oldSecond = bound[1];
                atlas.secondPassMaterial = null;
                Assert.That(build.MaterialPassLayoutMatches(), Is.False);
                HairCardMeshGenerator.Update(evaluation, "One pass", build, workspace);
                Assert.That(build.mesh.subMeshCount, Is.EqualTo(1));
                Assert.That(build.materials, Is.EqualTo(new[] { first }));
                Assert.That(build.MaterialPassLayoutMatches(), Is.True);
                Assert.That(previews.Update(build).Length, Is.EqualTo(1));
                Assert.That(oldSecond == null, Is.True);
            }
            finally { Object.DestroyImmediate(atlas); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void RuntimeBindsBothPassesAndReleasesOwnedMaterials()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material first = new Material(Shader.Find("Standard")), second = new Material(Shader.Find("Standard"));
            GameObject target = new GameObject("Two pass runtime test");
            try
            {
                atlas.material = first; atlas.secondPassMaterial = second; atlas.albedo = Texture2D.blackTexture;
                groom.Groups[0].atlas = atlas;
                MeshFilter filter = target.AddComponent<MeshFilter>();
                MeshRenderer renderer = target.AddComponent<MeshRenderer>();
                using (HairGroomRuntimeAPI.GeneratedHair generated = HairGroomRuntimeAPI.Generate(groom))
                {
                    HairGroomRuntimeAPI.ApplyTo(generated, filter, renderer);
                    Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(2));
                    Assert.That(filter.sharedMesh.subMeshCount, Is.EqualTo(2));
                    Material owned = renderer.sharedMaterials[1];
                    Assert.That(owned.mainTexture, Is.SameAs(Texture2D.blackTexture));
                    Assert.That(owned, Is.Not.SameAs(second));
                    HairGroomRuntimeAPI.ApplyTo(generated, filter, renderer);
                    Assert.That(renderer.sharedMaterials[1], Is.SameAs(owned));
                    generated.Dispose();
                    Assert.That(owned == null, Is.True);
                }
                Assert.That(first.mainTexture, Is.Null); Assert.That(second.mainTexture, Is.Null);
                HairGroomRuntimeComponent component = target.AddComponent<HairGroomRuntimeComponent>();
                component.SetGroom(groom);
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(2));
                Material componentOwned = renderer.sharedMaterials[1];
                Assert.That(componentOwned.mainTexture, Is.SameAs(Texture2D.blackTexture));
                component.ReleaseGeneratedMesh();
                Assert.That(componentOwned == null, Is.True);
            }
            finally { Object.DestroyImmediate(target); Object.DestroyImmediate(atlas); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void ExternalSecondPassEditsQueueOneRebuildWithoutPostponingIt()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material material = new Material(Shader.Find("Standard"));
            groom.Groups[0].atlas = atlas; atlas.material = material;
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.PreviewMode = HairPreviewMode.Cards;
                stage.RebuildNow();
                atlas.secondPassMaterial = material;
                InvokeStageMethod(stage, "EditorUpdate");
                FieldInfo deadline = typeof(HairCardStage).GetField("rebuildNotBefore", BindingFlags.Instance | BindingFlags.NonPublic);
                double scheduled = (double)deadline.GetValue(stage);
                // A pending rebuild must retain its deadline instead of being re-debounced every update.
                InvokeStageMethod(stage, "EditorUpdate");
                Assert.That((double)deadline.GetValue(stage), Is.EqualTo(scheduled));
                SetStageField(stage, "rebuildNotBefore", 0d);
                InvokeStageMethod(stage, "EditorUpdate");
                Assert.That(stage.MeshBuild.mesh.subMeshCount, Is.EqualTo(2));
                Assert.That(stage.MeshBuild.MaterialPassLayoutMatches(), Is.True);
                atlas.secondPassMaterial = null;
                InvokeStageMethod(stage, "EditorUpdate");
                SetStageField(stage, "rebuildNotBefore", 0d);
                InvokeStageMethod(stage, "EditorUpdate");
                Assert.That(stage.MeshBuild.mesh.subMeshCount, Is.EqualTo(1));
            }
            finally { DestroyEditingStage(stage); Object.DestroyImmediate(atlas); Object.DestroyImmediate(material); }
        }

        [Test]
        public void UmaBakeWarnsWhenSecondPassDoesNotMatchWithoutChangingSharedAssets()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            UMAMaterial umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
            Material second = new Material(Shader.Find("Standard"));
            try
            {
                groom.Groups[0].atlas = atlas; atlas.secondPassMaterial = second;
                groom.BakeSettings.umaMaterial = umaMaterial;
                var report = new HairValidationReport();
                HairBakePipeline.ValidateUmaMaterialPasses(groom, report);
                Assert.That(report.issues.Exists(issue => issue.code == HairValidationCode.MaterialPassMismatch), Is.True);
                Assert.That(umaMaterial.secondPass, Is.Null);
                using (SerializedObject serialized = new SerializedObject(umaMaterial))
                {
                    serialized.FindProperty("_secondPass").objectReferenceValue = second;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                report = new HairValidationReport();
                HairBakePipeline.ValidateUmaMaterialPasses(groom, report);
                Assert.That(report.issues, Is.Empty);
                atlas.secondPassMaterial = null;
                HairBakePipeline.ValidateUmaMaterialPasses(groom, report);
                Assert.That(report.issues, Is.Empty);
            }
            finally { Object.DestroyImmediate(atlas); Object.DestroyImmediate(umaMaterial); Object.DestroyImmediate(second); }
        }

        [TestCase("Unlit/Transparent Cutout", false)]
        [TestCase("Hidden/UMA/HairCards/Tests/AtlasAlpha", false)]
        [TestCase("Unlit/Transparent Cutout", true)]
        [TestCase("Hidden/UMA/HairCards/Tests/AtlasAlpha", true)]
        public void SceneCardRendererUsesAtlasAlphaWithoutChangingSourceMaterial(string shaderName, bool secondPassOnly)
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material source = new Material(Shader.Find(shaderName));
            Material second = new Material(source) { renderQueue = 3000 };
            Texture2D texture = new Texture2D(2, 2) { filterMode = FilterMode.Point };
            Texture2D capture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            GameObject card = new GameObject("Atlas alpha regression card");
            GameObject cameraObject = new GameObject("Atlas alpha regression camera");
            RenderTexture target = new RenderTexture(64, 64, 24, RenderTextureFormat.ARGB32);
            HairCardStage stage = ScriptableObject.CreateInstance<HairCardStage>();
            RenderTexture previous = RenderTexture.active;
            try
            {
                texture.SetPixels(new[] { new Color(1f, 0f, 0f, 0f), Color.red,
                    new Color(1f, 0f, 0f, 0f), Color.red });
                texture.Apply();
                atlas.albedo = texture;
                atlas.material = source;
                if (secondPassOnly)
                {
                    source.SetFloat("_Cutoff", 1.1f); // No first-pass fragments: visible strands must come from pass two.
                    atlas.secondPassMaterial = second;
                }
                profile.Configure(HairCardShape.Ribbon, 1f, 1f, 2);
                HairEvaluatedCurve curve = new HairEvaluatedCurve { atlas = atlas, profile = profile, rootNormal = Vector3.forward };
                curve.points.Add(new HairCurvePoint(Vector3.down * 0.5f, 1f, 0f));
                curve.points.Add(new HairCurvePoint(Vector3.up * 0.5f, 1f, 0f));
                HairEvaluationResult evaluation = new HairEvaluationResult();
                evaluation.curves.Add(curve);
                using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation);
                card.layer = 31;
                card.AddComponent<MeshFilter>().sharedMesh = build.mesh;
                MeshRenderer renderer = card.AddComponent<MeshRenderer>();
                SetStageField(stage, "hairRenderer", renderer);
                SetStageField(stage, "meshBuild", build);
                InvokeStageMethod(stage, "ApplyHairMaterials");
                Assert.That(renderer.sharedMaterial.mainTexture, Is.SameAs(texture));
                Assert.That(source.mainTexture, Is.Null, "Shader-default material must stay untouched.");
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.cullingMask = 1 << 31;
                camera.transform.position = new Vector3(0f, 0f, -3f);
                camera.orthographic = true;
                camera.orthographicSize = 1f;
                camera.aspect = 1f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.blue;
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, 64, 64), 0, 0); capture.Apply();
                Color a = capture.GetPixel(24, 32), b = capture.GetPixel(40, 32);
                Assert.That(Mathf.Max(a.r, b.r), Is.GreaterThan(0.9f), "Opaque strands must render.");
                Assert.That(Mathf.Min(a.r, b.r), Is.LessThan(0.1f), "Transparent texels must not fill the card rectangle.");
                Assert.That(Mathf.Max(a.b, b.b), Is.GreaterThan(0.9f), "The scene behind transparent texels must remain visible.");
                atlas.albedo = Texture2D.blackTexture;
                InvokeStageMethod(stage, "ApplyHairMaterials");
                Assert.That(renderer.sharedMaterial.mainTexture, Is.SameAs(Texture2D.blackTexture), "Texture-only edits must refresh without remeshing.");
            }
            finally
            {
                RenderTexture.active = previous;
                InvokeStageMethod(stage, "DisposeBuild");
                Object.DestroyImmediate(stage); Object.DestroyImmediate(card); Object.DestroyImmediate(cameraObject);
                target.Release(); Object.DestroyImmediate(target); Object.DestroyImmediate(capture);
                Object.DestroyImmediate(atlas); Object.DestroyImmediate(source); Object.DestroyImmediate(texture);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void TubeMesherBuildsConfigurableSideCount()
        {
            profile.Configure(HairCardShape.TaperedTube, 0.04f, 0.002f, 4, sideCount: 5,
                generateBackfaces: false);
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Tube Test");

            Assert.That(build.vertexCount, Is.EqualTo(20));
            Assert.That(build.triangleCount, Is.EqualTo(30));
            Assert.That(build.degenerateTriangleCount, Is.Zero);
        }

        [TestCase(HairCardShape.Ribbon, 0.02f, 0.02f)]
        [TestCase(HairCardShape.Ribbon, 0.02f, 0.01f)]
        [TestCase(HairCardShape.Ribbon, 0.02f, 0f)]
        [TestCase(HairCardShape.Ribbon, 0.02f, 0.03f)]
        [TestCase(HairCardShape.Ribbon, 0f, 0.02f)]
        [TestCase(HairCardShape.TaperedTube, 0.02f, 0.02f)]
        [TestCase(HairCardShape.TaperedTube, 0.02f, 0.01f)]
        [TestCase(HairCardShape.TaperedTube, 0.02f, 0f)]
        [TestCase(HairCardShape.TaperedTube, 0f, 0.02f)]
        public void ProfileWidthBlendsAcrossEveryGuideAndChildCardSample(HairCardShape shape, float rootWidth, float tipWidth)
        {
            HairGuide guide = PrepareWidthTaperFixture();
            profile.Configure(shape, rootWidth, tipWidth, 9, sideCount: 6);
            string original = EditorJsonUtility.ToJson(groom);
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            foreach (bool explicitBaseline in new[] { false, true })
            {
                if (explicitBaseline)
                    foreach (HairGuidePoint point in guide.points) point.widthBaseline = point.width;
                HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom, options);
                using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Width taper", true);
                Assert.That(build.cardCount, Is.EqualTo(3));
                AssertCardWidths(build, t => Mathf.Lerp(rootWidth, tipWidth, t));
                if (!explicitBaseline) Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original),
                    "Legacy taper inference must not rewrite existing guide data.");
            }
        }

        [TestCase(-0.003f)]
        [TestCase(0.005f)]
        public void ProfileWidthPreservesSculptOffsetsAndChildWidthModifiers(float offset)
        {
            HairGuide guide = PrepareWidthTaperFixture();
            profile.Configure(HairCardShape.Ribbon, 0.02f, 0.02f, 9);
            HairSculptLayer layer = new HairSculptLayer();
            layer.deltas.Add(new HairGuideDelta { guideId = guide.Id,
                widthOffsets = new[] { offset, offset, offset, offset } });
            groom.Groups[0].sculptLayers.Add(layer);
            groom.Groups[0].modifiers.Add(new HairModifierSettings
            { type = HairModifierType.Width, domain = HairModifierDomain.Children, amount = 2f });
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom,
                new HairEvaluationOptions { evaluateSurfaceAnchors = false });
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Sculpted width", true);
            AssertCardWidths(build, t => 0.02f + offset, childMultiplier: 2f);
        }

        [Test]
        public void ProfileWidthRemainsContinuousAfterSliceAndRespectsCustomCurve()
        {
            HairGuide guide = PrepareWidthTaperFixture();
            profile.Configure(HairCardShape.Ribbon, 0.02f, 0.01f, 9);
            profile.WidthAlongCard.keys = new[] { new Keyframe(0f, 1f), new Keyframe(0.5f, 0.8f), new Keyframe(1f, 0f) };
            Assert.That(HairSliceUtility.TruncateGuide(groom.Groups[0], guide, 0.6f), Is.True);
            HairGuide clone = guide.Clone();
            for (int i = 0; i < guide.points.Count; i++)
                Assert.That(clone.points[i].widthBaseline, Is.EqualTo(guide.points[i].widthBaseline));
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom,
                new HairEvaluationOptions { evaluateSurfaceAnchors = false });
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Sliced width", true);
            AssertCardWidths(build, profile.EvaluateWidth);
        }

        private HairGuide PrepareWidthTaperFixture()
        {
            HairGroup group = groom.Groups[0];
            HairGuide guide = group.guides[0];
            guide.points.Clear();
            // Nonuniform control spacing: the profile must follow resampled arc length, not control index.
            float[] heights = { 0f, 0.05f, 0.8f, 1f };
            for (int i = 0; i < 4; i++) guide.points.Add(new HairGuidePoint
            { position = Vector3.up * heights[i], width = Mathf.Lerp(0.012f, 0f, i / 3f) });
            group.children.childrenPerGuide = 2;
            group.children.rootSpread = group.children.lengthVariation = group.children.widthVariation = group.children.rollVariation = 0f;
            groom.Lods[0].samplesPerCard = 9;
            return guide;
        }

        private static void AssertCardWidths(HairCardMeshBuildResult build, System.Func<float, float> expected, float childMultiplier = 1f)
        {
            Vector3[] vertices = build.mesh.vertices;
            foreach (HairCardSpan span in build.cards)
            {
                int stride = span.curve.profile.Shape == HairCardShape.TaperedTube ? 6 : 2;
                int samples = span.vertexCount / stride;
                for (int i = 0; i < samples; i++)
                {
                    int index = span.vertexStart + i * stride;
                    float width = Vector3.Distance(vertices[index], vertices[index + stride / 2]);
                    float wanted = expected(i / (samples - 1f)) * (span.curve.isChild ? childMultiplier : 1f);
                    Assert.That(width, Is.EqualTo(wanted).Within(0.000002f),
                        $"{span.curve.profile.Shape}, child={span.curve.isChild}, sample {i}/{samples - 1}");
                }
            }
        }

        [Test]
        public void ValidatorReportsMissingAtlasButAllowsGeometryWhenAtlasIsOptional()
        {
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation);

            HairValidationReport report = HairValidator.Validate(groom, evaluation, build,
                new HairValidationOptions { requireAtlas = false });

            Assert.That(report.ErrorCount, Is.Zero);
            Assert.That(report.WarningCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(report.issues.Exists(issue => issue.code == HairValidationCode.MissingAtlas), Is.True);
        }

        [Test]
        public void SourceTopologyChangeIsDetectedWithoutUsingTransientObjectIdentity()
        {
            sourceMesh.triangles = new[] { 0, 2, 1 };

            Assert.That(groom.SourceTopologyMatches(), Is.False);
            HairValidationReport report = HairValidator.Validate(groom);
            Assert.That(report.issues.Exists(issue => issue.code == HairValidationCode.SourceTopologyChanged), Is.True);
        }

        [Test]
        public void SourceVisibilityRemovesHiddenSlotTrianglesAndPreservesTriangleOwnership()
        {
            Mesh mesh = new Mesh { name = "Visibility Source" };
            SlotDataAsset firstAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset secondAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                mesh.vertices = new[]
                {
                    Vector3.zero, Vector3.right, Vector3.up,
                    Vector3.forward, Vector3.forward + Vector3.right, Vector3.forward + Vector3.up
                };
                mesh.triangles = new[] { 0, 1, 2, 3, 4, 5 };
                firstAsset.name = "Head";
                firstAsset.meshData = new UMAMeshData { vertexCount = 3 };
                secondAsset.name = "Helmet";
                secondAsset.meshData = new UMAMeshData { vertexCount = 3 };
                SlotData first = new SlotData(firstAsset) { vertexOffset = 0 };
                SlotData second = new SlotData(secondAsset) { vertexOffset = 3 };
                Dictionary<string, SlotData> slots = new Dictionary<string, SlotData>
                {
                    { first.slotName, first },
                    { second.slotName, second }
                };

                using HairSourceVisibility visibility = new HairSourceVisibility(mesh, null, null, slots);
                Mesh all = visibility.Rebuild(new HashSet<string>());
                Assert.That(all.triangles.Length / 3, Is.EqualTo(2));

                Mesh filtered = visibility.Rebuild(new HashSet<string> { first.slotName });
                Assert.That(filtered.triangles.Length / 3, Is.EqualTo(1));
                Assert.That(visibility.IsVertexVisible(0, new HashSet<string> { first.slotName }), Is.False);
                Assert.That(visibility.IsVertexVisible(3, new HashSet<string> { first.slotName }), Is.True);
                Assert.That(visibility.TryResolveVisibleTriangle(0, out HairSourceVisibility.TriangleReference triangle),
                    Is.True);
                Assert.That(triangle.Triangle, Is.EqualTo(1));
                Assert.That(new[] { triangle.A, triangle.B, triangle.C }, Is.EqualTo(new[] { 3, 4, 5 }));
            }
            finally
            {
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SurfaceRaycasterHitsReadableMeshWithoutEditorPhysics()
        {
            HairMeshRaycaster raycaster = new HairMeshRaycaster(sourceMesh);

            bool found = raycaster.Raycast(new Ray(Vector3.up, Vector3.down),
                out HairMeshRaycastHit hit);

            Assert.That(found, Is.True);
            Assert.That(hit.TriangleIndex, Is.Zero);
            Assert.That(hit.Point, Is.EqualTo(Vector3.zero)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(hit.Normal, Is.EqualTo(Vector3.up)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(hit.Barycentric.x + hit.Barycentric.y + hit.Barycentric.z,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(raycaster.TryGetTriangleVertices(hit.TriangleIndex,
                out int a, out int b, out int c), Is.True);
            Assert.That(new[] { a, b, c }, Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void SourceVisibilityUsesPosedSurfaceWhileKeepingSourceTriangleReferences()
        {
            Mesh posed = Object.Instantiate(sourceMesh);
            try
            {
                Vector3[] posedVertices = posed.vertices;
                for (int i = 0; i < posedVertices.Length; i++) posedVertices[i] += Vector3.up * 2f;
                posed.vertices = posedVertices;
                posed.RecalculateBounds();

                using HairSourceVisibility visibility = new HairSourceVisibility(sourceMesh, posed,
                    null, null, null);
                Mesh visible = visibility.Rebuild(new HashSet<string>());

                Assert.That(visible.vertices[0], Is.EqualTo(sourceMesh.vertices[0] + Vector3.up * 2f)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(visibility.TryResolveVisibleTriangle(0,
                    out HairSourceVisibility.TriangleReference triangle), Is.True);
                Assert.That(new[] { triangle.A, triangle.B, triangle.C }, Is.EqualTo(new[] { 0, 1, 2 }));
            }
            finally
            {
                Object.DestroyImmediate(posed);
            }
        }

        [Test]
        public void VisibilityCatalogGroupsUdimMembersWhileRetainingIndividualSlots()
        {
            SlotDataAsset firstAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            SlotDataAsset secondAsset = ScriptableObject.CreateInstance<SlotDataAsset>();
            try
            {
                firstAsset.name = "Face1001";
                firstAsset.udimGroupId = "face";
                firstAsset.udimGroupName = "Face UDIM";
                firstAsset.udimTileNumber = 1001;
                secondAsset.name = "Face1002";
                secondAsset.udimGroupId = "face";
                secondAsset.udimGroupName = "Face UDIM";
                secondAsset.udimTileNumber = 1002;
                Dictionary<string, SlotData> slots = new Dictionary<string, SlotData>
                {
                    { firstAsset.slotName, new SlotData(firstAsset) },
                    { secondAsset.slotName, new SlotData(secondAsset) }
                };

                HairAvatarVisibilityCatalog catalog = HairAvatarVisibilityCatalog.Build(null, slots);

                Assert.That(catalog.UdimGroups, Has.Count.EqualTo(1));
                Assert.That(catalog.UdimGroups[0].SlotNames,
                    Is.EquivalentTo(new[] { firstAsset.slotName, secondAsset.slotName }));
                Assert.That(catalog.SlotGroups, Has.Count.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(firstAsset);
                Object.DestroyImmediate(secondAsset);
            }
        }

        [Test]
        public void GuideGeneratorUsesGrowthAreaAndIsDeterministic()
        {
            HairGroup group = groom.Groups[0];
            HairGrowthMap growth = group.FindMap(HairMapKind.GrowthArea);
            for (int i = 0; i < growth.values.Length; i++) growth.values[i] = 1f;
            HairGuideGenerationSettings settings = new HairGuideGenerationSettings
            {
                guideCount = 8,
                minimumRootSpacing = 0f,
                defaultLength = 0.2f,
                seed = 42
            };

            HairGuideGenerationResult first = HairGuideGenerator.Generate(groom, group, settings);
            HairGuideGenerationResult second = HairGuideGenerator.Generate(groom, group, settings);

            Assert.That(first.guides, Has.Count.EqualTo(8));
            Assert.That(second.guides, Has.Count.EqualTo(8));
            for (int i = 0; i < first.guides.Count; i++)
            {
                Assert.That(second.guides[i].root.CachedLocalPosition,
                    Is.EqualTo(first.guides[i].root.CachedLocalPosition)
                        .Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(first.guides[i].root.IsValid, Is.True);
            }
        }

        [Test]
        public void UniformLowGrowthStrengthIsNotAppliedTwiceDuringGuideDistribution()
        {
            HairGroup group = groom.Groups[0];
            HairGrowthMap growth = group.FindMap(HairMapKind.GrowthArea);
            for (int vertex = 0; vertex < growth.values.Length; vertex++) growth.values[vertex] = 0.2f;
            HairGuideGenerationSettings settings = new HairGuideGenerationSettings
            {
                guideCount = 8,
                pointsPerGuide = 4,
                minimumRootSpacing = 0f,
                defaultLength = 0.2f,
                seed = 31415
            };

            HairGuideGenerationResult result = HairGuideGenerator.Generate(groom, group, settings);

            Assert.That(result.guides, Has.Count.EqualTo(8));
            Assert.That(result.rejectedByMask, Is.Zero,
                "Uniform strength should scale triangle density, not be reused as a second rejection probability.");
        }

        [Test]
        public void RootUniformityImprovesMeasuredSpacingAndRemainsDeterministic()
        {
            HairGroup group = groom.Groups[0];
            group.FindMap(HairMapKind.GrowthArea).values = new[] { 1f, 1f, 1f };
            double randomMean = 0d, uniformMean = 0d, randomCv = 0d, uniformCv = 0d;
            foreach (int seed in new[] { 42, 1729, 991, 731 })
            {
                HairGuideGenerationSettings settings = new HairGuideGenerationSettings
                { guideCount = 128, pointsPerGuide = 2, minimumRootSpacing = 0f, rootUniformity = 0f, seed = seed };
                HairGuideGenerationResult random = HairGuideGenerator.Generate(groom, group, settings);
                Assert.That(random.attemptedRoots, Is.EqualTo(128), "Zero uniformity should take the original single random candidate path.");
                settings.rootUniformity = 1f;
                HairGuideGenerationResult uniform = HairGuideGenerator.Generate(groom, group, settings);
                HairGuideGenerationResult repeat = HairGuideGenerator.Generate(groom, group, settings);
                Assert.That(uniform.guides.Count, Is.EqualTo(128));
                for (int i = 0; i < 128; i++)
                    Assert.That(uniform.guides[i].root.CachedLocalPosition, Is.EqualTo(repeat.guides[i].root.CachedLocalPosition));
                RootSpacingStatistics(random, out double mean, out double cv);
                randomMean += mean; randomCv += cv;
                RootSpacingStatistics(uniform, out mean, out cv);
                uniformMean += mean; uniformCv += cv;
            }
            TestContext.WriteLine($"GUIDE_UNIFORMITY: nearest spacing random={randomMean / 4:F6}, uniform={uniformMean / 4:F6}; spacing CV random={randomCv / 4:F4}, uniform={uniformCv / 4:F4}");
            Assert.That(uniformMean, Is.GreaterThan(randomMean * 1.25d), "Uniform placement should reduce accidental close pairs.");
            Assert.That(uniformCv, Is.LessThan(randomCv * 0.75d), "Nearest-neighbor spacing should be measurably more even.");
        }

        private static void RootSpacingStatistics(HairGuideGenerationResult result, out double mean, out double cv)
        {
            double sum = 0d, sumSquares = 0d;
            foreach (HairGuide guide in result.guides)
            {
                float nearest = float.PositiveInfinity;
                foreach (HairGuide other in result.guides)
                    if (other != guide) nearest = Mathf.Min(nearest, Vector3.Distance(guide.root.CachedLocalPosition, other.root.CachedLocalPosition));
                sum += nearest; sumSquares += nearest * nearest;
            }
            mean = sum / result.guides.Count;
            cv = System.Math.Sqrt(System.Math.Max(0d, sumSquares / result.guides.Count - mean * mean)) / mean;
        }

        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void RootUniformityKeepsMinimumSpacingAndReportsImpossibleCounts(float uniformity)
        {
            HairGroup group = groom.Groups[0];
            group.FindMap(HairMapKind.GrowthArea).values = new[] { 1f, 1f, 1f };
            HairGuideGenerationSettings settings = new HairGuideGenerationSettings
            { guideCount = 160, pointsPerGuide = 2, minimumRootSpacing = 0.025f, rootUniformity = uniformity, seed = 31415 };
            HairGuideGenerationResult result = HairGuideGenerator.Generate(groom, group, settings);
            Assert.That(result.guides.Count, Is.EqualTo(160));
            for (int i = 0; i < result.guides.Count; i++)
            for (int j = i + 1; j < result.guides.Count; j++)
                Assert.That(Vector3.Distance(result.guides[i].root.CachedLocalPosition, result.guides[j].root.CachedLocalPosition),
                    Is.GreaterThanOrEqualTo(0.025f - 0.000001f), "Check both indexed roots and newly accepted roots.");
            settings.guideCount = 8;
            settings.minimumRootSpacing = 10f;
            result = HairGuideGenerator.Generate(groom, group, settings);
            Assert.That(result.guides.Count, Is.EqualTo(1));
            Assert.That(result.rejectedBySpacing, Is.GreaterThan(0));
            Assert.That(result.warnings, Is.Not.Empty);
        }

        [Test]
        public void UniformGuidePlacementRetainsPaintedDensityAndExcludesZeroGrowth()
        {
            sourceMesh.vertices = new[] { new Vector3(-3f, 0f, 0f), new Vector3(-2f, 0f, 0f), new Vector3(-3f, 0f, 1f),
                Vector3.zero, Vector3.right, Vector3.forward,
                new Vector3(3f, 0f, 0f), new Vector3(4f, 0f, 0f), new Vector3(3f, 0f, 1f) };
            sourceMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            sourceMesh.triangles = new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 };
            groom.SetSource(sourceMesh, "mesh:test", "TestRace", "TestScalp");
            HairGroup group = groom.Groups[0];
            group.FindMap(HairMapKind.GrowthArea).values = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f };
            group.FindMap(HairMapKind.Density).values = new[] { 0.25f, 0.25f, 0.25f, 1f, 1f, 1f, 1f, 1f, 1f };
            string before = EditorJsonUtility.ToJson(groom);
            HairGuideGenerationResult result = HairGuideGenerator.Generate(groom, group, new HairGuideGenerationSettings
            { guideCount = 160, pointsPerGuide = 2, rootUniformity = 1f, minimumRootSpacing = 0f });
            int low = 0, high = 0;
            foreach (HairGuide guide in result.guides)
            {
                float x = guide.root.CachedLocalPosition.x;
                Assert.That(x, Is.LessThan(2f), "Zero Growth Area must never receive roots.");
                if (x < -1f) low++; else high++;
                Assert.That(guide.root.IsValid, Is.True);
            }
            Assert.That(low + high, Is.EqualTo(160));
            Assert.That(low, Is.GreaterThan(0));
            Assert.That(high, Is.GreaterThan(low * 2), "Uniformity must not flatten the deliberately painted 4:1 density contrast.");
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before), "Preview must not reposition accepted guides or alter paint.");
        }

        [TestCase(HairWorkflowStep.Setup, HairSceneTool.Select, HairPreviewMode.Cards)]
        [TestCase(HairWorkflowStep.Growth, HairSceneTool.PaintGrowth, HairPreviewMode.GrowthMap)]
        [TestCase(HairWorkflowStep.Guides, HairSceneTool.Select, HairPreviewMode.Guides)]
        [TestCase(HairWorkflowStep.Groom, HairSceneTool.Comb, HairPreviewMode.GuidesAndChildren)]
        [TestCase(HairWorkflowStep.Cards, HairSceneTool.Select, HairPreviewMode.Cards)]
        [TestCase(HairWorkflowStep.Optimize, HairSceneTool.Select, HairPreviewMode.Cards)]
        [TestCase(HairWorkflowStep.ValidateAndBake, HairSceneTool.Select, HairPreviewMode.Cards)]
        public void WorkflowStepsChooseScopedToolsAndUsefulPreviews(HairWorkflowStep step,
            HairSceneTool expectedTool, HairPreviewMode expectedPreview)
        {
            Assert.That(HairWorkflowState.DefaultTool(step), Is.EqualTo(expectedTool));
            Assert.That(HairWorkflowState.DefaultPreview(step), Is.EqualTo(expectedPreview));
            Assert.That(HairWorkflowState.IsToolAllowed(step, expectedTool), Is.True);
        }

        [Test]
        public void PaintToolCannotRemainActiveInGuidesGroomOrOutputSteps()
        {
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Growth,
                HairSceneTool.PaintGrowth), Is.True);
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Guides,
                HairSceneTool.PaintGrowth), Is.False);
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Groom,
                HairSceneTool.PaintGrowth), Is.False);
            Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Cards,
                HairSceneTool.PaintGrowth), Is.False);
            Assert.That(HairWorkflowState.StepForTool(HairSceneTool.PlaceGuide,
                HairWorkflowStep.Growth), Is.EqualTo(HairWorkflowStep.Guides));
            Assert.That(HairWorkflowState.StepForTool(HairSceneTool.Comb,
                HairWorkflowStep.Guides), Is.EqualTo(HairWorkflowStep.Groom));
        }

        [Test]
        public void GroomBrushPicksTheWholeGuideSegmentInsteadOfOnlyControlPoints()
        {
            Ray ray = new Ray(new Vector3(0f, 1f, -2f), Vector3.forward);

            bool found = HairCurveBrushUtility.TryClosestPoint(ray,
                new Vector3(-1f, 0f, 0f), new Vector3(1f, 0f, 0f),
                out Vector3 point, out float squareDistance);

            Assert.That(found, Is.True);
            Assert.That(point, Is.EqualTo(Vector3.zero).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(squareDistance, Is.EqualTo(1f).Within(0.00001f));
        }

        [Test]
        public void GroomBrushConvertsAHitBetweenSparsePointsIntoControlInfluence()
        {
            Vector3[] sparseDisplayedGuide =
            {
                new Vector3(-1f, 0f, 0f),
                new Vector3(1f, 0f, 0f)
            };
            float[] influences = new float[2];

            bool affected = HairCurveBrushUtility.FillControlPointInfluences(
                sparseDisplayedGuide, Vector3.zero, 0.1f, 0.75f, influences);

            Assert.That(affected, Is.True);
            Assert.That(influences[0], Is.EqualTo(1f));
            Assert.That(influences[1], Is.EqualTo(1f));
            Assert.That(HairCurveBrushUtility.FillControlPointInfluences(
                sparseDisplayedGuide, Vector3.up, 0.1f, 0.75f, influences), Is.False);
            Assert.That(influences, Is.All.EqualTo(0f));
        }

        [Test]
        public void GroomBrushSamplesResampledDisplayCurveAtAuthoredControlParameters()
        {
            Vector3[] displayedGuide =
            {
                Vector3.zero,
                Vector3.right,
                Vector3.right * 2f,
                Vector3.right * 3f,
                Vector3.right * 4f
            };

            Vector3 sample = HairCurveBrushUtility.SamplePolyline(displayedGuide, 0.625f);

            Assert.That(sample, Is.EqualTo(Vector3.right * 2.5f)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void ProjectedGroomBrushCanAffectGuidesAtDifferentDepths()
        {
            Vector3[] deepGuide =
            {
                new Vector3(-1f, 0f, 5f),
                new Vector3(1f, 0f, 5f)
            };
            float[] influences = new float[2];

            Assert.That(HairCurveBrushUtility.FillControlPointInfluences(deepGuide,
                Vector3.zero, 0.1f, 0.75f, influences), Is.False);
            Assert.That(HairCurveBrushUtility.FillControlPointInfluences(deepGuide,
                Vector3.zero, 0.1f, 0.75f, Vector3.forward, true, influences), Is.True);
            Assert.That(influences[1], Is.EqualTo(1f));
        }

        [Test]
        public void BrushFalloffMatchesOverlayPainterHardnessModel()
        {
            const float radius = 2f;
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(0f, radius, 0.75f), Is.EqualTo(1f));
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(1.5f, radius, 0.75f), Is.EqualTo(1f));
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(1.75f, radius, 0.75f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(2f, radius, 0.75f), Is.Zero);
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(1f, radius, 0f),
                Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(HairBrushInteractionUtility.EvaluateFalloff(1.99f, radius, 1f), Is.EqualTo(1f));
        }

        [Test]
        public void BrushResizeControlsMatchOverlayPainterSensitivityAndLimits()
        {
            float resized = HairBrushInteractionUtility.RadiusFromModifierDrag(0.05f, 100f);
            Assert.That(resized, Is.EqualTo(0.05f * Mathf.Exp(1.2f)).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.HardnessFromModifierDrag(0.75f, 90f),
                Is.EqualTo(0.25f).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.StepRadius(0.05f, 1f),
                Is.EqualTo(0.056f).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.StepHardness(0.75f, -1f),
                Is.EqualTo(0.70f).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.RadiusFromModifierDrag(0.5f, 1000f),
                Is.EqualTo(HairBrushInteractionUtility.MaximumRadius));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GrowthPaintingKeepsSoftFalloffAcrossOverlappingSamples(bool erase)
        {
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right * 0.25f, Vector3.right * 0.45f };
            sourceMesh.RecalculateBounds();
            groom.EnsureIntegrity();
            HairGrowthMap map = groom.Groups[0].FindMap(HairMapKind.GrowthArea);
            HairCardStage stage = CreateEditingStage();
            try
            {
                SetStageField(stage, "sceneTool", HairSceneTool.PaintGrowth);
                SetStageField(stage, "vertexSpatialIndex", new HairVertexSpatialIndex(sourceMesh));
                SetStageField(stage, "mirrorPaintX", false);
                stage.BrushRadius = 0.5f; stage.BrushStrength = 1f; stage.BrushHardness = 0f;
                stage.PaintErase = erase;
                for (int i = 0; i < map.values.Length; i++) map.values[i] = erase ? 1f : 0f;
                InvokeStageMethod(stage, "BeginStroke", "Test soft growth stroke");
                for (int sample = 0; sample < 80; sample++) InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                Assert.That(map.values[0], Is.EqualTo(erase ? 0f : 1f).Within(1e-5f));
                Assert.That(map.values[1], Is.EqualTo(0.5f).Within(1e-5f), "Overlapping samples must not harden the same stroke's soft edge.");
                Assert.That(map.values[2], Is.EqualTo(erase ? 0.9f : 0.1f).Within(1e-5f));
                InvokeStageMethod(stage, "EndStroke");
                InvokeStageMethod(stage, "BeginStroke", "Test separate growth stroke");
                InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                Assert.That(map.values[1], Is.EqualTo(erase ? 0.25f : 0.75f).Within(1e-5f), "A new stroke can build on the previous stroke.");
                InvokeStageMethod(stage, "EndStroke");
                stage.BrushHardness = 1f;
                InvokeStageMethod(stage, "BeginStroke", "Test hard growth stroke");
                InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                Assert.That(map.values[2], Is.EqualTo(erase ? 0f : 1f).Within(1e-5f));
            }
            finally { DestroyEditingStage(stage); }
        }

        [TestCase(KeyCode.LeftShift, false)]
        [TestCase(KeyCode.RightShift, false)]
        [TestCase(KeyCode.LeftShift, true)]
        public void ShiftEraseIsTemporaryAndRestoresTheSelectedMode(KeyCode key, bool selectedErase)
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SceneTool = HairSceneTool.PaintGrowth;
                stage.PaintErase = selectedErase;
                var press = new Event { type = EventType.KeyDown, keyCode = key, modifiers = EventModifiers.Shift };
                Assert.That(stage.UpdateTemporaryPaintErase(press), Is.True);
                Assert.That(stage.EffectivePaintErase, Is.True);
                Assert.That(stage.PaintErase, Is.EqualTo(selectedErase));
                Assert.That(stage.SceneHelpText, Does.StartWith("ERASE"));
                Assert.That(stage.SceneHelpText, Does.Contain("Hold Shift: erase"));
                Assert.That(stage.SceneHelpText, Does.Contain("Shift + right-drag"));
                Assert.That(press.type, Is.EqualTo(EventType.KeyDown), "Do not consume Shift shortcuts or resize gestures.");
                Assert.That(HairPreferenceCodec.Capture(stage), Does.Not.Contain("shiftPaintErase"));
                Assert.That(EditorJsonUtility.ToJson(stage), Does.Not.Contain("shiftPaintErase"));
                stage.UpdateTemporaryPaintErase(new Event { type = EventType.KeyUp, keyCode = key, modifiers = EventModifiers.Shift });
                Assert.That(stage.EffectivePaintErase, Is.EqualTo(selectedErase), "Key-up must release even if its modifier flags still contain Shift.");
                Assert.That(stage.SceneHelpText, Does.StartWith(selectedErase ? "ERASE" : "PAINT"));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void ShiftEraseChangesDirectionWithinOneMirroredUndoablePaintStroke()
        {
            sourceMesh.vertices = new[] { Vector3.left * 0.25f, Vector3.zero, Vector3.right * 0.25f };
            sourceMesh.RecalculateBounds(); groom.EnsureIntegrity();
            HairGrowthMap map = groom.Groups[0].FindMap(HairMapKind.GrowthArea);
            map.values = new[] { 0.2f, 0.2f, 0.2f };
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SceneTool = HairSceneTool.PaintGrowth;
                SetStageField(stage, "vertexSpatialIndex", new HairVertexSpatialIndex(sourceMesh));
                stage.MirrorPaintX = true; stage.BrushRadius = 0.15f;
                stage.BrushHardness = 1f; stage.BrushStrength = 0.5f; stage.PaintValue = 1f;
                InvokeStageMethod(stage, "BeginStroke", "Temporary erase test");
                InvokeStageMethod(stage, "PaintMapAt", Vector3.left * 0.25f);
                Assert.That(map.values, Is.EqualTo(new[] { 0.6f, 0.2f, 0.6f }).Within(1e-5f));
                stage.UpdateTemporaryPaintErase(new Event { type = EventType.MouseDrag, modifiers = EventModifiers.Shift });
                InvokeStageMethod(stage, "PaintMapAt", Vector3.left * 0.25f);
                Assert.That(map.values, Is.EqualTo(new[] { 0.3f, 0.2f, 0.3f }).Within(1e-5f));
                stage.UpdateTemporaryPaintErase(new Event { type = EventType.MouseDrag });
                InvokeStageMethod(stage, "PaintMapAt", Vector3.left * 0.25f);
                Assert.That(map.values, Is.EqualTo(new[] { 0.65f, 0.2f, 0.65f }).Within(1e-5f));
                Assert.That(stage.PaintErase, Is.False);
                InvokeStageMethod(stage, "EndStroke");
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(groom.Groups[0].FindMap(HairMapKind.GrowthArea).values, Is.EqualTo(new[] { 0.2f, 0.2f, 0.2f }));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void ShiftEraseClearsOnExitAndDoesNotReverseGroomingTools()
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SceneTool = HairSceneTool.PaintGrowth;
                stage.UpdateTemporaryPaintErase(new Event { type = EventType.KeyDown, keyCode = KeyCode.LeftShift });
                Assert.That(stage.EffectivePaintErase, Is.True);
                stage.UpdateTemporaryPaintErase(new Event { type = EventType.MouseLeaveWindow, modifiers = EventModifiers.Shift });
                Assert.That(stage.EffectivePaintErase, Is.False);
                stage.UpdateTemporaryPaintErase(new Event { type = EventType.MouseMove, modifiers = EventModifiers.Shift });
                InvokeStageMethod(stage, "ReleaseSceneInputCapture", true);
                Assert.That(stage.EffectivePaintErase, Is.False);
                stage.SceneTool = HairSceneTool.Comb;
                stage.UpdateTemporaryPaintErase(new Event { type = EventType.KeyDown, keyCode = KeyCode.LeftShift, modifiers = EventModifiers.Shift });
                Assert.That(stage.EffectivePaintErase, Is.False);
                Assert.That(stage.SceneHelpText, Does.Not.Contain("Hold Shift: erase"));
                stage.SceneTool = HairSceneTool.PaintGrowth;
                Assert.That(stage.EffectivePaintErase, Is.False);
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void GrowthStrokeHonorsStrengthMirroringAndStrongerCoverageWithoutOverpainting()
        {
            sourceMesh.vertices = new[] { Vector3.left * 0.25f, Vector3.zero, Vector3.right * 0.25f };
            sourceMesh.RecalculateBounds(); groom.EnsureIntegrity();
            HairGrowthMap map = groom.Groups[0].FindMap(HairMapKind.GrowthArea);
            HairCardStage stage = CreateEditingStage();
            try
            {
                SetStageField(stage, "sceneTool", HairSceneTool.PaintGrowth);
                SetStageField(stage, "vertexSpatialIndex", new HairVertexSpatialIndex(sourceMesh));
                SetStageField(stage, "mirrorPaintX", true);
                stage.BrushRadius = 0.5f; stage.BrushStrength = 0.4f; stage.BrushHardness = 0f;
                InvokeStageMethod(stage, "BeginStroke", "Mirrored soft stroke");
                for (int i = 0; i < 40; i++) InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                Assert.That(map.values, Is.EqualTo(new[] { 0.2f, 0.4f, 0.2f }).Within(1e-5f));
                InvokeStageMethod(stage, "PaintMapAt", Vector3.right * 0.125f);
                Assert.That(map.values, Is.EqualTo(new[] { 0.3f, 0.4f, 0.3f }).Within(1e-5f));
                InvokeStageMethod(stage, "PaintMapAt", Vector3.right * 0.25f);
                Assert.That(map.values, Is.EqualTo(new[] { 0.4f, 0.4f, 0.4f }).Within(1e-5f));
                map.locked = true;
                stage.BrushStrength = 1f;
                InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                Assert.That(map.values, Is.EqualTo(new[] { 0.4f, 0.4f, 0.4f }).Within(1e-5f));
                InvokeStageMethod(stage, "EndStroke");
                Undo.PerformUndo();
                Assert.That(groom.Groups[0].FindMap(HairMapKind.GrowthArea).values, Is.EqualTo(new[] { 0f, 0f, 0f }));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void GrowthStrokeRefreshesBaselineWhenItsMapStorageOrTargetChanges()
        {
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right * 0.25f, Vector3.right * 0.45f };
            sourceMesh.RecalculateBounds(); groom.EnsureIntegrity();
            HairGrowthMap map = groom.Groups[0].FindMap(HairMapKind.GrowthArea);
            HairCardStage stage = CreateEditingStage();
            try
            {
                SetStageField(stage, "sceneTool", HairSceneTool.PaintGrowth);
                SetStageField(stage, "vertexSpatialIndex", new HairVertexSpatialIndex(sourceMesh));
                SetStageField(stage, "mirrorPaintX", false);
                stage.BrushRadius = 0.5f; stage.BrushStrength = 1f; stage.BrushHardness = 0f;
                InvokeStageMethod(stage, "BeginStroke", "Refresh stroke baseline");
                InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                map.values = new[] { 0.2f, 0.2f, 0.2f };
                InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                Assert.That(map.values[1], Is.EqualTo(0.6f).Within(1e-5f));
                stage.PaintErase = true;
                InvokeStageMethod(stage, "PaintMapAt", Vector3.zero);
                Assert.That(map.values[1], Is.EqualTo(0.3f).Within(1e-5f));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void MirroredBrushUsesLocalXPlaneAndMaximumFalloff()
        {
            Vector3 center = new Vector3(0.5f, 0.1f, -0.2f);
            Vector3 mirroredCenter = HairBrushInteractionUtility.MirrorX(center);

            Assert.That(mirroredCenter, Is.EqualTo(new Vector3(-0.5f, 0.1f, -0.2f))
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(HairBrushInteractionUtility.EvaluateMirroredFalloff(
                mirroredCenter, center, 0.25f, 0.75f, true), Is.EqualTo(1f));
            Assert.That(HairBrushInteractionUtility.EvaluateMirroredFalloff(
                mirroredCenter, center, 0.25f, 0.75f, false), Is.Zero);

            // Taking the maximum prevents the two footprints from doubling strength on the centerline.
            float centerline = HairBrushInteractionUtility.EvaluateMirroredFalloff(
                Vector3.zero, Vector3.zero, 1f, 0.5f, true);
            Assert.That(centerline, Is.EqualTo(
                HairBrushInteractionUtility.EvaluateFalloff(0f, 1f, 0.5f)));
        }

        [Test]
        public void BrushAdjustmentCursorStaysAnchoredAndUsesLiveWorldRadiusWithoutEditingHair()
        {
            HairCardStage stage = CreateEditingStage();
            GameObject space = new GameObject("Brush radius space");
            try
            {
                space.transform.localScale = Vector3.one * 2f;
                SetStageField(stage, "sourceSpaceObject", space);
                SetStageField(stage, "sceneTool", HairSceneTool.Comb);
                stage.BrushRadius = 0.1f;
                string before = JsonUtility.ToJson(groom);
                stage.CaptureBrushAdjustmentCursor(new Ray(new Vector3(0f, 0.25f, -2f), Vector3.forward), Vector3.back, Vector3.zero);
                Assert.That(stage.TryGetBrushAdjustmentCursor(out Vector3 center, out Vector3 normal, out float radius), Is.True);
                Assert.That(normal, Is.EqualTo(Vector3.back));
                Assert.That(radius, Is.EqualTo(0.2f).Within(1e-6f));
                stage.BrushRadius = 0.3f;
                stage.BrushHardness = 0.25f;
                Assert.That(stage.TryGetBrushAdjustmentCursor(out Vector3 resizedCenter, out _, out radius), Is.True);
                Assert.That(resizedCenter, Is.EqualTo(center));
                Assert.That(radius, Is.EqualTo(0.6f).Within(1e-6f));
                Assert.That(JsonUtility.ToJson(groom), Is.EqualTo(before));
                InvokeStageMethod(stage, "ReleaseBrushModifierCapture");
                Assert.That(stage.TryGetBrushAdjustmentCursor(out _, out _, out _), Is.False);
            }
            finally { SetStageField(stage, "sourceSpaceObject", null); Object.DestroyImmediate(space); DestroyEditingStage(stage); }
        }

        [Test]
        public void BrushAdjustmentUsesPaintSurfaceOrientationAndFallsBackOffSurface()
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                SetStageField(stage, "sceneTool", HairSceneTool.PaintGrowth);
                SetStageField(stage, "surfaceRaycaster", new HairMeshRaycaster(sourceMesh));
                stage.CaptureBrushAdjustmentCursor(new Ray(Vector3.up * 2f, Vector3.down), Vector3.back, Vector3.one * 10f);
                Assert.That(stage.TryGetBrushAdjustmentCursor(out Vector3 center, out Vector3 normal, out _), Is.True);
                Assert.That(center.magnitude, Is.LessThan(1e-5f));
                Assert.That(Mathf.Abs(Vector3.Dot(normal, Vector3.up)), Is.GreaterThan(0.999f));
                stage.CaptureBrushAdjustmentCursor(new Ray(new Vector3(4f, 0f, -3f), Vector3.forward), Vector3.back, Vector3.zero);
                Assert.That(stage.TryGetBrushAdjustmentCursor(out center, out normal, out _), Is.True);
                Assert.That(center, Is.EqualTo(new Vector3(4f, 0f, 0f)));
                Assert.That(normal, Is.EqualTo(Vector3.back));
            }
            finally { DestroyEditingStage(stage); }
        }

        [TestCase(0.01f)]
        [TestCase(0.002f)]
        public void SmallScalpTriangleNormalsDoNotFallBackToCameraDirection(float size)
        {
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right * size, Vector3.up * size };
            sourceMesh.normals = System.Array.Empty<Vector3>();
            HairMeshRaycaster raycaster = new HairMeshRaycaster(sourceMesh);
            Vector3 center = new Vector3(size * 0.25f, size * 0.25f, 0f);
            foreach (Vector3 direction in new[] { new Vector3(0.6f, 0f, -1f).normalized, new Vector3(-0.3f, 0.5f, -1f).normalized })
            {
                Assert.That(raycaster.Raycast(new Ray(center - direction, direction), out HairMeshRaycastHit hit), Is.True);
                Assert.That(Vector3.Dot(hit.Normal, Vector3.forward), Is.GreaterThan(0.99999f));
                Assert.That(raycaster.TryGetSurfaceNormal(hit.TriangleIndex, hit.Barycentric, out Vector3 normal), Is.True);
                Assert.That(Vector3.Dot(normal, Vector3.forward), Is.GreaterThan(0.99999f));
                Assert.That(normal.magnitude, Is.EqualTo(1f).Within(1e-6f));
            }
        }

        [Test]
        public void GrowthBrushAndResizeUseInterpolatedPosedSurfaceNormal()
        {
            const float size = 0.004f;
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right * size, Vector3.up * size };
            Vector3[] normals = { Vector3.forward, new Vector3(0.4f, 0f, 1f).normalized, new Vector3(0f, 0.3f, 1f).normalized };
            sourceMesh.normals = normals;
            HairCardStage stage = CreateEditingStage();
            GameObject space = new GameObject("Brush normal test space");
            try
            {
                space.transform.SetPositionAndRotation(new Vector3(2f, 1f, -0.5f), Quaternion.Euler(25f, 40f, 10f));
                space.transform.localScale = new Vector3(2f, 0.7f, 1.2f);
                SetStageField(stage, "sourceSpaceObject", space);
                SetStageField(stage, "sceneTool", HairSceneTool.PaintGrowth);
                SetStageField(stage, "surfaceRaycaster", new HairMeshRaycaster(sourceMesh));
                Vector3 localCenter = new Vector3(size * 0.25f, size * 0.25f, 0f);
                Vector3 expected = space.transform.localToWorldMatrix.inverse.transpose.MultiplyVector(
                    normals[0] * 0.5f + normals[1] * 0.25f + normals[2] * 0.25f).normalized;
                Vector3 direction = space.transform.TransformVector(new Vector3(0.6f, 0f, -1f)).normalized;
                Ray ray = new Ray(space.transform.TransformPoint(localCenter) - direction, direction);
                // Normal hover/paint and modifier-resize must use the same hit orientation.
                object[] arguments = { ray, null };
                MethodInfo raycast = typeof(HairCardStage).GetMethod("TryRaycastSourceSurface", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That((bool)raycast.Invoke(stage, arguments), Is.True);
                Vector3 hoverNormal = (Vector3)arguments[1].GetType().GetField("WorldNormal", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(arguments[1]);
                Assert.That(Vector3.Dot(hoverNormal, expected), Is.GreaterThan(0.99999f));
                stage.CaptureBrushAdjustmentCursor(ray, -direction, Vector3.zero);
                Assert.That(stage.TryGetBrushAdjustmentCursor(out Vector3 center, out Vector3 resizeNormal, out _), Is.True);
                Assert.That(Vector3.Distance(center, space.transform.TransformPoint(localCenter)), Is.LessThan(1e-5f));
                Assert.That(Vector3.Dot(resizeNormal, expected), Is.GreaterThan(0.99999f));
                Assert.That(Vector3.Dot(resizeNormal, -direction), Is.LessThan(0.99f), "Surface normal must not be replaced by the view normal.");
            }
            finally { SetStageField(stage, "sourceSpaceObject", null); Object.DestroyImmediate(space); DestroyEditingStage(stage); }
        }

        [UnityTest]
        public IEnumerator BrushAdjustmentRendersLiveOuterAndFalloffCircles()
        {
            HairCardStage stage = CreateEditingStage();
            HairGuideDepthTestWindow window = ScriptableObject.CreateInstance<HairGuideDepthTestWindow>();
            GameObject cameraObject = new GameObject("Brush adjustment render camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.orthographic = true; camera.orthographicSize = 1f; camera.aspect = 1f;
            camera.transform.position = Vector3.back * 3f;
            RenderTexture target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
            target.Create(); camera.targetTexture = target;
            Texture2D capture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            try
            {
                SetStageField(stage, "sceneTool", HairSceneTool.PaintGrowth);
                stage.CaptureBrushAdjustmentCursor(new Ray(camera.transform.position, Vector3.forward), Vector3.back, Vector3.zero);
                window.Show();
                float firstInnerRadius = 0f, firstOuterRadius = 0f;
                for (int pass = 0; pass < 2; pass++)
                {
                    stage.BrushRadius = pass == 0 ? 0.25f : 0.5f;
                    stage.BrushHardness = pass == 0 ? 0.5f : 0.75f;
                    bool rendered = false;
                    Color[] pixels = null;
                    window.draw = () =>
                    {
                        if (rendered) return;
                        RenderTexture previous = RenderTexture.active;
                        Camera previousCamera = Camera.current;
                        UnityEngine.Rendering.CompareFunction previousDepth = Handles.zTest;
                        try
                        {
                            Graphics.SetRenderTarget(target); GL.Clear(true, true, Color.clear);
                            Handles.SetCamera(camera); Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                            InvokeStageMethod(stage, "DrawBrushAdjustmentCursor");
                            capture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); capture.Apply();
                            pixels = capture.GetPixels(); rendered = true;
                        }
                        finally
                        {
                            Handles.zTest = previousDepth;
                            if (previousCamera != null) Handles.SetCamera(previousCamera);
                            RenderTexture.active = previous;
                        }
                    };
                    for (int frame = 0; !rendered && frame < 20; frame++) { window.Repaint(); yield return null; }
                    Assert.That(rendered, Is.True);
                    float inner = 0f, outer = 0f; int innerCount = 0, outerCount = 0;
                    for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        Color pixel = pixels[y * 128 + x];
                        float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64f, 64f));
                        if (pixel.r > 0.45f && pixel.g > 0.45f && pixel.b > 0.45f) { inner += distance; innerCount++; }
                        else if (pixel.r < 0.4f && pixel.g > 0.5f && pixel.b > 0.5f) { outer += distance; outerCount++; }
                    }
                    Assert.That(innerCount, Is.GreaterThan(8), "The falloff must be drawn as a visible inner circle.");
                    Assert.That(outerCount, Is.GreaterThan(8), "The actual brush radius must be drawn as an outer circle.");
                    inner /= innerCount; outer /= outerCount;
                    if (pass == 0) { firstInnerRadius = inner; firstOuterRadius = outer; }
                    else
                    {
                        Assert.That(inner, Is.GreaterThan(firstInnerRadius * 2.5f));
                        Assert.That(outer, Is.GreaterThan(firstOuterRadius * 1.8f));
                    }
                }
            }
            finally
            {
                window.draw = null; window.Close(); camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(capture);
                target.Release(); Object.DestroyImmediate(target); DestroyEditingStage(stage);
            }
        }

        [Test]
        public void SlicePlaneContainsBothPerspectiveCameraRays()
        {
            Ray startRay = new Ray(Vector3.zero, new Vector3(-0.25f, -0.2f, 1f));
            Ray endRay = new Ray(Vector3.zero, new Vector3(0.35f, 0.25f, 1f));

            bool created = HairSliceUtility.TryCreateCameraPlane(startRay, endRay, out Plane plane);

            Assert.That(created, Is.True);
            Assert.That(Mathf.Abs(plane.GetDistanceToPoint(startRay.GetPoint(3f))), Is.LessThan(0.00001f));
            Assert.That(Mathf.Abs(plane.GetDistanceToPoint(endRay.GetPoint(4f))), Is.LessThan(0.00001f));
        }

        [Test]
        public void SlicePlaneSupportsOrthographicCameraRays()
        {
            Ray startRay = new Ray(new Vector3(-1f, 0.5f, 0f), Vector3.forward);
            Ray endRay = new Ray(new Vector3(1f, 0.5f, 0f), Vector3.forward);

            bool created = HairSliceUtility.TryCreateCameraPlane(startRay, endRay, out Plane plane);

            Assert.That(created, Is.True);
            Assert.That(Mathf.Abs(plane.GetDistanceToPoint(startRay.GetPoint(5f))), Is.LessThan(0.00001f));
            Assert.That(Mathf.Abs(plane.GetDistanceToPoint(endRay.GetPoint(8f))), Is.LessThan(0.00001f));
        }

        [Test]
        public void SliceChoosesFirstCrossingFromRootAndCanMirrorAcrossLocalX()
        {
            Vector3[] loopingGuide =
            {
                new Vector3(-1f, 0f, 0f),
                new Vector3(1f, 1f, 0f),
                new Vector3(-1f, 2f, 0f)
            };
            bool found = HairSliceUtility.TryFindRootFirstIntersection(loopingGuide,
                new Plane(Vector3.right, Vector3.zero), false, out HairSliceIntersection first);

            Assert.That(found, Is.True);
            Assert.That(first.SegmentEndIndex, Is.EqualTo(1));
            Assert.That(first.SegmentT, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(HairSliceUtility.TryFindRootFirstIntersection(loopingGuide,
                new Plane(Vector3.right, Vector3.zero), false, point => point.y > 1f,
                out HairSliceIntersection finiteCrossing), Is.True);
            Assert.That(finiteCrossing.SegmentEndIndex, Is.EqualTo(2));
            Assert.That(finiteCrossing.SegmentT, Is.EqualTo(0.5f).Within(0.00001f));

            Vector3[] rightSideGuide =
            {
                new Vector3(0.2f, 0f, 0f),
                new Vector3(0.8f, 1f, 0f),
                new Vector3(0.9f, 2f, 0f)
            };
            Plane leftSlice = new Plane(Vector3.right, new Vector3(-0.5f, 0f, 0f));
            Assert.That(HairSliceUtility.TryFindRootFirstIntersection(
                rightSideGuide, leftSlice, false, out _), Is.False);
            Assert.That(HairSliceUtility.TryFindRootFirstIntersection(
                rightSideGuide, leftSlice, true, out HairSliceIntersection mirrored), Is.True);
            Assert.That(mirrored.SegmentEndIndex, Is.EqualTo(1));
            Assert.That(mirrored.SegmentT, Is.EqualTo(0.5f).Within(0.00001f));
            Assert.That(mirrored.PlanePoint.x, Is.EqualTo(-0.5f).Within(0.00001f));
        }

        [Test]
        public void SliceIntersectionMustLandOnFiniteDragGesture()
        {
            Vector2 start = new Vector2(10f, 20f);
            Vector2 end = new Vector2(110f, 20f);

            Assert.That(HairSliceUtility.IsOnFiniteGesture(start, end, new Vector2(50f, 29f)), Is.True);
            Assert.That(HairSliceUtility.IsOnFiniteGesture(start, end, new Vector2(50f, 31f)), Is.False);
            Assert.That(HairSliceUtility.IsOnFiniteGesture(start, end, new Vector2(120f, 20f)), Is.False);
            Assert.That(HairSliceUtility.IsOnFiniteGesture(start, start + Vector2.right,
                new Vector2(10.5f, 20f)), Is.False);
        }

        [Test]
        public void SliceCutInterpolatesExactTipAndKeepsEverySculptLayerAligned()
        {
            HairGroup group = groom.Groups[0];
            HairGuide guide = group.guides[0];
            guide.points[1] = new HairGuidePoint
            {
                position = new Vector3(0f, 1f, 0f), width = 0.3f, roll = 10f,
                stiffness = 0.2f, freeze = 0.4f, profileScale = 0.8f
            };
            guide.points[2] = new HairGuidePoint
            {
                position = new Vector3(2f, 1f, 0f), width = 0.1f, roll = 30f,
                stiffness = 0.6f, freeze = 0.8f, profileScale = 1.2f
            };
            guide.points.Add(new HairGuidePoint { position = new Vector3(4f, 1f, 0f), width = 0f });
            HairGuideDelta delta = new HairGuideDelta
            {
                guideId = guide.Id,
                positionOffsets = new[] { Vector3.zero, Vector3.right, Vector3.right * 3f, Vector3.right * 6f },
                widthOffsets = new[] { 0f, 0.2f, 0.6f, 1f },
                rollOffsets = new[] { 0f, 2f, 6f, 10f }
            };
            HairSculptLayer layer = new HairSculptLayer();
            layer.deltas.Add(delta);
            group.sculptLayers.Add(layer);

            bool changed = HairSliceUtility.TruncateGuide(group, guide, 0.5f);

            Assert.That(changed, Is.True);
            Assert.That(guide.points, Has.Count.EqualTo(3));
            HairGuidePoint tip = guide.points[2];
            Assert.That(tip.position, Is.EqualTo(new Vector3(1f, 1f, 0f))
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(tip.width, Is.EqualTo(0.2f).Within(0.00001f));
            Assert.That(tip.roll, Is.EqualTo(20f).Within(0.00001f));
            Assert.That(tip.stiffness, Is.EqualTo(0.4f).Within(0.00001f));
            Assert.That(tip.freeze, Is.EqualTo(0.6f).Within(0.00001f));
            Assert.That(tip.profileScale, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(delta.positionOffsets, Has.Length.EqualTo(3));
            Assert.That(delta.widthOffsets, Has.Length.EqualTo(3));
            Assert.That(delta.rollOffsets, Has.Length.EqualTo(3));
            Assert.That(delta.positionOffsets[2], Is.EqualTo(Vector3.right * 2f)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(delta.widthOffsets[2], Is.EqualTo(0.4f).Within(0.00001f));
            Assert.That(delta.rollOffsets[2], Is.EqualTo(4f).Within(0.00001f));
        }

        [Test]
        public void PositionalGroomConstraintPreservesRootAndEverySegmentLength()
        {
            Vector3[] original =
            {
                new Vector3(0.1f, 0.2f, -0.1f),
                new Vector3(0.1f, 0.3f, -0.1f),
                new Vector3(0.12f, 0.41f, -0.08f),
                new Vector3(0.16f, 0.5f, -0.02f)
            };
            Vector3[] target =
            {
                original[0] + Vector3.one * 99f,
                new Vector3(4f, -2f, 8f),
                new Vector3(-5f, 3f, 2f),
                new Vector3(7f, 9f, -3f)
            };

            HairGuideShapeUtility.PreserveSegmentLengths(original, target);

            Assert.That(target[0], Is.EqualTo(original[0])
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            for (int pointIndex = 1; pointIndex < original.Length; pointIndex++)
            {
                float expected = Vector3.Distance(original[pointIndex - 1], original[pointIndex]);
                float actual = Vector3.Distance(target[pointIndex - 1], target[pointIndex]);
                Assert.That(actual, Is.EqualTo(expected).Within(0.000001f));
            }
        }

        [Test]
        public void GroomStrokeClampRejectsProjectionDepthSpikes()
        {
            Vector3 delta = HairBrushInteractionUtility.ClampStrokeDelta(
                new Vector3(20f, -8f, 12f), 0.1f);

            Assert.That(delta.magnitude, Is.EqualTo(0.05f).Within(0.000001f));
            Assert.That(HairBrushInteractionUtility.ClampStrokeDelta(
                    new Vector3(0.01f, 0f, 0f), 0.1f).x,
                Is.EqualTo(0.01f).Within(0.000001f));
        }

        [Test]
        public void StretchRepairRestoresAuthoredLengthsAndKeepsCurrentDirections()
        {
            Vector3[] authored =
            {
                Vector3.zero,
                Vector3.up * 0.1f,
                Vector3.up * 0.3f
            };
            Vector3[] stretched =
            {
                new Vector3(2f, 1f, -1f),
                new Vector3(3f, 1f, -1f),
                new Vector3(3f, 1f, 4f)
            };
            Vector3[] repaired = new Vector3[authored.Length];

            HairGuideShapeUtility.RestoreReferenceSegmentLengths(authored, stretched, repaired);

            Assert.That(repaired[0], Is.EqualTo(stretched[0])
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(repaired[1] - repaired[0], Is.EqualTo(Vector3.right * 0.1f)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(repaired[2] - repaired[1], Is.EqualTo(Vector3.forward * 0.2f)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void GravitySeparationIsStableAndFansGuidesApart()
        {
            Vector3 first = HairGuideShapeUtility.StableGravityDirection(
                Vector3.down, Vector3.right, 101, 0.6f);
            Vector3 repeat = HairGuideShapeUtility.StableGravityDirection(
                Vector3.down, Vector3.right, 101, 0.6f);
            Vector3 oppositeScalp = HairGuideShapeUtility.StableGravityDirection(
                Vector3.down, Vector3.left, 202, 0.6f);

            Assert.That(first, Is.EqualTo(repeat)
                .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(first.y, Is.LessThan(0f));
            Assert.That(first.x, Is.GreaterThan(0f));
            Assert.That(oppositeScalp.x, Is.LessThan(0f));
            Assert.That(Vector3.Angle(first, oppositeScalp), Is.GreaterThan(10f));
        }

        [Test]
        public void VerticalSlicePaintGenerateAcceptStyleChildrenMeshAndValidate()
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear();
            group.children.childrenPerGuide = 3;
            group.children.includeGuideCard = true;
            HairGrowthMap growth = group.FindMap(HairMapKind.GrowthArea);
            for (int vertex = 0; vertex < growth.values.Length; vertex++) growth.values[vertex] = 1f;
            HairGuideGenerationSettings settings = new HairGuideGenerationSettings
            {
                guideCount = 4,
                pointsPerGuide = 6,
                defaultLength = 0.2f,
                minimumRootSpacing = 0f,
                seed = 9081
            };

            HairGuideGenerationResult preview = HairGuideGenerator.Generate(groom, group, settings);
            Assert.That(preview.guides, Has.Count.EqualTo(4));

            int accepted = HairGroomCommands.AddGeneratedGuides(groom, group, preview.guides);
            Assert.That(accepted, Is.EqualTo(4));
            Assert.That(group.guides, Has.Count.EqualTo(4));

            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group, "Shape");
            HairGuide styledGuide = group.guides[0];
            HairGuideDelta delta = new HairGuideDelta
            {
                guideId = styledGuide.Id,
                positionOffsets = new Vector3[styledGuide.points.Count],
                widthOffsets = new float[styledGuide.points.Count],
                rollOffsets = new float[styledGuide.points.Count]
            };
            delta.positionOffsets[delta.positionOffsets.Length - 1] = Vector3.right * 0.05f;
            layer.deltas.Add(delta);

            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            Assert.That(evaluation.evaluatedGuides, Has.Count.EqualTo(4));
            Assert.That(evaluation.guideCurveCount, Is.EqualTo(4));
            Assert.That(evaluation.childCurveCount, Is.EqualTo(12));
            Assert.That(evaluation.CardCount, Is.EqualTo(16));
            Assert.That(evaluation.evaluatedGuides[0].points[evaluation.evaluatedGuides[0].points.Count - 1]
                .position.x, Is.GreaterThan(styledGuide.points[styledGuide.points.Count - 1].position.x));

            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Vertical Slice");
            Assert.That(build.cardCount, Is.EqualTo(16));
            Assert.That(build.vertexCount, Is.GreaterThan(0));
            Assert.That(build.triangleCount, Is.GreaterThan(0));
            Assert.That(build.degenerateTriangleCount, Is.Zero);

            HairValidationReport report = HairValidator.Validate(groom, evaluation, build,
                new HairValidationOptions { requireAtlas = false });
            Assert.That(report.ErrorCount, Is.Zero);
            Assert.That(report.cardCount, Is.EqualTo(16));

            HairBakeOutcome dryRun = HairBakePipeline.DryRun(groom);
            Assert.That(dryRun.succeeded, Is.True);
            Assert.That(dryRun.cardCount, Is.EqualTo(16));
            Assert.That(dryRun.triangleCount, Is.GreaterThan(0));
        }

        [Test]
        public void LockedGroupRejectsGeneratedGuideAcceptance()
        {
            HairGroup group = groom.Groups[0];
            group.locked = true;
            int originalCount = group.guides.Count;
            int accepted = HairGroomCommands.AddGeneratedGuides(groom, group,
                new[] { CreateGuide("Blocked", 42, Vector3.zero) });

            Assert.That(accepted, Is.Zero);
            Assert.That(group.guides, Has.Count.EqualTo(originalCount));
        }

        [Test]
        public void ChildDomainModifierChangesChildrenWithoutChangingGuideCard()
        {
            HairGroup group = groom.Groups[0];
            group.children.childrenPerGuide = 1;
            HairModifierSettings modifier = new HairModifierSettings
            {
                name = "Wide Children",
                type = HairModifierType.Width,
                domain = HairModifierDomain.Children,
                amount = 2f,
                weight = 1f
            };
            modifier.EnsureIntegrity();
            group.modifiers.Add(modifier);

            HairEvaluationResult result = HairGroomEvaluator.Evaluate(groom);

            Assert.That(result.CardCount, Is.EqualTo(2));
            HairEvaluatedCurve guide = result.curves.Find(curve => !curve.isChild);
            HairEvaluatedCurve child = result.curves.Find(curve => curve.isChild);
            Assert.That(guide, Is.Not.Null);
            Assert.That(child, Is.Not.Null);
            Assert.That(child.points[0].width, Is.GreaterThan(guide.points[0].width));
        }

        [Test]
        public void SkinningTransfersClosestSourceWeightsAndBindPoses()
        {
            sourceMesh.bindposes = new[] { Matrix4x4.identity };
            sourceMesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }
            };
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation);

            bool transferred = HairSkinningUtility.TransferClosestVertexWeights(build.mesh, sourceMesh,
                out string warning);

            Assert.That(transferred, Is.True, warning);
            Assert.That(build.mesh.bindposes, Has.Length.EqualTo(1));
            Assert.That(build.mesh.boneWeights, Has.Length.EqualTo(build.mesh.vertexCount));
        }

        [Test]
        public void RuntimeApiGeneratesDisposableHairWithoutEditorServices()
        {
            using HairGroomRuntimeAPI.GeneratedHair generated = HairGroomRuntimeAPI.Generate(groom);

            Assert.That(generated.Mesh, Is.Not.Null);
            Assert.That(generated.Evaluation.CardCount, Is.EqualTo(1));
            Assert.That(generated.Build.triangleCount, Is.GreaterThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedCardsRefreshOnceAfterEachBrushStrokeOrGravityHold(bool useGravity)
        {
            HairCardStage stage = ScriptableObject.CreateInstance<HairCardStage>();
            GameObject preview = new GameObject("Groom Preview Test") { hideFlags = HideFlags.HideAndDontSave };
            MeshFilter filter = preview.AddComponent<MeshFilter>();
            MeshRenderer renderer = preview.AddComponent<MeshRenderer>();
            try
            {
                SetStageField(stage, "groom", groom);
                SetStageField(stage, "hairFilter", filter);
                SetStageField(stage, "hairRenderer", renderer);
                groom.Groups[0].children.childrenPerGuide = 3;

                // Guide evaluation alone should not opt a new groom into the card display.
                stage.WorkflowStep = HairWorkflowStep.Guides;
                stage.RebuildNow();
                stage.WorkflowStep = HairWorkflowStep.Groom;
                Assert.That(stage.PreviewMode, Is.EqualTo(HairPreviewMode.GuidesAndChildren));

                stage.WorkflowStep = HairWorkflowStep.Cards;
                stage.RebuildNow();
                stage.WorkflowStep = HairWorkflowStep.Groom;
                Assert.That(stage.PreviewMode, Is.EqualTo(HairPreviewMode.Cards));
                stage.RebuildNow();
                Assert.That(renderer.enabled, Is.True);

                // Exercise successive strokes so the preview cannot get stuck after the first one.
                for (int stroke = 0; stroke < 2; stroke++)
                {
                    Mesh originalMesh = filter.sharedMesh;
                    Vector3[] originalVertices = originalMesh.vertices;
                    if (useGravity) stage.SetGravityHeld(true);
                    else InvokeStageMethod(stage, "BeginStroke", "Test Groom Stroke");
                    Assert.That(renderer.enabled, Is.False);

                    groom.Groups[0].guides[0].points[^1].position += Vector3.right * 0.1f;
                    stage.QueueRebuild(true);
                    stage.RebuildNow();
                    InvokeStageMethod(stage, "EditorUpdate");
                    Assert.That(filter.sharedMesh, Is.SameAs(originalMesh),
                        "Queued and explicit mesh builds must both wait for stroke release.");
                    Assert.That(filter.sharedMesh.vertices, Is.EqualTo(originalVertices));
                    Assert.That(renderer.enabled, Is.False);

                    if (useGravity) stage.SetGravityHeld(false);
                    else InvokeStageMethod(stage, "EndStroke");
                    Assert.That(filter.sharedMesh, Is.SameAs(originalMesh));
                    InvokeStageMethod(stage, "EditorUpdate");
                    Assert.That(renderer.enabled, Is.True);
                    Assert.That(filter.sharedMesh, Is.SameAs(originalMesh), "Stroke updates reuse the preview's dynamic mesh.");
                    Assert.That(filter.sharedMesh.vertices, Is.Not.EqualTo(originalVertices));
                    Assert.That(stage.MeshBuild.cardCount, Is.EqualTo(4));

                    Mesh refreshedMesh = filter.sharedMesh;
                    InvokeStageMethod(stage, "EditorUpdate");
                    Assert.That(filter.sharedMesh, Is.SameAs(refreshedMesh),
                        "The completed stroke should trigger one refresh, not continuous idle builds.");
                }

                stage.PreviewMode = HairPreviewMode.Guides;
                stage.RebuildNow();
                InvokeStageMethod(stage, "BeginStroke", "Guide-only Stroke");
                InvokeStageMethod(stage, "EndStroke");
                InvokeStageMethod(stage, "EditorUpdate");
                Assert.That(stage.PreviewMode, Is.EqualTo(HairPreviewMode.Guides));
                Assert.That(renderer.enabled, Is.False, "Respect the user's explicit guide-only preview.");
            }
            finally
            {
                stage.SetGravityHeld(false);
                InvokeStageMethod(stage, "EndStroke");
                InvokeStageMethod(stage, "RestoreUnityToolState");
                InvokeStageMethod(stage, "DisposeBuild");
                Undo.ClearUndo(groom);
                Object.DestroyImmediate(stage);
                Object.DestroyImmediate(preview);
            }
        }

        [TestCase(0.999f)]
        [TestCase(1f)]
        public void FreezeBrushCanEraseFullyFrozenPoints(float frozen)
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                HairGuide guide = groom.Groups[0].guides[0];
                guide.points[1].freeze = frozen;
                SetStageField(stage, "sceneTool", HairSceneTool.Freeze);
                stage.PaintErase = true;
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.zero);
                Assert.That(guide.points[1].freeze, Is.LessThan(0.01f));
                Assert.That(groom.Groups[0].sculptLayers, Is.Empty, "Mask edits do not create geometry layers.");
            }
            finally { DestroyEditingStage(stage); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void LengthBrushIntentionallyChangesLengthAndKeepsRootAndFrozenPoints(bool shorten, bool freezeMiddle)
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                HairGuide guide = groom.Groups[0].guides[0];
                Vector3 root = stage.DisplayedControlPoint(guide, 0);
                Vector3 middle = stage.DisplayedControlPoint(guide, 1);
                float original = stage.Evaluation.evaluatedGuides[0].Length;
                if (freezeMiddle) guide.points[1].freeze = 1f;
                Vector3 baseTip = guide.points[^1].position;
                SetStageField(stage, "sceneTool", HairSceneTool.Length);
                stage.PaintErase = shorten;
                InvokeStageMethod(stage, "BeginStroke", "Length Test");
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.zero);
                InvokeStageMethod(stage, "EndStroke");
                stage.RebuildNow();
                float changed = stage.Evaluation.evaluatedGuides[0].Length;
                Assert.That(shorten ? changed < original - 0.0001f : changed > original + 0.0001f, Is.True);
                Assert.That(stage.DisplayedControlPoint(guide, 0), Is.EqualTo(root).Using(Vector3ComparerWithEqualsOperator.Instance));
                if (freezeMiddle)
                    Assert.That(stage.DisplayedControlPoint(guide, 1), Is.EqualTo(middle).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(guide.points[^1].position, Is.EqualTo(baseTip), "Length changes belong to the sculpt layer.");
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void PinnedLengthSolverPreservesEverySegmentAndFrozenAnchor()
        {
            Vector3[] original = { Vector3.zero, new Vector3(0.2f, 0.5f), new Vector3(0f, 1f), new Vector3(0f, 1.5f) };
            Vector3[] target = { Vector3.one, new Vector3(0.8f, 0.3f), new Vector3(1f, 2f), new Vector3(1f, 2.5f) };
            HairGuidePoint[] controls = { new HairGuidePoint(), new HairGuidePoint(), new HairGuidePoint { freeze = 1f }, new HairGuidePoint() };
            HairGuideShapeUtility.PreserveSegmentLengths(original, target, controls);
            Assert.That(target[0], Is.EqualTo(original[0]));
            Assert.That(target[2], Is.EqualTo(original[2]));
            for (int i = 1; i < original.Length; i++)
                Assert.That(Vector3.Distance(target[i - 1], target[i]),
                    Is.EqualTo(Vector3.Distance(original[i - 1], original[i])).Within(0.00001f));
        }

        [Test]
        public void PointEditingUsesDisplayedLayerSpaceAndHonorsLocksAndAnchors()
        {
            HairGuide guide = groom.Groups[0].guides[0];
            HairSculptLayer existing = HairGroomCommands.AddSculptLayer(groom, groom.Groups[0], "Existing style");
            existing.deltas.Add(new HairGuideDelta { guideId = guide.Id,
                positionOffsets = new[] { Vector3.zero, Vector3.right * 0.1f, Vector3.right * 0.2f } });
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SetActiveLayer(existing.Id);
                Vector3 baseTip = guide.points[^1].position;
                Vector3 displayed = stage.DisplayedControlPoint(guide, 2);
                float length = stage.Evaluation.evaluatedGuides[0].Length;
                Assert.That(displayed.x, Is.EqualTo(baseTip.x + 0.2f).Within(0.00001f));
                groom.Groups[0].locked = true;
                Assert.That(stage.MoveGuidePoint(guide.Id, 2, displayed + Vector3.right), Is.False);
                groom.Groups[0].locked = false;
                guide.points[2].freeze = 1f;
                Assert.That(stage.MoveGuidePoint(guide.Id, 2, displayed + Vector3.right), Is.False);
                guide.points[2].freeze = 0f;
                Assert.That(stage.MoveGuidePoint(guide.Id, 0, Vector3.one), Is.False);
                existing.locked = true;
                Vector3 protectedDelta = existing.deltas[0].positionOffsets[2];
                Assert.That(stage.MoveGuidePoint(guide.Id, 2, displayed + Vector3.forward * 0.1f), Is.True);
                InvokeStageMethod(stage, "EndStroke");
                stage.RebuildNow();
                Assert.That(existing.deltas[0].positionOffsets[2], Is.EqualTo(protectedDelta));
                Assert.That(guide.points[2].position, Is.EqualTo(baseTip));
                Assert.That(stage.DisplayedControlPoint(guide, 2), Is.Not.EqualTo(displayed));
                Assert.That(stage.Evaluation.evaluatedGuides[0].Length, Is.EqualTo(length).Within(0.00001f));
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                stage.RebuildNow();
                Assert.That(stage.DisplayedControlPoint(groom.Groups[0].guides[0], 2),
                    Is.EqualTo(displayed).Using(Vector3ComparerWithEqualsOperator.Instance));
            }
            finally { DestroyEditingStage(stage); }
        }

        [TestCase(false, 4)]
        [TestCase(true, 9)]
        public void SamplingSourceControlsActualGeneratedMesh(bool useProfile, int expected)
        {
            profile.Configure(HairCardShape.Ribbon, 0.04f, 0f, 9, generateBackfaces: true);
            groom.Lods[0].useProfileSamples = useProfile;
            HairEvaluationResult result = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult mesh = HairCardMeshGenerator.Build(result);
            Assert.That(result.curves[0].samplesPerCardOverride, Is.EqualTo(expected));
            Assert.That(mesh.vertexCount, Is.EqualTo(expected * 2));
        }

        [TestCase(0.5f, true)]
        [TestCase(1f, true)]
        [TestCase(0.5f, false)]
        public void PointEditsStayAtTheirDisplayedPositionThroughOverrideLayers(float opacity, bool upperAffectsGuide)
        {
            HairGroup group = groom.Groups[0];
            HairGuide guide = group.guides[0];
            HairSculptLayer lower = HairGroomCommands.AddSculptLayer(groom, group, "Editable");
            lower.opacity = 0.5f;
            HairSculptLayer upper = HairGroomCommands.AddSculptLayer(groom, group, "Override");
            upper.blendMode = HairSculptBlendMode.Override;
            upper.opacity = opacity;
            upper.deltas.Add(new HairGuideDelta { guideId = upperAffectsGuide ? guide.Id : "different-guide",
                positionOffsets = new Vector3[3] });
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SetActiveLayer(lower.Id);
                Vector3 target = stage.DisplayedControlPoint(guide, 2) + Vector3.forward * 0.15f;
                Assert.That(stage.MoveGuidePoint(guide.Id, 2, target), Is.True);
                Vector3 preview = stage.DisplayedControlPoint(guide, 2);
                InvokeStageMethod(stage, "EndStroke");
                stage.RebuildNow();
                Assert.That(stage.DisplayedControlPoint(guide, 2), Is.EqualTo(preview).Using(Vector3ComparerWithEqualsOperator.Instance));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void SliceWillNotDeleteAFrozenTipAnchor()
        {
            HairGroup group = groom.Groups[0];
            HairGuide guide = group.guides[0];
            guide.points[^1].freeze = 0.999f;
            Assert.That(HairSliceUtility.TruncateGuide(group, guide, 0.5f), Is.False);
            Assert.That(guide.points.Count, Is.EqualTo(3));
            guide.points[^1].freeze = 0f;
            Assert.That(HairSliceUtility.TruncateGuide(group, guide, 0.5f), Is.True);
        }

        [Test]
        public void ReleaseValidationIgnoresPreviewFiltersAndBlocksInvalidExportedLod()
        {
            groom.Groups[0].children.childrenPerGuide = 3;
            groom.Groups[0].visible = false;
            groom.Lods.Add(new HairLodSettings { level = 1, cardFraction = 0f });
            HairValidationReport release = HairBakePipeline.ValidateRelease(groom);
            Assert.That(release.isReleaseReport, Is.True);
            Assert.That(release.cardCount, Is.EqualTo(4));
            Assert.That(release.lods.Count, Is.EqualTo(2));
            Assert.That(release.issues.Exists(issue => issue.code == HairValidationCode.EmptyOutput && issue.lodLevel == 1), Is.True);
            HairBakeOutcome bake = HairBakePipeline.Bake(groom);
            Assert.That(bake.succeeded, Is.False);
            Assert.That(bake.assets, Is.Empty);
            groom.BakeSettings.createMesh = false;
            release = HairBakePipeline.ValidateRelease(groom);
            Assert.That(release.lods.Count, Is.EqualTo(1), "Only exported LODs should gate a bake.");
            Assert.That(release.CanBake, Is.True);
        }

        [Test]
        public void ReleaseValidationReportsMalformedDataBeforeEvaluationAndNavigatesToGuide()
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                HairGuide guide = groom.Groups[0].guides[0];
                guide.points[1] = null;
                HairValidationReport release = HairBakePipeline.ValidateRelease(groom);
                HairValidationIssue issue = release.issues.Find(item => item.code == HairValidationCode.InvalidPoint);
                Assert.That(issue, Is.Not.Null);
                Assert.That(release.CanBake, Is.False);
                Assert.That(release.lods, Is.Empty);
                stage.NavigateToIssue(issue, true);
                Assert.That(stage.ActiveGuideId, Is.EqualTo(guide.Id));
                Assert.That(stage.WorkflowStep, Is.EqualTo(HairWorkflowStep.Guides));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void SelectedGuideScopeEditsOnlySelectionAndIsolationNeverFiltersRelease()
        {
            HairGroup group = groom.Groups[0];
            group.guides.Add(CreateGuide("Guide B", 22, new Vector3(0.1f, 0f, 0f)));
            groom.EnsureIntegrity();
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SetGuideSelection(new[] { group.guides[1].Id });
                stage.BrushScope = HairBrushScope.SelectedGuidesOnly;
                InvokeStageMethod(stage, "BeginStroke", "Selected scope test");
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.forward, Vector3.right * 0.05f);
                InvokeStageMethod(stage, "EndStroke");
                Assert.That(group.sculptLayers.Count, Is.EqualTo(1));
                Assert.That(group.sculptLayers[0].deltas.Count, Is.EqualTo(1));
                Assert.That(group.sculptLayers[0].deltas[0].guideId, Is.EqualTo(group.guides[1].Id));
                stage.IsolateSelectedGuides = true;
                stage.RebuildNow();
                Assert.That(stage.Evaluation.evaluatedGuides.Count, Is.EqualTo(1));
                Assert.That(HairGroomEvaluator.Evaluate(groom).evaluatedGuides.Count, Is.EqualTo(2));
                stage.SetGuideSelection(null);
                stage.RebuildNow();
                Assert.That(stage.Evaluation.evaluatedGuides, Is.Empty);
                stage.IsolateSelectedGuides = false;
                stage.RebuildNow();
                Assert.That(stage.Evaluation.evaluatedGuides.Count, Is.EqualTo(2));
            }
            finally { DestroyEditingStage(stage); }
        }

        [UnityTest]
        public IEnumerator GuideDepthPassOccludesBackHandlesWithoutPaintingColorOrHidingSilhouettes()
        {
            HairGuideDepthTestWindow window = ScriptableObject.CreateInstance<HairGuideDepthTestWindow>();
            GameObject cameraObject = new GameObject("Guide depth test camera");
            GameObject surface = new GameObject("Guide depth test occluder");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(0.2f, 0f, -3f);
            camera.nearClipPlane = 0.1f; camera.farClipPlane = 10f;
            camera.orthographicSize = 1f; camera.aspect = 1f;
            surface.transform.position = new Vector3(0.2f, 0f, 0f);
            Mesh occluder = new Mesh { name = "Depth test head silhouette" };
            occluder.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            occluder.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            Material opaque = new Material(Shader.Find("Standard"));
            renderer.sharedMaterial = opaque;
            RenderTexture target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
            target.Create(); camera.targetTexture = target;
            Texture2D capture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            Texture2D alphaHole = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            alphaHole.SetPixel(0, 0, Color.clear); alphaHole.Apply();
            HairGuideDepthRenderer depth = new HairGuideDepthRenderer();
            try
            {
                Assert.That(depth.DepthMaterial, Is.Not.Null);
                Assert.That(ShaderUtil.ShaderHasError(depth.DepthMaterial.shader), Is.False);
                window.Show();
                foreach (bool orthographic in new[] { true, false })
                foreach (int mode in new[] { 0, 1, 2, 3, 4, 5 }) // X-ray, depth, hidden, cutout hole, solid cutout, transparent.
                {
                    camera.orthographic = orthographic;
                    renderer.enabled = mode != 2;
                    opaque.renderQueue = mode >= 3 ? mode == 5 ? 3000 : 2450 : 2000;
                    opaque.SetTexture("_MainTex", mode == 3 ? alphaHole : Texture2D.whiteTexture);
                    bool rendered = false;
                    Color[] pixels = null;
                    bool untouchedColor = false;
                    window.draw = () =>
                    {
                        if (rendered) return;
                        RenderTexture previous = RenderTexture.active;
                        Camera previousCamera = Camera.current;
                        UnityEngine.Rendering.CompareFunction previousDepth = Handles.zTest;
                        try
                        {
                            Graphics.SetRenderTarget(target);
                            GL.Clear(true, true, Color.clear);
                            Handles.SetCamera(camera);
                            if (mode != 0) depth.Draw(renderer, occluder);
                            capture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); capture.Apply();
                            untouchedColor = capture.GetPixel(64, 64).maxColorComponent == 0f;
                            using (new Handles.DrawingScope(surface.transform.localToWorldMatrix))
                            {
                                Handles.zTest = mode == 0 ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual;
                                Handles.color = Color.red;
                                Handles.DrawAAPolyLine(5f, new Vector3(-0.9f, -0.25f, -0.5f), new Vector3(0.9f, -0.25f, -0.5f));
                                Handles.DotHandleCap(0, new Vector3(0f, -0.1f, -0.5f), Quaternion.identity, 0.025f, EventType.Repaint);
                                Handles.color = Color.green;
                                Handles.DrawAAPolyLine(5f, new Vector3(-0.9f, 0.25f, 0.5f), new Vector3(0.9f, 0.25f, 0.5f));
                                Handles.DotHandleCap(0, new Vector3(0f, 0.1f, 0.5f), Quaternion.identity, 0.025f, EventType.Repaint);
                            }
                            capture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); capture.Apply();
                            pixels = capture.GetPixels();
                            rendered = true;
                        }
                        finally
                        {
                            Handles.zTest = previousDepth;
                            if (previousCamera != null) Handles.SetCamera(previousCamera);
                            RenderTexture.active = previous;
                        }
                    };
                    for (int frame = 0; !rendered && frame < 20; frame++) { window.Repaint(); yield return null; }
                    Assert.That(rendered, Is.True);
                    Assert.That(untouchedColor, Is.True, "The depth pass must not paint over the character or GUI.");
                    int frontCenter = 0, backCenter = 0, backAnywhere = 0;
                    for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        Color pixel = pixels[y * 128 + x];
                        bool green = pixel.g > 0.2f && pixel.r < 0.1f;
                        if (green) backAnywhere++;
                        if (x < 62 || x > 66) continue;
                        if (green) backCenter++;
                        if (pixel.r > 0.2f && pixel.g < 0.1f) frontCenter++;
                    }
                    Assert.That(frontCenter, Is.GreaterThan(0), "Front guides remain visible.");
                    Assert.That(backAnywhere, Is.GreaterThan(0), "Back guides extending beyond the head silhouette remain visible.");
                    Assert.That(backCenter, mode == 1 || mode == 4 ? Is.EqualTo(0) : Is.GreaterThan(0),
                        $"Back-guide occlusion: orthographic={orthographic}, mode={mode}");
                }
            }
            finally
            {
                window.draw = null; window.Close(); depth.Dispose();
                camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(surface);
                Object.DestroyImmediate(occluder); Object.DestroyImmediate(opaque);
                Object.DestroyImmediate(capture); Object.DestroyImmediate(alphaHole); target.Release(); Object.DestroyImmediate(target);
            }
        }

        [UnityTest]
        public IEnumerator StageDottedGuidePreviewsRespectDepthToggle()
        {
            HairCardStage stage = CreateEditingStage();
            HairGuideDepthTestWindow window = ScriptableObject.CreateInstance<HairGuideDepthTestWindow>();
            GameObject cameraObject = new GameObject("Dotted guide regression camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false; camera.transform.position = Vector3.back * 3f;
            camera.orthographicSize = 1f; camera.aspect = 1f;
            GameObject surface = new GameObject("Dotted guide head occluder");
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            Material material = new Material(Shader.Find("Standard"));
            renderer.sharedMaterial = material;
            Mesh head = new Mesh();
            head.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            head.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            RenderTexture target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32);
            target.Create(); camera.targetTexture = target;
            Texture2D capture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            HairGuideDepthRenderer depth = new HairGuideDepthRenderer();
            try
            {
                groom.Groups[0].guides.Clear();
                stage.RebuildNow();
                SetStageField(stage, "showHelpers", false);
                SetStageField(stage, "previewMode", HairPreviewMode.GuidesAndChildren);
                SetStageField(stage, "showChildren", true);
                HairGuideGenerationResult preview = new HairGuideGenerationResult();
                preview.guides.Add(CreateLinearGuide("Front preview", 1, new Vector3(-0.9f, -0.25f, -0.5f), Vector3.right * 1.8f));
                preview.guides.Add(CreateLinearGuide("Back preview", 2, new Vector3(-0.9f, 0.25f, 0.5f), Vector3.right * 1.8f));
                HairEvaluationResult children = new HairEvaluationResult { childCurveCount = 2 };
                foreach (HairGuide guide in preview.guides)
                {
                    HairEvaluatedCurve child = new HairEvaluatedCurve { isChild = true, parentGuideId = guide.Id,
                        groupColor = new Color(0.1f, 1f, 0.8f, 1f) };
                    foreach (HairGuidePoint point in guide.points) child.points.Add(new HairCurvePoint(point.position, point.width, point.roll));
                    children.curves.Add(child);
                }
                window.Show();
                foreach (bool generation in new[] { true, false })
                foreach (bool orthographic in new[] { true, false })
                foreach (bool depthEnabled in new[] { false, true })
                foreach (bool childSplines in new[] { true, false })
                {
                    SetStageField(stage, "generationPreview", generation ? preview : null);
                    SetStageField(stage, "evaluation", generation ? new HairEvaluationResult() : children);
                    stage.DepthTestGuides = depthEnabled;
                    stage.ShowChildSplines = childSplines;
                    camera.orthographic = orthographic;
                    bool rendered = false, stateRestored = false;
                    Color[] pixels = null;
                    window.draw = () =>
                    {
                        if (rendered) return;
                        RenderTexture previous = RenderTexture.active;
                        Camera previousCamera = Camera.current;
                        UnityEngine.Rendering.CompareFunction previousDepth = Handles.zTest;
                        try
                        {
                            Graphics.SetRenderTarget(target); GL.Clear(true, true, Color.clear); Handles.SetCamera(camera);
                            depth.Draw(renderer, head);
                            // Start in X-ray like Scene view does. Exercise the stage's real scope,
                            // not a test-authored LessEqual scope that can conceal a missing call site.
                            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                            InvokeStageMethod(stage, "DrawGuideOverlays");
                            stateRestored = Handles.zTest == UnityEngine.Rendering.CompareFunction.Always;
                            capture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); capture.Apply();
                            pixels = capture.GetPixels(); rendered = true;
                        }
                        finally
                        {
                            Handles.zTest = previousDepth;
                            if (previousCamera != null) Handles.SetCamera(previousCamera);
                            RenderTexture.active = previous;
                        }
                    };
                    for (int frame = 0; !rendered && frame < 20; frame++) { window.Repaint(); yield return null; }
                    Assert.That(rendered, Is.True); Assert.That(stateRestored, Is.True);
                    int front = 0, back = 0, backOutside = 0;
                    int frontY = Mathf.RoundToInt(camera.WorldToViewportPoint(new Vector3(0f, -0.25f, -0.5f)).y * 128);
                    int backY = Mathf.RoundToInt(camera.WorldToViewportPoint(new Vector3(0f, 0.25f, 0.5f)).y * 128);
                    for (int y = 0; y < 128; y++)
                    for (int x = 0; x < 128; x++)
                    {
                        Color pixel = pixels[y * 128 + x];
                        if (pixel.g <= 0.08f || pixel.g <= pixel.r * 2f) continue;
                        bool center = x >= 57 && x <= 71;
                        if (center && Mathf.Abs(y - frontY) < 4) front++;
                        if (Mathf.Abs(y - backY) < 4) { if (center) back++; else backOutside++; }
                    }
                    if (!generation && !childSplines)
                    {
                        Assert.That(front + back + backOutside, Is.Zero, "The child-spline toggle must hide their rendered lines.");
                        continue;
                    }
                    Assert.That(front, Is.GreaterThan(0), "Front dotted guides must stay visible.");
                    Assert.That(backOutside, Is.GreaterThan(0), "Do not hide complete back-side guides beyond the head silhouette.");
                    Assert.That(back, depthEnabled ? Is.EqualTo(0) : Is.GreaterThan(0),
                        $"generation={generation}, orthographic={orthographic}, depth={depthEnabled}");
                }
            }
            finally
            {
                window.draw = null; window.Close(); depth.Dispose(); camera.targetTexture = null;
                Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(surface); Object.DestroyImmediate(material);
                Object.DestroyImmediate(head); Object.DestroyImmediate(capture); target.Release(); Object.DestroyImmediate(target);
                DestroyEditingStage(stage);
            }
        }

        [Test]
        public void PaintedAreaBoundsIncludeAllBoneInfluencesInPosedStageSpace()
        {
            using var counts = new Unity.Collections.NativeArray<byte>(new byte[] { 5, 1, 1 }, Unity.Collections.Allocator.Temp);
            BoneWeight1[] weightData = new BoneWeight1[7];
            for (int i = 0; i < 5; i++) weightData[i] = new BoneWeight1 { boneIndex = i, weight = 0.2f };
            weightData[5] = weightData[6] = new BoneWeight1 { boneIndex = 5, weight = 1f };
            using var weights = new Unity.Collections.NativeArray<BoneWeight1>(weightData, Unity.Collections.Allocator.Temp);
            sourceMesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity,
                Matrix4x4.identity, Matrix4x4.identity, Matrix4x4.identity };
            sourceMesh.SetBoneWeights(counts, weights);
            Vector3?[] bones = { Vector3.zero, Vector3.up, Vector3.right, Vector3.back,
                new Vector3(4f, 1f, 1f), Vector3.one * 100f };
            Mesh posed = Object.Instantiate(sourceMesh);
            try
            {
                Vector3[] vertices = sourceMesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] += new Vector3(7f, 8f, -4f);
                posed.vertices = vertices;
                HairAuthoringPose pose = new HairAuthoringPose(sourceMesh, posed);
                Matrix4x4 transform = Matrix4x4.TRS(new Vector3(10f, -3f, 2f), Quaternion.Euler(12f, 50f, 8f), new Vector3(2f, 3f, 0.5f));
                Assert.That(HairPaintedAreaBounds.TryCalculate(sourceMesh, new[] { 1f, 0f, 0f }, pose, bones,
                    transform, out Bounds bounds, out int boneCount), Is.True);
                Assert.That(boneCount, Is.EqualTo(5), "Do not truncate modern skin weights to four influences.");
                for (int i = 0; i < 5; i++) Assert.That(bounds.Contains(transform.MultiplyPoint3x4(bones[i].Value)), Is.True);
                Assert.That(bounds.Contains(transform.MultiplyPoint3x4(vertices[0])), Is.True, "Include the displayed painted surface, not its bind pose.");
                Assert.That(bounds.Contains(transform.MultiplyPoint3x4(bones[5].Value)), Is.False, "Unpainted vertices must not contribute bones.");
                Assert.That(sourceMesh.GetAllBoneWeights().Length, Is.EqualTo(7), "Mesh-owned weight buffers must remain intact.");
                Assert.That(sourceMesh.GetBonesPerVertex()[0], Is.EqualTo(5));
            }
            finally { Object.DestroyImmediate(posed); }
        }

        [Test]
        public void PaintedAreaBoundsHandleEmptySoftUnskinnedAndBindPoseAreas()
        {
            Assert.That(HairPaintedAreaBounds.TryCalculate(sourceMesh, new float[3], null, null,
                Matrix4x4.identity, out _, out _), Is.False);
            Assert.That(HairPaintedAreaBounds.TryCalculate(sourceMesh, new float[2], null, null,
                Matrix4x4.identity, out _, out _), Is.False);
            Assert.That(HairPaintedAreaBounds.TryCalculate(sourceMesh, new[] { float.NaN, float.PositiveInfinity, -1f }, null, null,
                Matrix4x4.identity, out _, out _), Is.False);
            Assert.That(HairPaintedAreaBounds.TryCalculate(sourceMesh, new[] { 0.00001f, 0f, 0f }, null, null,
                Matrix4x4.identity, out Bounds bounds, out int boneCount), Is.True);
            Assert.That(boneCount, Is.Zero);
            Assert.That(bounds.center, Is.EqualTo(new Vector3(0f, sourceMesh.vertices[0].y, 0f)));
            Assert.That(bounds.Contains(sourceMesh.vertices[0]), Is.True);
            Assert.That(bounds.size.x, Is.GreaterThan(0f));
            Assert.That(bounds.size.y, Is.GreaterThan(0f));
            Assert.That(bounds.size.z, Is.GreaterThan(0f));
            sourceMesh.bindposes = new[] { Matrix4x4.Translate(Vector3.down * 2f) };
            BoneWeight weight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            sourceMesh.boneWeights = new[] { weight, weight, weight };
            Assert.That(HairPaintedAreaBounds.TryCalculate(sourceMesh, new[] { 1f, 0f, 0f }, null, null,
                Matrix4x4.identity, out bounds, out boneCount), Is.True);
            Assert.That(boneCount, Is.EqualTo(1));
            Assert.That(bounds.Contains(Vector3.up * 2f), Is.True, "Standalone weighted meshes use their bind-pose bone positions.");
            Assert.That(HairPaintedAreaBounds.TryCalculate(sourceMesh, new[] { 1f, 0f, 0f }, null, new Vector3?[] { null },
                Matrix4x4.identity, out bounds, out boneCount), Is.True);
            Assert.That(boneCount, Is.Zero, "Missing live bones should safely fall back to the painted surface.");
        }

        [Test]
        public void FocusBoneSnapshotUsesRendererSpaceAndDoesNotFollowSourceAnimation()
        {
            GameObject rendererObject = new GameObject("Focus source renderer");
            GameObject boneObject = new GameObject("Focus source bone");
            try
            {
                rendererObject.transform.SetPositionAndRotation(new Vector3(20f, 30f, 40f), Quaternion.Euler(10f, 20f, 30f));
                rendererObject.transform.localScale = new Vector3(2f, 1f, 3f);
                SkinnedMeshRenderer renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
                boneObject.transform.position = rendererObject.transform.TransformPoint(new Vector3(1f, 2f, 3f));
                renderer.bones = new[] { boneObject.transform, null };
                Vector3?[] snapshot = HairPaintedAreaBounds.CaptureBones(renderer);
                Assert.That(snapshot[0].Value, Is.EqualTo(new Vector3(1f, 2f, 3f)).Using(Vector3ComparerWithEqualsOperator.Instance));
                Assert.That(snapshot[1].HasValue, Is.False);
                boneObject.transform.position += Vector3.one * 100f;
                Assert.That(snapshot[0].Value, Is.EqualTo(new Vector3(1f, 2f, 3f)).Using(Vector3ComparerWithEqualsOperator.Instance));
            }
            finally { Object.DestroyImmediate(rendererObject); Object.DestroyImmediate(boneObject); }
        }

        [TestCase(HairCardShape.Ribbon)]
        [TestCase(HairCardShape.TaperedTube)]
        public void RootEmbeddingAffectsGuideAndChildCardsWithoutChangingCurves(HairCardShape shape)
        {
            HairGroup group = groom.Groups[0];
            profile.Configure(shape, 0.04f, 0.01f, 21, 6);
            groom.Lods[0].useProfileSamples = true;
            group.children.childrenPerGuide = 2;
            group.children.rootSpread = 0.005f;
            HairEvaluationResult original = HairGroomEvaluator.Evaluate(groom);
            group.rootEmbedDepth = 0.003f;
            HairEvaluationResult embedded = HairGroomEvaluator.Evaluate(groom);
            Assert.That(embedded.curves.Count, Is.EqualTo(original.curves.Count));
            Assert.That(embedded.childCurveCount, Is.GreaterThan(0));
            using (HairCardMeshBuildResult plain = HairCardMeshGenerator.Build(original, "Plain", true))
            using (HairCardMeshBuildResult inset = HairCardMeshGenerator.Build(embedded, "Embedded", true))
            using (HairCardMeshBuildResult repeat = HairCardMeshGenerator.Build(embedded))
            {
                Vector3[] plainVertices = plain.mesh.vertices, insetVertices = inset.mesh.vertices;
                Assert.That(repeat.mesh.vertices, Is.EqualTo(insetVertices), "Builds must not accumulate embedding.");
                Assert.That(inset.mesh.uv, Is.EqualTo(plain.mesh.uv));
                Assert.That(inset.mesh.triangles, Is.EqualTo(plain.mesh.triangles));
                for (int curveIndex = 0; curveIndex < embedded.curves.Count; curveIndex++)
                {
                    HairEvaluatedCurve curve = embedded.curves[curveIndex];
                    Assert.That(curve.rootEmbedDepth, Is.EqualTo(0.003f));
                    Assert.That(curve.Clone().rootEmbedDepth, Is.EqualTo(0.003f));
                    Assert.That(curve.points, Is.EqualTo(original.curves[curveIndex].points));
                    HairCardSpan span = inset.cards[curveIndex];
                    int ringSize = shape == HairCardShape.Ribbon ? 2 : 6;
                    for (int ring = 0; ring < 21; ring++)
                    {
                        Vector3 before = Vector3.zero, after = Vector3.zero;
                        for (int side = 0; side < ringSize; side++)
                        {
                            before += plainVertices[span.vertexStart + ring * ringSize + side];
                            after += insetVertices[span.vertexStart + ring * ringSize + side];
                        }
                        float weight = 1f - Mathf.SmoothStep(0f, 1f, (ring / 20f) / 0.2f);
                        Vector3 expected = -curve.rootNormal.normalized * (0.003f * weight);
                        Assert.That(Vector3.Distance((after - before) / ringSize, expected), Is.LessThan(0.000001f));
                    }
                }
                foreach (Vector3 normal in inset.mesh.normals)
                    Assert.That(float.IsFinite(normal.x) && float.IsFinite(normal.y) && float.IsFinite(normal.z), Is.True);
            }
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
            {
                group.rootEmbedDepth = invalid; group.EnsureIntegrity(sourceMesh.vertexCount);
                Assert.That(group.rootEmbedDepth, Is.Zero);
            }
            group.rootEmbedDepth = 1f; group.EnsureIntegrity(sourceMesh.vertexCount);
            Assert.That(group.rootEmbedDepth, Is.EqualTo(0.02f));
        }

        [TestCase(HairSceneTool.Comb)]
        [TestCase(HairSceneTool.Grab)]
        [TestCase(HairSceneTool.Smooth)]
        [TestCase(HairSceneTool.Clump)]
        [TestCase(HairSceneTool.Part)]
        public void GroomRootInfluenceControlsBaseBendingAndPreservesAnchorsAndLengths(HairSceneTool tool)
        {
            HairGroup group = groom.Groups[0];
            if (tool == HairSceneTool.Clump)
            {
                HairGuide neighbor = CreateGuide("Clump neighbor", 22, Vector3.zero);
                for (int i = 1; i < neighbor.points.Count; i++) neighbor.points[i].position += Vector3.right * 0.15f;
                group.guides.Add(neighbor);
            }
            float[] movement = new float[2];
            for (int mode = 0; mode < 2; mode++)
            {
                group.sculptLayers.Clear();
                HairCardStage stage = CreateEditingStage();
                try
                {
                    SetStageField(stage, "sceneTool", tool);
                    stage.BrushRootInfluence = mode;
                    Vector3[] before = group.guides[0].points.ConvertAll(point => point.position).ToArray();
                    InvokeStageMethod(stage, "BeginStroke", "Root influence regression");
                    Vector3 brushCenter = tool == HairSceneTool.Clump ? new Vector3(0.1f, 0.15f, 0f) : new Vector3(-0.05f, 0f, 0f);
                    InvokeStageMethod(stage, "SculptAt", brushCenter, Vector3.forward, Vector3.right * 0.05f);
                    InvokeStageMethod(stage, "EndStroke");
                    stage.RebuildNow();
                    HairEvaluatedCurve curve = stage.Evaluation.evaluatedGuides[0];
                    movement[mode] = Vector3.Distance(curve.points[1].position, before[1]);
                    Assert.That(curve.points[0].position, Is.EqualTo(before[0]));
                    for (int i = 1; i < before.Length; i++)
                        Assert.That(Vector3.Distance(curve.points[i - 1].position, curve.points[i].position),
                            Is.EqualTo(Vector3.Distance(before[i - 1], before[i])).Within(0.00001f));
                    Assert.That(group.guides[0].points.ConvertAll(point => point.position), Is.EqualTo(before));
                }
                finally { DestroyEditingStage(stage); }
            }
            Assert.That(movement[1], Is.GreaterThan(movement[0] * 1.1f), "Higher Root Influence should bend the base more readily.");
        }

        [Test]
        public void RootInfluenceRampAndWireframeToggleAreNonDestructive()
        {
            Assert.That(HairGuideShapeUtility.RootInfluence(0f, 0f), Is.Zero);
            Assert.That(HairGuideShapeUtility.RootInfluence(0f, 0.4f), Is.EqualTo(0.4f));
            Assert.That(HairGuideShapeUtility.RootInfluence(1f, 0f), Is.EqualTo(1f));
            Assert.That(HairGuideShapeUtility.RootInfluence(0.1f, 1f), Is.EqualTo(1f));
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.PreviewMode = HairPreviewMode.Cards; stage.RebuildNow();
                string original = EditorJsonUtility.ToJson(groom);
                Mesh mesh = stage.MeshBuild.mesh;
                HairEvaluationResult evaluation = stage.Evaluation;
                HairCardSpan card = stage.CardAtTriangle(0);
                foreach (bool show in new[] { false, true, false })
                {
                    stage.ShowCardWireframe = show;
                    Assert.That(stage.ShowCardWireframe, Is.EqualTo(show));
                    Assert.That(stage.MeshBuild.mesh, Is.SameAs(mesh));
                    Assert.That(stage.Evaluation, Is.SameAs(evaluation));
                    Assert.That(stage.CardAtTriangle(0), Is.SameAs(card), "Hidden outlines do not disable card picking.");
                }
                stage.BrushRootInfluence = 2f; Assert.That(stage.BrushRootInfluence, Is.EqualTo(1f));
                stage.BrushRootInfluence = float.NaN; Assert.That(stage.BrushRootInfluence, Is.Zero);
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void EraseIsSessionOnlyAndIgnoresPreviouslySavedPreferences()
        {
            HairCardStage first = CreateEditingStage(), second = CreateEditingStage();
            try
            {
                Assert.That(first.PaintErase, Is.False);
                first.PaintErase = true;
                first.BrushRadius = 0.23f;
                foreach (bool global in new[] { false, true })
                {
                    string snapshot = HairPreferenceCodec.Capture(first, global);
                    Assert.That(snapshot, Does.Not.Contain("paintErase"));
                    HairPreferenceCodec.Restore(second, snapshot);
                    Assert.That(second.PaintErase, Is.False);
                    Assert.That(second.BrushRadius, Is.EqualTo(0.23f));
                }
                HairPreferenceCodec.Restore(second,
                    "{\"fields\":[{\"name\":\"paintErase\",\"json\":\"{\\\"value\\\":true}\"}]}");
                Assert.That(second.PaintErase, Is.False, "Old saved Erase=true must be ignored.");
                Assert.That(EditorJsonUtility.ToJson(first), Does.Not.Contain("paintErase"),
                    "Stage serialization must not preserve Erase across reloads either.");
                EditorJsonUtility.FromJsonOverwrite("{\"paintErase\":true}", second);
                Assert.That(second.PaintErase, Is.False);
                Assert.That(first.PaintErase, Is.True, "Saving must not interrupt an active erase session.");
            }
            finally
            {
                DestroyEditingStage(first); DestroyEditingStage(second);
            }
        }

        [Test]
        public void EditorPreferencesRoundTripAllSerializedOptionsAndKeepGroomContextSeparate()
        {
            HairEditorPreferences.Suspended = false;
            HairCardStage first = CreateEditingStage(), second = CreateEditingStage();
            HairEditorPreferences preferences = HairEditorPreferences.instance;
            string oldPreferences = EditorJsonUtility.ToJson(preferences);
            try
            {
                first.BrushRadius = 0.27f; first.BrushHardness = 0.17f; first.BrushStrength = 0.63f;
                first.PaintErase = true;
                first.BrushRootInfluence = 0.4f; first.ShowCardWireframe = false;
                first.ShowGuideRoots = false; first.ShowGuideSplines = false; first.ShowChildSplines = false;
                first.ShowAvatar = false; first.RootHandleScale = 2.3f; first.DepthTestGuides = false;
                first.GravitySeparation = 0.7f; first.GravityCollision = true; first.MirrorCutX = true;
                first.GuideGeneration.guideCount = 300; first.GuideGeneration.rootUniformity = 0.95f;
                SetStageField(first, "hiddenAvatarSlots", new List<string> { "Body", "Hat" });
                SetStageField(first, "activeGroupId", "groom-a-group");
                string snapshot = HairPreferenceCodec.Capture(first);
                Assert.That(snapshot, Does.Not.Contain("\"name\":\"groom\""));
                Assert.That(snapshot, Does.Not.Contain("\"name\":\"sourceAvatar\""));
                Assert.That(snapshot, Does.Not.Contain("instanceID"));
                HairPreferenceCodec.Restore(second, snapshot);
                Assert.That(HairPreferenceCodec.Capture(second), Is.EqualTo(snapshot));
                Assert.That(second.Groom, Is.SameAs(groom));
                first.GuideGeneration.guideCount = 77;
                Assert.That(second.GuideGeneration.guideCount, Is.EqualTo(300), "Generation options must be deep copied.");
                preferences.Remember(first, "test-stage", "groom-a");
                SetStageField(first, "activeGroupId", "groom-b-group"); first.BrushRadius = 0.13f;
                preferences.Remember(first, "test-stage", "groom-b");
                preferences.Restore(second, "test-stage", "groom-a");
                Assert.That(second.PaintErase, Is.False);
                Assert.That(second.BrushRadius, Is.EqualTo(0.27f));
                Assert.That(typeof(HairCardStage).GetField("activeGroupId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(second), Is.EqualTo("groom-a-group"));
                HairPreferenceCodec.Reset(second);
                preferences.Restore(second, "test-stage");
                Assert.That(second.PaintErase, Is.False, "New grooms must not inherit Erase.");
                Assert.That(second.BrushRadius, Is.EqualTo(0.13f), "A new groom inherits last-used options.");
                Assert.That(typeof(HairCardStage).GetField("activeGroupId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(second), Is.Null.Or.Empty);
                string authored = EditorJsonUtility.ToJson(groom);
                HairPreferenceCodec.Reset(second);
                Assert.That(second.BrushRadius, Is.EqualTo(0.075f));
                Assert.That(second.ShowCardWireframe, Is.True);
                Assert.That(second.GuideGeneration.guideCount, Is.EqualTo(50));
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(authored));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(oldPreferences, preferences);
                preferences.Flush();
                DestroyEditingStage(first); DestroyEditingStage(second);
            }
        }

        [Test]
        public void AtlasPanelPreferencesRememberDisplayOptionsAndResetWithoutChangingAtlas()
        {
            HairAtlasEditorPanel first = new HairAtlasEditorPanel(), second = new HairAtlasEditorPanel();
            try
            {
                typeof(HairAtlasEditorPanel).GetField("checkerLight", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(first, Color.red);
                typeof(HairAtlasEditorPanel).GetField("checkerboard", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(first, false);
                typeof(HairAtlasEditorPanel).GetField("showOutlines", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(first, false);
                typeof(HairAtlasEditorPanel).GetField("zoom", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(first, 3f);
                HairPreferenceCodec.Restore(second, HairPreferenceCodec.Capture(first));
                Assert.That(HairPreferenceCodec.Capture(second), Is.EqualTo(HairPreferenceCodec.Capture(first)));
                HairPreferenceCodec.Reset(second);
                Assert.That(typeof(HairAtlasEditorPanel).GetField("checkerboard", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(second), Is.True);
                Assert.That(typeof(HairAtlasEditorPanel).GetField("zoom", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(second), Is.EqualTo(1f));
            }
            finally { first.Dispose(); second.Dispose(); }
        }

        [Test]
        public void NewGroomsInheritPrivateProfilesTexturesAndSetupAndResetIsUndoable()
        {
            HairEditorPreferences.Suspended = false;
            string folderName = "HairPreferenceTest_" + System.Guid.NewGuid().ToString("N");
            string folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            HairEditorPreferences preferences = HairEditorPreferences.instance;
            string oldPreferences = EditorJsonUtility.ToJson(preferences);
            HairGroomAsset next = null;
            try
            {
                AssetDatabase.CreateAsset(sourceMesh, folder + "/Scalp.asset");
                AssetDatabase.CreateAsset(groom, folder + "/Source.asset");
                AssetDatabase.CreateAsset(profile, folder + "/Profile.asset");
                HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
                AssetDatabase.CreateAsset(atlas, folder + "/Atlas.asset");
                Texture2D texture = new Texture2D(2, 2);
                AssetDatabase.CreateAsset(texture, folder + "/Albedo.asset");
                Material material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, folder + "/Material.mat");
                atlas.albedo = texture; atlas.normal = texture; atlas.mask = texture; atlas.material = material;
                Material secondMaterial = new Material(material);
                AssetDatabase.CreateAsset(secondMaterial, folder + "/SecondPass.mat");
                atlas.secondPassMaterial = secondMaterial;
                SharedColorTable sharedTable = ScriptableObject.CreateInstance<SharedColorTable>();
                sharedTable.colors = new[] { new OverlayColorData(3), new OverlayColorData(3) };
                AssetDatabase.CreateAsset(sharedTable, folder + "/SharedColors.asset");
                atlas.sharedColorTable = sharedTable; atlas.sharedColorIndex = 1;
                profile.ConfigureVertexColors(true, new Color(0.1f, 0.2f, 0.3f, 0f), Color.white, 2);
                HairAtlasRegion region = atlas.CreateRegion("Remember me", new Rect(0.2f, 0.1f, 0.3f, 0.7f));
                HairGroup group = groom.Groups[0]; group.atlas = atlas;
                group.rootEmbedDepth = 0.004f; group.children.childrenPerGuide = 7;
                group.atlasRegionSelection = HairAtlasRegionSelectionMode.Selected; group.atlasRegionIds.Add(region.Id);
                groom.Lods[0].samplesPerCard = 19; groom.BakeSettings.umaMaterial = material;
                string original = EditorJsonUtility.ToJson(groom);
                preferences.RememberSetup(groom, group);
                next = ScriptableObject.CreateInstance<HairGroomAsset>();
                next.name = "Next_HairGroom"; next.SetSource(sourceMesh, "mesh:test");
                AssetDatabase.CreateAsset(next, folder + "/Next.asset");
                preferences.ApplySetupToNewGroom(next);
                HairGroup inherited = next.Groups[0];
                Assert.That(inherited.profile, Is.Not.SameAs(profile));
                Assert.That(inherited.profile.DefaultWidth, Is.EqualTo(profile.DefaultWidth));
                Assert.That(inherited.profile.UseVertexColorGradient, Is.True);
                Assert.That(inherited.profile.RootVertexColor, Is.EqualTo(profile.RootVertexColor));
                Assert.That(inherited.profile.TipVertexColor, Is.EqualTo(profile.TipVertexColor));
                Assert.That(inherited.profile.RootColorSegments, Is.EqualTo(2));
                Assert.That(inherited.profile.ProfileId, Is.Not.EqualTo(profile.ProfileId));
                Assert.That(inherited.atlas, Is.Not.SameAs(atlas));
                Assert.That(inherited.atlas.albedo == texture, Is.True);
                Assert.That(inherited.atlas.sharedColorTable == sharedTable, Is.True);
                Assert.That(inherited.atlas.sharedColorIndex, Is.EqualTo(1));
                Assert.That(inherited.atlas.normal == texture, Is.True);
                Assert.That(inherited.atlas.material == material, Is.True);
                Assert.That(inherited.atlas.secondPassMaterial == secondMaterial, Is.True);
                Assert.That(inherited.atlas.regions[0].uvRect, Is.EqualTo(region.uvRect));
                Assert.That(inherited.atlasRegionIds, Is.EqualTo(group.atlasRegionIds));
                Assert.That(inherited.children.childrenPerGuide, Is.EqualTo(7));
                Assert.That(next.Lods[0].samplesPerCard, Is.EqualTo(19));
                Assert.That(next.BakeSettings.umaMaterial == material, Is.True);
                Assert.That(inherited.guides, Is.Empty);
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original));
                preferences.ClearSetupDefaults();
                preferences.ClearSetupDefaults(); // Forget is idempotent.
                preferences.RememberSetup(groom, group); // An unchanged idle save must not undo Forget.
                Assert.That(typeof(HairEditorPreferences).GetField("hasLastSetup", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(preferences), Is.False);
                HairGroomAsset fresh = ScriptableObject.CreateInstance<HairGroomAsset>(); fresh.EnsureIntegrity();
                AssetDatabase.CreateAsset(fresh, folder + "/ForgottenDefaults.asset");
                preferences.ApplySetupToNewGroom(fresh);
                Assert.That(fresh.Groups[0].atlas, Is.Null);
                Assert.That(fresh.Groups[0].profile, Is.Null);
                HairCardProfileAsset beforeProfile = inherited.profile;
                HairAtlasProfileAsset beforeAtlas = inherited.atlas;
                inherited.guides.Add(CreateGuide("Keep authored guide", 15, Vector3.zero));
                preferences.ResetCurrentSetup(next, inherited);
                Assert.That(inherited.atlas.albedo, Is.Null);
                Assert.That(inherited.atlas.material, Is.Null);
                Assert.That(inherited.atlas.secondPassMaterial, Is.Null);
                Assert.That(inherited.rootEmbedDepth, Is.Zero);
                Assert.That(inherited.guides.Count, Is.EqualTo(1));
                Assert.That(beforeAtlas.albedo == texture, Is.True);
                Undo.PerformUndo();
                Assert.That(next.Groups[0].profile == beforeProfile, Is.True, "Undo restores the old card profile assignment.");
                Assert.That(next.Groups[0].atlas == beforeAtlas, Is.True, "Undo restores the old atlas assignment.");
                Assert.That(next.Groups[0].rootEmbedDepth, Is.EqualTo(0.004f));
            }
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(oldPreferences, preferences);
                preferences.Flush();
                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
                    foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                        if (asset != null) Undo.ClearUndo(asset);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void NamedBoneFocusSnapshotsCharacterLocalPoseAndRejectsSimilarNames()
        {
            GameObject character = new GameObject("Focus character");
            try
            {
                character.SetActive(false);
                var avatar = character.AddComponent<UMA.CharacterSystem.DynamicCharacterAvatar>();
                character.transform.SetPositionAndRotation(new Vector3(20f, 30f, 40f), Quaternion.Euler(10f, 20f, 30f));
                character.transform.localScale = new Vector3(2f, 1f, 3f);
                new GameObject("Head_End").transform.SetParent(character.transform, false);
                new GameObject("NeckTwist").transform.SetParent(character.transform, false);
                Assert.That(HairBoneFocus.Capture(avatar, HumanBodyBones.Head), Is.Null);
                Assert.That(HairBoneFocus.Capture(avatar, HumanBodyBones.Neck), Is.Null);
                Transform head = new GameObject("Head").transform;
                head.SetParent(character.transform, false);
                head.localPosition = new Vector3(0.02f, 1.7f, 0.04f);
                Transform neck = new GameObject("Neck").transform;
                neck.SetParent(character.transform, false);
                neck.localPosition = new Vector3(0f, 1.5f, 0.01f);
                Vector3? headSnapshot = HairBoneFocus.Capture(avatar, HumanBodyBones.Head);
                Assert.That(Vector3.Distance(headSnapshot.Value, head.localPosition), Is.LessThan(0.00001f));
                Assert.That(Vector3.Distance(HairBoneFocus.Capture(avatar, HumanBodyBones.Neck).Value, neck.localPosition), Is.LessThan(0.00001f));
                head.localPosition += Vector3.one;
                Assert.That(Vector3.Distance(headSnapshot.Value, head.localPosition), Is.GreaterThan(1f));
                Assert.That(HairBoneFocus.Capture(avatar, HumanBodyBones.Hips), Is.Null);
                Assert.That(HairBoneFocus.Capture(null, HumanBodyBones.Head), Is.Null);
            }
            finally { Object.DestroyImmediate(character); }
        }

        [UnityTest]
        public IEnumerator NamedBoneFocusPreservesAngleAndUsesUpdatedOffsetAndPointFourFiveMeterDistance()
        {
            HairCardStage stage = CreateEditingStage();
            SceneView view = ScriptableObject.CreateInstance<SceneView>();
            try
            {
                Vector3 head = new Vector3(0.02f, 1.7f, 0.04f), neck = new Vector3(0f, 1.5f, 0.01f);
                SetStageField(stage, "authoringHeadPosition", head);
                SetStageField(stage, "authoringNeckPosition", neck);
                string original = EditorJsonUtility.ToJson(groom);
                view.Show();
                Quaternion rotation = Quaternion.Euler(15f, 30f, 0f);
                foreach (bool orthographic in new[] { false, true })
                foreach (float fov in new[] { 40f, 90f })
                foreach (HumanBodyBones bone in new[] { HumanBodyBones.Head, HumanBodyBones.Neck })
                {
                    view.position = new Rect(20f, 20f, fov == 40f ? 800f : 400f, 600f);
                    view.orthographic = orthographic;
                    view.cameraSettings.fieldOfView = fov;
                    view.rotation = rotation;
                    Assert.That(stage.CanFocusBone(bone), Is.True);
                    Assert.That(stage.FocusBone(bone), Is.True);
                    // Repeated focus must apply the offset to the bone snapshot, not accumulate it.
                    Assert.That(stage.FocusBone(bone), Is.True);
                    InvokeStageMethod(stage, "ApplyPendingFocus", view);
                    double deadline = EditorApplication.timeSinceStartup + 0.2d;
                    while (EditorApplication.timeSinceStartup < deadline) { view.Repaint(); yield return null; }
                    Vector3 expected = bone == HumanBodyBones.Head ? head : neck;
#if !NoFudge
                    // The framing adjustment is in stage space and applies to both named bones.
                    expected += Vector3.up * 0.05f;
#endif
                    Assert.That(Vector3.Distance(view.pivot, expected), Is.LessThan(0.0001f));
                    Assert.That(Vector3.Distance(view.camera.transform.position, expected), Is.EqualTo(0.45f).Within(0.0001f));
                    Assert.That(Quaternion.Angle(view.rotation, rotation), Is.LessThan(0.1f));
                    Assert.That(view.orthographic, Is.EqualTo(orthographic));
                }
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original));
                SetStageField(stage, "authoringHeadPosition", null);
                Vector3 previous = view.pivot;
                Assert.That(stage.CanFocusBone(HumanBodyBones.Head), Is.False);
                Assert.That(stage.FocusBone(HumanBodyBones.Head), Is.False);
                InvokeStageMethod(stage, "ApplyPendingFocus", view);
                Assert.That(view.pivot, Is.EqualTo(previous));
                stage.FrameGroom();
                Assert.That(typeof(HairCardStage).GetField("pendingFocusPoint", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage), Is.Null);
            }
            finally { view.Close(); DestroyEditingStage(stage); }
        }

        [UnityTest]
        public IEnumerator FocusCurrentAreaUsesGrowthMapAndFramesWithoutChangingGroomOrVisibility()
        {
            HairCardStage stage = CreateEditingStage();
            SceneView view = ScriptableObject.CreateInstance<SceneView>();
            try
            {
                HairGroup first = groom.Groups[0];
                HairGrowthMap growth = first.FindMap(HairMapKind.GrowthArea);
                growth.values = new[] { 1f, 0f, 0f };
                HairGrowthMap density = first.FindMap(HairMapKind.Density);
                density.values = new[] { 0f, 1f, 1f };
                SetStageField(stage, "activeMapId", density.Id);
                string original = EditorJsonUtility.ToJson(groom);
                bool showAvatar = stage.ShowAvatar, showScalp = stage.ShowScalp;
                Assert.That(stage.TryGetCurrentAreaBounds(out Bounds expected, out _), Is.True);
                Assert.That(expected.center, Is.EqualTo(new Vector3(0f, sourceMesh.vertices[0].y, 0f)), "The orbit pivot stays on the character axis.");
                Assert.That(expected.Contains(sourceMesh.vertices[0]), Is.True, "Focus the Growth Area even when another scalar map is selected.");
                Assert.That(stage.FocusCurrentArea(), Is.True);
                view.Show();
                Quaternion rotation = Quaternion.Euler(15f, 30f, 0f);
                view.rotation = rotation;
                view.pivot = new Vector3(8f, 10f, -4f);
                InvokeStageMethod(stage, "ApplyPendingFocus", view);
                double deadline = EditorApplication.timeSinceStartup + 1d;
                while (EditorApplication.timeSinceStartup < deadline) { view.Repaint(); yield return null; }
                Assert.That(Vector3.Distance(view.pivot, expected.center), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(view.rotation, rotation), Is.LessThan(0.1f));
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original));
                Assert.That(stage.ShowAvatar, Is.EqualTo(showAvatar));
                Assert.That(stage.ShowScalp, Is.EqualTo(showScalp));
                Vector3 previousPivot = view.pivot;
                growth.values = new float[3];
                Assert.That(stage.FocusCurrentArea(), Is.False);
                InvokeStageMethod(stage, "ApplyPendingFocus", view);
                Assert.That(view.pivot, Is.EqualTo(previousPivot), "Empty paint must not reframe the entire avatar.");
                growth.values = new[] { 0f, 0f, 1f };
                Assert.That(stage.TryGetCurrentAreaBounds(out Bounds changed, out _), Is.True);
                Assert.That(changed.center, Is.EqualTo(expected.center), "Painting a different side must not shift the orbit axis.");
                Assert.That(changed.Contains(sourceMesh.vertices[2]), Is.True);
                Assert.That(changed.size.x, Is.LessThan(expected.size.x), "Read the latest paint for zoom, not cached bounds.");
            }
            finally { view.Close(); DestroyEditingStage(stage); }
        }

        [Test]
        public void FocusAxisIsStableForLeftRightFrontAndQuarterHeadPaint()
        {
            Mesh mesh = new Mesh();
            try
            {
                Vector3[] characterPoints = { new Vector3(-1f, 2f, -0.5f), new Vector3(-1f, 2f, 0.5f),
                    new Vector3(1f, 2f, -0.5f), new Vector3(1f, 2f, 0.5f) };
                Vector3 characterBone = new Vector3(0f, 1.5f, 0f);
                BoneWeight weight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
                foreach (Matrix4x4 rendererToCharacter in new[] { Matrix4x4.identity,
                    Matrix4x4.TRS(new Vector3(5f, -2f, 4f), Quaternion.Euler(15f, 45f, 10f), new Vector3(2f, 0.5f, 3f)) })
                {
                    Vector3[] localPoints = new Vector3[4];
                    for (int i = 0; i < 4; i++) localPoints[i] = rendererToCharacter.inverse.MultiplyPoint3x4(characterPoints[i]);
                    mesh.vertices = localPoints;
                    mesh.RecalculateBounds();
                    mesh.bindposes = new[] { Matrix4x4.identity };
                    mesh.boneWeights = new[] { weight, weight, weight, weight };
                    Vector3?[] bones = { rendererToCharacter.inverse.MultiplyPoint3x4(characterBone) };
                    Bounds? fullBounds = null;
                    foreach (float[] paint in new[] { new[] { 1f, 1f, 1f, 1f }, new[] { 1f, 1f, 0f, 0f },
                        new[] { 0f, 0f, 1f, 1f }, new[] { 1f, 0f, 1f, 0f }, new[] { 0f, 0f, 0f, 1f } })
                    {
                        Assert.That(HairPaintedAreaBounds.TryCalculate(mesh, paint, null, bones, rendererToCharacter,
                            out Bounds bounds, out int boneCount), Is.True);
                        Assert.That(boneCount, Is.EqualTo(1));
                        Assert.That(bounds.center.x, Is.Zero, "Renderer offsets must not become the character's orbit axis.");
                        Assert.That(bounds.center.z, Is.Zero, "Front-only paint must not pull the orbit pivot forward.");
                        Assert.That(bounds.center.y, Is.EqualTo(1.75f).Within(0.00001f));
                        fullBounds ??= bounds;
                        Assert.That(Vector3.Distance(bounds.size, fullBounds.Value.size), Is.LessThan(0.00001f),
                            "Equivalent half/quarter head extents should frame identically to the whole region.");
                        foreach (Vector3 point in characterPoints) Assert.That(bounds.Contains(point), Is.True,
                            "Centering must expand the box, not crop the painted side.");
                    }
                }
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void ChildSplineVisibilityDoesNotRebuildOrRemoveChildCards()
        {
            groom.Groups[0].children.childrenPerGuide = 3;
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.PreviewMode = HairPreviewMode.Cards;
                stage.RebuildNow();
                HairCardMeshBuildResult build = stage.MeshBuild;
                HairEvaluationResult evaluation = stage.Evaluation;
                Assert.That(evaluation.childCurveCount, Is.GreaterThan(0));
                string original = EditorJsonUtility.ToJson(groom);
                stage.ShowChildSplines = false;
                stage.ShowGuideSplines = false;
                Assert.That(stage.ShowChildren, Is.True, "Line visibility must not disable child generation.");
                Assert.That(stage.MeshBuild, Is.SameAs(build));
                Assert.That(stage.Evaluation, Is.SameAs(evaluation));
                Assert.That(typeof(HairCardStage).GetField("rebuildQueued", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(stage), Is.False, "Display toggles should repaint without rebuilding geometry.");
                stage.PreviewMode = HairPreviewMode.GuidesAndChildren;
                stage.RebuildNow();
                Assert.That(stage.Evaluation.childCurveCount, Is.EqualTo(evaluation.childCurveCount));
                Assert.That(stage.ShowChildSplines, Is.False, "Display preferences must survive mode changes.");
                stage.ShowChildSplines = true;
                Assert.That(stage.ShowGuideSplines, Is.False, "Parent and child line visibility are independent.");
                Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(original));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void GuideDepthPassIncludesAlphaTestButSkipsTransparentSurfaces()
        {
            Material material = new Material(Shader.Find("Standard"));
            try
            {
                Assert.That(HairGuideDepthRenderer.CanOccludeGuides(material), Is.True);
                material.renderQueue = 2450;
                Assert.That(HairGuideDepthRenderer.CanOccludeGuides(material), Is.True);
                material.renderQueue = 3000;
                Assert.That(HairGuideDepthRenderer.CanOccludeGuides(material), Is.False);
                material.renderQueue = 2000;
                material.EnableKeyword("_ALPHATEST_ON");
                Assert.That(HairGuideDepthRenderer.CanOccludeGuides(material), Is.True);
                material.DisableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "Transparent");
                Assert.That(HairGuideDepthRenderer.CanOccludeGuides(material), Is.False);
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void VisibleScopeUsesSurfaceOcclusionAndThroughScopeCanReachBackHair()
        {
            HairGroup group = groom.Groups[0];
            group.guides[0].points[1].position = Vector3.down * 0.1f;
            group.guides[0].points[2].position = Vector3.down * 0.2f;
            HairCardStage stage = CreateEditingStage();
            GameObject cameraObject = new GameObject("Brush scope camera");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.transform.SetPositionAndRotation(new Vector3(0f, 2f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                camera.orthographic = true;
                SetStageField(stage, "brushCamera", camera);
                SetStageField(stage, "surfaceRaycaster", new HairMeshRaycaster(sourceMesh));
                stage.BrushScope = HairBrushScope.VisibleHair;
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.up, Vector3.right * 0.05f);
                Assert.That(group.sculptLayers, Is.Empty, "Back-side points must not create an edit layer in Visible scope.");
                stage.BrushScope = HairBrushScope.ThroughDepth;
                InvokeStageMethod(stage, "SculptAt", Vector3.zero, Vector3.up, Vector3.right * 0.05f);
                Assert.That(group.sculptLayers[0].deltas.Count, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(cameraObject); DestroyEditingStage(stage); }
        }

        [Test]
        public void InfluenceFeedbackUsesTheSameSelectionAndFreezeFilteringAsSculpt()
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                IList entries = (IList)typeof(HairCardStage).GetField("curveBrushEntries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage);
                MethodInfo fill = typeof(HairCardStage).GetMethod("FillBrushInfluences", BindingFlags.Instance | BindingFlags.NonPublic);
                object[] args = { entries[0], Vector3.zero, Vector3.forward };
                Assert.That(fill.Invoke(stage, args), Is.True);
                stage.BrushScope = HairBrushScope.SelectedGuidesOnly;
                Assert.That(fill.Invoke(stage, args), Is.False);
                stage.SetGuideSelection(new[] { groom.Groups[0].guides[0].Id });
                Assert.That(fill.Invoke(stage, args), Is.True);
                foreach (HairGuidePoint point in groom.Groups[0].guides[0].points) point.freeze = 1f;
                Assert.That(fill.Invoke(stage, args), Is.False);
                stage.SceneTool = HairSceneTool.Freeze;
                stage.PaintErase = true;
                Assert.That(fill.Invoke(stage, args), Is.True, "Unfreeze must reach frozen points.");
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void GuideBatchDuplicateCopiesLayerDeltasWithIndependentIdsAndArrays()
        {
            HairGroup group = groom.Groups[0];
            HairGuide source = group.guides[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            layer.deltas.Add(new HairGuideDelta { guideId = source.Id, positionOffsets = new[] { Vector3.zero, Vector3.right, Vector3.up } });
            List<string> copied = HairGroomCommands.ApplyGuideBatch(groom, group, new[] { source.Id }, HairGuideBatchAction.Duplicate);
            Assert.That(copied.Count, Is.EqualTo(1));
            Assert.That(copied[0], Is.Not.EqualTo(source.Id));
            Assert.That(group.guides[1].points[1], Is.Not.SameAs(source.points[1]));
            Assert.That(layer.deltas[1].positionOffsets, Is.EqualTo(layer.deltas[0].positionOffsets));
            Assert.That(layer.deltas[1].positionOffsets, Is.Not.SameAs(layer.deltas[0].positionOffsets));
            HairGroomCommands.ApplyGuideBatch(groom, group, copied, HairGuideBatchAction.Freeze);
            Assert.That(group.guides[1].points[2].freeze, Is.EqualTo(1f));
            Assert.That(source.points[2].freeze, Is.Zero);
            HairGroomCommands.ApplyGuideBatch(groom, group, copied, HairGuideBatchAction.Unfreeze);
            HairGroomCommands.ApplyGuideBatch(groom, group, copied, HairGuideBatchAction.Disable);
            Assert.That(group.guides[1].enabled, Is.False);
            group.locked = true;
            Assert.That(HairGroomCommands.ApplyGuideBatch(groom, group, copied, HairGuideBatchAction.Delete), Is.Empty);
            group.locked = false;
            HairGroomCommands.ApplyGuideBatch(groom, group, copied, HairGuideBatchAction.Delete);
            Assert.That(group.guides.Count, Is.EqualTo(1));
            Assert.That(layer.deltas.Count, Is.EqualTo(1));
            Undo.ClearUndo(groom);
        }

        [Test]
        public void LayerSoloIsPreviewOnlyAndOtherGroupsKeepTheirLayers()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer first = HairGroomCommands.AddSculptLayer(groom, group, "First");
            first.deltas.Add(new HairGuideDelta { guideId = group.guides[0].Id, positionOffsets = new[] { Vector3.zero, Vector3.right * 0.1f, Vector3.right * 0.1f } });
            HairSculptLayer second = HairGroomCommands.DuplicateLayer(groom, group, first);
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
            Assert.That(second.deltas[0].positionOffsets, Is.Not.SameAs(first.deltas[0].positionOffsets));
            HairGroup other = groom.CreateGroup("Other", HairGroupRole.Coverage);
            other.guides.Add(CreateGuide("Other guide", 123, Vector3.zero));
            HairSculptLayer otherLayer = HairGroomCommands.DuplicateLayer(groom, group, first);
            group.sculptLayers.Remove(otherLayer);
            otherLayer.deltas[0].guideId = other.guides[0].Id;
            other.sculptLayers.Add(otherLayer);
            groom.EnsureIntegrity();
            HairEvaluationResult full = HairGroomEvaluator.Evaluate(groom);
            HairEvaluationResult solo = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { soloLayerId = first.Id });
            Assert.That(full.evaluatedGuides[0].points[1].position.x - solo.evaluatedGuides[0].points[1].position.x, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(solo.evaluatedGuides[1].points[1].position, Is.EqualTo(full.evaluatedGuides[1].points[1].position));
            Assert.That(HairGroomCommands.EditStack(groom, group, first.Id, true, 1), Is.True);
            Assert.That(group.sculptLayers[1], Is.SameAs(first));
            first.locked = true;
            Assert.That(HairGroomCommands.EditStack(groom, group, first.Id, true, 0, true), Is.False);
            Undo.ClearUndo(groom);
        }

        [TestCase(HairCardShape.Ribbon)]
        [TestCase(HairCardShape.TaperedTube)]
        public void UvRefreshKeepsGeometryAndMatchesAFreshBuild(HairCardShape shape)
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            profile.Configure(shape, 0.04f, 0.005f, 7);
            groom.Groups[0].atlas = atlas;
            HairAtlasRegion region = atlas.CreateRegion("Original", new Rect(0f, 0f, 0.4f, 1f));
            HairCardMeshBuildResult build = HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom), "UV regression", includeEditingMetadata: true);
            HairCardMeshBuildResult fresh = null;
            try
            {
                Mesh original = build.mesh;
                Vector3[] vertices = original.vertices;
                int[] triangles = original.triangles;
                region.uvRect = new Rect(0.5f, 0.2f, 0.3f, 0.7f); region.flipU = true; region.flipV = true;
                HairCardMeshGenerator.RefreshAtlasUvs(build, groom);
                fresh = HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom));
                Assert.That(fresh.cards, Is.Empty, "Release/runtime builds should not retain editor picking metadata.");
                Assert.That(fresh.localUvs, Is.Empty);
                Assert.That(build.mesh, Is.SameAs(original));
                Assert.That(build.mesh.vertices, Is.EqualTo(vertices));
                Assert.That(build.mesh.triangles, Is.EqualTo(triangles));
                Assert.That(build.mesh.uv, Is.EqualTo(fresh.mesh.uv));
                Assert.That(build.cards[0].uvSetId, Is.EqualTo(region.Id));
                Assert.That(build.cards[0].vertexCount, Is.EqualTo(build.vertexCount));
                Assert.That(build.cards[0].triangleCount, Is.EqualTo(build.triangleCount));
            }
            finally { fresh?.Dispose(); build.Dispose(); Object.DestroyImmediate(atlas); }
        }

        [Test]
        public void TargetedPreviewUpdatesReuseEvaluationAndUvEditsSurviveGeometryRebuild()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasRegion first = atlas.CreateRegion("First", new Rect(0f, 0f, 0.4f, 1f));
            HairAtlasRegion second = atlas.CreateRegion("Second", new Rect(0.5f, 0f, 0.5f, 1f));
            groom.Groups[0].atlas = atlas;
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.PreviewMode = HairPreviewMode.Cards; stage.RebuildNow();
                HairEvaluationResult evaluation = stage.Evaluation;
                Mesh mesh = stage.MeshBuild.mesh;
                groom.Groups[0].atlasRegionSelection = HairAtlasRegionSelectionMode.Selected;
                groom.Groups[0].atlasRegionIds.Add(second.Id);
                stage.QueuePreviewChange(HairPreviewChange.Uvs); stage.RebuildNow();
                Assert.That(stage.Evaluation, Is.SameAs(evaluation));
                Assert.That(stage.MeshBuild.mesh, Is.SameAs(mesh));
                Assert.That(stage.MeshBuild.cards[0].uvSetId, Is.EqualTo(second.Id));
                Assert.That(stage.CardAtTriangle(0), Is.SameAs(stage.MeshBuild.cards[0]));
                Assert.That(stage.CardAtTriangle(-1), Is.Null);
                stage.QueuePreviewChange(HairPreviewChange.Materials | HairPreviewChange.Display | HairPreviewChange.Validation); stage.RebuildNow();
                Assert.That(stage.MeshBuild.mesh, Is.SameAs(mesh));
                Assert.That(stage.Evaluation, Is.SameAs(evaluation));
                stage.PreviewMode = HairPreviewMode.Wireframe; stage.RebuildNow();
                Assert.That(stage.MeshBuild.mesh, Is.SameAs(mesh), "Card display changes must reuse geometry.");
                stage.QueuePreviewChange(HairPreviewChange.Geometry); stage.RebuildNow();
                Assert.That(stage.Evaluation, Is.SameAs(evaluation));
                Assert.That(stage.MeshBuild.cards[0].uvSetId, Is.EqualTo(second.Id));
                stage.PreviewMode = HairPreviewMode.Guides; stage.RebuildNow();
                Assert.That(stage.MeshBuild, Is.Null, "Guide-only mode must not allocate a card mesh.");
                Assert.That(stage.Evaluation.evaluatedGuides.Count, Is.EqualTo(1));
            }
            finally { DestroyEditingStage(stage); Object.DestroyImmediate(atlas); }
        }

        [Test]
        public void DraftPreviewLimitsNeverChangeFullOrReleaseOutput()
        {
            groom.Lods[0].samplesPerCard = 24;
            groom.Groups[0].children.childrenPerGuide = 2000;
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.PreviewMode = HairPreviewMode.Cards;
                stage.PreviewQuality = HairPreviewQuality.Draft; stage.RebuildNow();
                Assert.That(stage.MeshBuild.cardCount, Is.EqualTo(1500));
                Assert.That(stage.Evaluation.curves[0].samplesPerCardOverride, Is.EqualTo(8));
                HairEvaluationResult release = HairGroomEvaluator.Evaluate(groom);
                Assert.That(release.CardCount, Is.GreaterThan(1500));
                Assert.That(release.curves[0].samplesPerCardOverride, Is.EqualTo(24));
                stage.PreviewQuality = HairPreviewQuality.Full; stage.RebuildNow();
                Assert.That(stage.MeshBuild.cardCount, Is.EqualTo(release.CardCount));
                Assert.That(stage.Evaluation.curves[0].samplesPerCardOverride, Is.EqualTo(24));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void SlicePreviewIsNonMutatingAndHonorsFrozenTipsSelectionAndMirror()
        {
            HairCardStage stage = CreateEditingStage();
            try
            {
                HairGuide guide = groom.Groups[0].guides[0];
                Vector3 original = guide.points[2].position;
                Plane plane = new Plane(Vector3.up, Vector3.up * 0.2f);
                System.Predicate<Vector3> finite = _ => true;
                InvokeStageMethod(stage, "CollectSlicePreview", plane, finite);
                Assert.That(stage.SlicePreviewCount, Is.EqualTo(1));
                stage.MirrorCutX = true;
                InvokeStageMethod(stage, "CollectSlicePreview", plane, finite);
                Assert.That(stage.SlicePreviewCount, Is.EqualTo(1), "A guide hit by both planes is counted only once.");
                Assert.That(guide.points[2].position, Is.EqualTo(original));
                guide.points[2].freeze = 1f;
                InvokeStageMethod(stage, "CollectSlicePreview", plane, finite);
                Assert.That(stage.SlicePreviewCount, Is.Zero);
                guide.points[2].freeze = 0f;
                stage.BrushScope = HairBrushScope.SelectedGuidesOnly;
                InvokeStageMethod(stage, "CollectSlicePreview", plane, finite);
                Assert.That(stage.SlicePreviewCount, Is.Zero);
                stage.SetGuideSelection(new[] { guide.Id });
                InvokeStageMethod(stage, "CollectSlicePreview", plane, finite);
                Assert.That(stage.SlicePreviewCount, Is.EqualTo(1));
                InvokeStageMethod(stage, "CollectSlicePreview", plane, new System.Predicate<Vector3>(_ => false));
                Assert.That(stage.SlicePreviewCount, Is.Zero);
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void MirroredSlicePreviewIncludesTheOppositeSideWithoutDoubleCounting()
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear();
            group.guides.Add(CreateLinearGuide("Right", 11, Vector3.zero, new Vector3(0.2f, 0.3f, 0f)));
            group.guides.Add(CreateLinearGuide("Left", 22, Vector3.zero, new Vector3(-0.2f, 0.3f, 0f)));
            groom.EnsureIntegrity();
            HairCardStage stage = CreateEditingStage();
            try
            {
                Plane plane = new Plane(Vector3.right, Vector3.right * 0.08f);
                System.Predicate<Vector3> finite = _ => true;
                InvokeStageMethod(stage, "CollectSlicePreview", plane, finite);
                Assert.That(stage.SlicePreviewCount, Is.EqualTo(1));
                stage.MirrorCutX = true;
                InvokeStageMethod(stage, "CollectSlicePreview", plane, finite);
                Assert.That(stage.SlicePreviewCount, Is.EqualTo(2));
            }
            finally { DestroyEditingStage(stage); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CardPickingMetadataMapsFlattenedTrianglesAcrossMaterials(bool twoPasses)
        {
            HairAtlasProfileAsset firstAtlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasProfileAsset secondAtlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material firstMaterial = new Material(Shader.Find("Unlit/Color"));
            Material secondMaterial = new Material(Shader.Find("Unlit/Color"));
            firstAtlas.material = firstMaterial; secondAtlas.material = secondMaterial;
            if (twoPasses) firstAtlas.secondPassMaterial = secondAtlas.secondPassMaterial = secondMaterial;
            HairAtlasRegion firstSet = firstAtlas.CreateRegion("First", new Rect(0f, 0f, 0.5f, 1f));
            HairAtlasRegion secondSet = secondAtlas.CreateRegion("Second", new Rect(0.5f, 0f, 0.5f, 1f));
            groom.Groups[0].atlas = firstAtlas;
            HairGroup other = groom.CreateGroup("Other", HairGroupRole.Coverage);
            other.profile = profile; other.atlas = secondAtlas; other.children.childrenPerGuide = 0;
            other.guides.Add(CreateGuide("Other guide", 15, Vector3.right * 0.1f));
            groom.EnsureIntegrity();
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.PreviewMode = HairPreviewMode.Cards; stage.RebuildNow();
                Assert.That(stage.MeshBuild.mesh.subMeshCount, Is.EqualTo(twoPasses ? 4 : 2));
                int firstTriangles = (int)stage.MeshBuild.mesh.GetIndexCount(0) / 3;
                Assert.That(stage.CardAtTriangle(firstTriangles - 1).uvSetId, Is.EqualTo(firstSet.Id));
                Assert.That(stage.CardAtTriangle(firstTriangles).uvSetId, Is.EqualTo(secondSet.Id));
                if (twoPasses)
                {
                    Assert.That(stage.CardAtTriangle(stage.MeshBuild.triangleCount).uvSetId, Is.EqualTo(firstSet.Id));
                    Assert.That(stage.CardAtTriangle(stage.MeshBuild.triangleCount + firstTriangles).uvSetId, Is.EqualTo(secondSet.Id));
                }
                Assert.That(stage.CardAtTriangle(stage.MeshBuild.triangleCount * (twoPasses ? 2 : 1)), Is.Null);
            }
            finally
            {
                DestroyEditingStage(stage); Object.DestroyImmediate(firstAtlas); Object.DestroyImmediate(secondAtlas);
                Object.DestroyImmediate(firstMaterial); Object.DestroyImmediate(secondMaterial);
            }
        }

        [Test]
        public void CollisionClosestPointUsesTriangleSurfaceAndOutwardWinding()
        {
            sourceMesh.triangles = new[] { 0, 2, 1 };
            HairMeshRaycaster bvh = new HairMeshRaycaster(sourceMesh);
            Assert.That(bvh.ClosestPoint(new Vector3(0f, 0.2f, 0f), out HairMeshRaycastHit hit), Is.True);
            Assert.That(hit.Point.y, Is.Zero.Within(1e-6f));
            Assert.That(hit.Normal, Is.EqualTo(Vector3.up));
            Assert.That(hit.Distance, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(bvh.ClosestPoint(Vector3.up, out _, 0.1f), Is.False);
        }

        [Test]
        public void GravityCollisionKeepsSegmentLengthsAndDoesNotSinkThroughTheScalp()
        {
            sourceMesh.triangles = new[] { 0, 2, 1 };
            HairGuide guide = groom.Groups[0].guides[0];
            guide.points[1].position = new Vector3(0.1f, 0.01f, 0f);
            guide.points[2].position = new Vector3(0.2f, 0.015f, 0f);
            HairCardStage stage = CreateEditingStage();
            try
            {
                HairEvaluatedCurve original = stage.Evaluation.evaluatedGuides[0].Clone();
                stage.GravityCollision = true; stage.GravityClearance = 0.002f;
                stage.GravitySeparation = 0f; stage.SetGravityHeld(true);
                for (int i = 0; i < 90; i++) InvokeStageMethod(stage, "ApplyGravityStep", 1f / 60f);
                stage.SetGravityHeld(false); stage.RebuildNow();
                HairEvaluatedCurve result = stage.Evaluation.evaluatedGuides[0];
                Assert.That(result.points[0].position, Is.EqualTo(original.points[0].position));
                for (int i = 1; i < result.points.Count; i++)
                {
                    Assert.That(result.points[i].position.y, Is.GreaterThanOrEqualTo(-0.00005f));
                    Assert.That(Vector3.Distance(result.points[i - 1].position, result.points[i].position),
                        Is.EqualTo(Vector3.Distance(original.points[i - 1].position, original.points[i].position)).Within(1e-5f));
                }
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void UvDuplicateAndNudgeAreIndependentClampedAndUndoable()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasEditorPanel panel = new HairAtlasEditorPanel();
            try
            {
                HairAtlasRegion original = atlas.CreateRegion("Original", new Rect(0.1f, 0.2f, 0.3f, 0.4f));
                panel.SelectSet(atlas, original.Id); panel.DuplicateSelected();
                Assert.That(atlas.regions.Count, Is.EqualTo(2));
                Assert.That(panel.Selected.Id, Is.Not.EqualTo(original.Id));
                Assert.That(panel.Selected.uvRect, Is.EqualTo(original.uvRect));
                Undo.IncrementCurrentGroup();
                panel.NudgeSelected(new Vector2(100f, -100f));
                Assert.That(panel.Selected.uvRect.x, Is.EqualTo(0.7f).Within(1e-6f));
                Assert.That(panel.Selected.uvRect.y, Is.Zero);
                Assert.That(original.uvRect, Is.EqualTo(new Rect(0.1f, 0.2f, 0.3f, 0.4f)));
                Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Assert.That(panel.Selected.uvRect, Is.EqualTo(original.uvRect));
            }
            finally { panel.Dispose(); Undo.ClearUndo(atlas); Object.DestroyImmediate(atlas); }
        }

        [TestCase(1)]
        [TestCase(4)]
        public void DensePointIndexMatchesBruteForceAndPrunesDistantBranches(int neighbors)
        {
            System.Random random = new System.Random(516);
            List<Vector3> points = new List<Vector3>();
            for (int i = 0; i < 4096; i++)
                points.Add(new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble()));
            HairPointSpatialIndex index = new HairPointSpatialIndex();
            index.Rebuild(points);
            HairNearestPoint[] nearest = new HairNearestPoint[neighbors];
            int[] expected = new int[points.Count];
            long visited = 0;
            for (int query = 0; query < 100; query++)
            {
                Vector3 position = query == 0 ? Vector3.one * 100f : new Vector3((float)random.NextDouble(),
                    (float)random.NextDouble(), (float)random.NextDouble());
                for (int i = 0; i < expected.Length; i++) expected[i] = i;
                System.Array.Sort(expected, (a, b) =>
                {
                    int order = (points[a] - position).sqrMagnitude.CompareTo((points[b] - position).sqrMagnitude);
                    return order != 0 ? order : a.CompareTo(b);
                });
                Assert.That(index.QueryNearest(position, nearest), Is.EqualTo(neighbors));
                visited += index.LastQueryVisitedNodes;
                for (int i = 0; i < neighbors; i++)
                {
                    Assert.That(nearest[i].index, Is.EqualTo(expected[i]));
                    Assert.That(nearest[i].distanceSquared, Is.EqualTo((position - points[expected[i]]).sqrMagnitude));
                }
            }
            Assert.That(visited / 100d, Is.LessThan(points.Count / 8d));
        }

        [Test]
        public void DensePointIndexHasStableTiesAndDetectsExactInPlaceChanges()
        {
            List<Vector3> points = new List<Vector3>();
            for (int i = 0; i < 4096; i++) points.Add(Vector3.zero);
            HairPointSpatialIndex index = new HairPointSpatialIndex();
            index.Rebuild(points);
            HairNearestPoint[] nearest = new HairNearestPoint[4];
            Assert.That(index.QueryNearest(Vector3.one, nearest), Is.EqualTo(4));
            for (int i = 0; i < 4; i++) Assert.That(nearest[i].index, Is.EqualTo(i));
            Assert.That(index.LastQueryVisitedNodes, Is.LessThan(100), "Coincident roots must not force a full scan.");
            points[0] = Vector3.right * 0.0000001f; // Smaller than Unity's approximate Vector3 equality.
            index.Rebuild(points);
            Assert.That(index.TryFindNearest(Vector3.zero, out int closest, out _), Is.True);
            Assert.That(closest, Is.EqualTo(1));
            points.Clear(); points.Add(Vector3.left); points.Add(Vector3.right); points.Add(new Vector3(float.NaN, 0f, 0f));
            index.Rebuild(points);
            Assert.That(index.QueryNearest(Vector3.zero, nearest), Is.EqualTo(2));
            Assert.That(nearest[0].index, Is.EqualTo(0));
            points.Reverse(); index.Rebuild(points);
            Assert.That(index.QueryNearest(Vector3.zero, nearest), Is.EqualTo(2));
            Assert.That(nearest[0].index, Is.EqualTo(1));
            index.Clear();
            Assert.That(index.TryFindNearest(Vector3.zero, out closest, out _), Is.False);
            Assert.That(closest, Is.EqualTo(-1));
            index.Rebuild(points);
            Assert.That(index.QueryNearest(new Vector3(float.PositiveInfinity, 0f, 0f), nearest), Is.Zero);
            Assert.That(index.QueryNearest(Vector3.zero, System.Array.Empty<HairNearestPoint>()), Is.Zero);
        }

        [Test]
        public void DensePointQueriesAndResamplingAllocateNothingAfterWarmup()
        {
            HairPointSpatialIndex index = new HairPointSpatialIndex();
            Vector3[] roots = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            index.Rebuild(roots);
            List<HairCurvePoint> source = HairCurveUtility.Resample(new[]
            { new HairCurvePoint(Vector3.zero, 0.1f, 0f), new HairCurvePoint(Vector3.up, 0f, 50f) }, 32);
            List<HairCurvePoint> destination = new List<HairCurvePoint>();
            float[] cumulative = null;
            HairNearestPoint[] nearest = new HairNearestPoint[4];
            System.Action operation = () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    index.Rebuild(roots);
                    index.QueryNearest(Vector3.one * 0.3f, nearest);
                    HairCurveUtility.ResampleInto(source, 12, destination, ref cumulative);
                }
            };
            operation();
            System.Action allocate = () => System.GC.KeepAlive(new byte[4096]);
            allocate();
            Assert.That(CountManagedAllocations(allocate), Is.GreaterThan(0), "Verify the allocation recorder before asserting zero.");
            Assert.That(CountManagedAllocations(operation), Is.Zero);
        }

        [Test]
        public void ReusedResamplingHandlesShrinkingAndDegenerateCurves()
        {
            List<HairCurvePoint> destination = new List<HairCurvePoint>();
            float[] cumulative = null;
            HairCurvePoint a = new HairCurvePoint(Vector3.zero, 0.1f, 15f);
            HairCurvePoint b = new HairCurvePoint(Vector3.up, 0.02f, 30f);
            List<HairCurvePoint> longSource = HairCurveUtility.Resample(new[] { a, b }, 64);
            HairCurveUtility.ResampleInto(longSource, 64, destination, ref cumulative);
            float[] retained = cumulative;
            HairCurveUtility.ResampleInto(new[] { a, b }, 3, destination, ref cumulative);
            Assert.That(cumulative, Is.SameAs(retained));
            Assert.That(destination.Count, Is.EqualTo(3));
            Assert.That(destination[1].position.y, Is.EqualTo(0.5f));
            Assert.That(destination[2].position, Is.EqualTo(b.position));
            HairCurveUtility.ResampleInto(new[] { a, a }, 9, destination, ref cumulative);
            Assert.That(destination.Count, Is.EqualTo(9));
            foreach (HairCurvePoint point in destination) Assert.That(point.position, Is.EqualTo(a.position));
            HairCurveUtility.ResampleInto(null, 32, destination, ref cumulative);
            Assert.That(destination.Count, Is.EqualTo(2));
            Assert.Throws<System.ArgumentException>(() => HairCurveUtility.ResampleInto(destination, 4, destination, ref cumulative));
        }

        [TestCase(HairGuideInterpolationMode.WeightedNearest)]
        [TestCase(HairGuideInterpolationMode.Nearest)]
        [TestCase(HairGuideInterpolationMode.ExplicitParent)]
        [TestCase(HairGuideInterpolationMode.ClumpParent)]
        public void ReusedEvaluationTracksGuideEditsFiltersAndGroupChanges(HairGuideInterpolationMode mode)
        {
            PopulateDenseProfileGuides(80, 4, 8);
            HairGroup group = groom.Groups[0];
            group.children.interpolation = mode;
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            HairEvaluationResult retained = HairGroomEvaluator.Evaluate(groom, options, workspace);
            string retainedFingerprint = CurveFingerprint(retained);
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options, workspace)), Is.EqualTo(retainedFingerprint));
            for (int pass = 0; pass < 5; pass++)
            {
                foreach (HairGuidePoint point in group.guides[pass].points) point.position += Vector3.right * 0.1f;
                group.guides.Reverse();
                group.guides.RemoveAt(group.guides.Count - 1);
                group.guides[0].enabled = false;
                options.includedGuideIds = pass % 2 == 0 ? new HashSet<string> { group.guides[1].Id, group.guides[4].Id, group.guides[7].Id } : null;
                options.previewSampleCount = pass % 2 == 0 ? 3 : 8;
                HairEvaluationResult fresh = HairGroomEvaluator.Evaluate(groom, options);
                HairEvaluationResult reused = HairGroomEvaluator.Evaluate(groom, options, workspace);
                Assert.That(CurveFingerprint(reused), Is.EqualTo(CurveFingerprint(fresh)));
                Assert.That(CurveFingerprint(retained), Is.EqualTo(retainedFingerprint));
            }
            options.includedGuideIds = null;
            HairGroup second = new HairGroup { profile = profile };
            second.children.childrenPerGuide = 3;
            second.guides.Add(CreateGuide("Other group", 246, Vector3.right));
            groom.Groups.Add(second);
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options, workspace)),
                Is.EqualTo(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options))));
            workspace.Clear();
            Assert.That(CurveFingerprint(retained), Is.EqualTo(retainedFingerprint));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void IndexedSurfaceProjectionMatchesLegacyAndRefreshesPositionsAndNormals(bool hasNormals)
        {
            PopulateDenseProfileGuides(16, 2, 8);
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false, includeChildren = false };
            HairGroup group = groom.Groups[0];
            HairModifierSettings projection = new HairModifierSettings { type = HairModifierType.SurfaceProjection,
                domain = HairModifierDomain.GuidesAndChildren, amount = 0.03f, weight = 0.65f };
            group.modifiers.Add(projection);
            for (int pass = 0; pass < 3; pass++)
            {
                Vector3[] vertices = sourceMesh.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] += Vector3.up * 0.005f;
                sourceMesh.vertices = vertices;
                Vector3 normal = pass % 2 == 0 ? Vector3.forward : Vector3.right;
                sourceMesh.normals = hasNormals ? new[] { normal, normal, normal } : System.Array.Empty<Vector3>();
                options.applyModifiers = false;
                HairEvaluationResult reference = HairGroomEvaluator.Evaluate(groom, options);
                foreach (HairEvaluatedCurve curve in reference.curves)
                    for (int p = 1; p < curve.points.Count; p++)
                    {
                        HairCurvePoint point = curve.points[p];
                        int closest = 0; float square = float.MaxValue;
                        for (int v = 0; v < vertices.Length; v++)
                        {
                            float distance = (vertices[v] - point.position).sqrMagnitude;
                            if (distance >= square) continue;
                            square = distance; closest = v;
                        }
                        Vector3 target = vertices[closest] + (hasNormals ? normal : curve.rootNormal).normalized * projection.amount;
                        float t = p / (curve.points.Count - 1f);
                        point.position = Vector3.Lerp(point.position, target, Mathf.Clamp01(projection.rootToTip.Evaluate(t) * projection.weight));
                        curve.points[p] = point;
                    }
                options.applyModifiers = true;
                Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options, workspace)), Is.EqualTo(CurveFingerprint(reference)));
            }
            options.includeChildren = true;
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options, workspace)),
                Is.EqualTo(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options))));
        }

        [Test]
        public void ReusedSourceMeshRefreshesAnchorTopologyAndRootMaps()
        {
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            HairGroup group = groom.Groups[0];
            group.children.childrenPerGuide = 8;
            HairGrowthMap map = new HairGrowthMap { kind = HairMapKind.ChildCount, values = new[] { 0f, 0.25f, 1f } };
            group.maps.Add(map);
            HairEvaluationResult first = HairGroomEvaluator.Evaluate(groom, null, workspace);
            sourceMesh.triangles = new[] { 2, 1, 0 };
            sourceMesh.normals = new[] { Vector3.forward, Vector3.right, Vector3.up };
            HairEvaluationResult second = HairGroomEvaluator.Evaluate(groom, null, workspace);
            HairEvaluationResult fresh = HairGroomEvaluator.Evaluate(groom);
            Assert.That(CurveFingerprint(second), Is.EqualTo(CurveFingerprint(fresh)));
            Assert.That(second.CardCount, Is.Not.EqualTo(first.CardCount));
            Assert.That(second.evaluatedGuides[0].points[0].position, Is.Not.EqualTo(first.evaluatedGuides[0].points[0].position));
            groom.SetSource(null, string.Empty);
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, null, workspace)),
                Is.EqualTo(CurveFingerprint(HairGroomEvaluator.Evaluate(groom))));
        }

        [TestCase(HairCardShape.Ribbon)]
        [TestCase(HairCardShape.TaperedTube)]
        public void ReusedMeshBuffersPreserveGeometryMetadataAndIndependentResults(HairCardShape shape)
        {
            PopulateDenseProfileGuides(16, 3, 12);
            profile.Configure(shape, 0.04f, 0.001f, 12, 6, true);
            HairMeshBuildWorkspace workspace = new HairMeshBuildWorkspace();
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom, options);
            using (HairCardMeshBuildResult retained = HairCardMeshGenerator.Build(evaluation, "Retained", true, workspace))
            {
                string fingerprint = MeshFingerprint(retained.mesh);
                Vector2[] retainedLocalUvs = retained.localUvs.ToArray();
                for (int pass = 0; pass < 3; pass++)
                {
                    options.previewSampleCount = pass % 2 == 0 ? 3 : 8;
                    groom.Groups[0].guides.RemoveAt(0);
                    evaluation = HairGroomEvaluator.Evaluate(groom, options);
                    using (HairCardMeshBuildResult fresh = HairCardMeshGenerator.Build(evaluation, "Fresh", true))
                    using (HairCardMeshBuildResult reused = HairCardMeshGenerator.Build(evaluation, "Reused", true, workspace))
                    {
                        Assert.That(MeshFingerprint(reused.mesh), Is.EqualTo(MeshFingerprint(fresh.mesh)));
                        Assert.That(reused.mesh, Is.Not.SameAs(retained.mesh));
                        Assert.That(reused.cards.Count, Is.EqualTo(fresh.cards.Count));
                        Assert.That(reused.localUvs, Is.EqualTo(fresh.localUvs));
                        Assert.That(reused.materials, Is.EqualTo(fresh.materials));
                        for (int i = 0; i < fresh.cards.Count; i++)
                        {
                            Assert.That(reused.cards[i].vertexStart, Is.EqualTo(fresh.cards[i].vertexStart));
                            Assert.That(reused.cards[i].triangleStart, Is.EqualTo(fresh.cards[i].triangleStart));
                        }
                    }
                }
                workspace.Clear();
                Assert.That(MeshFingerprint(retained.mesh), Is.EqualTo(fingerprint));
                Assert.That(retained.localUvs, Is.EqualTo(retainedLocalUvs));
                using (HairCardMeshBuildResult empty = HairCardMeshGenerator.Build(null, "Empty", true, workspace))
                    Assert.That(empty.mesh.vertexCount, Is.Zero);
            }
        }

        [Test]
        public void GuidePreviewRetainsBuffersAndPrunesRemovedGuides()
        {
            PopulateDenseProfileGuides(8, 0, 8);
            HairCardStage stage = CreateEditingStage();
            try
            {
                Dictionary<string, Vector3[]> polylines = (Dictionary<string, Vector3[]>)typeof(HairCardStage)
                    .GetField("displayGuidePolylines", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage);
                IDictionary brushes = (IDictionary)typeof(HairCardStage).GetField("retainedBrushEntries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(stage);
                HairGuide guide = groom.Groups[0].guides[0];
                Vector3[] retained = polylines[guide.Id];
                object retainedBrush = brushes[guide.Id];
                guide.points[1].position += Vector3.right * 0.01f;
                stage.RebuildNow();
                Assert.That(polylines[guide.Id], Is.SameAs(retained));
                Assert.That(brushes[guide.Id], Is.SameAs(retainedBrush));
                groom.Groups[0].guides.RemoveAt(0);
                stage.RebuildNow();
                Assert.That(polylines.ContainsKey(guide.Id), Is.False);
                Assert.That(brushes.Contains(guide.Id), Is.False);
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void ReusedMeshBucketsRefreshMaterialsAndUvsWithoutKeepingOldAssignments()
        {
            HairAtlasProfileAsset firstAtlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasProfileAsset secondAtlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material firstMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            Material secondMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
            HairMeshBuildWorkspace workspace = new HairMeshBuildWorkspace();
            try
            {
                firstAtlas.material = firstMaterial;
                secondAtlas.material = secondMaterial;
                PopulateDenseProfileGuides(6, 0, 8);
                HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
                evaluation.curves[0].atlas = firstAtlas;
                evaluation.curves[1].atlas = secondAtlas;
                using (HairCardMeshBuildResult first = HairCardMeshGenerator.Build(evaluation, "First", true, workspace))
                {
                    Assert.That(first.materials, Is.EqualTo(new[] { firstMaterial, secondMaterial, null }));
                    for (int i = 0; i < 3; i++) Assert.That(first.cards[i].submesh, Is.EqualTo(i));
                    evaluation.curves[0].atlas = null;
                    evaluation.curves[1].atlas = firstAtlas;
                    using (HairCardMeshBuildResult fresh = HairCardMeshGenerator.Build(evaluation, "Fresh", true))
                    using (HairCardMeshBuildResult second = HairCardMeshGenerator.Build(evaluation, "Second", true, workspace))
                    {
                        Assert.That(second.materials, Is.EqualTo(new[] { null, firstMaterial }));
                        Assert.That(MeshFingerprint(second.mesh), Is.EqualTo(MeshFingerprint(fresh.mesh)));
                        Assert.That(second.mesh.subMeshCount, Is.EqualTo(2));
                        for (int i = 0; i < 2; i++) Assert.That(second.mesh.GetTriangles(i), Is.EqualTo(fresh.mesh.GetTriangles(i)));
                        Assert.That(first.materials[1], Is.SameAs(secondMaterial));
                    }
                }
            }
            finally
            {
                workspace.Clear();
                Object.DestroyImmediate(firstAtlas); Object.DestroyImmediate(secondAtlas);
                Object.DestroyImmediate(firstMaterial); Object.DestroyImmediate(secondMaterial);
            }
        }

        private static long CountManagedAllocations(System.Action operation)
        {
            using (ProfilerRecorder recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                operation();
                recorder.Stop();
                Assert.That(recorder.Valid, Is.True);
                return recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            }
        }

        [TestCase(1000, 8)]
        [TestCase(5000, 8)]
        [Category("HairDenseProfile")]
        [Timeout(600000)]
        public void ProfileDenseChildEvaluation(int guideCount, int children)
        {
            PopulateDenseProfileGuides(guideCount, children, 8);
            HairEvaluationResult result = null;
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            ProfileOperation($"children-{guideCount}x{children}", () => result = HairGroomEvaluator.Evaluate(groom, options),
                () => CurveFingerprint(result));
            Assert.That(result.CardCount, Is.EqualTo(guideCount * (children + 1)));
        }

        [Test]
        [Category("HairDenseProfile")]
        public void ProfileDenseSourceProjection()
        {
            const int vertexCount = 50000;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            System.Random random = new System.Random(713);
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() * 0.1f, (float)random.NextDouble() - 0.5f);
                normals[i] = new Vector3(vertices[i].x * 0.1f, 1f, vertices[i].z * 0.1f).normalized;
            }
            sourceMesh.Clear(); sourceMesh.vertices = vertices; sourceMesh.normals = normals;
            sourceMesh.triangles = new[] { 0, 1, 2 };
            groom.SetSource(sourceMesh, "mesh:test");
            PopulateDenseProfileGuides(128, 0, 8);
            HairModifierSettings projection = new HairModifierSettings { type = HairModifierType.SurfaceProjection, domain = HairModifierDomain.GuidesAndChildren, amount = 0.002f, weight = 0.65f };
            groom.Groups[0].modifiers.Add(projection);
            groom.EnsureIntegrity();
            HairEvaluationResult result = null;
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            ProfileOperation("projection-128x8-on-50000", () => result = HairGroomEvaluator.Evaluate(groom, options), () => CurveFingerprint(result));
            Assert.That(result.evaluatedGuides.Count, Is.EqualTo(128));
        }

        [TestCase(HairCardShape.Ribbon)]
        [TestCase(HairCardShape.TaperedTube)]
        [Category("HairDenseProfile")]
        public void ProfileDenseCardMeshing(HairCardShape shape)
        {
            PopulateDenseProfileGuides(1000, 4, 12);
            profile.Configure(shape, 0.04f, 0.001f, 12, 6, true);
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false });
            HairCardMeshBuildResult build = null;
            try
            {
                ProfileOperation("mesh-5000x12-" + shape,
                    () => { build?.Dispose(); build = HairCardMeshGenerator.Build(evaluation); },
                    () => MeshFingerprint(build.mesh));
                Assert.That(build.cardCount, Is.EqualTo(5000));
            }
            finally { build?.Dispose(); }
        }

        private void PopulateDenseProfileGuides(int guideCount, int children, int samples)
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear(); group.modifiers.Clear();
            group.children.childrenPerGuide = children;
            group.children.interpolation = HairGuideInterpolationMode.WeightedNearest;
            group.children.rootSpread = 0.02f;
            group.children.seed = 41;
            groom.Lods[0].samplesPerCard = samples;
            System.Random random = new System.Random(214);
            for (int i = 0; i < guideCount; i++)
            {
                Vector3 root = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() * 0.02f, (float)random.NextDouble() - 0.5f);
                HairGuide guide = CreateLinearGuide("Dense " + i, 13 + i * 17, root, Vector3.up * 0.3f);
                guide.lodImportance = 1f;
                guide.points.Clear();
                for (int point = 0; point < samples; point++)
                {
                    float t = point / (samples - 1f);
                    guide.points.Add(new HairGuidePoint { position = root + new Vector3(Mathf.Sin(i * 0.31f + t) * t * 0.04f, t * 0.3f, t * t * 0.025f), width = Mathf.Lerp(0.04f, 0.001f, t), roll = i % 23 * t });
                }
                group.guides.Add(guide);
            }
            groom.EnsureIntegrity();
        }

        [TestCase(1000, 8)]
        [TestCase(5000, 8)]
        [Category("HairDenseCachedProfile")]
        public void ProfileCachedDenseChildEvaluation(int guideCount, int children)
        {
            PopulateDenseProfileGuides(guideCount, children, 8);
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            HairEvaluationResult result = null;
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            ProfileOperation($"cached-children-{guideCount}x{children}",
                () => result = HairGroomEvaluator.Evaluate(groom, options, workspace), () => CurveFingerprint(result));
            Assert.That(result.CardCount, Is.EqualTo(guideCount * (children + 1)));
        }

        [Test]
        [Category("HairDenseCachedProfile")]
        public void ProfileCachedDenseSourceProjection()
        {
            // Same fixture as the uncached/baseline test, with a retained preview workspace.
            const int vertexCount = 50000;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            System.Random random = new System.Random(713);
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() * 0.1f, (float)random.NextDouble() - 0.5f);
                normals[i] = new Vector3(vertices[i].x * 0.1f, 1f, vertices[i].z * 0.1f).normalized;
            }
            sourceMesh.Clear(); sourceMesh.vertices = vertices; sourceMesh.normals = normals;
            sourceMesh.triangles = new[] { 0, 1, 2 };
            groom.SetSource(sourceMesh, "mesh:test");
            PopulateDenseProfileGuides(128, 0, 8);
            groom.Groups[0].modifiers.Add(new HairModifierSettings { type = HairModifierType.SurfaceProjection,
                domain = HairModifierDomain.GuidesAndChildren, amount = 0.002f, weight = 0.65f });
            groom.EnsureIntegrity();
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            HairEvaluationResult result = null;
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            ProfileOperation("cached-projection-128x8-on-50000", () => result = HairGroomEvaluator.Evaluate(groom, options, workspace),
                () => CurveFingerprint(result));
        }

        [TestCase(HairCardShape.Ribbon, false)]
        [TestCase(HairCardShape.TaperedTube, false)]
        [TestCase(HairCardShape.Ribbon, true)]
        [Category("HairDenseCachedProfile")]
        public void ProfileCachedDenseCardMeshing(HairCardShape shape, bool editingMetadata)
        {
            PopulateDenseProfileGuides(1000, 4, 12);
            profile.Configure(shape, 0.04f, 0.001f, 12, 6, true);
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false });
            HairMeshBuildWorkspace workspace = new HairMeshBuildWorkspace();
            HairCardMeshBuildResult build = null;
            try
            {
                ProfileOperation("cached-mesh-5000x12-" + shape + (editingMetadata ? "-editing" : string.Empty),
                    () => { build?.Dispose(); build = HairCardMeshGenerator.Build(evaluation, "Cached", editingMetadata, workspace); },
                    () => MeshFingerprint(build.mesh));
                Assert.That(build.cardCount, Is.EqualTo(5000));
            }
            finally { build?.Dispose(); }
        }

        [TestCase(HairCardShape.Ribbon)]
        [TestCase(HairCardShape.TaperedTube)]
        public void CardVertexColorsHoldRootSegmentsThenFadeRgbaForGuidesAndChildren(HairCardShape shape)
        {
            profile.Configure(shape, 0.04f, 0.01f, 6, 6, true);
            groom.Lods[0].samplesPerCard = 6;
            groom.Groups[0].children.childrenPerGuide = 3;
            Color root = new Color(0.2f, 0.4f, 0.6f, 0f), tip = new Color(0.8f, 0.6f, 0.1f, 1f);
            profile.ConfigureVertexColors(true, root, tip, 2);
            HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
            using HairCardMeshBuildResult build = HairCardMeshGenerator.Build(evaluation, "Gradient", true);
            Color[] colors = build.mesh.colors;
            Assert.That(build.cards.Count, Is.EqualTo(4));
            foreach (HairCardSpan card in build.cards)
            {
                int columns = card.vertexCount / 6;
                for (int row = 0; row < 6; row++)
                {
                    Color expected = Color.Lerp(root, tip, Mathf.Max(0f, row - 2) / 3f);
                    for (int column = 0; column < columns; column++)
                        Assert.That(colors[card.vertexStart + row * columns + column], Is.EqualTo(expected));
                }
            }
            profile.ConfigureVertexColors(false, root, tip, 2);
            HairCardMeshGenerator.Update(evaluation, "Gradient", build, new HairMeshBuildWorkspace());
            foreach (Color color in build.mesh.colors) Assert.That(color, Is.EqualTo(groom.Groups[0].color));
        }

        [TestCase(2, 0)]
        [TestCase(4, 2)]
        [TestCase(12, 10)]
        public void VertexColorHoldClampsToLeaveAFadeSegmentForEveryLod(int samples, int expectedHold)
        {
            Color root = new Color(0.1f, 0.4f, 0.9f, 0f), tip = new Color(0.9f, 0.4f, 0.1f, 1f);
            profile.ConfigureVertexColors(true, root, tip, 63);
            for (int row = 0; row <= expectedHold; row++)
                Assert.That(profile.EvaluateVertexColor(row, samples, Color.magenta), Is.EqualTo(root));
            Assert.That(profile.EvaluateVertexColor(samples - 1, samples, Color.magenta), Is.EqualTo(tip));
            profile.ConfigureVertexColors(true, root, tip, 0);
            for (int row = 0; row < samples; row++)
                Assert.That(profile.EvaluateVertexColor(row, samples, Color.magenta), Is.EqualTo(Color.Lerp(root, tip, row / (samples - 1f))));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SharedColorShaderParametersApplyToMaterialAndPreviewWithUndo(bool twoPasses)
        {
            SharedColorTable table = ScriptableObject.CreateInstance<SharedColorTable>();
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material material = new Material(Shader.Find("Standard"));
            Material secondPass = new Material(material);
            Texture2D texture = new Texture2D(2, 2);
            HairPreviewMaterialSet preview = new HairPreviewMaterialSet();
            HairCardMeshBuildResult build = new HairCardMeshBuildResult();
            try
            {
                Color beforeColor = material.color;
                float beforeGloss = material.GetFloat("_Glossiness");
                OverlayColorData selected = new OverlayColorData(3);
                Color applied = new Color(0.3f, 0.1f, 0.05f, 0.7f);
                selected.SetColorProperty("_Color", applied);
                selected.SetFloatProperty("_Glossiness", 0.27f);
                selected.SetTextureProperty("_MainTex", texture);
                selected.SetFloatProperty("_PropertyNotInTheShader", 0.42f);
                selected.PropertyBlock.shaderProperties.Add(null);
                table.colors = new[] { new OverlayColorData(3), selected };
                atlas.material = material; atlas.sharedColorTable = table; atlas.sharedColorIndex = 1;
                if (twoPasses) atlas.secondPassMaterial = secondPass;
                Assert.That(HairSharedColorUtility.ParameterCount(atlas), Is.EqualTo(twoPasses ? 6 : 3));
                Undo.IncrementCurrentGroup();
                Assert.That(HairSharedColorUtility.Apply(atlas), Is.EqualTo(twoPasses ? 6 : 3));
                Undo.FlushUndoRecordObjects();
                Assert.That(material.color, Is.EqualTo(applied));
                Assert.That(material.GetFloat("_Glossiness"), Is.EqualTo(0.27f));
                Assert.That(material.GetTexture("_MainTex"), Is.SameAs(texture));
                if (twoPasses)
                {
                    Assert.That(secondPass.color, Is.EqualTo(applied));
                    Assert.That(secondPass.GetFloat("_Glossiness"), Is.EqualTo(0.27f));
                    Assert.That(secondPass.GetTexture("_MainTex"), Is.SameAs(texture));
                }
                build.atlases.Add(atlas); build.materials.Add(material);
                Material shown = preview.Update(build)[0];
                Assert.That(shown.color, Is.EqualTo(applied));
                Assert.That(shown.GetFloat("_Glossiness"), Is.EqualTo(0.27f));
                Undo.PerformUndo();
                Assert.That(material.color, Is.EqualTo(beforeColor));
                Assert.That(material.GetFloat("_Glossiness"), Is.EqualTo(beforeGloss));
                Assert.That(secondPass.color, Is.EqualTo(beforeColor));
                atlas.secondPassMaterial = material;
                Assert.That(HairSharedColorUtility.ParameterCount(atlas), Is.EqualTo(3), "A shared first/second material is only edited once.");
                atlas.sharedColorIndex = 50;
                Assert.That(HairSharedColorUtility.Apply(atlas), Is.Zero);
                atlas.sharedColorIndex = 0;
                Assert.That(HairSharedColorUtility.Apply(atlas), Is.Zero, "Channel colors must not be guessed as shader parameters.");
                atlas.sharedColorTable = null;
                Assert.That(HairSharedColorUtility.SelectedColor(atlas), Is.Null);
            }
            finally
            {
                preview.Dispose(); build.Dispose(); Undo.ClearUndo(material);
                Undo.ClearUndo(secondPass); Object.DestroyImmediate(secondPass);
                Object.DestroyImmediate(material); Object.DestroyImmediate(texture);
                Object.DestroyImmediate(table); Object.DestroyImmediate(atlas);
            }
        }

        [Test]
        public void UnifiedStackMigrationPreservesGuidesChildrenAndModifierIds()
        {
            HairGroup group = groom.Groups[0];
            group.children.childrenPerGuide = 3;
            HairSculptLayer sculpt = HairGroomCommands.AddSculptLayer(groom, group);
            sculpt.deltas.Add(new HairGuideDelta { guideId = group.guides[0].Id,
                positionOffsets = new[] { Vector3.zero, Vector3.right * 0.02f, Vector3.forward * 0.03f } });
            HairModifierSettings flow = HairGroomCommands.AddModifier(groom, group, HairModifierType.FlowAlign);
            flow.vector = Vector3.right; flow.amount = 0.3f;
            HairModifierSettings noise = HairGroomCommands.AddModifier(groom, group, HairModifierType.Noise);
            noise.amount = 0.01f; noise.domain = HairModifierDomain.Children;
            string before = CurveFingerprint(HairGroomEvaluator.Evaluate(groom));
            HairSculptLayer imported = HairGroomCommands.ImportLegacyModifiers(groom, group);
            Assert.That(group.modifiers, Is.Empty);
            Assert.That(group.sculptLayers[^1], Is.SameAs(imported));
            Assert.That(imported.modifiers[0].Id, Is.EqualTo(flow.Id));
            Assert.That(imported.modifiers[1].Id, Is.EqualTo(noise.Id));
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom)), Is.EqualTo(before));
            Assert.That(HairGroomCommands.ImportLegacyModifiers(groom, group), Is.Null);
        }

        [TestCase(false, 0f)]
        [TestCase(false, 1f)]
        [TestCase(true, 0.3f)]
        public void GravityModifierMatchesHoldSolverWithRootRules(bool collision, float rootInfluence)
        {
            sourceMesh.triangles = new[] { 0, 2, 1 };
            HairGroup group = groom.Groups[0];
            HairGuide guide = group.guides[0];
            guide.points[1].position = new Vector3(0.1f, 0.05f, 0f);
            guide.points[2].position = new Vector3(0.2f, 0.08f, 0.02f);
            guide.points[1].stiffness = 0.7f;
            HairCardStage stage = CreateEditingStage();
            HairEvaluatedCurve held;
            try
            {
                stage.GravityCollision = collision; stage.GravityClearance = 0.002f;
                stage.GravitySeparation = 0.2f; stage.BrushRootInfluence = rootInfluence; stage.GravityStrength = 2.5f;
                stage.SetGravityHeld(true);
                for (int i = 0; i < 6; i++) InvokeStageMethod(stage, "ApplyGravityStep", 1f / 60f);
                stage.SetGravityHeld(false); stage.RebuildNow();
                held = stage.Evaluation.evaluatedGuides[0].Clone();
            }
            finally { DestroyEditingStage(stage); }
            group.sculptLayers.Clear();
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Gravity);
            modifier.amount = 0.1f; modifier.gravityCollision = collision; modifier.rootInfluence = rootInfluence;
            modifier.gravityClearance = 0.002f; modifier.gravitySeparation = 0.2f; modifier.gravityStrength = 2.5f;
            HairEvaluatedCurve procedural = HairGroomEvaluator.Evaluate(groom).evaluatedGuides[0];
            for (int i = 0; i < procedural.points.Count; i++)
                Assert.That(Vector3.Distance(procedural.points[i].position, held.points[i].position), Is.LessThan(2e-5f), $"Point {i}");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public void GravityModifierConvertsWorldAndLocalDirectionsThroughObjectAndGuidePose(bool localOverride, bool mirroredScale)
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear(); group.guides.Add(CreateLinearGuide("Space", 3, new Vector3(0.1f, 0.2f, 0.3f), Vector3.up * 0.3f));
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Gravity);
            modifier.amount = 0.5f; modifier.gravityStrength = 100f; modifier.gravityCollision = false;
            modifier.gravitySeparation = 0f; modifier.useWorldGravity = !localOverride; modifier.gravityDirection = Vector3.back;
            Matrix4x4 objectMatrix = Matrix4x4.TRS(new Vector3(8f, 3f, 10f), Quaternion.Euler(17f, 21f, 90f), new Vector3(mirroredScale ? -2f : 2f, 0.5f, 3f));
            Matrix4x4 pose = Matrix4x4.TRS(Vector3.right * 0.1f, Quaternion.Euler(-30f, 15f, 5f), new Vector3(1f, 1.2f, 0.8f));
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false, sourceToWorld = objectMatrix,
                guideToSourcePose = _ => pose, worldGravity = new Vector3(0f, -9.81f, 0f) };
            Matrix4x4 toWorld = objectMatrix * pose;
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            HairEvaluationResult result = new HairEvaluationResult();
            HairGroomEvaluator.EvaluateInto(groom, options, workspace, result);
            HairEvaluatedCurve curve = result.evaluatedGuides[0];
            Assert.That(curve.points[0].position, Is.EqualTo(group.guides[0].points[0].position));
            Vector3 expected = localOverride ? toWorld.MultiplyVector(Vector3.back).normalized : Vector3.down;
            for (int i = 1; i < curve.points.Count; i++)
            {
                Vector3 segment = curve.points[i].position - curve.points[i - 1].position;
                Assert.That(Vector3.Angle(toWorld.MultiplyVector(segment), expected), Is.LessThan(0.1f));
                Assert.That(segment.magnitude, Is.EqualTo(0.15f).Within(2e-5f));
            }
            string fingerprint = CurveFingerprint(result);
            for (int i = 0; i < 3; i++)
                Assert.That(CurveFingerprint(HairGroomEvaluator.EvaluateInto(groom, options, workspace, result)), Is.EqualTo(fingerprint));
            options.sourceToWorld = Matrix4x4.Translate(Vector3.one * 50f) * objectMatrix;
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options)), Is.EqualTo(fingerprint), "Translation must not enter direction conversion.");
            workspace.Clear();
        }

        [Test]
        public void GravityRuntimeComponentUsesItsHierarchyTransform()
        {
            HairModifierSettings gravity = HairGroomCommands.AddModifier(groom, groom.Groups[0], HairModifierType.Gravity);
            gravity.amount = 0.15f; gravity.gravityCollision = false;
            GameObject parent = new GameObject("Rotated parent"), child = new GameObject("Hair");
            try
            {
                parent.transform.rotation = Quaternion.Euler(20f, 10f, 90f);
                parent.transform.localScale = new Vector3(2f, 0.7f, 1.5f);
                child.transform.SetParent(parent.transform, false);
                child.transform.localRotation = Quaternion.Euler(15f, 30f, 0f);
                HairGroomRuntimeComponent component = child.AddComponent<HairGroomRuntimeComponent>();
                component.SetGroom(groom);
                var options = new HairEvaluationOptions { sourceToWorld = child.transform.localToWorldMatrix };
                Assert.That(CurveFingerprint(component.CurrentEvaluation), Is.EqualTo(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options))));
                using (var generated = HairGroomRuntimeAPI.Generate(groom, sourceToWorld: child.transform.localToWorldMatrix))
                    Assert.That(CurveFingerprint(generated.Evaluation), Is.EqualTo(CurveFingerprint(component.CurrentEvaluation)));
                Assert.That(CurveFingerprint(component.CurrentEvaluation), Is.Not.EqualTo(CurveFingerprint(HairGroomEvaluator.Evaluate(groom))));
            }
            finally { Object.DestroyImmediate(child); Object.DestroyImmediate(parent); }
        }

        [Test]
        public void GravityModifierProtectsFrozenAnchorsAndRootInfluenceChangesBaseResponse()
        {
            HairGroup group = groom.Groups[0];
            HairModifierSettings gravity = HairGroomCommands.AddModifier(groom, group, HairModifierType.Gravity);
            gravity.gravityCollision = false; gravity.gravitySeparation = 0f; gravity.amount = 0.2f;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            gravity.rootInfluence = 0f;
            Vector3 protectedBase = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0].points[1].position;
            gravity.rootInfluence = 1f;
            Vector3 mobileBase = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0].points[1].position;
            Assert.That(Vector3.Distance(protectedBase, group.guides[0].points[1].position), Is.LessThan(Vector3.Distance(mobileBase, group.guides[0].points[1].position)));
            group.guides[0].points[^1].freeze = 1f;
            HairEvaluatedCurve pinned = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(pinned.points[^1].position, Is.EqualTo(group.guides[0].points[^1].position));
            for (int i = 1; i < pinned.points.Count; i++)
                Assert.That(Vector3.Distance(pinned.points[i - 1].position, pinned.points[i].position),
                    Is.EqualTo(Vector3.Distance(group.guides[0].points[i - 1].position, group.guides[0].points[i].position)).Within(1e-5f));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void GravityZeroControlsDoNotMoveOrProjectHair(int control)
        {
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            string baseline = CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options));
            HairModifierSettings gravity = HairGroomCommands.AddModifier(groom, groom.Groups[0], HairModifierType.Gravity);
            gravity.gravityCollision = true;
            if (control == 0) options.worldGravity = Vector3.zero;
            if (control == 1) gravity.amount = 0f;
            if (control == 2) gravity.rootToTip = AnimationCurve.Constant(0f, 1f, 0f);
            if (control == 3) options.sourceToWorld = Matrix4x4.Scale(new Vector3(1f, 0f, 1f));
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options)), Is.EqualTo(baseline));
        }

        [Test]
        public void ModifierMetadataSurvivesResamplingChildBlendingAndSerialization()
        {
            HairGroup group = groom.Groups[0]; group.children.childrenPerGuide = 2;
            group.guides[0].points[0].stiffness = 0.2f; group.guides[0].points[^1].stiffness = 0.8f;
            group.guides[0].points[^1].freeze = 1f;
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Resample).amount = 12f;
            HairEvaluationResult result = HairGroomEvaluator.Evaluate(groom);
            Assert.That(result.evaluatedGuides[0].points[^1].stiffness, Is.EqualTo(0.8f).Within(1e-6f));
            foreach (HairEvaluatedCurve curve in result.curves)
            {
                Assert.That(curve.points[^1].freeze, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(curve.points[^1].stiffness, Is.EqualTo(0.8f).Within(1e-6f));
            }
            HairModifierSettings gravity = HairGroomCommands.AddModifier(groom, group, HairModifierType.Gravity);
            gravity.rootInfluence = 0.3f; gravity.gravityStrength = 6f; gravity.gravitySeparation = 0.4f;
            gravity.gravityCollision = false; gravity.gravityClearance = 0.012f; gravity.useWorldGravity = false;
            gravity.gravityDirection = Vector3.back;
            HairModifierSettings saved = JsonUtility.FromJson<HairModifierSettings>(JsonUtility.ToJson(gravity));
            Assert.That(saved.rootInfluence, Is.EqualTo(0.3f)); Assert.That(saved.gravityStrength, Is.EqualTo(6f));
            Assert.That(saved.gravitySeparation, Is.EqualTo(0.4f)); Assert.That(saved.gravityCollision, Is.False);
            Assert.That(saved.gravityClearance, Is.EqualTo(0.012f)); Assert.That(saved.useWorldGravity, Is.False);
            Assert.That(saved.gravityDirection, Is.EqualTo(Vector3.back));
        }

        [Test]
        public void EveryModifierRejectsNonFiniteAmountWithoutCorruptingCurves()
        {
            string baseline = CurveFingerprint(HairGroomEvaluator.Evaluate(groom));
            foreach (HairModifierType type in System.Enum.GetValues(typeof(HairModifierType)))
            {
                groom.Groups[0].modifiers.Clear();
                HairGroomCommands.AddModifier(groom, groom.Groups[0], type).amount = float.NaN;
                Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom)), Is.EqualTo(baseline), type.ToString());
            }
        }

        [Test]
        public void CollisionModifierSolvesClearanceAndUsesBlendIndependentlyOfOffset()
        {
            HairGroup group = groom.Groups[0];
            HairHelper helper = HairGroomCommands.AddHelper(groom, HairHelperType.Sphere, Vector3.up * 0.15f);
            helper.radius = 0.07f;
            HairModifierSettings collision = HairGroomCommands.AddModifier(groom, group, HairModifierType.Collision);
            collision.helperId = helper.Id; collision.amount = 0f;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluatedCurve full = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(Vector3.Distance(full.points[1].position, helper.position), Is.GreaterThanOrEqualTo(helper.radius - 0.0001f));
            collision.weight = 0.25f;
            HairEvaluatedCurve partial = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(Vector3.Distance(partial.points[1].position, group.guides[0].points[1].position),
                Is.LessThan(Vector3.Distance(full.points[1].position, group.guides[0].points[1].position)));
        }

        [TestCase(HairModifierType.Smooth)]
        [TestCase(HairModifierType.FlowAlign)]
        [TestCase(HairModifierType.Lift)]
        [TestCase(HairModifierType.Clump)]
        [TestCase(HairModifierType.Part)]
        [TestCase(HairModifierType.Curl)]
        [TestCase(HairModifierType.Wave)]
        [TestCase(HairModifierType.Noise)]
        [TestCase(HairModifierType.HelperFollow)]
        [TestCase(HairModifierType.Collision)]
        [TestCase(HairModifierType.PushOut)]
        public void BendingModifiersKeepRootsAndSegmentLengths(HairModifierType type)
        {
            HairGroup group = groom.Groups[0];
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, type);
            modifier.amount = type == HairModifierType.Collision || type == HairModifierType.PushOut ? 0.002f : 0.3f;
            modifier.vector = new Vector3(2f, 0.7f, 0.3f);
            HairHelper helper = HairGroomCommands.AddHelper(groom, type == HairModifierType.HelperFollow ? HairHelperType.CurveRail : HairHelperType.Sphere, Vector3.up * 0.15f);
            helper.points.Clear(); helper.points.Add(Vector3.zero); helper.points.Add(Vector3.right * 0.15f); helper.points.Add(Vector3.right * 0.3f);
            helper.radius = 0.1f; modifier.helperId = helper.Id;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluatedCurve curve = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(curve.points[0].position, Is.EqualTo(group.guides[0].points[0].position));
            for (int i = 1; i < curve.points.Count; i++)
                Assert.That(Vector3.Distance(curve.points[i - 1].position, curve.points[i].position),
                    Is.EqualTo(Vector3.Distance(group.guides[0].points[i - 1].position, group.guides[0].points[i].position)).Within(1e-5f));
            modifier.rootToTip = AnimationCurve.Constant(0f, 1f, 0f);
            string zeroRamp = CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options));
            modifier.enabled = false;
            Assert.That(zeroRamp, Is.EqualTo(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options))));
        }

        [TestCase(HairModifierType.Length)]
        [TestCase(HairModifierType.Width)]
        [TestCase(HairModifierType.Mirror)]
        [TestCase(HairModifierType.Gravity)]
        [TestCase(HairModifierType.Curl)]
        [TestCase(HairModifierType.Twist)]
        public void CombinedModifiersApplyOnceThroughChildInheritance(HairModifierType type)
        {
            HairGroup group = groom.Groups[0]; group.children.childrenPerGuide = 3;
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, type);
            modifier.amount = type == HairModifierType.Length || type == HairModifierType.Width ? 2f : 0.2f;
            modifier.gravityCollision = false;
            modifier.domain = HairModifierDomain.Guides;
            string once = CurveFingerprint(HairGroomEvaluator.Evaluate(groom));
            modifier.domain = HairModifierDomain.GuidesAndChildren;
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom)), Is.EqualTo(once));
        }

        [TestCase(1f, 0.15f)]
        [TestCase(0.5f, 0.225f)]
        public void TrimByMeshKeepsRootSideAndBlendsCutLength(float weight, float expectedTip)
        {
            sourceMesh.vertices = new[] { new Vector3(-1f, 0.15f, -1f), new Vector3(1f, 0.15f, -1f), new Vector3(0f, 0.15f, 1f) };
            HairGroup group = groom.Groups[0]; group.guides.Clear();
            group.guides.Add(CreateLinearGuide("Trim", 3, Vector3.zero, Vector3.up * 0.3f));
            HairModifierSettings trim = HairGroomCommands.AddModifier(groom, group, HairModifierType.TrimByMesh);
            trim.weight = weight;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluatedCurve curve = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(curve.points[0].position, Is.EqualTo(Vector3.zero));
            Assert.That(curve.points[^1].position.y, Is.EqualTo(expectedTip).Within(1e-6f));
            group.guides[0].points[^1].freeze = 1f;
            Assert.That(HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0].points[^1].position.y, Is.EqualTo(0.3f));
        }

        [Test]
        public void UnifiedStackLayerOrderInterleavesSculptAndModifiers()
        {
            HairGroup group = groom.Groups[0];
            HairGuide guide = group.guides[0];
            HairSculptLayer scale = HairGroomCommands.AddSculptLayer(groom, group);
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Length, scale).amount = 2f;
            HairSculptLayer sculpt = HairGroomCommands.AddSculptLayer(groom, group);
            sculpt.deltas.Add(new HairGuideDelta { guideId = guide.Id,
                positionOffsets = new[] { Vector3.zero, Vector3.zero, Vector3.right * 0.1f } });
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            Vector3 root = guide.points[0].position, tip = guide.points[^1].position;
            Vector3 Expected(float offset) => root + (tip - root) * 2f + Vector3.right * offset;
            Assert.That(Vector3.Distance(HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0].points[^1].position, Expected(0.1f)), Is.LessThan(1e-6f));
            HairGroomCommands.EditStack(groom, group, scale.Id, true, 1);
            Assert.That(Vector3.Distance(HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0].points[^1].position, Expected(0.2f)), Is.LessThan(1e-6f));
        }

        [Test]
        public void UnifiedStackVisibilityOpacitySoloAndDisabledModifierAffectOwnedChildren()
        {
            HairGroup group = groom.Groups[0];
            group.children.childrenPerGuide = 2;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            string baseline = CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options));
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Length, layer);
            modifier.domain = HairModifierDomain.Children; modifier.amount = 2f;
            HairEvaluationResult full = HairGroomEvaluator.Evaluate(groom, options);
            Assert.That(CurveFingerprint(full), Is.Not.EqualTo(baseline));
            layer.opacity = 0.5f;
            HairEvaluationResult half = HairGroomEvaluator.Evaluate(groom, options);
            for (int i = 0; i < full.curves.Count; i++)
                Assert.That(half.curves[i].Length, Is.EqualTo(full.curves[i].Length * (full.curves[i].isChild ? 0.75f : 1f)).Within(1e-5f));
            Assert.That(modifier.weight, Is.EqualTo(1f), "Layer opacity must not mutate serialized modifier weights.");
            layer.visible = false;
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options)), Is.EqualTo(baseline));
            options.soloLayerId = layer.Id;
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options)), Is.EqualTo(CurveFingerprint(half)));
            layer.opacity = 0f;
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options)), Is.EqualTo(baseline));
            layer.opacity = 1f; modifier.enabled = false;
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options)), Is.EqualTo(baseline));
            modifier.enabled = true; options.applyModifiers = false;
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options)), Is.EqualTo(baseline));
        }

        [TestCase(HairSculptBlendMode.Additive)]
        [TestCase(HairSculptBlendMode.Override)]
        public void UnifiedStackResamplingBeforeSculptUsesNormalizedOffsets(HairSculptBlendMode blend)
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer resample = HairGroomCommands.AddSculptLayer(groom, group);
            HairGroomCommands.AddModifier(groom, group, HairModifierType.Resample, resample).amount = 12f;
            HairSculptLayer sculpt = HairGroomCommands.AddSculptLayer(groom, group);
            sculpt.blendMode = blend;
            sculpt.deltas.Add(new HairGuideDelta { guideId = group.guides[0].Id,
                positionOffsets = new[] { Vector3.zero, Vector3.right * 0.1f, Vector3.right * 0.2f } });
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluatedCurve curve = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(curve.points.Count, Is.EqualTo(12));
            Assert.That(Vector3.Distance(curve.points[^1].position, group.guides[0].points[^1].position + Vector3.right * 0.2f), Is.LessThan(1e-6f));
            foreach (HairCurvePoint point in curve.points) Assert.That(float.IsFinite(point.position.sqrMagnitude), Is.True);
        }

        [Test]
        public void UnifiedStackDuplicatesOwnTheirModifiersAndLocksProtectThem()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.FlowAlign, layer);
            HairSculptLayer copy = HairGroomCommands.DuplicateLayer(groom, group, layer);
            Assert.That(copy.modifiers.Count, Is.EqualTo(1));
            Assert.That(copy.modifiers[0].Id, Is.Not.EqualTo(modifier.Id));
            copy.modifiers[0].rootToTip.MoveKey(0, new Keyframe(0f, 0.2f));
            Assert.That(modifier.rootToTip.Evaluate(0f), Is.EqualTo(1f));
            HairModifierSettings duplicate = HairGroomCommands.DuplicateModifier(groom, group, layer, modifier);
            Assert.That(duplicate.Id, Is.Not.EqualTo(modifier.Id));
            Assert.That(HairGroomCommands.EditStack(groom, group, duplicate.Id, false, -1), Is.True);
            Assert.That(layer.modifiers[0], Is.SameAs(duplicate));
            layer.locked = true;
            Assert.That(HairGroomCommands.EditStack(groom, group, duplicate.Id, false, 0, true), Is.False);
            Assert.That(HairGroomCommands.AddModifier(groom, group, HairModifierType.Length, layer), Is.Null);
            Assert.That(HairGroomCommands.DuplicateModifier(groom, group, layer, modifier), Is.Null);
        }

        [Test]
        public void UnifiedStackMigrationAndRemovalAreUndoable()
        {
            HairGroup group = groom.Groups[0];
            HairGroomCommands.AddModifier(groom, group, HairModifierType.FlowAlign);
            Undo.FlushUndoRecordObjects(); Undo.ClearUndo(groom); Undo.IncrementCurrentGroup();
            HairSculptLayer imported = HairGroomCommands.ImportLegacyModifiers(groom, group);
            string layerId = imported.Id, modifierId = imported.modifiers[0].Id;
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(groom.Groups[0].modifiers.Count, Is.EqualTo(1));
            Assert.That(groom.Groups[0].sculptLayers.Exists(layer => layer.Id == layerId), Is.False);
            Undo.PerformRedo();
            group = groom.Groups[0];
            Assert.That(group.modifiers, Is.Empty);
            Assert.That(group.sculptLayers[^1].modifiers[0].Id, Is.EqualTo(modifierId));
            Undo.IncrementCurrentGroup();
            HairGroomCommands.EditStack(groom, group, layerId, true, 0, true);
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(groom.Groups[0].sculptLayers[^1].modifiers[0].Id, Is.EqualTo(modifierId));
        }

        [Test]
        public void UnifiedStackSelectionIsExclusiveAndSerialized()
        {
            HairGroup group = groom.Groups[0];
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Curl, layer);
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SetActiveGroup(group.Id);
                stage.SetActiveModifier(layer.Id, modifier.Id);
                Assert.That(stage.ActiveLayer, Is.SameAs(layer));
                Assert.That(stage.ActiveModifier, Is.SameAs(modifier));
                string saved = HairPreferenceCodec.Capture(stage);
                Assert.That(saved, Does.Contain("activeModifierId"));
                Assert.That(HairPreferenceCodec.Capture(stage, true), Does.Not.Contain("activeModifierId"));
                stage.SetActiveLayer(layer.Id);
                Assert.That(stage.ActiveModifierId, Is.Null);
                HairPreferenceCodec.Restore(stage, saved);
                Assert.That(stage.ActiveModifierId, Is.EqualTo(modifier.Id));
                HairSculptLayer restoredLayer = JsonUtility.FromJson<HairSculptLayer>(JsonUtility.ToJson(layer));
                Assert.That(restoredLayer.modifiers[0].Id, Is.EqualTo(modifier.Id));
                Assert.That(restoredLayer.modifiers[0].type, Is.EqualTo(modifier.type));
                stage.SetActiveModifier(layer.Id, modifier.Id);
                HairGroomCommands.EditStack(groom, group, modifier.Id, false, 0, true);
                Assert.That(stage.ActiveModifier, Is.Null);
                Assert.That(stage.ActiveLayer, Is.SameAs(layer));
                HairSculptLayer replacement = HairGroomCommands.AddSculptLayer(groom, group);
                HairGroomCommands.EditStack(groom, group, layer.Id, true, 0, true);
                InvokeStageMethod(stage, "NormalizeStackSelection");
                Assert.That(stage.ActiveLayer, Is.SameAs(replacement));
                Assert.That(stage.ActiveModifierId, Is.Null);
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void FlowAlignAnchorsRootsAndPreservesEverySegmentAndPointAttribute()
        {
            HairGroup group = groom.Groups[0];
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluatedCurve before = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            group.modifiers.Add(new HairModifierSettings { type = HairModifierType.FlowAlign,
                domain = HairModifierDomain.GuidesAndChildren, amount = 1f, vector = Vector3.right });
            HairEvaluationResult result = HairGroomEvaluator.Evaluate(groom, options);
            HairEvaluatedCurve after = result.evaluatedGuides[0];
            Assert.That(after.points[0].position, Is.EqualTo(before.points[0].position));
            Assert.That(result.CardCount, Is.EqualTo(1));
            for (int i = 0; i < before.points.Count; i++)
            {
                Assert.That(after.points[i].width, Is.EqualTo(before.points[i].width));
                Assert.That(after.points[i].widthBaseline, Is.EqualTo(before.points[i].widthBaseline));
                Assert.That(after.points[i].profileScale, Is.EqualTo(before.points[i].profileScale));
                Assert.That(after.points[i].roll, Is.EqualTo(before.points[i].roll));
                Assert.That(group.guides[0].points[i].position, Is.EqualTo(before.points[i].position), "Modifier must not edit authored guides.");
                if (i == 0) continue;
                Vector3 segment = after.points[i].position - after.points[i - 1].position;
                float originalLength = Vector3.Distance(before.points[i].position, before.points[i - 1].position);
                Assert.That(segment.magnitude, Is.EqualTo(originalLength).Within(1e-6f));
                Assert.That(Vector3.Dot(segment.normalized, Vector3.right), Is.GreaterThan(0.99999f));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FlowAlignBlendsAnglesAndHandlesOppositeDirectionsWithoutCollapsing(bool opposite)
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear(); group.guides.Add(CreateLinearGuide("Flow", 42, new Vector3(0.3f, 0.2f, 0.1f), Vector3.up * 0.3f));
            Vector3 direction = opposite ? Vector3.down : Vector3.right;
            HairModifierSettings modifier = new HairModifierSettings { type = HairModifierType.FlowAlign,
                domain = HairModifierDomain.Guides, amount = 0.5f, weight = 0.5f, vector = direction * 5f,
                rootToTip = AnimationCurve.Linear(0f, 0.2f, 1f, 1f) };
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluatedCurve before = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            group.modifiers.Add(modifier);
            HairEvaluatedCurve after = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(after.points[0].position, Is.EqualTo(before.points[0].position));
            for (int i = 1; i < after.points.Count; i++)
            {
                Vector3 segment = after.points[i].position - after.points[i - 1].position;
                float expectedAngle = (opposite ? 180f : 90f) * 0.25f * modifier.rootToTip.Evaluate(i / (after.points.Count - 1f));
                Assert.That(segment.magnitude, Is.EqualTo(Vector3.Distance(before.points[i].position, before.points[i - 1].position)).Within(1e-6f));
                Assert.That(Vector3.Angle(Vector3.up, segment), Is.EqualTo(expectedAngle).Within(0.002f));
            }
        }

        [Test]
        public void FlowAlignCombinedDomainDoesNotApplyTwiceAndChildrenOnlyLeavesGuidesAlone()
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear(); group.guides.Add(CreateLinearGuide("Flow", 42, Vector3.zero, Vector3.up * 0.3f));
            group.children.childrenPerGuide = 3; group.children.rootSpread = 0f;
            group.children.lengthVariation = group.children.widthVariation = group.children.rollVariation = 0f;
            group.children.clump = 0f;
            HairModifierSettings modifier = new HairModifierSettings { type = HairModifierType.FlowAlign,
                domain = HairModifierDomain.Guides, amount = 0.5f, vector = Vector3.right };
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluationResult unmodified = HairGroomEvaluator.Evaluate(groom, options);
            group.modifiers.Add(modifier);
            HairEvaluationResult guides = HairGroomEvaluator.Evaluate(groom, options);
            modifier.domain = HairModifierDomain.GuidesAndChildren;
            HairEvaluationResult both = HairGroomEvaluator.Evaluate(groom, options);
            Assert.That(both.CardCount, Is.EqualTo(unmodified.CardCount));
            Assert.That(CurveFingerprint(both), Is.EqualTo(CurveFingerprint(guides)), "Children must inherit alignment exactly once.");
            foreach (HairEvaluatedCurve curve in both.curves)
            {
                Assert.That(curve.points[0].position, Is.EqualTo(Vector3.zero));
                Assert.That(Vector3.Angle(Vector3.up, curve.points[^1].position - curve.points[0].position), Is.EqualTo(45f).Within(0.002f));
            }
            modifier.domain = HairModifierDomain.Children;
            HairEvaluationResult children = HairGroomEvaluator.Evaluate(groom, options);
            Assert.That(children.evaluatedGuides[0].points, Is.EqualTo(unmodified.evaluatedGuides[0].points));
            for (int i = 0; i < children.curves.Count; i++)
            {
                HairEvaluatedCurve curve = children.curves[i];
                Assert.That(curve.points[0].position, Is.EqualTo(unmodified.curves[i].points[0].position));
                Assert.That(curve.Length, Is.EqualTo(unmodified.curves[i].Length).Within(1e-6f));
                Assert.That(Vector3.Angle(Vector3.up, curve.points[^1].position - curve.points[0].position),
                    Is.EqualTo(curve.isChild ? 45f : 0f).Within(0.002f));
            }
            modifier.domain = HairModifierDomain.GuidesAndChildren;
            HairEvaluationResult pooled = new HairEvaluationResult();
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            for (int i = 0; i < 3; i++)
                Assert.That(CurveFingerprint(HairGroomEvaluator.EvaluateInto(groom, options, workspace, pooled)), Is.EqualTo(CurveFingerprint(both)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void FlowAlignZeroControlsAreNonMutating(int control)
        {
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            string before = CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options));
            HairModifierSettings modifier = new HairModifierSettings { type = HairModifierType.FlowAlign, amount = 1f, vector = Vector3.right };
            if (control == 0) modifier.amount = 0f;
            if (control == 1) modifier.weight = 0f;
            if (control == 2) modifier.vector = Vector3.zero;
            if (control == 3) modifier.rootToTip = AnimationCurve.Linear(0f, 0f, 1f, 0f);
            groom.Groups[0].modifiers.Add(modifier);
            Assert.That(CurveFingerprint(HairGroomEvaluator.Evaluate(groom, options)), Is.EqualTo(before));
        }

        [Test]
        public void MutablePreviewEvaluationMatchesFreshResultsAcrossStructuralEdits()
        {
            PopulateDenseProfileGuides(24, 3, 12);
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            HairEvaluationResult reused = new HairEvaluationResult();
            for (int pass = 0; pass < 6; pass++)
            {
                HairGroup group = groom.Groups[0];
                if (pass == 1) group.guides[0].points[2].position += Vector3.right * 0.05f;
                if (pass == 2) { group.children.childrenPerGuide = 1; group.children.seed++; group.guides.Reverse(); }
                if (pass == 3) { options.includeGuideCards = false; groom.Lods[0].samplesPerCard = 6; }
                if (pass == 4) { options.includeChildren = false; group.guides.RemoveRange(0, 12); }
                if (pass == 5) { options.includeChildren = options.includeGuideCards = true; group.children.childrenPerGuide = 5; }
                HairEvaluationResult fresh = HairGroomEvaluator.Evaluate(groom, options);
                Assert.That(HairGroomEvaluator.EvaluateInto(groom, options, workspace, reused), Is.SameAs(reused));
                Assert.That(CurveFingerprint(reused), Is.EqualTo(CurveFingerprint(fresh)), $"Pass {pass}");
                Assert.That(reused.curves.Count, Is.EqualTo(fresh.curves.Count));
                Assert.That(reused.evaluatedGuides.Count, Is.EqualTo(fresh.evaluatedGuides.Count));
                for (int i = 0; i < fresh.curves.Count; i++)
                {
                    Assert.That(reused.curves[i].curveId, Is.EqualTo(fresh.curves[i].curveId), $"Pass {pass} card {i}");
                    Assert.That(reused.curves[i].parentGuideId, Is.EqualTo(fresh.curves[i].parentGuideId));
                    Assert.That(reused.curves[i].samplesPerCardOverride, Is.EqualTo(fresh.curves[i].samplesPerCardOverride));
                }
            }
        }

        [Test]
        public void MutableMeshUpdatesMatchFreshBuildsAcrossProfilesCountsAndAtlasChanges()
        {
            PopulateDenseProfileGuides(16, 2, 12);
            HairMeshBuildWorkspace workspace = new HairMeshBuildWorkspace();
            HairCardMeshBuildResult reused = null;
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasRegion region = atlas.CreateRegion("Set", new Rect(0.1f, 0.2f, 0.5f, 0.7f));
            try
            {
                for (int pass = 0; pass < 7; pass++)
                {
                    HairGroup group = groom.Groups[0];
                    if (pass == 1) { group.atlas = atlas; group.rootEmbedDepth = 0.003f; group.guides[0].points[3].roll += 25f; }
                    if (pass == 2) { profile.Configure(HairCardShape.TaperedTube, 0.05f, 0.004f, 8, 8, false); groom.Lods[0].samplesPerCard = 8; }
                    if (pass == 3) { group.children.childrenPerGuide = 0; region.flipU = true; region.uvRect = new Rect(0.2f, 0.1f, 0.3f, 0.6f); }
                    if (pass == 4) profile.WidthAlongCard.MoveKey(0, new Keyframe(0f, 0.4f));
                    if (pass == 5) group.enabled = false;
                    if (pass == 6) { group.enabled = true; group.atlas = null; profile.Configure(HairCardShape.Ribbon, 0.04f, 0f, 12); }
                    HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false });
                    Mesh previousMesh = reused?.mesh;
                    reused = HairCardMeshGenerator.Update(evaluation, "Updated", reused, workspace);
                    if (previousMesh != null) Assert.That(reused.mesh, Is.SameAs(previousMesh));
                    using HairCardMeshBuildResult fresh = HairCardMeshGenerator.Build(evaluation, "Fresh", true);
                    Assert.That(MeshFingerprint(reused.mesh), Is.EqualTo(MeshFingerprint(fresh.mesh)), $"Pass {pass}");
                    Assert.That(reused.mesh.normals, Is.EqualTo(fresh.mesh.normals));
                    Assert.That(reused.mesh.tangents, Is.EqualTo(fresh.mesh.tangents));
                    Assert.That(reused.mesh.colors, Is.EqualTo(fresh.mesh.colors));
                    Assert.That(reused.mesh.bounds, Is.EqualTo(fresh.mesh.bounds));
                    Assert.That(reused.localUvs, Is.EqualTo(fresh.localUvs));
                    Assert.That(reused.cards.Count, Is.EqualTo(fresh.cards.Count));
                    for (int i = 0; i < fresh.cards.Count; i++)
                    {
                        Assert.That(reused.cards[i].triangleStart, Is.EqualTo(fresh.cards[i].triangleStart));
                        Assert.That(reused.cards[i].triangleCount, Is.EqualTo(fresh.cards[i].triangleCount));
                        Assert.That(reused.cards[i].uvSetId, Is.EqualTo(fresh.cards[i].uvSetId));
                    }
                }
            }
            finally { reused?.Dispose(); Object.DestroyImmediate(atlas); }
        }

        [Test]
        public void PreviewPoolsAvoidPerCardAllocationsAfterWarmup()
        {
            PopulateDenseProfileGuides(100, 4, 12);
            HairEvaluationWorkspace evaluationWorkspace = new HairEvaluationWorkspace();
            HairMeshBuildWorkspace meshWorkspace = new HairMeshBuildWorkspace();
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluationResult evaluation = new HairEvaluationResult();
            HairCardMeshBuildResult mesh = null;
            try
            {
                System.Action update = () =>
                {
                    HairGroomEvaluator.EvaluateInto(groom, options, evaluationWorkspace, evaluation);
                    mesh = HairCardMeshGenerator.Update(evaluation, "Preview", mesh, meshWorkspace);
                };
                update(); update();
                long allocations = CountManagedAllocations(update);
                Assert.That(allocations, Is.LessThan(100), "A warm refresh must not allocate curves/card spans per card.");
            }
            finally { mesh?.Dispose(); }
        }

        [Test]
        public void HighlightCacheTracksDeformationTopologySelectionAndVisibility()
        {
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasRegion first = atlas.CreateRegion("First", new Rect(0f, 0f, 0.5f, 1f));
            HairAtlasRegion second = atlas.CreateRegion("Second", new Rect(0.5f, 0f, 0.5f, 1f));
            groom.Groups[0].atlas = atlas;
            groom.Groups[0].children.childrenPerGuide = 4;
            HairCardStage stage = CreateEditingStage();
            FieldInfo linesField = typeof(HairCardStage).GetField("cardHighlightLines", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                stage.PreviewMode = HairPreviewMode.Cards;
                for (int pass = 0; pass < 5; pass++)
                {
                    if (pass == 1) groom.Groups[0].guides[0].points[^1].position += Vector3.right * 0.07f;
                    if (pass == 2) { groom.Groups[0].children.childrenPerGuide = 1; profile.Configure(HairCardShape.TaperedTube, 0.04f, 0.004f, 4); }
                    if (pass == 4) groom.Groups[0].enabled = false;
                    string selected = pass == 3 ? second.Id : first.Id;
                    stage.RebuildNow(); stage.HighlightUvSet(atlas, selected);
                    Vector3[] vertices = stage.MeshBuild.mesh.vertices;
                    List<Vector3> expected = new List<Vector3>();
                    HashSet<ulong> edges = new HashSet<ulong>();
                    foreach (HairCardSpan card in stage.MeshBuild.cards)
                    {
                        if (card.uvSetId != selected) continue;
                        int[] indices = stage.MeshBuild.mesh.GetTriangles(card.submesh);
                        for (int i = card.triangleStart * 3; i < (card.triangleStart + card.triangleCount) * 3; i += 3)
                            for (int edge = 0; edge < 3; edge++)
                            {
                                int a = indices[i + edge], b = indices[i + (edge + 1) % 3];
                                ulong key = ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);
                                if (edges.Add(key)) { expected.Add(vertices[a]); expected.Add(vertices[b]); }
                            }
                    }
                    Assert.That((Vector3[])linesField.GetValue(stage), Is.EqualTo(expected), $"Pass {pass}");
                    stage.ShowCardWireframe = false;
                    Assert.That((Vector3[])linesField.GetValue(stage), Is.Empty);
                    stage.ShowCardWireframe = true;
                    Assert.That((Vector3[])linesField.GetValue(stage), Is.EqualTo(expected));
                }
            }
            finally { DestroyEditingStage(stage); Object.DestroyImmediate(atlas); }
        }

        [Test]
        public void PooledPoseUpdatesRemainIndependentAndRefreshGuideBindings()
        {
            Mesh posed = Object.Instantiate(sourceMesh);
            Vector3[] vertices = posed.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] += Vector3.up;
            posed.vertices = vertices;
            try
            {
                HairAuthoringPose pose = new HairAuthoringPose(sourceMesh, posed);
                HairEvaluationResult evaluation = HairGroomEvaluator.Evaluate(groom);
                string fingerprint = CurveFingerprint(evaluation);
                HairEvaluationResult reused = pose.TransformEvaluation(groom, evaluation);
                HairEvaluatedCurve first = reused.curves[0];
                for (int pass = 0; pass < 3; pass++)
                {
                    if (pass == 1) groom.Groups[0].guides[0].root = default;
                    if (pass == 2) groom.Groups[0].guides.Clear();
                    HairEvaluationResult fresh = pose.TransformEvaluation(groom, evaluation);
                    Assert.That(pose.TransformEvaluation(groom, evaluation, reused), Is.SameAs(reused));
                    Assert.That(reused.curves[0], Is.SameAs(first));
                    Assert.That(CurveFingerprint(reused), Is.EqualTo(CurveFingerprint(fresh)));
                    Assert.That(CurveFingerprint(evaluation), Is.EqualTo(fingerprint), "Posing must not mutate source curves.");
                }
            }
            finally { Object.DestroyImmediate(posed); }
        }

        [TestCase(256, 4)]
        [TestCase(1000, 4)]
        [TestCase(5000, 4)]
        [Category("HairPreviewRefreshProfile")]
        public void ProfileCardPreviewRefresh(int guideCount, int children)
        {
            PopulateDenseProfileGuides(guideCount, children, 12);
            profile.Configure(HairCardShape.Ribbon, 0.04f, 0.001f, 12, 6, true);
            Mesh posed = Object.Instantiate(sourceMesh);
            Vector3[] positions = posed.vertices;
            for (int i = 0; i < positions.Length; i++) positions[i] += new Vector3(0.01f, 0.02f, 0.03f);
            posed.vertices = positions;
            HairAuthoringPose pose = new HairAuthoringPose(sourceMesh, posed);
            HairEvaluationWorkspace workspace = new HairEvaluationWorkspace();
            HairEvaluationOptions options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairEvaluationResult result = null, transformed = null;
            HairCardStage stage = null;
            try
            {
                ProfileOperation($"refresh-evaluate-{guideCount}",
                    () => result = HairGroomEvaluator.Evaluate(groom, options, workspace), () => CurveFingerprint(result));
                ProfileOperation($"refresh-pose-{guideCount}",
                    () => transformed = pose.TransformEvaluation(groom, result), () => CurveFingerprint(transformed));
                stage = CreateEditingStage();
                SetStageField(stage, "authoringPose", pose);
                stage.PreviewMode = HairPreviewMode.Cards;
                ProfileOperation($"refresh-stage-{guideCount}",
                    () => { stage.QueuePreviewChange(HairPreviewChange.Evaluation, true); stage.RebuildNow(); },
                    () => MeshFingerprint(stage.MeshBuild.mesh));
            }
            finally { if (stage != null) DestroyEditingStage(stage); Object.DestroyImmediate(posed); }
        }

        private static void ProfileOperation(string name, System.Action operation, System.Func<string> fingerprint)
        {
            operation(); // warm JIT/native paths before measuring
            double[] milliseconds = new double[3];
            System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
            for (int pass = 0; pass < 3; pass++)
            {
                System.GC.Collect(); System.GC.WaitForPendingFinalizers(); System.GC.Collect();
                timer.Restart(); operation(); timer.Stop();
                milliseconds[pass] = timer.Elapsed.TotalMilliseconds;
            }
            System.Array.Sort(milliseconds);
            // Unity's Mono GC.GetAllocatedBytesForCurrentThread returns zero in this editor.
            // Count GC.Alloc events with the native recorder, separately from timing samples.
            long allocationCount;
            using (ProfilerRecorder recorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "GC.Alloc", 1,
                ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.CollectOnlyOnCurrentThread))
            {
                operation();
                recorder.Stop();
                Assert.That(recorder.Valid, Is.True, "GC.Alloc recorder must be available.");
                allocationCount = recorder.Count == 0 ? 0 : recorder.GetSample(0).Count;
            }
            TestContext.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "HAIR_PROFILE|{0}|median_ms={1:F3}|gc_allocations={2}|fingerprint={3}", name, milliseconds[1], allocationCount, fingerprint()));
        }

        private static string CurveFingerprint(HairEvaluationResult result)
        {
            ulong hash = 14695981039346656037UL;
            foreach (HairEvaluatedCurve curve in result.curves)
            {
                HashFloat(ref hash, curve.rootNormal.x); HashFloat(ref hash, curve.rootNormal.y); HashFloat(ref hash, curve.rootNormal.z);
                foreach (HairCurvePoint point in curve.points)
                { HashFloat(ref hash, point.position.x); HashFloat(ref hash, point.position.y); HashFloat(ref hash, point.position.z); HashFloat(ref hash, point.width); HashFloat(ref hash, point.roll); }
            }
            return hash.ToString("X16");
        }

        private static string MeshFingerprint(Mesh mesh)
        {
            ulong hash = 14695981039346656037UL;
            foreach (Vector3 value in mesh.vertices) { HashFloat(ref hash, value.x); HashFloat(ref hash, value.y); HashFloat(ref hash, value.z); }
            foreach (Vector3 value in mesh.normals) { HashFloat(ref hash, value.x); HashFloat(ref hash, value.y); HashFloat(ref hash, value.z); }
            foreach (Vector4 value in mesh.tangents) { HashFloat(ref hash, value.x); HashFloat(ref hash, value.y); HashFloat(ref hash, value.z); HashFloat(ref hash, value.w); }
            foreach (Vector2 value in mesh.uv) { HashFloat(ref hash, value.x); HashFloat(ref hash, value.y); }
            foreach (int index in mesh.triangles) unchecked { hash = (hash ^ (uint)index) * 1099511628211UL; }
            return hash.ToString("X16");
        }

        private static void HashFloat(ref ulong hash, float value)
        { unchecked { hash = (hash ^ (uint)System.BitConverter.SingleToInt32Bits(value)) * 1099511628211UL; } }

        private HairCardStage CreateEditingStage()
        {
            HairCardStage stage = ScriptableObject.CreateInstance<HairCardStage>();
            SetStageField(stage, "groom", groom);
            stage.WorkflowStep = HairWorkflowStep.Groom;
            stage.BrushRadius = 1f;
            stage.BrushHardness = 1f;
            stage.BrushStrength = 1f;
            stage.RebuildNow();
            return stage;
        }

        [UnityTest]
        public IEnumerator FixFirstWorkspacePanelsRenderAtNarrowAndWideSizes()
        {
            HairCardStage stage = CreateEditingStage();
            HairGroomWorkspace window = ScriptableObject.CreateInstance<HairGroomWorkspace>();
            FieldInfo activeStage = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previousStage = activeStage.GetValue(null);
            try
            {
                activeStage.SetValue(null, stage);
                typeof(HairGroomWorkspace).GetField("cardGeometryExpanded", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true);
                typeof(HairGroomWorkspace).GetField("guideLibraryExpanded", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, true);
                HairGroomCommands.AddSculptLayer(groom, groom.Groups[0], "Test layer");
                HairGroomCommands.AddModifier(groom, groom.Groups[0], HairModifierType.Curl);
                HairGroomCommands.AddModifier(groom, groom.Groups[0], HairModifierType.Gravity);
                HairGroomCommands.AddModifier(groom, groom.Groups[0], HairModifierType.Length);
                HairSculptLayer imported = HairGroomCommands.ImportLegacyModifiers(groom, groom.Groups[0]);
                stage.SetActiveGroup(groom.Groups[0].Id);
                stage.SetActiveLayer(imported.Id);
                stage.ValidateReleaseNow();
                window.position = new Rect(0f, 0f, 760f, 900f);
                window.Show();
                foreach (float width in new[] { 760f, 1200f })
                {
                    window.position = new Rect(0f, 0f, width, 900f);
                    foreach (HairWorkflowStep step in new[] { HairWorkflowStep.Setup, HairWorkflowStep.Growth, HairWorkflowStep.Guides, HairWorkflowStep.Groom,
                                 HairWorkflowStep.Cards, HairWorkflowStep.Optimize, HairWorkflowStep.ValidateAndBake })
                    {
                        stage.WorkflowStep = step;
                        window.Repaint();
                        yield return null;
                        yield return null;
                        LogAssert.NoUnexpectedReceived();
                        if (step == HairWorkflowStep.Groom)
                        {
                            foreach (HairModifierSettings modifier in imported.modifiers)
                            {
                                stage.SetActiveModifier(imported.Id, modifier.Id);
                                window.Repaint(); yield return null; yield return null;
                                LogAssert.NoUnexpectedReceived();
                            }
                            stage.SetActiveLayer(imported.Id);
                            imported.locked = true;
                            window.Repaint(); yield return null; yield return null;
                            LogAssert.NoUnexpectedReceived();
                            imported.locked = false;
                        }
                    }
                }
            }
            finally
            {
                window.Close();
                activeStage.SetValue(null, previousStage);
                DestroyEditingStage(stage);
            }
        }

        private void DestroyEditingStage(HairCardStage stage)
        {
            InvokeStageMethod(stage, "EndStroke");
            InvokeStageMethod(stage, "RestoreUnityToolState");
            InvokeStageMethod(stage, "DisposeBuild");
            Undo.ClearUndo(groom);
            Object.DestroyImmediate(stage);
        }

        private static void SetStageField(HairCardStage stage, string field, object value)
        {
            typeof(HairCardStage).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(stage, value);
        }

        private static void InvokeStageMethod(HairCardStage stage, string method, params object[] arguments)
        {
            typeof(HairCardStage).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(stage, arguments);
        }

        private static HairGuide CreateGuide(string name, int seed, Vector3 rootPosition)
        {
            HairGuide guide = new HairGuide
            {
                name = name,
                seed = seed,
                root = HairSurfaceAnchor.Create("mesh:test", 0, 0, new Vector3(0.25f, 0.25f, 0.5f),
                    0f, rootPosition, Vector3.up)
            };
            guide.points.Add(new HairGuidePoint { position = rootPosition, width = 0.04f });
            guide.points.Add(new HairGuidePoint { position = rootPosition + new Vector3(0.02f, 0.15f, 0f), width = 0.025f });
            guide.points.Add(new HairGuidePoint { position = rootPosition + new Vector3(0.05f, 0.3f, 0.02f), width = 0.005f });
            return guide;
        }

        private static HairGuide CreateLinearGuide(
            string name,
            int seed,
            Vector3 rootPosition,
            Vector3 tipOffset)
        {
            HairGuide guide = new HairGuide
            {
                name = name,
                seed = seed,
                root = HairSurfaceAnchor.Create("mesh:test", 0, 0,
                    new Vector3(0.25f, 0.25f, 0.5f), 0f, rootPosition, Vector3.forward)
            };
            guide.points.Add(new HairGuidePoint { position = rootPosition, width = 0.04f });
            guide.points.Add(new HairGuidePoint
            {
                position = rootPosition + tipOffset * 0.5f,
                width = 0.025f
            });
            guide.points.Add(new HairGuidePoint { position = rootPosition + tipOffset, width = 0.005f });
            return guide;
        }
    }

    public sealed class HairGroomRecoveryTests
    {
        private string testFolder;
        private string recoveryPath;
        private HairGroomAsset persistentGroom;
        private bool preferencesWereSuspended;

        [SetUp]
        public void SetUp()
        {
            string folderName = "__HairGroomRecoveryTests_" + System.Guid.NewGuid().ToString("N");
            preferencesWereSuspended = HairEditorPreferences.Suspended;
            HairEditorPreferences.Suspended = true;
            testFolder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            Mesh sourceMesh = new Mesh { name = "RecoverySource" };
            sourceMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            sourceMesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
            sourceMesh.triangles = new[] { 0, 1, 2 };
            AssetDatabase.CreateAsset(sourceMesh, testFolder + "/RecoverySource.asset");

            persistentGroom = ScriptableObject.CreateInstance<HairGroomAsset>();
            persistentGroom.name = "PersistentHairGroom";
            persistentGroom.SetSource(sourceMesh, "asset:recovery-test", "TestRace", "TestHead");
            persistentGroom.BakeSettings.assetName = "SnapshotValue";
            AssetDatabase.CreateAsset(
                persistentGroom, testFolder + "/PersistentHairGroom.asset");
            AssetDatabase.SaveAssetIfDirty(persistentGroom);
            recoveryPath = "Assets/UMAProjectData/HairCards/Recovery/" +
                           persistentGroom.GroomId + ".asset";
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(recoveryPath)) AssetDatabase.DeleteAsset(recoveryPath);
            if (!string.IsNullOrEmpty(testFolder)) AssetDatabase.DeleteAsset(testFolder);
            HairEditorPreferences.Suspended = preferencesWereSuspended;
        }

        [Test]
        public void SnapshotMainObjectNameMatchesStableRecoveryFilename()
        {
            HairGroomRecovery.SaveSnapshot(persistentGroom);
            HairGroomAsset snapshot =
                AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath);
            string originalRecoveryGuid = AssetDatabase.AssetPathToGUID(recoveryPath);

            persistentGroom.BakeSettings.assetName = "SecondSnapshotValue";
            HairGroomRecovery.SaveSnapshot(persistentGroom);

            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.name, Is.EqualTo(persistentGroom.GroomId));
            Assert.That(snapshot.GroomId, Is.EqualTo(persistentGroom.GroomId));
            Assert.That(
                (snapshot.hideFlags & HideFlags.NotEditable) != 0,
                Is.True);
            Assert.That(snapshot.BakeSettings.assetName, Is.EqualTo("SecondSnapshotValue"));
            Assert.That(AssetDatabase.AssetPathToGUID(recoveryPath), Is.EqualTo(originalRecoveryGuid));
        }

        [Test]
        public void GeneratedSourceIsSavedOnceAsPrivateMeshAndSurvivesReload()
        {
            Mesh generated = Object.Instantiate(persistentGroom.SourceMesh);
            generated.hideFlags = HideFlags.HideAndDontSave;
            generated.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            generated.bindposes = new[] { Matrix4x4.identity };
            BoneWeight weight = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
            generated.boneWeights = new[] { weight, weight, weight };
            generated.AddBlendShapeFrame("Animation only", 100f, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
            string groomPath = AssetDatabase.GetAssetPath(persistentGroom);
            try
            {
                persistentGroom.SetSource(generated, "generated:persistence-test", "TestRace", "Head");
                persistentGroom.Groups[0].FindMap(HairMapKind.GrowthArea).values = new[] { 0.2f, 0.5f, 0.9f };
                Assert.That(HairGroomSourcePersistence.EnsurePersistent(persistentGroom), Is.True, "Persisting a generated source must succeed.");
                Mesh snapshot = persistentGroom.SourceMesh;
                Assert.That(snapshot, Is.Not.SameAs(generated));
                Assert.That(AssetDatabase.IsSubAsset(snapshot), Is.True, "The saved source must be a subasset, not a scene-only mesh or main asset.");
                Assert.That(AssetDatabase.GetAssetPath(snapshot), Is.EqualTo(groomPath));
                Assert.That(snapshot.hideFlags & HideFlags.DontSave, Is.EqualTo(HideFlags.None));
                Assert.That(snapshot.vertices, Is.EqualTo(generated.vertices));
                Assert.That(snapshot.boneWeights, Is.EqualTo(generated.boneWeights));
                Assert.That(snapshot.bindposes, Is.EqualTo(generated.bindposes));
                Assert.That(snapshot.uv, Is.EqualTo(generated.uv));
                Assert.That(snapshot.blendShapeCount, Is.Zero);
                Assert.That(generated.blendShapeCount, Is.EqualTo(1), "The source character mesh must remain untouched.");
                generated.vertices = new[] { Vector3.one * 100f, Vector3.right, Vector3.up };
                Assert.That(snapshot.vertices[0], Is.EqualTo(Vector3.zero));
                HairGroomSourcePersistence.EnsurePersistent(persistentGroom);
                Assert.That(System.Array.FindAll(AssetDatabase.LoadAllAssetsAtPath(groomPath), item => item is Mesh).Length, Is.EqualTo(1));
                Object.DestroyImmediate(generated); generated = null;
                Resources.UnloadAsset(persistentGroom);
                Resources.UnloadAsset(snapshot);
                persistentGroom = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(groomPath);
                Assert.That(persistentGroom.SourceMesh, Is.Not.Null);
                Assert.That(persistentGroom.SourceMesh.isReadable, Is.True);
                Assert.That(persistentGroom.SourceTopologyMatches(), Is.True);
                Assert.That(persistentGroom.SourceMeshId, Is.EqualTo("generated:persistence-test"));
                Assert.That(persistentGroom.Groups[0].FindMap(HairMapKind.GrowthArea).values, Is.EqualTo(new[] { 0.2f, 0.5f, 0.9f }));
                Assert.That(AssetDatabase.LoadMainAssetAtPath(groomPath), Is.SameAs(persistentGroom));
            }
            finally { if (generated != null) Object.DestroyImmediate(generated); }
        }

        [Test]
        public void PersistentSourcesAreReferencedWithoutDuplicatingTheMesh()
        {
            Mesh original = persistentGroom.SourceMesh;
            Assert.That(HairGroomSourcePersistence.EnsurePersistent(persistentGroom), Is.True);
            Assert.That(persistentGroom.SourceMesh, Is.SameAs(original));
            Assert.That(System.Array.FindAll(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(persistentGroom)), item => item is Mesh), Is.Empty);
        }

        [Test]
        public void MissingSourcePreservesMapsAndRecoversFromRecordedOriginalWithoutRebinding()
        {
            Mesh original = persistentGroom.SourceMesh;
            HairGroomSourcePersistence.RememberOrigin(persistentGroom, original);
            string origin = persistentGroom.SourceObjectId, sourceId = persistentGroom.SourceMeshId;
            persistentGroom.Groups[0].FindMap(HairMapKind.GrowthArea).values = new[] { 0.3f, 0.6f, 1f };
            using (SerializedObject serialized = new SerializedObject(persistentGroom))
            {
                serialized.FindProperty("sourceMesh").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            persistentGroom.EnsureIntegrity();
            Assert.That(persistentGroom.Groups[0].FindMap(HairMapKind.GrowthArea).values, Is.EqualTo(new[] { 0.3f, 0.6f, 1f }));
            Assert.That(HairGroomSourcePersistence.TryEnsureSource(persistentGroom, out string message), Is.True, message);
            Assert.That(persistentGroom.SourceMesh, Is.SameAs(original));
            Assert.That(persistentGroom.SourceMeshId, Is.EqualTo(sourceId));
            Assert.That(persistentGroom.SourceObjectId, Is.EqualTo(origin));
            Assert.That(persistentGroom.Groups[0].FindMap(HairMapKind.GrowthArea).values, Is.EqualTo(new[] { 0.3f, 0.6f, 1f }));
        }

        [Test]
        public void SourceRepairRejectsDifferentTopologyAndUnreadableMeshesWithoutChangingGroom()
        {
            Mesh replacement = Object.Instantiate(persistentGroom.SourceMesh);
            GameObject character = new GameObject("Source repair renderer");
            try
            {
                replacement.triangles = new[] { 2, 1, 0 };
                MeshFilter filter = character.AddComponent<MeshFilter>(); filter.sharedMesh = replacement;
                string before = EditorJsonUtility.ToJson(persistentGroom);
                Assert.That(HairGroomSourcePersistence.RestoreFrom(persistentGroom, character, out _), Is.False);
                Assert.That(EditorJsonUtility.ToJson(persistentGroom), Is.EqualTo(before));
                replacement.triangles = new[] { 0, 1, 2 };
                Assert.That(HairGroomSourcePersistence.RestoreFrom(persistentGroom, character, out string message), Is.True, message);
                Assert.That(persistentGroom.SourceMesh, Is.Not.SameAs(replacement));
                Assert.That(EditorUtility.IsPersistent(persistentGroom.SourceMesh), Is.True);
                before = EditorJsonUtility.ToJson(persistentGroom);
                replacement.UploadMeshData(true);
                Assert.That(HairGroomSourcePersistence.RestoreFrom(persistentGroom, replacement, out _), Is.False);
                Assert.That(EditorJsonUtility.ToJson(persistentGroom), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(character); Object.DestroyImmediate(replacement); }
        }

        [Test]
        public void LegacyMissingSourceWithoutOriginReturnsRepairGuidanceAndKeepsPaint()
        {
            Assert.That(HairGroomSourcePersistence.ResolveAvatar(persistentGroom), Is.Null);
            persistentGroom.SetSource(persistentGroom.SourceMesh, "generated:legacy-source");
            persistentGroom.Groups[0].FindMap(HairMapKind.GrowthArea).values = new[] { 0.1f, 0.5f, 0.8f };
            using (SerializedObject serialized = new SerializedObject(persistentGroom))
            {
                serialized.FindProperty("sourceMesh").objectReferenceValue = null;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            string before = EditorJsonUtility.ToJson(persistentGroom);
            Assert.That(HairGroomSourcePersistence.TryEnsureSource(persistentGroom, out string message), Is.False);
            Assert.That(message, Does.Contain("Restore Source"));
            Assert.That(EditorJsonUtility.ToJson(persistentGroom), Is.EqualTo(before));
            Assert.That(persistentGroom.Groups[0].FindMap(HairMapKind.GrowthArea).values, Is.EqualTo(new[] { 0.1f, 0.5f, 0.8f }));
        }

        [Test]
        public void MissingSnapshotReferenceIsRecoveredWithoutCreatingAnotherSnapshot()
        {
            Mesh transient = Object.Instantiate(persistentGroom.SourceMesh);
            try
            {
                HairGroomSourcePersistence.RememberOrigin(persistentGroom, persistentGroom.SourceMesh);
                string origin = persistentGroom.SourceObjectId;
                persistentGroom.SetSource(transient, persistentGroom.SourceMeshId);
                HairGroomSourcePersistence.EnsurePersistent(persistentGroom);
                Mesh snapshot = persistentGroom.SourceMesh;
                using (SerializedObject serialized = new SerializedObject(persistentGroom))
                {
                    serialized.FindProperty("sourceMesh").objectReferenceValue = null;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                Assert.That(HairGroomSourcePersistence.TryEnsureSource(persistentGroom, out string message), Is.True, message);
                Assert.That(persistentGroom.SourceMesh, Is.SameAs(snapshot));
                Assert.That(persistentGroom.SourceObjectId, Is.EqualTo(origin));
                Assert.That(System.Array.FindAll(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(persistentGroom)), item => item is Mesh).Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(transient); }
        }

        [Test]
        public void WorkspaceSaveIncludesDirtyAndDetachedEditedProfilesAndAtlasesButNotUnrelatedAssets()
        {
            HairCardStage stage = ScriptableObject.CreateInstance<HairCardStage>();
            HairCardProfileAsset profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasProfileAsset unrelated = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            Material assignedMaterial = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(assignedMaterial, testFolder + "/AssignedMaterial.mat");
            atlas.material = assignedMaterial;
            AssetDatabase.CreateAsset(profile, testFolder + "/Profile.asset");
            AssetDatabase.CreateAsset(atlas, testFolder + "/Atlas.asset");
            AssetDatabase.CreateAsset(unrelated, testFolder + "/Unrelated.asset");
            try
            {
                typeof(HairCardStage).GetField("groom", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stage, persistentGroom);
                persistentGroom.Groups[0].profile = profile;
                persistentGroom.Groups[0].atlas = atlas;
                stage.TrackResourceEdit(profile);
                stage.TrackResourceEdit(atlas);
                stage.SaveNow(false);
                Assert.That(stage.HasUnsavedChanges, Is.False);
                assignedMaterial.color = Color.magenta;
                EditorUtility.SetDirty(assignedMaterial);
                stage.SaveNow(false);
                Assert.That(EditorUtility.IsDirty(assignedMaterial), Is.False, "Assigned material Inspector settings are included in workspace Save.");

                profile.Configure(HairCardShape.Ribbon, 0.05f, 0.001f, 17);
                atlas.CreateRegion("Saved UV set", new Rect(0.1f, 0.2f, 0.3f, 0.4f));
                EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(atlas);
                EditorUtility.SetDirty(unrelated);
                stage.TrackResourceEdit(atlas);
                persistentGroom.Groups[0].atlas = null;
                EditorUtility.SetDirty(persistentGroom);
                Assert.That(stage.HasUnsavedChanges, Is.True);

                typeof(HairCardStage).GetField("strokeActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stage, true);
                stage.SaveNow();
                Assert.That(EditorUtility.IsDirty(profile), Is.True, "Save waits for stroke completion.");
                typeof(HairCardStage).GetField("strokeActive", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stage, false);
                typeof(HairCardStage).GetMethod("EditorUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stage, null);
                Assert.That(EditorUtility.IsDirty(profile), Is.False);
                Assert.That(EditorUtility.IsDirty(atlas), Is.False, "An atlas edited then unassigned still saves.");
                Assert.That(EditorUtility.IsDirty(persistentGroom), Is.False);
                Assert.That(EditorUtility.IsDirty(unrelated), Is.True, "Workspace Save must not save unrelated project assets.");
                Assert.That(stage.HasUnsavedChanges, Is.False);
                Assert.That(AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath), Is.Not.Null);
            }
            finally { Object.DestroyImmediate(stage); }
        }

        [TestCase("assign-profile")]
        [TestCase("assign-atlas")]
        public void ValidationResourceFixIsScopedUndoableAndRespectsLockedGroups(string fixId)
        {
            HairCardStage stage = ScriptableObject.CreateInstance<HairCardStage>();
            try
            {
                typeof(HairCardStage).GetField("groom", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stage, persistentGroom);
                HairGroup group = persistentGroom.Groups[0];
                HairValidationIssue issue = new HairValidationIssue { groupId = group.Id, fixId = fixId };
                group.locked = true;
                Assert.That(stage.FixMissingResource(issue), Is.False);
                group.locked = false;
                Undo.IncrementCurrentGroup();
                Assert.That(stage.FixMissingResource(issue), Is.True);
                Assert.That(fixId == "assign-profile" ? group.profile != null : group.atlas != null, Is.True);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.That(fixId == "assign-profile" ? persistentGroom.Groups[0].profile == null :
                    persistentGroom.Groups[0].atlas == null, Is.True);
            }
            finally
            {
                Undo.ClearUndo(persistentGroom);
                Object.DestroyImmediate(stage);
            }
        }

        [Test]
        public void AutosaveWaitsForWorkspaceGesturesAndSkipsUnchangedRecoveryWrites()
        {
            HairCardStage stage = ScriptableObject.CreateInstance<HairCardStage>();
            try
            {
                typeof(HairCardStage).GetField("groom", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stage, persistentGroom);
                stage.SetWorkspaceInteraction(true);
                persistentGroom.BakeSettings.assetName = "Edited during UV/workspace gesture";
                EditorUtility.SetDirty(persistentGroom);
                stage.SaveNow();
                Assert.That(stage.SaveStatus, Is.EqualTo("Save queued"));
                typeof(HairCardStage).GetMethod("EditorUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stage, null);
                Assert.That(EditorUtility.IsDirty(persistentGroom), Is.True);
                stage.SetWorkspaceInteraction(false);
                typeof(HairCardStage).GetMethod("EditorUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stage, null);
                Assert.That(EditorUtility.IsDirty(persistentGroom), Is.True, "Wait for the idle debounce after mouse release.");
                typeof(HairCardStage).GetField("lastWorkspaceInteraction", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stage, EditorApplication.timeSinceStartup - 2d);
                typeof(HairCardStage).GetMethod("EditorUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stage, null);
                Assert.That(EditorUtility.IsDirty(persistentGroom), Is.False);
                Assert.That(stage.SaveStatus, Does.StartWith("Saved"));
                Assert.That(AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath), Is.Not.Null);
                System.DateTime timestamp = System.IO.File.GetLastWriteTimeUtc(recoveryPath);
                stage.SaveNow();
                Assert.That(System.IO.File.GetLastWriteTimeUtc(recoveryPath), Is.EqualTo(timestamp), "Unchanged work must not rewrite a recovery snapshot.");
            }
            finally { Object.DestroyImmediate(stage); }
        }

        [Test]
        public void MakeUniqueCopiesResourcesAndRemapsOnlyTheActiveGroupsUvIds()
        {
            HairGroup group = persistentGroom.Groups[0];
            HairCardProfileAsset profile = ScriptableObject.CreateInstance<HairCardProfileAsset>();
            profile.Configure(HairCardShape.TaperedTube, 0.08f, 0.004f, 17, 8, false);
            profile.WidthAlongCard.keys = new[] { new Keyframe(0f, 0.8f), new Keyframe(0.7f, 0.4f), new Keyframe(1f, 0.1f) };
            HairAtlasProfileAsset atlas = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
            HairAtlasRegion original = atlas.CreateRegion("Selected", new Rect(0.1f, 0.2f, 0.4f, 0.6f));
            original.flipU = true;
            AssetDatabase.CreateAsset(profile, testFolder + "/OriginalProfile.asset");
            AssetDatabase.CreateAsset(atlas, testFolder + "/OriginalAtlas.asset");
            group.profile = profile; group.atlas = atlas;
            group.atlasRegionSelection = HairAtlasRegionSelectionMode.Selected;
            group.atlasRegionIds.Add(original.Id);
            HairGroup other = persistentGroom.CreateGroup("Other", HairGroupRole.Coverage);
            other.profile = profile; other.atlas = atlas;
            try
            {
                HairGroomCommands.MakeResourcesUnique(persistentGroom, group, false);
                HairGroomCommands.MakeResourcesUnique(persistentGroom, group, true);
                Assert.That(group.profile, Is.Not.SameAs(profile));
                Assert.That(group.profile.ProfileId, Is.Not.EqualTo(profile.ProfileId));
                Assert.That(group.profile.Shape, Is.EqualTo(profile.Shape));
                Assert.That(group.profile.WidthAlongCard.keys, Is.EqualTo(profile.WidthAlongCard.keys));
                Assert.That(group.atlas, Is.Not.SameAs(atlas));
                Assert.That(group.atlas.regions[0].Id, Is.Not.EqualTo(original.Id));
                Assert.That(group.atlas.regions[0].uvRect, Is.EqualTo(original.uvRect));
                Assert.That(group.atlas.regions[0].flipU, Is.True);
                Assert.That(group.atlasRegionIds, Is.EqualTo(new[] { group.atlas.regions[0].Id }));
                Assert.That(other.profile, Is.SameAs(profile)); Assert.That(other.atlas, Is.SameAs(atlas));
                Assert.That(AssetDatabase.GetAssetPath(group.profile), Does.StartWith(testFolder + "/"));
                Assert.That(AssetDatabase.GetAssetPath(group.atlas), Does.StartWith(testFolder + "/"));
            }
            finally { Undo.ClearUndo(persistentGroom); }
        }

        [Test]
        public void RestorePreservesTargetFilenameAndRestoresGroomData()
        {
            HairGroomRecovery.SaveSnapshot(persistentGroom);
            HairGroomAsset snapshot =
                AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath);
            persistentGroom.BakeSettings.assetName = "ModifiedAfterSnapshot";

            HairGroomRecovery.RestoreSnapshotData(persistentGroom, snapshot);

            Assert.That(persistentGroom.name, Is.EqualTo("PersistentHairGroom"));
            Assert.That(persistentGroom.BakeSettings.assetName, Is.EqualTo("SnapshotValue"));
            Assert.That(
                AssetDatabase.GetAssetPath(persistentGroom),
                Is.EqualTo(testFolder + "/PersistentHairGroom.asset"));
            Assert.That(persistentGroom.hideFlags, Is.EqualTo(HideFlags.None));
        }

        [Test]
        public void RepairCorrectsLegacyRecoveryMainObjectName()
        {
            HairGroomRecovery.SaveSnapshot(persistentGroom);
            HairGroomAsset snapshot =
                AssetDatabase.LoadAssetAtPath<HairGroomAsset>(recoveryPath);
            snapshot.name = "PersistentHairGroom Recovery";
            EditorUtility.SetDirty(snapshot);

            int repaired = HairGroomRecovery.RepairSnapshotNames();

            Assert.That(repaired, Is.GreaterThanOrEqualTo(1));
            Assert.That(snapshot.name, Is.EqualTo(persistentGroom.GroomId));
        }
    }
}
#endif
