#if UNITY_INCLUDE_TESTS
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.HairCards.Editor.Tests
{
    internal sealed class HairNaturalInspectorTestWindow : EditorWindow
    {
        internal System.Action draw;
        internal int repaints;
        private void OnGUI() { draw?.Invoke(); if(Event.current.type==EventType.Repaint)repaints++; }
    }
    public sealed partial class HairCoreTests
    {
        private HairGuide NaturalFixture(bool curved = true)
        {
            var guide = groom.Groups[0].guides[0]; guide.points.Clear();
            guide.root = HairSurfaceAnchor.Create(groom.SourceMeshId, 0, 0, Vector3.right, 0, Vector3.zero, Vector3.up);
            for (int i = 0; i < 25; i++)
            {
                float t = i / 24f;
                guide.points.Add(new HairGuidePoint { position = new Vector3(t * .12f, curved ? .018f * Mathf.Sin(t * 2f) : 0, 0), width = .004f });
            }
            return guide;
        }

        private HairEvaluatedCurve NaturalEvaluate(HairEvaluationOptions options = null) =>
            HairGroomEvaluator.Evaluate(groom, options ?? new HairEvaluationOptions { evaluateSurfaceAnchors = false }).evaluatedGuides[0];

        [TestCase(HairModifierType.Noise)]
        [TestCase(HairModifierType.SurfaceBend)]
        public void NaturalModifiersAnchorRootPreserveLengthsAndAreRepeatable(HairModifierType type)
        {
            var guide = NaturalFixture();
            var before = NaturalEvaluate();
            groom.Groups[0].modifiers.Add(new HairModifierSettings { type = type, domain = HairModifierDomain.Guides,
                strandAlignedNoise = true, amount = type == HairModifierType.Noise ? .003f : 40f, noiseNormalAmplitude = .002f, seed = 47 });
            var after = NaturalEvaluate(); var repeat = NaturalEvaluate();
            Assert.That(after.points[0].position, Is.EqualTo(before.points[0].position));
            Assert.That(after.points.Last().position, Is.Not.EqualTo(before.points.Last().position));
            for (int i = 1; i < after.points.Count; i++)
            {
                Assert.That(Vector3.Distance(after.points[i-1].position, after.points[i].position),
                    Is.EqualTo(Vector3.Distance(before.points[i-1].position, before.points[i].position)).Within(2e-6f));
                Assert.That(after.points[i].position, Is.EqualTo(repeat.points[i].position));
                Assert.That(float.IsFinite(after.points[i].position.sqrMagnitude), Is.True);
            }
            Assert.That(guide.points.Last().position, Is.EqualTo(before.points.Last().position), "Evaluation must not edit authored guides.");
        }

        [TestCase(HairModifierType.Noise)]
        [TestCase(HairModifierType.SurfaceBend)]
        public void NaturalModifiersFollowSourceRotationAndIgnoreCardRoll(HairModifierType type)
        {
            var guide = NaturalFixture();
            groom.Groups[0].modifiers.Add(new HairModifierSettings { type = type, domain = HairModifierDomain.Guides,
                strandAlignedNoise = true, amount = type == HairModifierType.Noise ? .003f : 40f, noiseNormalAmplitude = .002f });
            var original = NaturalEvaluate();
            foreach (var point in guide.points) point.roll = 115;
            var rolled = NaturalEvaluate();
            Assert.That(rolled.points.Select(p=>p.position), Is.EqualTo(original.points.Select(p=>p.position)));
            Quaternion rotation = Quaternion.Euler(72, -135, 27); Vector3 offset = new Vector3(4, 2, -3);
            foreach (var point in guide.points) point.position = rotation * point.position + offset;
            guide.root = HairSurfaceAnchor.Create(groom.SourceMeshId, 0, 0, Vector3.right, 0, offset, rotation * Vector3.up);
            var transformed = NaturalEvaluate();
            for (int i = 0; i < original.points.Count; i++)
                Assert.That(Vector3.Distance(transformed.points[i].position, rotation * original.points[i].position + offset), Is.LessThan(8e-6f));
        }

        [TestCase(HairModifierType.Noise)]
        [TestCase(HairModifierType.SurfaceBend)]
        public void NaturalModifiersRespectBypassFreezeAndStiffness(HairModifierType type)
        {
            var guide = NaturalFixture(); var baseline = NaturalEvaluate();
            var modifier = new HairModifierSettings { type = type, domain = HairModifierDomain.Guides, strandAlignedNoise = true,
                amount = type == HairModifierType.Noise ? .003f : 40f, noiseNormalAmplitude = .002f, enabled = false };
            groom.Groups[0].modifiers.Add(modifier);
            Assert.That(NaturalEvaluate().points.Select(p=>p.position), Is.EqualTo(baseline.points.Select(p=>p.position)));
            modifier.enabled = true; foreach (var p in guide.points) p.freeze = 1;
            Assert.That(NaturalEvaluate().points.Select(p=>p.position), Is.EqualTo(baseline.points.Select(p=>p.position)));
            foreach (var p in guide.points) { p.freeze = 0; p.stiffness = 1; }
            var stiff = NaturalEvaluate();
            for (int i=0;i<stiff.points.Count;i++) Assert.That(Vector3.Distance(stiff.points[i].position,baseline.points[i].position),Is.LessThan(1e-7f));
        }

        [Test]
        public void StrandNoiseAxesAreIndependentAndRootFadeProtectsBase()
        {
            NaturalFixture(false);
            var modifier = new HairModifierSettings { type = HairModifierType.Noise, domain = HairModifierDomain.Guides,
                strandAlignedNoise = true, amount = .003f, noiseNormalAmplitude = 0, noiseRootFade = .8f };
            groom.Groups[0].modifiers.Add(modifier);
            var lateral = NaturalEvaluate(); Assert.That(lateral.points.All(p => Mathf.Abs(p.position.y) < 1e-7f), Is.True);
            Assert.That(lateral.points.Any(p => Mathf.Abs(p.position.z) > .00001f), Is.True);
            modifier.amount = 0; modifier.noiseNormalAmplitude = .003f;
            var normal = NaturalEvaluate(); Assert.That(normal.points.All(p => Mathf.Abs(p.position.z) < 1e-7f), Is.True);
            Assert.That(normal.points.Any(p => Mathf.Abs(p.position.y) > .00001f), Is.True);
            modifier.noiseRootFade = 0;
            Assert.That(Mathf.Abs(normal.points[1].position.y), Is.LessThan(Mathf.Abs(NaturalEvaluate().points[1].position.y)));
        }

        [Test]
        public void SurfaceBendLiftsInRootNormalPlaneAndSupportsReturningTips()
        {
            NaturalFixture(false);
            var modifier = new HairModifierSettings { type=HairModifierType.SurfaceBend, domain=HairModifierDomain.Guides,
                amount=45, bendProfile=AnimationCurve.Linear(0,1,1,-.5f) };
            groom.Groups[0].modifiers.Add(modifier);
            var bent=NaturalEvaluate();
            Assert.That(bent.points[10].position.y, Is.GreaterThan(.015f));
            Assert.That(bent.points.Last().position.y, Is.LessThan(bent.points[18].position.y));
            Assert.That(bent.points.All(p=>Mathf.Abs(p.position.z)<1e-7f),Is.True);
        }

        [Test]
        public void NaturalSettingsDuplicateSerializeAndUndoWithoutSharingCurves()
        {
            var modifier=HairGroomCommands.AddModifier(groom,groom.Groups[0],HairModifierType.SurfaceBend);
            modifier.strandAlignedNoise=true; modifier.noiseNormalAmplitude=.004f; modifier.noiseRootFade=.35f;
            var duplicate=modifier.Duplicate(); duplicate.bendProfile.MoveKey(0,new Keyframe(0,.2f));
            Assert.That(modifier.bendProfile.Evaluate(0),Is.EqualTo(1));
            Assert.That(duplicate.noiseNormalAmplitude,Is.EqualTo(.004f));
            var restored=JsonUtility.FromJson<HairModifierSettings>(JsonUtility.ToJson(modifier));
            Assert.That(restored.strandAlignedNoise,Is.True); Assert.That(restored.noiseRootFade,Is.EqualTo(.35f));
            Undo.IncrementCurrentGroup(); Undo.RecordObject(groom,"Test natural controls"); modifier.noiseNormalAmplitude=.02f;
            Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
            Assert.That(groom.Groups[0].modifiers.Last().noiseNormalAmplitude,Is.EqualTo(.004f));
        }

        [Test]
        public void StrandNoiseBlendsSharedClumpAndIndividualSeeds()
        {
            var guide=NaturalFixture(false);var group=groom.Groups[0];
            group.children.childrenPerGuide=3;group.children.includeGuideCard=false;
            group.children.rootSpread=group.children.lengthVariation=group.children.widthVariation=group.children.rollVariation=group.children.clump=0;
            var modifier=new HairModifierSettings {type=HairModifierType.Noise,domain=HairModifierDomain.Children,
                strandAlignedNoise=true,amount=.003f,noiseNormalAmplitude=.001f,noiseParentCoherence=1};
            group.modifiers.Add(modifier);
            var shared=HairGroomEvaluator.Evaluate(groom,new HairEvaluationOptions{evaluateSurfaceAnchors=false}).curves.Where(c=>c.isChild).ToArray();
            Assert.That(shared.Length,Is.EqualTo(3));
            Assert.That(shared[0].points.Select(p=>p.position),Is.EqualTo(shared[1].points.Select(p=>p.position)));
            modifier.noiseParentCoherence=0;
            var independent=HairGroomEvaluator.Evaluate(groom,new HairEvaluationOptions{evaluateSurfaceAnchors=false}).curves.Where(c=>c.isChild).ToArray();
            Assert.That(independent[0].points.Select(p=>p.position),Is.Not.EqualTo(independent[1].points.Select(p=>p.position)));
        }

        [Test]
        public void NaturalPopulationShapeDoesNotChangeWithPreviewTessellation()
        {
            var group=PopulationFixture();
            group.generation.cards.modifiers.Add(new HairModifierSettings {type=HairModifierType.Noise,strandAlignedNoise=true,amount=.002f});
            group.generation.cards.modifiers.Add(new HairModifierSettings {type=HairModifierType.SurfaceBend,amount=30});
            var full=HairGroomEvaluator.Evaluate(groom);
            var draft=HairGroomEvaluator.Evaluate(groom,new HairEvaluationOptions{previewSampleCount=3,interactiveSampleLimit=10});
            foreach(var curve in draft.curves)
                Assert.That(curve.points.Select(p=>p.position),Is.EqualTo(full.curves.Single(c=>c.curveId==curve.curveId).points.Select(p=>p.position)));
        }

        [TestCase(HairModifierType.Noise)]
        [TestCase(HairModifierType.SurfaceBend)]
        public void NaturalModifiersKeepInteriorFrozenPointsAndProtectRoots(HairModifierType type)
        {
            var guide=NaturalFixture();guide.points[10].freeze=1;
            var before=NaturalEvaluate();
            var modifier=new HairModifierSettings {type=type,domain=HairModifierDomain.Guides,strandAlignedNoise=true,
                amount=type==HairModifierType.Noise?.003f:35f,rootInfluence=0};
            groom.Groups[0].modifiers.Add(modifier);var after=NaturalEvaluate();
            Assert.That(after.points[10].position,Is.EqualTo(before.points[10].position));
            Assert.That(after.points[0].position,Is.EqualTo(before.points[0].position));
            // A pinned interior point may constrain the entire prefix. Test root falloff
            // on the free chain, rather than requiring an impossible displacement there.
            guide.points[10].freeze=0;after=NaturalEvaluate();
            modifier.rootInfluence=1;var unprotected=NaturalEvaluate();
            Assert.That(Vector3.Distance(after.points[1].position,before.points[1].position),
                Is.LessThan(Vector3.Distance(unprotected.points[1].position,before.points[1].position)));
        }

        [Test]
        public void NaturalSamplesOwnTheirSettingsAndHaveAnEditableForelockMask()
        {
            const string folder="Assets/UMAProjectData/HairCards/Examples/ShortHairPart/";
            if(!System.IO.Directory.Exists(folder))return;
            var classic=AssetDatabase.LoadAssetAtPath<HairGroomAsset>(folder+"ShortHairPart_HairGroom.asset");
            var natural=AssetDatabase.LoadAssetAtPath<HairGroomAsset>(folder+"ShortHairPart_Natural_HairGroom.asset");
            var flip=AssetDatabase.LoadAssetAtPath<HairGroomAsset>(folder+"ShortHairPart_FrontFlipNatural_HairGroom.asset");
            Assert.That(natural,Is.Not.Null);Assert.That(flip,Is.Not.Null);
            Assert.That(natural.GroomId,Is.Not.EqualTo(classic.GroomId));Assert.That(flip.GroomId,Is.Not.EqualTo(natural.GroomId));
            foreach(var example in new[]{natural,flip})
            {
                Assert.That(example.Groups[0].profile,Is.Not.EqualTo(classic.Groups[0].profile));
                Assert.That(example.Groups[0].atlas.material,Is.Not.EqualTo(classic.Groups[0].atlas.material));
                Assert.That(example.Groups[0].profile.SamplesPerCard,Is.GreaterThan(example.Groups[1].profile.SamplesPerCard));
                Assert.That(example.Groups.All(g=>g.generation.cards.modifiers.Count(m=>m.strandAlignedNoise)>=2),Is.True);
            }
            var forelock=flip.Groups[0].generation.cards.modifiers.Single(m=>m.name.StartsWith("Front flip"));
            var mask=flip.Groups[0].maps.Single(m=>m.Id==forelock.maskMapId);
            Assert.That(mask.UsesTexture,Is.True);Assert.That(mask.values.Any(v=>v>0&&v<1),Is.True);
            Assert.That(mask.values.Any(v=>v==0),Is.True);Assert.That(mask.values.Any(v=>v>.9f),Is.True);
        }

        [UnityEngine.TestTools.UnityTest]
        public System.Collections.IEnumerator NaturalModifierPropertiesDrawForSelectedTreeNodes()
        {
            var group=groom.Groups[0];group.generation.enabled=true;
            var noise=HairGenerationEditor.AddModifier(groom,group,group.generation.cards,HairModifierType.Noise);noise.strandAlignedNoise=true;
            var bend=HairGenerationEditor.AddModifier(groom,group,group.generation.cards,HairModifierType.SurfaceBend);
            var stage=ScriptableObject.CreateInstance<HairCardStage>();
            var window=ScriptableObject.CreateInstance<HairNaturalInspectorTestWindow>();
            var method=typeof(HairGroomWorkspace).GetMethod("DrawModifier",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            try
            {
                typeof(HairCardStage).GetField("groom",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(stage,groom);
                foreach(var modifier in new[]{noise,bend})
                {
                    var node=stage.Nodes.Single(n=>n.Modifier==modifier);stage.SelectNode(node.Key);
                    Assert.That(stage.SelectedNode.Modifier,Is.SameAs(modifier));
                    window.draw=()=>method.Invoke(null,new object[]{stage,modifier});window.position=new Rect(50,50,420,750);window.Show();
                    int count=window.repaints;
                    for(int frame=0;frame<8;frame++){window.Repaint();yield return null;}
                    Assert.That(window.repaints,Is.GreaterThan(count));
                }
                UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
            }
            finally {window.draw=null;window.Close();Object.DestroyImmediate(stage);}
        }

        [Test]
        public void NaturalModifierWorkspaceDoesNotAllocatePerStrandAfterWarmup()
        {
            PopulateDenseProfileGuides(100,4,24);
            groom.Groups[0].modifiers.Add(new HairModifierSettings{type=HairModifierType.Noise,domain=HairModifierDomain.Children,strandAlignedNoise=true,amount=.002f});
            groom.Groups[0].modifiers.Add(new HairModifierSettings{type=HairModifierType.SurfaceBend,domain=HairModifierDomain.Children,amount=20});
            var workspace=new HairEvaluationWorkspace();var evaluation=new HairEvaluationResult();
            var options=new HairEvaluationOptions{evaluateSurfaceAnchors=false};
            System.Action update=()=>HairGroomEvaluator.EvaluateInto(groom,options,workspace,evaluation);
            update();update();
            Assert.That(CountManagedAllocations(update),Is.LessThan(100),"Frame/noise buffers must be pooled, not allocated per card.");
        }
    }
}
#endif
