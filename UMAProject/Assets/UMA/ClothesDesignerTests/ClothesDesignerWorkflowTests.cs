#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UMA.Editors;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
        public void Workflow_HidesTheWorkflowBars()
        {
            // The designer needs none of the other workflows; it is left by closing the Vertex Editor.
            Assert.That(ClothesDesignerWorkflow.Instance.HidesWorkflowBars, Is.True);
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
            Assert.That(panel.Length, Is.EqualTo(12), "Place, Size, Grab, Pin, Erase Pin, Fit Brush, Roll, Measure, Dart, Pleat, Draw Piece and Body Region");
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
            var brush = new FitBrushClothToolToggle();
            Assert.That(brush.tooltip, Is.EqualTo(ClothToolInfo.Tooltip(ClothTool.FitBrush)));
            Assert.That(brush.icon, Is.SameAs(ClothToolInfo.Icon(ClothTool.FitBrush)));
            var region = new BodyRegionClothToolToggle();
            Assert.That(region.tooltip, Is.EqualTo(ClothToolInfo.Tooltip(ClothTool.BodyRegion)));
            Assert.That(region.icon, Is.SameAs(ClothToolInfo.Icon(ClothTool.BodyRegion)));
            var size = new SizeClothToolToggle();
            Assert.That(size.tooltip, Is.EqualTo(ClothToolInfo.Tooltip(ClothTool.Size)));
            Assert.That(size.icon, Is.SameAs(ClothToolInfo.Icon(ClothTool.Size)));
            Assert.That(ClothToolInfo.Order[1], Is.EqualTo(ClothTool.Size), "Size sits next to Place: both work on whole pieces.");
        }

        [Test]
        public void DesignerToolbars_AreHiddenOutsideTheDesigner_AndLeftAsSetWhileDesigning()
        {
            Assert.That(ClothToolInfo.ToolbarShown(true, false, false), Is.True, "The designer opens: shown.");
            Assert.That(ClothToolInfo.ToolbarShown(false, true, true), Is.False, "It closes: hidden.");
            Assert.That(ClothToolInfo.ToolbarShown(true, true, false), Is.False, "Closed by the user while designing: stays closed.");
            Assert.That(ClothToolInfo.ToolbarShown(true, true, true), Is.True);
            Assert.That(ClothToolInfo.ToolbarShown(false, false, true), Is.False,
                "Shown outside the designer (a Scene view the Overlay Painter opened as the designer closed): hidden again.");
            Assert.That(ClothToolInfo.ToolbarShown(false, false, false), Is.False);
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

        [Test]
        public void FitTools_HoldEveryPaintModeOnce_EraseLast_WithAName_ATooltip_AndAnIcon()
        {
            var all = (FitPaintMode[])System.Enum.GetValues(typeof(FitPaintMode));
            Assert.That(FitPaintInfo.Order, Is.EquivalentTo(all), "every paint mode once");
            Assert.That(FitPaintInfo.Order[FitPaintInfo.Order.Length - 1], Is.EqualTo(FitPaintMode.Erase));
            UnityEngine.GUIContent[] panel = FitPaintInfo.PanelContents();
            Assert.That(panel.Length, Is.EqualTo(FitPaintInfo.Order.Length));
            var names = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < FitPaintInfo.Order.Length; i++)
            {
                FitPaintMode mode = FitPaintInfo.Order[i];
                Assert.That(names.Add(FitPaintInfo.Name(mode)), Is.True, "names differ: " + mode);
                Assert.That(FitPaintInfo.Tooltip(mode), Is.Not.Empty, mode.ToString());
                Assert.That(FitPaintInfo.Icon(mode), Is.Not.Null, mode.ToString());
                Assert.That(FitPaintInfo.Icon(mode), Is.SameAs(FitPaintInfo.Icon(mode)), "icons are made once");
                Assert.That(panel[i].text, Is.EqualTo(FitPaintInfo.Name(mode)));
                Assert.That(panel[i].tooltip, Is.EqualTo(FitPaintInfo.Tooltip(mode)));
            }
            // The dot is the colour the cloth turns: Hug green, Erase the brush's red.
            Assert.That(FitPaintInfo.PaintColor(FitPaintMode.Hug), Is.EqualTo(FitPaint.Color(1f, 0f, 0f)));
            Assert.That(FitPaintInfo.PaintColor(FitPaintMode.Erase), Is.EqualTo(FitPaintInfo.EraseColor));
        }

        [Test]
        public void FitTools_Toggles_ShowTheirMode_AndHaveNoSessionOutsideTheDesigner()
        {
            FitPaintModeToggle[] toggles =
            {
                new HugFitToolToggle(), new SmoothFitToolToggle(), new PuffFitToolToggle(), new SlimFitToolToggle(),
                new StiffenFitToolToggle(), new WeightFitToolToggle(), new EraseFitToolToggle()
            };
            Assert.That(toggles.Length, Is.EqualTo(FitPaintInfo.Order.Length), "one toggle a mode");
            for (int i = 0; i < toggles.Length; i++)
            {
                FitPaintMode mode = FitPaintInfo.Order[i];
                Assert.That(toggles[i].Mode, Is.EqualTo(mode), "toolbar order follows the panel's");
                Assert.That(toggles[i].text, Is.EqualTo(FitPaintInfo.Name(mode)));
                Assert.That(toggles[i].icon, Is.SameAs(FitPaintInfo.Icon(mode)));
                Assert.That(toggles[i].tooltip, Does.StartWith(FitPaintInfo.Name(mode) + ": " + FitPaintInfo.Tooltip(mode)));
            }
            // The toolbar shows only while the Fit Brush is the designer's tool.
            if (UnityEditor.SceneManagement.StageUtility.GetCurrentStage() is VertexEditorStage) Assert.Ignore("A Vertex Editor is open.");
            Assert.That(FitToolsOverlay.BrushSession, Is.Null);
            foreach (FitPaintModeToggle toggle in toggles)
            {
                Assert.That(toggle.enabledSelf, Is.False, toggle.Mode.ToString());
                Assert.That(toggle.value, Is.False, toggle.Mode.ToString());
            }
        }

        [Test]
        public void Publish_OffersTheRacesRegions_AndAsksForOneUntilTheClothesArePublished()
        {
            var race = ScriptableObject.CreateInstance<RaceData>();
            try
            {
                race.name = "Test Race";
                race.wardrobeSlots = new List<string> { "None", "Chest", "", "Legs", "Chest" };
                Assert.That(PublishChoice.Regions(race), Is.EqualTo(new[] { "Chest", "Legs" }), "the race's own regions, in order, without None");
                var settings = new PublishSettings();
                PublishChoice first = PublishChoice.Prefill(settings, null, "Polo Shirt", race);
                Assert.That(first.wardrobeSlot, Is.Null, "never published: the settings' default region was never chosen");
                Assert.That(first.name, Is.EqualTo("PoloShirt"), "a default name takes the pattern's");
                Assert.That(first.race, Is.SameAs(race));
                Assert.That(first.Problems(), Has.Some.Contains("wardrobe region"));
                settings.slotName = "MyPolo";
                settings.wardrobeSlot = "Legs";
                PublishChoice again = PublishChoice.Prefill(settings, "MyPolo", "Polo Shirt", race);
                Assert.That(again.wardrobeSlot, Is.EqualTo("Legs"), "published before: its region again");
                Assert.That(again.name, Is.EqualTo("MyPolo"));
                Assert.That(again.UpdatesEarlierPublish, Is.True);
                again.name = "OtherPolo";
                Assert.That(again.UpdatesEarlierPublish, Is.False, "a new name makes new assets");
                settings.wardrobeSlot = "Wings";
                Assert.That(PublishChoice.Prefill(settings, "MyPolo", "Polo Shirt", race).wardrobeSlot, Is.Null, "a region the race does not have");
            }
            finally { Object.DestroyImmediate(race); }
        }

        [Test]
        public void Publish_SaysWhatIsMissing_AndGoesAheadWithEverything()
        {
            var race = ScriptableObject.CreateInstance<RaceData>();
            var material = ScriptableObject.CreateInstance<UMAMaterial>();
            try
            {
                race.name = "Test Race";
                race.wardrobeSlots = new List<string> { "None", "Chest" };
                var choice = new PublishChoice { name = "Shirt", wardrobeSlot = "Chest", material = material, folder = "Assets/Clothes", race = race };
                Assert.That(choice.Problems(), Is.Empty);
                choice.name = " ";
                Assert.That(choice.Problems(), Has.Some.Contains("name"));
                choice.name = "a/b";
                Assert.That(choice.Problems(), Has.Some.Contains("file name"));
                choice.name = "Shirt";
                choice.wardrobeSlot = "Legs";
                Assert.That(choice.Problems(), Has.Some.Contains("no wardrobe region 'Legs'"));
                choice.wardrobeSlot = "Chest";
                choice.material = null;
                Assert.That(choice.Problems(), Has.Some.Contains("material"));
                choice.material = material;
                choice.folder = "C:/Temp";
                Assert.That(choice.Problems(), Has.Some.Contains("inside Assets"));
                choice.folder = "Assets/Clothes/";
                choice.race = null;
                Assert.That(choice.Problems(), Has.Some.Contains("race"));
                Assert.That(PublishChoice.NameFrom("  "), Is.EqualTo(new PublishSettings().slotName));
            }
            finally
            {
                Object.DestroyImmediate(race);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Publish_IsOnTheClothToolsToolbar_AndWaitsForTheDesigner()
        {
            var button = new ClothPublishButton();
            Assert.That(button.text, Is.EqualTo("Publish"));
            Assert.That(button.tooltip, Is.EqualTo(PublishClothesWindow.Tip));
            if (UnityEditor.SceneManagement.StageUtility.GetCurrentStage() is VertexEditorStage) Assert.Ignore("A Vertex Editor is open.");
            Assert.That(button.enabledSelf, Is.False, "nothing to publish outside the designer");
        }

        /// <summary>Stands in for the Overlay Painter: remembers what it was handed.</summary>
        private sealed class FakePainter : IUMATrimPainter
        {
            public SlotDataAsset slot;
            public OverlayDataAsset overlay;
            public int trims = -1;
            public string Name => "Fake Painter";
            public IReadOnlyList<UMATrimConstruction> Constructions => new UMATrimConstruction[0];

            public bool PaintTrims(SlotDataAsset s, OverlayDataAsset o, IReadOnlyList<UMATrimPath> t, out string message)
            {
                slot = s; overlay = o; trims = t != null ? t.Count : -1; message = null;
                return true;
            }
        }

        [Test]
        public void PublishWindow_KnowsWhetherTheOverlayPainterIsInstalled()
        {
            IUMATrimPainter installed = UMATrimPainting.Painter;
            var fake = new FakePainter();
            try
            {
                UMATrimPainting.Register(fake);
                Assert.That(PublishChoice.PainterInstalled, Is.True);
                UMATrimPainting.Unregister(fake);
                Assert.That(PublishChoice.PainterInstalled, Is.False, "nothing registered: the option is not offered");
                Assert.That(new PublishChoice().openInPainter, Is.False, "plain Publish by default");
            }
            finally
            {
                UMATrimPainting.Unregister(fake);
                if (installed != null) UMATrimPainting.Register(installed);
            }
        }

        [Test]
        public void OpenOverlayPainter_HandsOverTheSlotOverlayAndTrims_AndWorksWithoutTrims()
        {
            if (UnityEditor.SceneManagement.StageUtility.GetCurrentStage() is VertexEditorStage) Assert.Ignore("A Vertex Editor is open: handing over closes it.");
            IUMATrimPainter installed = UMATrimPainting.Painter;
            var fake = new FakePainter();
            var slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            var overlay = ScriptableObject.CreateInstance<OverlayDataAsset>();
            try
            {
                UMATrimPainting.Register(fake);
                var trims = new List<UMATrimPath> { new UMATrimPath { name = "Zipper", uv = new List<Vector2> { Vector2.zero, Vector2.one } } };
                Assert.That(DesignerSession.OpenOverlayPainter(slot, overlay, trims, out string message), Is.True, message);
                Assert.That(fake.slot, Is.SameAs(slot));
                Assert.That(fake.overlay, Is.SameAs(overlay));
                Assert.That(fake.trims, Is.EqualTo(1));
                Assert.That(DesignerSession.OpenOverlayPainter(slot, overlay, null, out message), Is.True, "a pattern with no trims still opens the painter");
                Assert.That(fake.trims, Is.EqualTo(0), "with no path layers");
                UMATrimPainting.Unregister(fake);
                Assert.That(DesignerSession.OpenOverlayPainter(slot, overlay, trims, out message), Is.False);
                Assert.That(message, Does.Contain("not installed"));
            }
            finally
            {
                UMATrimPainting.Unregister(fake);
                if (installed != null) UMATrimPainting.Register(installed);
                Object.DestroyImmediate(slot);
                Object.DestroyImmediate(overlay);
            }
        }

        [Test]
        public void TemporaryObjects_LeftBehind_AreRemoved_OwnedOnesAreKept()
        {
            // A designer's cloth left in the open scene (a closed designer, a script reload) is a stray; the
            // objects a live designer owns stay.
            GameObject stray = new GameObject(DesignerTemporaryObjects.ClothName) { hideFlags = HideFlags.DontSave | HideFlags.NotEditable };
            GameObject hidden = new GameObject(DesignerTemporaryObjects.TryOnName) { hideFlags = HideFlags.HideAndDontSave };
            GameObject owned = DesignerTemporaryObjects.Create(DesignerTemporaryObjects.TryOnViewName, HideFlags.NotEditable);
            GameObject other = new GameObject("Clothes Designer Something Else") { hideFlags = HideFlags.DontSave };
            try
            {
                Assert.That(DesignerTemporaryObjects.RemoveStrays(false), Is.EqualTo(1));
                Assert.That(stray == null, Is.True, "the stray in the open scene");
                Assert.That(hidden != null, Is.True, "hidden objects are only looked for in a full check");
                Assert.That(DesignerTemporaryObjects.RemoveStrays(true), Is.EqualTo(1));
                Assert.That(hidden == null, Is.True, "the hidden stray");
                Assert.That(owned != null && other != null, Is.True, "owned, and not one of the designer's names");
            }
            finally
            {
                if (stray != null) Object.DestroyImmediate(stray);
                if (hidden != null) Object.DestroyImmediate(hidden);
                DesignerTemporaryObjects.Destroy(owned);
                if (other != null) Object.DestroyImmediate(other);
            }
            LogAssert.Expect(LogType.Warning, new Regex("removed 'Clothes Designer Cloth'"));
            LogAssert.Expect(LogType.Warning, new Regex(@"removed 'Clothes Designer Try-On \(temporary\)'"));
        }

        [Test]
        public void TemporaryObjects_AreNotMade_OutsideALiveStageScene()
        {
            // With the stage's scene gone the object would land in the artist's scene: none is made.
            Assert.That(DesignerTemporaryObjects.CreateInScene(DesignerTemporaryObjects.ClothName, default(Scene)), Is.Null);
        }
    }
}
#endif
