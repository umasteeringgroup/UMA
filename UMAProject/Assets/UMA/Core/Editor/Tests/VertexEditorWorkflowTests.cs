#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The registry of workflows that plugins add to the Vertex Editor: what can be registered, the
    /// order of the buttons, and that a reload cannot leave two of one workflow.
    /// </summary>
    public sealed class VertexEditorWorkflowTests
    {
        private sealed class TestWorkflow : VertexEditorWorkflow
        {
            private readonly string id;
            private readonly int order;
            private readonly GUIContent content;

            public TestWorkflow(string id, int order = 100, string label = "Test")
            {
                this.id = id;
                this.order = order;
                content = new GUIContent(label);
            }

            public override string Id => id;
            public override GUIContent ToolbarContent => content;
            public override int Order => order;
        }

        private const string IdA = "uma.tests.workflow-a";
        private const string IdB = "uma.tests.workflow-b";

        [TearDown]
        public void Remove()
        {
            VertexEditorWorkflows.Unregister(IdA);
            VertexEditorWorkflows.Unregister(IdB);
        }

        [Test]
        public void Register_MakesTheWorkflowFindable()
        {
            var workflow = new TestWorkflow(IdA);
            Assert.That(VertexEditorWorkflows.Register(workflow), Is.True);
            Assert.That(VertexEditorWorkflows.Find(IdA), Is.SameAs(workflow));
            Assert.That(VertexEditorWorkflows.All, Does.Contain(workflow));
        }

        [Test]
        public void Register_AgainWithTheSameId_ReplacesTheEarlierOne()
        {
            var first = new TestWorkflow(IdA);
            var second = new TestWorkflow(IdA);
            VertexEditorWorkflows.Register(first);
            VertexEditorWorkflows.Register(second);
            Assert.That(VertexEditorWorkflows.Find(IdA), Is.SameAs(second));
            Assert.That(VertexEditorWorkflows.All, Has.No.Member(first));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("Sculpt")]
        [TestCase("VertexPaint")]
        [TestCase("AdvancedVertexAdjustments")]
        [TestCase("BlendshapeExtraction")]
        public void Register_RefusesAnIdThatIsEmptyOrBuiltIn(string id)
        {
            // The refusal is reported as a warning, which does not fail a test.
            Assert.That(VertexEditorWorkflows.Register(new TestWorkflow(id)), Is.False);
            Assert.That(VertexEditorWorkflows.Find(id), Is.Null);
        }

        [Test]
        public void Register_Null_IsRefused()
        {
            Assert.That(VertexEditorWorkflows.Register(null), Is.False);
        }

        [Test]
        public void All_IsOrderedByOrderThenId()
        {
            var late = new TestWorkflow(IdA, 900000);
            var later = new TestWorkflow(IdB, 900001);
            VertexEditorWorkflows.Register(later);
            VertexEditorWorkflows.Register(late);
            int lateIndex = IndexOf(late), laterIndex = IndexOf(later);
            Assert.That(lateIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(laterIndex, Is.GreaterThan(lateIndex));
        }

        [Test]
        public void Unregister_RemovesTheWorkflow()
        {
            VertexEditorWorkflows.Register(new TestWorkflow(IdA));
            Assert.That(VertexEditorWorkflows.Unregister(IdA), Is.True);
            Assert.That(VertexEditorWorkflows.Find(IdA), Is.Null);
            Assert.That(VertexEditorWorkflows.Unregister(IdA), Is.False);
        }

        [Test]
        public void ToolSettings_DoNotStoreAPluginWorkflow()
        {
            // The Vertex Editor always opens on one of its own workflows; a plugin's workflow is
            // started by the plugin. Nothing in the stored settings names one.
            string json = new VertexEditorToolSettings { workflow = "Sculpt" }.ToJson();
            Assert.That(json, Does.Not.Contain("pluginWorkflow"));
        }

        private static int IndexOf(VertexEditorWorkflow workflow)
        {
            for (int i = 0; i < VertexEditorWorkflows.All.Count; i++)
            {
                if (VertexEditorWorkflows.All[i] == workflow)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
#endif
