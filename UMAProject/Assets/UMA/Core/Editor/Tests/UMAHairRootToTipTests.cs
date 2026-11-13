#if UNITY_EDITOR

using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace UMA.Editors.Tests
{
    public sealed class UMAHairRootToTipTests
    {
        // A flat card: rows along +Z (from z0 to z0 + length), two vertices across X, at height y = f(row).
        private sealed class Card
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> triangles = new List<int>();
        }

        private static int AddStrip(Card card, Vector3 start, Vector3 along, Vector3 across, int rows, System.Func<float, Vector3> bend = null, bool rootAtTextureTop = true)
        {
            int first = card.positions.Count;
            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)(rows - 1);
                Vector3 offset = bend != null ? bend(t) : Vector3.zero;
                for (int s = 0; s < 2; s++)
                {
                    card.positions.Add(start + along * t + across * s + offset);
                    // Hair textures are drawn with the root at the top: v = 1 at the root.
                    card.uvs.Add(new Vector2(s, rootAtTextureTop ? 1f - t : t));
                }
                if (r < rows - 1)
                {
                    int a = first + r * 2;
                    card.triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
                }
            }
            return first;
        }

        // A head that is the floor: a large flat grid at y = 0, facing up.
        private static HairRootToTip.HeadSurface Floor()
        {
            const int n = 21;
            var positions = new Vector3[n * n];
            for (int x = 0; x < n; x++)
                for (int z = 0; z < n; z++)
                    positions[x * n + z] = new Vector3((x - n / 2) * 0.05f, 0f, (z - n / 2) * 0.05f);
            var triangles = new List<int>();
            for (int x = 0; x < n - 1; x++)
                for (int z = 0; z < n - 1; z++)
                {
                    int a = x * n + z;
                    triangles.AddRange(new[] { a, a + 1, a + n, a + 1, a + n + 1, a + n });
                }
            return new HairRootToTip.HeadSurface(positions, triangles.ToArray());
        }

        [Test]
        public void TheFloorIsAHeadSurfaceWithDistancesNegativeBelowIt()
        {
            HairRootToTip.HeadSurface floor = Floor();
            Assert.That(floor.SignedDistance(new Vector3(0.1f, 0.02f, 0.1f)), Is.EqualTo(0.02f).Within(1e-5f));
            Assert.That(floor.SignedDistance(new Vector3(0.1f, -0.004f, 0.1f)), Is.EqualTo(-0.004f).Within(1e-5f));
            Assert.That(floor.SignedDistance(new Vector3(0.1f, -0.2f, 0.1f), 1f), Is.EqualTo(0.2f).Within(1e-5f),
                "far behind an open surface is not inside the head");
            Assert.IsTrue(float.IsPositiveInfinity(floor.SignedDistance(new Vector3(0f, 0.5f, 0f), 0.1f)));
        }

        [Test]
        public void CoincidentVerticesJoinACardSplitAtASeam()
        {
            var card = new Card();
            AddStrip(card, Vector3.zero, new Vector3(0, 0, 0.1f), new Vector3(0.02f, 0, 0), 3);
            // The same strip continued, with its own copy of the seam vertices.
            AddStrip(card, new Vector3(0, 0, 0.1f), new Vector3(0, 0, 0.1f), new Vector3(0.02f, 0, 0), 3);
            AddStrip(card, new Vector3(1f, 0, 0), new Vector3(0, 0, 0.1f), new Vector3(0.02f, 0, 0), 3);
            var graph = new HairRootToTip.CardGraph(card.positions.ToArray(), card.triangles.ToArray());
            Assert.AreEqual(2, graph.IslandCount);
            Assert.AreEqual(graph.Island[0], graph.Island[11]);
            Assert.AreNotEqual(graph.Island[0], graph.Island[12]);
        }

        [Test]
        public void FromRootsGivesTheWholeRootEdgeZeroAndTheTipOne()
        {
            var card = new Card();
            AddStrip(card, Vector3.zero, new Vector3(0, 0, 0.2f), new Vector3(0.03f, 0, 0), 11);
            var graph = new HairRootToTip.CardGraph(card.positions.ToArray(), card.triangles.ToArray());
            float[] values = HairRootToTip.FromRoots(graph, new[] { 0, 1 });
            for (int r = 0; r < 11; r++)
            {
                Assert.That(values[r * 2], Is.EqualTo(r / 10f).Within(0.02f), "row " + r);
                Assert.That(values[r * 2 + 1], Is.EqualTo(values[r * 2]).Within(1e-5f), "both sides of a row match");
            }
        }

        [Test]
        public void BetweenRootsAndTipsIsEvenAlongACurvedCardAndLeavesOtherCards()
        {
            var card = new Card();
            // A card that curves down as it goes, like hanging hair.
            AddStrip(card, Vector3.zero, new Vector3(0, 0, 0.2f), new Vector3(0.03f, 0, 0), 11, t => new Vector3(0, -0.15f * t * t, 0));
            int other = AddStrip(card, new Vector3(1f, 0, 0), new Vector3(0, 0, 0.2f), new Vector3(0.03f, 0, 0), 5);
            var graph = new HairRootToTip.CardGraph(card.positions.ToArray(), card.triangles.ToArray());

            // One vertex marked at each end is enough.
            float[] values = HairRootToTip.BetweenRootsAndTips(graph, new[] { 0 }, new[] { 21 }, out int cards);
            Assert.AreEqual(1, cards);
            Assert.That(values[0], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(values[21], Is.EqualTo(1f).Within(1e-5f));
            float previous = -1f;
            for (int r = 0; r < 11; r++)
            {
                Assert.That(values[r * 2], Is.GreaterThan(previous), "rises from root to tip, row " + r);
                previous = values[r * 2];
            }
            // The other corners of the root and tip edges are close to 0 and 1.
            Assert.That(values[1], Is.LessThan(0.15f));
            Assert.That(values[20], Is.GreaterThan(0.85f));
            Assert.IsTrue(float.IsNaN(values[other]), "a card without marks is left alone");

            // With whole edges marked the ends are exact.
            values = HairRootToTip.BetweenRootsAndTips(graph, new[] { 0, 1 }, new[] { 20, 21 }, out cards);
            Assert.That(values[1], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(values[20], Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void MarksInsideACardClampBeforeTheRootAndPastTheTip()
        {
            var card = new Card();
            AddStrip(card, Vector3.zero, new Vector3(0, 0, 0.2f), new Vector3(0.03f, 0, 0), 11);
            var graph = new HairRootToTip.CardGraph(card.positions.ToArray(), card.triangles.ToArray());
            float[] values = HairRootToTip.BetweenRootsAndTips(graph, new[] { 4, 5 }, new[] { 16, 17 }, out _);
            Assert.That(values[0], Is.EqualTo(0f).Within(1e-5f), "before the root");
            Assert.That(values[10], Is.EqualTo(0.5f).Within(0.02f), "half way");
            Assert.That(values[21], Is.EqualTo(1f).Within(1e-5f), "past the tip");
        }

        [Test]
        public void FromHeadRootsEachCardTheWayItMeetsTheHead()
        {
            var card = new Card();
            var across = new Vector3(0.02f, 0, 0);
            // 1: grows out of the head: its lower end is 5 mm in it.
            int growing = AddStrip(card, new Vector3(0, -0.005f, 0), new Vector3(0, 0.1f, 0), across, 11);
            // 2: does not go in; one end hovers 2 mm above the head, the other is 6 cm up.
            int tilted = AddStrip(card, new Vector3(0.2f, 0.06f, 0), new Vector3(0, -0.058f, 0.08f), across, 11);
            // 3: lies along the head at 3 mm, both ends alike; the texture says which end is the root.
            int lying = AddStrip(card, new Vector3(-0.3f, 0.003f, 0.1f), new Vector3(0, 0, -0.15f), across, 11);
            // 4: arcs into the head only in its middle, and grows from its (texture) top end.
            int arching = AddStrip(card, new Vector3(0.3f, 0.004f, 0.2f), new Vector3(0, 0, -0.2f), across, 11,
                t => new Vector3(0, -0.012f * Mathf.Sin(t * Mathf.PI), 0));
            // 5: floats far above the head.
            int far = AddStrip(card, new Vector3(0, 0.3f, 0.3f), new Vector3(0, 0.1f, 0), across, 5);

            Vector3[] positions = card.positions.ToArray();
            var graph = new HairRootToTip.CardGraph(positions, card.triangles.ToArray());
            float[] values = HairRootToTip.FromHead(graph, positions, card.uvs.ToArray(), Floor(), HairRootToTip.HeadSettings.Default, out HairRootToTip.HeadReport report);

            Assert.AreEqual(4, report.Cards);
            Assert.AreEqual(1, report.CardsIntoHead);
            Assert.AreEqual(1, report.CardsByNearerEnd);
            Assert.AreEqual(2, report.CardsByTieRule);
            Assert.AreEqual(1, report.CardsTooFarFromHead);

            // 1: everything in the head is root; the top is the tip.
            Assert.That(values[growing], Is.EqualTo(0f), "the end in the head");
            Assert.That(values[growing + 21], Is.EqualTo(1f).Within(1e-5f));
            // 2: the end near the head is the root, although the texture says otherwise.
            Assert.That(values[tilted + 21], Is.EqualTo(0f).Within(1e-5f), "the end 2 mm from the head");
            Assert.That(values[tilted], Is.EqualTo(1f).Within(1e-5f));
            // 3 and 4: the texture's top (the first row) is the root, and the middle of 4 is not.
            Assert.That(values[lying], Is.EqualTo(0f).Within(1e-5f));
            Assert.That(values[lying + 21], Is.EqualTo(1f).Within(1e-5f));
            Assert.That(values[lying + 1], Is.EqualTo(0f).Within(1e-5f), "the whole root edge");
            Assert.That(values[lying + 10], Is.EqualTo(0.5f).Within(0.05f), "even along a card lying on the head");
            Assert.That(values[arching], Is.EqualTo(0f).Within(1e-5f), "not rooted where the middle touches");
            Assert.That(values[arching + 10], Is.EqualTo(0.5f).Within(0.05f));
            Assert.IsTrue(float.IsNaN(values[far]));

            // The other tie rules: the higher end, or the nearer one.
            HairRootToTip.HeadSettings higher = HairRootToTip.HeadSettings.Default;
            higher.tieRule = HairRootToTip.EndRule.HigherEnd;
            values = HairRootToTip.FromHead(graph, positions, card.uvs.ToArray(), Floor(), higher, out _);
            Assert.That(values[growing + 21], Is.EqualTo(1f).Within(1e-5f), "cards that are clear do not change");
            Assert.AreEqual(2, report.VertexRule[tilted]);
        }

        [Test]
        public void ColorsAreGreyFromBlackToWhiteAndKeepAlpha()
        {
            Color32 root = HairRootToTip.ToColor(0f, 77);
            Color32 tip = HairRootToTip.ToColor(1f, 200);
            Color32 half = HairRootToTip.ToColor(0.5f, 255);
            Assert.AreEqual(new Color32(0, 0, 0, 77), root);
            Assert.AreEqual(new Color32(255, 255, 255, 200), tip);
            Assert.AreEqual(128, half.r);
            Assert.AreEqual(half.r, half.g);
            Assert.AreEqual(half.r, half.b);
        }
    }
}

#endif
