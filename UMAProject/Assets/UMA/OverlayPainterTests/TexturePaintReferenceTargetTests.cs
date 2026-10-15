#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintReferenceTargetTests
    {
        [Test]
        public void ReferencesDefaultHiddenAndDoNotExpandThePaintSelection()
        {
            var state = new TexturePaintStageState();
            Assert.That(state.visibleReferenceSlots, Is.Empty);
            var active = new List<string> { "Brows" };
            var references = new List<string> { "Body", "Hair" };
            var visible = TexturePaintStageWindow.VisiblePreviewSlots(active, references);
            Assert.That(visible, Is.EquivalentTo(new[] { "Brows", "Body", "Hair" }));
            Assert.That(active, Is.EqualTo(new[] { "Brows" }));
            references.Clear();
            Assert.That(TexturePaintStageWindow.VisiblePreviewSlots(active, references), Is.EquivalentTo(active));
        }

        [Test]
        public void VisibilityToggleCannotSelectAReferenceOrHideTheCurrentTarget()
        {
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            try
            {
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var type=typeof(TexturePaintStageWindow);
                type.GetField("selectedTargetId",flags).SetValue(stage,"brows");
                type.GetField("selectedSlots",flags).SetValue(stage,new List<string>{"Brows"});
                var current=new TexturePaintLogicalTarget{id="brows"};
                current.members.Add(new TexturePaintLogicalTargetMember{slotName="Brows"});
                var reference=new TexturePaintLogicalTarget{id="body",isUdim=true};
                reference.members.Add(new TexturePaintLogicalTargetMember{slotName="Body_1001"});
                reference.members.Add(new TexturePaintLogicalTargetMember{slotName="Body_1002"});
                var toggle=type.GetMethod("SetReferenceTargetVisible",flags);
                toggle.Invoke(stage,new object[]{reference,true});
                Assert.That(type.GetField("selectedTargetId",flags).GetValue(stage),Is.EqualTo("brows"));
                Assert.That((List<string>)type.GetField("selectedSlots",flags).GetValue(stage),Is.EqualTo(new[]{"Brows"}));
                var references=(List<string>)type.GetField("visibleReferenceSlots",flags).GetValue(stage);
                Assert.That(references,Is.EquivalentTo(new[]{"Body_1001","Body_1002"}));
                toggle.Invoke(stage,new object[]{current,false});
                Assert.That(TexturePaintStageWindow.VisiblePreviewSlots(new[]{"Brows"},references),Does.Contain("Brows"));
                toggle.Invoke(stage,new object[]{reference,false});
                Assert.That(references,Is.Empty);
            }
            finally{Object.DestroyImmediate(stage);}
        }

        [Test]
        public void ReferenceVisibilityRoundTripsSeparatelyFromTheCurrentTarget()
        {
            var state = new TexturePaintStageState { selectedSlots = new List<string>{"Brows"},
                visibleReferenceSlots = new List<string>{"Body"} };
            var copy = JsonUtility.FromJson<TexturePaintStageState>(JsonUtility.ToJson(state));
            Assert.That(copy.version, Is.GreaterThanOrEqualTo(19));
            Assert.That(copy.selectedSlots, Is.EqualTo(new[]{"Brows"}));
            Assert.That(copy.visibleReferenceSlots, Is.EqualTo(new[]{"Body"}));
        }

        [Test]
        public void SharedMeshVisibilityDoesNotModifyTheSourceOrColliderAndCleansUp()
        {
            using var fixture = new TexturePaintGpuTestFixture(Color.clear);
            var surface = fixture.set.surface;
            surface.slotNames = new List<string>{"Brows", "Body"};
            surface.triangleSlotNames = new[]{"Brows", "Body"};
            var filter=fixture.owner.AddComponent<MeshFilter>(); filter.sharedMesh=fixture.mesh;
            surface.collider=fixture.owner.AddComponent<MeshCollider>();surface.collider.sharedMesh=fixture.mesh;
            var stage=ScriptableObject.CreateInstance<TexturePaintStageWindow>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var apply=typeof(TexturePaintStageWindow).GetMethod("ApplyReferenceTargetVisibility",flags);
            Mesh filtered=null;
            try
            {
                apply.Invoke(stage,new object[]{surface,new HashSet<string>{"Brows"}});
                filtered=filter.sharedMesh;
                Assert.That(filtered,Is.Not.SameAs(fixture.mesh));
                Assert.That(filtered.triangles.Length,Is.EqualTo(3));
                Assert.That(fixture.mesh.triangles.Length,Is.EqualTo(6));
                Assert.That(surface.collider.sharedMesh,Is.SameAs(fixture.mesh));
                apply.Invoke(stage,new object[]{surface,new HashSet<string>{"Brows","Body"}});
                Assert.That(filter.sharedMesh,Is.SameAs(fixture.mesh));
                Assert.That(filtered==null,Is.True,"The temporary filtered mesh must be destroyed.");
                apply.Invoke(stage,new object[]{surface,new HashSet<string>()});
                Assert.That(fixture.owner.activeSelf,Is.False);
                apply.Invoke(stage,new object[]{surface,new HashSet<string>{"Brows"}});
                Assert.That(fixture.owner.activeSelf,Is.True);
                filtered=filter.sharedMesh;
                typeof(TexturePaintStageWindow).GetMethod("DisposeReferenceTargetVisibility",flags).Invoke(stage,null);
                Assert.That(filter.sharedMesh,Is.SameAs(fixture.mesh));
                Assert.That(filtered==null,Is.True);
            }
            finally{Object.DestroyImmediate(stage);}
        }

        [Test]
        public void VisibleReferenceInFrontCannotReceiveOrInterceptATargetPaintHit()
        {
            using var active = new TexturePaintGpuTestFixture(Color.clear);
            using var reference = new TexturePaintGpuTestFixture(Color.clear);
            active.set.surface.slotNames = new List<string>{"Brows"};
            active.set.surface.triangleSlotNames = new[]{"Brows","Brows"};
            reference.set.surface.slotNames = new List<string>{"Body"};
            reference.set.surface.triangleSlotNames = new[]{"Body","Body"};
            reference.owner.transform.position = new Vector3(0,0,-1);
            foreach(var fixture in new[]{active,reference})
            {fixture.set.surface.collider=fixture.owner.AddComponent<MeshCollider>();fixture.set.surface.collider.sharedMesh=fixture.mesh;}
            Physics.SyncTransforms();
            var reconstruction = new MeshReconstructionResult();
            reconstruction.surfaces.Add(reference.set.surface);reconstruction.surfaces.Add(active.set.surface);
            Assert.That(reconstruction.Raycast(new Ray(new Vector3(.25f,.25f,-2),Vector3.forward),
                new List<string>{"Brows"},true,out var hitSurface,out _),Is.True);
            Assert.That(hitSurface,Is.SameAs(active.set.surface));
            reconstruction.surfaces.Clear();
        }
    }
}
#endif
