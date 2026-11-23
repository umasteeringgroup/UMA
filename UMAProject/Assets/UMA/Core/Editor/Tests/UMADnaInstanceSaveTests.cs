using NUnit.Framework;
using UnityEngine;

namespace UMA.Tests
{
    /// <summary>
    /// Saved DNA holds names, values and enabled flags only. A DNA group must not be saved: JsonUtility
    /// writes an object reference as an instance ID, which only means something in the session that
    /// wrote it.
    /// </summary>
    public class UMADnaInstanceSaveTests
    {
        private DNAGroup group;

        [SetUp]
        public void SetUp()
        {
            group = ScriptableObject.CreateInstance<DNAGroup>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(group);
        }

        private static DNAInstanceCollection Collection(DNAGroup parent)
        {
            var collection = new DNAInstanceCollection();
            collection.dnaInstances.Add(new DNAInstance("height", 0.4f, parent));
            collection.dnaInstances.Add(new DNAInstance("weight", 0.9f, parent) { enabled = false });
            return collection;
        }

        [Test]
        public void SaveIsTheSameWithOrWithoutAGroup()
        {
            string withGroup = UMADnaInstance.SaveInstance(new UMADnaInstance(Collection(group)));
            string withoutGroup = UMADnaInstance.SaveInstance(new UMADnaInstance(Collection(null)));

            Assert.AreEqual(withoutGroup, withGroup,
                "A group reference must not reach the saved text, or the same character saves differently in every session.");
        }

        [Test]
        public void SaveDoesNotChangeTheLiveInstances()
        {
            DNAInstanceCollection collection = Collection(group);

            UMADnaInstance.SaveInstance(new UMADnaInstance(collection));

            Assert.AreSame(group, collection.dnaInstances[0].parentGroup);
            Assert.AreSame(group, collection.dnaInstances[1].parentGroup);
        }

        [Test]
        public void ValuesAndEnabledFlagsSurviveSaveAndLoad()
        {
            string saved = UMADnaInstance.SaveInstance(new UMADnaInstance(Collection(group)));

            DNAInstanceCollection loaded = UMADnaInstance.LoadInstance(saved).DNAInstances;

            Assert.AreEqual(2, loaded.dnaInstances.Count);
            Assert.AreEqual("height", loaded.dnaInstances[0].Name);
            Assert.AreEqual(0.4f, loaded.dnaInstances[0].Value, 0.00001f);
            Assert.IsTrue(loaded.dnaInstances[0].enabled);
            Assert.AreEqual("weight", loaded.dnaInstances[1].Name);
            Assert.AreEqual(0.9f, loaded.dnaInstances[1].Value, 0.00001f);
            Assert.IsFalse(loaded.dnaInstances[1].enabled);
        }

        [Test]
        public void GroupInTextFromAnOlderVersionIsDroppedOnLoad()
        {
            // What older versions wrote: the collection itself, group reference included.
            string oldText = JsonUtility.ToJson(Collection(group));
            StringAssert.Contains("instanceID", oldText);

            DNAInstanceCollection loaded = UMADnaInstance.LoadInstance(oldText).DNAInstances;

            Assert.AreEqual(2, loaded.dnaInstances.Count);
            foreach (DNAInstance instance in loaded.dnaInstances)
            {
                Assert.IsTrue(ReferenceEquals(instance.parentGroup, null),
                    "A group read from an instance ID must not be trusted; groups are found again by DNA name.");
            }
            Assert.AreEqual(0.4f, loaded.dnaInstances[0].Value, 0.00001f);
        }

        [Test]
        public void EmptyAndMissingCollectionsSaveWithoutThrowing()
        {
            Assert.DoesNotThrow(() => UMADnaInstance.SaveInstance(new UMADnaInstance(new DNAInstanceCollection())));
            Assert.DoesNotThrow(() => UMADnaInstance.SaveInstance(new UMADnaInstance(null)));
        }
    }
}
