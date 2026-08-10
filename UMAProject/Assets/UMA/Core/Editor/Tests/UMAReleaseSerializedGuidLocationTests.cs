using NUnit.Framework;

namespace UMA.Editors.Tests
{
    public class UMAReleaseSerializedGuidLocationTests
    {
        private const string MissingGuid = "9e2e0dcce00983248a5c1b8107cb1b1d";

        [Test]
        public void FindsStaleFieldAndLineForWrappedObjectReference()
        {
            string yaml =
                "%YAML 1.1\n" +
                "--- !u!114 &11400000\n" +
                "MonoBehaviour:\n" +
                "  _dnaConverterLegacy: {fileID: 11400000, guid: " + MissingGuid + ",\n" +
                "    type: 2}\n";

            string property = UMAReleaseTests.FindSerializedGuidPropertyPath(
                yaml, yaml.IndexOf(MissingGuid, System.StringComparison.Ordinal), out int line);

            Assert.AreEqual("_dnaConverterLegacy", property);
            Assert.AreEqual(4, line);
        }

        [Test]
        public void FindsOwningListForBareArrayReference()
        {
            string yaml =
                "%YAML 1.1\n" +
                "MonoBehaviour:\n" +
                "  tweenPoses:\n" +
                "  - {fileID: 11400000, guid: " + MissingGuid + ", type: 2}\n";

            string property = UMAReleaseTests.FindSerializedGuidPropertyPath(
                yaml, yaml.IndexOf(MissingGuid, System.StringComparison.Ordinal), out int line);

            Assert.AreEqual("tweenPoses[]", property);
            Assert.AreEqual(4, line);
        }

        [Test]
        public void FindsNestedSavedMaterialProperty()
        {
            string yaml =
                "%YAML 1.1\n" +
                "Material:\n" +
                "  m_SavedProperties:\n" +
                "    m_TexEnvs:\n" +
                "    - _MetallicGlossMap:\n" +
                "        m_Texture: {fileID: 2800000, guid: " + MissingGuid + ", type: 3}\n";

            string property = UMAReleaseTests.FindSerializedGuidPropertyPath(
                yaml, yaml.IndexOf(MissingGuid, System.StringComparison.Ordinal), out int line);

            Assert.AreEqual(
                "m_SavedProperties.m_TexEnvs._MetallicGlossMap.m_Texture", property);
            Assert.AreEqual(6, line);
        }
    }
}
