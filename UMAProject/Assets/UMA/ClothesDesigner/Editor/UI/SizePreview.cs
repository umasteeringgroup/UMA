using UnityEngine;

namespace UMA.ClothesDesigner.Editor
{
    /// <summary>
    /// The Size tool's picture while dragging: the draped cloth with its resized pieces moved to the size the drag gives,
    /// by geometry alone, with the simulation paused. The pattern is changed and the cloth built again and simulated when
    /// the button is released. It shows, without settling anything:
    /// <list type="bullet">
    /// <item>A piece made wider or narrower that wraps round a body part with other pieces of the same width factor (a front
    /// and a back sized by the chest, a sleeve round its arm, a belt's two halves) grows or shrinks round the part: its
    /// distance from the part's axis is scaled, so the girth changes by the factor and its seams to those pieces stay
    /// together.</item>
    /// <item>Any other piece made wider or narrower is stretched across itself from its middle, along the cloth.</item>
    /// <item>A piece made longer or shorter is stretched down itself from its top, along the cloth.</item>
    /// <item>A piece not resized but sewn to one that is (a cuff on a sleeve's hem) moves with it.</item>
    /// <item>No point is left inside the body: one that would be is put on it, the clearance out.</item>
    /// </list>
    /// </summary>
    public static class SizePreview
    {
        /// <summary>
        /// For each point, how far the cloth moves (m) for one centimetre across (<paramref name="across"/>) and up
        /// (<paramref name="up"/>) its piece as drawn: the mean over the cloth triangles it is a corner of.
        /// </summary>
        public static void Axes(PatternMesh mesh, Vector3[] positions, out Vector3[] across, out Vector3[] up)
        {
            int n = positions.Length;
            across = new Vector3[n];
            up = new Vector3[n];
            var count = new int[n];
            for (int t = 0; t + 2 < mesh.triangles.Count; t += 3)
            {
                int a = mesh.triangles[t], b = mesh.triangles[t + 1], c = mesh.triangles[t + 2];
                if (a >= n || b >= n || c >= n) continue;
                if (!DesignerSession.ClothAxes(positions[a], positions[b], positions[c], mesh.drawn[a], mesh.drawn[b], mesh.drawn[c], out Vector3 x, out Vector3 y)) continue;
                across[a] += x; across[b] += x; across[c] += x;
                up[a] += y; up[b] += y; up[c] += y;
                count[a]++; count[b]++; count[c]++;
            }
            for (int i = 0; i < n; i++)
            {
                if (count[i] == 0) continue;
                across[i] /= count[i];
                up[i] /= count[i];
            }
        }

        /// <summary>
        /// The axis each resized piece grows round when made wider or narrower, or null for one stretched across itself:
        /// a piece wrapped round a body part (not laid flat) and sewn to itself or to another piece of the same width factor.
        /// </summary>
        public static Ray?[] RingAxes(PatternMesh mesh, ClothesPattern pattern, ClothesFit fit, BodyLandmarks body, Vector2[] factors)
        {
            int pieces = mesh.pieceStart != null ? mesh.pieceStart.Length : 0;
            var axes = new Ray?[pieces];
            if (pattern == null || factors == null || body == null || !body.valid) return axes;
            var ring = new bool[pieces];
            bool[] tack = Tacks(mesh);
            for (int i = 0; i < mesh.stitches.Count; i++)
            {
                if (tack[i]) continue;
                Vector2Int s = mesh.stitches[i];
                if (s.x >= mesh.piece.Count || s.y >= mesh.piece.Count) continue;
                int a = mesh.piece[s.x], b = mesh.piece[s.y];
                if (a >= pieces || b >= pieces || a >= factors.Length || b >= factors.Length) continue;
                float ka = factors[a].x, kb = factors[b].x;
                if (Mathf.Abs(ka - 1f) < 1e-5f || Mathf.Abs(ka - kb) > 1e-4f) continue;
                ring[a] = ring[b] = true;
            }
            for (int p = 0; p < pieces && p < pattern.pieces.Count; p++)
            {
                if (!ring[p]) continue;
                PiecePlacement placement = fit != null ? fit.PlacementFor(pattern.pieces[p]) : pattern.pieces[p].placement;
                if (placement == null || placement.flat) continue;
                axes[p] = PartAxis(placement.part, body);
            }
            return axes;
        }

        /// <summary>Which of the mesh's stitches are tacks or buttons (single points held together), not seams.</summary>
        private static bool[] Tacks(PatternMesh mesh)
        {
            var tack = new bool[mesh.stitches.Count];
            foreach (int i in mesh.tackStitches) if (i >= 0 && i < tack.Length) tack[i] = true;
            return tack;
        }

        /// <summary>The axis of a body part (renderer space): the torso's and the head's upright through their middles, a limb's along it.</summary>
        public static Ray? PartAxis(BodyPart part, BodyLandmarks body)
        {
            if (body == null || !body.valid) return null;
            if (part == BodyPart.Torso) return new Ray(new Vector3(body.torsoCenter.x, 0f, body.torsoCenter.y), Vector3.up);
            if (part == BodyPart.Head) return new Ray(new Vector3(body.headCenter.x, 0f, body.headCenter.y), Vector3.up);
            if (!body.limbs) return null;
            if (PiecePlacement.IsArm(part))
            {
                body.ArmFrame(part == BodyPart.LeftArm, out Vector3 root, out Vector3 axis, out _, out _);
                return new Ray(root, axis);
            }
            if (PiecePlacement.IsLeg(part))
            {
                bool left = part == BodyPart.LeftLeg;
                Vector3 hip = left ? body.leftHip : body.rightHip, ankle = left ? body.leftAnkle : body.rightAnkle;
                Vector3 along = ankle - hip;
                return along.sqrMagnitude > 1e-8f ? new Ray(hip, along) : (Ray?)null;
            }
            return null;
        }

        /// <summary>
        /// The cloth with each piece at its factor across and down (<paramref name="factors"/>, as cut; one for a piece not
        /// resized), from <paramref name="start"/> (the drape when the drag began) and its <see cref="Axes"/>.
        /// <paramref name="rings"/> (from <see cref="RingAxes"/>) says which pieces grow round a body part. With the
        /// <paramref name="body"/> (made once a drag from the same start), moved points are kept <paramref name="clearance"/>
        /// (m) outside it.
        /// </summary>
        public static Vector3[] Shape(PatternMesh mesh, Vector3[] start, Vector3[] across, Vector3[] up, Vector2[] factors, Ray?[] rings, BodyPlanes body, float clearance)
        {
            int n = start.Length;
            var result = (Vector3[])start.Clone();
            if (mesh == null || factors == null || mesh.pieceStart == null) return result;
            int pieces = mesh.pieceStart.Length;
            var move = new Vector3[n];
            var resized = new bool[pieces];
            for (int p = 0; p < pieces && p < factors.Length; p++)
            {
                Vector2 k = factors[p];
                bool wider = Mathf.Abs(k.x - 1f) > 1e-5f, longer = Mathf.Abs(k.y - 1f) > 1e-5f;
                if (!wider && !longer) continue;
                int first = mesh.pieceStart[p], count = mesh.pieceCount[p];
                if (count <= 0) continue;
                resized[p] = true;
                // Its middle across and its top, as drawn: the top stays where it is laid.
                float minX = float.MaxValue, maxX = float.MinValue, top = float.MinValue;
                for (int v = first; v < first + count && v < n; v++)
                {
                    Vector2 d = mesh.drawn[v];
                    minX = Mathf.Min(minX, d.x); maxX = Mathf.Max(maxX, d.x); top = Mathf.Max(top, d.y);
                }
                float middle = (minX + maxX) * 0.5f;
                Ray? ring = rings != null && p < rings.Length ? rings[p] : null;
                Vector3 axisDirection = ring.HasValue ? ring.Value.direction.normalized : Vector3.zero;
                for (int v = first; v < first + count && v < n; v++)
                {
                    Vector2 d = mesh.drawn[v];
                    Vector3 m = Vector3.zero;
                    if (wider)
                    {
                        if (ring.HasValue)
                        {
                            Vector3 rel = start[v] - ring.Value.origin;
                            m += (rel - axisDirection * Vector3.Dot(rel, axisDirection)) * (k.x - 1f);
                        }
                        else m += across[v] * ((k.x - 1f) * (d.x - middle));
                    }
                    if (longer) m += up[v] * ((k.y - 1f) * (d.y - top));
                    move[v] = m;
                }
            }
            // Sewn points of resized pieces move as one (each side of a seam would otherwise follow its own piece's direction
            // and the seam open): the mean of their moves.
            int[] weld = GarmentBaker.WeldGroups(n, mesh.stitches);
            var groupSum = new System.Collections.Generic.Dictionary<int, (Vector3 sum, int count)>();
            for (int v = 0; v < n; v++)
            {
                int p = mesh.piece[v];
                if (p >= pieces || !resized[p]) continue;
                groupSum.TryGetValue(weld[v], out (Vector3 sum, int count) g);
                groupSum[weld[v]] = (g.sum + move[v], g.count + 1);
            }
            for (int v = 0; v < n; v++)
            {
                int p = mesh.piece[v];
                if (p >= pieces || !resized[p] || !groupSum.TryGetValue(weld[v], out (Vector3 sum, int count) g) || g.count < 2) continue;
                move[v] = g.sum / g.count;
            }
            // Pieces not resized follow the resized ones they are sewn to, by the mean move of their stitched points. Tacks
            // (a belt's loops to a dress) hold a point, not an edge: the dress does not follow a belt made taller.
            var sum = new Vector3[pieces];
            var sewn = new int[pieces];
            bool[] tack = Tacks(mesh);
            for (int i = 0; i < mesh.stitches.Count; i++)
            {
                if (tack[i]) continue;
                Vector2Int s = mesh.stitches[i];
                if (s.x >= n || s.y >= n) continue;
                int a = mesh.piece[s.x], b = mesh.piece[s.y];
                if (a >= pieces || b >= pieces) continue;
                if (resized[a] && !resized[b]) { sum[b] += move[s.x]; sewn[b]++; }
                else if (resized[b] && !resized[a]) { sum[a] += move[s.y]; sewn[a]++; }
            }
            for (int p = 0; p < pieces; p++)
            {
                if (resized[p] || sewn[p] == 0) continue;
                Vector3 m = sum[p] / sewn[p];
                for (int v = mesh.pieceStart[p]; v < mesh.pieceStart[p] + mesh.pieceCount[p] && v < n; v++) move[v] = m;
            }
            var moved = new System.Collections.Generic.List<int>();
            for (int v = 0; v < n; v++) if (move[v] != Vector3.zero) moved.Add(v);
            if (body == null) foreach (int v in moved) result[v] = start[v] + move[v];
            else System.Threading.Tasks.Parallel.For(0, moved.Count, i => { int v = moved[i]; result[v] = body.Outside(v, start[v] + move[v], clearance); });
            return result;
        }

        /// <summary>
        /// Keeps moved points of the cloth outside the body, quickly enough for every move of a drag. For each point the
        /// body's nearest point and outward normal are found where the point was when the drag began (all at once, on
        /// every core, when it starts) and again only once the point has moved <see cref="Relook"/> from where they were
        /// last found; a moved point is held outside that tangent plane. Searching the whole body for every moved point of
        /// a T-shirt took 160 ms an update; the planes take under a millisecond, and finding them 2414 times, one point
        /// after another, took about 190 ms.
        /// </summary>
        public sealed class BodyPlanes
        {
            /// <summary>How far from the body a quick search reaches (m); a point farther than that is taken as clear of it.</summary>
            public const float Near = 0.05f;
            /// <summary>How far a point moves before the body is looked up again where it is (m).</summary>
            public const float Relook = 0.02f;
            private readonly BodyCollider body;
            // Per point: where the body was last looked up, whether it was within Near, and its nearest point and normal.
            private readonly Vector3[] from, point, normal;
            private readonly bool[] near;

            public BodyPlanes(BodyCollider body, Vector3[] start)
            {
                this.body = body;
                int n = start.Length;
                from = new Vector3[n];
                point = new Vector3[n];
                normal = new Vector3[n];
                near = new bool[n];
                System.Threading.Tasks.Parallel.For(0, n, v => LookUp(v, start[v]));
            }

            private void LookUp(int v, Vector3 at)
            {
                from[v] = at;
                near[v] = body.ClosestNear(at, Near, out int t, out Vector3 nearest, out float d) && t >= 0 && Mathf.Abs(d) < Near;
                if (!near[v]) return;
                point[v] = nearest;
                normal[v] = body.N[t];
            }

            /// <summary>
            /// A moved point of the cloth (its index and where it moved to), put at least <paramref name="clearance"/>
            /// outside the body. Safe to call for different points at once.
            /// </summary>
            public Vector3 Outside(int v, Vector3 q, float clearance)
            {
                if ((q - from[v]).sqrMagnitude > Relook * Relook) LookUp(v, q);
                // Farther than Near from the body where it was last looked up, and moved less than Relook since: clear of it.
                return near[v] ? OnPlane(q, point[v], normal[v], clearance) : q;
            }

            private static Vector3 OnPlane(Vector3 q, Vector3 at, Vector3 outward, float clearance)
            {
                float d = Vector3.Dot(q - at, outward);
                return d < clearance ? q + outward * (clearance - d) : q;
            }
        }
    }
}
