using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.CharacterSystem;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class WardrobeStandardInspectorTests
    {
        private readonly List<Object> objects = new List<Object>();
        private static readonly FieldInfo Indexer = typeof(UMAAssetIndexer).GetField("theIndexer", BindingFlags.Static | BindingFlags.NonPublic);
        private object oldIndexer;
        private UMASettings oldSettings;
        private bool hadPreference, oldPreference;
        private UMAWardrobeRecipe wardrobe;
        private SlotDataAsset slotAsset;
        private OverlayDataAsset overlayAsset;
        private UMAMaterial umaMaterial;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private T Make<T>() where T : ScriptableObject
        { var value = ScriptableObject.CreateInstance<T>(); objects.Add(value); return value; }

        [SetUp]
        public void SetUp()
        {
            string key = UMAInspectorView.PreferenceKey(typeof(UMAWardrobeRecipe));
            hadPreference = EditorPrefs.HasKey(key); oldPreference = EditorPrefs.GetBool(key); EditorPrefs.SetBool(key, false);
            oldIndexer = Indexer.GetValue(null); oldSettings = UMASettings.instance;
            UMASettings.instance = Make<UMASettings>(); UMASettings.instance.autoRepairIndex = false;
            var indexer = Make<UMAAssetIndexer>(); Indexer.SetValue(null, indexer);
            var race = Make<RaceData>(); race.name = "Wardrobe Test Race";
            slotAsset = Make<SlotDataAsset>(); slotAsset.name = "Wardrobe Test Slot";
            slotAsset.tags = new[] { "Asset tag" }; slotAsset.Races = new[] { "Asset race" };
            umaMaterial = Make<UMAMaterial>(); umaMaterial.name = "Wardrobe Test Material";
            umaMaterial.channels = new UMAMaterial.MaterialChannel[3];
            var material = new Material(Shader.Find("Unlit/Texture")); objects.Add(material); umaMaterial.material = material;
            overlayAsset = Make<OverlayDataAsset>(); overlayAsset.name = "Wardrobe Test Overlay";
            overlayAsset.material = umaMaterial;
            overlayAsset.textureList = new Texture[] { Texture2D.whiteTexture, Texture2D.grayTexture, Texture2D.blackTexture };
            indexer.SerializedItems.Clear();
            indexer.SerializedItems.Add(new AssetItem(typeof(RaceData), race.name, "", race));
            indexer.SerializedItems.Add(new AssetItem(typeof(SlotDataAsset), slotAsset.slotName, "", slotAsset));
            indexer.SerializedItems.Add(new AssetItem(typeof(OverlayDataAsset), overlayAsset.overlayName, "", overlayAsset));
            indexer.SerializedItems.Add(new AssetItem(typeof(UMAMaterial), umaMaterial.name, "", umaMaterial));
            indexer.DoInitialDictionaryLoad();
            wardrobe = Make<UMAWardrobeRecipe>(); wardrobe.name = "Wardrobe Standard Test";
            wardrobe.compatibleRaces.Add(race.raceName); wardrobe.compatibleRaces.Add("Uninstalled race");
            wardrobe.wardrobeSlot = "Uninstalled region"; wardrobe.replaces = "Preserve replacement";
            wardrobe.UserField = "Preserve custom field";
            var color = new OverlayColorData(3) { name = "Cloth", color = Color.red };
            color.channelMask[1] = Color.cyan; color.channelAdditiveMask[2] = Color.blue;
            color.SetColorProperty("_Tint", Color.green); color.SetFloatProperty("_Gloss", 0.27f);
            var overlay = new OverlayData(overlayAsset) { colorData = color, Rotation = 17, Scale = new Vector2(0.5f, 0.7f), instanceTransformed = true };
            var slot = new SlotData(slotAsset) { tags = new[] { "Recipe tag" }, Races = new[] { race.raceName }, isDisabled = true, expandAlongNormal = 120 };
            slot.AddOverlay(overlay);
            var recipe = new UMAData.UMARecipe { sharedColors = new[] { color } };
            recipe.SetSlots(new[] { slot }); wardrobe.Save(recipe);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in objects) if (item != null) Undo.ClearUndo(item);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] is EditorWindow window) window.Close();
                else if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear(); Indexer.SetValue(null, oldIndexer); UMASettings.instance = oldSettings;
            string key = UMAInspectorView.PreferenceKey(typeof(UMAWardrobeRecipe));
            if (hadPreference) EditorPrefs.SetBool(key, oldPreference); else EditorPrefs.DeleteKey(key);
        }

        private UMAWardrobeRecipeEditor Inspector()
        {
            var editor = (UMAWardrobeRecipeEditor)Editor.CreateEditor(wardrobe); objects.Add(editor);
            typeof(RecipeEditor).GetMethod("InitializeEditor", Private).Invoke(editor, null);
            Invoke(editor, "ReloadStandardRecipe", wardrobe);
            return editor;
        }

        private static void Invoke(UMAWardrobeRecipeEditor editor, string method, params object[] args) =>
            typeof(UMAWardrobeRecipeEditor).GetMethod(method, Private).Invoke(editor, args);
        private static UMAData.UMARecipe Loaded(UMAWardrobeRecipeEditor editor) =>
            (UMAData.UMARecipe)typeof(CharacterBaseEditor).GetField("_recipe", Private).GetValue(editor);
        private static List<Action> Edits(UMAWardrobeRecipeEditor editor) =>
            (List<Action>)typeof(UMAWardrobeRecipeEditor).GetField("standardEdits", Private).GetValue(editor);

        [UnityTest]
        public IEnumerator StandardDrawDoesNotMutateRecipeOrSourceAssetsAtNarrowAndWideWidths()
        {
            var editor = Inspector();
            string before = EditorJsonUtility.ToJson(wardrobe);
            string slotBefore = EditorJsonUtility.ToJson(slotAsset), overlayBefore = EditorJsonUtility.ToJson(overlayAsset);
            var window = Make<AvatarInspectorTestWindow>(); window.DrawExtra = editor.OnInspectorGUI;
            window.position = new Rect(80, 80, 380, 900); window.Show();
            yield return Repaint(window);
            window.position = new Rect(80, 80, 900, 900);
            yield return Repaint(window);
            Assert.That(EditorJsonUtility.ToJson(wardrobe), Is.EqualTo(before));
            Assert.That(EditorJsonUtility.ToJson(slotAsset), Is.EqualTo(slotBefore));
            Assert.That(EditorJsonUtility.ToJson(overlayAsset), Is.EqualTo(overlayBefore));
            Assert.That(Loaded(editor).slotDataList[0].isDisabled, Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void StandardEditsSaveAndUndoWithoutDroppingAdvancedData()
        {
            var editor = Inspector();
            var loaded = Loaded(editor);
            string before = wardrobe.recipeString;
            editor.serializedObject.Update();
            editor.serializedObject.FindProperty("DisplayValue").stringValue = "Artist name";
            Edits(editor).Add(() => loaded.slotDataList[0].isDisabled = false);
            Edits(editor).Add(() => loaded.sharedColors[0].color = Color.yellow);
            Invoke(editor, "CommitStandardEdits");
            Undo.FlushUndoRecordObjects();
            var result = new UMAData.UMARecipe(); wardrobe.Load(result);
            Assert.That(result.slotDataList[0].isDisabled, Is.False);
            Assert.That(result.sharedColors[0].color, Is.EqualTo(Color.yellow));
            Assert.That(result.sharedColors[0].channelMask[1], Is.EqualTo(Color.cyan));
            Assert.That(result.sharedColors[0].channelAdditiveMask[2], Is.EqualTo(Color.blue));
            Assert.That(result.sharedColors[0].GetProperty<UMAFloatProperty>("_Gloss").Value, Is.EqualTo(0.27f).Within(0.001f));
            Assert.That(result.slotDataList[0].GetOverlay(0).Rotation, Is.EqualTo(17));
            Assert.That(result.slotDataList[0].GetOverlay(0).colorData, Is.SameAs(result.sharedColors[0]));
            Assert.That(result.slotDataList[0].expandAlongNormal, Is.EqualTo(120));
            Assert.That(slotAsset.tags, Is.EqualTo(new[] { "Asset tag" }));
            Assert.That(wardrobe.wardrobeSlot, Is.EqualTo("Uninstalled region"));
            Assert.That(wardrobe.replaces, Is.EqualTo("Preserve replacement"));
            Undo.PerformUndo();
            Assert.That(wardrobe.recipeString, Is.EqualTo(before));
            Invoke(editor, "ReloadStandardRecipe", wardrobe);
            Assert.That(Loaded(editor).slotDataList[0].isDisabled, Is.True);
            Undo.PerformRedo();
            Invoke(editor, "ReloadStandardRecipe", wardrobe);
            Assert.That(Loaded(editor).slotDataList[0].isDisabled, Is.False);
        }

        [Test]
        public void MetadataOnlyEditDoesNotRepackRecipe()
        {
            var editor = Inspector(); string before = wardrobe.recipeString;
            editor.serializedObject.Update();
            editor.serializedObject.FindProperty("DisplayValue").stringValue = "Renamed";
            Invoke(editor, "CommitStandardEdits");
            Assert.That(wardrobe.DisplayValue, Is.EqualTo("Renamed"));
            Assert.That(wardrobe.recipeString, Is.EqualTo(before));
        }

        [Test]
        public void RemovingSharedColorKeepsLocalAppearanceAndShaderProperties()
        {
            var editor = Inspector(); var recipe = Loaded(editor);
            var color = recipe.sharedColors[0];
            typeof(UMAWardrobeRecipeEditor).GetMethod("RemoveStandardColor", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { recipe, color });
            var local = recipe.slotDataList[0].GetOverlay(0).colorData;
            Assert.That(recipe.sharedColors, Is.Empty);
            Assert.That(local, Is.Not.SameAs(color));
            Assert.That(local.name, Is.EqualTo(OverlayColorData.UNSHARED));
            Assert.That(local.color, Is.EqualTo(color.color));
            Assert.That(local.channelMask[1], Is.EqualTo(color.channelMask[1]));
            Assert.That(local.GetProperty<UMAFloatProperty>("_Gloss").Value, Is.EqualTo(0.27f).Within(0.001f));
        }

        [Test]
        public void PreviewUsesTextureZeroOrExistingMaterialsMainTextureAndHandlesEmptyTextures()
        {
            var slot = new SlotData(slotAsset); var overlay = new OverlayData(overlayAsset); slot.AddOverlay(overlay);
            var preview = typeof(UMAWardrobeRecipeEditor).GetMethod("StandardBaseTexture", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(preview.Invoke(null, new object[] { slot, overlay }), Is.SameAs(Texture2D.whiteTexture));
            umaMaterial.materialType = UMAMaterial.MaterialType.UseExistingMaterial;
            umaMaterial.material.mainTexture = Texture2D.blackTexture;
            Assert.That(preview.Invoke(null, new object[] { slot, overlay }), Is.SameAs(Texture2D.blackTexture));
            umaMaterial.materialType = UMAMaterial.MaterialType.Atlas; overlayAsset.textureList = Array.Empty<Texture>();
            Assert.That(preview.Invoke(null, new object[] { slot, overlay }), Is.Null);
        }

        private static IEnumerator Repaint(AvatarInspectorTestWindow window)
        {
            int count = window.Repaints; double deadline = EditorApplication.timeSinceStartup + 15;
            while (window.Repaints < count + 3 && EditorApplication.timeSinceStartup < deadline)
            { window.Repaint(); yield return null; }
            Assert.That(window.Repaints, Is.GreaterThanOrEqualTo(count + 3));
        }
    }
}
