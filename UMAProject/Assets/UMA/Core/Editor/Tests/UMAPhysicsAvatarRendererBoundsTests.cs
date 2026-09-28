#if UNITY_EDITOR

using System.Reflection;
using NUnit.Framework;
using UMA.Dynamics;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAPhysicsAvatarRendererBoundsTests
    {
        [Test]
        [Category("UMA")]
        [Category("RendererLifecycle")]
        [Category("Ragdoll")]
        public void RagdollCentersCubifiedBoundsOnHipAndRestoresGeneratedBounds()
        {
            GameObject avatarObject = null;
            Mesh mesh = null;
            try
            {
                avatarObject = new GameObject("Ragdoll bounds fixture");
                UMAData data = avatarObject.AddComponent<UMAData>();
                UMAPhysicsAvatar physics =
                    avatarObject.AddComponent<UMAPhysicsAvatar>();

                GameObject rendererObject = new GameObject("UMARenderer");
                rendererObject.transform.SetParent(
                    avatarObject.transform,
                    false);
                GameObject boneObject = new GameObject("Hip");
                boneObject.transform.SetParent(
                    avatarObject.transform,
                    false);
                SkinnedMeshRenderer renderer =
                    rendererObject.AddComponent<SkinnedMeshRenderer>();
                mesh = new Mesh
                {
                    name = "UMAMesh"
                };
                mesh.vertices = new[]
                {
                    new Vector3(-0.5f, 0f, 0f),
                    new Vector3(0.5f, 0f, 0f),
                    new Vector3(0f, 1f, 0f)
                };
                mesh.triangles = new[] { 0, 1, 2 };
                BoneWeight fullBoneWeight = new BoneWeight
                {
                    boneIndex0 = 0,
                    weight0 = 1f
                };
                mesh.boneWeights = new[]
                {
                    fullBoneWeight,
                    fullBoneWeight,
                    fullBoneWeight
                };
                mesh.bindposes = new[]
                {
                    boneObject.transform.worldToLocalMatrix *
                    renderer.transform.localToWorldMatrix
                };
                mesh.RecalculateBounds();
                renderer.sharedMesh = mesh;
                renderer.bones = new[] { boneObject.transform };
                renderer.rootBone = boneObject.transform;

                Bounds generatedBounds = new Bounds(
                    new Vector3(0.25f, 1.5f, -0.1f),
                    new Vector3(2f, 4f, 1.5f));
                mesh.bounds = generatedBounds;
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = generatedBounds;
                data.SetRenderers(new[] { renderer });

                FieldInfo dataField = typeof(UMAPhysicsAvatar).GetField(
                    "_umaData",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo rootBoneField = typeof(UMAPhysicsAvatar).GetField(
                    "_rootBone",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo captureState =
                    typeof(UMAPhysicsAvatar).GetMethod(
                        "SetRendereroffscreenStates",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo setRagdollRendererState =
                    typeof(UMAPhysicsAvatar).GetMethod(
                        "SetRagdollRendererState",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo updateRagdollBounds =
                    typeof(UMAPhysicsAvatar).GetMethod(
                        "UpdateRagdollRendererBounds",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo cubifyBounds =
                    typeof(UMAPhysicsAvatar).GetMethod(
                        "CubifyBounds",
                        BindingFlags.Static | BindingFlags.NonPublic);
                Assert.NotNull(dataField);
                Assert.NotNull(rootBoneField);
                Assert.NotNull(captureState);
                Assert.NotNull(setRagdollRendererState);
                Assert.NotNull(updateRagdollBounds);
                Assert.NotNull(cubifyBounds);

                Bounds knownCube = (Bounds)cubifyBounds.Invoke(
                    null,
                    new object[] { generatedBounds });
                AssertBoundsEqual(
                    new Bounds(
                        generatedBounds.center,
                        new Vector3(4f, 4f, 4f)),
                    knownCube,
                    "Cubifying must expand both smaller axes to the largest " +
                    "captured axis.");

                dataField.SetValue(physics, data);
                rootBoneField.SetValue(physics, boneObject);
                Bounds capturedBounds = renderer.localBounds;
                captureState.Invoke(physics, null);
                setRagdollRendererState.Invoke(
                    physics,
                    new object[] { true });

                Assert.IsFalse(
                    renderer.updateWhenOffscreen,
                    "Custom ragdoll bounds must not be overwritten by Unity's " +
                    "offscreen bounds calculation.");

                boneObject.transform.localPosition =
                    new Vector3(0f, -8f, 2f);
                updateRagdollBounds.Invoke(physics, null);

                float capturedCubeSize = Mathf.Max(
                    capturedBounds.size.x,
                    Mathf.Max(
                        capturedBounds.size.y,
                        capturedBounds.size.z));
                AssertBoundsEqual(
                    new Bounds(
                        new Vector3(0f, -8f, 2f),
                        Vector3.one * capturedCubeSize),
                    renderer.localBounds,
                    "Ragdoll bounds must be cubified once and centered on the " +
                    "hip in renderer-local space.");

                setRagdollRendererState.Invoke(
                    physics,
                    new object[] { false });

                Assert.IsTrue(
                    renderer.updateWhenOffscreen,
                    "Leaving ragdoll must restore the renderer's original " +
                    "offscreen update setting.");
                AssertBoundsEqual(
                    capturedBounds,
                    renderer.localBounds,
                    "Leaving ragdoll must restore UMA's generated/manual bounds.");
            }
            finally
            {
                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh);
                }
                if (avatarObject != null)
                {
                    Object.DestroyImmediate(avatarObject);
                }
            }
        }

        private static void AssertBoundsEqual(
            Bounds expected,
            Bounds actual,
            string message)
        {
            Assert.Less(
                (expected.center - actual.center).sqrMagnitude,
                0.0000000001f,
                message + " Expected center " + expected.center +
                ", actual center " + actual.center + ".");
            Assert.Less(
                (expected.size - actual.size).sqrMagnitude,
                0.0000000001f,
                message + " Expected size " + expected.size +
                ", actual size " + actual.size + ".");
        }

    }
}

#endif
