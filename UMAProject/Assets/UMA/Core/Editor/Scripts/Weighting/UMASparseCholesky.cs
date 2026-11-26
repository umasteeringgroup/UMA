using System;
using System.Collections.Generic;
using UnityEngine;

namespace UMA.Editors.Weighting
{
    /// <summary>
    /// A sparse symmetric positive definite matrix factored once (A = L Lᵀ) and solved for any number of right-hand
    /// sides. Rows are reordered by nested dissection on the nodes' positions, which keeps the factor small on
    /// meshes. The factorization follows the up-looking Cholesky of Davis' CSparse.
    /// </summary>
    public sealed class UMASparseCholesky
    {
        private int n;
        private int[] perm, inv;
        private int[] Lp, Li;
        private double[] Lx;

        public int Size => n;
        public int FactorNonZeros => Lp != null ? Lp[n] : 0;

        /// <summary>
        /// Factors the matrix given as rows: for each row, its column indices and values (both halves, the diagonal
        /// included, each column once). <paramref name="positions"/> (one per row) guide the ordering. False when the
        /// matrix is not positive definite.
        /// </summary>
        public bool Factor(List<int>[] columns, List<double>[] values, Vector3[] positions)
        {
            n = columns.Length;
            perm = NestedDissection(columns, positions);
            inv = new int[n];
            for (int k = 0; k < n; k++) inv[perm[k]] = k;

            // The upper triangle of the reordered matrix, by column: column k holds rows i <= k.
            var Ap = new int[n + 1];
            for (int k = 0; k < n; k++)
            {
                int row = perm[k];
                int c = 0;
                foreach (int j in columns[row]) if (inv[j] <= k) c++;
                Ap[k + 1] = Ap[k] + c;
            }
            var Ai = new int[Ap[n]];
            var Ax = new double[Ap[n]];
            for (int k = 0; k < n; k++)
            {
                int row = perm[k], p = Ap[k];
                List<int> cols = columns[row];
                List<double> vals = values[row];
                for (int e = 0; e < cols.Count; e++)
                {
                    int pi = inv[cols[e]];
                    if (pi > k) continue;
                    Ai[p] = pi; Ax[p] = vals[e]; p++;
                }
            }

            // Elimination tree.
            var parent = new int[n];
            var ancestor = new int[n];
            for (int k = 0; k < n; k++)
            {
                parent[k] = -1; ancestor[k] = -1;
                for (int p = Ap[k]; p < Ap[k + 1]; p++)
                {
                    int i = Ai[p];
                    while (i != -1 && i < k)
                    {
                        int next = ancestor[i];
                        ancestor[i] = k;
                        if (next == -1) parent[i] = k;
                        i = next;
                    }
                }
            }

            // Column counts of L from each row's pattern.
            var mark = new int[n];
            for (int i = 0; i < n; i++) mark[i] = -1;
            var stack = new int[n];
            var pattern = new int[n];
            var colCount = new int[n];
            for (int k = 0; k < n; k++)
            {
                int top = Reach(Ap, Ai, parent, k, mark, stack, pattern);
                for (int t = top; t < n; t++) colCount[pattern[t]]++;
                colCount[k]++;
            }
            Lp = new int[n + 1];
            for (int k = 0; k < n; k++) Lp[k + 1] = Lp[k] + colCount[k];
            Li = new int[Lp[n]];
            Lx = new double[Lp[n]];
            var next2 = new int[n];
            Array.Copy(Lp, next2, n);
            var x = new double[n];
            for (int i = 0; i < n; i++) mark[i] = -1;

            for (int k = 0; k < n; k++)
            {
                int top = Reach(Ap, Ai, parent, k, mark, stack, pattern);
                x[k] = 0;
                for (int p = Ap[k]; p < Ap[k + 1]; p++) x[Ai[p]] += Ax[p];
                double d = x[k];
                x[k] = 0;
                for (; top < n; top++)
                {
                    int i = pattern[top];
                    double lki = x[i] / Lx[Lp[i]];
                    x[i] = 0;
                    for (int p = Lp[i] + 1; p < next2[i]; p++) x[Li[p]] -= Lx[p] * lki;
                    d -= lki * lki;
                    int q = next2[i]++;
                    Li[q] = k; Lx[q] = lki;
                }
                if (d <= 0 || double.IsNaN(d)) return false;
                int qd = next2[k]++;
                Li[qd] = k; Lx[qd] = Math.Sqrt(d);
            }
            return true;
        }

        /// <summary>The nonzero pattern of row k of L (CSparse's ereach), left in pattern[top..n-1].</summary>
        private int Reach(int[] Ap, int[] Ai, int[] parent, int k, int[] mark, int[] stack, int[] pattern)
        {
            int top = n;
            mark[k] = k;
            for (int p = Ap[k]; p < Ap[k + 1]; p++)
            {
                int i = Ai[p];
                if (i > k) continue;
                int len = 0;
                for (; mark[i] != k; i = parent[i])
                {
                    stack[len++] = i;
                    mark[i] = k;
                }
                while (len > 0) pattern[--top] = stack[--len];
            }
            return top;
        }

        /// <summary>Solves A x = b in place (b in the original row order).</summary>
        public void Solve(double[] b)
        {
            var y = new double[n];
            for (int k = 0; k < n; k++) y[k] = b[perm[k]];
            for (int j = 0; j < n; j++)
            {
                y[j] /= Lx[Lp[j]];
                for (int p = Lp[j] + 1; p < Lp[j + 1]; p++) y[Li[p]] -= Lx[p] * y[j];
            }
            for (int j = n - 1; j >= 0; j--)
            {
                for (int p = Lp[j] + 1; p < Lp[j + 1]; p++) y[j] -= Lx[p] * y[Li[p]];
                y[j] /= Lx[Lp[j]];
            }
            for (int k = 0; k < n; k++) b[perm[k]] = y[k];
        }

        /// <summary>
        /// An ordering by nested dissection: split the rows in half along their longest extent, order each half, and
        /// put the rows that join the halves (the separator) last.
        /// </summary>
        private static int[] NestedDissection(List<int>[] columns, Vector3[] positions)
        {
            int count = columns.Length;
            var order = new List<int>(count);
            var side = new int[count];
            var all = new int[count];
            for (int i = 0; i < count; i++) all[i] = i;
            var work = new Stack<(int[] set, bool emitOnly)>();
            // An explicit stack of tasks: parts to split, and separators to put after their parts.
            work.Push((all, false));
            var emitted = new bool[count];
            while (work.Count > 0)
            {
                (int[] set, bool emitOnly) = work.Pop();
                if (emitOnly || set.Length <= 64)
                {
                    foreach (int i in set) if (!emitted[i]) { emitted[i] = true; order.Add(i); }
                    continue;
                }
                Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
                foreach (int i in set) { min = Vector3.Min(min, positions[i]); max = Vector3.Max(max, positions[i]); }
                Vector3 size = max - min;
                int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                var sorted = (int[])set.Clone();
                Array.Sort(sorted, (a, b) =>
                {
                    int c = positions[a][axis].CompareTo(positions[b][axis]);
                    return c != 0 ? c : a.CompareTo(b);
                });
                int half = sorted.Length / 2;
                foreach (int i in set) side[i] = 0;
                for (int k = half; k < sorted.Length; k++) side[sorted[k]] = 2;
                for (int k = 0; k < half; k++) side[sorted[k]] = 1;
                var left = new List<int>(half);
                var separator = new List<int>();
                for (int k = 0; k < half; k++)
                {
                    int i = sorted[k];
                    bool joins = false;
                    foreach (int j in columns[i]) if (j != i && side[j] == 2) { joins = true; break; }
                    if (joins) separator.Add(i); else left.Add(i);
                }
                var right = new int[sorted.Length - half];
                Array.Copy(sorted, half, right, 0, right.Length);
                foreach (int i in set) side[i] = 0;
                if (left.Count == 0 || right.Length == 0)
                {
                    // Nothing to split (all rows at one place): order them as they are.
                    work.Push((sorted, true));
                    continue;
                }
                // Popped in reverse: left, then right, then the separator.
                work.Push((separator.ToArray(), true));
                work.Push((right, false));
                work.Push((left.ToArray(), false));
            }
            return order.ToArray();
        }
    }
}
