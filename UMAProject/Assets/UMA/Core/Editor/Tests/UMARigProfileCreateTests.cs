using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The Rig Profile Editor's New button (<see cref="UMARigProfileEditorWindow.CreateProfileAsset"/>): the new profile
    /// is written to disk, other unsaved assets are not.
    /// </summary>
    public sealed class UMARigProfileCreateTests
    {
        private string folder;
        private RaceData race;

        [SetUp]
        public void SetUp()
        {
            string name = "__UMARigProfileCreateTests_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            folder = "Assets/" + name;
            AssetDatabase.CreateFolder("Assets", name);
        }

        [TearDown]
        public void TearDown()
        {
            if (race != null)
            {
                Undo.ClearUndo(race);
                Object.DestroyImmediate(race);
            }
            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
        }

        private RaceData Race()
        {
            race = ScriptableObject.CreateInstance<RaceData>();
            race.name = "Profile Test Race";   // raceName follows the asset name
            return race;
        }

        [Test]
        public void NewProfileIsWrittenWithoutSavingOtherUnsavedAssets()
        {
            // Another asset with unsaved changes, like one open in an inspector.
            var bystander = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            AssetDatabase.CreateAsset(bystander, folder + "/Bystander.asset");
            bystander.defaultMode = UMARigBoneCorrectionMode.TranslateOnly;
            EditorUtility.SetDirty(bystander);
            Assert.IsTrue(EditorUtility.IsDirty(bystander), "the check needs an unsaved asset");
            RaceData testRace = Race();

            UMASlotRigConversionProfile created = UMARigProfileEditorWindow.CreateProfileAsset(testRace, folder);

            Assert.IsTrue(EditorUtility.IsPersistent(created));
            Assert.AreEqual(folder + "/Profile Test Race Legacy Rig Profile.asset", AssetDatabase.GetAssetPath(created));
            Assert.IsFalse(EditorUtility.IsDirty(created), "the new profile is on disk");
            Assert.AreSame(created, testRace.legacyRigProfile, "a race without a profile gets the new one");
            Assert.IsTrue(EditorUtility.IsDirty(bystander), "another asset's unsaved changes must not be written");
        }

        [Test]
        public void RaceWithAProfileOfItsOwnKeepsIt()
        {
            var own = ScriptableObject.CreateInstance<UMASlotRigConversionProfile>();
            AssetDatabase.CreateAsset(own, folder + "/Own.asset");
            RaceData testRace = Race();
            testRace.legacyRigProfile = own;

            UMASlotRigConversionProfile created = UMARigProfileEditorWindow.CreateProfileAsset(testRace, folder);

            Assert.AreNotSame(own, created);
            Assert.IsTrue(EditorUtility.IsPersistent(created));
            Assert.AreSame(own, testRace.legacyRigProfile);
        }
    }
}
