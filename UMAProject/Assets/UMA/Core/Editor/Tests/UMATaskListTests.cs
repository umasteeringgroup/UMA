#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMATaskListTests
    {
        private string _createdTaskPath;
        private UMATaskItem _working;

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_createdTaskPath))
                AssetDatabase.DeleteAsset(_createdTaskPath);
            if (_working != null)
                UnityEngine.Object.DestroyImmediate(_working);
            _createdTaskPath = null;
            AssetDatabase.Refresh();
        }

        [Test]
        public void NewTaskHasValidTodayDateAndExpectedDefaults()
        {
            _working = ScriptableObject.CreateInstance<UMATaskItem>();

            Assert.IsTrue(_working.TryGetDate(out DateTime date));
            Assert.AreEqual(DateTime.Today, date);
            Assert.AreEqual(UMATaskStatus.New, _working.Status);
            Assert.AreEqual(UMATaskCategory.General, _working.Category);
            Assert.IsNotNull(_working.ObjectReferences);
        }

        [Test]
        public void NewTaskIsImmediatelyCreatedAsPersistentAsset()
        {
            UMATaskItem asset =
                UMATaskListStorage.CreateNewTaskAsset();
            _createdTaskPath = AssetDatabase.GetAssetPath(asset);

            Assert.IsTrue(EditorUtility.IsPersistent(asset));
            StringAssert.StartsWith(
                UMATaskListStorage.TaskFolder + "/",
                _createdTaskPath);
            Assert.AreEqual("UMATaskItem", asset.GetType().Name);
        }

        [Test]
        public void TaskAssetIsCreatedUnderRequiredFolderAndLoaded()
        {
            UMATaskItem asset =
                UMATaskListStorage.CreateNewTaskAsset();
            _createdTaskPath = AssetDatabase.GetAssetPath(asset);
            asset.Title = "Mesh Combiner regression / planning";
            asset.Category = UMATaskCategory.MeshCombiners;
            asset.Status = UMATaskStatus.InProcess;
            asset.SetDate(new DateTime(2030, 4, 12));
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);

            StringAssert.StartsWith(
                UMATaskListStorage.TaskFolder + "/",
                _createdTaskPath);
            Assert.AreEqual("2030-04-12", asset.TaskDate);
            Assert.AreEqual(
                UMATaskCategory.MeshCombiners, asset.Category);
            Assert.IsTrue(
                UMATaskListStorage.LoadTasks().Contains(asset));
        }

        [Test]
        public void DirectAssetEditsPersistAllTaskInformation()
        {
            UMATaskItem asset =
                UMATaskListStorage.CreateNewTaskAsset();
            _createdTaskPath = AssetDatabase.GetAssetPath(asset);

            asset.Title = "Updated clothing task";
            asset.Description =
                "Create and validate the new clothing set.";
            asset.Category = UMATaskCategory.Clothing;
            asset.Status = UMATaskStatus.Done;
            asset.SetDate(new DateTime(2031, 7, 9));
            asset.ObjectReferences.Add(asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.ImportAsset(
                _createdTaskPath,
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            UMATaskItem loaded =
                AssetDatabase.LoadAssetAtPath<UMATaskItem>(
                    _createdTaskPath);

            Assert.AreEqual("Updated clothing task", loaded.Title);
            Assert.AreEqual(
                "Create and validate the new clothing set.",
                loaded.Description);
            Assert.AreEqual(UMATaskCategory.Clothing, loaded.Category);
            Assert.AreEqual(UMATaskStatus.Done, loaded.Status);
            Assert.AreEqual("2031-07-09", loaded.TaskDate);
            Assert.AreEqual(1, loaded.ObjectReferences.Count);
            Assert.AreSame(loaded, loaded.ObjectReferences[0]);
        }
    }
}
#endif
