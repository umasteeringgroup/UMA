using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintStandalonePhase3Tests
    {
        private const string Folder = "Assets/UMA/Temp/TexturePaintPhase3Tests";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/UMA/Temp")) AssetDatabase.CreateFolder("Assets/UMA", "Temp");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/UMA/Temp", "TexturePaintPhase3Tests");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void UdimResolverUsesExactGroupAndSortsByTile()
        {
            string group = "phase3-" + Guid.NewGuid().ToString("N");
            SlotDataAsset tile1011 = CreateSlotAsset("Torso_1011", group, 1011);
            SlotDataAsset tile1001 = CreateSlotAsset("Torso_1001", group, 1001);
            CreateSlotAsset("Other_1001", group + "-other", 1001);

            Assert.That(TexturePaintUdimResolver.TryResolve(tile1011, out List<SlotDataAsset> result,
                out string error), Is.True, error);
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0], Is.SameAs(tile1001));
            Assert.That(result[1], Is.SameAs(tile1011));
        }

        [Test]
        public void UdimResolverRejectsDuplicateTilesAndConflictingSourceSubmesh()
        {
            string duplicateGroup = "duplicate-" + Guid.NewGuid().ToString("N");
            SlotDataAsset first = CreateSlotAsset("First", duplicateGroup, 1001);
            CreateSlotAsset("Duplicate", duplicateGroup, 1001);
            Assert.That(TexturePaintUdimResolver.TryResolve(first, out _, out string duplicateError), Is.False);
            Assert.That(duplicateError, Does.Contain("both"));

            string conflictGroup = "conflict-" + Guid.NewGuid().ToString("N");
            SlotDataAsset sourceZero = CreateSlotAsset("SourceZero", conflictGroup, 1001);
            SlotDataAsset sourceOne = CreateSlotAsset("SourceOne", conflictGroup, 1011);
            sourceOne.udimSourceSubmeshIndex = 1;
            EditorUtility.SetDirty(sourceOne);
            AssetDatabase.SaveAssetIfDirty(sourceOne);
            Assert.That(TexturePaintUdimResolver.TryResolve(sourceZero, out _, out string conflictError), Is.False);
            Assert.That(conflictError, Does.Contain("but this group uses source submesh 0"));
        }

        [Test]
        public void ReconstructSlotGroupNeedsNoAvatarAndBuildsOneLogicalTarget()
        {
            SlotDataAsset first = CreateSlot("Body1001", "body", 1001);
            SlotDataAsset second = CreateSlot("Body1002", "body", 1002);
            Quaternion canonicalRotation = Quaternion.Euler(-90f, 90f, 0f);
            ConfigureCanonicalTransform(first, canonicalRotation);
            ConfigureCanonicalTransform(second, canonicalRotation);
            UMAMaterial umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
            Material material = new Material(Shader.Find("Standard"));
            umaMaterial.material = material;
            TexturePaintLaunchContext context = new TexturePaintLaunchContext
            {
                kind = TexturePaintLaunchKind.StandaloneSlot,
                selectedSlot = first,
                umaMaterial = umaMaterial,
                udimGroupId = "body",
                standaloneMeshTransformVersion = 2,
                fixupRotations = false,
                slotRotationEuler = Vector3.zero,
                members = new List<TexturePaintStandaloneMemberContext>
                {
                    new TexturePaintStandaloneMemberContext { slot = second, tileNumber = 1002 },
                    new TexturePaintStandaloneMemberContext { slot = first, tileNumber = 1001 }
                }
            };

            MeshReconstructionResult reconstruction = MeshReconstructor.ReconstructSlotGroup(context);
            try
            {
                Assert.That(reconstruction.surfaces, Has.Count.EqualTo(2));
                Assert.That(reconstruction.logicalTargets.Targets, Has.Count.EqualTo(1));
                TexturePaintLogicalTarget target = reconstruction.logicalTargets.Targets[0];
                Assert.That(target.isUdim, Is.True);
                Assert.That(target.members, Has.Count.EqualTo(2));
                Assert.That(target.members[0].udimTileNumber, Is.EqualTo(1001));
                Assert.That(reconstruction.surfaces[0].gameObject.GetComponent<MeshFilter>(), Is.Not.Null);
                Assert.That(reconstruction.surfaces[0].collider, Is.Not.Null);
                Assert.That(reconstruction.surfaces[0].mesh.uv2, Has.Length.EqualTo(4));
                AssertVector(reconstruction.surfaces[0].mesh.vertices[0],
                    canonicalRotation * second.meshData.vertices[0]);
                AssertVector(reconstruction.surfaces[0].mesh.normals[0],
                    canonicalRotation * second.meshData.normals[0]);
                Vector3 expectedTangent = canonicalRotation *
                    new Vector3(second.meshData.tangents[0].x, second.meshData.tangents[0].y,
                        second.meshData.tangents[0].z);
                Vector4 actualTangent = reconstruction.surfaces[0].mesh.tangents[0];
                AssertVector(new Vector3(actualTangent.x, actualTangent.y, actualTangent.z), expectedTangent);
                Assert.That(actualTangent.w, Is.EqualTo(second.meshData.tangents[0].w));
                CollectionAssert.AreEqual(second.meshData.submeshes[0].GetBaseTriangles(),
                    reconstruction.surfaces[0].mesh.GetIndices(0));
            }
            finally
            {
                reconstruction.Dispose();
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(umaMaterial);
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void TransientDocumentClonesStandaloneGuidAndFingerprintContext()
        {
            TexturePaintLaunchContext context = new TexturePaintLaunchContext
            {
                kind = TexturePaintLaunchKind.StandaloneSlot,
                umaMaterialGuid = "material-guid",
                udimGroupId = "group",
                fixupRotations = true,
                slotRotationEuler = new Vector3(10f, 20f, 30f),
                members = new List<TexturePaintStandaloneMemberContext>
                {
                    new TexturePaintStandaloneMemberContext
                    {
                        slotGuid = "slot-guid", overlayGuid = "overlay-guid", sourceFingerprint = "fingerprint", tileNumber = 1001
                    }
                }
            };
            TexturePaintDocument document = TexturePaintDocumentStorage.CreateTransient(null, context);
            try
            {
                Assert.That(document.launchContext, Is.Not.SameAs(context));
                Assert.That(document.launchContext.members[0], Is.Not.SameAs(context.members[0]));
                Assert.That(document.launchContext.members[0].sourceFingerprint, Is.EqualTo("fingerprint"));
                Assert.That(document.launchContext.slotRotationEuler, Is.EqualTo(context.slotRotationEuler));
                Assert.That(TexturePaintRecoveryStore.GetContextKey(document.launchContext),
                    Is.EqualTo(TexturePaintRecoveryStore.GetContextKey(context)));
            }
            finally { UnityEngine.Object.DestroyImmediate(document); }
        }

        [Test]
        public void MaterialOnlyControllerCreatesRemovableDefaultWhiteFill()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null, "The required URP Lit shader is unavailable in this certification project.");
            SlotDataAsset slot = CreateSlot("StandaloneBody", string.Empty, 0);
            UMAMaterial umaMaterial = ScriptableObject.CreateInstance<UMAMaterial>();
            Material material = new Material(shader);
            umaMaterial.material = material;
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.DiffuseTexture,
                    materialPropertyName = "_BaseMap",
                    sourceTextureName = "_BaseMap"
                },
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.NormalMap,
                    materialPropertyName = "_BumpMap",
                    sourceTextureName = "_BumpMap"
                }
            };
            TexturePaintLaunchContext context = new TexturePaintLaunchContext
            {
                kind = TexturePaintLaunchKind.StandaloneSlot,
                sourceMode = TexturePaintStandaloneSourceMode.UMAMaterial,
                selectedSlot = slot,
                umaMaterial = umaMaterial,
                resolution = 128,
                members = new List<TexturePaintStandaloneMemberContext>
                {
                    new TexturePaintStandaloneMemberContext { slot = slot }
                }
            };
            TexturePaintStageController controller = new TexturePaintStageController();
            try
            {
                const string shaderRoot = "Assets/UMA/OverlayPainter/Shaders/";
                controller.InitializeStandalone(context,
                    AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderRoot + "StrokeRasterize.compute"),
                    AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderRoot + "Blur.compute"),
                    AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderRoot + "NormalTouchup.compute"),
                    AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderRoot + "LayerComposite.compute"),
                    AssetDatabase.LoadAssetAtPath<ComputeShader>(shaderRoot + "ChannelPack.compute"), 128,
                    AssetDatabase.LoadAssetAtPath<Shader>(shaderRoot + "FillLayer.shader"));
                TextureSet set = controller.Textures.Sets[0];
                Assert.That(set.layers, Has.Count.EqualTo(1));
                Assert.That(set.layers[0].name, Is.EqualTo("Default White"));
                Assert.That(set.layers[0].kind, Is.EqualTo(TexturePaintLayerKind.Fill));
                Assert.That(set.layers[0].fillColor, Is.EqualTo(Color.white));
                Assert.That(set.layers[0].logicalLayerId, Is.Not.Empty);
                Color normalNeutral = ReadTexturePixel(
                    controller.Reconstruction.surfaces[0].sourceTextures[1]);
                Assert.That(normalNeutral.r, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(normalNeutral.g, Is.EqualTo(0.5f).Within(0.01f));
                Assert.That(normalNeutral.b, Is.EqualTo(1f).Within(0.01f));
                Assert.That(normalNeutral.a, Is.EqualTo(1f).Within(0.01f));
                set.RemoveLayerAt(0);
                Assert.That(set.layers, Is.Empty);
            }
            finally
            {
                controller.Dispose();
                UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(umaMaterial);
                UnityEngine.Object.DestroyImmediate(slot);
            }
        }

        private static SlotDataAsset CreateSlotAsset(string name, string group, int tile)
        {
            SlotDataAsset slot = CreateSlot(name, group, tile);
            AssetDatabase.CreateAsset(slot, Folder + "/" + name + "_" + Guid.NewGuid().ToString("N") + ".asset");
            return slot;
        }

        private static SlotDataAsset CreateSlot(string name, string group, int tile)
        {
            SlotDataAsset slot = ScriptableObject.CreateInstance<SlotDataAsset>();
            slot.name = name;
            slot.udimGroupId = group;
            slot.udimGroupName = group;
            slot.udimTileNumber = tile;
            slot.udimSourceSubmeshIndex = 0;
            slot.meshData = new UMAMeshData
            {
                vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
                    new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
                },
                normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward },
                tangents = new[]
                {
                    new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f),
                    new Vector4(1f, 0f, 0f, 1f), new Vector4(1f, 0f, 0f, 1f)
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                uv2 = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up },
                colors32 = new[] { new Color32(1, 2, 3, 4), new Color32(1, 2, 3, 4), new Color32(1, 2, 3, 4), new Color32(1, 2, 3, 4) },
                vertexCount = 4,
                subMeshCount = 1,
                submeshes = new[] { new SubMeshTriangles(new[] { 0, 1, 2, 0, 2, 3 }) }
            };
            slot.subMeshIndex = 0;
            return slot;
        }

        private static void AssertVector(Vector3 actual, Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.00001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.00001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.00001f));
        }

        private static void ConfigureCanonicalTransform(SlotDataAsset slot, Quaternion rotation)
        {
            const int rootHash = 1234567;
            slot.meshData.rootBoneHash = rootHash;
            slot.meshData.boneNameHashes = new[] { rootHash };
            slot.meshData.bindPoses = new[] { Matrix4x4.Rotate(rotation) };
        }

        private static Color ReadTexturePixel(Texture texture)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = null;
            try
            {
                RenderTexture source = texture as RenderTexture;
                if (source == null)
                {
                    temporary = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32,
                        RenderTextureReadWrite.Linear);
                    Graphics.Blit(texture, temporary);
                    source = temporary;
                }
                RenderTexture.active = source;
                Texture2D readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                try
                {
                    readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    readback.Apply(false, false);
                    return readback.GetPixel(0, 0);
                }
                finally { UnityEngine.Object.DestroyImmediate(readback); }
            }
            finally
            {
                RenderTexture.active = previous;
                if (temporary != null) RenderTexture.ReleaseTemporary(temporary);
            }
        }
    }
}
