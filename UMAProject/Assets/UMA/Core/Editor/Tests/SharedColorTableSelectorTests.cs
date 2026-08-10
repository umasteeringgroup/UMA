using System.Reflection;
using NUnit.Framework;
using UMA.Editors;
using UnityEngine;

namespace UMA.Tests
{
    public class SharedColorTableSelectorTests
    {
        private static readonly MethodInfo CompatibilityMethod =
            typeof(OverlayColorDataPropertyDrawer).GetMethod("IsSharedColorTableCompatible",
                BindingFlags.Static | BindingFlags.NonPublic);

        [TestCase("Skin", "Skin", true)]
        [TestCase("Skin", "Hair", false)]
        [TestCase("Skin", "skin", false)]
        [TestCase("", "Skin", true)]
        [TestCase("   ", "Skin", true)]
        public void TableCompatibilityUsesSharedColorNameAndAllowsGenericTables(
            string tableSharedColorName, string sharedColorName, bool expected)
        {
            Assert.That(CompatibilityMethod, Is.Not.Null);
            SharedColorTable table = ScriptableObject.CreateInstance<SharedColorTable>();
            try
            {
                table.sharedColorName = tableSharedColorName;
                bool compatible = (bool)CompatibilityMethod.Invoke(null,
                    new object[] { table, sharedColorName });
                Assert.That(compatible, Is.EqualTo(expected));
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }
    }
}
