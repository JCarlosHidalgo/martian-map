using System;

namespace MartianMap.Routing
{
    /// <summary>
    /// A binary min-heap of node indices with decrease-key by <c>heapIndex[node]</c>
    /// (docs/guide/chapters/tablet.md, "Rutas (D7)"): <c>PriorityQueue</c> does not exist in
    /// netstandard2.1. Entries are ordered by <c>(key, node)</c>, so equal keys pop the lower node
    /// first and the order does not depend on the order of insertion. Not thread-safe: each search
    /// owns one.
    /// </summary>
    internal sealed class NodeHeap
    {
        private int[] heapIndex = Array.Empty<int>();
        private int[] nodes = new int[16];
        private float[] keys = new float[16];

        /// <summary>The number of nodes in the heap.</summary>
        internal int Count { get; private set; }

        /// <summary>Makes room for nodes <c>0 … nodeCount − 1</c>. It never shrinks.</summary>
        internal void EnsureNodes(int nodeCount)
        {
            if (nodeCount <= heapIndex.Length)
            {
                return;
            }

            int old = heapIndex.Length;
            Array.Resize(ref heapIndex, nodeCount);
            for (int n = old; n < nodeCount; n++)
            {
                heapIndex[n] = -1;
            }
        }

        internal bool Contains(int node) => heapIndex[node] >= 0;

        /// <summary>
        /// Inserts <paramref name="node"/> with <paramref name="key"/>, or lowers its key if
        /// <paramref name="key"/> is lower than the one it has. It never raises a key. Returns
        /// whether anything changed.
        /// </summary>
        internal bool PushOrDecrease(int node, float key)
        {
            int slot = heapIndex[node];
            if (slot < 0)
            {
                if (Count == nodes.Length)
                {
                    Array.Resize(ref nodes, nodes.Length * 2);
                    Array.Resize(ref keys, keys.Length * 2);
                }

                slot = Count++;
                nodes[slot] = node;
                keys[slot] = key;
                heapIndex[node] = slot;
            }
            else if (key < keys[slot])
            {
                keys[slot] = key;
            }
            else
            {
                return false;
            }

            SiftUp(slot);
            return true;
        }

        /// <summary>Removes and returns the node with the lowest key.</summary>
        internal int PopMin(out float key)
        {
            if (Count == 0)
            {
                throw new InvalidOperationException("The heap is empty.");
            }

            int top = nodes[0];
            key = keys[0];
            heapIndex[top] = -1;
            Count--;
            if (Count > 0)
            {
                nodes[0] = nodes[Count];
                keys[0] = keys[Count];
                heapIndex[nodes[0]] = 0;
                SiftDown(0);
            }

            return top;
        }

        /// <summary>Empties the heap in O(Count): only the nodes it holds are reset.</summary>
        internal void Clear()
        {
            for (int s = 0; s < Count; s++)
            {
                heapIndex[nodes[s]] = -1;
            }

            Count = 0;
        }

        private bool Less(int a, int b) =>
            keys[a] < keys[b] || (keys[a] == keys[b] && nodes[a] < nodes[b]);

        private void SiftUp(int slot)
        {
            while (slot > 0)
            {
                int parent = (slot - 1) >> 1;
                if (!Less(slot, parent))
                {
                    return;
                }

                Swap(slot, parent);
                slot = parent;
            }
        }

        private void SiftDown(int slot)
        {
            while (true)
            {
                int left = (2 * slot) + 1;
                if (left >= Count)
                {
                    return;
                }

                int child = left + 1 < Count && Less(left + 1, left) ? left + 1 : left;
                if (!Less(child, slot))
                {
                    return;
                }

                Swap(slot, child);
                slot = child;
            }
        }

        private void Swap(int a, int b)
        {
            int na = nodes[a];
            float ka = keys[a];
            nodes[a] = nodes[b];
            keys[a] = keys[b];
            nodes[b] = na;
            keys[b] = ka;
            heapIndex[nodes[a]] = a;
            heapIndex[nodes[b]] = b;
        }
    }
}
