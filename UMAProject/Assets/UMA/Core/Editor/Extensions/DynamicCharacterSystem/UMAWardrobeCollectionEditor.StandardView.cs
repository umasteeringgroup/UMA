#if UNITY_EDITOR
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors
{
    public partial class UMAWardrobeCollectionEditor
    {
        private readonly UMAInspectorView collectionView =
            new UMAInspectorView(typeof(UMAWardrobeCollection));

        public override void OnInspectorGUI()
        {
            if (collectionView.DrawSelector())
            {
                base.OnInspectorGUI();
                return;
            }
            serializedObject.Update();
            using (collectionView.Section("Collection presentation",
                "Display Name is shown to artists and users. Collection Region is normally FullOutfit and identifies where the collection is equipped. Cover Images provide collection-level promotional artwork; recipe thumbnails remain available as a fallback."))
            {
                collectionView.Field(serializedObject, "DisplayValue",
                    "Display Name");
                collectionView.Field(serializedObject, "wardrobeSlot",
                    "Collection Region");
                collectionView.Field(serializedObject, "coverImages",
                    "Cover Images");
                collectionView.Field(serializedObject,
                    "wardrobeRecipeThumbs", "Fallback Thumbnails");
            }
            using (collectionView.Section("Compatible races",
                "Compatible Races identifies every race that can consume the collection. Each named race receives its own wardrobe-region set in Collection Contents. Inspect opens a resolved Race Data asset without changing the collection."))
                DrawNamedAssets(serializedObject.FindProperty(
                    "compatibleRaces"), true);
            using (collectionView.Section("Collection contents",
                "Wardrobe Collection stores the race-specific wardrobe-region assignments. Expand a race and assign the recipes for its regions. Arbitrary Recipes adds items that are not tied to a particular region or race, such as hairstyle or tattoo packs."))
            {
                collectionView.Field(serializedObject,
                    "wardrobeCollection", "Race Wardrobe Sets");
                EditorGUILayout.LabelField("Arbitrary Recipes",
                    EditorStyles.boldLabel);
                DrawNamedAssets(serializedObject.FindProperty(
                    "arbitraryRecipes"), false);
            }
            using (collectionView.Section("Validation",
                "Empty names and unresolved assets remain serialized for recovery but cannot be equipped. Advanced View contains the original race-aware collection editor and recipe save workflow."))
                DrawCollectionValidation();
            if (serializedObject.ApplyModifiedProperties())
                SynchronizeRaceSets();
        }

        private void DrawNamedAssets(SerializedProperty list, bool races)
        {
            if (list == null) return;
            for (int i = 0; i < list.arraySize; i++)
                using (new EditorGUILayout.HorizontalScope())
                {
                    SerializedProperty item = list.GetArrayElementAtIndex(i);
                    EditorGUILayout.PropertyField(item, GUIContent.none);
                    Object asset = null;
                    if (!string.IsNullOrWhiteSpace(item.stringValue) &&
                        UMAAssetIndexer.Instance != null)
                        asset = races
                            ? (Object)UMAAssetIndexer.Instance
                                .RawGetAsset<RaceData>(item.stringValue)
                            : UMAAssetIndexer.Instance
                                .RawGetAsset<UMATextRecipe>(item.stringValue);
                    using (new EditorGUI.DisabledScope(asset == null))
                        if (GUILayout.Button("Inspect", GUILayout.Width(58f)))
                            Selection.activeObject = asset;
                    if (GUILayout.Button("-", GUILayout.Width(24f)))
                    {
                        list.DeleteArrayElementAtIndex(i);
                        break;
                    }
                }
            if (GUILayout.Button("Add Entry"))
            {
                int index = list.arraySize++;
                list.GetArrayElementAtIndex(index).stringValue = string.Empty;
            }
        }

        private void DrawCollectionValidation()
        {
            SerializedProperty races = serializedObject.FindProperty(
                "compatibleRaces");
            SerializedProperty recipes = serializedObject.FindProperty(
                "arbitraryRecipes");
            int emptyRaces = CountEmpty(races);
            int emptyRecipes = CountEmpty(recipes);
            EditorGUILayout.LabelField("Compatible races",
                (races != null ? races.arraySize : 0).ToString());
            EditorGUILayout.LabelField("Arbitrary recipes",
                (recipes != null ? recipes.arraySize : 0).ToString());
            if (emptyRaces + emptyRecipes > 0)
                EditorGUILayout.HelpBox(
                    "Remove or fill " + (emptyRaces + emptyRecipes) +
                    " empty collection entries.", MessageType.Warning);
        }

        private void SynchronizeRaceSets()
        {
            UMAWardrobeCollection collection =
                target as UMAWardrobeCollection;
            if (collection == null || collection.wardrobeCollection == null)
                return;
            Undo.RecordObject(collection, "Synchronize wardrobe collection races");
            for (int i = 0; i < collection.compatibleRaces.Count; i++)
            {
                string race = collection.compatibleRaces[i];
                if (!string.IsNullOrWhiteSpace(race) &&
                    !collection.wardrobeCollection.Contains(race))
                    collection.wardrobeCollection.Add(race);
            }
            var existing = collection.wardrobeCollection
                .GetAllRacesInCollection();
            for (int i = 0; i < existing.Count; i++)
                if (!collection.compatibleRaces.Contains(existing[i]))
                    collection.wardrobeCollection.Remove(existing[i]);
            EditorUtility.SetDirty(collection);
        }

        private static int CountEmpty(SerializedProperty list)
        {
            if (list == null) return 0;
            int count = 0;
            for (int i = 0; i < list.arraySize; i++)
                if (string.IsNullOrWhiteSpace(
                    list.GetArrayElementAtIndex(i).stringValue)) count++;
            return count;
        }
    }
}
#endif
