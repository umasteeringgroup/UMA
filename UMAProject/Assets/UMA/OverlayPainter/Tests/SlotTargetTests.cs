#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class SlotTargetTests
    {
        private readonly List<SlotDataAsset> createdAssets = new List<SlotDataAsset>();
        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < createdAssets.Count; i++)
                if (createdAssets[i] != null) Object.DestroyImmediate(createdAssets[i]);
            createdAssets.Clear();
            for (int i = createdObjects.Count - 1; i >= 0; i--)
                if (createdObjects[i] != null) Object.DestroyImmediate(createdObjects[i]);
            createdObjects.Clear();
        }

        [Test]
        public void SurfaceResolvesSlotForEachTriangle()
        {
            ReconstructedSurface surface = new ReconstructedSurface
            {
                slotName = "Torso",
                slotNames = new List<string> { "Torso", "Arms" },
                triangleSlotNames = new[] { "Torso", "Arms" }
            };

            Assert.That(surface.ContainsSlot("Arms"), Is.True);
            Assert.That(surface.ContainsSlot("Head"), Is.False);
            Assert.That(surface.GetTriangleSlotName(0), Is.EqualTo("Torso"));
            Assert.That(surface.GetTriangleSlotName(1), Is.EqualTo("Arms"));
        }

        [Test]
        public void SelectedSlotsRoundTripThroughStageState()
        {
            TexturePaintStageState state = new TexturePaintStageState();
            state.selectedSlots.Add("Torso");
            state.selectedSlots.Add("Arms");

            TexturePaintStageState restored = JsonUtility.FromJson<TexturePaintStageState>(JsonUtility.ToJson(state));

            Assert.That(restored.selectedSlots, Is.EqualTo(new[] { "Torso", "Arms" }));
        }

        [Test]
        public void CatalogGroupsOnlySlotsWithTheSameUdimGroupId()
        {
            SlotDataAsset tile1001 = CreateSlot("HumanBody_UDIM1001", "human-body", "Human Body", 1001);
            SlotDataAsset tile1002 = CreateSlot("HumanBody_UDIM1002", "human-body", "Human Body", 1002);
            SlotDataAsset similarlyNamedSingle = CreateSlot("HumanBody_UDIMAccessory");
            ReconstructedSurface surface1001 = CreateSurface(tile1001);
            ReconstructedSurface surface1002 = CreateSurface(tile1002);
            ReconstructedSurface singleSurface = CreateSurface(similarlyNamedSingle);
            var catalog = new TexturePaintLogicalTargetCatalog();

            catalog.Rebuild(new[] { surface1002, singleSurface, surface1001 });

            Assert.That(catalog.Targets.Count, Is.EqualTo(2));
            TexturePaintLogicalTarget udim = catalog.FindById("udim:human-body");
            Assert.That(udim, Is.Not.Null);
            Assert.That(udim.isUdim, Is.True);
            Assert.That(udim.displayName, Is.EqualTo("Human Body"));
            Assert.That(udim.members.Count, Is.EqualTo(2));
            Assert.That(udim.members[0].udimTileNumber, Is.EqualTo(1001));
            Assert.That(udim.members[1].udimTileNumber, Is.EqualTo(1002));
            Assert.That(catalog.FindBySlot(similarlyNamedSingle.slotName).isUdim, Is.False);
            Assert.That(catalog.FindBySlot(similarlyNamedSingle.slotName).members.Count, Is.EqualTo(1));
        }

        [Test]
        public void LogicalTargetExpandsExactlyItsConcreteMembers()
        {
            SlotDataAsset tile1001 = CreateSlot("Body_1001", "body", "Body", 1001);
            SlotDataAsset tile1003 = CreateSlot("Body_1003", "body", "Body", 1003);
            SlotDataAsset shoes = CreateSlot("Shoes");
            var catalog = new TexturePaintLogicalTargetCatalog();
            catalog.Rebuild(new[] { CreateSurface(tile1001), CreateSurface(shoes), CreateSurface(tile1003) });
            var expanded = new List<string> { "stale" };

            catalog.FindById("udim:body").ExpandSlotNames(expanded);

            Assert.That(expanded, Is.EqualTo(new[] { "Body_1001", "Body_1003" }));
            Assert.That(expanded, Does.Not.Contain("Shoes"));
        }

        [Test]
        public void CatalogBindsEachMemberToItsPhysicalTextureSet()
        {
            SlotDataAsset tile1001 = CreateSlot("Body_1001", "body", "Body", 1001);
            SlotDataAsset tile1002 = CreateSlot("Body_1002", "body", "Body", 1002);
            ReconstructedSurface surface1001 = CreateSurface(tile1001);
            ReconstructedSurface surface1002 = CreateSurface(tile1002);
            TextureSet set1001 = new TextureSet { surface = surface1001 };
            TextureSet set1002 = new TextureSet { surface = surface1002 };
            var catalog = new TexturePaintLogicalTargetCatalog();
            catalog.Rebuild(new[] { surface1001, surface1002 });

            catalog.BindTextureSets(new[] { set1001, set1002 });

            TexturePaintLogicalTarget target = catalog.FindById("udim:body");
            Assert.That(target.members[0].textureSets, Is.EqualTo(new[] { set1001 }));
            Assert.That(target.members[1].textureSets, Is.EqualTo(new[] { set1002 }));
        }

        [Test]
        public void LogicalLayerCreationLinksAndActivatesEveryUdimMember()
        {
            SlotDataAsset tile1001 = CreateSlot("Body_1001", "body", "Body", 1001);
            SlotDataAsset tile1002 = CreateSlot("Body_1002", "body", "Body", 1002);
            ReconstructedSurface surface1001 = CreateSurface(tile1001);
            ReconstructedSurface surface1002 = CreateSurface(tile1002);
            var set1001 = new TextureSet { surface = surface1001 };
            var set1002 = new TextureSet { surface = surface1002 };
            var catalog = new TexturePaintLogicalTargetCatalog();
            catalog.Rebuild(new[] { surface1001, surface1002 });
            catalog.BindTextureSets(new[] { set1001, set1002 });
            var logicalLayers = new TexturePaintLogicalLayerController(catalog);
            TexturePaintLayer primary = set1001.AddLayer("Skin Paint");
            primary.effects.stroke.enabled = true;
            primary.effects.stroke.width = 6f;
            primary.effects.edgeFade.enabled = true;
            primary.effects.edgeFade.edgeFadeStart = 0.7f;
            primary.effects.edgeFade.edgeFadeSize = 0.9f;
            var created = new List<TexturePaintLogicalLayerMember>();

            bool linked = logicalLayers.LinkAndRepair(catalog.FindById("udim:body"), set1001, primary,
                created, out TexturePaintLogicalLayerBinding binding);

            Assert.That(linked, Is.True);
            Assert.That(binding.complete, Is.True);
            Assert.That(binding.members.Count, Is.EqualTo(2));
            Assert.That(created.Count, Is.EqualTo(1), "Only newly-created siblings are reported for undo.");
            Assert.That(binding.members[0].layer.logicalLayerId, Is.EqualTo(binding.members[1].layer.logicalLayerId));
            Assert.That(binding.members[0].layer.paintTargetId, Is.EqualTo("udim:body"));
            Assert.That(binding.members[1].layer.effects.stroke.enabled, Is.True);
            Assert.That(binding.members[1].layer.effects.stroke.width, Is.EqualTo(6f));
            Assert.That(binding.members[1].layer.effects.edgeFade.enabled, Is.True);
            Assert.That(binding.members[1].layer.effects.edgeFade.edgeFadeStart, Is.EqualTo(0.7f));
            Assert.That(binding.members[1].layer.effects.edgeFade.edgeFadeSize, Is.EqualTo(0.9f));
            Assert.That(binding.members[1].layer.effects, Is.Not.SameAs(primary.effects));
            Assert.That(logicalLayers.Activate(binding), Is.True);
            Assert.That(set1001.layers[set1001.activeLayerIndex], Is.SameAs(binding.members[0].layer));
            Assert.That(set1002.layers[set1002.activeLayerIndex], Is.SameAs(binding.members[1].layer));
            set1001.Dispose();
            set1002.Dispose();
        }

        [Test]
        public void LogicalPathCreationCopiesAuthoredChannelSourcesToEveryMember()
        {
            SlotDataAsset tile1001 = CreateSlot("Body_1001", "body", "Body", 1001);
            SlotDataAsset tile1002 = CreateSlot("Body_1002", "body", "Body", 1002);
            ReconstructedSurface surface1001 = CreateSurface(tile1001);
            ReconstructedSurface surface1002 = CreateSurface(tile1002);
            var set1001 = new TextureSet { surface = surface1001 };
            var set1002 = new TextureSet { surface = surface1002 };
            AddTestChannel(set1001, TexturePaintChannel.Albedo);
            AddTestChannel(set1002, TexturePaintChannel.Albedo);
            var catalog = new TexturePaintLogicalTargetCatalog();
            catalog.Rebuild(new[] { surface1001, surface1002 });
            catalog.BindTextureSets(new[] { set1001, set1002 });
            var logicalLayers = new TexturePaintLogicalLayerController(catalog);
            TexturePaintLayer primary = set1001.AddSplineLayer("Leather Path");
            Assert.That(set1001.GetPaintTarget(TexturePaintChannel.Albedo,
                TexturePaintSourceMode.SourceOverlay), Is.Not.Null);
            primary.GetChannelSettings(TexturePaintChannel.Albedo).sourceSettings =
                new TexturePaintChannelSourceSettings
                {
                    source = TexturePaintBrushSource.Color,
                    color = Color.red
                };

            bool linked = logicalLayers.LinkAndRepair(catalog.FindById("udim:body"), set1001,
                primary, null, out TexturePaintLogicalLayerBinding binding);

            Assert.That(linked, Is.True);
            TexturePaintLayer peer = binding.members[1].layer;
            Assert.That(peer.IsSplineLayer, Is.True);
            Assert.That(peer.channels.ContainsKey(TexturePaintChannel.Albedo), Is.True);
            TexturePaintChannelSourceSettings peerSource =
                peer.GetChannelSettings(TexturePaintChannel.Albedo, false)?.sourceSettings;
            Assert.That(peerSource, Is.Not.Null);
            Assert.That(peerSource.source, Is.EqualTo(TexturePaintBrushSource.Color));
            Assert.That(peerSource.color, Is.EqualTo(Color.red));
            set1001.Dispose();
            set1002.Dispose();
        }

        [Test]
        public void MissingLogicalMemberIsReportedThenExplicitlyRepaired()
        {
            SlotDataAsset tile1001 = CreateSlot("Body_1001", "body", "Body", 1001);
            SlotDataAsset tile1002 = CreateSlot("Body_1002", "body", "Body", 1002);
            ReconstructedSurface surface1001 = CreateSurface(tile1001);
            ReconstructedSurface surface1002 = CreateSurface(tile1002);
            var set1001 = new TextureSet { surface = surface1001 };
            var set1002 = new TextureSet { surface = surface1002 };
            var catalog = new TexturePaintLogicalTargetCatalog();
            catalog.Rebuild(new[] { surface1001, surface1002 });
            catalog.BindTextureSets(new[] { set1001, set1002 });
            var logicalLayers = new TexturePaintLogicalLayerController(catalog);
            TexturePaintLayer primary = set1001.AddLayer("Skin Paint");
            logicalLayers.LinkAndRepair(catalog.FindById("udim:body"), set1001, primary,
                null, out TexturePaintLogicalLayerBinding initial);
            TexturePaintLayer removed = initial.members[1].layer;
            set1002.layers.Remove(removed);

            TexturePaintLogicalLayerBinding broken = logicalLayers.Resolve(initial.target, primary.logicalLayerId);
            var repairedMembers = new List<TexturePaintLogicalLayerMember>();
            bool repaired = logicalLayers.LinkAndRepair(initial.target, set1001, primary, repairedMembers,
                out TexturePaintLogicalLayerBinding repairedBinding);

            Assert.That(broken.complete, Is.False);
            Assert.That(broken.error, Does.Contain("missing"));
            Assert.That(repaired, Is.True);
            Assert.That(repairedBinding.complete, Is.True);
            Assert.That(repairedMembers.Count, Is.EqualTo(1));
            removed.Dispose();
            set1001.Dispose();
            set1002.Dispose();
        }

        [Test]
        public void CollapsedUdimSurfaceSplitsTrianglesByMemberSlot()
        {
            SlotDataAsset tile1001 = CreateSlot("Body_1001", "body", "Body", 1001);
            SlotDataAsset tile1002 = CreateSlot("Body_1002", "body", "Body", 1002);
            var slots = new List<SlotData> { new SlotData(tile1001), new SlotData(tile1002) };

            List<MeshReconstructor.SurfaceSlice> slices = MeshReconstructor.BuildSurfaceSlices(
                new[] { 0, 1, 2, 3, 4, 5 }, new[] { "Body_1001", "Body_1002" },
                new List<string> { "Body_1001", "Body_1002" }, slots);

            Assert.That(slices.Count, Is.EqualTo(2));
            Assert.That(slices[0].slotNames, Is.EqualTo(new[] { "Body_1001" }));
            Assert.That(slices[0].triangles, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(slices[1].slotNames, Is.EqualTo(new[] { "Body_1002" }));
            Assert.That(slices[1].triangles, Is.EqualTo(new[] { 3, 4, 5 }));
        }

        [Test]
        public void NonUdimSlotsSharingOneGeneratedSurfaceSplitIntoNativeSlotSurfaces()
        {
            SlotDataAsset shirt = CreateSlot("Shirt");
            SlotDataAsset shoes = CreateSlot("Shoes");
            var slots = new List<SlotData> { new SlotData(shirt), new SlotData(shoes) };

            List<MeshReconstructor.SurfaceSlice> slices = MeshReconstructor.BuildSurfaceSlices(
                new[] { 0, 1, 2, 3, 4, 5 }, new[] { "Shirt", "Shoes" },
                new List<string> { "Shirt", "Shoes" }, slots);
            var catalog = new TexturePaintLogicalTargetCatalog();
            ReconstructedSurface surface = new ReconstructedSurface
            {
                slotName = "Shirt",
                slotNames = new List<string> { "Shirt", "Shoes" },
                slots = slots
            };
            catalog.Rebuild(new[] { surface });

            Assert.That(slices.Count, Is.EqualTo(2));
            Assert.That(new[] { slices[0].slotNames[0], slices[1].slotNames[0] },
                Is.EquivalentTo(new[] { "Shirt", "Shoes" }));
            Assert.That(catalog.Targets.Count, Is.EqualTo(2));
            Assert.That(catalog.FindBySlot("Shirt"), Is.Not.SameAs(catalog.FindBySlot("Shoes")));
        }

        [Test]
        public void CollapsedUdimSurfaceStaysIntactWhenTriangleOwnershipIsAmbiguous()
        {
            SlotDataAsset tile1001 = CreateSlot("Body_1001", "body", "Body", 1001);
            SlotDataAsset tile1002 = CreateSlot("Body_1002", "body", "Body", 1002);
            var slots = new List<SlotData> { new SlotData(tile1001), new SlotData(tile1002) };

            List<MeshReconstructor.SurfaceSlice> slices = MeshReconstructor.BuildSurfaceSlices(
                new[] { 0, 1, 2, 3, 4, 5 }, new[] { "Body_1001", null },
                new List<string> { "Body_1001", "Body_1002" }, slots);

            Assert.That(slices.Count, Is.EqualTo(1));
            Assert.That(slices[0].slotNames, Is.EqualTo(new[] { "Body_1001", "Body_1002" }));
            Assert.That(slices[0].triangles, Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
        }

        [TestCase(UMAMaterial.MaterialType.UseExistingTextures, true)]
        [TestCase(UMAMaterial.MaterialType.UseExistingMaterial, false)]
        [TestCase(UMAMaterial.MaterialType.Atlas, false)]
        [TestCase(UMAMaterial.MaterialType.NoAtlas, false)]
        public void OnlyExistingMaterialModesUseReadOnlyMaterialSources(
            UMAMaterial.MaterialType materialType, bool expected)
        {
            UMAMaterial umaMaterial = Track(ScriptableObject.CreateInstance<UMAMaterial>());
            umaMaterial.materialType = materialType;

            Assert.That(MeshReconstructor.UsesReadOnlyMaterialSources(umaMaterial), Is.EqualTo(expected));
        }

        [Test]
        public void ReadOnlyMaterialSourcesComeFromTheAssignedRuntimeMaterial()
        {
            Shader shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ??
                            Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            Texture2D templateTexture = Track(new Texture2D(2, 2) { name = "Template Texture" });
            Texture2D assignedTexture = Track(new Texture2D(2, 2) { name = "Assigned Texture" });
            Material template = Track(new Material(shader) { name = "UMA Template" });
            Material assigned = Track(new Material(shader) { name = "Generated Runtime Material" });
            template.SetTexture("_MainTex", templateTexture);
            assigned.SetTexture("_MainTex", assignedTexture);
            UMAMaterial umaMaterial = Track(ScriptableObject.CreateInstance<UMAMaterial>());
            umaMaterial.material = template;
            umaMaterial.materialType = UMAMaterial.MaterialType.UseExistingTextures;
            umaMaterial.channels = new[]
            {
                new UMAMaterial.MaterialChannel
                {
                    channelType = UMAMaterial.ChannelType.TintedTexture,
                    materialPropertyName = "_MainTex"
                }
            };

            Texture[] sources = MeshReconstructor.BuildReadOnlyMaterialSources(umaMaterial, assigned);

            Assert.That(sources, Has.Length.EqualTo(1));
            Assert.That(sources[0], Is.SameAs(assignedTexture));
        }

        [Test]
        public void GeneratedSecondPassIsRecognizedAsDuplicateGeometry()
        {
            Shader shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Sprites/Default") ??
                            Shader.Find("Standard");
            Assert.That(shader, Is.Not.Null);
            GameObject avatarObject = Track(new GameObject("Generated Avatar"));
            UMAData data = avatarObject.AddComponent<UMAData>();
            SkinnedMeshRenderer renderer = avatarObject.AddComponent<SkinnedMeshRenderer>();
            Material firstPass = Track(new Material(shader) { name = "First Pass" });
            Material secondPass = Track(new Material(shader) { name = "Second Pass(Clone)" });
            UMAMaterial umaMaterial = Track(ScriptableObject.CreateInstance<UMAMaterial>());
            UMAData.GeneratedMaterial generated = new UMAData.GeneratedMaterial
            {
                material = firstPass,
                secondPassMaterial = secondPass,
                umaMaterial = umaMaterial,
                skinnedMeshRenderer = renderer,
                materialIndex = 0
            };
            data.generatedMaterials.materials.Add(generated);

            MeshReconstructor.FindGeneratedMaterial(data, renderer, secondPass, 1,
                out UMAData.GeneratedMaterial resolved, out UMAMaterial resolvedUmaMaterial,
                out bool isSecondPass);

            Assert.That(resolved, Is.SameAs(generated));
            Assert.That(resolvedUmaMaterial, Is.SameAs(umaMaterial));
            Assert.That(isSecondPass, Is.True);
        }

        private SlotDataAsset CreateSlot(string slotName, string groupId = null, string groupName = null, int tile = 0)
        {
            SlotDataAsset asset = ScriptableObject.CreateInstance<SlotDataAsset>();
            asset.name = slotName;
            asset.udimGroupId = groupId;
            asset.udimGroupName = groupName;
            asset.udimTileNumber = tile;
            createdAssets.Add(asset);
            return asset;
        }

        private T Track<T>(T value) where T : Object
        {
            createdObjects.Add(value);
            return value;
        }

        private static void AddTestChannel(TextureSet set, TexturePaintChannel channel)
        {
            set.channels[channel] = new TextureChannelTarget
            {
                channel = channel,
                format = RenderTextureFormat.ARGB32,
                editable = new EditableTextureTarget("Logical Path " + channel, 8, 8,
                    RenderTextureFormat.ARGB32, null, TextureSet.DefaultColor(channel))
            };
        }

        private static ReconstructedSurface CreateSurface(SlotDataAsset asset)
        {
            SlotData slot = new SlotData(asset);
            return new ReconstructedSurface
            {
                slotName = asset.slotName,
                slotNames = new List<string> { asset.slotName },
                slots = new List<SlotData> { slot }
            };
        }
    }
}
#endif
