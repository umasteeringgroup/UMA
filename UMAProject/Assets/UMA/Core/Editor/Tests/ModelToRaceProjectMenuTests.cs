using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UMA.Editors.ModelToRace;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class ModelToRaceProjectMenuTests
    {
        private string folder;
        private Object[] previousSelection;

        [SetUp]
        public void SetUp()
        {
            previousSelection = Selection.objects;
            folder = "Assets/__ModelToRaceMenuTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<ModelToRaceWindow>()) window.Close();
            Selection.objects = previousSelection;
            AssetDatabase.DeleteAsset(folder);
        }

        private GameObject Model(string name)
        {
            var root = new GameObject(name);
            try
            {
                var child = new GameObject("Skinned part");
                child.transform.SetParent(root.transform);
                child.AddComponent<SkinnedMeshRenderer>();
                return PrefabUtility.SaveAsPrefabAsset(root, folder + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static bool MenuEnabled() => (bool)typeof(ModelToRaceWindow)
            .GetMethod("ValidateSelectedModel", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);

        [UnityTest]
        public IEnumerator ProjectMenuOpensSelectedRootAndReplacesPreviousModelFromChild()
        {
            var first = Model("First model");
            var second = Model("Second model");
            foreach (var model in new[] { first, second })
            {
                Selection.activeObject = model == first ? model : model.transform.GetChild(0).gameObject;
                Assert.That(MenuEnabled(), Is.True);
                Assert.That(EditorApplication.ExecuteMenuItem("Assets/UMA/Model To Race/Clothing..."), Is.True);
                yield return null;
                Assert.That(EditorWindow.HasOpenInstances<ModelToRaceWindow>(), Is.True);
                var window = EditorWindow.GetWindow<ModelToRaceWindow>();
                var serialized = new SerializedObject(window);
                Assert.That(serialized.FindProperty("plan").FindPropertyRelative("source").objectReferenceValue, Is.EqualTo(model));
                Assert.That(serialized.FindProperty("page").intValue, Is.Zero);
                var field = window.rootVisualElement.Query<ObjectField>().Where(f => f.label == "Model root").First();
                Assert.That(field, Is.Not.Null, "Source dialog must be populated.");
                Assert.That(field.value, Is.EqualTo(model));
            }
        }

        [Test]
        public void ProjectMenuRejectsFoldersSceneObjectsAndMultipleModels()
        {
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(folder);
            Assert.That(MenuEnabled(), Is.False);
            var sceneObject = new GameObject("Not a project model");
            try
            {
                sceneObject.AddComponent<SkinnedMeshRenderer>();
                Selection.activeObject = sceneObject;
                Assert.That(MenuEnabled(), Is.False);
            }
            finally { Object.DestroyImmediate(sceneObject); }
            Selection.objects = new Object[] { Model("One"), Model("Two") };
            Assert.That(MenuEnabled(), Is.False);
        }
    }
}
