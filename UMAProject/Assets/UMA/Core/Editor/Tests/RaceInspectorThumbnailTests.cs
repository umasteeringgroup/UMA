using System;
using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public class RaceInspectorThumbnailTests
    {
        [Test]
        public void ClearRaceThumbnailsReplacesCompleteContainer()
        {
            RaceData race = ScriptableObject.CreateInstance<RaceData>();
            Texture2D texture = new Texture2D(2, 2);
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f));

            try
            {
                race.raceThumbnails = new RaceData.RaceThumbnails
                {
                    fullThumb = sprite,
                    faceThumb = sprite
                };
                RaceData.RaceThumbnails originalContainer = race.raceThumbnails;

                SerializedObject serializedRace = new SerializedObject(race);
                SerializedProperty wardrobeThumbs = serializedRace
                    .FindProperty("raceThumbnails")
                    .FindPropertyRelative("wardrobeSlotThumbs");
                wardrobeThumbs.arraySize = 2;
                SetWardrobeThumb(wardrobeThumbs.GetArrayElementAtIndex(0), "Hair", sprite);
                SetWardrobeThumb(wardrobeThumbs.GetArrayElementAtIndex(1), "Face,Complexion", sprite);
                serializedRace.ApplyModifiedPropertiesWithoutUndo();

                Assert.IsTrue(RaceInspector.ClearRaceThumbnails(race, false));

                Assert.AreNotSame(originalContainer, race.raceThumbnails);
                Assert.IsNotNull(race.raceThumbnails);
                Assert.IsNull(race.raceThumbnails.fullThumb);
                Assert.IsNull(race.raceThumbnails.faceThumb);

                serializedRace.Update();
                wardrobeThumbs = serializedRace
                    .FindProperty("raceThumbnails")
                    .FindPropertyRelative("wardrobeSlotThumbs");
                Assert.AreEqual(0, wardrobeThumbs.arraySize);
            }
            finally
            {
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(race);
            }
        }

        [Test]
        public void ClearRaceThumbnailsRejectsNullRace()
        {
            Assert.IsFalse(RaceInspector.ClearRaceThumbnails(null, false));
        }

        [Test]
        public void RaceCommitPersistsManualRendererBoundsImmediately()
        {
            string folderName = "__UMARaceInspectorSaveTests_" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + folderName;
            string path = folder + "/BoundsRace.asset";
            AssetDatabase.CreateFolder("Assets", folderName);
            RaceData race = ScriptableObject.CreateInstance<RaceData>();
            RaceInspectorSaveTestEditor inspector = null;
            try
            {
                race.name = "BoundsRace";
                AssetDatabase.CreateAsset(race, path);
                AssetDatabase.SaveAssetIfDirty(race);
                inspector = Editor.CreateEditor(race, typeof(RaceInspectorSaveTestEditor)) as
                    RaceInspectorSaveTestEditor;
                Assert.IsNotNull(inspector);

                race.useManualRendererBounds = true;
                race.manualRendererBounds = new Vector3(1.25f, 2.5f, 0.75f);
                race.manualRendererBoundsCenter = new Vector3(0.1f, 0.2f, -0.3f);
                EditorUtility.SetDirty(race);

                inspector.CommitForTest();
                Assert.IsFalse(EditorUtility.IsDirty(race));

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                RaceData reloaded = AssetDatabase.LoadAssetAtPath<RaceData>(path);
                Assert.IsTrue(reloaded.useManualRendererBounds);
                Assert.AreEqual(new Vector3(1.25f, 2.5f, 0.75f), reloaded.manualRendererBounds);
                Assert.AreEqual(new Vector3(0.1f, 0.2f, -0.3f), reloaded.manualRendererBoundsCenter);
            }
            finally
            {
                if (inspector != null) UnityEngine.Object.DestroyImmediate(inspector);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        [Test]
        public void ClosingRaceInspectorFlushesPendingBoundsSave()
        {
            string folderName = "__UMARaceInspectorCloseTests_" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + folderName;
            string path = folder + "/PendingBoundsRace.asset";
            AssetDatabase.CreateFolder("Assets", folderName);
            RaceData race = ScriptableObject.CreateInstance<RaceData>();
            RaceInspectorSaveTestEditor inspector = null;
            try
            {
                race.name = "PendingBoundsRace";
                AssetDatabase.CreateAsset(race, path);
                AssetDatabase.SaveAssetIfDirty(race);
                inspector = Editor.CreateEditor(race, typeof(RaceInspectorSaveTestEditor)) as
                    RaceInspectorSaveTestEditor;
                Assert.IsNotNull(inspector);

                race.useManualRendererBounds = true;
                race.manualRendererBounds = new Vector3(3f, 4f, 5f);
                race.manualRendererBoundsCenter = new Vector3(-0.5f, 0.25f, 0.75f);
                EditorUtility.SetDirty(race);
                FieldInfo pendingSave = typeof(RaceInspector).GetField("doSave",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(pendingSave);
                pendingSave.SetValue(inspector, true);

                UnityEngine.Object.DestroyImmediate(inspector);
                inspector = null;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                RaceData reloaded = AssetDatabase.LoadAssetAtPath<RaceData>(path);
                Assert.IsTrue(reloaded.useManualRendererBounds);
                Assert.AreEqual(new Vector3(3f, 4f, 5f), reloaded.manualRendererBounds);
                Assert.AreEqual(new Vector3(-0.5f, 0.25f, 0.75f), reloaded.manualRendererBoundsCenter);
            }
            finally
            {
                if (inspector != null) UnityEngine.Object.DestroyImmediate(inspector);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static void SetWardrobeThumb(
            SerializedProperty element,
            string wardrobeSlots,
            Sprite sprite)
        {
            element.FindPropertyRelative("thumbIsFor").stringValue = wardrobeSlots;
            element.FindPropertyRelative("thumb").objectReferenceValue = sprite;
        }
    }

    public sealed class RaceInspectorSaveTestEditor : RaceInspector
    {
        public void CommitForTest()
        {
            DoUpdate();
        }
    }
}
