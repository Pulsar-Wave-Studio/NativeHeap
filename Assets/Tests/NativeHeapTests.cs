using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using NUnit.Framework;


namespace Amarcolina.NativeHeap.Tests
{
    public class NativeHeapTests
    {
        private NativeHeap<int, Min> _heap;


        [SetUp]
        public void SetUp()
        {
            _heap = new NativeHeap<int, Min>(Allocator.Persistent);
        }

        [TearDown]
        public void TearDown()
        {
            _heap.Dispose();
        }

        [Test]
        public void TestInsertionAndRemoval()
        {
            List<int> list = new List<int>();
            for (int i = 0; i < 100; i++)
            {
                _heap.Insert(i);
                list.Add(i);
            }

            for (int i = 0; i < 1000; i++)
            {
                var min = _heap.Pop();
                Assert.That(min, Is.EqualTo(list.Min()));

                list.Remove(min);

                int toInsert = Random.Range(0, 100);
                _heap.Insert(toInsert);
                list.Add(toInsert);
            }
        }

        [Test]
        public void TestCanRemoveUsingIndex()
        {
            List<(int, NativeHeapIndex)> itemRefs = new List<(int, NativeHeapIndex)>();
            for (int i = 0; i < 100; i++)
            {
                int value = Random.Range(0, 1000);
                var itemRef = _heap.Insert(value);
                itemRefs.Add((value, itemRef));
            }

            foreach ((var value, var itemRef) in itemRefs)
            {
                var item = _heap.Remove(itemRef);
                Assert.That(item, Is.EqualTo(value));
            }
        }

        [Test]
        public void TestRemovingTwiceThrowsException()
        {
            InconclusiveIfNoSafety();

            for (int i = 0; i < 10; i++)
                _heap.Insert(i);

            var itemRef = _heap.Insert(5);

            for (int i = 0; i < 10; i++)
                _heap.Insert(i);

            _heap.Remove(itemRef);

            Assert.That(() => { _heap.Remove(itemRef); }, Throws.ArgumentException);
        }

        [Test]
        public void TestIndicesBecomeInvalidAfterPopping()
        {
            InconclusiveIfNoSafety();

            List<NativeHeapIndex> indices = new List<NativeHeapIndex>();
            for (int i = 0; i < 10; i++)
            {
                indices.Add(_heap.Insert(Random.Range(0, 100)));
            }

            for (int i = 0; i < 10; i++)
            {
                _heap.Pop();
            }

            foreach (var index in indices)
            {
                Assert.That(_heap.IsValidIndex(index), Is.False);
            }
        }

        [Test]
        public void TestIndicesBecomeInvalidAfterClearing()
        {
            InconclusiveIfNoSafety();

            List<NativeHeapIndex> indices = new List<NativeHeapIndex>();
            for (int i = 0; i < 100; i++)
            {
                indices.Add(_heap.Insert(i));
            }

            _heap.Clear();

            foreach (var index in indices)
            {
                Assert.That(_heap.IsValidIndex(index), Is.False);
            }
        }

        [Test]
        public void TestIndicesAreStillValidAfterRealloc()
        {
            InconclusiveIfNoSafety();

            List<NativeHeapIndex> indices = new List<NativeHeapIndex>();
            for (int i = 0; i < 100; i++)
            {
                indices.Add(_heap.Insert(i));
            }

            _heap.Capacity *= 2;

            for (int i = 0; i < 100; i++)
            {
                Assert.That(_heap.Peek(), Is.EqualTo(i));
                _heap.Remove(indices[i]);
            }
            Assert.That(_heap.Count, Is.Zero);
        }

        [Test]
        public void TestIndicesFromOneHeapAreInvalidForAnother()
        {
            InconclusiveIfNoSafety();

            using NativeHeap<int, Min> heap2 = new NativeHeap<int, Min>(Allocator.Temp);
            NativeHeapIndex index = _heap.Insert(0);
            heap2.Insert(0);

            Assert.That(heap2.IsValidIndex(index), Is.False);
            Assert.That(() => { heap2.Remove(index); }, Throws.ArgumentException);
        }

        [Test]
        public void TestPeekIsSameAsPop()
        {
            for (int i = 0; i < 100; i++)
            {
                _heap.Insert(Random.Range(0, 1000));
            }

            while (_heap.Count > 0)
            {
                int value1 = _heap.Peek();
                int value2 = _heap.Pop();

                Assert.That(value1, Is.EqualTo(value2));
            }
        }

        [Test]
        public void TestRemoveFromMiddle()
        {
            List<int> items = new List<int>();
            int GetValue() => Random.value > 0.5f ? Random.Range(0, 1000) : Random.Range(1001, 2000);

            for (int i = 0; i < 100; i++)
            {
                var value = GetValue();
                items.Add(value);
                _heap.Insert(value);
            }

            var index = _heap.Insert(1000);

            for (int i = 0; i < 100; i++)
            {
                var value = GetValue();
                items.Add(value);
                _heap.Insert(value);
            }

            _heap.Remove(index);

            foreach (var item in items.OrderBy(i => i))
            {
                Assert.That(_heap.Pop(), Is.EqualTo(item));
            }
        }

        [Test]
        public void TestCopyReflectsChanges()
        {
            var heapCopy = _heap;
            heapCopy.Insert(5);
            heapCopy.Capacity *= 2;

            Assert.That(_heap.Peek(), Is.EqualTo(5));

            heapCopy.Pop();

            Assert.That(_heap.Count, Is.Zero);
            Assert.That(_heap.Capacity, Is.EqualTo(heapCopy.Capacity));
        }

        private void InconclusiveIfNoSafety()
        {
            bool isOn = false;
            CheckSafetyChecks(ref isOn);
            if (!isOn)
            {
                Assert.Inconclusive("This test requires safety checks");
            }
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void CheckSafetyChecks(ref bool isOn)
        {
            isOn = true;
        }
    }
}
