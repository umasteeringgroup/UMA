using System.Collections.Generic;
using System.IO;
using UMA.Editors;
using UnityEditor;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// Cloth that moves at run time, through Unity's Cloth on the clothes' own renderer, which UMA builds from a slot's
    /// cloth coefficients and its renderer asset. Each vertex may move from where the bones put it by a distance that
    /// grows with how far it is, over the cloth, from where the clothes hold on to the body: a waistband stays, a hem
    /// swings. The clothes get their own renderer, so the body's vertices are not simulated.
    /// </summary>
    public static class ClothMotion
    {
        /// <summary>
        /// How far each vertex may move from its skinned place (m): <paramref name="gain"/> times its distance over the
        /// cloth from the nearest vertex within <paramref name="attach"/> of the body, at most <paramref name="max"/>.
        /// Sewn copies (<paramref name="group"/>) get one value. Cloth that touches the body nowhere may move the most.
        /// </summary>
        public static float[] MaxDistances(Vector3[] positions, int[] group, IList<int> triangles, float[] bodyDistance, float attach, float gain, float max)
        {
            int n = positions.Length;
            var result = new float[n];
            if (n == 0) return result;
            var nodeOf = new int[n];
            var root = new Dictionary<int, int>();
            var nodeVertex = new List<int>();
            for (int v = 0; v < n; v++)
            {
                if (!root.TryGetValue(group[v], out int node)) { node = nodeVertex.Count; root[group[v]] = node; nodeVertex.Add(group[v]); }
                nodeOf[v] = node;
            }
            int nodes = nodeVertex.Count;
            var nodeDistance = new float[nodes];
            for (int i = 0; i < nodes; i++) nodeDistance[i] = float.MaxValue;
            for (int v = 0; v < n; v++) nodeDistance[nodeOf[v]] = Mathf.Min(nodeDistance[nodeOf[v]], bodyDistance[v]);
            var neighbours = new List<(int node, float length)>[nodes];
            for (int i = 0; i < nodes; i++) neighbours[i] = new List<(int, float)>(6);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                for (int e = 0; e < 3; e++)
                {
                    int va = triangles[t + e], vb = triangles[t + (e + 1) % 3];
                    int a = nodeOf[va], b = nodeOf[vb];
                    if (a == b) continue;
                    bool known = false;
                    foreach (var x in neighbours[a]) if (x.node == b) { known = true; break; }
                    if (known) continue;
                    float length = (positions[va] - positions[vb]).magnitude;
                    neighbours[a].Add((b, length)); neighbours[b].Add((a, length));
                }
            }
            // Distance over the cloth from the held places (Dijkstra, from every held node at once).
            var along = new float[nodes];
            var heap = new SortedSet<(float d, int node)>();
            for (int i = 0; i < nodes; i++)
            {
                along[i] = nodeDistance[i] <= attach ? 0f : float.MaxValue;
                if (along[i] == 0f) heap.Add((0f, i));
            }
            while (heap.Count > 0)
            {
                var top = heap.Min; heap.Remove(top);
                if (top.d > along[top.node]) continue;
                foreach (var (next, length) in neighbours[top.node])
                {
                    float d = top.d + length;
                    if (d >= along[next]) continue;
                    if (along[next] < float.MaxValue) heap.Remove((along[next], next));
                    along[next] = d;
                    heap.Add((d, next));
                }
            }
            for (int v = 0; v < n; v++)
            {
                float d = along[nodeOf[v]];
                result[v] = d == float.MaxValue ? max : Mathf.Min(max, gain * d);
            }
            return result;
        }

        /// <summary>
        /// The slot's cloth coefficients as UMA's mesh combiner reads them: one per distinct vertex position, in the order
        /// the positions first appear, x the collision sphere distance and y the max distance.
        /// </summary>
        public static Vector2[] Serialize(Vector3[] slotVertices, float[] maxDistance, float sphereDistance)
        {
            var seen = new Dictionary<Vector3, int>();
            var list = new List<Vector2>();
            for (int v = 0; v < slotVertices.Length; v++)
            {
                if (seen.ContainsKey(slotVertices[v])) continue;
                seen[slotVertices[v]] = list.Count;
                list.Add(new Vector2(sphereDistance, maxDistance[v]));
            }
            return list.ToArray();
        }

        /// <summary>
        /// The renderer the moving clothes are drawn on (made beside the slot, or updated): its own object, so Unity's Cloth
        /// simulates only the clothes, with cloth settings from the fabric.
        /// </summary>
        public static UMARendererAsset RendererFor(string folder, string slotName, FabricSettings fabric, List<string> createdPaths)
        {
            string stem = folder.TrimEnd('/') + "/" + UMASlotSkinTransfer.MakeSafeAssetName(slotName);
            string propertiesPath = stem + "_ClothMotion.asset";
            var properties = AssetDatabase.LoadAssetAtPath<UMAClothProperties>(propertiesPath);
            if (properties == null)
            {
                properties = ScriptableObject.CreateInstance<UMAClothProperties>();
                AssetDatabase.CreateAsset(properties, propertiesPath);
                createdPaths?.Add(propertiesPath);
            }
            Settle(properties, fabric);
            EditorUtility.SetDirty(properties);
            AssetDatabase.SaveAssetIfDirty(properties);

            string rendererPath = stem + "_ClothRenderer.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UMARendererAsset>(rendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UMARendererAsset>();
                AssetDatabase.CreateAsset(renderer, rendererPath);
                createdPaths?.Add(rendererPath);
            }
            var serialized = new SerializedObject(renderer);
            serialized.FindProperty("_RendererName").stringValue = UMASlotSkinTransfer.MakeSafeAssetName(slotName) + " Cloth";
            serialized.FindProperty("_ClothProperties").objectReferenceValue = properties;
            serialized.FindProperty("_UpdateWhenOffscreen").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(renderer);
            return renderer;
        }

        /// <summary>Unity Cloth settings for a fabric: firmer fabric stretches and bends less.</summary>
        public static void Settle(UMAClothProperties p, FabricSettings fabric)
        {
            // The designer's compliances run from about 0 (firm) up: stretch to about 0.1 (spandex), bend over several
            // orders of magnitude. Unity's stiffnesses run 0 to 1.
            float stretch = fabric != null ? fabric.stretchCompliance : 0.01f;
            float bend = fabric != null ? Mathf.Max(1e-7f, fabric.bendCompliance) : 1e-3f;
            p.stretchingStiffness = Mathf.Clamp(1f - stretch * 4f, 0.5f, 1f);
            p.bendingStiffness = Mathf.Clamp(0.9f - 0.25f * (Mathf.Log10(bend) + 5f), 0.05f, 1f);
            p.damping = 0.2f;
            p.friction = fabric != null ? Mathf.Clamp01(fabric.friction) : 0.5f;
            p.useGravity = true;
            p.clothSolverFrequency = 120f;
            p.sleepThreshold = 0.1f;
            p.worldVelocityScale = 0.5f;
            p.worldAccelerationScale = 1f;
            p.useVirtualParticles = 1f;
            p.collisionMassScale = 0f;
            p.selfcollisionDistance = 0f;
            p.selfcollisionStifiness = 0.2f;
            p.enableContinuousCollision = true;
            p.externalAcceleration = Vector3.zero;
            p.randomAcceleration = Vector3.zero;
        }
    }
}
