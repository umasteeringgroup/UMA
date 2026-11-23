#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The Vertex Editor's Beautify brush: which edges are lines of the mesh, where a vertex belongs
    /// among its neighbours, and where on the surface (and in the texture) it lands.
    /// </summary>
    public sealed class VertexEditorBeautifyTests
    {
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        private static object Call(string method, params object[] arguments)
        {
            MethodInfo info = typeof(VertexEditorStage).GetMethod(method, PrivateStatic);
            Assert.That(info, Is.Not.Null, "VertexEditorStage." + method);
            return info.Invoke(null, arguments);
        }

        private static T Field<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name);
            Assert.That(field, Is.Not.Null, target.GetType().Name + "." + name);
            return (T)field.GetValue(target);
        }

        /// <summary>A grid of vertices in the XY plane, columns at <paramref name="xs"/> and rows at <paramref name="ys"/>.</summary>
        private static Vector3[] Grid(float[] xs, float[] ys)
        {
            var positions = new Vector3[xs.Length * ys.Length];
            for (int y = 0; y < ys.Length; y++)
            {
                for (int x = 0; x < xs.Length; x++)
                {
                    positions[(y * xs.Length) + x] = new Vector3(xs[x], ys[y], 0f);
                }
            }
            return positions;
        }

        [Test]
        public void QuadDiagonalsAreNotLinesOfTheMesh()
        {
            // 3 x 3 vertices: four quads, each stored as two triangles. The middle vertex (4) is joined
            // by triangle edges to six vertices, but only four of those edges were modelled.
            Vector3[] positions = Grid(new[] { 0f, 1f, 2f }, new[] { 0f, 1f, 2f });
            int[] triangles = VertexEditorUVSculptTests.GridTriangles(3, 3);
            object topology = Call("BuildBeautifyTopology", 0, 9, VertexEditorUVSculptTests.TriangleStarts(triangles), triangles, positions, null, null);
            int[][] ring = Field<int[][]>(topology, "ring");
            bool[] open = Field<bool[]>(topology, "open");
            int[][] fan = Field<int[][]>(topology, "fan");

            Assert.That(ring[4], Is.EquivalentTo(new[] { 1, 3, 5, 7 }));
            // In order around the vertex: the neighbour opposite each one is two places on.
            for (int k = 0; k < 2; k++)
            {
                Vector3 a = positions[ring[4][k]] - positions[4];
                Vector3 b = positions[ring[4][k + 2]] - positions[4];
                Assert.That(Vector3.Dot(a.normalized, b.normalized), Is.EqualTo(-1f).Within(1e-5f));
            }
            Assert.That(fan[4].Length, Is.EqualTo(6), "The triangles around the middle vertex.");
            for (int i = 0; i < 9; i++)
            {
                Assert.That(open[i], Is.EqualTo(i != 4), "Only the middle vertex is off the open edge: vertex " + i);
            }
        }

        [Test]
        public void BothSidesOfDoubleSidedGeometryAreOneSurface()
        {
            // The same grid with a reversed copy of every triangle on the same vertices, as double-sided
            // cloth and hair cards have.
            Vector3[] positions = Grid(new[] { 0f, 1f, 2f }, new[] { 0f, 1f, 2f });
            int[] front = VertexEditorUVSculptTests.GridTriangles(3, 3);
            var triangles = new int[front.Length * 2];
            for (int i = 0; i < front.Length; i += 3)
            {
                triangles[i] = front[i]; triangles[i + 1] = front[i + 1]; triangles[i + 2] = front[i + 2];
                triangles[front.Length + i] = front[i]; triangles[front.Length + i + 1] = front[i + 2]; triangles[front.Length + i + 2] = front[i + 1];
            }
            object topology = Call("BuildBeautifyTopology", 0, 9, VertexEditorUVSculptTests.TriangleStarts(triangles), triangles, positions, null, null);
            Assert.That(Field<bool[]>(topology, "open")[4], Is.False);
            Assert.That(Field<int[][]>(topology, "ring")[4], Is.EquivalentTo(new[] { 1, 3, 5, 7 }));
        }

        [Test]
        public void CopiesOnAUVSeamAreMarked()
        {
            // Vertex 9 is a copy of the middle vertex (4) with another UV.
            Vector3[] grid = Grid(new[] { 0f, 1f, 2f }, new[] { 0f, 1f, 2f });
            var positions = new Vector3[10];
            grid.CopyTo(positions, 0);
            positions[9] = positions[4];
            var uvs = new Vector2[10];
            for (int i = 0; i < 9; i++) uvs[i] = new Vector2(positions[i].x * 0.5f, positions[i].y * 0.5f);
            uvs[9] = new Vector2(0.9f, 0.9f);
            int[] groupOf = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 4 };
            int[] triangles = VertexEditorUVSculptTests.GridTriangles(3, 3);
            object topology = Call("BuildBeautifyTopology", 0, 10, VertexEditorUVSculptTests.TriangleStarts(triangles), triangles, positions, uvs, groupOf);
            bool[] seam = Field<bool[]>(topology, "seam");
            Assert.That(seam[4], Is.True);
            Assert.That(seam[1], Is.False);
            Assert.That(Field<int[]>(topology, "group")[9], Is.EqualTo(4));
        }

        [Test]
        public void AVertexGoesWhereItsTwoLinesMeetAndKeepsItsSpacing()
        {
            // Lines that are straight but unevenly spaced, as around a joint: the crossing is at (1, 1),
            // a quarter of the way along each line. The vertex has been knocked off it.
            Vector3 crossing = new Vector3(1f, 1f, 0f);
            Vector3[] ring = new Vector3[16];
            ring[0] = new Vector3(4f, 1f, 0f);
            ring[1] = new Vector3(1f, 4f, 0f);
            ring[2] = new Vector3(0f, 1f, 0f);
            ring[3] = new Vector3(1f, 0f, 0f);
            Vector3 knocked = new Vector3(1.3f, 0.8f, 0.05f);

            var straight = (Vector3)Call("BeautifyTarget", knocked, ring, 4, 0f);
            Assert.That((straight - crossing).magnitude, Is.LessThan(1e-3f), "In line both ways, spacing as modelled.");

            var even = (Vector3)Call("BeautifyTarget", knocked, ring, 4, 1f);
            // The lines' middles are (2, 1) and (1, 2); the neighbours themselves do not move, so the
            // vertex ends between the two, which is the middle of its four neighbours.
            Assert.That((even - new Vector3(1.5f, 1.5f, 0f)).magnitude, Is.LessThan(1e-3f), "Fully even spacing: the middle of its neighbours.");

            // A vertex that is already in line stays where it is.
            var unchanged = (Vector3)Call("BeautifyTarget", crossing, ring, 4, 0f);
            Assert.That((unchanged - crossing).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void ThreeLinesOfATriangleMeshAndPoles()
        {
            // Six neighbours (a triangle mesh): three lines through the middle.
            Vector3[] ring = new Vector3[16];
            for (int i = 0; i < 6; i++)
            {
                float angle = i * 60f * Mathf.Deg2Rad;
                ring[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (i % 2 == 0 ? 1f : 2f);
            }
            var middle = (Vector3)Call("BeautifyTarget", new Vector3(0.2f, -0.1f, 0f), ring, 6, 0f);
            Assert.That(middle.magnitude, Is.LessThan(1e-3f));

            // Five neighbours (a pole) have no opposite pairs: the vertex goes to their middle.
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < 5; i++)
            {
                ring[i] = new Vector3(i, i * i, 1f);
                sum += ring[i];
            }
            var pole = (Vector3)Call("BeautifyTarget", new Vector3(9f, 9f, 9f), ring, 5, 0f);
            Assert.That((pole - (sum / 5f)).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void AMovedVertexLandsOnTheSurfaceAndTakesTheUVThere()
        {
            Vector3[] positions = Grid(new[] { 0f, 1f, 2f }, new[] { 0f, 1f, 2f });
            int[] triangles = VertexEditorUVSculptTests.GridTriangles(3, 3);
            object topology = Call("BuildBeautifyTopology", 0, 9, VertexEditorUVSculptTests.TriangleStarts(triangles), triangles, positions, null, null);
            int[] fan = Field<int[][]>(topology, "fan")[4];

            var normal = (Vector3)Call("FanNormal", fan, triangles, positions);
            Assert.That(Mathf.Abs(Vector3.Dot(normal, Vector3.forward)), Is.EqualTo(1f).Within(1e-5f));

            // A point above the surface comes down onto it.
            Vector3 point = new Vector3(1.25f, 0.9f, 0.3f);
            var arguments = new object[] { point, fan, triangles, positions, Vector3.zero, 0, Vector3.zero };
            Assert.That((bool)Call("ClosestPointOnFan", arguments), Is.True);
            var closest = (Vector3)arguments[4];
            int triangle = (int)arguments[5];
            var weights = (Vector3)arguments[6];
            Assert.That((closest - new Vector3(1.25f, 0.9f, 0f)).magnitude, Is.LessThan(1e-5f));
            Assert.That(weights.x + weights.y + weights.z, Is.EqualTo(1f).Within(1e-5f));
            Vector3 rebuilt = (positions[triangles[triangle]] * weights.x) + (positions[triangles[triangle + 1]] * weights.y) +
                              (positions[triangles[triangle + 2]] * weights.z);
            Assert.That((rebuilt - closest).magnitude, Is.LessThan(1e-5f), "The weights give the point back.");

            // With a texture laid flat over the grid, the same weights give the UV of that place.
            Vector2 UV(Vector3 p) => new Vector2(0.1f + (p.x * 0.4f), 0.2f + (p.y * 0.3f));
            Vector2 uv = (UV(positions[triangles[triangle]]) * weights.x) + (UV(positions[triangles[triangle + 1]]) * weights.y) +
                         (UV(positions[triangles[triangle + 2]]) * weights.z);
            Assert.That((uv - UV(closest)).magnitude, Is.LessThan(1e-5f));
        }
    }
}
#endif
