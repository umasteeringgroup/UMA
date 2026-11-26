#if UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.ClothesDesigner.Editor.Tests
{
    /// <summary>The Roller lifts the pressed sheet of cloth out over the cloth behind it, and nothing else.</summary>
    public sealed class ClothRollerTests
    {
        private const float Gap = 0.002f;
        private const float Spacing = 0.01f;

        private sealed class Cloth
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();

            /// <summary>A grid of (columns + 1) x (rows + 1) points; returns the index of its first point.</summary>
            public int Grid(int columns, int rows, System.Func<int, int, Vector3> at)
            {
                int first = positions.Count;
                for (int v = 0; v <= rows; v++) for (int u = 0; u <= columns; u++) positions.Add(at(u, v));
                for (int v = 0; v < rows; v++)
                {
                    for (int u = 0; u < columns; u++)
                    {
                        int a = first + v * (columns + 1) + u, b = a + 1, c = a + columns + 1, d = c + 1;
                        triangles.AddRange(new[] { a, b, d, a, d, c });
                    }
                }
                return first;
            }

            public List<int>[] Neighbours()
            {
                var n = new List<int>[positions.Count];
                for (int i = 0; i < n.Length; i++) n[i] = new List<int>();
                for (int t = 0; t < triangles.Count; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        int a = triangles[t + k], b = triangles[t + (k + 1) % 3];
                        if (!n[a].Contains(b)) n[a].Add(b);
                        if (!n[b].Contains(a)) n[b].Add(a);
                    }
                }
                return n;
            }
        }

        private static Vector3 Out(Vector3 p) => Vector3.forward;

        private static ClothRoller.Result Roll(Cloth cloth, Vector3[] p, int start, float radius = 0.03f) =>
            ClothRoller.Roll(p, cloth.triangles.ToArray(), cloth.Neighbours(), Out, start, radius, Gap, 1f);

        [TestCase(0.0005f, Description = "the front sheet has sunk 0.5 mm behind the other cloth")]
        [TestCase(-0.0003f, Description = "the other cloth is behind, but only 0.3 mm")]
        public void Roll_LiftsThePressedSheet_TheThicknessClearOfTheClothBehind(float behindZ)
        {
            var cloth = new Cloth();
            int front = cloth.Grid(10, 10, (u, v) => new Vector3(u * Spacing, v * Spacing, 0f));
            int behind = cloth.Grid(10, 10, (u, v) => new Vector3(u * Spacing + 0.003f, v * Spacing + 0.003f, behindZ));
            Vector3[] p = cloth.positions.ToArray();
            int centre = front + 5 * 11 + 5;
            ClothRoller.Result r = Roll(cloth, p, centre);

            Assert.That(r.lifted, Is.GreaterThan(0));
            Assert.That(p[centre].z, Is.EqualTo(behindZ + Gap).Within(0.00005f), "the pressed point ends the thickness in front");
            for (int i = behind; i < behind + 121; i++) Assert.That(p[i], Is.EqualTo(cloth.positions[i]), "the cloth behind does not move");
            Assert.That(p[front], Is.EqualTo(cloth.positions[front]), "beyond the brush nothing moves");
            for (int i = front; i < front + 121; i++) Assert.That(p[i].z, Is.GreaterThanOrEqualTo(-1e-7f), "nothing is pushed back");
        }

        [Test]
        public void Roll_LeavesClothThatIsAlreadyClear()
        {
            var cloth = new Cloth();
            int front = cloth.Grid(10, 10, (u, v) => new Vector3(u * Spacing, v * Spacing, 0f));
            cloth.Grid(10, 10, (u, v) => new Vector3(u * Spacing, v * Spacing, -0.005f));
            Vector3[] p = cloth.positions.ToArray();
            ClothRoller.Result r = Roll(cloth, p, front + 60);
            Assert.That(r.lifted, Is.EqualTo(0));
            Assert.That(p, Is.EqualTo(cloth.positions.ToArray()));
        }

        [TestCase(-0.001f, Description = "the fold lies 1 mm under: too close")]
        [TestCase(0.0005f, Description = "the fold pokes 0.5 mm through")]
        public void Roll_StopsAtAFold_AndLiftsOnlyTheSideThatWasPressed(float underZ)
        {
            // One strip folded back under itself at u = 10, like a pleat: the under half is the same cloth.
            var cloth = new Cloth();
            cloth.Grid(20, 6, (u, v) => u <= 10 ? new Vector3(u * Spacing, v * Spacing, 0f) : new Vector3((20 - u) * Spacing, v * Spacing, underZ));
            Vector3[] p = cloth.positions.ToArray();
            int pressed = 3 * 21 + 4;
            ClothRoller.Result r = Roll(cloth, p, pressed);

            Assert.That(r.sheet, Is.LessThan(11 * 7), "the walk does not go round the fold");
            Assert.That(p[pressed].z, Is.EqualTo(underZ + Gap).Within(0.00005f));
            for (int v = 0; v <= 6; v++) for (int u = 11; u <= 20; u++) Assert.That(p[v * 21 + u], Is.EqualTo(cloth.positions[v * 21 + u]), "the folded-under half does not move");
        }

        [Test]
        public void Roll_DoesNotCountClothSewnBesideAPoint_AsBehindIt()
        {
            // Two pieces meeting at a seam, at an angle: next to the seam they are closer than any thickness.
            var cloth = new Cloth();
            int a = cloth.Grid(6, 6, (u, v) => new Vector3(u * Spacing, v * Spacing, 0f));
            int b = cloth.Grid(6, 6, (u, v) => new Vector3(0.06f + u * Spacing * 0.2f, v * Spacing, -u * Spacing));
            List<int>[] neighbours = cloth.Neighbours();
            for (int v = 0; v <= 6; v++) { int x = a + v * 7 + 6, y = b + v * 7; neighbours[x].Add(y); neighbours[y].Add(x); }
            Vector3[] p = cloth.positions.ToArray();
            ClothRoller.Result r = ClothRoller.Roll(p, cloth.triangles.ToArray(), neighbours, Out, a + 3 * 7 + 5, 0.03f, Gap, 1f);
            Assert.That(r.lifted, Is.EqualTo(0));
        }
    }
}
#endif
