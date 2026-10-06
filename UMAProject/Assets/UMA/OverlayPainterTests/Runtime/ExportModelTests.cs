#if UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class ExportModelTests
    {
        [Test]
        public void ChannelSelectionAndInversionAreIndependent()
        {
            TexturePaintExportTemplate template = ScriptableObject.CreateInstance<TexturePaintExportTemplate>();
            template.channels = TexturePaintChannelMask.Albedo | TexturePaintChannelMask.Normal;
            template.invertedChannels = TexturePaintChannelMask.Roughness;
            Assert.That(template.Includes(TexturePaintChannel.Albedo), Is.True);
            Assert.That(template.Includes(TexturePaintChannel.Metallic), Is.False);
            Assert.That(template.Inverts(TexturePaintChannel.Roughness), Is.True);
            Assert.That(template.Inverts(TexturePaintChannel.Albedo), Is.False);
            template.channels = TexturePaintChannelMask.All;
            Assert.That(template.Includes(TexturePaintChannel.SkinColorMask), Is.True);
            Assert.That(template.Includes(TexturePaintChannel.Thickness), Is.True);
            Assert.That(template.Includes(TexturePaintChannel.DetailMask), Is.True);
            Assert.That(template.Includes(TexturePaintChannel.NormalControl), Is.True);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void ExportContentModeDefaultsToFlattenedAndSurvivesSerialization()
        {
            TexturePaintExportTemplate template = ScriptableObject.CreateInstance<TexturePaintExportTemplate>();
            Assert.That(template.content, Is.EqualTo(TexturePaintExportContent.FlattenedComposite));
            template.content = TexturePaintExportContent.AuthoredOverlay;

            string json = JsonUtility.ToJson(template);
            TexturePaintExportTemplate restored = ScriptableObject.CreateInstance<TexturePaintExportTemplate>();
            JsonUtility.FromJsonOverwrite(json, restored);

            Assert.That(restored.version, Is.EqualTo(TexturePaintExportTemplate.CurrentVersion));
            Assert.That(restored.content, Is.EqualTo(TexturePaintExportContent.AuthoredOverlay));
            Object.DestroyImmediate(restored);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void LegacyAllChannelsTemplateAddsNewSkinMaterialChannels()
        {
            TexturePaintExportTemplate template = ScriptableObject.CreateInstance<TexturePaintExportTemplate>();
            template.version = 3;
            template.channels = TexturePaintChannelMask.Albedo | TexturePaintChannelMask.Normal |
                TexturePaintChannelMask.Metallic | TexturePaintChannelMask.Roughness |
                TexturePaintChannelMask.AmbientOcclusion | TexturePaintChannelMask.Emission |
                TexturePaintChannelMask.Custom;

            template.Migrate();

            Assert.That(template.version, Is.EqualTo(TexturePaintExportTemplate.CurrentVersion));
            Assert.That(template.Includes(TexturePaintChannel.SkinColorMask), Is.True);
            Assert.That(template.Includes(TexturePaintChannel.Thickness), Is.True);
            Assert.That(template.Includes(TexturePaintChannel.DetailMask), Is.True);
            Assert.That(template.Includes(TexturePaintChannel.NormalControl), Is.True);
            Object.DestroyImmediate(template);
        }

        [Test]
        public void FingerprintSeparatesTopologyUvAndGeometryChanges()
        {
            Mesh mesh = MakeQuad();
            TexturePaintSurfaceFingerprint baseline = TexturePaintSurfaceFingerprintUtility.Compute(mesh);
            Vector2[] uv = mesh.uv; uv[1] = new Vector2(0.75f, 0f); mesh.uv = uv;
            TexturePaintSurfaceFingerprint uvChanged = TexturePaintSurfaceFingerprintUtility.Compute(mesh);
            Assert.That(uvChanged.topology, Is.EqualTo(baseline.topology));
            Assert.That(uvChanged.geometry, Is.EqualTo(baseline.geometry));
            Assert.That(uvChanged.uv, Is.Not.EqualTo(baseline.uv));
            int[] triangles = mesh.triangles; mesh.triangles = new[] { triangles[0], triangles[2], triangles[1], triangles[3], triangles[2], triangles[0] };
            TexturePaintSurfaceFingerprint topologyChanged = TexturePaintSurfaceFingerprintUtility.Compute(mesh);
            Assert.That(topologyChanged.topology, Is.Not.EqualTo(uvChanged.topology));
            Vector3[] vertices = mesh.vertices; vertices[0] += Vector3.forward; mesh.vertices = vertices;
            TexturePaintSurfaceFingerprint geometryChanged = TexturePaintSurfaceFingerprintUtility.Compute(mesh);
            Assert.That(geometryChanged.geometry, Is.Not.EqualTo(topologyChanged.geometry));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ExportRecordsSurviveRecipeStateSerialization()
        {
            TexturePaintStageState state = new TexturePaintStageState { exportTemplateGuid = "template" };
            state.exportRecords.Add(new TexturePaintExportRecord
            {
                surfaceId = "surface", texturePath = "Assets/paint.png", channel = TexturePaintChannel.Albedo,
                overlayGuid = "overlay", materialGuid = "material"
            });
            TexturePaintStageState restored = JsonUtility.FromJson<TexturePaintStageState>(JsonUtility.ToJson(state));
            Assert.That(restored.version, Is.EqualTo(TexturePaintStageState.CurrentVersion));
            Assert.That(restored.exportTemplateGuid, Is.EqualTo("template"));
            Assert.That(restored.exportRecords.Count, Is.EqualTo(1));
            Assert.That(restored.exportRecords[0].overlayGuid, Is.EqualTo("overlay"));
        }

        [Test]
        public void WorkspaceLayoutAndBrushShelfSurviveRecipeStateSerialization()
        {
            TexturePaintStageState state = new TexturePaintStageState
            {
                tool = TexturePaintTool.Smear,
                workspaceLeftWidth = 271f,
                workspaceRightWidth = 347f,
                workspaceShelfHeight = 191f,
                workspaceShowUV = false,
                workspaceShowAssetShelf = true,
                workspaceUVPan = new Vector2(17f, -9f),
                workspaceUVZoom = 2.25f,
                channelSolo = true,
                previewBefore = true,
                isolateSelectedSlots = true,
                wireframe = true,
                assetShelfSearch = "skin soft",
                assetShelfFolder = "Assets/Brushes",
                assetShelfFavoritesOnly = true
            };
            state.favoriteBrushGuids.Add("favorite");
            state.recentBrushGuids.Add("recent");
            state.brushOrderGuids.Add("ordered");
            state.collapsedLayerGroupIds.Add("collapsed-group");
            state.collapsedPropertySectionIds.Add("properties.layer-channels");

            TexturePaintStageState restored = JsonUtility.FromJson<TexturePaintStageState>(JsonUtility.ToJson(state));

            Assert.That(restored.version, Is.EqualTo(TexturePaintStageState.CurrentVersion));
            Assert.That(restored.tool, Is.EqualTo(TexturePaintTool.Smear));
            Assert.That(restored.workspaceLeftWidth, Is.EqualTo(271f));
            Assert.That(restored.workspaceRightWidth, Is.EqualTo(347f));
            Assert.That(restored.workspaceShelfHeight, Is.EqualTo(191f));
            Assert.That(restored.workspaceShowUV, Is.False);
            Assert.That(restored.workspaceUVPan, Is.EqualTo(new Vector2(17f, -9f)));
            Assert.That(restored.workspaceUVZoom, Is.EqualTo(2.25f));
            Assert.That(restored.channelSolo && restored.previewBefore && restored.isolateSelectedSlots && restored.wireframe, Is.True);
            Assert.That(restored.assetShelfSearch, Is.EqualTo("skin soft"));
            Assert.That(restored.favoriteBrushGuids, Is.EqualTo(new[] { "favorite" }));
            Assert.That(restored.recentBrushGuids, Is.EqualTo(new[] { "recent" }));
            Assert.That(restored.brushOrderGuids, Is.EqualTo(new[] { "ordered" }));
            Assert.That(restored.collapsedLayerGroupIds, Is.EqualTo(new[] { "collapsed-group" }));
            Assert.That(restored.collapsedPropertySectionIds,
                Is.EqualTo(new[] { "properties.layer-channels" }));
        }

        private static Mesh MakeQuad()
        {
            return new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 1, 2, 2, 1, 3 }
            };
        }
    }
}
#endif
