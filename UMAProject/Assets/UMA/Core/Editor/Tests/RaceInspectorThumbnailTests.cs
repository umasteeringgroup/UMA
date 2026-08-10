using NUnit.Framework;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

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

        private static void SetWardrobeThumb(
            SerializedProperty element,
            string wardrobeSlots,
            Sprite sprite)
        {
            element.FindPropertyRelative("thumbIsFor").stringValue = wardrobeSlots;
            element.FindPropertyRelative("thumb").objectReferenceValue = sprite;
        }
    }
}
