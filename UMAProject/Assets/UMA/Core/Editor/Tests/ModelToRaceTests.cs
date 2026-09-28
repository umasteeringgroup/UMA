using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UMA.CharacterSystem;
using UMA.Editors.ModelToRace;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace UMA.Tests
{
    public sealed class ModelToRaceTests
    {
        private GameObject source;
        private Mesh mesh;
        private Material material;
        private ModelToRacePlan plan;
        private string folder;
        private IDisposable generator;

        [SetUp]
        public void SetUp()
        {
            generator = new UMAMeshPreparationTests.GeneratorScope();
            folder = "Assets/__ModelToRaceTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring(7));
            source = new GameObject("SourceModel");
            var hips = new GameObject("Hips").transform; hips.SetParent(source.transform, false); hips.localPosition = Vector3.up;
            var head = new GameObject("Head").transform; head.SetParent(hips, false); head.localPosition = Vector3.up;
            var renderer = new GameObject("Body").AddComponent<SkinnedMeshRenderer>(); renderer.transform.SetParent(source.transform, false);
            mesh = new Mesh { name = "TwoMaterialBody" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.one };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 4).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4).ToArray();
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.subMeshCount = 2; mesh.SetTriangles(new[] { 0, 1, 2 }, 0); mesh.SetTriangles(new[] { 1, 3, 2 }, 1);
            mesh.bindposes = new[] { hips.worldToLocalMatrix, head.worldToLocalMatrix };
            mesh.boneWeights = Enumerable.Range(0, 4).Select(i => new BoneWeight { boneIndex0 = i % 2, weight0 = 1 }).ToArray();
            mesh.AddBlendShapeFrame("Smile", 50, Enumerable.Repeat(Vector3.right * .1f, 4).ToArray(), new Vector3[4], new Vector3[4]);
            mesh.AddBlendShapeFrame("Smile", 100, Enumerable.Repeat(Vector3.right * .2f, 4).ToArray(), new Vector3[4], new Vector3[4]);
            mesh.AddBlendShapeFrame("Blink", 100, Enumerable.Repeat(Vector3.up * .1f, 4).ToArray(), new Vector3[4], new Vector3[4]);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Hidden/InternalErrorShader")) { name = "SourceMaterial" };
            renderer.sharedMesh = mesh; renderer.sharedMaterials = new[] { material, material }; renderer.bones = new[] { hips, head }; renderer.rootBone = hips;
            renderer.SetBlendShapeWeight(0, 25);
            plan = new ModelToRacePlan { source = source, outputParent = folder, registerAssets = false, createAvatarPrefab = false };
            plan.Scan();
        }

        [TearDown]
        public void TearDown()
        {
            // Only this test's unique folder and entries are removed, never user assets.
            var index = UMAAssetIndexer.Instance;
            foreach (string guid in AssetDatabase.FindAssets("", new[] { folder }))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null && index.IsIndexedType(asset.GetType()))
                {
                    var item = index.GetAssetItem(asset.GetType(), AssetItem.GetEvilName(asset));
                    if (item != null && item._Path.StartsWith(folder + "/", StringComparison.Ordinal)) index.RemoveAsset(asset.GetType(), AssetItem.GetEvilName(asset), false);
                }
            }
            index.RemoveAssetsComplete();
            AssetDatabase.DeleteAsset(folder);
            Object.DestroyImmediate(source); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
            generator.Dispose();
        }

        [Test]
        public void ScanDefaultsToAllShapesAndRefreshRetainsUserChoices()
        {
            Assert.That(plan.shapes.Count, Is.EqualTo(2)); Assert.That(plan.shapes.All(s => s.include));
            Assert.That(plan.bones.Count, Is.EqualTo(2));
            Assert.That(plan.shapes.Single(s => s.name == "Smile").defaultWeight, Is.EqualTo(25));
            plan.shapes[1].include = false; plan.RefreshDNA(); Assert.False(plan.shapes[1].include);
            plan.parts.ForEach(p => p.role = ModelPartRole.Skip); plan.RefreshDNA(); Assert.IsEmpty(plan.shapes);
        }

        [Test]
        public void ConversionPreservesAllFramesBindPosesAndSource()
        {
            var transform = Matrix4x4.TRS(new Vector3(2, 3, 4), Quaternion.Euler(0, 30, 0), new Vector3(2, 1, 1));
            var copy = ModelToRaceBuilder.ConvertMesh(mesh, transform, new HashSet<string> { "Smile" });
            try
            {
                Assert.That(copy.blendShapeCount, Is.EqualTo(1)); Assert.That(copy.GetBlendShapeFrameCount(0), Is.EqualTo(2));
                Assert.That(mesh.blendShapeCount, Is.EqualTo(2)); Assert.That(mesh.vertices[0], Is.EqualTo(Vector3.zero));
                Assert.That(Vector3.Distance(copy.vertices[0], new Vector3(2, 3, 4)), Is.LessThan(0.00001f));
                var delta = new Vector3[4]; copy.GetBlendShapeFrameVertices(0, 1, delta, null, null);
                Assert.That(Vector3.Distance(delta[0], transform.MultiplyVector(Vector3.right * .2f)), Is.LessThan(.00001f));
                AssertMatrix(mesh.bindposes[0], copy.bindposes[0] * transform);
            }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test]
        public void MirroredTransformsCorrectWindingAndTangentHandedness()
        {
            var copy = ModelToRaceBuilder.ConvertMesh(mesh, Matrix4x4.Scale(new Vector3(-1, 1, 1)), new HashSet<string>());
            try { CollectionAssert.AreEqual(new[] { 1, 0, 2 }, copy.GetTriangles(0)); Assert.That(copy.tangents[0].w, Is.EqualTo(-1)); Assert.That(copy.blendShapeCount, Is.Zero); }
            finally { Object.DestroyImmediate(copy); }
        }

        [Test]
        public void StagingPreservesRestSkinningWithNestedRendererTransforms()
        {
            var renderer = plan.parts[0].renderer;
            renderer.transform.localPosition = new Vector3(.4f, .5f, .1f); renderer.transform.localRotation = Quaternion.Euler(30, 0, 20);
            mesh.bindposes = renderer.bones.Select(b => b.worldToLocalMatrix * renderer.localToWorldMatrix).ToArray();
            using (var stage = new ModelToRaceBuilder.StagingRig(plan))
            {
                var copy = stage.CreateRenderer(plan.parts[0]);
                try
                {
                    for (int i = 0; i < copy.bones.Length; i++) AssertMatrix(Matrix4x4.identity, copy.bones[i].localToWorldMatrix * copy.sharedMesh.bindposes[i]);
                    Assert.That(stage.BoneNames, Does.Contain("Hips"));
                }
                finally { stage.DestroyRenderer(copy); }
            }
        }

        [Test]
        public void ValidationRejectsBrokenRigAndDuplicateBoneNamesBeforeWriting()
        {
            Assert.IsEmpty(plan.Validate());
            plan.parts[0].renderer.bones[0].name = "Head";
            Assert.That(plan.Validate().Any(s => s.Contains("duplicate bone")));
            Assert.Throws<InvalidOperationException>(() => ModelToRaceBuilder.Build(plan));
            Assert.That(AssetDatabase.GetSubFolders(folder), Is.Empty);
        }

        [Test]
        public void GlobalRestTransformAndWeightedGlobalArePreservedWhenNormalized()
        {
            var renderer = plan.parts[0].renderer;
            var global = new GameObject("Global").transform; global.SetParent(source.transform, false);
            global.localRotation = Quaternion.Euler(90, 90, 0); global.localScale = Vector3.one * .1f;
            renderer.bones[0].SetParent(global, false);
            renderer.bones = renderer.bones.Concat(new[] { global }).ToArray();
            mesh.bindposes = renderer.bones.Select(b => b.worldToLocalMatrix * renderer.localToWorldMatrix).ToArray();
            plan.RefreshDNA();
            using (var stage = new ModelToRaceBuilder.StagingRig(plan))
            {
                var copy = stage.CreateRenderer(plan.parts[0]);
                try { for (int i = 0; i < copy.bones.Length; i++) AssertMatrix(Matrix4x4.identity, copy.bones[i].localToWorldMatrix * copy.sharedMesh.bindposes[i]); }
                finally { stage.DestroyRenderer(copy); }
            }
        }

        [Test]
        public void UnskinnedMeshesFailValidationWithActionableMessage()
        {
            mesh.boneWeights = new BoneWeight[4];
            Assert.That(plan.Validate().Any(e => e.Contains("every vertex must be skinned")));
        }

        [Test]
        public void ShapeRangeUsesAuthoredFrameWeightsRatherThanAssumingHundred()
        {
            mesh.ClearBlendShapes();
            mesh.AddBlendShapeFrame("CustomRange", 25, Enumerable.Repeat(Vector3.up, 4).ToArray(), new Vector3[4], new Vector3[4]);
            plan.parts[0].renderer.SetBlendShapeWeight(0, 0); plan.Scan();
            Assert.That(plan.shapes[0].maximum, Is.EqualTo(25));
            Assert.That(plan.shapes[0].minimum, Is.Zero);
        }

        [Test]
        public void NewRaceCreatesSlotsOverlaysNeutralBoneDnaAndSelectedShapeDna()
        {
            plan.shapes.Single(s => s.name == "Blink").include = false;
            var result = ModelToRaceBuilder.Build(plan);
            Assert.That(result.slots.Count, Is.EqualTo(2)); Assert.That(result.overlays.Count, Is.EqualTo(2));
            Assert.That(result.race.baseRaceRecipe, Is.Not.Null); Assert.That(result.race.umaTarget, Is.EqualTo(RaceData.UMATarget.Generic));
            Assert.False(result.race.FixupRotations); Assert.True(result.race.useNewDNA);
            foreach (var slot in result.slots)
            {
                Assert.That(slot.meshData.blendShapes.Select(s => s.shapeName), Does.Contain("Smile"));
                Assert.That(slot.meshData.blendShapes.Select(s => s.shapeName), Does.Not.Contain("Blink"));
                Assert.That(slot.meshData.umaBones.Select(b => b.name), Does.Contain("Hips"));
            }
            foreach (var overlay in result.overlays)
            {
                Assert.That(overlay.material.materialType, Is.EqualTo(UMAMaterial.MaterialType.UseExistingMaterial));
                Assert.AreNotSame(material, overlay.material.material);
            }
            var boneDna = result.dnaGroups.SelectMany(g => g.dnaList).First(d => d.effects[0] is DNAEffect_BoneScale);
            Assert.That(boneDna.effects[0].GetMappedValue(boneDna.defaultValue), Is.Zero);
            var shapeDna = result.dnaGroups.SelectMany(g => g.dnaList).Single(d => d.effects[0] is DNAEffect_BlendShape);
            Assert.That(shapeDna.effects[0].GetMappedValue(shapeDna.defaultValue), Is.EqualTo(.25f).Within(.0001f));
            Assert.That(result.race.UnbakedShapesToInclude, Is.EquivalentTo(new[] { "Smile" }));
            AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(result.race));
            Assert.That(AssetDatabase.LoadAssetAtPath<RaceData>(AssetDatabase.GetAssetPath(result.race)).DNACollection.DNAGroups.Count, Is.EqualTo(2));
        }

        [Test]
        public void ClothingOnlyCreatesRegionRecipeWithoutModifyingExistingRace()
        {
            var race = ModelToRaceBuilder.Build(plan).race;
            string before = EditorJsonUtility.ToJson(race);
            plan.mode = ModelImportMode.ClothingForExistingRace; plan.targetRace = race;
            plan.parts.ForEach(p => { p.role = ModelPartRole.Clothing; p.outfit = "Shirt"; p.region = "Chest"; });
            var result = ModelToRaceBuilder.Build(plan);
            Assert.That(result.wardrobe.Count, Is.EqualTo(1)); Assert.That(result.wardrobe[0].wardrobeSlot, Is.EqualTo("Chest"));
            Assert.That(result.wardrobe[0].compatibleRaces, Does.Contain(race.raceName));
            Assert.That(EditorJsonUtility.ToJson(race), Is.EqualTo(before));
        }

        [Test]
        public void ClothingDnaAttachmentIsAdditiveAndExplicit()
        {
            var race = ModelToRaceBuilder.Build(plan).race; var original = race.DNACollection.DNAGroups.ToArray();
            plan.mode = ModelImportMode.ClothingForExistingRace; plan.targetRace = race; plan.parts.ForEach(p => p.role = ModelPartRole.Clothing);
            plan.attachDnaToExistingRace = true;
            var result = ModelToRaceBuilder.Build(plan);
            Assert.That(race.DNACollection.DNAGroups.Count, Is.EqualTo(4));
            CollectionAssert.AreEqual(original, race.DNACollection.DNAGroups.Take(2));
            Assert.AreSame(race, result.race);
        }

        [Test]
        public void FailureRollsBackNewFolderAndPreservesEarlierImport()
        {
            var first = ModelToRaceBuilder.Build(plan); var before = AssetDatabase.GetSubFolders(folder);
            Assert.Throws<InvalidOperationException>(() => ModelToRaceBuilder.Build(plan, (step, value) => { if (value > .7f) throw new InvalidOperationException("Injected failure"); }));
            CollectionAssert.AreEqual(before, AssetDatabase.GetSubFolders(folder));
            Assert.NotNull(first.race); Assert.True(AssetDatabase.IsValidFolder(first.folder));
        }

        [Test]
        public void UiTemplateHasScrollableContentAndPersistentNavigation()
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UMA/Core/Editor/Scripts/ModelToRace/ModelToRaceWindow.uxml");
            Assert.NotNull(template);
            var root = template.CloneTree(); Assert.NotNull(root.Q<ScrollView>("content"));
            Assert.NotNull(root.Q<Button>("back")); Assert.NotNull(root.Q<Button>("next")); Assert.NotNull(root.Q("steps"));
        }

        [Test]
        public void OneRendererCanSupplyBodyAndClothingInDifferentRegions()
        {
            Assert.That(plan.parts.Count, Is.EqualTo(2));
            plan.parts[1].role = ModelPartRole.Clothing; plan.parts[1].outfit = "Vest"; plan.parts[1].region = "Chest";
            var result = ModelToRaceBuilder.Build(plan);
            Assert.That(result.slots.Count, Is.EqualTo(2)); Assert.That(result.wardrobe.Count, Is.EqualTo(1));
            Assert.That(result.wardrobe[0].PackedLoad().slotsV3.Length, Is.EqualTo(1));
            Assert.That(((UMATextRecipe)result.race.baseRaceRecipe).PackedLoad().slotsV3.Length, Is.EqualTo(1));
        }

        [Test]
        public void ClothingRejectsSameNamesWithDifferentRestPose()
        {
            var race = ModelToRaceBuilder.Build(plan).race;
            plan.mode = ModelImportMode.ClothingForExistingRace; plan.targetRace = race; plan.parts.ForEach(p => p.role = ModelPartRole.Clothing);
            plan.parts[0].renderer.bones[1].localRotation = Quaternion.Euler(0, 25, 0);
            Assert.That(plan.Validate().Any(e => e.Contains("Rest pose differs")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RegisteredImportBuildsThroughStandardGeneratorAndDrivesShapeDna(bool humanoid)
        {
            Avatar sourceAvatar = humanoid ? AddHumanoidRig() : null;
            plan.registerAssets = true; plan.createAvatarPrefab = true; plan.name = "ImportTest_" + Guid.NewGuid().ToString("N");
            var result = ModelToRaceBuilder.Build(plan);
            Assert.NotNull(result.avatarPrefab);
            Assert.That(result.avatarPrefab.GetComponent<DynamicCharacterAvatar>().loadBlendShapes);
            Assert.AreSame(result.race, UMAAssetIndexer.Instance.GetRace(result.race.raceName));
            var go = new GameObject("Generated character test");
            var rendererSettings = ScriptableObject.CreateInstance<UMARendererAsset>();
            var textureMerge = ScriptableObject.CreateInstance<TextureMerge>();
            var gen = UMAAssetIndexer.Instance.generator;
            var combiner = gen.gameObject.AddComponent<UMADefaultMeshCombiner>(); gen.meshCombiner = combiner;
            gen.defaultRendererAsset = rendererSettings;
            gen.textureMerge = textureMerge;
            try
            {
                var data = go.AddComponent<UMAData>(); data.defaultRendererAsset = rendererSettings;
                data.umaRecipe = new UMAData.UMARecipe(); data.umaRecipe.SetRace(result.race);
                result.race.baseRaceRecipe.Load(data.umaRecipe);
                data.umaRecipe.dnaInstanceCollection = result.race.DNACollection.GetDefaultDNA(result.race);
                data.blendShapeSettings.ignoreBlendShapes = false; data.blendShapeSettings.loadAllFrames = true;
                data.isMeshDirty = data.isTextureDirty = data.isShapeDirty = true;
                Assert.True(gen.GenerateSingleUMA(data, false));
                Assert.That(data.GetRenderers().Length, Is.GreaterThan(0));
                var renderer = data.GetRenderers().First(r => r.sharedMesh.GetBlendShapeIndex("Smile") >= 0);
                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("Smile")), Is.EqualTo(25).Within(.001f));
                var dna = result.dnaGroups.SelectMany(g => g.dnaList).Single(d => d.effects[0] is DNAEffect_BlendShape effect && effect.BlendShapeName == "Smile");
                dna.PostApply(data, 1);
                Assert.That(renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex("Smile")), Is.EqualTo(100).Within(.001f));
                Assert.That(data.skeleton.GetBoneTransform(UMAUtils.StringToHash("Head")), Is.Not.Null);
                Assert.NotNull(data.animator.avatar); Assert.True(data.animator.avatar.isValid);
                Assert.That(data.animator.avatar.isHuman, Is.EqualTo(humanoid));
            }
            finally { Object.DestroyImmediate(go); Object.DestroyImmediate(rendererSettings); Object.DestroyImmediate(textureMerge); if (sourceAvatar != null) Object.DestroyImmediate(sourceAvatar); gen.defaultRendererAsset = null; gen.textureMerge = null; }
        }

        private Avatar AddHumanoidRig()
        {
            var human = new List<HumanBone>();
            Transform Bone(HumanBodyBones id, Transform parent, Vector3 position)
            {
                string name = id.ToString();
                var bone = source.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == name) ?? new GameObject(name).transform;
                bone.SetParent(parent, false); bone.localPosition = position;
                human.Add(new HumanBone { boneName = name, humanName = HumanTrait.BoneName[(int)id], limit = new HumanLimit { useDefaultValues = true } });
                return bone;
            }
            var hips = Bone(HumanBodyBones.Hips, source.transform, new Vector3(0, 1, 0));
            var spine = Bone(HumanBodyBones.Spine, hips, new Vector3(0, .2f, 0));
            var chest = Bone(HumanBodyBones.Chest, spine, new Vector3(0, .2f, 0));
            var neck = Bone(HumanBodyBones.Neck, chest, new Vector3(0, .15f, 0));
            Bone(HumanBodyBones.Head, neck, new Vector3(0, .1f, 0));
            var leftArm = Bone(HumanBodyBones.LeftUpperArm, chest, new Vector3(-.2f, .1f, 0));
            var leftForearm = Bone(HumanBodyBones.LeftLowerArm, leftArm, new Vector3(-.25f, 0, 0));
            Bone(HumanBodyBones.LeftHand, leftForearm, new Vector3(-.2f, 0, 0));
            var rightArm = Bone(HumanBodyBones.RightUpperArm, chest, new Vector3(.2f, .1f, 0));
            var rightForearm = Bone(HumanBodyBones.RightLowerArm, rightArm, new Vector3(.25f, 0, 0));
            Bone(HumanBodyBones.RightHand, rightForearm, new Vector3(.2f, 0, 0));
            var leftThigh = Bone(HumanBodyBones.LeftUpperLeg, hips, new Vector3(-.1f, -.1f, 0));
            var leftShin = Bone(HumanBodyBones.LeftLowerLeg, leftThigh, new Vector3(0, -.4f, 0));
            Bone(HumanBodyBones.LeftFoot, leftShin, new Vector3(0, -.4f, .05f));
            var rightThigh = Bone(HumanBodyBones.RightUpperLeg, hips, new Vector3(.1f, -.1f, 0));
            var rightShin = Bone(HumanBodyBones.RightLowerLeg, rightThigh, new Vector3(0, -.4f, 0));
            Bone(HumanBodyBones.RightFoot, rightShin, new Vector3(0, -.4f, .05f));
            var description = new HumanDescription
            {
                human = human.ToArray(), skeleton = source.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                armStretch = .05f, legStretch = .05f, lowerArmTwist = .5f, upperArmTwist = .5f, lowerLegTwist = .5f, upperLegTwist = .5f
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(source, description);
            Assert.That(avatar.isValid && avatar.isHuman, "Humanoid source fixture");
            source.AddComponent<Animator>().avatar = avatar;
            var renderer = plan.parts[0].renderer;
            mesh.bindposes = renderer.bones.Select(b => b.worldToLocalMatrix * renderer.localToWorldMatrix).ToArray();
            return avatar;
        }

        [Test]
        public void WizardConstructsAllFourPagesWithoutMissingControls()
        {
            var window = ScriptableObject.CreateInstance<ModelToRaceWindow>();
            try
            {
                typeof(ModelToRaceWindow).GetField("plan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(window, plan);
                window.CreateGUI();
                for (int page = 0; page < 4; page++)
                {
                    typeof(ModelToRaceWindow).GetField("page", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(window, page);
                    typeof(ModelToRaceWindow).GetMethod("Render", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(window, null);
                    Assert.That(window.rootVisualElement.Q<ScrollView>("content").childCount, Is.GreaterThan(0));
                    Assert.That(window.rootVisualElement.Q<Button>("next").enabledSelf, Is.True);
                }
            }
            finally { Object.DestroyImmediate(window); }
        }

        private static void AssertMatrix(Matrix4x4 expected, Matrix4x4 actual)
        { for (int i = 0; i < 16; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(.0001f), "Matrix entry " + i); }
    }
}
