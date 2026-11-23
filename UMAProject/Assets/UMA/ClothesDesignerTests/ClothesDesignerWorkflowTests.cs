#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UMA.Editors;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The Clothes Designer is offered by the Vertex Editor as a plugin workflow.</summary>
    public sealed class ClothesDesignerWorkflowTests
    {
        [Test]
        public void Workflow_IsRegisteredWithTheVertexEditor()
        {
            VertexEditorWorkflow workflow = VertexEditorWorkflows.Find(ClothesDesignerWorkflow.WorkflowId);
            Assert.That(workflow, Is.SameAs(ClothesDesignerWorkflow.Instance));
        }

        [Test]
        public void Workflow_IdIsStable()
        {
            // Other tools start the designer by this name.
            Assert.That(ClothesDesignerWorkflow.WorkflowId, Is.EqualTo("uma.clothes-designer"));
        }

        [Test]
        public void Workflow_ButtonSaysWhatItIsFor()
        {
            Assert.That(ClothesDesignerWorkflow.Instance.ToolbarContent.text, Is.EqualTo("Clothes Designer"));
            Assert.That(ClothesDesignerWorkflow.Instance.ToolbarContent.tooltip, Is.Not.Empty);
        }

        [Test]
        public void Workflow_HasNothingUnsavedBeforeItIsUsed()
        {
            Assert.That(ClothesDesignerWorkflow.Instance.HasUnsavedChanges(null), Is.False);
            Assert.That(ClothesDesignerWorkflow.Instance.GetBody(null), Is.Null);
        }

        [Test]
        public void ClothTools_EachHasAnIconAndATooltip_InTheToolbarAndThePanel()
        {
            UnityEngine.GUIContent[] panel = ClothToolInfo.PanelContents();
            Assert.That(panel.Length, Is.EqualTo(4));
            for (int i = 0; i < ClothToolInfo.Order.Length; i++)
            {
                ClothTool tool = ClothToolInfo.Order[i];
                Assert.That(ClothToolInfo.Icon(tool), Is.Not.Null, tool.ToString());
                Assert.That(ClothToolInfo.Tooltip(tool), Does.StartWith(ClothToolInfo.Name(tool) + ":"));
                Assert.That(panel[i].tooltip, Is.EqualTo(ClothToolInfo.Tooltip(tool)));
            }
            var toggle = new PinClothToolToggle();
            Assert.That(toggle.tooltip, Is.EqualTo(ClothToolInfo.Tooltip(ClothTool.Pin)));
            Assert.That(toggle.icon, Is.SameAs(ClothToolInfo.PinIcon()));
        }

        [Test]
        public void ClothTools_HaveNoSessionOutsideTheDesigner()
        {
            // The toolbar shows only while the designer is the Vertex Editor's active workflow.
            if (UnityEditor.SceneManagement.StageUtility.GetCurrentStage() is VertexEditorStage) Assert.Ignore("A Vertex Editor is open.");
            Assert.That(ClothToolsOverlay.ActiveSession, Is.Null);
            var toggle = new GrabClothToolToggle();
            Assert.That(toggle.enabledSelf, Is.False);
            Assert.That(toggle.value, Is.False);
        }
    }
}
#endif
