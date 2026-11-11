#if UNITY_EDITOR

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace UMA.Editors.Tests
{
    public sealed class UMAHairVolumeCenterTests
    {
        private const string HairShaderSource =
            "Shader \"Hidden/UMA/Tests/HairVolumeCenter\" { Properties { _HairCenterWS (\"Center\", Vector) = (0,0,0,0) " +
            "_Other (\"Other\", Float) = 0 } SubShader { Pass { } } }";
        private const string PlainShaderSource =
            "Shader \"Hidden/UMA/Tests/HairVolumeCenterPlain\" { Properties { _Other (\"Other\", Float) = 0 } SubShader { Pass { } } }";

        private readonly List<Object> created = new List<Object>();
        private Shader hairShader, plainShader;
        private Material hairMaterial, plainMaterial;
        private MaterialPropertyBlock block;

        // A character whose mesh space is Z-up while its root bone is Y-up, as on a UMA.
        private sealed class Rig
        {
            public GameObject avatar;
            public Transform root, head;
            public SkinnedMeshRenderer renderer;
        }

        [SetUp]
        public void SetUp()
        {
            hairShader = Track(ShaderUtil.CreateShaderAsset(HairShaderSource, false));
            plainShader = Track(ShaderUtil.CreateShaderAsset(PlainShaderSource, false));
            hairMaterial = Track(new Material(hairShader));
            plainMaterial = Track(new Material(plainShader));
            block = new MaterialPropertyBlock();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] != null)
                {
                    Object.DestroyImmediate(created[i]);
                }
            }
            created.Clear();
        }

        private T Track<T>(T value) where T : Object
        {
            created.Add(value);
            return value;
        }

        private Rig CreateRig(Vector3 position, string headName = "Head", Material hair = null)
        {
            var rig = new Rig();
            rig.avatar = Track(new GameObject("TestAvatar"));
            rig.avatar.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 40f, 0f));

            var rendererObject = new GameObject("Renderer");
            rendererObject.transform.SetParent(rig.avatar.transform, false);

            rig.root = new GameObject("Global").transform;
            rig.root.SetParent(rig.avatar.transform, false);
            rig.root.localRotation = Quaternion.Euler(90f, 0f, 0f);

            rig.head = new GameObject(headName).transform;
            rig.head.SetParent(rig.root, false);
            rig.head.localPosition = new Vector3(0f, 1.7f, 0.05f);
            rig.head.localRotation = Quaternion.Euler(10f, 200f, 30f);

            var mesh = Track(new Mesh());
            mesh.vertices = new[] { new Vector3(0f, 0f, 1.8f), new Vector3(0.1f, 0f, 1.8f), new Vector3(0f, 0.1f, 1.8f) };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 0, 2, 1 }, 1);
            var weights = new BoneWeight[3];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i].boneIndex0 = 1;
                weights[i].weight0 = 1f;
            }
            mesh.boneWeights = weights;
            Matrix4x4 meshToWorld = rendererObject.transform.localToWorldMatrix;
            mesh.bindposes = new[] { rig.root.worldToLocalMatrix * meshToWorld, rig.head.worldToLocalMatrix * meshToWorld };

            rig.renderer = rendererObject.AddComponent<SkinnedMeshRenderer>();
            rig.renderer.sharedMesh = mesh;
            rig.renderer.bones = new[] { rig.root, rig.head };
            rig.renderer.rootBone = rig.root;
            rig.renderer.sharedMaterials = new[] { plainMaterial, hair != null ? hair : hairMaterial };
            return rig;
        }

        private Vector4 CenterOn(Renderer renderer, int materialIndex)
        {
            renderer.GetPropertyBlock(block, materialIndex);
            return block.GetVector(UMAHairVolumeCenter.HairCenterProperty);
        }

        private bool BlockIsEmpty(Renderer renderer, int materialIndex)
        {
            renderer.GetPropertyBlock(block, materialIndex);
            return block.isEmpty;
        }

        private static void AssertPoint(Vector3 expected, Vector4 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f), "expected " + expected.ToString("F4") + " but was " + ((Vector3)actual).ToString("F4"));
            Assert.That(actual.w, Is.EqualTo(1f), "w marks the center as valid");
        }

        [Test]
        public void WritesCenterAboveHeadBoneOnlyToMaterialsWithTheProperty()
        {
            Rig rig = CreateRig(new Vector3(3f, 1f, -2f));
            var center = rig.avatar.AddComponent<UMAHairVolumeCenter>();
            center.centerOffset = new Vector3(0.01f, 0.09f, -0.02f);
            center.UpdateCenter();

            // The offset is in the root bone's space, not in the Z-up mesh space.
            Vector3 expected = rig.head.position + rig.root.TransformVector(center.centerOffset);
            AssertPoint(expected, CenterOn(rig.renderer, 1));
            Assert.That(Vector3.Distance(expected, rig.head.position + rig.renderer.transform.TransformVector(center.centerOffset)), Is.GreaterThan(0.05f),
                "the rig must tell root-bone space and mesh space apart for this test to mean anything");
            Assert.IsTrue(BlockIsEmpty(rig.renderer, 0), "a material without _HairCenterWS is left alone");
            Assert.AreSame(rig.head, center.HeadBone);
            Assert.IsTrue(center.TryGetCenter(out Vector3 reported));
            Assert.That(Vector3.Distance(expected, reported), Is.LessThan(1e-4f));
        }

        [Test]
        public void CenterFollowsHeadPoseAndDoesNotDependOnThePoseItWasSetUpIn()
        {
            Rig rig = CreateRig(Vector3.zero);
            Vector3 offset = new Vector3(0f, 0.09f, 0f);
            Vector3 headLocal = rig.head.InverseTransformVector(rig.root.TransformVector(offset)); // rest pose

            // Pose the head before the component ever sees the character.
            rig.head.localRotation = Quaternion.Euler(-35f, 120f, 75f);
            rig.head.localPosition = new Vector3(0.2f, 1.5f, -0.1f);
            var center = rig.avatar.AddComponent<UMAHairVolumeCenter>();
            center.centerOffset = offset;
            center.UpdateCenter();
            AssertPoint(rig.head.TransformPoint(headLocal), CenterOn(rig.renderer, 1));

            rig.head.localRotation = Quaternion.Euler(60f, -10f, 5f);
            rig.head.localScale = new Vector3(1.2f, 1.2f, 1.2f);
            rig.avatar.transform.position = new Vector3(-4f, 2f, 9f);
            center.UpdateCenter();
            AssertPoint(rig.head.TransformPoint(headLocal), CenterOn(rig.renderer, 1));
        }

        [Test]
        public void CharactersSharingOneMaterialEachGetTheirOwnCenter()
        {
            Rig first = CreateRig(new Vector3(0f, 0f, 0f));
            Rig second = CreateRig(new Vector3(5f, 0f, 0f));
            Assert.AreSame(first.renderer.sharedMaterials[1], second.renderer.sharedMaterials[1]);
            var a = first.avatar.AddComponent<UMAHairVolumeCenter>();
            var b = second.avatar.AddComponent<UMAHairVolumeCenter>();
            a.UpdateCenter();
            b.UpdateCenter();

            AssertPoint(first.head.position + first.root.TransformVector(a.centerOffset), CenterOn(first.renderer, 1));
            AssertPoint(second.head.position + second.root.TransformVector(b.centerOffset), CenterOn(second.renderer, 1));
            Assert.AreEqual(Vector4.zero, hairMaterial.GetVector(UMAHairVolumeCenter.HairCenterProperty), "the shared material itself is not written");
        }

        [Test]
        public void DisablingRemovesTheCenterAndKeepsValuesOtherCodePutOnTheRenderer()
        {
            Rig clean = CreateRig(Vector3.zero);
            Rig shared = CreateRig(new Vector3(2f, 0f, 0f));
            int other = Shader.PropertyToID("_Other");
            block.Clear();
            block.SetFloat(other, 0.75f);
            shared.renderer.SetPropertyBlock(block, 1);

            var a = clean.avatar.AddComponent<UMAHairVolumeCenter>();
            var b = shared.avatar.AddComponent<UMAHairVolumeCenter>();
            a.UpdateCenter();
            b.UpdateCenter();
            shared.renderer.GetPropertyBlock(block, 1);
            Assert.AreEqual(0.75f, block.GetFloat(other), "another component's value survives the write");
            Assert.AreEqual(1f, block.GetVector(UMAHairVolumeCenter.HairCenterProperty).w);

            a.enabled = false;
            b.enabled = false;
            Assert.IsTrue(BlockIsEmpty(clean.renderer, 1), "a block that was empty before is removed again");
            shared.renderer.GetPropertyBlock(block, 1);
            Assert.AreEqual(0.75f, block.GetFloat(other));
            Assert.AreEqual(0f, block.GetVector(UMAHairVolumeCenter.HairCenterProperty).w, "w = 0 sends the shader back to the renderer bounds");
        }

        [Test]
        public void MissingHeadBoneWritesNothingAndWarnsOnce()
        {
            Rig rig = CreateRig(Vector3.zero, "Skull");
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no bone named 'Head'"));
            var center = rig.avatar.AddComponent<UMAHairVolumeCenter>();
            center.UpdateCenter();
            center.UpdateCenter();
            Assert.IsTrue(BlockIsEmpty(rig.renderer, 1));
            Assert.IsFalse(center.TryGetCenter(out _));

            center.headBoneName = "Skull";
            center.UpdateCenter();
            AssertPoint(rig.head.position + rig.root.TransformVector(center.centerOffset), CenterOn(rig.renderer, 1));
        }

        [Test]
        public void MaterialsReplacedByARebuildArePickedUp()
        {
            Rig rig = CreateRig(Vector3.zero);
            var center = rig.avatar.AddComponent<UMAHairVolumeCenter>();
            center.UpdateCenter();

            // A rebuild that moves the hair to the other slot, with a new material instance.
            var rebuilt = Track(new Material(hairShader));
            rig.renderer.sharedMaterials = new[] { rebuilt, plainMaterial };
            center.UpdateCenter();

            AssertPoint(rig.head.position + rig.root.TransformVector(center.centerOffset), CenterOn(rig.renderer, 0));
            Assert.IsTrue(BlockIsEmpty(rig.renderer, 1), "the slot that no longer holds hair is cleared");
        }
    }
}

#endif
