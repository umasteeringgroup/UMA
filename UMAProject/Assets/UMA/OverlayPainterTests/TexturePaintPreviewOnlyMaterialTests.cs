#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.TexturePaint.Editor.Tests
{
    public sealed class TexturePaintPreviewOnlyMaterialTests
    {
        private readonly List<Object> owned = new List<Object>();
        private GameObject source;
        private TexturePaintStageController controller;

        [TearDown]
        public void TearDown()
        {
            controller?.Dispose();
            controller = null;
            if (source != null) Object.DestroyImmediate(source);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChannelLessCorneaRemainsVisibleWithoutBlockingPaintableSkin(bool nullChannels)
        {
            DynamicCharacterAvatar avatar = CreateAvatar(nullChannels, true);
            Material originalCornea = avatar.GetRenderers()[0].sharedMaterials[0];
            string originalProperties = UnityEditor.EditorJsonUtility.ToJson(originalCornea);
            controller = new TexturePaintStageController();

            controller.Initialize(avatar, null, null, null, resolution: 128);

            MeshReconstructionResult reconstruction = controller.Reconstruction;
            Assert.That(reconstruction.surfaces, Has.Count.EqualTo(1));
            Assert.That(reconstruction.surfaces[0].index, Is.Zero);
            Assert.That(reconstruction.previewOnlySurfaces, Has.Count.EqualTo(1));
            ReconstructedSurface cornea = reconstruction.previewOnlySurfaces[0];
            Assert.That(cornea.sourceMaterial, Is.SameAs(originalCornea));
            Assert.That(cornea.previewMaterial, Is.Not.SameAs(originalCornea));
            Assert.That(cornea.previewMaterial.GetColor("_BaseColor"), Is.EqualTo(originalCornea.GetColor("_BaseColor")));
            Assert.That(cornea.gameObject.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(cornea.previewMaterial));
            Assert.That(cornea.mesh.vertexCount, Is.EqualTo(3));
            Assert.That(cornea.gameObject.GetComponent<Collider>(), Is.Null);
            Assert.That(controller.Textures.Sets, Has.Count.EqualTo(1));
            Assert.That(controller.Textures.Sets[0].materialCapability.IsSupported, Is.True);
            Assert.That(controller.Textures.Sets[0].GetChannel(TexturePaintChannel.Albedo), Is.Not.Null);
            Assert.That(controller.LogicalTargets.Targets, Has.Count.EqualTo(1));
            Assert.That(controller.LogicalTargets.FindBySlot(cornea.slotName), Is.Null);
            Assert.That(reconstruction.importWarnings.Any(w => w.Code == "OP_IMPORT_PREVIEW_ONLY_MATERIAL"), Is.True);
            Assert.That(UnityEditor.EditorJsonUtility.ToJson(originalCornea), Is.EqualTo(originalProperties));

            Mesh previewMesh = cornea.mesh;
            Material previewMaterial = cornea.previewMaterial;
            GameObject previewObject = cornea.gameObject;
            controller.Dispose();
            Assert.That(previewMesh == null, Is.True, "Preview-only meshes must be released with the stage.");
            Assert.That(previewMaterial == null, Is.True, "Preview-only materials must be released with the stage.");
            Assert.That(previewObject == null, Is.True);
            Assert.That(originalCornea != null, Is.True);
        }

        [Test]
        public void AvatarWithOnlyPreviewMaterialsReportsNoPaintableGeometry()
        {
            DynamicCharacterAvatar avatar = CreateAvatar(false, false);
            var error = Assert.Throws<InvalidOperationException>(() => MeshReconstructor.Reconstruct(avatar));
            Assert.That(error.Message, Does.Contain("No paintable material submeshes"));
        }

        private DynamicCharacterAvatar CreateAvatar(bool nullChannels, bool includeSkin)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Assert.That(shader, Is.Not.Null);
            source = new GameObject("Preview-only material test avatar");
            source.SetActive(false);
            var avatar = source.AddComponent<DynamicCharacterAvatar>();
            avatar.umaRecipe = new UMAData.UMARecipe();
            var renderer = source.AddComponent<SkinnedMeshRenderer>();
            renderer.rootBone = source.transform;
            renderer.bones = new[] { source.transform };
            var mesh = Track(new Mesh
            {
                name = "Cornea and skin",
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.right, Vector3.forward, Vector3.up + Vector3.forward, Vector3.right + Vector3.forward },
                uv = new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.zero, Vector2.up, Vector2.right },
                bindposes = new[] { Matrix4x4.identity },
                boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 6).ToArray(),
                subMeshCount = includeSkin ? 2 : 1
            });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            if (includeSkin) mesh.SetTriangles(new[] { 3, 4, 5 }, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            renderer.sharedMesh = mesh;
            var corneaMaterial = Track(new Material(shader) { name = "Cornea" });
            corneaMaterial.SetColor("_BaseColor", new Color(0.2f, 0.3f, 0.4f, 0.1f));
            var cornea = Track(ScriptableObject.CreateInstance<UMAMaterial>());
            cornea.name = "Cornea";
            cornea.material = corneaMaterial;
            cornea.materialType = UMAMaterial.MaterialType.UseExistingMaterial;
            cornea.channels = nullChannels ? null : Array.Empty<UMAMaterial.MaterialChannel>();
            avatar.generatedMaterials = new UMAData.GeneratedMaterials();
            avatar.generatedMaterials.materials.Add(new UMAData.GeneratedMaterial
            {
                umaMaterial = cornea, material = corneaMaterial, skinnedMeshRenderer = renderer, materialIndex = 0
            });
            var materials = new List<Material> { corneaMaterial };
            if (includeSkin)
            {
                var skinMaterial = Track(new Material(shader) { name = "Skin" });
                skinMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
                var skin = Track(ScriptableObject.CreateInstance<UMAMaterial>());
                skin.name = "Skin";
                skin.material = skinMaterial;
                skin.materialType = UMAMaterial.MaterialType.UseExistingTextures;
                skin.channels = new[] { new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.DiffuseTexture,
                    materialPropertyName = "_BaseMap", sourceTextureName = "_BaseMap", textureFormat = RenderTextureFormat.ARGB32
                } };
                materials.Add(skinMaterial);
                avatar.generatedMaterials.materials.Add(new UMAData.GeneratedMaterial
                {
                    umaMaterial = skin, material = skinMaterial, skinnedMeshRenderer = renderer, materialIndex = 1
                });
            }
            renderer.sharedMaterials = materials.ToArray();
            avatar.SetRenderers(new[] { renderer });
            return avatar;
        }

        private T Track<T>(T item) where T : Object
        {
            owned.Add(item);
            return item;
        }
    }
}
#endif
