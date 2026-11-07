#if UNITY_INCLUDE_TESTS
using System.Linq;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        private HairHelper Form(HairPopulationSource source)
        {
            var group = groom.Groups[0]; group.guides.Clear(); group.generation.enabled = true;
            group.generation.cards.source = source; group.generation.cards.count = 30;
            group.generation.cards.form.segments = 60;
            group.generation.cards.form.adaptiveSamples = false;
            groom.Lods[0].useProfileSamples = true;
            profile.Configure(HairCardShape.Ribbon, .012f, .008f, 8, generateBackfaces: false);
            profile.ConfigureRibbon(1, 0, false);
            var h = new HairHelper { type = source == HairPopulationSource.Braid ? HairHelperType.BraidRail : HairHelperType.GuideGrid };
            h.EnsureIntegrity(); groom.SharedHelpers.Add(h);
            if (source == HairPopulationSource.GridPanels) HairFormUtility.CreateGrid(h, 3, 5, new Vector3(0,.02f,0), Vector3.right * .1f, Vector3.forward * .25f);
            else { h.points.Add(new Vector3(0,.02f,0)); h.points.Add(new Vector3(0,.3f,0)); }
            group.generation.cards.form.helperIds.Add(h.Id); return h;
        }
        [TestCase(HairPopulationSource.GridPanels)]
        [TestCase(HairPopulationSource.Braid)]
        public void FormsGenerateWithoutAuthoredGuidesAndHonorBudgets(HairPopulationSource source)
        {
            Form(source); var e = HairGroomEvaluator.Evaluate(groom);
            Assert.That(e.CardCount, Is.EqualTo(30)); Assert.That(e.warnings, Is.Empty);
            using var mesh = HairCardMeshGenerator.Build(e);
            Assert.That(mesh.triangleCount, Is.EqualTo(30 * 60 * 2)); Assert.That(mesh.degenerateTriangleCount, Is.Zero);
            Assert.That(mesh.mesh.vertices.All(p=>float.IsFinite(p.sqrMagnitude)), Is.True);
            Assert.That(e.curves.All(c=>c.rootAnchor.IsValid), Is.True);
            Assert.That(e.curves.All(c=>c.points[0].position.y > 0), Is.True, "Form roots must not be teleported to the skin.");
        }
        [TestCase(HairPopulationSource.GridPanels)]
        [TestCase(HairPopulationSource.Braid)]
        public void FormLodKeepsExactShapeAndStableCardSubset(HairPopulationSource source)
        {
            Form(source); var full = HairGroomEvaluator.Evaluate(groom);
            groom.Lods[0].cardFraction = .5f; groom.Lods[0].useProfileSamples = false; groom.Lods[0].samplesPerCard = 8;
            var low = HairGroomEvaluator.Evaluate(groom);
            Assert.That(low.CardCount, Is.InRange(1, 29));
            foreach (var curve in low.curves)
            {
                var original = full.curves.Single(c=>c.curveId == curve.curveId);
                Assert.That(curve.points.Select(p=>p.position), Is.EqualTo(original.points.Select(p=>p.position)));
                Assert.That(curve.samplesPerCardOverride, Is.EqualTo(8));
            }
        }
        [Test]
        public void FormGeometryIsSourceLocalAndEvaluationDoesNotMutateHelpers()
        {
            var h = Form(HairPopulationSource.Braid); string before = JsonUtility.ToJson(h);
            var original = HairGroomEvaluator.Evaluate(groom);
            // The form's object transform is a preview/bake concern. Moving points in source
            // coordinates must translate generated geometry exactly, not also apply helper.position.
            var offset = new Vector3(.15f,.2f,-.1f);
            for(int i=0;i<h.points.Count;i++) h.points[i] += offset;
            h.position += offset;
            var moved = HairGroomEvaluator.Evaluate(groom);
            for(int c=0;c<original.curves.Count;c++) for(int p=0;p<original.curves[c].points.Count;p++)
                Assert.That(Vector3.Distance(moved.curves[c].points[p].position, original.curves[c].points[p].position + offset), Is.LessThan(1e-6f));
            string snapshot = JsonUtility.ToJson(h); HairGroomEvaluator.Evaluate(groom); Assert.That(JsonUtility.ToJson(h), Is.EqualTo(snapshot));
        }
        [Test]
        public void GridResamplingAndReverseKeepSurfaceAndFacing()
        {
            var h = Form(HairPopulationSource.GridPanels);
            var p = HairFormUtility.GridPoint(h,.37f,.63f); var normal = HairFormUtility.GridNormal(h,.37f,.63f,true);
            HairFormUtility.ResizeGrid(h, 6, 9);
            Assert.That(Vector3.Distance(HairFormUtility.GridPoint(h,.37f,.63f),p), Is.LessThan(1e-6f));
            HairFormUtility.Reverse(h);
            Assert.That(Vector3.Distance(HairFormUtility.GridPoint(h,.63f,.37f),p), Is.LessThan(1e-6f));
            Assert.That(Vector3.Dot(HairFormUtility.GridNormal(h,.63f,.37f,true),normal), Is.GreaterThan(.999f));
        }
        [Test]
        public void BraidCrossingsHaveOppositeDepthRatherThanHelicalOrbit()
        {
            float phase = -Mathf.PI / 3;
            var a = HairFormUtility.Weave(phase); var b = HairFormUtility.Weave(phase + 2 * Mathf.PI / 3);
            Assert.That(a.x, Is.EqualTo(b.x).Within(1e-5));
            Assert.That(a.y * b.y, Is.LessThan(-.7f));
        }
        [Test]
        public void FormMissingHelpersSkipSafelyAndDuplicateDoesNotAliasSettings()
        {
            Form(HairPopulationSource.GridPanels);
            var pipeline = groom.Groups[0].generation;
            var copy = pipeline.cards.Duplicate(); copy.form.cardWidth = .03f; copy.form.helperIds.Clear();
            Assert.That(pipeline.cards.form.cardWidth, Is.Not.EqualTo(.03f)); Assert.That(pipeline.cards.form.helperIds.Count, Is.EqualTo(1));
            var fresh = pipeline.DuplicateForNewGroom(); Assert.That(fresh.cards.form.helperIds, Is.Empty);
            groom.SharedHelpers.Clear(); var result = HairGroomEvaluator.Evaluate(groom);
            Assert.That(result.CardCount, Is.Zero); Assert.That(result.warnings, Is.Not.Empty);
        }
        [Test]
        public void FormsRespectStageToggleAndApplyPopulationModifierOnce()
        {
            Form(HairPopulationSource.GridPanels); var stage = groom.Groups[0].generation.cards;
            var baseline = HairGroomEvaluator.Evaluate(groom);
            stage.modifiers.Add(new HairModifierSettings { type=HairModifierType.Width, amount=.5f, domain=HairModifierDomain.Children });
            var changed = HairGroomEvaluator.Evaluate(groom);
            Assert.That(changed.CardCount, Is.EqualTo(baseline.CardCount));
            Assert.That(changed.curves[0].points.Last().width, Is.EqualTo(baseline.curves[0].points.Last().width * .5f).Within(1e-7f));
            // Active toggle must disable the generator without deleting authored helpers.
            stage.enabled=false; Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount, Is.Zero);
            Assert.That(groom.SharedHelpers.Count, Is.EqualTo(1));
        }

        [Test]
        public void BunCoilAndSoftEditingAreFiniteRootAwareAndUndoable()
        {
            var helper = Form(HairPopulationSource.Braid); helper.coilRadius=.04f; helper.coilTurns=1.5f; helper.coilRise=.02f;
            HairFormUtility.CoilRail(helper);
            Assert.That(helper.points.Count, Is.EqualTo(25));
            Assert.That(helper.points.All(p=>float.IsFinite(p.sqrMagnitude)), Is.True);
            Assert.That(helper.points.Last().y-helper.points[0].y, Is.EqualTo(.02f).Within(1e-6));
            var stage=CreateEditingStage();
            try
            {
                var original=helper.points.ToArray();stage.FormSoftRadius=0;stage.FormEditAll=false;
                HairFormEditor.MovePoint(stage,helper,0,original[0]+Vector3.up*.01f);
                Assert.That(helper.points.Skip(1), Is.EqualTo(original.Skip(1)));
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();
                Assert.That(groom.FindHelper(helper.Id).points, Is.EqualTo(original));
                helper=groom.FindHelper(helper.Id);stage.FormEditAll=true;
                HairFormEditor.MovePoint(stage,helper,0,original[0]+Vector3.right*.01f);
                for(int i=0;i<original.Length;i++) Assert.That(Vector3.Distance(helper.points[i],original[i]+Vector3.right*.01f),Is.LessThan(1e-6));
            }
            finally{Undo.ClearUndo(groom);DestroyEditingStage(stage);}
        }

        [Test]
        public void AdaptiveFormSamplingIsBoundedAndWidthControlChangesActualMesh()
        {
            Form(HairPopulationSource.GridPanels); var population=groom.Groups[0].generation.cards;
            population.form.adaptiveSamples=true;
            using var narrow=HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom));
            population.form.cardWidth*=2;
            using var wide=HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom));
            Assert.That(wide.triangleCount,Is.LessThanOrEqualTo(population.count*population.form.segments*2));
            Assert.That(wide.mesh.bounds.size.x,Is.GreaterThan(narrow.mesh.bounds.size.x));
            Assert.That(wide.degenerateTriangleCount,Is.Zero);
            var helper=groom.SharedHelpers[0]; population.source=HairPopulationSource.Braid;helper.type=HairHelperType.BraidRail;
            helper.points.Clear();helper.points.Add(Vector3.zero);helper.points.Add(Vector3.up*.2f);
            groom.Lods[0].cardFraction=0;Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount,Is.Zero);
            groom.Lods[0].cardFraction=.001f;Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount,Is.GreaterThanOrEqualTo(3));
        }

        [TestCase("BraidedBun_HairGroom")]
        [TestCase("BraidedBun_Loose_HairGroom")]
        [TestCase("BraidedBun_Compact_HairGroom")]
        public void PackagedBunRebuildMatchesSavedLodsAndKeepsAllFormsEditable(string name)
        {
            const string folder="Assets/UMAProjectData/HairCards/Examples/BraidedBun/";
            if(!System.IO.Directory.Exists(folder))return;
            var sample=AssetDatabase.LoadAssetAtPath<HairGroomAsset>(folder+name+".asset");Assert.That(sample,Is.Not.Null);
            Assert.That(sample.Groups.Count,Is.EqualTo(6));
            Assert.That(sample.SharedHelpers.Count(h=>h.type==HairHelperType.Gather),Is.EqualTo(1));
            Assert.That(sample.SharedHelpers.Count(h=>h.type==HairHelperType.Bun),Is.EqualTo(1));
            Assert.That(sample.Groups.All(g=>g.generation.enabled && g.generation.cards.source!=HairPopulationSource.Scalp),Is.True);
            foreach(var group in sample.Groups.Take(2))
            {
                Assert.That(group.generation.cards.source,Is.EqualTo(HairPopulationSource.PaintedScalp));
                Assert.That(group.generation.cards.modifiers.Any(m=>m.type==HairModifierType.Gather && m.gather.length==HairGatherLength.ExtendToReach),Is.True);
                var map=group.FindMap(HairMapKind.GrowthArea);
                Assert.That(map.UsesTexture,Is.True);Assert.That(map.texture.resolution,Is.EqualTo(32));
                Assert.That(map.texture.tiles.Count,Is.GreaterThan(0));
                Assert.That(map.texture.IsValid(sample.SourceTopologySignature,sample.SourceVertexCount),Is.True);
                Assert.That(group.preventCardPenetration,Is.True);
            }
            var bun=sample.Groups[2];var braid=sample.Groups[4];var wisps=sample.Groups[5];
            Assert.That(sample.Groups[0].generation.cards.rootMapGroupId,Is.EqualTo(sample.Groups[1].Id));
            Assert.That(bun.generation.cards.form.helperIds.Count,Is.EqualTo(1));
            Assert.That(braid.generation.cards.source,Is.EqualTo(HairPopulationSource.Braid));
            Assert.That(braid.generation.cards.form.helperIds.Count,Is.EqualTo(1),"Braid surrounds a separate wrapped bun, not another braid coil.");
            Assert.That(wisps.generation.cards.count,Is.InRange(20,40));
            Assert.That(wisps.generation.cards.form.cardWidth,Is.LessThan(.002f));
            Assert.That(wisps.atlas.regions.All(r=>r.uvRect.width<.04f),Is.True,"Flyaways must use the narrow strand area.");
            Assert.That(wisps.atlas,Is.Not.SameAs(bun.atlas));
            var bind = sample.FindHelper(bun.generation.cards.form.helperIds[0]);
            Assert.That(bind.type,Is.EqualTo(HairHelperType.Bun));
            Assert.That(sample.FindHelper(bind.bun.gatherHelperId).type,Is.EqualTo(HairHelperType.Gather));
            var braidSpline=sample.FindHelper(braid.generation.cards.form.helperIds[0]);
            Assert.That(braidSpline.type,Is.EqualTo(HairHelperType.BraidRail));
            Assert.That(braidSpline.rail.parentHelperId,Is.EqualTo(bind.Id));
            Assert.That(braidSpline.points.Count,Is.EqualTo(17));
            var before=EditorJsonUtility.ToJson(sample);int previous=int.MaxValue;
            for(int lod=0;lod<3;lod++)
            {
                var evaluated=HairGroomEvaluator.Evaluate(sample,new HairEvaluationOptions{lodLevel=lod});
                using var mesh=HairCardMeshGenerator.Build(evaluated);
                Assert.That(mesh.triangleCount,Is.InRange(1,75000));Assert.That(mesh.triangleCount,Is.LessThan(previous));previous=mesh.triangleCount;
                Assert.That(mesh.degenerateTriangleCount,Is.Zero);Assert.That(mesh.frameFlipCount,Is.Zero);Assert.That(evaluated.warnings,Is.Empty);
                var saved=AssetDatabase.LoadAssetAtPath<Mesh>(folder+name+"_LOD"+lod+".asset");Assert.That(saved,Is.Not.Null);
                Assert.That(saved.vertices,Is.EqualTo(mesh.mesh.vertices));Assert.That(saved.normals,Is.EqualTo(mesh.mesh.normals));
                Assert.That(saved.triangles,Is.EqualTo(mesh.mesh.triangles));
            }
            Assert.That(EditorJsonUtility.ToJson(sample),Is.EqualTo(before));
        }

        [TestCase(HairPopulationSource.GridPanels)] [TestCase(HairPopulationSource.Braid)]
        public void PaintedFormRootsAreOptInAndEmptyGrowthRemovesOnlyTheirOutput(HairPopulationSource source)
        {
            Form(source);var group=groom.Groups[0];var map=group.FindMap(HairMapKind.GrowthArea);
            HairGroomCommands.FillMap(groom,map,0);
            Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount,Is.EqualTo(30),"Existing independent forms ignore scalp growth.");
            group.generation.cards.form.usePaintedRootDensity=true;
            Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount,Is.Zero);
            HairGroomCommands.FillMap(groom,map,1);
            Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount,Is.EqualTo(30));
            Assert.That(groom.SharedHelpers.Count,Is.EqualTo(1));
            Assert.That(group.generation.cards.Duplicate().form.usePaintedRootDensity,Is.True);
        }

        [Test]
        public void TextureFormMaskCanEraseInsideAFaceWithoutMovingSurvivorsOrChangingLodIdentity()
        {
            Form(HairPopulationSource.GridPanels);var group=groom.Groups[0];var map=group.FindMap(HairMapKind.GrowthArea);
            group.generation.cards.count=80;group.generation.cards.form.usePaintedRootDensity=true;
            HairGroomCommands.FillMap(groom,map,1);HairGroomCommands.SetMapStorage(groom,map,HairMapStorage.Texture,32);
            var full=HairGroomEvaluator.Evaluate(groom);var tile=map.texture.EnsureTile(map,0,0,0,1,2);
            var vertices=sourceMesh.vertices;int n=map.texture.resolution;
            for(int y=0;y<=n;y++)for(int x=0;x<=n;x++)
            {
                var bc=HairTextureMap.TexelBarycentric(x,y,n);var p=vertices[0]*bc.x+vertices[1]*bc.y+vertices[2]*bc.z;
                tile.pixels[y*(n+1)+x]=p.x<0?0:1;
            }
            map.texture.Touch();var masked=HairGroomEvaluator.Evaluate(groom);
            Assert.That(masked.CardCount,Is.InRange(20,60));
            foreach(var curve in masked.curves)
            {
                var original=full.curves.Single(c=>c.curveId==curve.curveId);
                Assert.That(curve.points.Select(p=>p.position),Is.EqualTo(original.points.Select(p=>p.position)));
                Assert.That(curve.rootAnchor.CachedLocalPosition.x,Is.GreaterThan(-.035f));
            }
            groom.Lods[0].cardFraction=.5f;var lod=HairGroomEvaluator.Evaluate(groom);
            Assert.That(lod.CardCount,Is.GreaterThan(0));Assert.That(lod.CardCount,Is.LessThan(masked.CardCount));
            Assert.That(lod.curves.All(c=>masked.curves.Any(m=>m.curveId==c.curveId)),Is.True);
            Assert.That(map.values,Is.EqualTo(new[]{1f,1f,1f}),"The hole is in texture texels, not vertex values.");
        }

        [TestCase(.0005f)] [TestCase(.003f)] [TestCase(.03f)] [TestCase(1f)]
        public void ClosestSurfaceRetainsInteriorBarycentricsAtScalpTriangleScales(float size)
        {
            var origin=new Vector3(.07f,1.86f,.04f);
            var vertices=new[]{origin,origin+Vector3.right*size,origin+Vector3.forward*size};
            sourceMesh.vertices=vertices;sourceMesh.triangles=new[]{0,2,1};sourceMesh.RecalculateBounds();
            var expected=new Vector3(.2f,.3f,.5f);var point=vertices[0]*expected.x+vertices[2]*expected.y+vertices[1]*expected.z;
            var query=new HairMeshRaycaster(sourceMesh);
            Assert.That(query.ClosestPoint(point+Vector3.up*.004f,out var hit),Is.True);
            Assert.That(Vector3.Distance(hit.Barycentric,expected),Is.LessThan(.001f));
            Assert.That(Vector3.Distance(vertices[0]*hit.Barycentric.x+vertices[2]*hit.Barycentric.y+vertices[1]*hit.Barycentric.z,hit.Point),Is.LessThan(1e-6f));
            Assert.That(Vector3.Distance(HairMeshUtility.Barycentric(point,vertices[0],vertices[2],vertices[1]),expected),Is.LessThan(.001f));
            Assert.That(HairMeshUtility.Barycentric(origin,origin,origin,origin),Is.EqualTo(Vector3.right));
        }

        [Test]
        public void GridRootVariationStaggersStartsWithoutMovingTipsOrDependingOnLod()
        {
            Form(HairPopulationSource.GridPanels);var group=groom.Groups[0];var original=HairGroomEvaluator.Evaluate(groom);
            group.generation.cards.form.rootStartVariation=.06f;var varied=HairGroomEvaluator.Evaluate(groom);
            Assert.That(varied.CardCount,Is.EqualTo(original.CardCount));
            foreach(var curve in varied.curves)
            {
                var before=original.curves.Single(c=>c.curveId==curve.curveId);
                Assert.That(curve.points[0].position.z,Is.GreaterThan(before.points[0].position.z));
                Assert.That(curve.points.Last().position,Is.EqualTo(before.points.Last().position));
            }
            var repeat=HairGroomEvaluator.Evaluate(groom);
            Assert.That(repeat.curves.Select(c=>c.points[0].position),Is.EqualTo(varied.curves.Select(c=>c.points[0].position)));
            groom.Lods[0].cardFraction=.5f;var lod=HairGroomEvaluator.Evaluate(groom);
            foreach(var curve in lod.curves)Assert.That(curve.points[0].position,Is.EqualTo(varied.curves.Single(c=>c.curveId==curve.curveId).points[0].position));
        }

        [Test]
        public void CoverageLodPreservesFullDetailAndEvenlyThinsStableGridCards()
        {
            Form(HairPopulationSource.GridPanels);var form=groom.Groups[0].generation.cards.form;
            var full=HairGroomEvaluator.Evaluate(groom);form.preserveLodCoverage=true;var enabled=HairGroomEvaluator.Evaluate(groom);
            Assert.That(enabled.curves.SelectMany(c=>c.points.Select(p=>p.position)),Is.EqualTo(full.curves.SelectMany(c=>c.points.Select(p=>p.position))));
            Assert.That(enabled.curves.SelectMany(c=>c.points.Select(p=>p.width)),Is.EqualTo(full.curves.SelectMany(c=>c.points.Select(p=>p.width))));
            groom.Lods[0].cardFraction=.38f;var low=HairGroomEvaluator.Evaluate(groom);
            Assert.That(low.CardCount,Is.InRange(8,16));
            var indices=low.curves.Select(c=>full.curves.FindIndex(p=>p.curveId==c.curveId)).OrderBy(i=>i).ToArray();
            for(int i=1;i<indices.Length;i++)Assert.That(indices[i]-indices[i-1],Is.LessThanOrEqualTo(5));
            foreach(var c in low.curves)
            {
                var before=full.curves.Single(p=>p.curveId==c.curveId);
                Assert.That(c.points.Select(p=>p.position),Is.EqualTo(before.points.Select(p=>p.position)));
                Assert.That(c.points[0].width,Is.GreaterThan(before.points[0].width));
                Assert.That(c.points[0].width,Is.LessThanOrEqualTo(before.points[0].width*2.001f));
            }
            groom.Lods[0].cardFraction=.7f;var medium=HairGroomEvaluator.Evaluate(groom);
            Assert.That(low.curves.All(c=>medium.curves.Any(m=>m.curveId==c.curveId)),Is.True);
            groom.Lods[0].cardFraction=.0001f;Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount,Is.GreaterThanOrEqualTo(1));
            groom.Lods[0].cardFraction=0;Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount,Is.Zero);
        }

        [UnityTest]
        public IEnumerator FormNodesRenderAtNarrowAndWideWidthsWithoutChangingData()
        {
            Form(HairPopulationSource.Braid);var grid=new HairHelper();grid.EnsureIntegrity();
            HairFormUtility.CreateGrid(grid,3,5,Vector3.zero,Vector3.right*.1f,Vector3.up*.2f);groom.SharedHelpers.Add(grid);
            var stage=CreateEditingStage();var properties=ScriptableObject.CreateInstance<HairGroomWorkspace>();var tree=ScriptableObject.CreateInstance<HairGroomNodeWindow>();
            var active=typeof(HairCardStage).GetField("<ActiveStage>k__BackingField",BindingFlags.NonPublic|BindingFlags.Static);object previous=active.GetValue(null);
            try
            {
                active.SetValue(null,stage);properties.Show();tree.Show();
                string before=EditorJsonUtility.ToJson(groom);
                foreach(float width in new[]{360f,900f})
                {
                    properties.position=new Rect(420,40,width,900);tree.position=new Rect(20,40,360,900);
                    foreach(var node in stage.Nodes.Where(n=>n.Kind==HairGroomNodeKind.Population || n.Helper!=null || n.Kind==HairGroomNodeKind.Children).ToArray())
                    {stage.SelectNode(node.Key);properties.Repaint();tree.Repaint();yield return null;yield return null;LogAssert.NoUnexpectedReceived();}
                }
                Assert.That(EditorJsonUtility.ToJson(groom),Is.EqualTo(before));
            }
            finally{properties.Close();tree.Close();active.SetValue(null,previous);DestroyEditingStage(stage);}
        }
    }
}
#endif
