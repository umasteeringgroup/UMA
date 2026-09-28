using System;
using System.Collections.Generic;
using System.Linq;
using UMA.CharacterSystem;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UMA.Editors.ModelToRace
{
    public sealed class ModelToRaceResult
    {
        public string folder;
        public RaceData race;
        public GameObject avatarPrefab;
        public readonly List<SlotDataAsset> slots = new List<SlotDataAsset>();
        public readonly List<OverlayDataAsset> overlays = new List<OverlayDataAsset>();
        public readonly List<UMAWardrobeRecipe> wardrobe = new List<UMAWardrobeRecipe>();
        public readonly List<DNAGroup> dnaGroups = new List<DNAGroup>();
    }

    /// <summary>Independent UMA import orchestration; extraction remains in the standard Slot Builder.</summary>
    public static class ModelToRaceBuilder
    {
        public static ModelToRaceResult Build(ModelToRacePlan plan, Action<string, float> progress = null)
        {
            plan.RefreshDNA();
            var errors = plan.Validate();
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            var result = new ModelToRaceResult();
            var indexed = new List<Object>();
            var created = new List<Object>();
            var index = plan.registerAssets ? UMAAssetIndexer.Instance : null;
            if (plan.registerAssets && index == null) throw new InvalidOperationException("Create an UMA Global Library before registering assets, or turn registration off.");
            string oldRace = !plan.IsNewRace && plan.attachDnaToExistingRace ? EditorJsonUtility.ToJson(plan.targetRace) : null;
            // One NEW folder is the entire transaction boundary. Never update or replace an existing import.
            result.folder = AssetDatabase.GenerateUniqueAssetPath(plan.outputParent + "/" + plan.name);
            string prefix = System.IO.Path.GetFileName(result.folder).Replace(' ', '_');
            if (plan.IsNewRace && index != null && index.GetAssetItem(typeof(RaceData), prefix) != null)
                throw new InvalidOperationException("A race named '" + prefix + "' is already registered. Choose another import name.");
            string folderGuid = AssetDatabase.CreateFolder(plan.outputParent, System.IO.Path.GetFileName(result.folder));
            if (string.IsNullOrEmpty(folderGuid)) throw new InvalidOperationException("Could not create the output folder.");
            try
            {
                using (var stage = new StagingRig(plan))
                {
                    result.race = plan.IsNewRace ? CreateRace(plan, stage, prefix, result.folder, created) : plan.targetRace;
                    var materialMap = new Dictionary<Material, UMAMaterial>();
                    var partSlots = new Dictionary<ModelPart, List<SlotData>>();
                    int count = 0;
                    foreach (var part in plan.IncludedParts)
                    {
                        progress?.Invoke("Extracting " + part.name, 0.1f + 0.55f * count++ / plan.IncludedParts.Count());
                        var renderer = stage.CreateRenderer(part);
                        try
                        {
                            var firstMaterial = GetMaterial(renderer.sharedMaterials.First(m => m != null), prefix, result.folder, materialMap, created);
                            var built = UMASlotProcessingUtil.CreateSlotData(new SlotBuilderParameters
                            {
                                slotMesh = renderer, rootBone = "Global", keepAllBones = false, keepList = stage.BoneNames.ToList(),
                                assetName = prefix + "_" + part.name, slotName = prefix + "_" + part.name,
                                slotFolder = result.folder, assetFolder = result.folder, useRootFolder = true,
                                material = firstMaterial, createOverlays = true, appendTypeToName = true,
                                batchMode = true, binarySerialization = true, rotation = Quaternion.identity,
                                addToGlobalLibrary = false, generateSlotLods = false
                            });
                            if (built == null || built.Slots == null || built.Slots.Count == 0) throw new InvalidOperationException(part.name + ": Slot Builder produced no slots.");
                            var list = new List<SlotData>();
                            partSlots.Add(part, list);
                            foreach (var slot in built.Slots)
                            {
                                if (!built.SlotToOverlay.TryGetValue(slot, out var overlay) || overlay == null) throw new InvalidOperationException("Slot Builder did not create an overlay for " + slot.name);
                                overlay.material = GetMaterial(renderer.sharedMaterials[slot.sourceSubmeshIndex], prefix, result.folder, materialMap, created);
                                // Existing-material mode deliberately has no atlas channels. Its copied material retains all shader properties.
                                overlay.textureList = Array.Empty<Texture>();
                                overlay.overlayBlend = Array.Empty<OverlayDataAsset.OverlayBlend>();
                                slot.meshData.bones = null; slot.meshData.rootBone = null;
                                EditorUtility.SetDirty(slot); EditorUtility.SetDirty(overlay);
                                result.slots.Add(slot); result.overlays.Add(overlay);
                                created.Add(slot); created.Add(overlay);
                                var data = new SlotData(slot);
                                data.AddOverlay(new OverlayData(overlay));
                                list.Add(data);
                            }
                        }
                        finally { stage.DestroyRenderer(renderer); }
                    }

                    progress?.Invoke("Creating DNA and recipes", 0.75f);
                    CreateDNA(plan, prefix, result, created);
                    if (plan.IsNewRace || plan.attachDnaToExistingRace)
                    {
                        if (!plan.IsNewRace) Undo.RecordObject(result.race, "Attach imported clothing DNA");
                        foreach (var group in result.dnaGroups) result.race.DNACollection.DNAGroups.Add(group);
                        foreach (var shape in plan.shapes.Where(s => s.include))
                            if (!result.race.UnbakedShapesToInclude.Contains(shape.name)) result.race.UnbakedShapesToInclude.Add(shape.name);
                        result.race.DNACollection.Reset();
                        EditorUtility.SetDirty(result.race);
                    }
                    if (plan.IsNewRace)
                    {
                        var recipe = Create<UMATextRecipe>(prefix + "_Base", result.folder, created);
                        recipe.recipeType = "Standard";
                        recipe.Save(Recipe(result.race, partSlots.Where(p => p.Key.role == ModelPartRole.Body).SelectMany(p => p.Value)));
                        result.race.baseRaceRecipe = recipe;
                        EditorUtility.SetDirty(recipe);
                    }
                    foreach (var outfit in partSlots.Where(p => p.Key.role == ModelPartRole.Clothing).GroupBy(p => p.Key.outfit))
                    {
                        var recipe = Create<UMAWardrobeRecipe>(prefix + "_" + outfit.Key + "_Wardrobe", result.folder, created);
                        recipe.DisplayValue = outfit.Key;
                        recipe.wardrobeSlot = outfit.First().Key.region;
                        recipe.compatibleRaces = new List<string> { result.race.raceName };
                        recipe.Save(Recipe(result.race, outfit.SelectMany(p => p.Value)));
                        EditorUtility.SetDirty(recipe);
                        result.wardrobe.Add(recipe);
                    }
                    if (index != null)
                    {
                        foreach (var asset in created.Where(a => index.IsIndexedType(a.GetType())))
                        {
                            var existing = index.GetAssetItem(asset.GetType(), AssetItem.GetEvilName(asset));
                            if (existing != null && existing._Path != AssetDatabase.GetAssetPath(asset))
                                throw new InvalidOperationException("An asset with this name is already registered: " + asset.name + ". Choose a different import name.");
                            if (existing == null && !index.EvilAddAsset(asset.GetType(), asset)) throw new InvalidOperationException("Could not register " + asset.name);
                            indexed.Add(asset);
                        }
                        index.ForceSave();
                    }
                    if (plan.createAvatarPrefab) result.avatarPrefab = CreateAvatar(plan, result, prefix);
                    if (plan.IsNewRace || plan.attachDnaToExistingRace) EditorUtility.SetDirty(result.race);
                    foreach (var asset in created) AssetDatabase.SaveAssetIfDirty(asset);
                    if (plan.IsNewRace || plan.attachDnaToExistingRace) AssetDatabase.SaveAssetIfDirty(result.race);
                    progress?.Invoke("Complete", 1);
                    return result;
                }
            }
            catch
            {
                if (oldRace != null)
                {
                    EditorJsonUtility.FromJsonOverwrite(oldRace, plan.targetRace);
                    plan.targetRace.DNACollection?.Reset();
                    EditorUtility.SetDirty(plan.targetRace);
                    AssetDatabase.SaveAssetIfDirty(plan.targetRace);
                }
                if (index != null)
                {
                    foreach (var asset in indexed) if (asset != null) index.RemoveAsset(asset.GetType(), AssetItem.GetEvilName(asset), false);
                    index.RemoveAssetsComplete();
                }
                AssetDatabase.DeleteAsset(result.folder);
                throw;
            }
        }

        private static T Create<T>(string name, string folder, List<Object> created) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>(); asset.name = name;
            AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(folder + "/" + name + ".asset"));
            created.Add(asset); return asset;
        }

        private static UMAMaterial GetMaterial(Material source, string prefix, string folder, Dictionary<Material, UMAMaterial> map, List<Object> created)
        {
            if (map.TryGetValue(source, out var existing)) return existing;
            string name = prefix + "_Material_" + map.Count + "_" + ModelToRacePlan.SafeName(source.name);
            var copy = new Material(source) { name = name };
            AssetDatabase.CreateAsset(copy, folder + "/" + name + ".mat"); created.Add(copy);
            var material = Create<UMAMaterial>(name + "_UMA", folder, created);
            material.material = copy;
            material.materialType = UMAMaterial.MaterialType.UseExistingMaterial;
            material.channels = Array.Empty<UMAMaterial.MaterialChannel>();
            EditorUtility.SetDirty(material); map.Add(source, material); return material;
        }

        private static RaceData CreateRace(ModelToRacePlan plan, StagingRig stage, string prefix, string folder, List<Object> created)
        {
            var race = Create<RaceData>(prefix, folder, created);
            race.useNewDNA = true; race.FixupRotations = false;
            race.wardrobeSlots = plan.regions.Distinct().ToList();
            race.KeepBoneNames = stage.BoneNames.ToList();
            var pose = Create<UmaTPose>(prefix + "_TPose", folder, created);
            var animator = plan.source.GetComponentInChildren<Animator>(true);
            bool human = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
            if (human) { pose.ReadFromHumanDescription(animator.avatar.humanDescription); pose.DeSerialize(); }
            else pose.humanInfo = Array.Empty<HumanBone>();
            pose.boneInfo = stage.Skeleton;
            pose.Serialize();
            race.TPose = pose;
            race.umaTarget = human ? RaceData.UMATarget.Humanoid : RaceData.UMATarget.Generic;
            race.genericRootMotionTransformName = "Global";
            EditorUtility.SetDirty(pose); EditorUtility.SetDirty(race);
            return race;
        }

        private static void CreateDNA(ModelToRacePlan plan, string prefix, ModelToRaceResult result, List<Object> created)
        {
            if (plan.boneDNA && plan.bones.Any(b => b.include))
            {
                var group = Create<DNAGroup>(prefix + "_BoneDNA", result.folder, created); group.DNAArea = "Body proportions";
                int id = 0;
                foreach (var bone in plan.bones.Where(b => b.include))
                {
                    var dna = Create<DNA>(prefix + "_Bone_" + id++ + "_" + ModelToRacePlan.SafeName(bone.bone), result.folder, created);
                    dna.displayName = bone.bone + " size";
                    dna.description = "Local-axis scale on " + bone.bone + ". 0.5 is the imported rest pose. Children inherit this scale.";
                    dna.defaultValue = 0.5f;
                    dna.effects.Add(new DNAEffect_BoneScale { BoneName = bone.bone, ScaleFactor = bone.scaleRange, EffectName = dna.displayName, minMapping = -1, maxMapping = 1 });
                    group.dnaList.Add(dna); EditorUtility.SetDirty(dna);
                }
                result.dnaGroups.Add(group); EditorUtility.SetDirty(group);
            }
            if (plan.blendshapeDNA && plan.shapes.Any(s => s.include))
            {
                var group = Create<DNAGroup>(prefix + "_BlendshapeDNA", result.folder, created); group.DNAArea = "Blendshapes";
                int id = 0;
                foreach (var shape in plan.shapes.Where(s => s.include))
                {
                    var dna = Create<DNA>(prefix + "_Shape_" + id++ + "_" + ModelToRacePlan.SafeName(shape.name), result.folder, created);
                    dna.displayName = shape.name; dna.description = "Imported blendshape: " + shape.name;
                    dna.defaultValue = Mathf.InverseLerp(shape.minimum, shape.maximum, shape.defaultWeight);
                    dna.effects.Add(new DNAEffect_BlendShape { BlendShapeName = shape.name, EffectName = shape.name, minMapping = shape.minimum / 100, maxMapping = shape.maximum / 100 });
                    group.dnaList.Add(dna); EditorUtility.SetDirty(dna);
                }
                result.dnaGroups.Add(group); EditorUtility.SetDirty(group);
            }
        }

        private static UMAData.UMARecipe Recipe(RaceData race, IEnumerable<SlotData> slots)
        {
            var recipe = new UMAData.UMARecipe(); recipe.SetRace(race);
            if (race.useNewDNA) recipe.dnaInstanceCollection = race.DNACollection.GetDefaultDNA(race);
            int i = 0; foreach (var slot in slots) recipe.SetSlot(i++, slot);
            return recipe;
        }

        private static GameObject CreateAvatar(ModelToRacePlan plan, ModelToRaceResult result, string prefix)
        {
            var go = new GameObject(prefix + "_Avatar"); go.SetActive(false);
            try
            {
                var avatar = go.AddComponent<DynamicCharacterAvatar>();
                avatar.activeRace.name = result.race.raceName; avatar.activeRace.data = result.race;
                avatar.loadBlendShapes = plan.shapes.Any(s => s.include);
                avatar.raceAnimationControllers.defaultAnimationController = plan.animatorController;
                var regions = new HashSet<string>();
                foreach (var recipe in result.wardrobe)
                {
                    bool equip = plan.IncludedParts.Any(p => p.role == ModelPartRole.Clothing && p.outfit == recipe.DisplayValue && p.equip);
                    var item = new DynamicCharacterAvatar.WardrobeRecipeListItem(recipe) { _enabledInDefaultWardrobe = equip && regions.Add(recipe.wardrobeSlot) };
                    avatar.preloadWardrobeRecipes.recipes.Add(item);
                }
                go.SetActive(true);
                return PrefabUtility.SaveAsPrefabAsset(go, result.folder + "/" + prefix + "_Avatar.prefab");
            }
            finally { Object.DestroyImmediate(go); }
        }

        /// <summary>Transform-only copy: no scripts, animators or source-prefab callbacks are executed.</summary>
        public sealed class StagingRig : IDisposable
        {
            private readonly ModelToRacePlan plan;
            private readonly GameObject container;
            private readonly Transform global;
            private readonly Dictionary<Transform, Transform> bones = new Dictionary<Transform, Transform>();
            public IEnumerable<string> BoneNames => bones.Values.Select(b => b.name).Distinct();
            public SkeletonBone[] Skeleton => container.GetComponentsInChildren<Transform>(true).Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();

            public StagingRig(ModelToRacePlan plan)
            {
                this.plan = plan;
                container = new GameObject("Root") { hideFlags = HideFlags.HideAndDontSave }; container.SetActive(false);
                global = new GameObject("Global").transform; global.SetParent(container.transform, false);
                if (!plan.IsNewRace && plan.targetRace != null && plan.targetRace.FixupRotations)
                    global.localRotation = Quaternion.Euler(270, 0, 0) * Quaternion.Euler(90, 90, 0);
                try
                {
                    foreach (var bone in plan.IncludedParts.SelectMany(p => p.renderer.bones)) CopyBone(bone);
                    var animator = plan.source.GetComponentInChildren<Animator>(true);
                    if (animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
                        for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                        {
                            var bone = animator.GetBoneTransform((HumanBodyBones)i);
                            if (bone != null && bone.IsChildOf(plan.source.transform)) CopyBone(bone);
                        }
                }
                catch { Dispose(); throw; }
            }

            private Transform CopyBone(Transform source)
            {
                if (bones.TryGetValue(source, out var found)) return found;
                if (source == plan.source.transform && !plan.IncludedParts.Any(p => p.renderer.bones.Contains(source)))
                { bones.Add(source, global); return global; }
                if (source.name == "Global") { bones.Add(source, global); return global; }
                var parent = source == plan.source.transform ? global : CopyBone(source.parent);
                var target = new GameObject(source.name).transform; target.SetParent(parent, false);
                bones.Add(source, target);
                // Canonical model-local coordinates, including flattened source Global/helper ancestors.
                Matrix4x4 relative = parent.worldToLocalMatrix * plan.source.transform.worldToLocalMatrix * source.localToWorldMatrix;
                target.localPosition = relative.GetColumn(3); target.localRotation = relative.rotation; target.localScale = relative.lossyScale;
                var reconstructed = Matrix4x4.TRS(target.localPosition, target.localRotation, target.localScale);
                if (!MatricesMatch(relative, reconstructed)) throw new InvalidOperationException("Bone '" + source.name + "' has a mirrored/sheared rest transform. Apply rig transforms in the model authoring tool before importing.");
                return target;
            }

            public IEnumerable<string> ValidateTargetPose(RaceData race)
            {
                race.TPose.DeSerialize();
                var rest = race.TPose.boneInfo.GroupBy(b => b.name).ToDictionary(g => g.Key, g => g.First());
                foreach (var bone in bones.Values.Distinct().Where(b => b != global))
                {
                    if (!rest.TryGetValue(bone.name, out var expected)) continue;
                    if (!MatricesMatch(Matrix4x4.TRS(expected.position, expected.rotation, expected.scale), Matrix4x4.TRS(bone.localPosition, bone.localRotation, bone.localScale)))
                        yield return "Rest pose differs for '" + bone.name + "'. Use clothing exported against the target race's rest rig (not a posed/scaled instance), or transfer weights with Scene Mesh Slot Builder.";
                }
            }

            private static bool MatricesMatch(Matrix4x4 a, Matrix4x4 b)
            {
                for (int i = 0; i < 16; i++) if (!float.IsFinite(a[i]) || !float.IsFinite(b[i]) || Mathf.Abs(a[i] - b[i]) > .0002f * Mathf.Max(1, Mathf.Abs(a[i]))) return false;
                return true;
            }

            public SkinnedMeshRenderer CreateRenderer(ModelPart part)
            {
                var go = new GameObject("ImportMesh_" + Guid.NewGuid().ToString("N")); go.transform.SetParent(container.transform, false);
                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = ConvertMesh(part.renderer.sharedMesh, plan.source.transform.worldToLocalMatrix * part.renderer.localToWorldMatrix,
                    new HashSet<string>(plan.shapes.Where(s => s.include).Select(s => s.name)));
                if (part.sourceSubmesh >= 0)
                    for (int s = 0; s < renderer.sharedMesh.subMeshCount; s++)
                        if (s != part.sourceSubmesh) renderer.sharedMesh.SetTriangles(Array.Empty<int>(), s, false);
                var rendererBones = part.renderer.bones.Select(b => bones[b]).ToList();
                var bindposes = renderer.sharedMesh.bindposes.ToList();
                // A source Global may have been folded into its children. Keep its rest skinning exact too.
                for (int i = 0; i < rendererBones.Count; i++)
                    bindposes[i] = rendererBones[i].worldToLocalMatrix * plan.source.transform.worldToLocalMatrix * part.renderer.bones[i].localToWorldMatrix * bindposes[i];
                // Keep non-weighted humanoid/animation bones, even if only another part uses them.
                foreach (var bone in bones.Values.Distinct())
                    if (!rendererBones.Contains(bone)) { rendererBones.Add(bone); bindposes.Add(bone.worldToLocalMatrix); }
                renderer.sharedMesh.bindposes = bindposes.ToArray();
                renderer.bones = rendererBones.ToArray(); renderer.rootBone = global;
                renderer.sharedMaterials = part.renderer.sharedMaterials;
                return renderer;
            }

            public void DestroyRenderer(SkinnedMeshRenderer renderer)
            {
                Object.DestroyImmediate(renderer.sharedMesh); Object.DestroyImmediate(renderer.gameObject);
            }

            public void Dispose() { Object.DestroyImmediate(container); }
        }

        public static Mesh ConvertMesh(Mesh source, Matrix4x4 transform, HashSet<string> includedShapes)
        {
            var mesh = Object.Instantiate(source); mesh.name = source.name + "_Import";
            var vertices = source.vertices; var normals = source.normals; var tangents = source.tangents;
            var normalMatrix = transform.inverse.transpose;
            bool mirrored = transform.determinant < 0;
            mesh.vertices = vertices.Select(transform.MultiplyPoint3x4).ToArray();
            if (normals.Length == source.vertexCount) mesh.normals = normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToArray();
            if (tangents.Length == source.vertexCount)
                mesh.tangents = tangents.Select(t => { Vector3 v = transform.MultiplyVector(t).normalized; return new Vector4(v.x, v.y, v.z, t.w * (mirrored ? -1 : 1)); }).ToArray();
            mesh.bindposes = source.bindposes.Select(b => b * transform.inverse).ToArray();
            if (mirrored)
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    var triangles = mesh.GetTriangles(s);
                    for (int i = 0; i < triangles.Length; i += 3) (triangles[i], triangles[i + 1]) = (triangles[i + 1], triangles[i]);
                    mesh.SetTriangles(triangles, s, false);
                }
            mesh.ClearBlendShapes();
            var dv = new Vector3[source.vertexCount]; var dn = new Vector3[source.vertexCount]; var dt = new Vector3[source.vertexCount];
            for (int s = 0; s < source.blendShapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                if (!includedShapes.Contains(name)) continue;
                for (int f = 0; f < source.GetBlendShapeFrameCount(s); f++)
                {
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    for (int i = 0; i < dv.Length; i++)
                    {
                        dv[i] = transform.MultiplyVector(dv[i]);
                        dn[i] = normals.Length == dv.Length ? normalMatrix.MultiplyVector(normals[i] + dn[i]).normalized - normalMatrix.MultiplyVector(normals[i]).normalized : Vector3.zero;
                        dt[i] = tangents.Length == dv.Length ? transform.MultiplyVector((Vector3)tangents[i] + dt[i]).normalized - transform.MultiplyVector(tangents[i]).normalized : Vector3.zero;
                    }
                    mesh.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(s, f), dv, dn, dt);
                }
            }
            mesh.RecalculateBounds(); return mesh;
        }
    }
}
