using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    public sealed class SharedColorClipboardTests
    {
        private static readonly Type ClipboardType = typeof(SharedColorTableEditor).Assembly.GetType("UMA.Editors.SharedColorClipboard", true);
        private Object clipboard;
        private string previousClipboard;
        private readonly List<Object> objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            clipboard = (Object)ClipboardType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            previousClipboard = EditorJsonUtility.ToJson(clipboard);
        }

        [TearDown]
        public void TearDown()
        {
            EditorJsonUtility.FromJsonOverwrite(previousClipboard, clipboard);
            foreach (var obj in objects)
            {
                Undo.ClearUndo(obj);
                Object.DestroyImmediate(obj);
            }
            objects.Clear();
        }

        private void Copy(OverlayColorData source) => ClipboardType.GetMethod("Copy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clipboard, new object[] { source });
        private void Paste(OverlayColorData destination) => ClipboardType.GetMethod("PasteValues", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clipboard, new object[] { destination });
        private void CopyChecked(params OverlayColorData[] colors) => ClipboardType.GetMethod("CopyChecked", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clipboard, new object[] { colors });
        private OverlayColorData[] NamedCopies() => (OverlayColorData[])ClipboardType.GetMethod("GetNamedColors", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clipboard, null);

        private SharedColorTable MakeTable(int channels, params OverlayColorData[] colors)
        {
            var table = ScriptableObject.CreateInstance<SharedColorTable>();
            table.channelCount = channels;
            table.colors = colors;
            objects.Add(table);
            return table;
        }

        private void PasteTable(SharedColorTable table, int index)
        {
            InvokeTableEditor(table, "PasteColor", table, index);
        }

        private static void InvokeTableEditor(SharedColorTable table, string method, params object[] arguments)
        {
            var editor = Editor.CreateEditor(table);
            try
            {
                typeof(SharedColorTableEditor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(editor, arguments);
            }
            finally { Object.DestroyImmediate(editor); }
        }

        private static OverlayColorData MakeSource()
        {
            var color = new OverlayColorData(5)
            {
                name = "SOURCE_NAME_MUST_NOT_BE_COPIED",
                displayColor = new Color(2.1234567f, -0.2f, 0.9876543f, 0.31f),
                showDisplayColor = true,
                isBaseColor = true,
                deleteThis = true,
                moveUpThis = true,
                PropertyBlock = new UMAMaterialPropertyBlock { alwaysUpdate = true, alwaysUpdateParms = true }
            };
            for (int i = 0; i < color.channelCount; i++)
            {
                color.channelMask[i] = new Color(i + 0.1234567f, -i, 0.37f, 0.1234567f);
                color.channelAdditiveMask[i] = new Color(-i, 0.7654321f, i * 1.5f, 0.75f);
            }
            color.PropertyBlock.shaderProperties = new List<UMAProperty>
            {
                new UMAFloatProperty { name = "_Float", Value = 0.12345679f },
                new UMAIntProperty { name = "_Int", Value = 271 },
                new UMAColorProperty { name = "_HDRColor", Value = color.displayColor },
                new UMAVectorProperty { name = "_Vector", Value = new Vector4(-1, 2, 0.7654321f, 4) },
                new UMAFloatArrayProperty { name = "_Floats", Value = new[] { 0.1234567f, -2f } },
                new UMAVectorArrayProperty { name = "_Vectors", Value = new[] { Vector4.one, new Vector4(2, -3, 4, 5) } },
                new UMAMatrixProperty { name = "_Matrix", Value = Matrix4x4.Scale(new Vector3(2, 3, 4)) },
                new UMAMatrixArrayProperty { name = "_Matrices", Value = new[] { Matrix4x4.identity, Matrix4x4.Translate(Vector3.up) } },
                new UMATextureProperty { name = "_Texture", Value = Texture2D.whiteTexture },
                new UMAOverlayTransformProperty(new Vector2(0.1234567f, -0.3f), 29.1234567f, new Vector2(1.2f, 0.8f)) { name = "_Transform" },
                new UMAComputeBufferProperty { name = "_Buffer" },
                new UMAConstantComputeBufferProperty { name = "_ConstantBuffer", offset = 16, size = 32 }
            };
            return color;
        }

        private static void AssertSettings(OverlayColorData expected, OverlayColorData actual)
        {
            Assert.That(actual.channelMask, Is.EqualTo(expected.channelMask));
            Assert.That(actual.channelAdditiveMask, Is.EqualTo(expected.channelAdditiveMask));
            Assert.That(actual.displayColor, Is.EqualTo(expected.displayColor));
            Assert.That(actual.showDisplayColor, Is.EqualTo(expected.showDisplayColor));
            Assert.That(actual.isBaseColor, Is.EqualTo(expected.isBaseColor));
            Assert.That(actual.PropertyBlock.alwaysUpdate, Is.EqualTo(expected.PropertyBlock.alwaysUpdate));
            Assert.That(actual.PropertyBlock.alwaysUpdateParms, Is.EqualTo(expected.PropertyBlock.alwaysUpdateParms));
            Assert.That(actual.PropertyBlock.shaderProperties.Count, Is.EqualTo(expected.PropertyBlock.shaderProperties.Count));
            for (int i = 0; i < expected.PropertyBlock.shaderProperties.Count; i++)
            {
                var source = expected.PropertyBlock.shaderProperties[i];
                var pasted = actual.PropertyBlock.shaderProperties[i];
                Assert.That(pasted.GetType(), Is.EqualTo(source.GetType()));
                Assert.That(pasted.name, Is.EqualTo(source.name));
                Assert.That(pasted, Is.Not.SameAs(source));
                // Compare serialized fields, not UMAProperty.ToString (which rounds/clamps values).
                foreach (var field in source.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (field.Name != "stringRepresentation")
                        Assert.That(field.GetValue(pasted), Is.EqualTo(field.GetValue(source)), source.name + "." + field.Name);
            }
        }

        [Test]
        public void CopyPreservesEveryChannelParameterNameTypeAndValueButNotColorNameOrUiState()
        {
            var source = MakeSource();
            var destination = new OverlayColorData(1) { name = "Destination", isSelected = true, colorsExpanded = true };
            Copy(source);
            Assert.That(EditorJsonUtility.ToJson(clipboard), Does.Not.Contain(source.name));
            Paste(destination);
            AssertSettings(source, destination);
            Assert.That(destination.name, Is.EqualTo("Destination"));
            Assert.That(destination.isSelected && destination.colorsExpanded, Is.True);
            Assert.That(destination.deleteThis || destination.moveUpThis, Is.False);
        }

        [Test]
        public void CopyAndEveryPasteOwnTheirArraysAndParameterObjects()
        {
            var source = MakeSource();
            Copy(source);
            source.channelMask[0] = Color.magenta;
            source.PropertyBlock.GetProperty<UMAFloatArrayProperty>("_Floats").Value[0] = 99f;
            var first = new OverlayColorData();
            Paste(first);
            AssertSettings(MakeSource(), first);
            first.channelAdditiveMask[0] = Color.cyan;
            first.PropertyBlock.GetProperty<UMAFloatProperty>("_Float").Value = 99f;
            first.PropertyBlock.GetProperty<UMAMatrixArrayProperty>("_Matrices").Value[0] = Matrix4x4.zero;
            var second = new OverlayColorData();
            Paste(second);
            AssertSettings(MakeSource(), second);
        }

        [Test]
        public void SerializedClipboardRoundTripPreservesPreciseTypedParametersAndTextureReference()
        {
            var source = MakeSource();
            string path = "Assets/UMA/Core/Editor/Tests/ClipboardTexture_" + Guid.NewGuid().ToString("N") + ".asset";
            var texture = new Texture2D(2, 2);
            AssetDatabase.CreateAsset(texture, path);
            try
            {
                source.PropertyBlock.GetProperty<UMATextureProperty>("_Texture").Value = texture;
                Copy(source);
                string saved = EditorJsonUtility.ToJson(clipboard);
                Copy(new OverlayColorData(0));
                EditorJsonUtility.FromJsonOverwrite(saved, clipboard);
                var destination = new OverlayColorData { name = "Kept" };
                Paste(destination);
                AssertSettings(source, destination);
                Assert.That(destination.name, Is.EqualTo("Kept"));
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [Test]
        public void PasteOverKeepsNameReplacesOldParametersGrowsTableAndSupportsUndoRedo()
        {
            var original = new OverlayColorData(2) { name = "Existing", color = Color.blue };
            original.SetFloatProperty("_OldProperty", 3f);
            var table = MakeTable(2, original, new OverlayColorData(2) { name = "Other", color = Color.green });
            Copy(MakeSource());
            Undo.IncrementCurrentGroup();
            PasteTable(table, 0);
            Assert.That(table.colors[0], Is.SameAs(original));
            Assert.That(original.name, Is.EqualTo("Existing"));
            Assert.That(original.PropertyBlock.GetProperty<UMAFloatProperty>("_OldProperty"), Is.Null);
            AssertSettings(MakeSource(), original);
            Assert.That(table.channelCount, Is.EqualTo(5));
            Assert.That(table.colors[1].color, Is.EqualTo(Color.green));
            Assert.That(table.colors[1].channelMask[4], Is.EqualTo(Color.white));
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(table.channelCount, Is.EqualTo(2));
            Assert.That(table.colors[0].name, Is.EqualTo("Existing"));
            Assert.That(table.colors[0].color, Is.EqualTo(Color.blue));
            Assert.That(table.colors[0].PropertyBlock.GetProperty<UMAFloatProperty>("_OldProperty").Value, Is.EqualTo(3f));
            Undo.PerformRedo();
            AssertSettings(MakeSource(), table.colors[0]);
            Assert.That(table.colors[0].name, Is.EqualTo("Existing"));
        }

        [Test]
        public void PasteAsNewUsesUniqueGenericNamesAndCanBeUndone()
        {
            var table = MakeTable(5, new OverlayColorData(5) { name = "New Color", isSelected = true });
            Copy(MakeSource());
            Undo.IncrementCurrentGroup();
            PasteTable(table, -1);
            Assert.That(table.colors, Has.Length.EqualTo(2));
            Assert.That(table.colors[1].name, Is.EqualTo("New Color 2"));
            AssertSettings(MakeSource(), table.colors[1]);
            Assert.That(table.colors[1].isSelected, Is.True);
            Assert.That(table.colors[0].isSelected, Is.False);
            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(table.colors, Has.Length.EqualTo(1));
            Assert.That(table.colors[0].isSelected, Is.True);
        }

        [Test]
        public void PastingFewerChannelsKeepsTableSizeAndClearsRemainingValuesAndParameters()
        {
            var old = MakeSource();
            old.name = "Keep this name";
            var table = MakeTable(5, old);
            Copy(new OverlayColorData(1) { color = Color.red });
            PasteTable(table, 0);
            Assert.That(table.channelCount, Is.EqualTo(5));
            Assert.That(old.name, Is.EqualTo("Keep this name"));
            Assert.That(old.color, Is.EqualTo(Color.red));
            Assert.That(old.channelMask[4], Is.EqualTo(Color.white));
            Assert.That(old.channelAdditiveMask[4], Is.EqualTo(Color.clear));
            // Unity can materialize an empty inline block while serializing the table.
            Assert.That(old.PropertyBlock == null || old.PropertyBlock.shaderProperties == null || old.PropertyBlock.shaderProperties.Count == 0, Is.True);
        }

        [Test]
        public void EmptyClipboardDoesNotModifyDestination()
        {
            using (var serialized = new SerializedObject(clipboard))
            {
                serialized.FindProperty("hasColor").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var table = MakeTable(1, new OverlayColorData(1) { name = "Existing", color = Color.red });
            PasteTable(table, -1);
            PasteTable(table, 0);
            Assert.That(table.colors, Has.Length.EqualTo(1));
            Assert.That(table.colors[0].name, Is.EqualTo("Existing"));
            Assert.That(table.colors[0].color, Is.EqualTo(Color.red));
        }

        [Test]
        public void NullArraysAndNullParametersCanBeCopiedAndPasted()
        {
            var source = new OverlayColorData { channelMask = null, channelAdditiveMask = null, PropertyBlock = new UMAMaterialPropertyBlock() };
            source.PropertyBlock.shaderProperties = new List<UMAProperty>
            {
                null, new UMAFloatArrayProperty { name = "floats" },
                new UMAVectorArrayProperty { name = "vectors" }, new UMAMatrixArrayProperty { name = "matrices" }
            };
            Copy(source);
            var destination = new OverlayColorData();
            Paste(destination);
            Assert.That(destination.channelMask, Is.Empty);
            Assert.That(destination.channelAdditiveMask, Is.Empty);
            Assert.That(destination.PropertyBlock.shaderProperties, Has.Count.EqualTo(4));
            Assert.That(destination.PropertyBlock.shaderProperties[0], Is.Null);
            Assert.That(((UMAFloatArrayProperty)destination.PropertyBlock.shaderProperties[1]).Value, Is.Null);
        }

        [Test]
        public void BulkCopyIncludesOnlyCheckedColorsAndKeepsIndependentNamedSnapshots()
        {
            var source = MakeSource();
            source.isSelected = true;
            source.name = "Skin";
            var other = new OverlayColorData(2) { name = "Eyes", isSelected = true, color = Color.blue };
            CopyChecked(source, null, new OverlayColorData { name = "Unchecked" }, other);
            source.name = "Edited name";
            source.channelMask[0] = Color.black;
            source.PropertyBlock.GetProperty<UMAFloatArrayProperty>("_Floats").Value[0] = 100;

            var copied = NamedCopies();
            Assert.That(copied, Has.Length.EqualTo(2));
            Assert.That(copied[0].name, Is.EqualTo("Skin"));
            Assert.That(copied[1].name, Is.EqualTo("Eyes"));
            Assert.That(copied[1].color, Is.EqualTo(Color.blue));
            AssertSettings(MakeSource(), copied[0]);
            Assert.That(copied[0].isSelected || copied[0].deleteThis || copied[0].moveUpThis, Is.False);
            copied[0].PropertyBlock.GetProperty<UMAFloatArrayProperty>("_Floats").Value[0] = 200;
            copied[0].channelMask[0] = Color.green;
            AssertSettings(MakeSource(), NamedCopies()[0]);
        }

        [Test]
        public void BulkAndValuesOnlyClipboardsSurviveSerializationIndependently()
        {
            var single = new OverlayColorData(1) { name = "Single name excluded", color = Color.red };
            var source = MakeSource();
            source.name = "Named bulk color";
            source.isSelected = true;
            string path = "Assets/UMA/Core/Editor/Tests/BulkClipboardTexture_" + Guid.NewGuid().ToString("N") + ".asset";
            var texture = new Texture2D(2, 2);
            AssetDatabase.CreateAsset(texture, path);
            try
            {
                source.PropertyBlock.GetProperty<UMATextureProperty>("_Texture").Value = texture;
                Copy(single);
                CopyChecked(source);
                string saved = EditorJsonUtility.ToJson(clipboard);
                Assert.That(saved, Does.Not.Contain(single.name));
                Copy(new OverlayColorData(0));
                CopyChecked();
                EditorJsonUtility.FromJsonOverwrite(saved, clipboard);

                var destination = new OverlayColorData { name = "Destination" };
                Paste(destination);
                Assert.That(destination.name, Is.EqualTo("Destination"));
                Assert.That(destination.color, Is.EqualTo(Color.red));
                AssertSettings(source, NamedCopies()[0]);
                Assert.That(NamedCopies()[0].name, Is.EqualTo(source.name));
                Copy(new OverlayColorData(2));
                AssertSettings(source, NamedCopies()[0]);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        [Test]
        public void PasteUpdateMatchesNamesAddsMissingColorsAndSupportsUndoRedo()
        {
            var source = MakeSource();
            source.name = "Skin";
            source.isSelected = true;
            CopyChecked(source, new OverlayColorData(1) { name = "Eyes", isSelected = true, color = Color.green });
            var existing = new OverlayColorData(2) { name = "Skin", color = Color.blue, isSelected = true, colorsExpanded = true };
            existing.SetFloatProperty("_Old", 1);
            var table = MakeTable(2, existing, new OverlayColorData(2) { name = "Unrelated", color = Color.yellow });
            Undo.IncrementCurrentGroup();
            InvokeTableEditor(table, "PasteOrUpdateColors", table);

            Assert.That(table.colors, Has.Length.EqualTo(3));
            Assert.That(table.colors[0], Is.SameAs(existing));
            Assert.That(existing.name, Is.EqualTo("Skin"));
            AssertSettings(source, existing);
            Assert.That(existing.PropertyBlock.GetProperty<UMAFloatProperty>("_Old"), Is.Null);
            Assert.That(existing.isSelected && existing.colorsExpanded, Is.True);
            Assert.That(existing.deleteThis || existing.moveUpThis, Is.False);
            Assert.That(table.channelCount, Is.EqualTo(5));
            Assert.That(table.colors[1].name, Is.EqualTo("Unrelated"));
            Assert.That(table.colors[1].color, Is.EqualTo(Color.yellow));
            Assert.That(table.colors[2].name, Is.EqualTo("Eyes"));
            Assert.That(table.colors[2].color, Is.EqualTo(Color.green));
            Assert.That(table.colors[2].channelMask[4], Is.EqualTo(Color.white));
            Assert.That(table.colors[2].channelAdditiveMask[4], Is.EqualTo(Color.clear));

            Undo.FlushUndoRecordObjects();
            Undo.PerformUndo();
            Assert.That(table.channelCount, Is.EqualTo(2));
            Assert.That(table.colors, Has.Length.EqualTo(2));
            Assert.That(table.colors[0].color, Is.EqualTo(Color.blue));
            Assert.That(table.colors[0].PropertyBlock.GetProperty<UMAFloatProperty>("_Old").Value, Is.EqualTo(1));
            Undo.PerformRedo();
            Assert.That(table.colors, Has.Length.EqualTo(3));
            AssertSettings(source, table.colors[0]);
            InvokeTableEditor(table, "PasteOrUpdateColors", table);
            Assert.That(table.colors, Has.Length.EqualTo(3), "Repeated paste must update the previously added entry.");
        }

        [Test]
        public void PasteUpdateUsesExactNamesAndUpdatesExistingDuplicates()
        {
            CopyChecked(new OverlayColorData(1) { name = "Skin", isSelected = true, color = Color.red });
            var table = MakeTable(3,
                new OverlayColorData(3) { name = "Skin", color = Color.blue },
                new OverlayColorData(3) { name = "skin", color = Color.green },
                new OverlayColorData(3) { name = "Skin", color = Color.blue });
            InvokeTableEditor(table, "PasteOrUpdateColors", table);
            Assert.That(table.colors, Has.Length.EqualTo(3));
            Assert.That(table.colors[0].color, Is.EqualTo(Color.red));
            Assert.That(table.colors[1].color, Is.EqualTo(Color.green));
            Assert.That(table.colors[2].color, Is.EqualTo(Color.red));
            Assert.That(table.channelCount, Is.EqualTo(3));
        }

        [Test]
        public void BulkPasteIntoEmptyTableAndEmptyClipboardAreSupported()
        {
            var table = MakeTable(1);
            CopyChecked();
            InvokeTableEditor(table, "PasteOrUpdateColors", table);
            Assert.That(table.colors, Is.Empty);
            CopyChecked(new OverlayColorData(2) { name = "New named color", isSelected = true, color = Color.cyan });
            InvokeTableEditor(table, "PasteOrUpdateColors", table);
            Assert.That(table.colors, Has.Length.EqualTo(1));
            Assert.That(table.colors[0].name, Is.EqualTo("New named color"));
            Assert.That(table.colors[0].color, Is.EqualTo(Color.cyan));
            Assert.That(table.channelCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator BothViewsKeepMultipleCheckedColorsAndShowTheirCheckboxes()
        {
            var table = MakeTable(1,
                new OverlayColorData(1) { name = "First", isSelected = true },
                new OverlayColorData(1) { name = "Second", isSelected = true });
            var editor = Editor.CreateEditor(table);
            var window = ScriptableObject.CreateInstance<UMA.Tests.AvatarInspectorTestWindow>();
            try
            {
                window.Unscrolled = true;
                window.position = new Rect(100, 100, 650, 700);
                window.ShowUtility();
                foreach (string method in new[] { "DrawStandardInspector", "DrawAdvancedInspector" })
                {
                    var draw = typeof(SharedColorTableEditor).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
                    window.DrawExtra = () => draw.Invoke(editor, null);
                    int repaints = window.Repaints;
                    double timeout = EditorApplication.timeSinceStartup + 10;
                    while (window.Repaints < repaints + 2 && EditorApplication.timeSinceStartup < timeout)
                    {
                        window.Repaint();
                        yield return null;
                    }
                    Assert.That(window.Repaints, Is.GreaterThanOrEqualTo(repaints + 2));
                    Assert.That(table.colors[0].isSelected, Is.True, method);
                    Assert.That(table.colors[1].isSelected, Is.True, method);
                    Assert.That(table.colors[0].showSelected && table.colors[1].showSelected, Is.True, method);
                }
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(editor);
            }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StandardCheckboxCanBeCheckedAndUncheckedWhileAnotherColorIsChecked()
        {
            var table = MakeTable(1,
                new OverlayColorData(1) { name = "First", isSelected = true },
                new OverlayColorData(1) { name = "Second", showSelected = true });
            var editor = Editor.CreateEditor(table);
            var window = ScriptableObject.CreateInstance<UMA.Tests.AvatarInspectorTestWindow>();
            try
            {
                var processCommands = typeof(SharedColorTableEditor).GetMethod("ProcessStandardColorCommands", BindingFlags.Instance | BindingFlags.NonPublic);
                var color = editor.serializedObject.FindProperty("colors").GetArrayElementAtIndex(1);
                color.FindPropertyRelative("name").isExpanded = false;
                Vector2 point = default;
                window.Unscrolled = true;
                window.position = new Rect(100, 100, 650, 150);
                window.DrawExtra = () =>
                {
                    editor.serializedObject.Update();
                    EditorGUILayout.PropertyField(color, true);
                    if (Event.current.type == EventType.Repaint)
                    {
                        Rect separator = GUILayoutUtility.GetLastRect();
                        point = new Vector2(separator.xMin + 10, separator.yMin - EditorGUIUtility.singleLineHeight / 2)
                            + GUIUtility.GUIToScreenPoint(Vector2.zero) - window.position.position;
                    }
                    editor.serializedObject.ApplyModifiedProperties();
                    processCommands.Invoke(editor, new object[] { table });
                };
                window.ShowUtility();
                double timeout = EditorApplication.timeSinceStartup + 10;
                while (window.Repaints < 2 && EditorApplication.timeSinceStartup < timeout)
                {
                    window.Repaint();
                    yield return null;
                }
                Assert.That(window.Repaints, Is.GreaterThanOrEqualTo(2));
                foreach (bool expected in new[] { true, false })
                {
                    window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = point });
                    yield return null;
                    window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = point });
                    yield return null;
                    Assert.That(table.colors[0].isSelected, Is.True);
                    Assert.That(table.colors[1].isSelected, Is.EqualTo(expected));
                }
            }
            finally
            {
                window.Close();
                Object.DestroyImmediate(editor);
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
