using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MartianMap.Routing.Tests
{
    /// <summary>The binary heap with decrease-key by node (docs/guide/chapters/tablet.md, "Rutas (D7)").</summary>
    public class NodeHeapTests
    {
        [Test]
        public void PopsInKeyOrder()
        {
            var heap = Heap(5);
            float[] keys = { 5, 1, 4, 2, 3 };
            for (int n = 0; n < keys.Length; n++)
            {
                Assert.That(heap.PushOrDecrease(n, keys[n]), Is.True);
            }

            Assert.That(PopAll(heap), Is.EqualTo(new[] { 1, 3, 4, 2, 0 }));
        }

        [Test]
        public void DecreaseKeyMovesANodeUp()
        {
            var heap = Heap(10);
            for (int n = 0; n < 10; n++)
            {
                heap.PushOrDecrease(n, 10 - n);
            }

            Assert.That(heap.PushOrDecrease(0, 0.5f), Is.True);
            Assert.That(heap.PopMin(out float key), Is.EqualTo(0));
            Assert.That(key, Is.EqualTo(0.5f));
        }

        [Test]
        public void AHigherKeyIsIgnored()
        {
            var heap = Heap(5);
            heap.PushOrDecrease(3, 2);
            Assert.That(heap.PushOrDecrease(3, 7), Is.False);
            Assert.That(heap.PushOrDecrease(3, 2), Is.False);
            Assert.That(heap.PopMin(out float key), Is.EqualTo(3));
            Assert.That(key, Is.EqualTo(2f));
        }

        [Test]
        public void EqualKeysPopTheLowerNodeFirst()
        {
            var heap = Heap(8);
            heap.PushOrDecrease(7, 1);
            heap.PushOrDecrease(2, 1);
            heap.PushOrDecrease(5, 1);
            Assert.That(PopAll(heap), Is.EqualTo(new[] { 2, 5, 7 }));
        }

        [Test]
        public void ClearResetsOnlyWhatItHolds()
        {
            var heap = Heap(6);
            heap.PushOrDecrease(4, 3);
            heap.PushOrDecrease(1, 2);
            heap.PushOrDecrease(5, 9);
            heap.Clear();
            Assert.That(heap.Count, Is.EqualTo(0));
            for (int n = 0; n < 6; n++)
            {
                Assert.That(heap.Contains(n), Is.False, $"node {n}");
            }

            heap.PushOrDecrease(5, 3);
            heap.PushOrDecrease(0, 4);
            heap.PushOrDecrease(4, 1);
            Assert.That(PopAll(heap), Is.EqualTo(new[] { 4, 5, 0 }));
        }

        [Test]
        public void EnsureNodesGrowsAndNeverShrinks()
        {
            var heap = Heap(3);
            heap.PushOrDecrease(2, 1);
            heap.EnsureNodes(1);
            Assert.That(heap.Contains(2), Is.True);
            heap.EnsureNodes(100);
            heap.PushOrDecrease(99, 0);
            Assert.That(PopAll(heap), Is.EqualTo(new[] { 99, 2 }));
        }

        [Test]
        public void HeapIndexStaysConsistent()
        {
            const int nodes = 300;
            var heap = Heap(nodes);
            var keys = new Dictionary<int, float>();
            var reference = new SortedSet<(float Key, int Node)>();
            var rng = new SplitMix64(12345);
            for (int op = 0; op < 10000; op++)
            {
                if (rng.Next() % 3 == 0 && reference.Count > 0)
                {
                    (float key, int node) = reference.Min;
                    reference.Remove(reference.Min);
                    keys.Remove(node);
                    Assert.That(heap.PopMin(out float got), Is.EqualTo(node), $"op {op}");
                    Assert.That(got, Is.EqualTo(key), $"op {op}");
                    Assert.That(heap.Contains(node), Is.False, $"op {op}");
                }
                else
                {
                    int node = (int)(rng.Next() % nodes);
                    float key = (rng.Next() % 1000) / 8f;
                    bool lower = !keys.TryGetValue(node, out float old) || key < old;
                    if (lower)
                    {
                        reference.Remove((old, node));
                        reference.Add((key, node));
                        keys[node] = key;
                    }

                    Assert.That(heap.PushOrDecrease(node, key), Is.EqualTo(lower), $"op {op}");
                }

                Assert.That(heap.Count, Is.EqualTo(reference.Count), $"op {op}");
            }

            for (int n = 0; n < nodes; n++)
            {
                Assert.That(heap.Contains(n), Is.EqualTo(keys.ContainsKey(n)), $"node {n}");
            }

            while (reference.Count > 0)
            {
                Assert.That(heap.PopMin(out _), Is.EqualTo(reference.Min.Node));
                reference.Remove(reference.Min);
            }
        }

        [Test]
        public void PopOnEmptyThrows()
        {
            var heap = Heap(2);
            Assert.Throws<InvalidOperationException>(() => heap.PopMin(out _));
            heap.PushOrDecrease(1, 1);
            heap.PopMin(out _);
            Assert.Throws<InvalidOperationException>(() => heap.PopMin(out _));
        }

        private static NodeHeap Heap(int nodes)
        {
            var heap = new NodeHeap();
            heap.EnsureNodes(nodes);
            return heap;
        }

        private static int[] PopAll(NodeHeap heap)
        {
            var order = new List<int>();
            while (heap.Count > 0)
            {
                order.Add(heap.PopMin(out _));
            }

            return order.ToArray();
        }

        /// <summary>A local SplitMix64, so the sequence is the same on every runtime.</summary>
        private sealed class SplitMix64
        {
            private ulong state;

            public SplitMix64(ulong seed) => state = seed;

            public ulong Next()
            {
                ulong z = state += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
