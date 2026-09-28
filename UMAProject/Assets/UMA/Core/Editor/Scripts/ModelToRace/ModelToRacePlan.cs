using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UMA.Editors.ModelToRace
{
    public enum ModelImportMode { NewRace, ClothingForExistingRace }
    public enum ModelPartRole { Body, Clothing, Skip }

    [Serializable]
    public sealed class ModelPart
    {
        public SkinnedMeshRenderer renderer;
        public int sourceSubmesh = -1; // -1 means the whole renderer, for programmatic callers.
        public ModelPartRole role;
        public string name;
        public string outfit;
        public string region = "Chest";
        public bool equip = true;
    }

    [Serializable]
    public sealed class ModelShape
    {
        public string name;
        public bool include = true;
        public float minimum;
        public float maximum = 100;
        public float defaultWeight;
    }

    [Serializable]
    public sealed class ModelBoneControl
    {
        public string bone;
        public bool include = true;
        public Vector3 scaleRange = Vector3.one * 0.2f;
    }

    /// <summary>Editor-only import choices. Source objects are always read-only.</summary>
    [Serializable]
    public sealed class ModelToRacePlan
    {
        public GameObject source;
        public ModelImportMode mode;
        public RaceData targetRace;
        public string name = "MyCharacter";
        public string outputParent = "Assets/UMAProjectData";
        public bool boneDNA = true;
        public bool blendshapeDNA = true;
        public bool attachDnaToExistingRace;
        public bool registerAssets = true;
        public bool createAvatarPrefab = true;
        public RuntimeAnimatorController animatorController;
        public List<ModelPart> parts = new List<ModelPart>();
        public List<ModelShape> shapes = new List<ModelShape>();
        public List<ModelBoneControl> bones = new List<ModelBoneControl>();
        public List<string> regions = new List<string> { "None", "Face", "Hair", "Complexion", "Eyebrows", "Beard", "Ears", "Helmet", "Shoulders", "Chest", "Arms", "Hands", "Waist", "Legs", "Feet" };

        public IEnumerable<ModelPart> IncludedParts => parts.Where(p => p.renderer != null && p.role != ModelPartRole.Skip);
        public bool IsNewRace => mode == ModelImportMode.NewRace;
        public IEnumerable<string> RegionChoices => IsNewRace ? regions : targetRace != null ? targetRace.wardrobeSlots : Enumerable.Empty<string>();

        public void Scan()
        {
            parts.Clear(); shapes.Clear(); bones.Clear();
            if (source == null) return;
            name = SafeName(source.name);
            var animator = source.GetComponentInChildren<Animator>(true);
            animatorController = animator != null ? animator.runtimeAnimatorController : null;
            var lowerLods = new HashSet<Renderer>();
            foreach (var lod in source.GetComponentsInChildren<LODGroup>(true))
                foreach (var level in lod.GetLODs().Skip(1))
                    foreach (var renderer in level.renderers) lowerLods.Add(renderer);
            int index = 0;
            foreach (var renderer in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int rendererIndex = index++;
                int count = renderer.sharedMesh != null ? renderer.sharedMesh.subMeshCount : 1;
                for (int submesh = 0; submesh < count; submesh++)
                {
                    if (renderer.sharedMesh != null && renderer.sharedMesh.GetIndexCount(submesh) == 0) continue;
                    parts.Add(new ModelPart { renderer = renderer, sourceSubmesh = submesh, name = SafeName(renderer.name) + "_" + rendererIndex + "_" + submesh, outfit = SafeName(renderer.name),
                        role = lowerLods.Contains(renderer) ? ModelPartRole.Skip : IsNewRace ? ModelPartRole.Body : ModelPartRole.Clothing });
                }
            }
            RefreshDNA();
        }

        // Rebuild from INCLUDED parts, retaining edits. Shared shape names intentionally share one DNA slider.
        public void RefreshDNA()
        {
            var oldShapes = shapes.ToDictionary(s => s.name, StringComparer.Ordinal);
            var oldBones = bones.ToDictionary(b => b.bone, StringComparer.Ordinal);
            shapes.Clear(); bones.Clear();
            var seenShapes = new HashSet<string>(StringComparer.Ordinal);
            var seenBones = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in IncludedParts)
            {
                var mesh = part.renderer.sharedMesh;
                if (mesh == null) continue;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string shapeName = mesh.GetBlendShapeName(i);
                    if (!seenShapes.Add(shapeName)) continue;
                    if (oldShapes.TryGetValue(shapeName, out var previous)) { shapes.Add(previous); continue; }
                    float min = 0, max = 0;
                    foreach (var item in IncludedParts)
                    {
                        var other = item.renderer.sharedMesh;
                        if (other == null) continue;
                        int shapeIndex = other.GetBlendShapeIndex(shapeName);
                        if (shapeIndex < 0) continue;
                        for (int f = 0; f < other.GetBlendShapeFrameCount(shapeIndex); f++)
                        {
                            float weight = other.GetBlendShapeFrameWeight(shapeIndex, f);
                            min = Mathf.Min(min, weight); max = Mathf.Max(max, weight);
                        }
                    }
                    float defaultWeight = part.renderer.GetBlendShapeWeight(i);
                    min = Mathf.Min(min, defaultWeight); max = Mathf.Max(max, defaultWeight);
                    if (max <= min) max = min + 100;
                    shapes.Add(new ModelShape { name = shapeName, minimum = min, maximum = max, defaultWeight = defaultWeight });
                }
                foreach (var bone in part.renderer.bones)
                    if (bone != null && bone.name != "Global" && seenBones.Add(bone.name))
                        bones.Add(oldBones.TryGetValue(bone.name, out var previous) ? previous : new ModelBoneControl { bone = bone.name });
            }
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (source == null) { errors.Add("Choose the model root containing the skeleton and all its skinned parts."); return errors; }
            if (string.IsNullOrWhiteSpace(name) || SafeName(name) != name) errors.Add("Use a non-empty name containing letters, numbers, underscores or hyphens.");
            string parent = (outputParent ?? "").Replace('\\', '/').TrimEnd('/');
            if (parent != outputParent || !parent.StartsWith("Assets/", StringComparison.Ordinal) || parent.Split('/').Any(p => p == "." || p == "..") || !AssetDatabase.IsValidFolder(parent))
                errors.Add("Choose an existing output folder inside Assets (not Assets itself).");
            if (!IsNewRace && targetRace == null) errors.Add("Choose the existing race for the clothing.");
            if (!IsNewRace && targetRace != null && attachDnaToExistingRace && !targetRace.useNewDNA)
                errors.Add("The target uses legacy DNA. Leave 'Attach DNA to existing race' off; migrate that race separately before adding UMA 3 DNA.");
            if (!IncludedParts.Any()) errors.Add("Include at least one skinned part.");
            if (IsNewRace && !IncludedParts.Any(p => p.role == ModelPartRole.Body)) errors.Add("A new race needs at least one Body part.");
            if (!IsNewRace && IncludedParts.Any(p => p.role == ModelPartRole.Body)) errors.Add("Clothing-only imports cannot replace the target race's body. Set parts to Clothing or Skip.");
            var partNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skeletonNames = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (var part in IncludedParts)
            {
                if (string.IsNullOrWhiteSpace(part.name) || SafeName(part.name) != part.name || !partNames.Add(part.name)) errors.Add("Part names must be safe and unique: " + part.name);
                var renderer = part.renderer;
                if (!renderer.transform.IsChildOf(source.transform)) errors.Add(part.name + ": renderer is outside the source root. Rescan the model.");
                var mesh = renderer.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) { errors.Add(part.name + ": missing or empty mesh."); continue; }
                if (!mesh.isReadable) { errors.Add(part.name + ": enable Read/Write in the model import settings, then Apply."); continue; }
                if (part.sourceSubmesh >= mesh.subMeshCount || part.sourceSubmesh < -1) errors.Add(part.name + ": source submesh changed; rescan the model.");
                if (!Enumerable.Range(0, mesh.subMeshCount).Any(s => (part.sourceSubmesh < 0 || s == part.sourceSubmesh) && mesh.GetIndexCount(s) > 0)) errors.Add(part.name + ": no triangles selected.");
                if (renderer.bones.Length == 0 || mesh.bindposes.Length != renderer.bones.Length) errors.Add(part.name + ": missing skinning or bone/bind-pose count mismatch.");
                if (Mathf.Abs((source.transform.worldToLocalMatrix * renderer.localToWorldMatrix).determinant) < 0.000001f) errors.Add(part.name + ": zero scale cannot be converted.");
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (part.sourceSubmesh >= 0 && part.sourceSubmesh != s) continue;
                    if (mesh.GetIndexCount(s) == 0) continue;
                    if (mesh.GetTopology(s) != MeshTopology.Triangles) errors.Add(part.name + ": only triangle submeshes are supported.");
                    if (s >= renderer.sharedMaterials.Length || renderer.sharedMaterials[s] == null) errors.Add(part.name + ": assign a material to submesh " + s + ".");
                }
                foreach (var bone in renderer.bones)
                {
                    if (bone == null || !bone.IsChildOf(source.transform)) { errors.Add(part.name + ": a bone is null or outside the chosen model root."); continue; }
                    for (var t = bone; t != null; t = t == source.transform ? null : t.parent)
                    {
                        if (skeletonNames.TryGetValue(t.name, out var found) && found != t) errors.Add("UMA identifies bones by name. Rename duplicate bone: " + t.name);
                        if (t.name == "Root" && (t != source.transform || renderer.bones.Contains(t))) errors.Add("Rename skeleton bone 'Root'; UMA reserves Root for the avatar container.");
                        skeletonNames[t.name] = t;
                    }
                }
                // Allocator.None views owned by Mesh; do not dispose or retain them across mesh changes.
                var weights = mesh.GetAllBoneWeights();
                var counts = mesh.GetBonesPerVertex();
                {
                    if (counts.Length != mesh.vertexCount || weights.Length == 0 || counts.Any(c => c == 0)) errors.Add(part.name + ": every vertex must be skinned. Transfer weights before importing.");
                    for (int i = 0; i < weights.Length; i++)
                        if (weights[i].boneIndex < 0 || weights[i].boneIndex >= renderer.bones.Length || !float.IsFinite(weights[i].weight) || weights[i].weight < 0)
                        { errors.Add(part.name + ": invalid skin weights/bone index."); break; }
                }
                if (part.role == ModelPartRole.Clothing)
                {
                    if (string.IsNullOrWhiteSpace(part.outfit) || SafeName(part.outfit) != part.outfit) errors.Add(part.name + ": enter a valid outfit name.");
                    if (part.region == "None" || !RegionChoices.Contains(part.region)) errors.Add(part.name + ": select a wardrobe region supported by the race.");
                }
            }
            foreach (var outfit in IncludedParts.Where(p => p.role == ModelPartRole.Clothing).GroupBy(p => p.outfit))
                if (outfit.Select(p => p.region).Distinct().Count() > 1) errors.Add(outfit.Key + ": all pieces of an outfit must use the same wardrobe region.");
            foreach (var control in bones.Where(b => boneDNA && b.include))
                if (!Finite(control.scaleRange) || Mathf.Max(Mathf.Abs(control.scaleRange.x), Mathf.Abs(control.scaleRange.y), Mathf.Abs(control.scaleRange.z)) >= 1)
                    errors.Add(control.bone + ": each bone scale range must be between -1 and 1 (exclusive) to avoid zero/negative scales.");
            foreach (var shape in shapes.Where(s => s.include))
                if (!float.IsFinite(shape.minimum) || !float.IsFinite(shape.maximum) || !float.IsFinite(shape.defaultWeight) || shape.minimum >= shape.maximum || shape.defaultWeight < shape.minimum || shape.defaultWeight > shape.maximum)
                    errors.Add(shape.name + ": check minimum, maximum and default weights.");
            if (!IsNewRace && targetRace != null)
            {
                if (targetRace.TPose == null || targetRace.TPose.serializedChunk == null || targetRace.TPose.serializedChunk.Length == 0) errors.Add("Target race has no saved TPose to validate clothing bone names against.");
                else
                {
                    targetRace.TPose.DeSerialize();
                    var names = new HashSet<string>(targetRace.TPose.boneInfo.Select(b => b.name));
                    foreach (var bone in IncludedParts.SelectMany(p => p.renderer.bones).Where(b => b != null).Distinct())
                        if (bone.name != "Global" && !names.Contains(bone.name)) errors.Add("Target rig has no bone '" + bone.name + "'. Use Scene Mesh Slot Builder to transfer weights first; this wizard does not retarget unrelated rigs.");
                    if (errors.Count == 0)
                    {
                        try { using (var stage = new ModelToRaceBuilder.StagingRig(this)) errors.AddRange(stage.ValidateTargetPose(targetRace)); }
                        catch (InvalidOperationException e) { errors.Add(e.Message); }
                    }
                }
            }
            return errors.Distinct().ToList();
        }

        public static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        public static string SafeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Unnamed";
            return new string(value.Trim().Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
        }
    }
}
