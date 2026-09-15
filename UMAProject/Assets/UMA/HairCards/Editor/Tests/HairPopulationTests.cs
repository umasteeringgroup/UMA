#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.HairCards.Editor.Tests
{
    public sealed partial class HairCoreTests
    {
        [Test]
        public void PackagedHairExamplesAreDiscoverableAndLoadableWhenIncluded()
        {
            // Examples live outside the distributable Hair Cards source folder and
            // are optional in source-only projects. When present, test the folder
            // index as well as direct loading: either can work while the other fails.
            const string root = "Assets/UMAProjectData/HairCards/Examples";
            if (!Directory.Exists(root)) return;
            foreach (string directory in new[] { root }.Concat(Directory.GetDirectories(root, "*", SearchOption.AllDirectories)))
            {
                string folder = directory.Replace('\\', '/');
                Assert.That(AssetDatabase.IsValidFolder(folder), Is.True, "Unimported example folder: " + folder);
                var discovered = new HashSet<string>(AssetDatabase.FindAssets("", new[] { folder })
                    .Select(AssetDatabase.GUIDToAssetPath), StringComparer.Ordinal);
                foreach (string file in Directory.GetFiles(directory))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = file.Replace('\\', '/');
                    Assert.That(discovered.Contains(path), Is.True,
                        "Example is missing from the Project browser's folder index, even if direct loading works: " + path);
                    string guid = AssetDatabase.AssetPathToGUID(path);
                    Assert.That(guid, Is.Not.Empty, path);
                    Assert.That(AssetDatabase.GUIDToAssetPath(guid), Is.EqualTo(path), "Incorrect GUID mapping: " + path);
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    Assert.That(asset, Is.Not.Null, "Example cannot be loaded: " + path);
                    Assert.That(asset.hideFlags & HideFlags.HideInHierarchy, Is.EqualTo(HideFlags.None),
                        "Example must not be hidden from the Project browser: " + path);
                }
            }
        }

        [Test]
        public void ExternalGuideJsonUsesOrdinaryNonzeroUvRectangles()
        {
            var data = JsonUtility.FromJson<HairGuideImport.Data>("{\"version\":1,\"regions\":[{\"x\":0.46,\"y\":0.03,\"width\":0.15,\"height\":0.94}]}");
            Assert.That(data.regions[0].Rectangle, Is.EqualTo(new Rect(.46f,.03f,.15f,.94f)));
        }

        [TestCase(1, 0f)]
        [TestCase(2, .2f)]
        public void RibbonFrontFaceWindingAgreesWithItsOutwardNormals(int spans, float camber)
        {
            profile.Configure(HairCardShape.Ribbon,.01f,.01f,4,generateBackfaces:false);
            profile.ConfigureRibbon(spans,camber,false);
            var curve=new HairEvaluatedCurve { profile=profile,rootNormal=Vector3.forward };
            curve.points.Add(new HairCurvePoint(Vector3.zero,.01f,0));
            curve.points.Add(new HairCurvePoint(Vector3.up*.1f,.01f,0));
            var evaluation=new HairEvaluationResult();evaluation.curves.Add(curve);
            using var build=HairCardMeshGenerator.Build(evaluation);
            var v=build.mesh.vertices;var normals=build.mesh.normals;var indices=build.mesh.triangles;
            for(int i=0;i<indices.Length;i+=3)
            {
                int a=indices[i],b=indices[i+1],c=indices[i+2];
                Assert.That(Vector3.Dot(Vector3.Cross(v[b]-v[a],v[c]-v[a]).normalized,
                    (normals[a]+normals[b]+normals[c]).normalized),Is.GreaterThan(.9f));
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void SweptShaderActuallyDrawsOpaqueTexelsAndClipsTransparentTexels(bool alphaToCoverage, bool softCoverage)
        {
            var shader = Shader.Find("UMA/Hair Cards/Swept Hair URP");
            Assert.That(shader, Is.Not.Null);
            bool async = ShaderUtil.allowAsyncCompilation; ShaderUtil.allowAsyncCompilation = false;
            var material = new Material(shader); var mesh = new Mesh();
            var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var target = new RenderTexture(64,64,24) { antiAliasing = alphaToCoverage ? 4 : 1 }; var pixels = new Texture2D(64,64,TextureFormat.RGBA32,false,true);
            var oldTarget = RenderTexture.active;
            try
            {
                texture.SetPixels(new[]{Color.white,Color.clear}); texture.Apply();
                material.SetTexture("_BaseMap",texture); material.SetFloat("_AlphaToCoverage",alphaToCoverage ? 1 : 0); material.SetFloat("_DebugView",3);
                material.SetFloat("_DitheredOpacity",softCoverage ? 1 : 0); material.SetFloat("_Coverage",.4f);
                mesh.vertices=new[]{new Vector3(-.8f,-.8f,-1),new Vector3(.8f,-.8f,-1),new Vector3(-.8f,.8f,-1),new Vector3(.8f,.8f,-1)};
                mesh.normals=Enumerable.Repeat(Vector3.back,4).ToArray(); mesh.tangents=Enumerable.Repeat(new Vector4(0,1,0,1),4).ToArray();
                mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.up,Vector2.one}; mesh.uv2=mesh.uv;mesh.uv3=mesh.uv;
                mesh.colors=Enumerable.Repeat(Color.white,4).ToArray(); mesh.triangles=new[]{0,2,1,1,2,3};
                target.Create();
                using(var commands=new UnityEngine.Rendering.CommandBuffer())
                {
                    commands.SetRenderTarget(target);commands.ClearRenderTarget(true,true,Color.clear);
                    commands.SetViewProjectionMatrices(Matrix4x4.identity,GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(-1,1,-1,1,.01f,10f),true));
                    commands.DrawMesh(mesh,Matrix4x4.identity,material,0,0);Graphics.ExecuteCommandBuffer(commands);
                }
                RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,64,64),0,0);pixels.Apply();
                int drawn=pixels.GetPixels32().Count(c=>c.r>20 || c.g>20 || c.b>20);
                Assert.That(drawn,softCoverage ? Is.InRange(300,850) : Is.InRange(900,1800),"Transparent texels must clip; soft coverage should retain about 40% of the opaque half, while cutout ignores strand opacity.");
            }
            finally
            {
                RenderTexture.active=oldTarget; ShaderUtil.allowAsyncCompilation=async;
                Object.DestroyImmediate(material);Object.DestroyImmediate(mesh);Object.DestroyImmediate(texture);
                target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(pixels);
            }
        }

        [Test]
        public void ScalpRecipeRefreshComposesGroupsAndRemovesDisabledOwnedModifier()
        {
            var group=PopulationFixture();var scalp=group.generation.scalp;
            scalp.enabled=true;scalp.bindingTopology=groom.SourceTopologySignature;
            scalp.slots.Add(new HairScalpSlotBinding{slotName="ReviewScalp",vertexCount=groom.SourceVertexCount});
            var modifier=ScriptableObject.CreateInstance<UMA.MeshModifier>();scalp.meshModifier=modifier;
            var other=ScriptableObject.CreateInstance<UMA.MeshModifier>();
            var recipe=ScriptableObject.CreateInstance<UMA.CharacterSystem.UMAWardrobeRecipe>();
            try
            {
                recipe.MeshModifiers.Add(other);
                HairScalpShadingEditor.SynchronizeRecipe(groom,recipe,null);
                HairScalpShadingEditor.SynchronizeRecipe(groom,recipe,null);
                Assert.That(recipe.MeshModifiers.Count(m=>m==modifier),Is.EqualTo(1));
                scalp.enabled=false;HairScalpShadingEditor.SynchronizeRecipe(groom,recipe,null);
                Assert.That(recipe.MeshModifiers,Is.EquivalentTo(new[]{other}));
            }
            finally{Object.DestroyImmediate(recipe);Object.DestroyImmediate(other);Object.DestroyImmediate(modifier);}
        }
        private HairGroup PopulationFixture()
        {
            var group = groom.Groups[0];
            HairGroomCommands.FillMap(groom, group.FindMap(HairMapKind.GrowthArea), 1f);
            group.generation.enabled = true;
            group.generation.clumps.Add(new HairGenerationStage { name = "Clumps", count = 8,
                minimumSpacing = 0f, uniformity = 0f, influenceRadius = 5f, seed = 27 });
            var cards = group.generation.cards;
            cards.count = 32; cards.minimumSpacing = 0f; cards.uniformity = 0f; cards.influenceRadius = 5f;
            cards.lengthVariation = cards.widthVariation = cards.tiltVariation = cards.flyawayFraction = 0f;
            group.EnsureIntegrity(groom.SourceVertexCount);
            return group;
        }

        [Test]
        public void LegacyChildClumpingUsesItsOwnIncomingPopulationAndPublicMasks()
        {
            var group = groom.Groups[0]; group.generation.enabled = false;
            group.children.childrenPerGuide = 12; group.children.rootSpread = .025f;
            group.children.lengthVariation = 0; group.children.clump = 0;
            var inputs = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { includeChildren = false }).evaluatedGuides;
            var before = new HairEvaluationResult();
            HairChildGenerator.Generate(groom, group, inputs, null, new HairEvaluationOptions(), before);
            var modifier = HairGroomCommands.AddModifier(groom, group, HairModifierType.Clump);
            modifier.domain = HairModifierDomain.Children; modifier.amount = 1; modifier.rootInfluence = 1; modifier.clumpRadius = 1;
            var after = new HairEvaluationResult();
            HairChildGenerator.Generate(groom, group, inputs, null, new HairEvaluationOptions(), after);
            Assert.That(after.curves.Where(c => c.isChild).Any(c => Vector3.Distance(c.points[^1].position,
                before.curves.Single(b => b.curveId == c.curveId).points[^1].position) > 1e-5f), Is.True, "Child-only Clump must not be a no-op.");
            foreach (var curve in after.curves.Where(c => c.isChild))
            {
                var original = before.curves.Single(c => c.curveId == curve.curveId);
                Assert.That(curve.points[0].position, Is.EqualTo(original.points[0].position));
                Assert.That(curve.Length, Is.EqualTo(original.Length).Within(1e-5f));
            }
            var mask = new HairGrowthMap { kind = HairMapKind.Custom, defaultValue = 0 };
            mask.EnsureIntegrity(groom.SourceVertexCount); group.maps.Add(mask); modifier.maskMapId = mask.Id;
            var masked = new HairEvaluationResult();
            HairChildGenerator.Generate(groom, group, inputs, null, new HairEvaluationOptions(), masked);
            foreach (var curve in masked.curves)
                Assert.That(curve.points.Select(p => p.position), Is.EqualTo(before.curves.Single(c => c.curveId == curve.curveId).points.Select(p => p.position)));
        }

        [Test]
        public void GeneratedHairShaderChannelsSurviveNativeUmaSlotSaveAndReload()
        {
            PopulationFixture();
            using var build = HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom));
            string folder = "Assets/__HairShaderChannels_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            try
            {
                var slot = ScriptableObject.CreateInstance<SlotDataAsset>(); slot.name = "ShaderChannelTest";
                slot.meshData = new UMAMeshData(); slot.meshData.RetrieveDataFromUnityMesh(build.mesh);
                AssetDatabase.CreateAsset(slot, folder + "/Slot.asset"); AssetDatabase.SaveAssets(); Resources.UnloadAsset(slot);
                var loaded = AssetDatabase.LoadAssetAtPath<SlotDataAsset>(folder + "/Slot.asset").meshData;
                Assert.That(loaded.uv, Is.EqualTo(build.mesh.uv));
                Assert.That(loaded.uv2, Is.EqualTo(build.mesh.uv2), "Root-to-tip position and strand seed must reach the UMA shader.");
                Assert.That(loaded.uv3, Is.EqualTo(build.mesh.uv3), "Parent-clump seed and mask must reach the UMA shader.");
                Assert.That(loaded.colors32, Is.EqualTo(build.mesh.colors32), "RGBA remains available for vertex animation.");
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }

        [Test]
        public void SurfacePopulationsGenerateClumpsThenCardsWithAnchoredRoots()
        {
            var group = PopulationFixture();
            var result = HairGroomEvaluator.Evaluate(groom);
            Assert.That(result.generatedGuides.Count, Is.EqualTo(8));
            Assert.That(result.CardCount, Is.EqualTo(32));
            Assert.That(result.populations[1].inputCount, Is.EqualTo(8));
            var parentIds = result.generatedGuides.Select(c => c.curveId).ToHashSet();
            foreach (var curve in result.curves)
            {
                Assert.That(curve.rootAnchor.IsValid, Is.True);
                Assert.That(Vector3.Distance(curve.points[0].position, curve.rootAnchor.CachedLocalPosition), Is.LessThan(1e-6f));
                Assert.That(curve.points[0].position.y, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(parentIds, Does.Contain(curve.clumpId));
                Assert.That(curve.generationStageId, Is.EqualTo(group.generation.cards.Id));
            }
        }

        [Test]
        public void MissingPopulationPartMapStopsUnsafeBlendingAndReportsActionableIssue()
        {
            var group = PopulationFixture(); group.generation.cards.partMapId = "removed-part-map";
            Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount, Is.Zero);
            Assert.That(HairValidator.Validate(groom).issues.Any(i => i.code == HairValidationCode.MissingMap && i.severity == HairValidationSeverity.Error), Is.True);
            group.generation.cards.partMapId = null;
            Assert.That(HairGroomEvaluator.Evaluate(groom).CardCount, Is.GreaterThan(0));
        }

        [Test]
        public void PopulationRootCacheSurvivesCombingButInvalidatesForPaintAndInPlaceMeshEdits()
        {
            var group = PopulationFixture(); var workspace = new HairEvaluationWorkspace();
            var first = HairGroomEvaluator.Evaluate(groom, null, workspace);
            Assert.That(first.populations.All(p => !p.rootsReused), Is.True);
            group.guides[0].points[1].position += Vector3.right * 0.01f;
            var second = HairGroomEvaluator.Evaluate(groom, null, workspace);
            Assert.That(second.populations.All(p => p.rootsReused), Is.True);
            CollectionAssert.AreEqual(first.curves.Select(c => c.rootAnchor.CachedLocalPosition), second.curves.Select(c => c.rootAnchor.CachedLocalPosition));
            group.FindMap(HairMapKind.GrowthArea).values[0] = 0.5f;
            Assert.That(HairGroomEvaluator.Evaluate(groom, null, workspace).populations.All(p => !p.rootsReused), Is.True);
            var vertices = sourceMesh.vertices; vertices[0].x -= 0.02f; sourceMesh.vertices = vertices;
            Assert.That(HairGroomEvaluator.Evaluate(groom, null, workspace).populations.All(p => !p.rootsReused), Is.True);
        }

        [Test]
        public void PopulationDraftAndLodKeepShapeSeedsAndClumpParentsStable()
        {
            var group = PopulationFixture(); var full = HairGroomEvaluator.Evaluate(groom);
            var draft = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { previewSampleCount = 3, interactiveSampleLimit = 10 });
            Assert.That(draft.CardCount, Is.EqualTo(10));
            for (int i = 0; i < draft.CardCount; i++)
            {
                Assert.That(draft.curves[i].curveId, Is.EqualTo(full.curves[i].curveId));
                Assert.That(draft.curves[i].clumpId, Is.EqualTo(full.curves[i].clumpId));
                CollectionAssert.AreEqual(full.curves[i].points.Select(p => p.position), draft.curves[i].points.Select(p => p.position));
            }
            groom.Lods[0].cardFraction = 0.5f;
            var reduced = HairGroomEvaluator.Evaluate(groom);
            Assert.That(reduced.CardCount, Is.InRange(1, 31));
            foreach (var curve in reduced.curves)
            {
                var original = full.curves.Single(c => c.curveId == curve.curveId);
                CollectionAssert.AreEqual(original.points.Select(p => p.position), curve.points.Select(p => p.position));
            }
        }

        [Test]
        public void PopulationBypassAndInspectionDoNotModifySavedSettings()
        {
            var group = PopulationFixture(); string saved = EditorJsonUtility.ToJson(groom);
            var isolated = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { previewGenerationStageId = group.generation.clumps[0].Id });
            Assert.That(isolated.CardCount, Is.EqualTo(8));
            Assert.That(isolated.populations.Count, Is.EqualTo(1));
            Assert.That(EditorJsonUtility.ToJson(groom), Is.EqualTo(saved));
            group.generation.clumps[0].enabled = false;
            var bypassed = HairGroomEvaluator.Evaluate(groom);
            Assert.That(bypassed.populations.Single().inputCount, Is.EqualTo(1));
            Assert.That(bypassed.CardCount, Is.EqualTo(32));
        }

        [Test]
        public void PopulationModifiersApplyOnceAndMasksActuallyGateOutput()
        {
            var group = PopulationFixture(); var baseline = HairGroomEvaluator.Evaluate(groom);
            var mask = new HairGrowthMap { kind = HairMapKind.Custom, name = "Test mask", defaultValue = 0f };
            mask.EnsureIntegrity(groom.SourceVertexCount); group.maps.Add(mask);
            var modifier = new HairModifierSettings { type = HairModifierType.Width, amount = 2f,
                domain = HairModifierDomain.GuidesAndChildren, maskMapId = mask.Id };
            modifier.EnsureIntegrity(); group.generation.cards.modifiers.Add(modifier);
            var masked = HairGroomEvaluator.Evaluate(groom);
            Assert.That(masked.curves[0].points[0].width, Is.EqualTo(baseline.curves[0].points[0].width));
            Array.Fill(mask.values, 1f);
            var active = HairGroomEvaluator.Evaluate(groom);
            Assert.That(active.curves[0].points[0].width, Is.EqualTo(baseline.curves[0].points[0].width * 2f).Within(1e-6f));
            modifier.maskMapId = "missing"; modifier.mask.invert = true;
            Assert.That(HairGroomEvaluator.Evaluate(groom).curves[0].points[0].width, Is.EqualTo(baseline.curves[0].points[0].width));
        }

        [Test]
        public void ClumpingPreservesEveryIncomingSegmentAndDoesNotMoveRoots()
        {
            var group = PopulationFixture(); group.generation.cards.clump = 0f;
            var before = HairGroomEvaluator.Evaluate(groom); group.generation.cards.clump = 1f;
            var after = HairGroomEvaluator.Evaluate(groom);
            for (int c = 0; c < before.CardCount; c++)
                for (int p = 1; p < before.curves[c].points.Count; p++)
                    Assert.That(Vector3.Distance(after.curves[c].points[p].position, after.curves[c].points[p - 1].position),
                        Is.EqualTo(Vector3.Distance(before.curves[c].points[p].position, before.curves[c].points[p - 1].position)).Within(1e-5f));
        }

        [Test]
        public void CurvedRibbonAddsCrossSectionWithoutUsingAnimationColorsForMetadata()
        {
            PopulationFixture(); profile.ConfigureRibbon(2, 0.2f, false);
            profile.ConfigureVertexColors(true, new Color(0.2f, 0.3f, 0.4f, 0.5f), Color.white, 1);
            using var mesh = HairCardMeshGenerator.Build(HairGroomEvaluator.Evaluate(groom), "Curved ribbon", includeEditingMetadata: true);
            Assert.That(mesh.cards[0].vertexCount, Is.EqualTo(4 * 3));
            var vertices = mesh.mesh.vertices;
            Assert.That(Vector3.Distance(vertices[1], (vertices[0] + vertices[2]) * 0.5f), Is.GreaterThan(1e-5f));
            Assert.That(mesh.mesh.colors[0].a, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(mesh.mesh.uv2.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.mesh.uv3.Length, Is.EqualTo(mesh.vertexCount));
            Assert.That(mesh.mesh.uv2[0].x, Is.EqualTo(0f));
            Assert.That(mesh.mesh.uv2[11].x, Is.EqualTo(1f));
        }

        [Test]
        public void AdaptiveRibbonReservesMoreSamplesForLongCurvedStrands()
        {
            profile.ConfigureRibbon(2, 0.2f, true, 0.01f, 10f);
            var shortLine = new List<HairCurvePoint> { new HairCurvePoint(Vector3.zero, 1f, 0f), new HairCurvePoint(Vector3.up * 0.005f, 1f, 0f) };
            Assert.That(profile.ResolveSampleCount(shortLine, 13), Is.EqualTo(2));
            shortLine[1] = new HairCurvePoint(Vector3.up * 0.2f, 1f, 0f);
            Assert.That(profile.ResolveSampleCount(shortLine, 13), Is.EqualTo(13));
        }

        [Test]
        public void ScalpShadingUsesVertexColorsAndKeepsSourceAndAlphaUnchanged()
        {
            sourceMesh.vertices = new[] { new Vector3(-1,0,-1), new Vector3(1,0,-1), new Vector3(1,0,1), new Vector3(-1,0,1), Vector3.zero };
            sourceMesh.normals = Enumerable.Repeat(Vector3.up, 5).ToArray();
            sourceMesh.triangles = new[] { 0,4,1, 1,4,2, 2,4,3, 3,4,0 };
            sourceMesh.colors32 = Enumerable.Repeat(new Color32(255,255,255,123), 5).ToArray();
            groom.SetSource(sourceMesh, "scalp-shading", slotName: "ScalpSlot");
            var group = groom.Groups[0]; Array.Fill(group.FindMap(HairMapKind.GrowthArea).values, 1f);
            group.generation.scalp.enabled = true; group.generation.scalp.strength = 1f; group.generation.scalp.color = Color.black;
            var output = new List<Color32>();
            HairScalpShadingUtility.Evaluate(groom, sourceMesh.colors32, output, new Dictionary<string, HairSurfaceFields>());
            Assert.That(output[4].r, Is.EqualTo(0)); Assert.That(output[4].a, Is.EqualTo(123));
            Assert.That(output[0].r, Is.EqualTo(255)); Assert.That(sourceMesh.colors32[4].r, Is.EqualTo(255));
            var modifier = ScriptableObject.CreateInstance<UMA.MeshModifier>();
            try
            {
                HairScalpShadingEditor.Populate(modifier, groom, group, new[] { new HairScalpSlotBinding { slotName = "ScalpSlot", vertexCount = 5 } });
                Assert.That(modifier.RuntimeModifiers.Count, Is.EqualTo(1));
                Assert.That(modifier.RuntimeModifiers[0].adjustments.vertexAdjustments.Single().vertexIndex, Is.EqualTo(4));
                HairScalpShadingEditor.Populate(modifier, groom, group, new[] { new HairScalpSlotBinding { slotName = "ScalpSlot", vertexCount = 5 } });
                Assert.That(modifier.RuntimeModifiers[0].adjustments.Count(), Is.EqualTo(1), "Repeated updates must not accumulate adjustments.");
                var input = new MeshDetails { vertices = sourceMesh.vertices, colors32 = sourceMesh.colors32 };
                var applied = modifier.RuntimeModifiers[0].Process(input);
                Assert.That(applied.colors32[4].r, Is.Zero);
                Assert.That(applied.colors32[4].a, Is.EqualTo(123));
                Assert.That(input.colors32[4].r, Is.EqualTo(255), "Applying the UMA modifier must not recolor its source.");
                Assert.That(applied.vertices, Is.SameAs(input.vertices), "Scalp shading adds no geometry.");
                Assert.That(applied.verticesModified, Is.False);
            }
            finally { Object.DestroyImmediate(modifier); }
        }

        [Test]
        public void PopulationTreeSupportsSingleSelectionToggleDuplicateReorderAndPopupRemoval()
        {
            var group = PopulationFixture(); var stage = CreateEditingStage();
            try
            {
                var population = group.generation.clumps[0];
                var modifier = HairGenerationEditor.AddModifier(groom, group, population, HairModifierType.Noise);
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Modifier, modifier.Id));
                Assert.That(stage.ActiveModifier, Is.SameAs(modifier));
                Assert.That(stage.SelectedNode.Layer, Is.Null);
                Assert.That(HairGroomNodes.Toggle(stage, stage.SelectedNode, false), Is.True);
                Assert.That(modifier.enabled, Is.False);
                Assert.That(HairGroomNodeWindow.DuplicateNode(stage, stage.SelectedNode), Is.True);
                Assert.That(population.modifiers.Count, Is.EqualTo(2));
                Assert.That(HairGroomNodeWindow.MoveSelected(stage, stage.SelectedNode, -1), Is.True);
                bool asked = false;
                Assert.That(HairGroomNodeWindow.RemoveNode(stage, stage.SelectedNode, (_,__,___,____) => { asked = true; return false; }), Is.False);
                Assert.That(asked, Is.True); Assert.That(population.modifiers.Count, Is.EqualTo(2));
                Assert.That(HairGroomNodeWindow.RemoveNode(stage, stage.SelectedNode, (_,__,___,____) => true), Is.True);
                Assert.That(population.modifiers.Count, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(stage); }
        }

        [Test]
        public void HairResourceScriptClassesHaveMatchingUnityImportFiles()
        {
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/UMA/HairCards/Core/HairCardProfileAsset.cs").GetClass(), Is.EqualTo(typeof(HairCardProfileAsset)));
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/UMA/HairCards/Core/HairAtlasProfileAsset.cs").GetClass(), Is.EqualTo(typeof(HairAtlasProfileAsset)));
        }

        [Test]
        public void HairPopulationResourcesSurviveAssetSaveAndReimport()
        {
            string folder = "Assets/__HairPopulationPersistence_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", System.IO.Path.GetFileName(folder));
            try
            {
                var saved = ScriptableObject.CreateInstance<HairGroomAsset>(); saved.name = "Persistent Population";
                var mesh = Object.Instantiate(sourceMesh); AssetDatabase.CreateAsset(mesh, folder + "/Source.asset");
                saved.SetSource(mesh, "asset:" + AssetDatabase.AssetPathToGUID(folder + "/Source.asset"));
                AssetDatabase.CreateAsset(saved, folder + "/Groom.asset");
                var group = saved.Groups[0]; HairGenerationEditor.ApplySweptPreset(saved, group, folder);
                group.generation.cards.modifiers[0].mask.useHairline = true;
                group.generation.cards.modifiers[0].mask.hairlineRange = new Vector2(.003f,.019f);
                var texture = new Texture2D(2,2); AssetDatabase.CreateAsset(texture, folder + "/Atlas.asset");
                group.atlas.albedo = texture; group.atlas.CreateRegion("Remember strip", new Rect(.1f,.2f,.3f,.7f));
                group.profile.ConfigureVertexColors(true, Color.red, Color.blue, 2);
                string stageId = group.generation.cards.Id, modifierId = group.generation.cards.modifiers[0].Id;
                EditorUtility.SetDirty(saved); EditorUtility.SetDirty(group.atlas); EditorUtility.SetDirty(group.profile); AssetDatabase.SaveAssets();
                Resources.UnloadAsset(saved);
                AssetDatabase.ImportAsset(folder + "/Groom.asset", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var loaded = AssetDatabase.LoadAssetAtPath<HairGroomAsset>(folder + "/Groom.asset");
                var actual = loaded.Groups[0];
                Assert.That(actual.generation.cards.Id, Is.EqualTo(stageId));
                Assert.That(actual.generation.cards.modifiers[0].Id, Is.EqualTo(modifierId));
                Assert.That(actual.generation.cards.modifiers[0].mask.hairlineRange, Is.EqualTo(new Vector2(.003f,.019f)));
                Assert.That(actual.profile.RibbonSpans, Is.EqualTo(2)); Assert.That(actual.profile.RibbonCamber, Is.EqualTo(.2f));
                Assert.That(actual.profile.RootColorSegments, Is.EqualTo(2));
                Assert.That(actual.atlas.albedo, Is.EqualTo(AssetDatabase.LoadAssetAtPath<Texture2D>(folder + "/Atlas.asset")));
                Assert.That(actual.atlas.regions.Any(r => r.name == "Remember strip"), Is.True);
                Assert.That(loaded.SourceMesh, Is.Not.Null);
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }

        [Test]
        public void CurvedClumpModifierIsOrderIndependentAndPreservesIncomingLengths()
        {
            var group = groom.Groups[0]; group.guides.Clear();
            for (int i=0;i<3;i++)
            {
                var guide = CreateGuide("Local clump " + i, i, Vector3.right * (.002f * i));
                guide.points.Clear(); Vector3 root = guide.root.CachedLocalPosition;
                foreach (Vector3 offset in i == 0 ? new[] { Vector3.zero, new Vector3(0,.06f,0), new Vector3(.05f,.1f,0), new Vector3(.05f,.15f,.04f) } :
                    new[] { Vector3.zero, new Vector3(0,.06f,0), new Vector3(0,.12f,0), new Vector3(0,.18f,0) })
                    guide.points.Add(new HairGuidePoint { position=root+offset, width=.01f, widthBaseline=.01f });
                group.guides.Add(guide);
            }
            var before = HairGroomEvaluator.Evaluate(groom, new HairEvaluationOptions { includeChildren=false }).evaluatedGuides.ToDictionary(c=>c.curveId);
            var modifier = HairGroomCommands.AddModifier(groom,group,HairModifierType.Clump);
            modifier.amount=1f; modifier.rootInfluence=1f; modifier.clumpRadius=3f;
            var first = HairGroomEvaluator.Evaluate(groom,new HairEvaluationOptions { includeChildren=false }).evaluatedGuides.ToDictionary(c=>c.curveId);
            group.guides.Reverse();
            var second = HairGroomEvaluator.Evaluate(groom,new HairEvaluationOptions { includeChildren=false }).evaluatedGuides;
            foreach(var curve in second)
            {
                Assert.That(curve.points.Select(p=>p.position),Is.EqualTo(first[curve.curveId].points.Select(p=>p.position)));
                Assert.That(curve.points[0].position,Is.EqualTo(before[curve.curveId].points[0].position));
                for(int p=1;p<curve.points.Count;p++) Assert.That(Vector3.Distance(curve.points[p-1].position,curve.points[p].position),
                    Is.EqualTo(Vector3.Distance(before[curve.curveId].points[p-1].position,before[curve.curveId].points[p].position)).Within(1e-5));
            }
        }

        [Test]
        public void PopulationInspectionDoesNotPersistVisibilityAndExitsForSculpting()
        {
            var group = PopulationFixture(); var stage = CreateEditingStage();
            try
            {
                var layer = HairGroomCommands.AddSculptLayer(groom, group);
                string before = HairPreferenceCodec.Capture(stage);
                stage.InspectPopulation(group.generation.clumps[0].Id);
                Assert.That(HairPreferenceCodec.Capture(stage), Is.EqualTo(before));
                stage.SelectNode(HairGroomNodes.Key(HairGroomNodeKind.Layer, layer.Id));
                Assert.That(stage.InspectedPopulationId, Is.Null);
                var result = HairGroomEvaluator.Evaluate(groom,new HairEvaluationOptions { editGroupId=group.Id,editLayerId=layer.Id });
                Assert.That(result.populations, Is.Empty); Assert.That(result.childCurveCount, Is.Zero);
                Assert.That(result.CardCount, Is.EqualTo(result.evaluatedGuides.Count));
            }
            finally { Object.DestroyImmediate(stage); }
        }

        [Test]
        public void GeneratedPopulationPoseUsesItsOwnSurfaceTriangleNotParentGuide()
        {
            sourceMesh.vertices = new[] { Vector3.zero,Vector3.right,Vector3.forward, Vector3.right*2,Vector3.right*3,Vector3.right*2+Vector3.forward };
            sourceMesh.triangles = new[] { 0,2,1, 3,5,4 }; sourceMesh.normals = Enumerable.Repeat(Vector3.up,6).ToArray();
            groom.SetSource(sourceMesh,"pose-population");
            var guide = groom.Groups[0].guides[0];
            guide.root = HairSurfaceAnchor.Create(groom.SourceMeshId,0,0,Vector3.right,0,Vector3.zero,Vector3.up);
            Mesh posed = Object.Instantiate(sourceMesh);
            try
            {
                var vertices=posed.vertices; for(int i=3;i<6;i++)vertices[i]+=Vector3.up*.4f; posed.vertices=vertices;
                var curve = new HairEvaluatedCurve { curveId="population:1",generationStageId="population",parentGuideId=guide.Id,
                    rootAnchor=HairSurfaceAnchor.Create(groom.SourceMeshId,0,1,Vector3.right,0,Vector3.right*2,Vector3.up) };
                curve.points.Add(new HairCurvePoint(Vector3.right*2,.01f,0)); curve.points.Add(new HairCurvePoint(Vector3.right*2+Vector3.up*.1f,.01f,0));
                var input=new HairEvaluationResult(); input.curves.Add(curve);
                var pose=new HairAuthoringPose(sourceMesh,posed); var actual=pose.TransformEvaluation(groom,input).curves[0];
                Assert.That(actual.points[0].position,Is.EqualTo(Vector3.right*2+Vector3.up*.4f));
                Assert.That(curve.points[0].position,Is.EqualTo(Vector3.right*2));
            }
            finally { Object.DestroyImmediate(posed); }
        }

        [Test]
        public void SweptShaderCompilesColorShadowDepthAndClusterVariants()
        {
            if (Shader.Find("Universal Render Pipeline/Lit") == null) Assert.Ignore("URP is not installed in this test project.");
            var shader=Shader.Find(HairSweptShaderGUI.ShaderName); Assert.That(shader,Is.Not.Null);
            var variants=new ShaderVariantCollection(); var material=new Material(shader);
            try
            {
                foreach(var keywords in new[] {Array.Empty<string>(),new[]{"_MAIN_LIGHT_SHADOWS","_ADDITIONAL_LIGHTS"},
                    new[]{"_MAIN_LIGHT_SHADOWS_CASCADE","_ADDITIONAL_LIGHTS","_CLUSTER_LIGHT_LOOP","_SHADOWS_SOFT","_SCREEN_SPACE_OCCLUSION"}})
                    variants.Add(new ShaderVariantCollection.ShaderVariant(shader,UnityEngine.Rendering.PassType.ScriptableRenderPipeline,keywords));
                variants.Add(new ShaderVariantCollection.ShaderVariant(shader,UnityEngine.Rendering.PassType.ShadowCaster));
                variants.WarmUp();
                Assert.That(material.passCount,Is.EqualTo(4));
                for(int i=0;i<material.passCount;i++) Assert.That(material.SetPass(i),Is.True,"Shader pass "+i);
                Assert.That(ShaderUtil.GetShaderMessages(shader).Where(m=>m.severity.ToString()=="Error").Select(m=>m.message),Is.Empty);
            }
            finally { Object.DestroyImmediate(material); Object.DestroyImmediate(variants); }
        }
    }
}
#endif
