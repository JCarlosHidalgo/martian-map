using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// The graph searches over the 16-connected grid (docs/guide/chapters/tablet.md, "Rutas (D7)").
    /// Every edge is a segment of <see cref="Cost"/>: the searches never compute cost themselves.
    /// The working arrays live in a <see cref="Workspace"/> per thread, reused between calls.
    /// </summary>
    internal static class Search
    {
        /// <summary>
        /// The 16 moves, in the order that fixes ties: the 8 neighbours counter-clockwise from east,
        /// then the 8 knight moves counter-clockwise from (2, 1).
        /// </summary>
        internal static readonly (int Di, int Dj)[] Moves =
        {
            (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1),
            (2, 1), (1, 2), (-1, 2), (-2, 1), (-2, -1), (-1, -2), (1, -2), (2, -1),
        };

        /// <summary>
        /// The cheapest path from <paramref name="start"/> to <paramref name="goal"/>, both included,
        /// minimising <see cref="Cost.SegmentCostS"/> with <paramref name="amberMult"/>; or
        /// <c>null</c>, with <paramref name="costS"/> = +∞, if there is none. <paramref name="costS"/>
        /// is the cost of the returned path, summed in <c>double</c> over its edges. The heuristic is
        /// the horizontal distance over <see cref="Cost.MaxSpeedMps"/>, admissible and consistent, so
        /// a closed node is never reopened. A move that leaves the grid is skipped.
        /// </summary>
        internal static int[] AStar(CostGrid g, int start, int goal, double v0Mps, CostParams p, double amberMult, out double costS)
        {
            int count = g.Width * g.Height;
            if ((uint)start >= (uint)count || (uint)goal >= (uint)count)
            {
                throw new ArgumentOutOfRangeException(nameof(start), "Node outside the grid.");
            }

            costS = double.PositiveInfinity;
            if (Cost.IsBlockedNode(g, start, p) || Cost.IsBlockedNode(g, goal, p))
            {
                return null;
            }

            if (start == goal)
            {
                costS = 0;
                return new[] { start };
            }

            Workspace w = Workspace.Rent(count);
            try
            {
                int width = g.Width, height = g.Height;
                int gi = goal % width, gj = goal / width;
                double hPerNode = g.CellM / Cost.MaxSpeedMps(v0Mps, p);
                float[] key = w.Key;
                byte[] move = w.Move;
                NodeHeap heap = w.Heap;

                w.Touch(start);
                key[start] = 0;
                move[start] = Workspace.Root;
                heap.PushOrDecrease(start, (float)Heuristic(start % width - gi, start / width - gj, hPerNode));
                bool found = false;
                while (heap.Count > 0)
                {
                    int u = heap.PopMin(out _);
                    if (u == goal)
                    {
                        found = true;
                        break;
                    }

                    move[u] |= Workspace.Closed;
                    int ui = u % width, uj = u / width;
                    float ku = key[u];
                    for (int m = 0; m < Moves.Length; m++)
                    {
                        int vi = ui + Moves[m].Di, vj = uj + Moves[m].Dj;
                        if ((uint)vi >= (uint)width || (uint)vj >= (uint)height)
                        {
                            continue;
                        }

                        int v = vj * width + vi;
                        byte mv = move[v];
                        if (mv != Workspace.Unseen && (mv & Workspace.Closed) != 0)
                        {
                            continue;
                        }

                        if (Cost.IsBlockedNode(g, v, p))
                        {
                            continue;
                        }

                        double edge = Cost.SegmentCostS(g, u, v, v0Mps, p, amberMult, out _);
                        if (double.IsPositiveInfinity(edge))
                        {
                            continue;
                        }

                        float kv = (float)(ku + edge);
                        if (kv < key[v])
                        {
                            if (mv == Workspace.Unseen)
                            {
                                w.Touch(v);
                            }

                            key[v] = kv;
                            move[v] = (byte)m;
                            heap.PushOrDecrease(v, (float)(kv + Heuristic(vi - gi, vj - gj, hPerNode)));
                        }
                    }
                }

                if (!found)
                {
                    return null;
                }

                int length = 1;
                for (int n = goal; n != start; n = Parent(n, move[n], width))
                {
                    length++;
                }

                var path = new int[length];
                int at = length - 1;
                for (int n = goal; ; n = Parent(n, move[n], width))
                {
                    path[at--] = n;
                    if (n == start)
                    {
                        break;
                    }
                }

                costS = 0;
                for (int k = 1; k < path.Length; k++)
                {
                    costS += Cost.SegmentCostS(g, path[k - 1], path[k], v0Mps, p, amberMult, out _);
                }

                return path;
            }
            finally
            {
                Workspace.Return(w);
            }
        }

        private static double Heuristic(int di, int dj, double hPerNode) =>
            Math.Sqrt((double)di * di + (double)dj * dj) * hPerNode;

        /// <summary>The node <paramref name="n"/> was reached from, by move <paramref name="m"/> (bits 0–3).</summary>
        private static int Parent(int n, byte m, int width)
        {
            (int di, int dj) = Moves[m & 0x0F];
            return n - (dj * width) - di;
        }

        /// <summary>
        /// The working arrays of one search, kept per thread (<c>[ThreadStatic]</c>) and reused: they
        /// grow to the largest grid seen and stay. A returned workspace is clean: <c>Key</c> is +∞
        /// and <c>Move</c> is <see cref="Unseen"/> on every node, and the heap is empty. A reentrant
        /// call that finds it taken makes another.
        /// </summary>
        private sealed class Workspace
        {
            /// <summary>Not reached yet.</summary>
            internal const byte Unseen = 0xFF;

            /// <summary>Bit 7: the node is closed.</summary>
            internal const byte Closed = 0x80;

            /// <summary>Bit 6: the node is the root, with no parent.</summary>
            internal const byte Root = 0x40;

            [ThreadStatic]
            private static Workspace cached;

            private int[] touched = new int[64];
            private int touchedCount;

            /// <summary>The best cost found to each node.</summary>
            internal float[] Key { get; private set; } = Array.Empty<float>();

            /// <summary>Bits 0–3: the move from the parent; bit 6: root; bit 7: closed; 0xFF: unseen.</summary>
            internal byte[] Move { get; private set; } = Array.Empty<byte>();

            internal NodeHeap Heap { get; } = new NodeHeap();

            internal static Workspace Rent(int nodeCount)
            {
                Workspace w = cached ?? new Workspace();
                cached = null;
                w.Ensure(nodeCount);
                return w;
            }

            internal static void Return(Workspace w)
            {
                w.Reset();
                cached = w;
            }

            /// <summary>Records a node reached for the first time, to reset only those.</summary>
            internal void Touch(int node)
            {
                if (touchedCount == touched.Length)
                {
                    int[] grown = new int[touched.Length * 2];
                    Array.Copy(touched, grown, touchedCount);
                    touched = grown;
                }

                touched[touchedCount++] = node;
            }

            private void Ensure(int nodeCount)
            {
                Heap.EnsureNodes(nodeCount);
                if (Key.Length >= nodeCount)
                {
                    return;
                }

                var key = new float[nodeCount];
                var move = new byte[nodeCount];
                for (int n = 0; n < nodeCount; n++)
                {
                    key[n] = float.PositiveInfinity;
                    move[n] = Unseen;
                }

                Key = key;
                Move = move;
            }

            private void Reset()
            {
                for (int t = 0; t < touchedCount; t++)
                {
                    Key[touched[t]] = float.PositiveInfinity;
                    Move[touched[t]] = Unseen;
                }

                touchedCount = 0;
                Heap.Clear();
            }
        }
    }
}
