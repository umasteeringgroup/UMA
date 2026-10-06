using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class PluginTestRecipe : UMARecipeBase
    {
        public string value = "initial";
        public override void Load(UMAData.UMARecipe recipe, bool loadSlots = true) => recipe.recipeName = value;
        public override void Load(UMAData.UMARecipe recipe, RaceData race) => Load(recipe);
        public override void Save(UMAData.UMARecipe recipe) => value = recipe.recipeName;
        public override string GetInfo() => value;
        public override byte[] GetBytes() => System.Text.Encoding.UTF8.GetBytes(value);
        public override void SetBytes(byte[] data) => value = System.Text.Encoding.UTF8.GetString(data);
    }
    public sealed class PluginTestRecipeEditor : RecipeEditor
    {
        public override void OnEnable() { Initialized = true; RefreshAfterPluginAction(); }
        public override void OnDisable() { }
        public string WorkingValue => _recipe.recipeName;
        public void EditWorkingValue(string value) { _recipe.recipeName = value; _needsUpdate = true; }
        public bool Synchronize() => SynchronizePluginRecipe();
    }
    public sealed class UMAPluginRecipeHostTests
    {
        private PluginTestRecipe recipe;
        private PluginTestRecipeEditor editor;
        [SetUp] public void SetUp()
        {
            recipe = ScriptableObject.CreateInstance<PluginTestRecipe>();
            editor = (PluginTestRecipeEditor)Editor.CreateEditor(recipe, typeof(PluginTestRecipeEditor));
        }
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(editor); Object.DestroyImmediate(recipe);
        }
        [Test] public void PreparationCommitsWorkingRecipeBeforeAction()
        {
            editor.EditWorkingValue("pending");
            Assert.That(recipe.value, Is.EqualTo("initial"));
            Assert.That(editor.TryPreparePluginAction(), Is.True);
            Assert.That(recipe.value, Is.EqualTo("pending"));
            recipe.value = "plugin";
            editor.RefreshAfterPluginAction();
            Assert.That(editor.WorkingValue, Is.EqualTo("plugin"));
            Assert.That(editor.TryPreparePluginAction(), Is.True);
            Assert.That(recipe.value, Is.EqualTo("plugin"));
        }
        [Test] public void ExternalRevisionReloadsWhenThereAreNoPendingEdits()
        {
            recipe.value = "external";
            Assert.That(editor.Synchronize(), Is.True);
            Assert.That(editor.WorkingValue, Is.EqualTo("external"));
        }
        [Test] public void ExternalRevisionCannotBeOverwrittenByPendingWorkingRecipe()
        {
            editor.EditWorkingValue("local"); recipe.value = "external";
            Assert.That(editor.TryPreparePluginAction(), Is.False);
            Assert.That(editor.CanRunPluginActions, Is.False);
            Assert.That(editor.WorkingValue, Is.EqualTo("local"));
            Assert.That(recipe.value, Is.EqualTo("external"));
            editor.RefreshAfterPluginAction();
            Assert.That(editor.CanRunPluginActions, Is.True);
            Assert.That(editor.WorkingValue, Is.EqualTo("external"));
        }
        [Test] public void UndoReloadsWorkingRecipeWithoutResavingTheUndoneValue()
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(recipe, "Plugin recipe change");
            recipe.value = "plugin"; Undo.FlushUndoRecordObjects();
            editor.RefreshAfterPluginAction();
            Undo.PerformUndo();
            Assert.That(editor.Synchronize(), Is.True);
            Assert.That(editor.WorkingValue, Is.EqualTo("initial"));
            Assert.That(editor.TryPreparePluginAction(), Is.True);
            Assert.That(recipe.value, Is.EqualTo("initial"));
        }
    }
}
