using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace UMA.Editors.Tests
{
    [TestFixture]
    [Category("UMA")]
    [Category("Build")]
    public sealed class UMABuildSampleTests
    {
        [Test]
        public void MainUmaBuildMenuContainsBothSamples()
        {
            Assert.That(MenuPath(nameof(
                    UMAAddressablesBuildWindow.OpenPlayerBuildWindow)),
                Is.EqualTo("UMA/Build/Non-Addressables Build Sample"));
            Assert.That(MenuPath(nameof(
                    UMAAddressablesBuildWindow.OpenAddressablesBuildWindow)),
                Is.EqualTo("UMA/Build/Addressables Build Sample"));
        }

        [Test]
        public void AddressablesMenuAvailabilityMatchesOptionalSupport()
        {
#if UMA_ADDRESSABLES
            Assert.That(
                UMAAddressablesBuildWindow.ValidateOpenAddressablesBuildWindow(),
                Is.EqualTo(UMAAddressablesBuildSample.IsAvailable));
#else
            Assert.That(UMAAddressablesBuildSample.IsAvailable, Is.False);
            Assert.That(
                UMAAddressablesBuildWindow.ValidateOpenAddressablesBuildWindow(),
                Is.False);
#endif
        }

        private static string MenuPath(string methodName)
        {
            MethodInfo method = typeof(UMAAddressablesBuildWindow).GetMethod(
                methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(method, Is.Not.Null, methodName);
            MenuItem attribute = method.GetCustomAttribute<MenuItem>();
            Assert.That(attribute, Is.Not.Null, methodName);
            return attribute.menuItem;
        }
    }
}
