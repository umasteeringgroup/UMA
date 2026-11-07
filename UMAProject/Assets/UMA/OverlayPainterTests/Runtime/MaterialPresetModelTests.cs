#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.TexturePaint.Tests
{
    public sealed class MaterialPresetModelTests
    {
        [Test]
        public void MigrationRepairsPresetCollectionsAndClearsSessionBindings()
        {
            TexturePaintMaterialPreset preset =
                ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
            preset.schemaVersion = 0;
            preset.presetId = null;
            preset.tags = null;
            preset.channels = null;
            preset.plugins = null;
            preset.packagedDependencies = null;
            preset.packagedExternalDependencies = null;
            preset.layers = new List<TexturePaintDocumentLayer>
            {
                new TexturePaintDocumentLayer
                {
                    id = null,
                    logicalLayerId = "source-logical-id",
                    paintTargetId = "source-target-id",
                    channels = null,
                    strokes = null
                }
            };

            preset.Migrate();

            Assert.That(preset.schemaVersion,
                Is.EqualTo(TexturePaintMaterialPreset.CurrentSchemaVersion));
            Assert.That(preset.presetId, Is.Not.Empty);
            Assert.That(preset.tags, Is.Not.Null);
            Assert.That(preset.channels, Is.Not.Null);
            Assert.That(preset.plugins, Is.Not.Null);
            Assert.That(preset.packagedDependencies, Is.Not.Null);
            Assert.That(preset.packagedExternalDependencies, Is.Not.Null);
            Assert.That(preset.layers[0].id, Is.Not.Empty);
            Assert.That(preset.layers[0].logicalLayerId, Is.Null);
            Assert.That(preset.layers[0].paintTargetId, Is.Null);
            Assert.That(preset.layers[0].channels, Is.Not.Null);
            Assert.That(preset.layers[0].strokes, Is.Not.Null);
            Object.DestroyImmediate(preset);
        }

        [Test]
        public void MigrationKeepsTemplateHierarchyAndRepairsDuplicateIdentity()
        {
            TexturePaintMaterialPreset preset =
                ScriptableObject.CreateInstance<TexturePaintMaterialPreset>();
            preset.layers = new List<TexturePaintDocumentLayer>
            {
                new TexturePaintDocumentLayer { id = "group", kind = TexturePaintLayerKind.Group },
                new TexturePaintDocumentLayer { id = "duplicate", parentId = "group" },
                new TexturePaintDocumentLayer { id = "duplicate", parentId = "group" }
            };

            preset.Migrate();

            Assert.That(preset.layers[1].id, Is.Not.EqualTo(preset.layers[2].id));
            Assert.That(preset.layers[1].parentId, Is.EqualTo("group"));
            Assert.That(preset.layers[2].parentId, Is.EqualTo("group"));
            Object.DestroyImmediate(preset);
        }
    }
}
#endif
