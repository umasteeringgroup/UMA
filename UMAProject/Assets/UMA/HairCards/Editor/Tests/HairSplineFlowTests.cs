#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine;
using UMA.HairCards.Runtime;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private HairSurfaceAnchor TestFlowAnchor(Vector3 point)
        {
            Vector3[] v = sourceMesh.vertices;
            return HairSurfaceAnchor.Create(groom.SourceMeshId, 0, 0,
                HairMeshUtility.Barycentric(point, v[0], v[1], v[2]), 0f, point, Vector3.up);
        }

        private HairFlowSpline TestFlowPath(params Vector3[] points)
        {
            var path = new HairFlowSpline(); path.EnsureIntegrity();
            foreach (Vector3 point in points) path.points.Add(TestFlowAnchor(point));
            return path;
        }

        private HairModifierSettings TestSplineFlow()
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear();
            HairGuide guide = CreateLinearGuide("Spline test", 17, Vector3.zero, Vector3.up * 0.3f);
            guide.root = TestFlowAnchor(Vector3.zero);
            group.guides.Add(guide);
            var modifier = new HairModifierSettings { type = HairModifierType.SplineFlow,
                domain = HairModifierDomain.GuidesAndChildren, flowRadius = 0.5f, flowLift = 0f };
            modifier.flowSplines.Add(TestFlowPath(Vector3.zero, Vector3.right * 0.3f));
            modifier.EnsureIntegrity(); group.modifiers.Add(modifier);
            return modifier;
        }

        private HairEvaluatedCurve TestFlowCurve() => HairGroomEvaluator.Evaluate(groom).evaluatedGuides[0];
        private static Vector3 FlowSegment(HairEvaluatedCurve curve, int i = 1) => curve.points[i].position - curve.points[i - 1].position;
        private static void AssertFlowLengths(HairEvaluatedCurve before, HairEvaluatedCurve after)
        {
            Assert.That(Vector3.Distance(before.points[0].position, after.points[0].position), Is.LessThan(1e-6f));
            for (int i = 1; i < before.points.Count; i++)
                Assert.That(FlowSegment(after, i).magnitude, Is.EqualTo(FlowSegment(before, i).magnitude).Within(2e-6f));
        }

        [Test]
        public void SplineFlowSteersWithoutMovingRootsChangingLengthsOrDuplicatingCards()
        {
            HairModifierSettings flow = TestSplineFlow();
            flow.enabled = false; HairEvaluatedCurve before = TestFlowCurve(); flow.enabled = true;
            string authored = EditorJsonUtility.ToJson(groom);
            HairEvaluationResult result = HairGroomEvaluator.Evaluate(groom);
            HairEvaluatedCurve after = result.evaluatedGuides[0];
            AssertFlowLengths(before, after);
            Assert.That(Vector3.Dot(FlowSegment(after).normalized, Vector3.right), Is.GreaterThan(0.9999f));
            Assert.That(result.CardCount, Is.EqualTo(1));
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(authored));
            for (int i = 0; i < after.points.Count; i++)
            {
                Assert.That(after.points[i].width, Is.EqualTo(before.points[i].width));
                Assert.That(after.points[i].roll, Is.EqualTo(before.points[i].roll));
                Assert.That(after.points[i].facingWeight, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(Vector3.Dot(after.points[i].facingNormal, Vector3.up), Is.GreaterThan(0.9999f));
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void SplineFlowEmptyDisabledDistantOppositeAndZeroSettingsAreNonMutating(int mode)
        {
            HairModifierSettings flow = TestSplineFlow(); flow.enabled = false;
            HairEvaluatedCurve before = TestFlowCurve(); flow.enabled = true;
            if (mode == 0) flow.flowSplines.Clear();
            if (mode == 1) flow.flowSplines[0].enabled = false;
            if (mode == 2) { flow.flowRadius = 0.01f; flow.flowSplines[0] = TestFlowPath(Vector3.forward * 0.2f, new Vector3(0.3f, 0f, 0.2f)); }
            if (mode == 3) sourceMesh.normals = new[] { Vector3.down, Vector3.down, Vector3.down };
            if (mode == 4) flow.flowDirection = flow.flowFacing = 0f;
            if (mode == 5) flow.weight = 0f;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            Assert.That(HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0].points, Is.EqualTo(before.points));
        }

        [Test]
        public void SplineFlowFacingOnlyOrientsUprightCardsAndResamplingRetainsFacing()
        {
            HairModifierSettings flow = TestSplineFlow(); flow.enabled = false;
            HairEvaluatedCurve before = TestFlowCurve(); flow.enabled = true; flow.flowDirection = 0f;
            HairEvaluatedCurve after = TestFlowCurve();
            for (int i = 0; i < before.points.Count; i++) Assert.That(after.points[i].position, Is.EqualTo(before.points[i].position));
            foreach (int count in new[] { 3, 9, 33 })
            {
                List<HairCurvePoint> points = HairCurveUtility.Resample(after.points, count);
                var tangents = new Vector3[count]; var sides = new Vector3[count]; var normals = new Vector3[count];
                HairCurveUtility.BuildRotationMinimizingFrames(points, after.rootNormal, tangents, sides, normals, out int flips);
                Assert.That(flips, Is.Zero);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(Vector3.Dot(normals[i], Vector3.right), Is.GreaterThan(0.9999f));
                    Assert.That(Vector3.Dot(normals[i], tangents[i]), Is.EqualTo(0f).Within(1e-6f));
                    Assert.That(normals[i].magnitude, Is.EqualTo(1f).Within(1e-6f));
                }
            }
        }

        [Test]
        public void SplineFlowBlendsPathsNotSampleDensityAndKeepsNeighboringRootsApart()
        {
            HairModifierSettings flow = TestSplineFlow();
            flow.flowSplines.Clear();
            flow.flowSplines.Add(TestFlowPath(new Vector3(-0.1f, 0f, -0.04f), new Vector3(0.3f, 0f, -0.04f)));
            flow.flowSplines.Add(TestFlowPath(new Vector3(0.04f, 0f, -0.1f), new Vector3(0.04f, 0f, 0.3f)));
            HairEvaluatedCurve blended = TestFlowCurve();
            Vector3 segment = FlowSegment(blended).normalized;
            Assert.That(segment.x, Is.EqualTo(segment.z).Within(0.002f));
            Assert.That(segment.x, Is.GreaterThan(0.65f));
            HairFlowSpline dense = flow.flowSplines[0]; dense.points.Clear();
            for (int i = 0; i <= 40; i++) dense.points.Add(TestFlowAnchor(new Vector3(-0.1f + i * 0.01f, 0f, -0.04f)));
            Assert.That(Vector3.Distance(FlowSegment(TestFlowCurve()), FlowSegment(blended)), Is.LessThan(1e-5f));
            HairGuide other = CreateLinearGuide("Adjacent", 27, Vector3.right * 0.01f, Vector3.up * 0.3f);
            other.root = TestFlowAnchor(Vector3.right * 0.01f); groom.Groups[0].guides.Add(other);
            HairEvaluationResult result = HairGroomEvaluator.Evaluate(groom);
            Assert.That(Vector3.Distance(result.evaluatedGuides[0].points[0].position, result.evaluatedGuides[1].points[0].position), Is.EqualTo(0.01f).Within(1e-6f));
        }

        [Test]
        public void SplineFlowFollowsCurvatureAndContinuesPastEndWithoutAttractingToEndpoint()
        {
            HairModifierSettings flow = TestSplineFlow();
            flow.flowSplines[0] = TestFlowPath(Vector3.zero, Vector3.right * 0.05f, new Vector3(0.05f, 0f, 0.08f));
            HairGuide guide = groom.Groups[0].guides[0]; guide.points.Clear();
            for (int i = 0; i <= 12; i++) guide.points.Add(new HairGuidePoint { position = Vector3.up * (i * 0.025f), width = 0.01f });
            HairEvaluatedCurve curve = TestFlowCurve();
            Assert.That(FlowSegment(curve).x, Is.GreaterThan(0.02f));
            Assert.That(FlowSegment(curve, 12).z, Is.GreaterThan(0.024f));
            Assert.That(curve.Length, Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(curve.points[12].position.z, Is.GreaterThan(0.15f));
        }

        [Test]
        public void SplineFlowRootStrengthFreezeStiffnessAndLayerOpacityAreRespected()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0];
            flow.enabled = false; HairEvaluatedCurve before = TestFlowCurve(); flow.enabled = true;
            foreach (HairGuidePoint point in group.guides[0].points) point.freeze = 1f;
            HairEvaluatedCurve frozen = TestFlowCurve(); AssertFlowLengths(before, frozen);
            for (int i = 0; i < frozen.points.Count; i++)
            { Assert.That(frozen.points[i].position, Is.EqualTo(before.points[i].position)); Assert.That(frozen.points[i].facingWeight, Is.Zero); }
            foreach (HairGuidePoint point in group.guides[0].points) { point.freeze = 0f; point.stiffness = 1f; }
            HairEvaluatedCurve stiff = TestFlowCurve();
            Assert.That(Vector3.Angle(FlowSegment(stiff), Vector3.up), Is.LessThan(0.01f));
            foreach (HairGuidePoint point in group.guides[0].points) point.stiffness = 0f;
            flow.rootInfluence = 0f; HairEvaluatedCurve protectedRoot = TestFlowCurve(); AssertFlowLengths(before, protectedRoot);
            Assert.That(Vector3.Angle(FlowSegment(protectedRoot), Vector3.up), Is.LessThan(89f));
            Assert.That(Vector3.Angle(FlowSegment(protectedRoot, 2), Vector3.right), Is.LessThan(0.01f));
            flow.rootInfluence = 1f; group.modifiers.Clear();
            var layer = new HairSculptLayer { opacity = 0.5f }; layer.modifiers.Add(flow); group.sculptLayers.Add(layer);
            Assert.That(Vector3.Angle(FlowSegment(TestFlowCurve()), Vector3.up), Is.EqualTo(45f).Within(0.01f));
            layer.opacity = 0f;
            Assert.That(TestFlowCurve().points, Is.EqualTo(before.points));
        }

        [Test]
        public void SplineFlowChildrenInheritFacingAndCombinedDomainDoesNotDoubleSteer()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0];
            group.children.childrenPerGuide = 3; group.children.rootSpread = 0f;
            group.children.lengthVariation = group.children.widthVariation = group.children.rollVariation = group.children.clump = 0f;
            flow.flowDirection = 0.5f; flow.domain = HairModifierDomain.Guides;
            HairEvaluationResult guides = HairGroomEvaluator.Evaluate(groom);
            flow.domain = HairModifierDomain.GuidesAndChildren;
            HairEvaluationResult combined = HairGroomEvaluator.Evaluate(groom);
            Assert.That(combined.CardCount, Is.EqualTo(4));
            for (int i = 0; i < combined.curves.Count; i++)
            {
                Assert.That(combined.curves[i].points, Is.EqualTo(guides.curves[i].points));
                Assert.That(combined.curves[i].points[1].facingWeight, Is.EqualTo(1f).Within(1e-6f));
                Assert.That(Vector3.Angle(FlowSegment(combined.curves[i]), Vector3.up), Is.EqualTo(45f).Within(0.01f));
            }
            flow.domain = HairModifierDomain.Children;
            HairEvaluationResult children = HairGroomEvaluator.Evaluate(groom);
            Assert.That(children.evaluatedGuides[0].points[1].facingWeight, Is.Zero);
            foreach (HairEvaluatedCurve curve in children.curves)
                Assert.That(curve.points[1].facingWeight, Is.EqualTo(curve.isChild ? 1f : 0f).Within(1e-6f));
        }

        [Test]
        public void SplineFlowWorkspaceRefreshesEditedPathsAndSourceNormals()
        {
            HairModifierSettings flow = TestSplineFlow(); var workspace = new HairEvaluationWorkspace(); var result = new HairEvaluationResult();
            HairGroomEvaluator.EvaluateInto(groom, null, workspace, result);
            Assert.That(FlowSegment(result.evaluatedGuides[0]).x, Is.GreaterThan(0.14f));
            flow.flowSplines[0].points.Reverse();
            HairGroomEvaluator.EvaluateInto(groom, null, workspace, result);
            Assert.That(FlowSegment(result.evaluatedGuides[0]).x, Is.LessThan(-0.14f));
            sourceMesh.normals = new[] { Vector3.down, Vector3.down, Vector3.down };
            HairGroomEvaluator.EvaluateInto(groom, null, workspace, result);
            Assert.That(result.evaluatedGuides[0].points[1].facingNormal.y, Is.LessThan(-0.99f));
            workspace.Clear();
        }

        [Test]
        public void SplineFlowDuplicateAndJsonRoundtripOwnIndependentPathData()
        {
            HairModifierSettings flow = TestSplineFlow(); flow.flowBank = 23f; flow.flowMirrorDrawing = true;
            HairModifierSettings copy = flow.Duplicate();
            Assert.That(copy.Id, Is.Not.EqualTo(flow.Id));
            Assert.That(copy.flowSplines[0].Id, Is.Not.EqualTo(flow.flowSplines[0].Id));
            Assert.That(copy.flowSplines[0].points, Is.Not.SameAs(flow.flowSplines[0].points));
            copy.flowSplines[0].points.Reverse();
            Assert.That(copy.flowSplines[0].points[0], Is.Not.EqualTo(flow.flowSplines[0].points[0]));
            HairGroomAsset restored = ScriptableObject.CreateInstance<HairGroomAsset>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(groom), restored);
                HairModifierSettings saved = restored.Groups[0].modifiers[0];
                Assert.That(saved.flowSplines[0].Id, Is.EqualTo(flow.flowSplines[0].Id));
                Assert.That(saved.flowSplines[0].points, Is.EqualTo(flow.flowSplines[0].points));
                Assert.That(saved.flowBank, Is.EqualTo(23f)); Assert.That(saved.flowMirrorDrawing, Is.True);
                HairEvaluatedCurve roundtrip = HairGroomEvaluator.Evaluate(restored).evaluatedGuides[0], original = TestFlowCurve();
                for (int i = 0; i < original.points.Count; i++)
                {
                    // Unity's JSON roundtrip can round barycentrics at ~1e-8; compare geometry
                    // numerically instead of requiring bit-identical derived float structs.
                    Assert.That(Vector3.Distance(roundtrip.points[i].position, original.points[i].position), Is.LessThan(1e-6f));
                    Assert.That(Vector3.Distance(roundtrip.points[i].facingNormal, original.points[i].facingNormal), Is.LessThan(1e-6f));
                    Assert.That(roundtrip.points[i].facingWeight, Is.EqualTo(original.points[i].facingWeight).Within(1e-6f));
                    Assert.That(roundtrip.points[i].width, Is.EqualTo(original.points[i].width).Within(1e-6f));
                }
            }
            finally { Object.DestroyImmediate(restored); }
        }

        [Test]
        public void SplineFlowRuntimeAndPosedPreviewKeepFacingInObjectSpace()
        {
            TestSplineFlow();
            Matrix4x4 transform = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.Euler(17f, 33f, 85f), new Vector3(1.4f, 0.8f, 1.1f));
            using (var generated = HairGroomRuntimeAPI.Generate(groom, sourceToWorld: transform))
            {
                Assert.That(generated.Evaluation.evaluatedGuides[0].points, Is.EqualTo(TestFlowCurve().points));
                foreach (Vector3 normal in generated.Mesh.normals) Assert.That(Mathf.Abs(Vector3.Dot(normal, Vector3.up)), Is.GreaterThan(0.999f));
            }
            Mesh posed = Object.Instantiate(sourceMesh);
            try
            {
                Vector3[] vertices = posed.vertices;
                for (int i = 0; i < vertices.Length; i++) vertices[i] = transform.MultiplyPoint3x4(vertices[i]);
                posed.vertices = vertices;
                HairAuthoringPose pose = new HairAuthoringPose(sourceMesh, posed);
                HairEvaluationResult original = HairGroomEvaluator.Evaluate(groom), transformed = pose.TransformEvaluation(groom, original);
                Vector3 expected = HairPoseUtility.TransformNormal(pose.MatrixForGuide(groom, original.curves[0].parentGuideId), original.curves[0].points[1].facingNormal);
                Assert.That(Vector3.Angle(transformed.curves[0].points[1].facingNormal, expected), Is.LessThan(0.03f));
                Assert.That(transformed.curves[0].points[1].facingWeight, Is.EqualTo(original.curves[0].points[1].facingWeight));
                Assert.That(original.curves[0].points[1].facingNormal, Is.EqualTo(Vector3.up));
            }
            finally { Object.DestroyImmediate(posed); }
        }

        [Test]
        public void SplineFlowMirrorModifierReflectsFacing()
        {
            HairModifierSettings flow = TestSplineFlow(); flow.flowDirection = 0f;
            groom.Groups[0].modifiers.Add(new HairModifierSettings { type = HairModifierType.Mirror, domain = HairModifierDomain.GuidesAndChildren });
            HairEvaluatedCurve curve = TestFlowCurve();
            Assert.That(curve.points[1].facingNormal.x, Is.LessThan(-0.99f));
        }

        [TestCase(0f)] [TestCase(90f)] [TestCase(180f)]
        public void SplineFlowBankRotatesAboutUprightStrandAndRollRemainsAnOffset(float bank)
        {
            HairModifierSettings flow = TestSplineFlow(); flow.flowDirection = 0f; flow.flowBank = bank;
            HairEvaluatedCurve curve = TestFlowCurve();
            var tangents = new Vector3[curve.points.Count]; var sides = new Vector3[curve.points.Count]; var normals = new Vector3[curve.points.Count];
            for (int i = 0; i < curve.points.Count; i++) { HairCurvePoint point = curve.points[i]; point.roll = 15f; curve.points[i] = point; }
            HairCurveUtility.BuildRotationMinimizingFrames(curve.points, curve.rootNormal, tangents, sides, normals, out _);
            Vector3 expected = Quaternion.AngleAxis(bank + 15f, Vector3.up) * Vector3.right;
            foreach (Vector3 normal in normals) Assert.That(Vector3.Angle(normal, expected), Is.LessThan(0.03f));
        }

        [Test]
        public void SplineFlowRejectsOtherSourceAnchorsAndHandlesOpposingHairDirections()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGuide guide = groom.Groups[0].guides[0];
            for (int i = 0; i < guide.points.Count; i++) guide.points[i].position = Vector3.left * (i * 0.15f);
            flow.flowDirection = 0.5f;
            HairEvaluatedCurve half = TestFlowCurve();
            Assert.That(FlowSegment(half).magnitude, Is.EqualTo(0.15f).Within(1e-6f));
            Assert.That(Vector3.Angle(FlowSegment(half), Vector3.left), Is.EqualTo(90f).Within(0.02f));
            HairSurfaceAnchor anchor = flow.flowSplines[0].points[0];
            flow.flowSplines[0].points[0] = HairSurfaceAnchor.Create("different:mesh", 0, 0, anchor.Barycentric,
                0f, anchor.CachedLocalPosition, anchor.CachedLocalNormal);
            HairEvaluatedCurve ignored = TestFlowCurve();
            Assert.That(ignored.points[1].facingWeight, Is.Zero);
            Assert.That(Vector3.Angle(FlowSegment(ignored), Vector3.left), Is.LessThan(0.03f));
        }

        [UnityTest]
        public IEnumerator SplineFlowScenePathsRenderAndObeyDepthAndHelperVisibility()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0]; group.modifiers.Clear();
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group); layer.modifiers.Add(flow);
            flow.flowSplines.Clear();
            flow.flowSplines.Add(TestFlowPath(new Vector3(-0.25f, 0.2f, -0.1f), new Vector3(0.25f, 0.2f, -0.1f)));
            flow.flowSplines.Add(TestFlowPath(new Vector3(-0.25f, -0.2f, 0.1f), new Vector3(0.25f, -0.2f, 0.1f)));
            HairCardStage stage = CreateEditingStage(); stage.SetActiveModifier(layer.Id, flow.Id);
            HairGuideDepthTestWindow window = ScriptableObject.CreateInstance<HairGuideDepthTestWindow>();
            GameObject cameraObject = new GameObject("Flow path test camera"), surface = new GameObject("Flow occluder");
            Camera camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 0.5f; camera.aspect = 1f;
            camera.transform.SetPositionAndRotation(Vector3.up * 3f, Quaternion.Euler(90f, 0f, 0f));
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>(); Material material = new Material(Shader.Find("Standard")); renderer.sharedMaterial = material;
            RenderTexture target = new RenderTexture(128, 128, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
            Texture2D capture = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            var depth = new HairGuideDepthRenderer();
            try
            {
                window.Show();
                foreach (bool editing in new[] { false, true })
                foreach (bool show in new[] { false, true })
                foreach (bool depthEnabled in new[] { false, true })
                {
                    stage.SceneTool = editing ? HairSceneTool.DrawFlow : HairSceneTool.Comb;
                    stage.ShowHelpers = show; stage.DepthTestGuides = depthEnabled;
                    bool rendered = false, restored = false; Color[] pixels = null;
                    window.draw = () =>
                    {
                        if (rendered) return;
                        RenderTexture previous = RenderTexture.active; Camera oldCamera = Camera.current;
                        UnityEngine.Rendering.CompareFunction oldDepth = Handles.zTest;
                        try
                        {
                            Graphics.SetRenderTarget(target); GL.Clear(true, true, Color.clear); Handles.SetCamera(camera);
                            depth.Draw(renderer, sourceMesh); Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
                            InvokeStageMethod(stage, "DrawFlowSplineOverlays");
                            restored = Handles.zTest == UnityEngine.Rendering.CompareFunction.Always;
                            capture.ReadPixels(new Rect(0, 0, 128, 128), 0, 0); capture.Apply(); pixels = capture.GetPixels(); rendered = true;
                        }
                        finally
                        { Handles.zTest = oldDepth; if (oldCamera != null) Handles.SetCamera(oldCamera); RenderTexture.active = previous; }
                    };
                    for (int frame = 0; !rendered && frame < 20; frame++) { window.Repaint(); yield return null; }
                    Assert.That(rendered && restored, Is.True);
                    int front = 0, back = 0;
                    int frontY = Mathf.RoundToInt(camera.WorldToViewportPoint(new Vector3(0f, 0.2f, -0.1f)).y * 128);
                    int backY = Mathf.RoundToInt(camera.WorldToViewportPoint(new Vector3(0f, -0.2f, 0.1f)).y * 128);
                    for (int y = 0; y < 128; y++) for (int x = 57; x <= 71; x++)
                    {
                        Color pixel = pixels[y * 128 + x];
                        if (pixel.g < 0.1f || pixel.g < pixel.r * 2f) continue;
                        if (Mathf.Abs(y - frontY) < 4) front++;
                        if (Mathf.Abs(y - backY) < 4) back++;
                    }
                    if (!editing && !show) Assert.That(front + back, Is.Zero);
                    else { Assert.That(front, Is.GreaterThan(0)); Assert.That(back, depthEnabled ? Is.EqualTo(0) : Is.GreaterThan(0)); }
                    LogAssert.NoUnexpectedReceived();
                }
            }
            finally
            {
                window.Close(); depth.Dispose(); camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target);
                Object.DestroyImmediate(capture); Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(surface); Object.DestroyImmediate(material);
                DestroyEditingStage(stage);
            }
        }

        [Test]
        public void SplineFlowEditorMirrorAttachesToSourceAndReverseSupportsUndo()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0];
            group.modifiers.Clear(); HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group);
            layer.modifiers.Add(flow);
            HairCardStage stage = CreateEditingStage();
            try
            {
                SetStageField(stage, "sourceVertices", sourceMesh.vertices); SetStageField(stage, "sourceNormals", sourceMesh.normals);
                SetStageField(stage, "sourceSurfaceRaycaster", new HairMeshRaycaster(sourceMesh));
                stage.SetActiveModifier(layer.Id, flow.Id);
                flow.flowSplines[0] = TestFlowPath(new Vector3(0.1f, 0f, -0.1f), new Vector3(0.15f, 0f, -0.05f));
                HairFlowSpline path = flow.flowSplines[0]; stage.SelectFlowSpline(path.Id);
                Assert.That(stage.TryMirrorFlowSpline(path, out HairFlowSpline mirror), Is.True);
                Assert.That(mirror.Id, Is.Not.EqualTo(path.Id));
                for (int i = 0; i < path.points.Count; i++)
                {
                    Assert.That(mirror.points[i].IsValid, Is.True);
                    Assert.That(Vector3.Distance(mirror.points[i].CachedLocalPosition, HairBrushInteractionUtility.MirrorX(path.points[i].CachedLocalPosition)), Is.LessThan(1e-6f));
                }
                Undo.ClearUndo(groom); Undo.IncrementCurrentGroup();
                Vector3 start = path.points[0].CachedLocalPosition;
                stage.ChangeFlowSpline("Reverse"); Undo.FlushUndoRecordObjects();
                Assert.That(path.points[1].CachedLocalPosition, Is.EqualTo(start));
                Undo.PerformUndo();
                Assert.That(groom.Groups[0].sculptLayers.Find(x => x.Id == layer.Id).modifiers[0].flowSplines[0].points[0].CachedLocalPosition, Is.EqualTo(start));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void SplineFlowDraftCommitsOnceAndCancelledOrLockedDraftDoesNotModifyGroom()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0]; group.modifiers.Clear();
            HairSculptLayer layer = HairGroomCommands.AddSculptLayer(groom, group); layer.modifiers.Add(flow);
            HairCardStage stage = CreateEditingStage();
            try
            {
                stage.SetActiveModifier(layer.Id, flow.Id); stage.SetFlowTool(true);
                Assert.That(HairWorkflowState.IsToolAllowed(HairWorkflowStep.Groom, stage.SceneTool), Is.True);
                var draft = (List<HairSurfaceAnchor>)typeof(HairCardStage).GetField("flowDraft", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(stage);
                SetStageField(stage, "flowGestureModifierId", flow.Id); draft.AddRange(flow.flowSplines[0].points);
                string before = EditorJsonUtility.ToJson(groom);
                InvokeStageMethod(stage, "CancelFlowGesture"); Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(before));
                Assert.That(draft, Is.Empty);
                SetStageField(stage, "flowGestureModifierId", flow.Id); draft.AddRange(flow.flowSplines[0].points);
                Assert.That(stage.IsEditing, Is.True);
                InvokeStageMethod(stage, "OnUndoRedo");
                Assert.That(draft, Is.Empty); Assert.That(stage.IsEditing, Is.False);
                SetStageField(stage, "flowGestureModifierId", flow.Id); draft.AddRange(flow.flowSplines[0].points);
                Undo.ClearUndo(groom); Undo.IncrementCurrentGroup(); InvokeStageMethod(stage, "CommitFlowGesture"); Undo.FlushUndoRecordObjects();
                Assert.That(flow.flowSplines.Count, Is.EqualTo(2));
                Undo.PerformUndo();
                Assert.That(groom.Groups[0].sculptLayers.Find(x => x.Id == layer.Id).modifiers[0].flowSplines.Count, Is.EqualTo(1));
                InvokeStageMethod(stage, "CancelFlowGesture");
                HairSculptLayer restoredLayer = groom.Groups[0].sculptLayers.Find(x => x.Id == layer.Id);
                restoredLayer.locked = true;
                SetStageField(stage, "flowGestureModifierId", flow.Id); draft.AddRange(flow.flowSplines[0].points);
                InvokeStageMethod(stage, "CommitFlowGesture");
                Assert.That(restoredLayer.modifiers[0].flowSplines.Count, Is.EqualTo(1));
            }
            finally { DestroyEditingStage(stage); }
        }

        [Test]
        public void SplineFlowDenseGroomProfileUsesReusableFieldAndBuffers()
        {
            HairModifierSettings flow = TestSplineFlow(); HairGroup group = groom.Groups[0]; group.guides.Clear();
            for (int i = 0; i < 256; i++)
            {
                Vector3 root = new Vector3((i % 16 - 8) * 0.003f, 0f, (i / 16 - 8) * 0.003f);
                HairGuide guide = CreateLinearGuide("Dense flow " + i, i + 1, root, Vector3.up * 0.3f);
                guide.root = TestFlowAnchor(root); group.guides.Add(guide);
            }
            group.children.childrenPerGuide = 16;
            for (int p = 0; p < 7; p++) flow.flowSplines.Add(TestFlowPath(new Vector3(0f, 0f, (p - 3) * 0.01f), new Vector3(0.3f, 0f, (p - 3) * 0.01f)));
            var workspace = new HairEvaluationWorkspace(); var result = new HairEvaluationResult();
            double[] times = new double[9]; long allocation = 0;
            for (int i = -3; i < times.Length; i++)
            {
                long startBytes = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                HairGroomEvaluator.EvaluateInto(groom, null, workspace, result); timer.Stop();
                if (i >= 0) { times[i] = timer.Elapsed.TotalMilliseconds; allocation += GC.GetAllocatedBytesForCurrentThread() - startBytes; }
            }
            Array.Sort(times);
            TestContext.WriteLine($"Spline Flow: {result.CardCount:N0} cards, 256 guides, 8 paths; median evaluation {times[4]:0.00} ms; mean managed allocation {allocation / times.Length:N0} bytes.");
            Assert.That(result.CardCount, Is.EqualTo(4352));
            Assert.That(result.curves[0].points[1].facingWeight, Is.GreaterThan(0.9f));
            Assert.That(allocation / times.Length, Is.LessThan(20000), "Flow must not allocate per guide or child after warm-up.");
            var meshWorkspace = new HairMeshBuildWorkspace(); HairCardMeshBuildResult build = null;
            try
            {
                for (int i = -2; i < times.Length; i++)
                {
                    var timer = Stopwatch.StartNew();
                    HairGroomEvaluator.EvaluateInto(groom, null, workspace, result);
                    build = HairCardMeshGenerator.Update(result, "Flow benchmark", build, meshWorkspace);
                    timer.Stop(); if (i >= 0) times[i] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(times);
                TestContext.WriteLine($"Spline Flow end-to-end core update: {result.CardCount:N0} cards, {build.vertexCount:N0} vertices; median {times[4]:0.00} ms (evaluation + mesh, excluding Scene rendering).");
            }
            finally { build?.Dispose(); meshWorkspace.Clear(); }
            workspace.Clear();
        }
    }
}
#endif
