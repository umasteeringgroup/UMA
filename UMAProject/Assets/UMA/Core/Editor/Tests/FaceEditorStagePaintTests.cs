#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class FaceEditorStagePaintTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<Object> objects = new List<Object>();
        private FaceEditorStage stage;
        private SceneView view;
        private Scene previewScene;
        private Camera previousCamera;
        private SlotData rightSlot;

        [SetUp]
        public void SetUp()
        {
            previousCamera = Camera.current;
            previewScene = EditorSceneManager.NewPreviewScene();
            var avatarObject = Track(new GameObject("Mirror paint test"));
            avatarObject.SetActive(false);
            SceneManager.MoveGameObjectToScene(avatarObject, previewScene);
            var avatar = avatarObject.AddComponent<DynamicCharacterAvatar>();
            avatar.editorTimeGeneration = false;

            // Non-identity transforms and an oblique camera catch screen-space/world-X mirroring.
            avatarObject.transform.SetPositionAndRotation(new Vector3(0.3f, 0.2f, 0.1f), Quaternion.Euler(0f, 20f, 15f));
            avatarObject.transform.localScale = new Vector3(1.3f, 0.8f, 1.1f);
            var vertices = new[]
            {
                new Vector3(-1.2f, -0.2f, 0f), new Vector3(-1f, 0.2f, 0f), new Vector3(-0.8f, -0.2f, 0f),
                new Vector3(1.2f, -0.2f, 0f), new Vector3(1f, 0.2f, 0f), new Vector3(0.8f, -0.2f, 0f)
            };
            SlotData leftSlot = CreateSlot("Left", new[] { vertices[0], vertices[1], vertices[2] }, new[] { 0, 1, 2 }, 0);
            rightSlot = CreateSlot("Right", new[] { vertices[3], vertices[4], vertices[5] }, new[] { 0, 2, 1 }, 3);
            avatar.umaRecipe = new UMAData.UMARecipe { slotDataList = new[] { leftSlot, rightSlot } };

            stage = Track(ScriptableObject.CreateInstance<FaceEditorStage>());
            stage.FaceObject = avatarObject;
            stage.thisDCA = avatar;
            stage.BakedMesh = Track(new Mesh { vertices = vertices, triangles = new[] { 0, 1, 2, 3, 5, 4 } });
            var lookup = (Dictionary<string, SlotData>)GetField("slotLookupByName");
            lookup.Add("Left", leftSlot);
            lookup.Add("Right", rightSlot);
            var selectedSlots = (HashSet<string>)GetField("selectedSlotNames");
            selectedSlots.Add("Left");
            selectedSlots.Add("Right");
            SetField("paintBrushRadiusPixels", 8);

            view = Track(ScriptableObject.CreateInstance<SceneView>());
            view.camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -6f), Quaternion.identity);
            view.camera.orthographic = false;
            view.camera.fieldOfView = 50f;
            view.camera.pixelRect = new Rect(0f, 0f, 800f, 600f);
            Camera.SetupCurrent(view.camera);

            var brush = Track(new Texture2D(2, 2));
            brush.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            brush.Apply();
            SetField("paintBrushTexture", brush);
        }

        [TearDown]
        public void TearDown()
        {
            Camera.SetupCurrent(previousCamera);
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
            objects.Clear();
            EditorSceneManager.ClosePreviewScene(previewScene);
        }

        [TestCase("Point", false, 1)]
        [TestCase("Point", true, 2)]
        [TestCase("Circle", false, 1)]
        [TestCase("Circle", true, 2)]
        [TestCase("Square", false, 1)]
        [TestCase("Square", true, 2)]
        [TestCase("Loadable", false, 1)]
        [TestCase("Loadable", true, 2)]
        public void PaintAddsAndRemovesMirroredFaces(string brush, bool mirror, int expectedCount)
        {
            SetBrush(brush);
            SetField("paintMirrorX", mirror);
            Paint(true);
            Assert.That(Selection.Count, Is.EqualTo(expectedCount));
            Paint(true);
            Assert.That(Selection.Count, Is.EqualTo(expectedCount), "Repeated strokes must not duplicate face selections.");
            Paint(false);
            Assert.That(Selection.Count, Is.Zero);
        }

        [TestCase("Point", true)]
        [TestCase("Point", false)]
        [TestCase("Circle", true)]
        [TestCase("Circle", false)]
        public void MirrorRespectsSlotVisibilityAndSelection(string brush, bool suppress)
        {
            SetBrush(brush);
            SetField("paintMirrorX", true);
            if (suppress)
            {
                rightSlot.Suppressed = true;
            }
            else
            {
                ((HashSet<string>)GetField("selectedSlotNames")).Remove("Right");
            }
            Paint(true);
            Assert.That(Selection.Count, Is.EqualTo(1));
            Assert.That(Selection[0].GetType().GetField("slotName").GetValue(Selection[0]), Is.EqualTo("Left"));
        }

        [Test]
        public void ChangingMirrorOptionRebuildsAreaBrushCache()
        {
            SetBrush("Circle");
            Paint(true);
            Assert.That(Selection.Count, Is.EqualTo(1));
            SetField("paintMirrorX", true);
            Paint(true);
            Assert.That(Selection.Count, Is.EqualTo(2));
            SetField("paintMirrorX", false);
            Paint(false);
            Assert.That(Selection.Count, Is.EqualTo(1), "Disabling Mirror X must stop painting the opposite side immediately.");
        }

        [Test]
        public void MirroredAreaBrushPreservesBackfaceCulling()
        {
            SetBrush("Circle");
            SetField("paintMirrorX", true);
            // Reverse only the opposite face: it must be culled after reflection too.
            rightSlot.asset.meshData.submeshes[0] = new SubMeshTriangles(new[] { 0, 1, 2 });
            Paint(true);
            Assert.That(Selection.Count, Is.EqualTo(1));
            SetField("rubberBandCullBackfaces", false);
            Paint(true);
            Assert.That(Selection.Count, Is.EqualTo(2));
        }

        [Test]
        public void ModifierPointPaintingAlsoMirrors()
        {
            SetField("paintMirrorX", true);
            Paint(true, false);
            Assert.That(Selection.Count, Is.EqualTo(2));
        }

        private IList Selection => (IList)GetField("selectedSlotTrianglesSerialized");

        private void Paint(bool add, bool useBrush = true)
        {
            Vector3 target = stage.FaceObject.transform.TransformPoint(new Vector3(-1f, -0.05f, 0f));
            Vector2 mouse = HandleUtility.WorldToGUIPoint(target);
            typeof(FaceEditorStage).GetMethod("ApplyPaintSelectionAtMouse", PrivateInstance)
                .Invoke(stage, new object[] { mouse, add, useBrush, view });
        }

        private SlotData CreateSlot(string name, Vector3[] vertices, int[] triangles, int offset)
        {
            var asset = Track(ScriptableObject.CreateInstance<SlotDataAsset>());
            asset.name = name;
            asset.meshData = new UMAMeshData
            {
                vertexCount = vertices.Length,
                vertices = vertices,
                subMeshCount = 1,
                submeshes = new[] { new SubMeshTriangles(triangles) }
            };
            return new SlotData(asset) { vertexOffset = offset };
        }

        private void SetBrush(string name)
        {
            Type brushType = typeof(FaceEditorStage).GetNestedType("PaintBrushMode", BindingFlags.NonPublic);
            SetField("paintBrushMode", Enum.Parse(brushType, name));
        }

        private object GetField(string name) => typeof(FaceEditorStage).GetField(name, PrivateInstance).GetValue(stage);
        private void SetField(string name, object value) => typeof(FaceEditorStage).GetField(name, PrivateInstance).SetValue(stage, value);

        private T Track<T>(T value) where T : Object
        {
            objects.Add(value);
            return value;
        }
    }
}
#endif
