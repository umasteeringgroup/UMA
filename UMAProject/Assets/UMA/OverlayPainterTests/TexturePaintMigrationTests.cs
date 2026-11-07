using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintMigrationTests
    {
        [Test] public void InstallationAnchorResolvesPackagedResources()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<ComputeShader>(
                TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Shaders/StrokeRasterize.compute")), Is.Not.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<Texture2D>(
                TexturePaintAssets.ResolveInstallAssetPath("OverlayPainter/Editor/Icons/TexturePaintIcons.png")), Is.Not.Null);
            Assert.Throws<ArgumentException>(() => TexturePaintAssets.ResolveInstallAssetPath("../Core"));
        }

        [Test] public void LegacySettingsCopyAllFiveValuesWithoutRetainingLegacyOwnership()
        {
            var legacy = ScriptableObject.CreateInstance<UMASettings>();
            var settings = ScriptableObject.CreateInstance<TexturePaintProjectSettings>();
            try
            {
                legacy.texturePaintCompactView = false;
                legacy.texturePaintAutomaticRecovery = false;
                legacy.texturePaintRecoveryFolder = "Assets/CustomRecovery";
                legacy.texturePaintRecoveryIdleDelaySeconds = 45;
                legacy.texturePaintRecoveryMinimumIntervalSeconds = 90;
                settings.ImportLegacy(legacy);
                legacy.texturePaintCompactView = true;
                using var serialized = new SerializedObject(settings);
                Assert.That(serialized.FindProperty("compactView").boolValue, Is.False);
                Assert.That(serialized.FindProperty("automaticRecovery").boolValue, Is.False);
                Assert.That(serialized.FindProperty("recoveryFolder").stringValue, Is.EqualTo("Assets/CustomRecovery"));
                Assert.That(serialized.FindProperty("recoveryIdleDelaySeconds").floatValue, Is.EqualTo(45));
                Assert.That(serialized.FindProperty("recoveryMinimumIntervalSeconds").floatValue, Is.EqualTo(90));
            }
            finally { UnityEngine.Object.DestroyImmediate(settings); UnityEngine.Object.DestroyImmediate(legacy); }
        }

        [Test] public void PainterOwnsSingleTargetAvatarAndSlotRegistrations()
        {
            var registrations = typeof(TexturePaintPluginActions).GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
                .SelectMany(m => m.GetCustomAttributes<UMAPluginAttribute>()).ToArray();
            Assert.That(registrations.Length, Is.EqualTo(2));
            Assert.That(registrations.Select(a => a.TargetType), Is.EquivalentTo(new[] { typeof(UMA.CharacterSystem.DynamicCharacterAvatar), typeof(SlotDataAsset) }));
            Assert.That(registrations.All(a => !a.SupportsMultipleTargets && !string.IsNullOrEmpty(a.ValidateMethod)), Is.True);
        }

        [Test] public void PersistentAvatarAssetsCannotLaunchScenePainting()
        {
            string path = UMAPathUtility.ResolveInstallAssetPath("Core/Defaults/UMADynamicCharacterAvatar.prefab");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var avatar = prefab.GetComponent<UMA.CharacterSystem.DynamicCharacterAvatar>();
            Assert.That(avatar, Is.Not.Null);
            var editor = UnityEditor.Editor.CreateEditor(avatar);
            try
            {
                var validate = typeof(TexturePaintPluginActions).GetMethod("CanPaintAvatar", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(validate.Invoke(null, new object[] { editor }), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(editor); }
        }
    }
}
