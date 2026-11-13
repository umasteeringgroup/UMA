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
        private HairHelper BraidSpline()
        {
            var h = Form(HairPopulationSource.Braid); h.points.Clear();
            for (int i = 0; i < 5; i++) h.points.Add(new Vector3((i - 2) * .07f, .04f + .01f * Mathf.Sin(i), 0));
            return h;
        }
        [Test]
        public void BraidSplineFreePlacementMatchesLegacyInterpolationAndDoesNotMutate()
        {
            var h = BraidSpline(); var before = JsonUtility.ToJson(h); var query = new HairRailWorkspace(); query.Prepare(groom, h);
            for (int i = 0; i <= 100; i++) Assert.That(query.Sample(i / 100f, true), Is.EqualTo(HairFormUtility.RailPoint(h.points, i / 100f, true)));
            var a = HairGroomEvaluator.Evaluate(groom); var b = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { sourceToWorld = Matrix4x4.TRS(Vector3.one * 3, Quaternion.Euler(25, 72, -13), new Vector3(.7f, 2, 1.1f)) });
            Assert.That(a.curves.SelectMany(c => c.points).Select(p => p.position), Is.EqualTo(b.curves.SelectMany(c => c.points).Select(p => p.position)));
            Assert.That(JsonUtility.ToJson(h), Is.EqualTo(before));
        }
        [TestCase(false)] [TestCase(true)]
        public void BraidSplineSurfaceOffsetsAreSourceSpaceDistances(bool custom)
        {
            var h = BraidSpline(); h.rail.followSurface = true; h.rail.surfaceOffset = .012f;
            if (custom)
            {
                h.rail.surfaceMesh = sourceMesh; h.rail.surfacePosition = new Vector3(.1f, .2f, -.1f);
                h.rail.surfaceRotation = Quaternion.Euler(40, 30, 10); h.rail.surfaceScale = new Vector3(2, .7f, 1.3f);
                for (int i = 0; i < h.points.Count; i++) h.points[i] = h.rail.SurfaceMatrix.MultiplyPoint3x4(h.points[i]);
            }
            var query = new HairRailWorkspace(); query.Prepare(groom, h);
            var matrix = h.rail.SurfaceMatrix; var normal = matrix.inverse.transpose.MultiplyVector(Vector3.up).normalized;
            for (int i = 0; i <= 100; i++) Assert.That(Vector3.Dot(query.Sample(i / 100f, true) - matrix.MultiplyPoint3x4(Vector3.zero), normal), Is.EqualTo(.012f).Within(1e-5f));
            Assert.That(HairGroomEvaluator.Evaluate(groom).warnings, Is.Empty);
        }
        [TestCase(true, false)] [TestCase(false, true)] [TestCase(true, true)]
        public void BraidSplineEndpointSurfaceSnappingLeavesMiddleFree(bool root, bool tip)
        {
            var h = BraidSpline(); h.rail.root.attachment = root ? HairRailAttachment.Surface : HairRailAttachment.Free;
            h.rail.tip.attachment = tip ? HairRailAttachment.Surface : HairRailAttachment.Free;
            h.rail.root.surfaceOffset = .013f; h.rail.tip.surfaceOffset = .017f;
            var query = new HairRailWorkspace(); query.Prepare(groom, h);
            Assert.That(query.Sample(0, true).y, Is.EqualTo(root ? .013f : h.points[0].y).Within(1e-6f));
            Assert.That(query.Sample(1, true).y, Is.EqualTo(tip ? .017f : h.points[^1].y).Within(1e-6f));
            Assert.That(query.Sample(.5f, true), Is.EqualTo(h.points[2]));
        }
        [TestCase(false)] [TestCase(true)]
        public void BraidSplineHelperAttachmentsFollowTransformsWithoutAccumulation(bool snapSpine)
        {
            var h = BraidSpline(); var target = HairGroomCommands.AddHelper(groom, HairHelperType.BindingRing, new Vector3(.14f,.18f,0));
            target.rotation = Quaternion.Euler(0,0,30); target.scale = new Vector3(2,1,1);
            h.rail.tip.attachment = HairRailAttachment.Helper; h.rail.tip.helperId = target.Id; h.rail.tip.helperOffset = Vector3.right * .01f;
            h.rail.followSurface = snapSpine; var snapshot = JsonUtility.ToJson(h); var query = new HairRailWorkspace(); query.Prepare(groom,h);
            Assert.That(Vector3.Distance(query.Sample(1,true),target.LocalToSource.MultiplyPoint3x4(h.rail.tip.helperOffset)),Is.LessThan(1e-6f));
            target.position += Vector3.up * .1f; query.Prepare(groom,h); var first = query.Sample(.9f,true); query.Prepare(groom,h);
            Assert.That(query.Sample(.9f,true),Is.EqualTo(first)); Assert.That(JsonUtility.ToJson(h),Is.EqualTo(snapshot));
            Assert.That(query.Sample(1,true).y,Is.GreaterThan(.27f));
        }
        [Test]
        public void BraidSplineParentBindingAndDetachingPreserveShape()
        {
            var h = BraidSpline(); var target = HairGroomCommands.AddHelper(groom, HairHelperType.BindingRing, Vector3.one);
            target.rotation = Quaternion.Euler(12,90,7); target.scale = new Vector3(2,1,.7f);
            var query = new HairRailWorkspace(); query.Prepare(groom,h); var before = query.Sample(.37f,true);
            HairRailUtility.BindParent(groom,h,target.Id); query.Prepare(groom,h); Assert.That(Vector3.Distance(before,query.Sample(.37f,true)),Is.LessThan(1e-6f));
            target.position += Vector3.up * .2f; query.Prepare(groom,h); Assert.That(Vector3.Distance(before+Vector3.up*.2f,query.Sample(.37f,true)),Is.LessThan(1e-6f));
            HairRailUtility.BindParent(groom,h,null); query.Prepare(groom,h); var detached=query.Sample(.37f,true);
            target.position += Vector3.right; query.Prepare(groom,h); Assert.That(query.Sample(.37f,true),Is.EqualTo(detached));
        }
        [Test]
        public void BraidSplineMissingTargetsAndRemovedMeshWarnWithoutThrowing()
        {
            var h=BraidSpline();h.rail.followSurface=true;h.rail.tip.attachment=HairRailAttachment.Helper;h.rail.tip.helperId="missing";
            var query=new HairRailWorkspace();query.Prepare(groom,h);groom.SetSource(null,"none");
            var warnings=new System.Collections.Generic.List<string>();query.Prepare(groom,h,warnings);
            Assert.That(warnings.Count,Is.GreaterThanOrEqualTo(2));Assert.That(float.IsFinite(query.Sample(.5f,true).sqrMagnitude),Is.True);
            groom.SetSource(sourceMesh,"restored");query.Prepare(groom,h);Assert.That(query.Sample(.5f,true).y,Is.EqualTo(h.rail.surfaceOffset).Within(1e-6f));
        }
        [Test]
        public void BraidSplineReverseSwapsEndpointBindings()
        {
            var h=BraidSpline();h.rail.root.attachment=HairRailAttachment.Surface;h.rail.root.surfaceOffset=.018f;
            HairFormUtility.Reverse(h);Assert.That(h.rail.root.attachment,Is.EqualTo(HairRailAttachment.Free));
            Assert.That(h.rail.tip.attachment,Is.EqualTo(HairRailAttachment.Surface));Assert.That(h.rail.tip.surfaceOffset,Is.EqualTo(.018f));
        }
        [Test]
        public void BunBraidExtractionCreatesIndependentParentedRailAndSupportsUndo()
        {
            var bun=HairGroomCommands.AddHelper(groom,HairHelperType.Bun,Vector3.up*.2f);
            var group=groom.Groups[0];group.generation.enabled=true;group.generation.cards.source=HairPopulationSource.Bun;
            group.generation.cards.form.bunPart=HairBunPart.SurroundingBraid;group.generation.cards.form.helperIds.Add(bun.Id);
            Undo.ClearUndo(groom);Undo.IncrementCurrentGroup();
            var rail=HairBraidSplineEditor.ExtractBunBraid(groom,bun);Assert.That(rail.points.Count,Is.EqualTo(17));
            Assert.That(group.generation.cards.source,Is.EqualTo(HairPopulationSource.Braid));Assert.That(rail.rail.parentHelperId,Is.EqualTo(bun.Id));
            var snapshot=JsonUtility.ToJson(bun);var stage=CreateEditingStage();
            try { stage.FormEditAll=true;var start=rail.points[0];HairBraidSplineEditor.Move(stage,rail,0,start,start-Vector3.up*.01f);
                Assert.That(rail.points[0],Is.EqualTo(start-Vector3.up*.01f));Assert.That(JsonUtility.ToJson(bun),Is.EqualTo(snapshot));
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();Assert.That(groom.SharedHelpers.Count,Is.EqualTo(1));
                Assert.That(groom.Groups[0].generation.cards.source,Is.EqualTo(HairPopulationSource.Bun)); }
            finally {DestroyEditingStage(stage);}
        }
        [UnityTest]
        public IEnumerator BraidSplineCanBePickedAndDraggedDirectlyInScene()
        {
            var h=BraidSpline();var stage=CreateEditingStage();stage.SceneTool=HairSceneTool.Select;
            var view=ScriptableObject.CreateInstance<SceneView>();Vector2 hit=Vector2.zero;bool drew=false;int layouts=0,nearest=0;EventType lastEvent=EventType.Ignore;
            bool driveMouse=false;Vector2 pointerOffset=Vector2.zero;Vector3 pickPoint=HairFormUtility.RailPoint(h.points,.38f,true);
            var draw=typeof(HairCardStage).GetMethod("DrawBraidSpline",BindingFlags.NonPublic|BindingFlags.Instance);
            void OnScene(SceneView current)
            {
                if(current!=view)return;
                using(new Handles.DrawingScope(Matrix4x4.identity))
                {
                    lastEvent=Event.current.type;if(lastEvent==EventType.Layout)layouts++;
                    // Inject the synthetic pointer in the actual Scene GUI coordinate
                    // space, avoiding native dock/DPI remapping in SendEvent. Picking,
                    // handle IDs, capture, drag rays, and Undo remain the real code path.
                    hit=HandleUtility.WorldToGUIPoint(pickPoint);drew=true;
                    if(driveMouse) Event.current.mousePosition=hit+pointerOffset;
                    draw.Invoke(stage,new object[]{h,stage.SceneTool==HairSceneTool.Helper});
                    nearest=HandleUtility.nearestControl;
                }
            }
            try
            {
                SceneView.duringSceneGui+=OnScene;view.Show();view.Focus();view.position=new Rect(30,30,600,600);
                view.pivot=new Vector3(0,.05f,0);view.rotation=Quaternion.identity;view.size=.35f;view.orthographic=true;
                view.Repaint();yield return null;yield return null;
                Assert.That(drew,Is.True);var before=h.points.ToArray();driveMouse=true;
                view.SendEvent(new Event{type=EventType.MouseMove,mousePosition=hit});view.Repaint();yield return null;
                view.SendEvent(new Event{type=EventType.Layout,mousePosition=hit});
                view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=hit});
                Assert.That(stage.FormEditAll,Is.True,$"last={lastEvent} layouts={layouts} nearest={nearest} hit={hit} tool={stage.SceneTool}");Assert.That(stage.SelectedNode.Helper,Is.SameAs(h));
                Assert.That(stage.IsEditing,Is.True,"Defer expensive card rebuilding during a line drag.");
                var destination=hit+new Vector2(0,35);
                pointerOffset=new Vector2(0,35);
                view.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=destination,delta=new Vector2(0,35)});
                view.SendEvent(new Event{type=EventType.MouseUp,button=0,mousePosition=destination});
                Assert.That(stage.IsEditing,Is.False,"Mouse-up must release capture for the next edit.");
                Assert.That(Vector3.Distance(h.points[0],before[0]),Is.GreaterThan(.005f));
                Vector3 delta=h.points[0]-before[0];for(int i=1;i<h.points.Count;i++)Assert.That(Vector3.Distance(h.points[i]-before[i],delta),Is.LessThan(1e-6f));
                Undo.FlushUndoRecordObjects();Undo.PerformUndo();Assert.That(groom.FindHelper(h.Id).points,Is.EqualTo(before));
                // A canceled second drag must release capture and restore every point.
                h=groom.FindHelper(h.Id);stage.SceneTool=HairSceneTool.Select;pointerOffset=Vector2.zero;
                view.SendEvent(new Event{type=EventType.Layout,mousePosition=hit});
                view.SendEvent(new Event{type=EventType.MouseDown,button=0,mousePosition=hit});
                Assert.That(stage.IsEditing,Is.True);
                pointerOffset=new Vector2(0,35);
                view.SendEvent(new Event{type=EventType.MouseDrag,button=0,mousePosition=destination,delta=pointerOffset});
                Assert.That(Vector3.Distance(h.points[0],before[0]),Is.GreaterThan(.005f));
                Undo.FlushUndoRecordObjects();
                view.SendEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.Escape});
                Assert.That(stage.IsEditing,Is.False);Assert.That(groom.FindHelper(h.Id).points,Is.EqualTo(before));
                LogAssert.NoUnexpectedReceived();
            }
            finally{SceneView.duringSceneGui-=OnScene;view.Close();DestroyEditingStage(stage);}
        }
        [UnityTest]
        public IEnumerator BraidSplinePropertiesRenderAtNarrowAndWideWidths()
        {
            var h=BraidSpline();var target=HairGroomCommands.AddHelper(groom,HairHelperType.BindingRing,Vector3.up);
            h.rail.tip.attachment=HairRailAttachment.Helper;h.rail.tip.helperId=target.Id;h.rail.root.attachment=HairRailAttachment.Surface;
            var stage=CreateEditingStage();var properties=ScriptableObject.CreateInstance<HairGroomWorkspace>();
            var active=typeof(HairCardStage).GetField("<ActiveStage>k__BackingField",BindingFlags.NonPublic|BindingFlags.Static);object previous=active.GetValue(null);
            try {active.SetValue(null,stage);properties.Show();stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Helper,h.Id));string before=EditorJsonUtility.ToJson(groom);
                foreach(float width in new[]{360f,900f}) {properties.position=new Rect(30,30,width,900);properties.Repaint();yield return null;yield return null;LogAssert.NoUnexpectedReceived();}
                Assert.That(EditorJsonUtility.ToJson(groom),Is.EqualTo(before));}
            finally{properties.Close();active.SetValue(null,previous);DestroyEditingStage(stage);}
        }
    }
}
#endif
