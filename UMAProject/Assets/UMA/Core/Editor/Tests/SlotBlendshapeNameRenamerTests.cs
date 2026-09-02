#if UNITY_EDITOR

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class SlotBlendshapeNameRenamerTests
    {
        private SlotDataAsset slot;

        [SetUp]
        public void SetUp()
        {
            slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.meshData = new UMAMeshData();
        }

        [TearDown]
        public void TearDown()
        {
            if (slot != null)
            {
                Object.DestroyImmediate(slot);
            }
        }

        [Test]
        public void PreviewRemovesEveryExactCaseSensitiveOccurrence()
        {
            slot.meshData.blendShapes = new[]
            {
                Shape("Prefix.BrowsBS.Left.BrowsBS.End"),
                Shape("prefix.browsbs.unchanged")
            };

            IList previews = InvokePreview(slot, "BrowsBS.");

            Assert.That(previews.Count, Is.EqualTo(1));
            object preview = previews[0];
            Assert.That(ReadPreviewField(preview, "Index"), Is.EqualTo(0));
            Assert.That(
                ReadPreviewField(preview, "Renamed"),
                Is.EqualTo("Prefix.Left.End"));
        }

        [Test]
        public void ValidationReportsCollisionCreatedByRename()
        {
            slot.meshData.blendShapes = new[]
            {
                Shape("BrowsBS.Smile"),
                Shape("Smile")
            };

            string error = InvokeNameValidation(slot, "BrowsBS.");

            Assert.That(error, Does.Contain("would create duplicate"));
            Assert.That(error, Does.Contain("indices 0 and 1"));
        }

        [Test]
        public void ValidationDistinguishesPreExistingDuplicateNames()
        {
            slot.meshData.blendShapes = new[]
            {
                Shape("Smile"),
                Shape("Smile"),
                Shape("BrowsBS.Frown")
            };

            string error = InvokeNameValidation(slot, "BrowsBS.");

            Assert.That(error, Does.Contain("already contains duplicate"));
            Assert.That(error, Does.Contain("indices 0 and 1"));
        }

        [Test]
        public void ValidationReportsExistingEmptyNameAccurately()
        {
            slot.meshData.blendShapes = new[]
            {
                Shape(string.Empty),
                Shape("BrowsBS.Frown")
            };

            string error = InvokeNameValidation(slot, "BrowsBS.");

            Assert.That(error, Does.Contain("already has an empty name"));
            Assert.That(error, Does.Contain("index 0"));
        }

        private static UMABlendShape Shape(string name)
        {
            return new UMABlendShape
            {
                shapeName = name,
                frames = new UMABlendFrame[0]
            };
        }

        private static IList InvokePreview(SlotDataAsset target, string removalText)
        {
            MethodInfo method = typeof(SlotBlendshapeNameRenamer).GetMethod(
                "BuildPreview", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (IList)method.Invoke(null, new object[] { target, removalText });
        }

        private static object ReadPreviewField(object preview, string fieldName)
        {
            FieldInfo field = preview.GetType().GetField(
                fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(preview);
        }

        private static string InvokeNameValidation(
            SlotDataAsset target, string removalText)
        {
            MethodInfo method = typeof(SlotBlendshapeNameRenamer).GetMethod(
                "ValidateNames", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(null, new object[] { target, removalText });
        }
    }
}

#endif
