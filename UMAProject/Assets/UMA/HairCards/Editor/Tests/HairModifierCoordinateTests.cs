#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private static IEnumerable<TestCaseData> ModifierCoordinateCases()
        {
            foreach (HairModifierType type in Enum.GetValues(typeof(HairModifierType)))
                foreach (HairModifierDomain domain in new[] { HairModifierDomain.GuidesAndChildren, HairModifierDomain.Children })
                    yield return new TestCaseData(type, domain);
        }

        private HairModifierSettings CoordinateModifier(HairModifierType type, HairModifierDomain domain)
        {
            HairGroup group = groom.Groups[0];
            group.guides.Clear();
            HairGuide guide = CreateLinearGuide("Coordinates", 12, Vector3.zero, new Vector3(0.2f, 0.2f, 0.1f));
            guide.points[1].position = new Vector3(0.12f, -0.03f, 0.02f);
            guide.root = TestFlowAnchor(Vector3.zero);
            group.guides.Add(guide);
            group.children.childrenPerGuide = 2;
            group.children.rootSpread = group.children.lengthVariation = group.children.widthVariation = group.children.rollVariation = 0f;
            HairModifierSettings modifier = HairGroomCommands.AddModifier(groom, group, type);
            modifier.domain = domain; modifier.amount = 0.05f;
            if (type == HairModifierType.Resample) modifier.amount = 7f;
            if (type == HairModifierType.Simplify || type == HairModifierType.LodReduction) modifier.amount = 2f;
            if (type == HairModifierType.Width || type == HairModifierType.Length) modifier.amount = 1.3f;
            if (type == HairModifierType.FlowAlign || type == HairModifierType.Clump || type == HairModifierType.HelperFollow || type == HairModifierType.Smooth) modifier.amount = 0.6f;
            modifier.vector = type == HairModifierType.Curl || type == HairModifierType.Wave ? new Vector3(2f, 0.4f, 0f) : new Vector3(0.3f, 1f, -0.2f);
            modifier.gravityCollision = false; modifier.gravitySeparation = 0.15f;
            modifier.flowRadius = 1f; modifier.flowSplines.Add(TestFlowPath(Vector3.zero, Vector3.right * 0.3f));
            var helper = HairGroomCommands.AddHelper(groom, type == HairModifierType.HelperFollow ? HairHelperType.CurveRail : HairHelperType.Sphere, guide.points[1].position);
            helper.points.Clear(); helper.points.Add(Vector3.zero); helper.points.Add(new Vector3(-0.1f, 0.1f, 0)); helper.points.Add(new Vector3(-0.2f, 0.2f, 0));
            helper.radius = 0.08f; modifier.helperId = helper.Id;
            return modifier;
        }

        private static Matrix4x4 CoordinateObjectMatrix(bool nonuniform = true) => Matrix4x4.TRS(
            new Vector3(7f, -3f, 11f), Quaternion.Euler(21f, 75f, -17f), nonuniform ? new Vector3(-2f, 0.6f, 1.7f) : Vector3.one * 2f);

        private static Matrix4x4 CoordinatePoseMatrix()
        {
            Matrix4x4 matrix = Matrix4x4.TRS(new Vector3(0.04f, 0.12f, -0.06f), Quaternion.Euler(-35f, 13f, 28f), new Vector3(1.1f, 0.8f, 1.2f));
            matrix.m01 += 0.17f;
            return matrix;
        }

        private static void AssertCoordinateCurves(HairEvaluationResult expected, HairEvaluationResult actual, float tolerance = 0.00002f)
        {
            Assert.That(actual.curves.Count, Is.EqualTo(expected.curves.Count));
            for (int c = 0; c < expected.curves.Count; c++)
            {
                HairEvaluatedCurve a = expected.curves[c], b = actual.curves[c];
                Assert.That(b.points.Count, Is.EqualTo(a.points.Count));
                Assert.That(Vector3.Distance(a.rootNormal, b.rootNormal), Is.LessThan(tolerance));
                Assert.That(b.samplesPerCardOverride, Is.EqualTo(a.samplesPerCardOverride));
                for (int i = 0; i < a.points.Count; i++)
                {
                    Assert.That(float.IsFinite(b.points[i].position.sqrMagnitude), Is.True);
                    Assert.That(Vector3.Distance(a.points[i].position, b.points[i].position), Is.LessThan(tolerance), $"Curve {c}, point {i}");
                    Assert.That(b.points[i].width, Is.EqualTo(a.points[i].width).Within(tolerance));
                    Assert.That(b.points[i].roll, Is.EqualTo(a.points[i].roll).Within(tolerance));
                    Assert.That(Vector3.Distance(a.points[i].facingNormal, b.points[i].facingNormal), Is.LessThan(tolerance));
                }
            }
        }

        [TestCaseSource(nameof(ModifierCoordinateCases))]
        public void EveryModifierKeepsOutputSourceLocalAndIgnoresWorldTranslation(HairModifierType type, HairModifierDomain domain)
        {
            CoordinateModifier(type, domain);
            Matrix4x4 pose = CoordinatePoseMatrix();
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false, sourceToWorld = CoordinateObjectMatrix(),
                guideToSourcePose = _ => pose, worldGravity = Vector3.down * 9.81f };
            HairEvaluationResult expected = HairGroomEvaluator.Evaluate(groom, options);
            string authored = EditorJsonUtility.ToJson(groom);
            options.sourceToWorld = Matrix4x4.Translate(new Vector3(100, -50, 80)) * options.sourceToWorld;
            AssertCoordinateCurves(expected, HairGroomEvaluator.Evaluate(groom, options));
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(authored));
            // Source-defined effects operate before the display pose. Gravity and Lift are
            // deliberately context-aware: forces and normals cannot be treated as plain vectors.
            if (type != HairModifierType.Gravity && type != HairModifierType.Lift)
                AssertCoordinateCurves(expected, HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { evaluateSurfaceAnchors = false }));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void LiftUsesEachRootNormalNotOneFixedAxis(int axis)
        {
            Vector3 normal = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.back }[axis];
            Vector3 tangent = Mathf.Abs(Vector3.Dot(normal, Vector3.forward)) < 0.5f ? Vector3.forward : Vector3.up;
            var group = groom.Groups[0]; group.guides.Clear();
            var guide = CreateLinearGuide("Lift", 3, Vector3.zero, tangent * 0.3f);
            guide.root = HairSurfaceAnchor.Create("mesh:test", 0, 0, new Vector3(0.25f, 0.25f, 0.5f), 0f, Vector3.zero, normal);
            group.guides.Add(guide);
            var modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Lift);
            modifier.vector = -normal; modifier.amount = 0.12f;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            modifier.enabled = false; var before = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0]; modifier.enabled = true;
            var after = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            AssertFlowLengths(before, after);
            Assert.That(Vector3.Dot(after.points[^1].position - before.points[^1].position, normal), Is.GreaterThan(0.05f));
            modifier.vector = new Vector3(float.NaN, 42, -300);
            var unchanged = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(unchanged.points[^1].position, Is.EqualTo(after.points[^1].position), "The obsolete fixed direction must not steer Lift.");
            modifier.amount = -0.12f;
            Assert.That(Vector3.Dot(HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0].points[^1].position, normal), Is.LessThan(-0.05f));
        }

        [TestCase(false)] [TestCase(true)]
        public void LiftUsesInverseTransposeNormalsButInverseDisplacementsUnderScaleAndPose(bool closestSurface)
        {
            sourceMesh.triangles = new[] { 0, 2, 1 }; // outward +Y
            var group = groom.Groups[0]; group.guides.Clear();
            group.guides.Add(CreateLinearGuide("Lift normal", 1, Vector3.zero, Vector3.right * 0.3f));
            group.guides[0].root = TestFlowAnchor(Vector3.zero);
            var modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Lift);
            modifier.amount = 0.1f; modifier.liftNormalMode = closestSurface ? HairLiftNormalMode.ClosestSurfaceNormal : HairLiftNormalMode.RootNormal;
            Matrix4x4 pose = CoordinatePoseMatrix(), objectMatrix = CoordinateObjectMatrix();
            Matrix4x4 toWorld = objectMatrix * pose;
            Vector3 worldNormal = toWorld.inverse.transpose.MultiplyVector(Vector3.up).normalized;
            Vector3 localDisplacement = HairCoordinateUtility.NormalDisplacement(Vector3.up, toWorld.inverse);
            Assert.That(Vector3.Angle(toWorld.MultiplyVector(localDisplacement), worldNormal), Is.LessThan(0.03f));
            Assert.That(Mathf.Abs(Vector3.Dot(worldNormal, toWorld.MultiplyVector(Vector3.right).normalized)), Is.LessThan(1e-5f));
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false, sourceToWorld = objectMatrix, guideToSourcePose = _ => pose };
            modifier.enabled = false; var before = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0]; modifier.enabled = true;
            var after = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            AssertFlowLengths(before, after);
            Vector3 change = toWorld.MultiplyVector(after.points[^1].position - before.points[^1].position);
            Assert.That(Vector3.Dot(change, worldNormal), Is.GreaterThan(0.01f));
            // Reproduce the target independently, then apply the same inextensibility rule.
            var original = new List<Vector3>(); var target = new List<Vector3>();
            foreach (var point in before.points) { original.Add(point.position); target.Add(point.position); }
            for (int i = 1; i < target.Count; i++) target[i] += localDisplacement * modifier.amount * (i / (target.Count - 1f));
            HairGuideShapeUtility.PreserveSegmentLengths(original, target, group.guides[0].points);
            for (int i = 0; i < target.Count; i++) Assert.That(Vector3.Distance(target[i], after.points[i].position), Is.LessThan(2e-6f));
        }

        [Test]
        public void LiftClosestFaceModeUsesGeometryNotCachedRootAndRefreshesMeshEdits()
        {
            sourceMesh.triangles = new[] { 0, 2, 1 };
            var group = groom.Groups[0]; group.guides.Clear();
            var guide = CreateLinearGuide("Normals", 2, new Vector3(0, 0.02f, 0), Vector3.forward * 0.2f);
            guide.root = HairSurfaceAnchor.Create("mesh:test", 0, 0, new Vector3(0.25f, 0.25f, 0.5f), 0f, Vector3.zero, Vector3.right);
            group.guides.Add(guide);
            var modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Lift); modifier.amount = 0.1f;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            var workspace = new HairEvaluationWorkspace();
            var root = HairGroomEvaluator.Evaluate(groom, options, workspace).evaluatedGuides[0];
            modifier.liftNormalMode = HairLiftNormalMode.ClosestSurfaceNormal;
            var surface = HairGroomEvaluator.Evaluate(groom, options, workspace).evaluatedGuides[0];
            Assert.That(root.points[^1].position.x, Is.GreaterThan(0.04f));
            Assert.That(surface.points[^1].position.x, Is.EqualTo(0).Within(1e-6f));
            Assert.That(surface.points[^1].position.y, Is.GreaterThan(guide.points[^1].position.y + 0.04f));
            sourceMesh.triangles = new[] { 0, 1, 2 };
            var reversed = HairGroomEvaluator.Evaluate(groom, options, workspace).evaluatedGuides[0];
            Assert.That(reversed.points[^1].position.y, Is.LessThan(guide.points[^1].position.y));
            workspace.Clear();
        }

        [TestCase(false)] [TestCase(true)]
        public void LiftProtectsFrozenRootAndRootInfluenceAndPersistsNormalMode(bool closestSurface)
        {
            sourceMesh.triangles = new[] { 0, 2, 1 };
            var group = groom.Groups[0]; group.guides.Clear(); group.guides.Add(CreateLinearGuide("Root", 3, Vector3.zero, Vector3.right * 0.3f));
            group.guides[0].root = TestFlowAnchor(Vector3.zero);
            var modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Lift);
            modifier.liftNormalMode = closestSurface ? HairLiftNormalMode.ClosestSurfaceNormal : HairLiftNormalMode.RootNormal;
            modifier.amount = 0.15f;
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            var full = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            modifier.rootInfluence = 0f;
            var protectedRoot = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(protectedRoot.points[1].position.y, Is.LessThan(full.points[1].position.y));
            group.guides[0].points[1].freeze = 1f;
            var frozen = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            Assert.That(frozen.points[0].position, Is.EqualTo(group.guides[0].points[0].position));
            Assert.That(frozen.points[1].position, Is.EqualTo(group.guides[0].points[1].position));
            Assert.That(modifier.Duplicate().liftNormalMode, Is.EqualTo(modifier.liftNormalMode));
            Assert.That(JsonUtility.FromJson<HairModifierSettings>(JsonUtility.ToJson(modifier)).liftNormalMode, Is.EqualTo(modifier.liftNormalMode));
            options.sourceToWorld = Matrix4x4.Scale(new Vector3(1, 0, 1));
            var singular = HairGroomEvaluator.Evaluate(groom, options).evaluatedGuides[0];
            for (int i = 0; i < singular.points.Count; i++) Assert.That(singular.points[i].position, Is.EqualTo(group.guides[0].points[i].position));
        }

        [TestCase(HairHelperType.Plane)] [TestCase(HairHelperType.Sphere)] [TestCase(HairHelperType.Box)] [TestCase(HairHelperType.Capsule)]
        public void ExternalHelperSnapshotsPreserveRelativeScaleReflectionShearAndCollision(HairHelperType type)
        {
            Matrix4x4 sourceToWorld = CoordinateObjectMatrix(), sourceToPose = CoordinatePoseMatrix();
            Matrix4x4 helperToWorld = Matrix4x4.TRS(new Vector3(0.04f, -0.05f, 0.06f), Quaternion.Euler(12, -20, 8), new Vector3(-0.8f, 1.3f, 0.6f));
            var helper = new HairHelper { type = type, embedded = false, radius = 0.3f, size = Vector3.one }; helper.EnsureIntegrity();
            Assert.That(HairCardStage.SyncExternalHelperTransform(helper, sourceToWorld, sourceToPose, helperToWorld), Is.True);
            Matrix4x4 expected = sourceToPose.inverse * sourceToWorld.inverse * helperToWorld;
            AssertMatrixNear(helper.LocalToSource, expected);
            AssertMatrixNear(sourceToWorld * sourceToPose * helper.LocalToSource, helperToWorld, 0.00002f);
            helper.EnsureIntegrity();
            var copy = JsonUtility.FromJson<HairHelper>(JsonUtility.ToJson(helper));
            AssertMatrixNear(copy.LocalToSource, expected);
            Vector3 localPoint = type == HairHelperType.Plane ? new Vector3(0, -0.1f, 0) : new Vector3(0.05f, 0.04f, 0.03f);
            Vector3 point = expected.MultiplyPoint3x4(localPoint);
            var push = typeof(HairGroomEvaluator).GetMethod("PushOutsideHelper", BindingFlags.Static | BindingFlags.NonPublic);
            Vector3 result = (Vector3)push.Invoke(null, new object[] { point, copy, 1f, 0f });
            Vector3 helperResult = expected.inverse.MultiplyPoint3x4(result);
            Assert.That(Vector3.Distance(result, point), Is.GreaterThan(0.001f));
            if (type == HairHelperType.Plane) Assert.That(helperResult.y, Is.EqualTo(0).Within(2e-5f));
            if (type == HairHelperType.Sphere) Assert.That(helperResult.magnitude, Is.EqualTo(helper.radius).Within(2e-5f));
            if (type == HairHelperType.Box) Assert.That(Mathf.Max(Mathf.Abs(helperResult.x), Mathf.Max(Mathf.Abs(helperResult.y), Mathf.Abs(helperResult.z))), Is.EqualTo(0.5f).Within(2e-5f));
            if (type == HairHelperType.Capsule)
            {
                Vector3 axis = new Vector3(0, Mathf.Clamp(helperResult.y, -0.2f, 0.2f), 0);
                Assert.That(Vector3.Distance(helperResult, axis), Is.EqualTo(0.3f).Within(2e-5f));
            }
            Assert.That(HairCardStage.SyncExternalHelperTransform(helper, Matrix4x4.zero, sourceToPose, helperToWorld), Is.False);
            AssertMatrixNear(helper.LocalToSource, expected, 2e-5f);
            copy.embedded = true;
            AssertMatrixNear(copy.LocalToSource, Matrix4x4.TRS(copy.position, copy.rotation, copy.scale));
        }

        private static void AssertMatrixNear(Matrix4x4 actual, Matrix4x4 expected, float tolerance = 1e-5f)
        { for (int i = 0; i < 16; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance), $"Matrix element {i}"); }

        [Test]
        public void ExternalHelperSyncMarksChangedSnapshotsDirtyAndRetainsWorldRelativeCurvePoints()
        {
            using var scope = new TestSculptStageScope(this, CreateEditingStage());
            var target = new GameObject("Coordinate helper"); var child = new GameObject("Coordinate helper tip");
            try
            {
                var identity = target.AddComponent<UMA.HairCards.Runtime.HairHelperId>(); identity.CreateNewId();
                var helper = HairGroomCommands.AddHelper(groom, HairHelperType.CurveRail, Vector3.zero);
                helper.embedded = false; helper.externalHelperId = identity.Id;
                target.transform.SetPositionAndRotation(new Vector3(1, 2, 3), Quaternion.Euler(15, 30, 45));
                target.transform.localScale = new Vector3(-2, 0.7f, 1.3f);
                child.transform.SetParent(target.transform, false); child.transform.localPosition = Vector3.up * 0.2f;
                int before = EditorUtility.GetDirtyCount(groom);
                InvokeStageMethod(scope.Stage, "SyncExternalHelpers");
                Assert.That(EditorUtility.GetDirtyCount(groom), Is.GreaterThan(before));
                AssertMatrixNear(helper.LocalToSource, target.transform.localToWorldMatrix);
                Assert.That(Vector3.Distance(helper.points[1], child.transform.position), Is.LessThan(1e-5f));
                before = EditorUtility.GetDirtyCount(groom);
                InvokeStageMethod(scope.Stage, "SyncExternalHelpers");
                Assert.That(EditorUtility.GetDirtyCount(groom), Is.EqualTo(before), "Unchanged external bindings must not dirty the groom every frame.");
                Object.DestroyImmediate(child);
                InvokeStageMethod(scope.Stage, "SyncExternalHelpers");
                Assert.That(helper.points.Count, Is.EqualTo(2));
                Assert.That(Vector3.Distance(helper.points[1], target.transform.TransformPoint(Vector3.up * 0.2f)), Is.LessThan(1e-5f));
            }
            finally { if (child != null) Object.DestroyImmediate(child); Object.DestroyImmediate(target); }
        }

        [UnityTest]
        public IEnumerator LiftNormalPropertiesRenderInNarrowAndWideWindows()
        {
            var layer = HairGroomCommands.AddSculptLayer(groom, groom.Groups[0]);
            var modifier = HairGroomCommands.AddModifier(groom, groom.Groups[0], HairModifierType.Lift, layer);
            using var scope = new TestSculptStageScope(this, CreateEditingStage()); var stage = scope.Stage;
            stage.SetActiveGroup(groom.Groups[0].Id); stage.SetActiveModifier(layer.Id, modifier.Id);
            var properties = ScriptableObject.CreateInstance<HairGroomWorkspace>();
            var active = typeof(HairCardStage).GetField("<ActiveStage>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            object previous = active.GetValue(null);
            try
            {
                active.SetValue(null, stage); properties.Show();
                foreach (int width in new[] { 320, 650 })
                    foreach (HairLiftNormalMode mode in Enum.GetValues(typeof(HairLiftNormalMode)))
                    {
                        modifier.liftNormalMode = mode; properties.position = new Rect(30, 50, width, 900);
                        properties.Repaint(); yield return null; yield return null; LogAssert.NoUnexpectedReceived();
                    }
            }
            finally { properties.Close(); active.SetValue(null, previous); }
        }
    }
}
#endif
