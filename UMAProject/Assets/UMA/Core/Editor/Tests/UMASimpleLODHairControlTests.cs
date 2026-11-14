using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UMA.Examples;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// UMASimpleLOD's Disable Hair Control at LOD: the Hair Control components on the character and below it are
    /// switched with the LOD zone, and left alone at -1.
    /// </summary>
    public sealed class UMASimpleLODHairControlTests
    {
        private static readonly MethodInfo ApplyZone =
            typeof(UMASimpleLOD).GetMethod("ApplyLODZoneChange", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo CharacterUpdated =
            typeof(UMASimpleLOD).GetMethod("CharacterUpdated", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in objects)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            objects.Clear();
        }

        private UMASimpleLOD Character(out UMAHairControl onRoot, out UMAHairControl onChild)
        {
            var root = new GameObject("LODHairControlTest") { hideFlags = HideFlags.HideAndDontSave };
            objects.Add(root);
            var child = new GameObject("Child") { hideFlags = HideFlags.HideAndDontSave };
            child.transform.SetParent(root.transform, false);
            onRoot = root.AddComponent<UMAHairControl>();
            onChild = child.AddComponent<UMAHairControl>();
            return root.AddComponent<UMASimpleLOD>();
        }

        private static void Zone(UMASimpleLOD lod, int level)
        {
            Assert.That(ApplyZone, Is.Not.Null, "UMASimpleLOD.ApplyLODZoneChange was renamed.");
            ApplyZone.Invoke(lod, new object[] { level });
        }

        [Test]
        public void HairControlIsDisabledAtAndBeyondTheLevelAndEnabledBelowIt()
        {
            UMASimpleLOD lod = Character(out UMAHairControl onRoot, out UMAHairControl onChild);
            lod.disableHairControlAtLOD = 2;

            Zone(lod, 1);
            Assert.That(onRoot.enabled && onChild.enabled, Is.True, "LOD 1 is below the level.");

            Zone(lod, 2);
            Assert.That(onRoot.enabled, Is.False, "The level itself disables it.");
            Assert.That(onChild.enabled, Is.False, "A Hair Control below the character is reached too.");

            Zone(lod, 4);
            Assert.That(onRoot.enabled || onChild.enabled, Is.False);

            Zone(lod, 0);
            Assert.That(onRoot.enabled && onChild.enabled, Is.True, "Coming closer enables it again.");
        }

        [Test]
        public void MinusOneLeavesHairControlAlone()
        {
            UMASimpleLOD lod = Character(out UMAHairControl onRoot, out UMAHairControl onChild);
            lod.disableHairControlAtLOD = -1;
            onChild.enabled = false;

            Zone(lod, 5);
            Assert.That(onRoot.enabled, Is.True);
            Zone(lod, 0);
            Assert.That(onChild.enabled, Is.False, "A Hair Control switched off by hand stays off.");
        }

        [Test]
        public void ARebuildAppliesTheCurrentZoneAgain()
        {
            UMASimpleLOD lod = Character(out UMAHairControl onRoot, out _);
            lod.disableHairControlAtLOD = 1;
            Zone(lod, 3);
            Assert.That(onRoot.enabled, Is.False);

            // Something else switches it back on, then the character is rebuilt.
            onRoot.enabled = true;
            Assert.That(CharacterUpdated, Is.Not.Null, "UMASimpleLOD.CharacterUpdated was renamed.");
            CharacterUpdated.Invoke(lod, new object[] { null });
            lod.LateUpdate();

            Assert.That(onRoot.enabled, Is.False);
        }
    }
}
