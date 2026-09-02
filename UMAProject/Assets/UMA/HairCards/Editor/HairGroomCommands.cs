using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UMA.HairCards.Runtime;

namespace UMA.HairCards.Editor
{
    internal static class HairGroomCommands
    {
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
            if (groom == null || group == null) return null;
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
            HairModifierType type)
        {
            if (groom == null || group == null) return null;
            Undo.RecordObject(groom, "Add Hair Modifier");
            HairModifierSettings modifier = new HairModifierSettings
            {
                name = Nicify(type),
                type = type,
                domain = HairModifierDomain.GuidesAndChildren,
                amount = DefaultModifierAmount(type)
            };
            modifier.EnsureIntegrity();
            group.modifiers.Add(modifier);
            Commit(groom);
            return modifier;
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

        public static void Commit(HairGroomAsset groom)
        {
            if (groom == null) return;
            groom.EnsureIntegrity();
            EditorUtility.SetDirty(groom);
            HairCardStage.ActiveStage?.QueueRebuild();
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
