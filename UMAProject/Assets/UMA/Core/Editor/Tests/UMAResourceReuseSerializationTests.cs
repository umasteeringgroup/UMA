#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Tests
{
    public class UMAResourceReuseSerializationTests
    {
        [TestCase(typeof(UMAData), nameof(UMAData.reuseGeneratedMeshes))]
        [TestCase(typeof(UMAData), nameof(UMAData.reuseGeneratedTextures))]
        [TestCase(typeof(UMAAvatarBase), nameof(UMAData.reuseGeneratedMeshes))]
        [TestCase(typeof(UMAAvatarBase), nameof(UMAData.reuseGeneratedTextures))]
        [TestCase(typeof(DynamicCharacterAvatar), nameof(UMAData.reuseGeneratedMeshes))]
        [TestCase(typeof(DynamicCharacterAvatar), nameof(UMAData.reuseGeneratedTextures))]
        public void ReuseSettingHasExactlyOneSerializedDeclaration(Type avatarType, string fieldName)
        {
            var declarations = new List<FieldInfo>();
            // Flattened reflection can hide a base field. Unity serializes the entire hierarchy.
            for (var type = avatarType; type != null; type = type.BaseType)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null && !field.IsNotSerialized && !field.IsInitOnly &&
                    (field.IsPublic || field.IsDefined(typeof(SerializeField), false) ||
                     field.IsDefined(typeof(SerializeReference), false)))
                    declarations.Add(field);
            }

            Assert.That(declarations.Count, Is.EqualTo(1),
                $"{avatarType.Name}.{fieldName} must not hide an inherited serialized setting.");
            Assert.That(declarations[0].DeclaringType, Is.EqualTo(typeof(UMAData)));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void InspectorSettingsRoundTripAndDriveTheSameBaseFields(bool meshes, bool textures)
        {
            var sourceObject = new GameObject("Reuse serialization source");
            var restoredObject = new GameObject("Reuse serialization restored");
            sourceObject.SetActive(false);
            restoredObject.SetActive(false);
            try
            {
                var avatar = sourceObject.AddComponent<DynamicCharacterAvatar>();
                var restored = restoredObject.AddComponent<DynamicCharacterAvatar>();
                avatar.editorTimeGeneration = false;
                restored.editorTimeGeneration = false;
                AssertSettings(avatar, false, false); // Opt-in, including on new DCAs.

                using (var serialized = new SerializedObject(avatar))
                {
                    var meshSetting = serialized.FindProperty(nameof(UMAData.reuseGeneratedMeshes));
                    var textureSetting = serialized.FindProperty(nameof(UMAData.reuseGeneratedTextures));
                    Assert.That(meshSetting, Is.Not.Null);
                    Assert.That(textureSetting, Is.Not.Null);
                    meshSetting.boolValue = meshes;
                    textureSetting.boolValue = textures;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                AssertSettings(avatar, meshes, textures);

                string json = JsonUtility.ToJson(avatar);
                Assert.That(CountField(json, nameof(UMAData.reuseGeneratedMeshes)), Is.EqualTo(1));
                Assert.That(CountField(json, nameof(UMAData.reuseGeneratedTextures)), Is.EqualTo(1));
                JsonUtility.FromJsonOverwrite(json, restored);
                AssertSettings(restored, meshes, textures);

                // Changes through the generator's UMAData reference must also reach the DCA UI.
                UMAData data = restored.umaData;
                data.reuseGeneratedMeshes = !meshes;
                data.reuseGeneratedTextures = !textures;
                using (var serialized = new SerializedObject(restored))
                {
                    Assert.That(serialized.FindProperty(nameof(UMAData.reuseGeneratedMeshes)).boolValue,
                        Is.EqualTo(!meshes));
                    Assert.That(serialized.FindProperty(nameof(UMAData.reuseGeneratedTextures)).boolValue,
                        Is.EqualTo(!textures));
                }
                AssertSettings(restored, !meshes, !textures);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(restoredObject);
                UnityEngine.Object.DestroyImmediate(sourceObject);
            }
        }

        private static void AssertSettings(DynamicCharacterAvatar avatar, bool meshes, bool textures)
        {
            Assert.That(avatar.reuseGeneratedMeshes, Is.EqualTo(meshes));
            Assert.That(avatar.reuseGeneratedTextures, Is.EqualTo(textures));
            Assert.That(avatar.umaData.reuseGeneratedMeshes, Is.EqualTo(meshes));
            Assert.That(avatar.umaData.reuseGeneratedTextures, Is.EqualTo(textures));
            Assert.That(UMAResourceReuse.MeshesEnabled(avatar.umaData), Is.EqualTo(meshes));
            Assert.That(UMAResourceReuse.TexturesEnabled(avatar.umaData), Is.EqualTo(textures));
        }

        private static int CountField(string json, string fieldName) =>
            json.Split(new[] { "\"" + fieldName + "\":" }, StringSplitOptions.None).Length - 1;
    }
}
#endif
