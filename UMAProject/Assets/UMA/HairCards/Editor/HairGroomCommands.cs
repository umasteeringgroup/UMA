using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.HairCards.Runtime;

namespace UMA.HairCards.Editor
{
    internal enum HairGuideBatchAction { Duplicate, Delete, Enable, Disable, Freeze, Unfreeze }

    // Dedicated map clipboard: immutable snapshots, independent of the OS text clipboard.
    // The workspace saves Serialize() in SessionState so domain reloads do not lose a cut.
    internal sealed class HairGrowthMapClipboard
    {
        [Serializable]
        private sealed class Payload
        {
            public int version = 1;
            public string sourceMeshId, topology, mapName;
            public float[] values;
        }

        private Payload payload;
        internal string Description => payload == null ? "Map clipboard is empty." :
            $"Clipboard: {payload.mapName} ({payload.values.Length:N0} vertices).";

        internal HairGrowthMapClipboard(string serialized = null)
        {
            if (string.IsNullOrEmpty(serialized)) return;
            try
            {
                Payload restored = JsonUtility.FromJson<Payload>(serialized);
                if (restored == null || restored.version != 1 || string.IsNullOrEmpty(restored.sourceMeshId) ||
                    string.IsNullOrEmpty(restored.topology) || restored.values == null || restored.values.Length == 0) return;
                foreach (float value in restored.values) if (!float.IsFinite(value)) return;
                payload = restored;
            }
            catch (ArgumentException) { /* An old/malformed clipboard is treated as empty. */ }
        }

        internal string Serialize() => payload == null ? string.Empty : JsonUtility.ToJson(payload);

        private static bool ValidateMap(HairGroomAsset groom, HairGroup group, HairGrowthMap map,
            bool writes, out string reason)
        {
            reason = null;
            if (groom == null || group == null || map == null || !groom.Groups.Contains(group) || !group.maps.Contains(map))
                reason = "Select a map in this groom.";
            else if (writes && (group.locked || map.locked)) reason = "Unlock the group and map before changing its values.";
            else if (groom.SourceMesh == null || !groom.SourceMesh.isReadable || string.IsNullOrEmpty(groom.SourceMeshId))
                reason = "A readable source mesh is required.";
            else if (map.values == null || map.values.Length == 0 || map.values.Length != groom.SourceVertexCount)
                reason = "Map vertex data does not match the source mesh. Repair the source binding first.";
            else if (!float.IsFinite(map.valueRange.x) || !float.IsFinite(map.valueRange.y))
                reason = "The map's value range must be finite.";
            return reason == null;
        }

        internal bool CanCopy(HairGroomAsset groom, HairGroup group, HairGrowthMap map, bool cut, out string reason)
            => ValidateMap(groom, group, map, cut, out reason);

        internal bool TryCopy(HairGroomAsset groom, HairGroup group, HairGrowthMap map, bool cut, out string message)
        {
            if (!CanCopy(groom, group, map, cut, out message)) return false;
            string topology = HairMeshUtility.ComputeTopologySignature(groom.SourceMesh);
            if (topology != groom.SourceTopologySignature)
            { message = "Source topology changed. Repair the source binding before copying map data."; return false; }
            foreach (float value in map.values)
                if (!float.IsFinite(value)) { message = "Map contains invalid values; the clipboard and map were not changed."; return false; }
            if (cut && !float.IsFinite(map.defaultValue))
            { message = "The map's default value must be finite before cutting."; return false; }
            Payload snapshot = new Payload { sourceMeshId = groom.SourceMeshId, topology = topology,
                mapName = map.name, values = (float[])map.values.Clone() };
            if (cut)
            {
                Undo.RecordObject(groom, $"Cut {map.name} Map Values");
                float reset = Mathf.Clamp(map.defaultValue, Mathf.Min(map.valueRange.x, map.valueRange.y), Mathf.Max(map.valueRange.x, map.valueRange.y));
                for (int i = 0; i < map.values.Length; i++) map.values[i] = reset;
                HairGroomCommands.Commit(groom);
            }
            payload = snapshot;
            message = cut ? $"Cut {map.name}; source values reset to the map default. Undo restores them." : $"Copied {map.name}.";
            return true;
        }

        // Cheap UI check. Validate live topology again only when the user actually pastes.
        internal bool CanPaste(HairGroomAsset groom, HairGroup group, HairGrowthMap map, out string reason)
        {
            if (!ValidateMap(groom, group, map, true, out reason)) return false;
            if (payload == null) reason = "Copy or cut a map first.";
            else if (payload.sourceMeshId != groom.SourceMeshId || payload.values.Length != map.values.Length ||
                payload.topology != groom.SourceTopologySignature)
                reason = "Paste requires the same source mesh identity and matching vertex topology.";
            return reason == null;
        }

        internal bool TryPaste(HairGroomAsset groom, HairGroup group, HairGrowthMap map, out string message)
        {
            if (!CanPaste(groom, group, map, out message)) return false;
            if (HairMeshUtility.ComputeTopologySignature(groom.SourceMesh) != payload.topology)
            { message = "Source topology changed since the copy/cut. Paste was cancelled without changing the map."; return false; }
            float minimum = Mathf.Min(map.valueRange.x, map.valueRange.y), maximum = Mathf.Max(map.valueRange.x, map.valueRange.y);
            float[] values = new float[payload.values.Length];
            for (int i = 0; i < values.Length; i++) values[i] = Mathf.Clamp(payload.values[i], minimum, maximum);
            Undo.RecordObject(groom, $"Paste Into {map.name} Map");
            map.values = values;
            HairGroomCommands.Commit(groom);
            message = $"Pasted {payload.mapName} into {map.name}. Values are clamped to the destination range; Undo restores the previous map.";
            return true;
        }
    }

    internal static class HairGroomCommands
    {
        internal static List<string> ApplyGuideBatch(HairGroomAsset groom, HairGroup group,
            IEnumerable<string> ids, HairGuideBatchAction action)
        {
            List<string> affected = new List<string>();
            if (groom == null || group == null || group.locked) return affected;
            HashSet<string> selection = new HashSet<string>(ids ?? Array.Empty<string>());
            Undo.RegisterCompleteObjectUndo(groom, action + " Hair Guides");
            foreach (HairGuide guide in group.guides.ToArray())
            {
                if (guide == null || !selection.Contains(guide.Id)) continue;
                if (action == HairGuideBatchAction.Duplicate)
                {
                    HairGuide copy = guide.Clone();
                    copy.name = guide.name + " Copy";
                    group.guides.Add(copy);
                    foreach (HairSculptLayer layer in group.sculptLayers)
                    {
                        HairGuideDelta delta = layer?.deltas?.Find(item => item != null && item.guideId == guide.Id);
                        if (delta != null) layer.deltas.Add(CloneDelta(delta, copy.Id));
                    }
                    affected.Add(copy.Id);
                    continue;
                }
                if (action == HairGuideBatchAction.Delete)
                {
                    group.guides.Remove(guide);
                    foreach (HairSculptLayer layer in group.sculptLayers)
                        layer?.deltas?.RemoveAll(delta => delta != null && delta.guideId == guide.Id);
                }
                else if (action == HairGuideBatchAction.Enable || action == HairGuideBatchAction.Disable)
                    guide.enabled = action == HairGuideBatchAction.Enable;
                else foreach (HairGuidePoint point in guide.points)
                    if (point != null) point.freeze = action == HairGuideBatchAction.Freeze ? 1f : 0f;
                affected.Add(guide.Id);
            }
            Commit(groom);
            return affected;
        }

        private static HairGuideDelta CloneDelta(HairGuideDelta source, string guideId) => new HairGuideDelta
        {
            guideId = guideId,
            positionOffsets = source.positionOffsets != null ? (Vector3[])source.positionOffsets.Clone() : Array.Empty<Vector3>(),
            widthOffsets = source.widthOffsets != null ? (float[])source.widthOffsets.Clone() : Array.Empty<float>(),
            rollOffsets = source.rollOffsets != null ? (float[])source.rollOffsets.Clone() : Array.Empty<float>()
        };

        internal static HairSculptLayer DuplicateLayer(HairGroomAsset groom, HairGroup group, HairSculptLayer source)
        {
            if (groom == null || group == null || group.locked || source == null || !group.sculptLayers.Contains(source)) return null;
            Undo.RecordObject(groom, "Duplicate Hair Sculpt Layer");
            HairSculptLayer copy = new HairSculptLayer { name = source.name + " Copy", opacity = source.opacity,
                blendMode = source.blendMode, visible = source.visible, maskMapId = source.maskMapId };
            copy.EnsureIntegrity();
            foreach (HairGuideDelta delta in source.deltas)
                if (delta != null) copy.deltas.Add(CloneDelta(delta, delta.guideId));
            foreach (HairModifierSettings modifier in source.modifiers)
                if (modifier != null) copy.modifiers.Add(modifier.Duplicate());
            group.sculptLayers.Insert(group.sculptLayers.IndexOf(source) + 1, copy);
            Commit(groom);
            return copy;
        }

        internal static bool EditStack(HairGroomAsset groom, HairGroup group, string id, bool layer, int direction, bool remove = false)
        {
            if (groom == null || group == null || group.locked) return false;
            HairSculptLayer owner = group.sculptLayers.Find(item => item != null && item.modifiers.Exists(modifier => modifier != null && modifier.Id == id));
            if (!layer && owner?.locked == true) return false;
            List<HairModifierSettings> modifiers = owner?.modifiers ?? group.modifiers;
            int index = layer ? group.sculptLayers.FindIndex(item => item != null && item.Id == id) : modifiers.FindIndex(item => item != null && item.Id == id);
            int count = layer ? group.sculptLayers.Count : modifiers.Count;
            if (index < 0 || (layer && group.sculptLayers[index].locked)) return false;
            int next = Mathf.Clamp(index + direction, 0, count - 1);
            if (!remove && index == next) return false;
            Undo.RecordObject(groom, remove ? "Remove Hair Stack Entry" : "Reorder Hair Stack");
            if (layer)
            {
                HairSculptLayer item = group.sculptLayers[index];
                group.sculptLayers.RemoveAt(index);
                if (!remove) group.sculptLayers.Insert(next, item);
            }
            else
            {
                HairModifierSettings item = modifiers[index];
                modifiers.RemoveAt(index);
                if (!remove) modifiers.Insert(next, item);
            }
            Commit(groom);
            return true;
        }

        internal static void MakeResourcesUnique(HairGroomAsset groom, HairGroup group, bool atlas)
        {
            if (groom == null || group == null || group.locked) return;
            string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(groom))?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || !(folder == "Assets" || folder.StartsWith("Assets/", StringComparison.Ordinal)) || !AssetDatabase.IsValidFolder(folder)) folder = "Assets";
            Undo.RecordObject(groom, "Make Hair Resource Unique");
            if (atlas && group.atlas != null)
            {
                HairAtlasProfileAsset source = group.atlas;
                HairAtlasProfileAsset copy = ScriptableObject.CreateInstance<HairAtlasProfileAsset>();
                copy.albedo = source.albedo; copy.normal = source.normal; copy.mask = source.mask; copy.material = source.material;
                List<string> selected = new List<string>();
                foreach (HairAtlasRegion region in source.regions)
                {
                    if (region == null) continue;
                    HairAtlasRegion added = copy.CreateRegion(region.name, region.uvRect, region.weight);
                    added.flipU = region.flipU; added.flipV = region.flipV; added.tags = region.tags != null ? (string[])region.tags.Clone() : Array.Empty<string>();
                    if (group.atlasRegionIds.Contains(region.Id)) selected.Add(added.Id);
                }
                SaveUniqueResource(copy, folder, source.name);
                group.atlas = copy;
                group.atlasRegionIds = selected;
            }
            else if (!atlas && group.profile != null)
            {
                HairCardProfileAsset source = group.profile;
                HairCardProfileAsset copy = ScriptableObject.CreateInstance<HairCardProfileAsset>();
                copy.Configure(source.Shape, source.DefaultWidth, source.TipWidth, source.SamplesPerCard, source.TubeSides, source.DoubleSided);
                copy.WidthAlongCard.keys = source.WidthAlongCard.keys;
                copy.WidthAlongCard.preWrapMode = source.WidthAlongCard.preWrapMode;
                copy.WidthAlongCard.postWrapMode = source.WidthAlongCard.postWrapMode;
                SaveUniqueResource(copy, folder, source.name);
                group.profile = copy;
            }
            Commit(groom);
        }

        private static void SaveUniqueResource(UnityEngine.Object copy, string folder, string sourceName)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + sourceName + " Unique.asset");
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            Undo.RegisterCreatedObjectUndo(copy, "Make Hair Resource Unique");
            AssetDatabase.SaveAssetIfDirty(copy);
        }

        public static HairGroup AddGroup(HairGroomAsset groom, HairGroupRole role, string groupName = null)
        {
            if (groom == null) return null;
            Undo.RecordObject(groom, "Add Hair Group");
            HairGroup group = groom.CreateGroup(groupName, role);
            Commit(groom);
            return group;
        }

        public static bool RemoveGroup(HairGroomAsset groom, string groupId)
        {
            HairGroup group = groom?.FindGroup(groupId);
            if (group == null || groom.Groups.Count <= 1) return false;
            if (!EditorUtility.DisplayDialog("Delete Hair Group",
                    $"Delete '{group.name}', its maps, guides, modifiers, and constraints?", "Delete", "Cancel"))
            {
                return false;
            }
            Undo.RecordObject(groom, "Delete Hair Group");
            groom.Groups.Remove(group);
            Commit(groom);
            return true;
        }

        public static HairGrowthMap EnsureMap(HairGroomAsset groom, HairGroup group, HairMapKind kind)
        {
            if (groom == null || group == null) return null;
            HairGrowthMap existing = group.FindMap(kind);
            if (existing != null) return existing;
            Undo.RecordObject(groom, "Add Growth Map");
            HairGrowthMap map = new HairGrowthMap
            {
                name = Nicify(kind),
                kind = kind,
                defaultValue = DefaultValue(kind)
            };
            map.EnsureIntegrity(groom.SourceVertexCount);
            group.maps.Add(map);
            Commit(groom);
            return map;
        }

        public static void FillMap(HairGroomAsset groom, HairGrowthMap map, float value)
        {
            if (groom == null || map == null || map.locked) return;
            Undo.RecordObject(groom, $"Fill {map.name}");
            map.EnsureIntegrity(groom.SourceVertexCount);
            float clamped = Mathf.Clamp(value, map.valueRange.x, map.valueRange.y);
            for (int i = 0; i < map.values.Length; i++) map.values[i] = clamped;
            Commit(groom);
        }

        public static void InvertMap(HairGroomAsset groom, HairGrowthMap map)
        {
            if (groom == null || map == null || map.locked) return;
            Undo.RecordObject(groom, $"Invert {map.name}");
            float minimum = map.valueRange.x;
            float maximum = map.valueRange.y;
            for (int i = 0; i < map.values.Length; i++)
            {
                map.values[i] = maximum - (map.values[i] - minimum);
            }
            Commit(groom);
        }

        public static void SmoothMap(HairGroomAsset groom, HairGrowthMap map, int iterations = 1)
        {
            if (groom == null || groom.SourceMesh == null || map == null || map.locked) return;
            Undo.RecordObject(groom, $"Smooth {map.name}");
            List<int>[] neighbors = BuildVertexNeighbors(groom.SourceMesh);
            float[] buffer = new float[map.values.Length];
            for (int iteration = 0; iteration < Mathf.Max(1, iterations); iteration++)
            {
                for (int vertex = 0; vertex < map.values.Length; vertex++)
                {
                    float total = map.values[vertex];
                    int count = 1;
                    List<int> adjacent = neighbors[vertex];
                    for (int i = 0; i < adjacent.Count; i++)
                    {
                        total += map.values[adjacent[i]];
                        count++;
                    }
                    buffer[vertex] = total / count;
                }
                Array.Copy(buffer, map.values, buffer.Length);
            }
            Commit(groom);
        }

        public static int AddGeneratedGuides(
            HairGroomAsset groom,
            HairGroup group,
            IReadOnlyList<HairGuide> generated,
            bool replaceGenerated = false,
            bool replaceAll = false)
        {
            if (groom == null || group == null || generated == null || group.locked || generated.Count == 0)
                return 0;
            Undo.RecordObject(groom, "Accept Generated Hair Guides");
            if (replaceAll) group.guides.Clear();
            else if (replaceGenerated)
                group.guides.RemoveAll(guide => guide != null && guide.name.StartsWith("Generated ", StringComparison.Ordinal));
            int added = 0;
            for (int i = 0; i < generated.Count; i++)
            {
                HairGuide guide = generated[i]?.Clone();
                if (guide == null) continue;
                guide.name = $"Generated {group.guides.Count + 1:000}";
                group.guides.Add(guide);
                added++;
            }
            groom.EnsureIntegrity();
            Commit(groom);
            return added;
        }

        public static void AddGuide(HairGroomAsset groom, HairGroup group, HairGuide guide)
        {
            if (groom == null || group == null || guide == null || group.locked) return;
            Undo.RecordObject(groom, "Add Hair Guide");
            guide.EnsureIntegrity(group.profile != null ? group.profile.DefaultWidth : 0.012f);
            group.guides.Add(guide);
            Commit(groom);
        }

        public static bool DeleteGuide(HairGroomAsset groom, string guideId)
        {
            HairGroup owner = null;
            HairGuide guide = groom != null ? groom.FindGuide(guideId, out owner) : null;
            if (guide == null || owner == null || owner.locked) return false;
            Undo.RecordObject(groom, "Delete Hair Guide");
            owner.guides.Remove(guide);
            for (int layerIndex = 0; layerIndex < owner.sculptLayers.Count; layerIndex++)
            {
                owner.sculptLayers[layerIndex]?.deltas?.RemoveAll(delta => delta != null && delta.guideId == guideId);
            }
            Commit(groom);
            return true;
        }

        public static HairSculptLayer AddSculptLayer(HairGroomAsset groom, HairGroup group, string layerName = null)
        {
            if (groom == null || group == null || group.locked) return null;
            Undo.RecordObject(groom, "Add Hair Sculpt Layer");
            HairSculptLayer layer = new HairSculptLayer
            {
                name = string.IsNullOrWhiteSpace(layerName) ? $"Sculpt Layer {group.sculptLayers.Count + 1}" : layerName
            };
            layer.EnsureIntegrity();
            group.sculptLayers.Add(layer);
            Commit(groom);
            return layer;
        }

        public static HairModifierSettings AddModifier(HairGroomAsset groom, HairGroup group,
            HairModifierType type, HairSculptLayer layer = null)
        {
            if (groom == null || group == null || group.locked || layer?.locked == true ||
                (layer != null && !group.sculptLayers.Contains(layer))) return null;
            Undo.RecordObject(groom, "Add Hair Modifier");
            HairModifierSettings modifier = new HairModifierSettings
            {
                name = Nicify(type),
                type = type,
                domain = HairModifierDomain.GuidesAndChildren,
                amount = DefaultModifierAmount(type),
                vector = type == HairModifierType.Part ? Vector3.right :
                    type == HairModifierType.Curl || type == HairModifierType.Wave ? new Vector3(2f, 0f, 0f) : Vector3.up
            };
            modifier.EnsureIntegrity();
            (layer?.modifiers ?? group.modifiers).Add(modifier);
            Commit(groom);
            return modifier;
        }

        internal static HairModifierSettings DuplicateModifier(HairGroomAsset groom, HairGroup group,
            HairSculptLayer layer, HairModifierSettings source)
        {
            if (groom == null || group == null || group.locked || layer == null || layer.locked ||
                !group.sculptLayers.Contains(layer) || source == null || !layer.modifiers.Contains(source)) return null;
            Undo.RecordObject(groom, "Duplicate Hair Modifier");
            HairModifierSettings copy = source.Duplicate();
            copy.name += " Copy";
            layer.modifiers.Insert(layer.modifiers.IndexOf(source) + 1, copy);
            Commit(groom);
            return copy;
        }

        // Editor-only, explicit migration: never mutate ownership during evaluation or repaint.
        internal static HairSculptLayer ImportLegacyModifiers(HairGroomAsset groom, HairGroup group)
        {
            if (groom == null || group == null || group.modifiers == null || group.modifiers.Count == 0) return null;
            Undo.RecordObject(groom, "Organize Hair Modifiers Into Layer");
            HairSculptLayer layer = new HairSculptLayer { name = "Imported Modifiers" };
            layer.EnsureIntegrity();
            layer.modifiers.AddRange(group.modifiers);
            group.modifiers.Clear();
            group.sculptLayers.Add(layer);
            Commit(groom);
            return layer;
        }

        public static HairHelper AddHelper(HairGroomAsset groom, HairHelperType type, Vector3 position)
        {
            if (groom == null) return null;
            Undo.RecordObject(groom, "Add Hair Helper");
            HairHelper helper = new HairHelper
            {
                name = $"{Nicify(type)} {groom.SharedHelpers.Count + 1}",
                type = type,
                position = position,
                radius = 0.1f,
                size = Vector3.one * 0.2f
            };
            if (type == HairHelperType.CurveRail || type == HairHelperType.PartLine ||
                type == HairHelperType.BraidRail)
            {
                helper.points.Add(position);
                helper.points.Add(position + Vector3.up * 0.2f);
            }
            helper.EnsureIntegrity();
            groom.SharedHelpers.Add(helper);
            Commit(groom);
            return helper;
        }

        public static HairConstraintSettings AddConstraint(HairGroomAsset groom, HairGroup group,
            HairConstraintType type, HairHelper helper)
        {
            if (groom == null || group == null || helper == null) return null;
            Undo.RecordObject(groom, "Add Hair Constraint");
            HairConstraintSettings constraint = new HairConstraintSettings
            {
                name = $"{Nicify(type)} to {helper.name}",
                type = type,
                helperId = helper.Id,
                evaluation = HairConstraintEvaluation.Live
            };
            constraint.EnsureIntegrity();
            group.constraints.Add(constraint);
            Commit(groom);
            return constraint;
        }

        public static bool RemoveConstraint(HairGroomAsset groom, HairGroup group, string constraintId)
        {
            HairConstraintSettings constraint = group?.constraints?.Find(candidate =>
                candidate != null && candidate.Id == constraintId);
            if (groom == null || group == null || constraint == null || group.locked) return false;
            Undo.RecordObject(groom, "Remove Hair Constraint");
            group.constraints.Remove(constraint);
            Commit(groom);
            return true;
        }

        public static HairHelper BindSceneHelper(HairGroomAsset groom, GameObject target,
            HairHelperType type)
        {
            if (groom == null || target == null) return null;
            HairHelperId helperId = target.GetComponent<HairHelperId>();
            if (helperId == null) helperId = Undo.AddComponent<HairHelperId>(target);
            if (string.IsNullOrEmpty(helperId.Id)) helperId.CreateNewId();
            Undo.RecordObject(groom, "Bind Hair Scene Helper");
            HairHelper helper = new HairHelper
            {
                name = target.name,
                type = type,
                embedded = false,
                externalGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                externalHelperId = helperId.Id,
                position = target.transform.position,
                rotation = target.transform.rotation,
                scale = target.transform.lossyScale
            };
            helper.points.Add(target.transform.position);
            for (int child = 0; child < target.transform.childCount; child++)
                helper.points.Add(target.transform.GetChild(child).position);
            if (helper.points.Count == 1) helper.points.Add(target.transform.position + target.transform.up * 0.2f);
            helper.EnsureIntegrity();
            helper.name = target.name + " [" + helperId.Id.Substring(0, Mathf.Min(8, helperId.Id.Length)) + "]";
            groom.SharedHelpers.Add(helper);
            Commit(groom);
            return helper;
        }

        public static HairLodSettings AddLod(HairGroomAsset groom)
        {
            if (groom == null) return null;
            Undo.RecordObject(groom, "Add Hair LOD");
            int level = groom.Lods.Count;
            HairLodSettings previous = level > 0 ? groom.Lods[level - 1] : null;
            HairLodSettings lod = new HairLodSettings
            {
                name = $"LOD {level}",
                level = level,
                cardFraction = previous != null ? Mathf.Clamp01(previous.cardFraction * 0.5f) : 1f,
                samplesPerCard = previous != null ? Mathf.Max(4, previous.samplesPerCard - 2) : 12,
                screenRelativeHeight = previous != null ? previous.screenRelativeHeight * 0.5f : 0.6f
            };
            lod.EnsureIntegrity();
            groom.Lods.Add(lod);
            Commit(groom);
            return lod;
        }

        public static int ReprojectAllRoots(HairGroomAsset groom)
        {
            if (groom?.SourceMesh == null) return 0;
            Undo.RecordObject(groom, "Reproject Hair Guide Roots");
            int repaired = 0;
            foreach (HairGuide guide in groom.EnumerateGuides(false))
            {
                Vector3 oldRoot = guide.points != null && guide.points.Count > 0
                    ? guide.points[0].position
                    : guide.root.CachedLocalPosition;
                if (!HairMeshUtility.TryFindClosestSurface(groom.SourceMesh, groom.SourceMeshId, oldRoot,
                        out HairSurfaceAnchor anchor)) continue;
                Vector3 delta = anchor.CachedLocalPosition - oldRoot;
                if (guide.points != null)
                    for (int i = 0; i < guide.points.Count; i++) guide.points[i].position += delta;
                guide.root = anchor;
                repaired++;
            }
            groom.AcceptCurrentSourceTopology();
            Commit(groom);
            return repaired;
        }

        public static void Commit(HairGroomAsset groom, HairPreviewChange change = HairPreviewChange.All)
        {
            if (groom == null) return;
            groom.EnsureIntegrity();
            EditorUtility.SetDirty(groom);
            HairCardStage.ActiveStage?.QueuePreviewChange(change);
            HairGroomWorkspace.RepaintOpenWindows();
        }

        private static List<int>[] BuildVertexNeighbors(Mesh mesh)
        {
            List<int>[] result = new List<int>[mesh.vertexCount];
            for (int i = 0; i < result.Length; i++) result[i] = new List<int>();
            int[] triangles = mesh.triangles;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Connect(result, triangles[i], triangles[i + 1]);
                Connect(result, triangles[i + 1], triangles[i + 2]);
                Connect(result, triangles[i + 2], triangles[i]);
            }
            return result;
        }

        private static void Connect(IReadOnlyList<List<int>> neighbors, int a, int b)
        {
            if (!neighbors[a].Contains(b)) neighbors[a].Add(b);
            if (!neighbors[b].Contains(a)) neighbors[b].Add(a);
        }

        private static string Nicify(object value)
        {
            return ObjectNames.NicifyVariableName(value.ToString());
        }

        private static float DefaultValue(HairMapKind kind)
        {
            switch (kind)
            {
                case HairMapKind.Density:
                case HairMapKind.Length:
                case HairMapKind.Lift:
                case HairMapKind.Width:
                case HairMapKind.LodImportance:
                    return 1f;
                case HairMapKind.FlowX:
                case HairMapKind.FlowY:
                    return 0.5f;
                default:
                    return 0f;
            }
        }

        private static float DefaultModifierAmount(HairModifierType type)
        {
            switch (type)
            {
                case HairModifierType.Resample: return 12f;
                case HairModifierType.Simplify: return 6f;
                case HairModifierType.Gravity: return 0.5f;
                case HairModifierType.HelperFollow: return 1f;
                case HairModifierType.Clump: return 0.35f;
                case HairModifierType.Collision:
                case HairModifierType.PushOut: return 0.001f;
                case HairModifierType.TrimByMesh: return 0f;
                case HairModifierType.LodReduction: return 4f;
                case HairModifierType.Length:
                case HairModifierType.Width: return 1f;
                case HairModifierType.Smooth: return 0.35f;
                case HairModifierType.Curl:
                case HairModifierType.Wave:
                case HairModifierType.Noise: return 0.01f;
                default: return 0.1f;
            }
        }
    }
}
