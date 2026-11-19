#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    /// <summary>
    /// The Vertex Editor's UV Sculpt brush: how a drag across the surface becomes a change of UV, what
    /// counts as one point or one island of the texture, where a drag stops, how tangents follow, and
    /// how the atlas mapping is taken back out when the UVs are saved.
    /// </summary>
    public sealed class VertexEditorUVSculptTests
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

        [Test]
        public void GradientsTurnAMovementOnTheSurfaceIntoAChangeOfUV()
        {
            // A triangle standing in the XY plane, its texture scaled 2 in u and 0.5 in v and offset.
            Vector3 p0 = new Vector3(1f, 1f, 3f), p1 = new Vector3(2f, 1f, 3f), p2 = new Vector3(1f, 3f, 3f);
            Vector2 Map(Vector3 p) => new Vector2(0.1f + (2f * p.x), 0.3f + (0.5f * p.y));
            var arguments = new object[] { p0, p1, p2, Map(p0), Map(p1), Map(p2), Vector3.zero, Vector3.zero, 0f };
            Assert.That((bool)Call("TriangleUVGradients", arguments), Is.True);
            var gradU = (Vector3)arguments[6];
            var gradV = (Vector3)arguments[7];
            Assert.That((gradU - new Vector3(2f, 0f, 0f)).magnitude, Is.LessThan(1e-5f));
            Assert.That((gradV - new Vector3(0f, 0.5f, 0f)).magnitude, Is.LessThan(1e-5f));
            Assert.That((float)arguments[8], Is.EqualTo(1f).Within(1e-5f), "area");

            // Any movement in the plane changes the UV as the mapping says; movement off the surface changes nothing.
            Vector3 move = new Vector3(0.3f, -0.2f, 0f);
            Vector2 expected = Map(p0 + move) - Map(p0);
            Assert.That(Vector3.Dot(gradU, move), Is.EqualTo(expected.x).Within(1e-5f));
            Assert.That(Vector3.Dot(gradV, move), Is.EqualTo(expected.y).Within(1e-5f));
            Assert.That(Vector3.Dot(gradU, Vector3.forward), Is.EqualTo(0f).Within(1e-6f));

            arguments = new object[] { p0, p0, p2, Map(p0), Map(p0), Map(p2), Vector3.zero, Vector3.zero, 0f };
            Assert.That((bool)Call("TriangleUVGradients", arguments), Is.False, "A triangle without area has no gradients.");
        }

        [Test]
        public void TheTextureUnderThePointerStaysUnderThePointer()
        {
            // A brush of radius 1 put down at the origin and dragged 0.3 along X, with a linear falloff.
            // The texture at a point moves by the weight there, so a vertex shows what ended up on it.
            System.Func<float, float> linear = x => 1f - x;
            Vector3 drag = new Vector3(0.3f, 0f, 0f);
            float At(float x, float max = 1f) =>
                (float)Call("DraggedWeight", max, new Vector3(x, 0f, 0f), Vector3.zero, drag, 1f, linear);

            // Where the pointer is now: the texture from where it was put down, which moved all the way.
            Assert.That(At(0.3f), Is.EqualTo(1f).Within(1e-3f));
            // The point at 0.5 has weight 0.5 and moved 0.15, to 0.65.
            Assert.That(At(0.65f), Is.EqualTo(0.5f).Within(1e-3f));
            // Behind the brush: the point at -0.6154 has weight 0.3846 and moved 0.1154, to -0.5.
            Assert.That(At(-0.5f), Is.EqualTo(0.5f / 1.3f).Within(1e-3f));
            // The edge of the brush does not move, and nothing outside it does.
            Assert.That(At(1f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(At(1.2f), Is.EqualTo(0f));
            // Effect 50 (or a half-masked vertex): everything moves half as far.
            Assert.That(At(0.15f, 0.5f), Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(At(0.3f, 0f), Is.EqualTo(0f));

            // No drag: the weight is the brush's weight at the vertex.
            Assert.That((float)Call("DraggedWeight", 1f, new Vector3(0.25f, 0f, 0f), Vector3.zero, Vector3.zero, 1f, linear),
                Is.EqualTo(0.75f).Within(1e-3f));
        }

        [Test]
        public void ADragIsLaidOnTheSurfaceAlongTheLineOfSight()
        {
            // The camera looks along +Z; the pointer moves 1 up on its view plane.
            Vector3 view = Vector3.forward;
            Vector3 drag = Vector3.up;

            // A surface facing the camera: the point under the pointer moves just as far.
            var facing = (Vector3)Call("DragOnSurface", drag, Vector3.back, view);
            Assert.That((facing - drag).magnitude, Is.LessThan(1e-5f));

            // A surface leaning back 60 degrees: the point has to travel twice as far along the surface
            // to stay under the pointer, and from the camera it still moves exactly with the pointer.
            Vector3 leaning = Quaternion.AngleAxis(60f, Vector3.right) * Vector3.back;
            var along = (Vector3)Call("DragOnSurface", drag, leaning, view);
            Assert.That(Vector3.Dot(along, leaning), Is.EqualTo(0f).Within(1e-5f), "In the surface.");
            Assert.That(along.magnitude, Is.EqualTo(2f).Within(1e-4f));
            Assert.That((Vector3.ProjectOnPlane(along, view) - drag).magnitude, Is.LessThan(1e-4f), "Under the pointer.");

            // Nearly edge on, that distance runs away: the drag is laid flat onto the surface instead.
            Vector3 edgeOn = Quaternion.AngleAxis(85f, Vector3.right) * Vector3.back;
            var flat = (Vector3)Call("DragOnSurface", drag, edgeOn, view);
            Assert.That(flat.magnitude, Is.LessThanOrEqualTo(1f + 1e-5f));
            Assert.That(Vector3.Dot(flat, edgeOn), Is.EqualTo(0f).Within(1e-5f));

            // No view direction (not in a Scene view): flat onto the surface as well.
            var noView = (Vector3)Call("DragOnSurface", drag, leaning, Vector3.zero);
            Assert.That((noView - Vector3.ProjectOnPlane(drag, leaning)).magnitude, Is.LessThan(1e-5f));
        }

        [Test]
        public void SeamCopiesAreSeparateIslandsAndHardEdgeCopiesAreOnePoint()
        {
            // Two quads side by side along X. The two vertices between them are doubled, and each copy
            // has its own place in the texture: a UV seam, so the quads are two islands.
            Vector3[] positions =
            {
                new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
                new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(2f, 1f, 0f), new Vector3(1f, 1f, 0f),
            };
            Vector2[] seamUVs =
            {
                new Vector2(0f, 0f), new Vector2(0.4f, 0f), new Vector2(0.4f, 0.4f), new Vector2(0f, 0.4f),
                new Vector2(0.6f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.4f), new Vector2(0.6f, 0.4f),
            };
            int[] triangles = { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            int[] slotTriangles = { 0, 3, 6, 9 };

            object seam = Call("BuildSculptUVTopology", 0, 8, slotTriangles, triangles, positions, seamUVs, 1e-5f, 1e-5f);
            int[] weld = Field<int[]>(seam, "weld");
            int[] island = Field<int[]>(seam, "island");
            bool[] border = Field<bool[]>(seam, "border");
            Assert.That(weld[4], Is.EqualTo(4), "A seam copy is its own point of the texture.");
            Assert.That(island[0], Is.Not.EqualTo(island[4]), "Two islands.");
            Assert.That(island[1], Is.EqualTo(island[3]));
            Assert.That(border, Is.All.True, "Every vertex of two separate quads is on an island edge.");

            // The same mesh with one continuous texture: the doubled vertices are hard-edge copies.
            var joinedUVs = new Vector2[8];
            for (int i = 0; i < 8; i++) joinedUVs[i] = new Vector2(positions[i].x * 0.5f, positions[i].y);
            object joined = Call("BuildSculptUVTopology", 0, 8, slotTriangles, triangles, positions, joinedUVs, 1e-5f, 1e-5f);
            weld = Field<int[]>(joined, "weld");
            island = Field<int[]>(joined, "island");
            Assert.That(weld[4], Is.EqualTo(1), "Copies with the same position and UV are one point.");
            Assert.That(weld[7], Is.EqualTo(2));
            Assert.That(island[0], Is.EqualTo(island[5]), "One island.");
        }

        [Test]
        public void BorderIsOnlyTheOutlineOfAnIsland()
        {
            // A 3 x 3 grid of vertices: only the middle one is inside.
            var positions = new Vector3[9];
            var uvs = new Vector2[9];
            for (int y = 0; y < 3; y++)
            {
                for (int x = 0; x < 3; x++)
                {
                    positions[(y * 3) + x] = new Vector3(x, y, 0f);
                    uvs[(y * 3) + x] = new Vector2(x * 0.5f, y * 0.5f);
                }
            }
            int[] triangles = GridTriangles(3, 3);
            object topology = Call("BuildSculptUVTopology", 0, 9, TriangleStarts(triangles), triangles, positions, uvs, 1e-5f, 1e-5f);
            bool[] border = Field<bool[]>(topology, "border");
            for (int i = 0; i < 9; i++)
            {
                Assert.That(border[i], Is.EqualTo(i != 4), "vertex " + i);
            }
        }

        [Test]
        public void ADragStopsBeforeTheTextureFoldsOver()
        {
            // Two triangles of a unit square in the texture. Vertex 2 (the far corner) is dragged toward
            // and past the diagonal it faces.
            Vector2[] start = { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            int[] triangles = { 0, 1, 2, 0, 2, 3 };
            int[] strokeTriangles = { 0, 3 };
            float[] areas =
            {
                (float)Call("SignedUVArea", start[0], start[1], start[2]),
                (float)Call("SignedUVArea", start[0], start[2], start[3]),
            };
            Assert.That(areas[0], Is.EqualTo(0.5f).Within(1e-6f));
            var scratch = (Vector2[])start.Clone();
            int[] vertices = { 2 };

            // A UV moves by minus its offset. A small move is applied in full.
            var small = new[] { new Vector2(0.2f, 0f) };
            Assert.That((float)Call("LargestUVDragWithoutFolding", triangles, strokeTriangles, areas, start, scratch, vertices, small, 0.1f),
                Is.EqualTo(1f));

            // Moving it 2 to the left would turn the second triangle over (it has no area left half way):
            // the drag stops where that triangle is down to a tenth of its area.
            var far = new[] { new Vector2(2f, 0f) };
            float share = (float)Call("LargestUVDragWithoutFolding", triangles, strokeTriangles, areas, start, scratch, vertices, far, 0.1f);
            Assert.That(share, Is.EqualTo(0.45f).Within(0.005f));
            Vector2 moved = start[2] - (far[0] * share);
            Assert.That((float)Call("SignedUVArea", start[0], moved, start[3]) / areas[1], Is.GreaterThanOrEqualTo(0.1f - 1e-4f));
        }

        [Test]
        public void TangentsTurnWithTheTextureAndKeepTheirHandedness()
        {
            // A quad facing +Z whose texture is turned 30 degrees: u then grows in a direction turned
            // the other way on the surface.
            Vector3[] positions = { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f) };
            int[] triangles = { 0, 2, 1, 0, 3, 2 };
            var normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            var before = new Vector2[4];
            var after = new Vector2[4];
            Quaternion turn = Quaternion.Euler(0f, 0f, 30f);
            for (int i = 0; i < 4; i++)
            {
                before[i] = positions[i];
                after[i] = turn * positions[i];
            }
            var tangents = new[]
            {
                new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f), new Vector4(1f, 0f, 0f, -1f),
            };
            var changed = (int)Call("RotateTangentsForUVChange", triangles, new[] { 0, 3 }, new[] { 0, 1, 2 }, positions, normals, before, after, tangents);
            Assert.That(changed, Is.EqualTo(3));
            Vector3 expected = Quaternion.Euler(0f, 0f, -30f) * Vector3.right;
            for (int i = 0; i < 3; i++)
            {
                Assert.That((new Vector3(tangents[i].x, tangents[i].y, tangents[i].z) - expected).magnitude, Is.LessThan(1e-4f), "vertex " + i);
                Assert.That(tangents[i].w, Is.EqualTo(-1f));
            }
            Assert.That(tangents[3], Is.EqualTo(new Vector4(1f, 0f, 0f, -1f)), "A vertex that was not listed is left alone.");

            // The same texture, unchanged: nothing turns.
            changed = (int)Call("RotateTangentsForUVChange", triangles, new[] { 0, 3 }, new[] { 0, 1, 2, 3 }, positions, normals, before, before, tangents);
            Assert.That(changed, Is.EqualTo(0));
        }

        [Test]
        public void TheAtlasScaleIsMeasuredFromTheSlotAndStageUVs()
        {
            var slot = new Vector2[20];
            var baked = new Vector2[20];
            for (int i = 0; i < slot.Length; i++)
            {
                slot[i] = new Vector2(Mathf.Repeat(i * 0.37f, 1f), Mathf.Repeat(i * 0.61f, 1f));
                baked[i] = new Vector2(0.25f + (0.5f * slot[i].x), 0.125f + (0.25f * slot[i].y));
            }
            var arguments = new object[] { slot, baked, Vector2.zero, 0f };
            Assert.That((bool)Call("TryFitUVScale", arguments), Is.True);
            var scale = (Vector2)arguments[2];
            Assert.That(scale.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(scale.y, Is.EqualTo(0.25f).Within(1e-5f));
            Assert.That((float)arguments[3], Is.LessThan(1e-5f));

            // One UV that something else already changed shows up as an error, so the fit is not trusted.
            baked[7] += new Vector2(0.05f, 0f);
            arguments = new object[] { slot, baked, Vector2.zero, 0f };
            Assert.That((bool)Call("TryFitUVScale", arguments), Is.True);
            Assert.That((float)arguments[3], Is.GreaterThan(0.01f));

            arguments = new object[] { slot, new Vector2[3], Vector2.zero, 0f };
            Assert.That((bool)Call("TryFitUVScale", arguments), Is.False, "Different vertex counts cannot be matched.");
        }

        internal static int[] GridTriangles(int width, int height)
        {
            var triangles = new int[(width - 1) * (height - 1) * 6];
            int t = 0;
            for (int y = 0; y < height - 1; y++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    int a = (y * width) + x, b = a + 1, c = a + width + 1, d = a + width;
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = c;
                }
            }
            return triangles;
        }

        internal static int[] TriangleStarts(int[] triangles)
        {
            var starts = new int[triangles.Length / 3];
            for (int i = 0; i < starts.Length; i++) starts[i] = i * 3;
            return starts;
        }
    }
}
#endif
