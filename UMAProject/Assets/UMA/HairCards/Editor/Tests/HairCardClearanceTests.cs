#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [Test]
        public void ClosestScalpPointKeepsMillimeterTriangleWindingNormal()
        {
            sourceMesh.vertices = new[]{Vector3.zero,Vector3.forward*.002f,Vector3.right*.002f};
            sourceMesh.triangles = new[]{0,1,2};
            var query = new HairMeshRaycaster(sourceMesh);
            var point = new Vector3(.0005f,-.004f,.0005f);
            Assert.That(query.ClosestPoint(point,out var hit),Is.True);
            Assert.That(hit.Normal.y,Is.EqualTo(1).Within(1e-6f));
            Assert.That(Vector3.Dot(point-hit.Point,hit.Normal),Is.EqualTo(-.004f).Within(1e-6f));
            // Exercise the cached-patch path through the public build API as well.
            profile.Configure(HairCardShape.Ribbon,.0002f,.0002f,2,generateBackfaces:false);
            var curve = new HairEvaluatedCurve { profile=profile, rootNormal=Vector3.up,
                preventCardPenetration=true, cardSurfaceClearance=.0005f };
            curve.points.Add(new HairCurvePoint(point,.0002f,0));
            curve.points.Add(new HairCurvePoint(point+new Vector3(0,.001f,.0002f),.0002f,0));
            var evaluation = new HairEvaluationResult {cardCollisionMesh=sourceMesh}; evaluation.curves.Add(curve);
            using var build=HairCardMeshGenerator.Build(evaluation);
            Assert.That(build.mesh.vertices.Min(v=>v.y),Is.GreaterThan(.00049f));
        }

        [TestCase(HairCardShape.Ribbon, false, 1)]
        [TestCase(HairCardShape.Ribbon, true, 3)]
        [TestCase(HairCardShape.TaperedTube, false, 1)]
        public void CardClearanceChecksFacesWithoutChangingGuidesWidthOrTopology(HairCardShape shape, bool backfaces, int spans)
        {
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var surface = Object.Instantiate(ball.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(ball);
            Vector3 center = new Vector3(1.2f, .8f, -.4f);
            surface.vertices = surface.vertices.Select(v => center + v * .2f).ToArray(); surface.RecalculateBounds();
            profile.Configure(shape, .012f, .012f, 2, 6, backfaces); profile.ConfigureRibbon(spans, .1f, false);
            var curve = new HairEvaluatedCurve { profile = profile, rootNormal = Vector3.up, cardSurfaceClearance = .0005f };
            // Two endpoints outside a convex scalp; their connecting low-LOD face crosses inside it.
            curve.points.Add(new HairCurvePoint(center + new Vector3(0,.091f,-.045f), .012f, 18));
            curve.points.Add(new HairCurvePoint(center + new Vector3(0,.091f,.045f), .012f, 18));
            var snapshot = curve.points.Select(p=>p.position).ToArray();
            var evaluation = new HairEvaluationResult { cardCollisionMesh = surface }; evaluation.curves.Add(curve);
            try
            {
                using var before = HairCardMeshGenerator.Build(evaluation);
                var query = new HairMeshRaycaster(surface);
                Assert.That(WorstCardDistance(before.mesh,query), Is.LessThan(-.003f));
                curve.preventCardPenetration = true;
                using var after = HairCardMeshGenerator.Build(evaluation);
                Assert.That(WorstCardDistance(after.mesh,query), Is.GreaterThan(-.0001f));
                Assert.That(after.mesh.triangles, Is.EqualTo(before.mesh.triangles));
                Assert.That(after.mesh.uv, Is.EqualTo(before.mesh.uv));
                Assert.That(curve.points.Select(p=>p.position), Is.EqualTo(snapshot));
                int columns=after.vertexCount/2;
                for(int row=0;row<2;row++)
                    Assert.That(Vector3.Distance(after.mesh.vertices[row*columns],after.mesh.vertices[row*columns+1]),
                        Is.EqualTo(Vector3.Distance(before.mesh.vertices[row*columns],before.mesh.vertices[row*columns+1])).Within(1e-6f));
                var normals=after.mesh.normals;var tangents=after.mesh.tangents;
                for(int i=0;i<normals.Length;i++)
                {
                    Assert.That(normals[i].magnitude,Is.EqualTo(1).Within(1e-4f));
                    Assert.That(((Vector3)tangents[i]).magnitude,Is.EqualTo(1).Within(1e-4f));
                    Assert.That(Vector3.Dot(normals[i],tangents[i]),Is.EqualTo(0).Within(1e-4f));
                }
            }
            finally { Object.DestroyImmediate(surface); }
        }

        [Test]
        public void CardClearanceRetainsIntentionalRootEmbeddingAndHasNoBuildDrift()
        {
            sourceMesh.triangles = new[]{0,2,1}; // Outward +Y winding.
            profile.Configure(HairCardShape.Ribbon,.012f,.004f,5,generateBackfaces:false);
            var curve = new HairEvaluatedCurve { profile=profile, rootNormal=Vector3.up, rootEmbedDepth=.002f,
                preventCardPenetration=true, cardSurfaceClearance=.0005f };
            curve.points.Add(new HairCurvePoint(Vector3.zero,.012f,0));
            curve.points.Add(new HairCurvePoint(Vector3.up*.05f,.004f,0));
            var evaluation = new HairEvaluationResult {cardCollisionMesh=sourceMesh};evaluation.curves.Add(curve);
            var workspace = new HairMeshBuildWorkspace();
            using var first=HairCardMeshGenerator.Build(evaluation,"First",true,workspace);
            using var second=HairCardMeshGenerator.Build(evaluation,"Second",true,workspace);
            Assert.That(first.mesh.vertices[0].y,Is.EqualTo(-.002f).Within(2e-5f));
            Assert.That(first.mesh.vertices,Is.EqualTo(second.mesh.vertices));
            Assert.That(curve.points[0].position,Is.EqualTo(Vector3.zero));
            Assert.That(curve.points[1].position,Is.EqualTo(Vector3.up*.05f));
            workspace.Clear();
        }

        [Test]
        public void CardClearanceCacheRefreshesInPlaceMeshEditsAndToggleOff()
        {
            sourceMesh.triangles=new[]{0,2,1};
            var group=groom.Groups[0];group.preventCardPenetration=true;group.cardSurfaceClearance=.001f;
            var evaluator=new HairEvaluationWorkspace();var meshWorkspace=new HairMeshBuildWorkspace();var result=new HairEvaluationResult();
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            HairGroomEvaluator.EvaluateInto(groom,options,evaluator,result);
            using var first=HairCardMeshGenerator.Build(result,"First",false,meshWorkspace);
            var positions=sourceMesh.vertices;for(int i=0;i<positions.Length;i++)positions[i]+=Vector3.up*.02f;
            sourceMesh.vertices=positions;sourceMesh.RecalculateBounds();
            using var moved=HairCardMeshGenerator.Build(result,"Moved",false,meshWorkspace);
            Assert.That(moved.mesh.vertices.Min(v=>v.y),Is.GreaterThan(.0209f));
            group.preventCardPenetration=false;
            HairGroomEvaluator.EvaluateInto(groom,options,evaluator,result);
            Assert.That(result.curves.All(c=>!c.preventCardPenetration),Is.True);
            using var disabled=HairCardMeshGenerator.Build(result,"Disabled",false,meshWorkspace);
            Assert.That(disabled.mesh.vertices.Min(v=>v.y),Is.LessThan(.02f));
            evaluator.Clear();meshWorkspace.Clear();
        }

        [Test]
        public void CardClearanceCachedBuildMatchesFreshAcrossGeometryAndSettingChanges()
        {
            sourceMesh.triangles = new[]{0,2,1};
            var group = groom.Groups[0]; group.preventCardPenetration = true;
            var ew = new HairEvaluationWorkspace(); var mw = new HairMeshBuildWorkspace(); var evaluation = new HairEvaluationResult();
            var options = new HairEvaluationOptions { evaluateSurfaceAnchors = false };
            try
            {
                for (int pass = 0; pass < 7; pass++)
                {
                    if (pass == 2) group.cardSurfaceClearance = .002f;
                    if (pass == 3) { group.guides[0].points[1].position += Vector3.down*.02f; group.guides[0].points[1].roll += 20; }
                    if (pass == 4) { group.rootEmbedDepth=.001f; profile.ConfigureRibbon(3,.15f,false); }
                    if (pass == 5) profile.Configure(HairCardShape.TaperedTube,.03f,.006f,8,5,false);
                    if (pass == 6) { var v=sourceMesh.vertices; for(int i=0;i<v.Length;i++)v[i]+=Vector3.up*.01f; sourceMesh.vertices=v; }
                    HairGroomEvaluator.EvaluateInto(groom, options, ew, evaluation);
                    using var cached = HairCardMeshGenerator.Build(evaluation,"cached",true,mw);
                    using var fresh = HairCardMeshGenerator.Build(evaluation,"fresh",true);
                    Assert.That(cached.mesh.vertices,Is.EqualTo(fresh.mesh.vertices),"positions at pass "+pass);
                    Assert.That(cached.mesh.normals,Is.EqualTo(fresh.mesh.normals),"normals at pass "+pass);
                    Assert.That(cached.mesh.tangents,Is.EqualTo(fresh.mesh.tangents),"tangents at pass "+pass);
                    Assert.That(cached.mesh.triangles,Is.EqualTo(fresh.mesh.triangles));
                }
            }
            finally { ew.Clear();mw.Clear(); }
        }

        [Test]
        public void CardClearanceFollowsRotatedTranslatedAvatarPreviewPose()
        {
            sourceMesh.triangles = new[]{0,2,1};
            groom.Groups[0].preventCardPenetration = true;
            var posed = Object.Instantiate(sourceMesh);
            var transform = Matrix4x4.TRS(new Vector3(2,3,4),Quaternion.Euler(30,40,60),Vector3.one);
            posed.vertices = sourceMesh.vertices.Select(transform.MultiplyPoint3x4).ToArray();
            posed.normals = sourceMesh.normals.Select(transform.MultiplyVector).ToArray(); posed.RecalculateBounds();
            try
            {
                var original = HairGroomEvaluator.Evaluate(groom,new HairEvaluationOptions{evaluateSurfaceAnchors=false});
                var root = original.curves[0].points[0]; root.position -= Vector3.up*.003f; original.curves[0].points[0]=root;
                var pose = new HairAuthoringPose(sourceMesh,posed);
                var transformed = pose.TransformEvaluation(groom,original);
                Assert.That(transformed.cardCollisionMesh,Is.SameAs(posed));
                Assert.That(original.cardCollisionMesh,Is.SameAs(sourceMesh));
                using var build = HairCardMeshGenerator.Build(transformed);
                Assert.That(WorstCardDistance(build.mesh,new HairMeshRaycaster(posed)),Is.GreaterThan(-.0001f));
                Assert.That(original.curves[0].points[0].position,Is.EqualTo(root.position));
            }
            finally { Object.DestroyImmediate(posed); }
        }

        // More samples than the solver: catches faces that chord through the scalp
        // despite safe vertices. Also includes the edges, for both material passes.
        private static float WorstCardDistance(Mesh mesh, HairMeshRaycaster surface, int samples = 8)
        {
            float minimum=float.PositiveInfinity;var v=mesh.vertices;var indices=mesh.GetTriangles(0);
            for(int i=0;i<indices.Length;i+=3)
                for(int y=0;y<=samples;y++)for(int x=0;x<=samples-y;x++)
                {
                    var p=v[indices[i]]*(1f-(x+y)/(float)samples)+v[indices[i+1]]*(x/(float)samples)+v[indices[i+2]]*(y/(float)samples);
                    if(surface.ClosestPoint(p,out var hit))minimum=Mathf.Min(minimum,Vector3.Dot(p-hit.Point,hit.Normal));
                }
            return minimum;
        }
    }
}
#endif
