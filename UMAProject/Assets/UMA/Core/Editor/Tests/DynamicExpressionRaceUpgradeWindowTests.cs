#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UMA;
using UMA.PoseTools;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class DynamicExpressionRaceUpgradeWindowTests
    {
        private readonly List<UnityEngine.Object> _objects =
            new List<UnityEngine.Object>();
        private readonly List<string> _folders = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _objects.Count; i++)
                if (_objects[i] != null)
                    UnityEngine.Object.DestroyImmediate(_objects[i]);
            for (int i = 0; i < _folders.Count; i++)
                if (AssetDatabase.IsValidFolder(_folders[i]))
                    AssetDatabase.DeleteAsset(_folders[i]);
            AssetDatabase.Refresh();
        }

        [Test]
        public void ExistingGroupIsAssignedAndLegacySetIsRemoved()
        {
            RaceData first = Track(
                ScriptableObject.CreateInstance<RaceData>());
            RaceData second = Track(
                ScriptableObject.CreateInstance<RaceData>());
            UMAExpressionSet firstLegacy = Track(
                ScriptableObject.CreateInstance<UMAExpressionSet>());
            UMAExpressionSet secondLegacy = Track(
                ScriptableObject.CreateInstance<UMAExpressionSet>());
            UMAExpressionGroup group = NewValidGroup();
            first.expressionSet = firstLegacy;
            second.expressionSet = secondLegacy;

            Dictionary<RaceData, UMAExpressionGroup> result =
                DynamicExpressionRaceUpgradeUtility.UpdateRaces(
                    new[] { first, second }, group, false);

            Assert.AreEqual(2, result.Count);
            Assert.AreSame(group, first.expressionGroup);
            Assert.AreSame(group, second.expressionGroup);
            Assert.IsNull(first.expressionSet);
            Assert.IsNull(second.expressionSet);
        }

        [Test]
        public void CurrentSetConversionIsSavedBesideSourceAndAssigned()
        {
            string folder = "Assets/__UMAExpressionUpgradeTests_" +
                Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(
                "Assets", folder.Substring("Assets/".Length));
            _folders.Add(folder);

            UMAExpressionSet legacy =
                ScriptableObject.CreateInstance<UMAExpressionSet>();
            legacy.name = "LegacyExpressions";
            AssetDatabase.CreateAsset(
                legacy, folder + "/LegacyExpressions.asset");
            RaceData race = Track(
                ScriptableObject.CreateInstance<RaceData>());
            race.name = "UpgradeRace";
            race.expressionSet = legacy;

            Dictionary<RaceData, UMAExpressionGroup> result =
                DynamicExpressionRaceUpgradeUtility.UpdateRaces(
                    new[] { race }, null, true);

            Assert.AreEqual(1, result.Count);
            Assert.IsNull(race.expressionSet);
            Assert.IsNotNull(race.expressionGroup);
            Assert.AreEqual(ExpressionPlayer.PoseCount,
                race.expressionGroup.Count);
            string groupPath =
                AssetDatabase.GetAssetPath(race.expressionGroup);
            Assert.AreEqual(folder,
                System.IO.Path.GetDirectoryName(groupPath)
                    ?.Replace('\\', '/'));
            Assert.AreSame(race.expressionGroup, result[race]);
        }

        [Test]
        public void CreateModeRequiresEveryRaceToHaveSavedLegacySet()
        {
            RaceData race = Track(
                ScriptableObject.CreateInstance<RaceData>());
            string error =
                DynamicExpressionRaceUpgradeUtility.GetValidationError(
                    new[] { race }, null, true);

            StringAssert.Contains(
                "has no current Expression Set", error);
        }

        private UMAExpressionGroup NewValidGroup()
        {
            DNA dna = Track(ScriptableObject.CreateInstance<DNA>());
            dna.name = "SmileDNA";
            dna.displayName = "Smile";
            UMAExpressionGroup group =
                Track(ScriptableObject.CreateInstance<UMAExpressionGroup>());
            group.expressions.Add(new UMAExpressionDefinition
            {
                id = "smile",
                displayName = "Smile",
                dna = dna
            });
            return group;
        }

        private T Track<T>(T item) where T : UnityEngine.Object
        {
            _objects.Add(item);
            return item;
        }
    }
}
#endif
