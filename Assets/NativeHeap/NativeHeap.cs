using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;


namespace Amarcolina.NativeHeap
{
    /// <summary>
    /// Opaque handle for an entry in the heap.<br/>
    /// </summary>
    public struct NativeHeapIndex
    {
        internal int TableIndex;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal int Version;
        internal int StructureId;
#endif
    }

    /// <summary>
    /// This is a basic implementation of the MinHeap/MaxHeap data structure.  It allows you
    /// to insert objects into the container with a O(log(n)) cost per item, and it allows you
    /// to extract the min/max from the container with a O(log(n)) cost per item.
    /// 
    /// This implementation provides the ability to remove items from the middle of the container
    /// as well.  This is a critical operation when implementing algorithms like a-star.  When an
    /// item is added to the container, an index is returned which can be used to later remove
    /// the item no matter where it is in the heap, for the same cost of removing it if it was
    /// popped normally.
    /// 
    /// This container is parameterized with a comparator type that defines the ordering of the
    /// container.  The default form of the comparator can be used, or you can specify your own.
    /// The item that comes first in the ordering is the one that will be returned by the Pop
    /// operation.  This allows you to use the comparator to parameterize this collection into a 
    /// MinHeap, MaxHeap, or other type of ordered heap using your own custom type.
    /// 
    /// For convenience, this library contains the Min and Max comparator, which provide
    /// comparisons for all built in primitives.
    /// </summary>
    /// <typeparam name="TValue">Type of the elements in the heap.</typeparam>
    /// <typeparam name="TComparer">Type of the comparer used to order the elements in the heap.</typeparam>
    [NativeContainer]
    [DebuggerDisplay("Count = {Count}")]
    [DebuggerTypeProxy(typeof(NativeHeapDebugView<,>))]
    [StructLayout(LayoutKind.Sequential)]
    public struct NativeHeap<TValue, TComparer> : IDisposable where TValue : unmanaged where TComparer : unmanaged, IComparer<TValue>
    {
        private const int DEFAULT_CAPACITY = 128;
        private const int VALIDATION_ERROR_WRONG_INSTANCE = 1;
        private const int VALIDATION_ERROR_INVALID = 2;
        private const int VALIDATION_ERROR_REMOVED = 3;


        /// <summary>
        /// Returns whether the internal data structures have been allocated.
        /// </summary>
        public bool IsCreated
        {
            get
            {
                unsafe
                {
                    return Data != null;
                }
            }
        }

        /// <summary>
        /// Returns the number of elements that this collection can hold before the internal structures
        /// need to be reallocated.
        /// </summary>
        public int Capacity
        {
            get
            {
                unsafe
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                    return Data->Capacity;
                }
            }
            set
            {
                unsafe
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
                    if (value < Data->Count)
                    {
                        throw new ArgumentException($"Capacity of {value} cannot be smaller than count of {Data->Count}.");
                    }
#endif
                    TableValue* newTable = (TableValue*)UnsafeUtility.MallocTracked(UnsafeUtility.SizeOf<TableValue>() * value, UnsafeUtility.AlignOf<TableValue>(), Allocator, 0);
                    HeapNode<TValue>* newHeap = (HeapNode<TValue>*)UnsafeUtility.MallocTracked(UnsafeUtility.SizeOf<HeapNode<TValue>>() * value, UnsafeUtility.AlignOf<HeapNode<TValue>>(), Allocator, 0);

                    int toCopy = Data->Capacity < value ? Data->Capacity : value;
                    UnsafeUtility.MemCpy(newTable, Data->Table, toCopy * UnsafeUtility.SizeOf<TableValue>());
                    UnsafeUtility.MemCpy(newHeap, Data->Heap, toCopy * UnsafeUtility.SizeOf<HeapNode<TValue>>());

                    for (int i = 0; i < value - Data->Capacity; i++)
                    {
                        //For each new heap node, make sure that it has a new unique index
                        newHeap[i + Data->Capacity] = new HeapNode<TValue>()
                        {
                            TableIndex = i + Data->Capacity
                        };

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        //For each new table value, make sure it has a specific version
                        newTable[i + Data->Capacity] = new TableValue()
                        {
                            Version = 1
                        };
#endif
                    }

                    UnsafeUtility.FreeTracked(Data->Table, Allocator);
                    UnsafeUtility.FreeTracked(Data->Heap, Allocator);

                    Data->Table = newTable;
                    Data->Heap = newHeap;

                    Data->Capacity = value;
                }
            }
        }

        /// <summary>
        /// Returns the number of elements currently contained inside this collection.
        /// </summary>
        public int Count
        {
            get
            {
                unsafe
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                    return Data->Count;
                }
            }
        }

        /// <summary>
        /// Gets or sets the comparator used for this Heap. Note that you can only set the comparator
        /// when the Heap is empty.
        /// </summary>
        public TComparer Comparator
        {
            get
            {
                unsafe
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
                    return Data->Comparator;
                }
            }
            set
            {
                unsafe
                {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);

                    if (Data->Count != 0)
                    {
                        throw new InvalidOperationException("Can only change the comparator of a NativeHeap when it is empty.");
                    }
#endif
                    Data->Comparator = value;
                }
            }
        }


#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private static readonly SharedStatic<int> s_nextId = SharedStatic<int>.GetOrCreate<int>();
        private int _id;

        private AtomicSafetyHandle m_Safety;
        private static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<NativeHeap<TValue, TComparer>>();
#endif

        [NativeDisableUnsafePtrRestriction] internal unsafe HeapData<TValue, TComparer>* Data;
        private Allocator Allocator;


        /// <summary>
        /// Constructs a new NativeHeap using the given Allocator.  You must call Dispose on this collection
        /// when you are finished with it.
        /// </summary>
        /// <param name="allocator">
        /// You must specify an allocator to use for the creation of the internal data structures.
        /// </param>
        public NativeHeap(Allocator allocator) : this(DEFAULT_CAPACITY, default, allocator) { }

        /// <summary>
        /// Constructs a new NativeHeap using the given Allocator.  You must call Dispose on this collection
        /// when you are finished with it.
        /// </summary>
        /// <param name="allocator">
        /// You must specify an allocator to use for the creation of the internal data structures.
        /// </param>
        /// <param name="initialCapacity">
        /// You can optionally specify the default number of elements this collection can contain before the internal
        /// data structures need to be re-allocated.
        /// </param>
        /// <param name="comparator">
        /// You can optionally specify the comparator used to order the elements in this collection.  The Pop operation will
        /// always return the smallest element according to the ordering specified by this comparator.
        /// </param>
        public unsafe NativeHeap(int initialCapacity, TComparer comparator, Allocator allocator)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (initialCapacity <= 0)
            {
                throw new ArgumentException("Must provide an initial capacity that is greater than zero.", nameof(initialCapacity));
            }

            if (allocator <= Allocator.None)
            {
                throw new ArgumentException("Allocator must be Temp, TempJob, Persistent or registered custom allocator", nameof(allocator));
            }

            _id = Interlocked.Increment(ref s_nextId.Data);
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
            CollectionHelper.SetStaticSafetyId<NativeHeap<TValue, TComparer>>(ref m_Safety, ref s_staticSafetyId.Data);
#endif

            Data = (HeapData<TValue, TComparer>*)UnsafeUtility.MallocTracked(UnsafeUtility.SizeOf<HeapData<TValue, TComparer>>(), UnsafeUtility.AlignOf<HeapData<TValue, TComparer>>(), allocator, 0);
            Data->Heap = (HeapNode<TValue>*)UnsafeUtility.MallocTracked(UnsafeUtility.SizeOf<HeapNode<TValue>>() * initialCapacity, UnsafeUtility.AlignOf<HeapNode<TValue>>(), allocator, 0);
            Data->Table = (TableValue*)UnsafeUtility.MallocTracked(UnsafeUtility.SizeOf<TableValue>() * initialCapacity, UnsafeUtility.AlignOf<TableValue>(), allocator, 0);

            Allocator = allocator;

            for (int i = 0; i < initialCapacity; i++)
            {
                Data->Heap[i] = new HeapNode<TValue>()
                {
                    TableIndex = i
                };
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Data->Table[i] = new TableValue()
                {
                    Version = 1
                };
#endif
            }

            Data->Count = 0;
            Data->Capacity = initialCapacity;
            Data->Comparator = comparator;
        }

        /// <summary>
        /// Disposes of this container and deallocates its memory immediately.
        /// 
        /// Any NativeHeapIndex structures obtained will be invalidated and cannot be used again.
        /// </summary>
        public void Dispose()
        {
            if (!IsCreated)
            {
                return;
            }

            unsafe
            {
                Data->Count = 0;
                Data->Capacity = 0;

                UnsafeUtility.FreeTracked(Data->Heap, Allocator);
                UnsafeUtility.FreeTracked(Data->Table, Allocator);
                UnsafeUtility.FreeTracked(Data, Allocator);

                Data = null;
            }
        }

        /// <summary>
        /// Removes all elements from this container.  Any NativeHeapIndex structures obtained will be
        /// invalidated and cannot be used again.
        /// </summary>
        public void Clear()
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);

                for (int i = 0; i < Data->Count; i++)
                {
                    HeapNode<TValue> node = Data->Heap[i];
                    Data->Table[node.TableIndex].Version++;
                }
#endif

                Data->Count = 0;
            }
        }

        /// <summary>
        /// Returns whether the given NativeHeapIndex is a valid index for this container.  If true,
        /// that index can be used to Remove the element tied to that index from the container.
        /// 
        /// This method will always return true if Unity safety checks is turned off.
        /// </summary>
        public bool IsValidIndex(NativeHeapIndex index)
        {
            bool isValid = true;
            int errorCode = 0;
            IsValidIndexInternal(index, ref isValid, ref errorCode);
            return isValid;
        }

        /// <summary>
        /// Returns the next element that would be obtained if Pop was called.  This is the first/the smallest
        /// item according to the ordering specified by the comparator.
        /// 
        /// This method is an O(1) operation.
        /// 
        /// This method will throw an InvalidOperationException if the collection is empty.
        /// </summary>
        public TValue Peek()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

            if (!TryPeek(out TValue t))
            {
                throw new InvalidOperationException("Cannot Peek NativeHeap when the count is zero.");
            }

            return t;
        }

        /// <summary>
        /// Returns the next element that would be obtained if Pop was called.  This is the first/the smallest
        /// item according to the ordering specified by the comparator.
        /// 
        /// This method is an O(1) operation.
        /// 
        /// This method will return true if an element could be obtained, or false if the container is empty.
        /// </summary>
        public bool TryPeek(out TValue t)
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif

                if (Data->Count == 0)
                {
                    t = default;
                    return false;
                }

                t = Data->Heap[0].Item;
                return true;
            }
        }

        /// <summary>
        /// Removes the first/the smallest element from the container and returns it.
        /// 
        /// This method is an O(log(n)) operation.
        /// 
        /// This method will throw an InvalidOperationException if the collection is empty.
        /// </summary>
        public TValue Pop()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

            if (!TryPop(out TValue t))
            {
                throw new InvalidOperationException("Cannot Pop NativeHeap when the count is zero.");
            }

            return t;
        }

        /// <summary>
        /// Removes the first/the smallest element from the container and returns it.
        /// 
        /// This method is an O(log(n)) operation.
        /// 
        /// This method will return true if an element could be obtained, or false if the container is empty.
        /// </summary>
        public bool TryPop(out TValue t)
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

                if (Data->Count == 0)
                {
                    t = default;
                    return false;
                }

                HeapNode<TValue> rootNode = Data->Heap[0];

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                //Update version to invalidate all existing handles
                Data->Table[rootNode.TableIndex].Version++;
#endif

                //Grab the last node off the end and remove it
                int lastNodeIndex = --Data->Count;
                HeapNode<TValue> lastNode = Data->Heap[lastNodeIndex];

                //Move the previous root to the end of the array to fill the space we just made
                Data->Heap[lastNodeIndex] = rootNode;

                //Finally insert the previously last node at the root and bubble it down
                InsertAndBubbleDown(lastNode, 0);

                t = rootNode.Item;
                return true;
            }
        }

        /// <summary>
        /// Inserts the provided element into the container.  It may later be removed by a call to Pop,
        /// TryPop, or Remove.
        /// 
        /// This method returns a NativeHeapIndex.  This index can later be used to Remove the item from
        /// the collection.  Once the item is removed by any means, this NativeHeapIndex will become invalid.
        /// If an item is re-added to the collection after it has been removed, Insert will return a NEW
        /// index that is distinct from the previous index.  Each index can only be used exactly once to
        /// remove a single item.
        /// 
        /// This method is an O(log(n)) operation.
        /// </summary>
        public NativeHeapIndex Insert(in TValue t)
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif

                if (Data->Count == Data->Capacity)
                {
                    Capacity *= 2;
                }

                HeapNode<TValue> node = Data->Heap[Data->Count];
                node.Item = t;

                int insertIndex = Data->Count++;
                InsertAndBubbleUp(node, insertIndex);

                return new NativeHeapIndex()
                {
                    TableIndex = node.TableIndex,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                    Version = Data->Table[node.TableIndex].Version,
                    StructureId = _id
#endif
                };
            }
        }

        /// <summary>
        /// Removes the element tied to this NativeHeapIndex from the container.  The NativeHeapIndex must be
        /// the result of a previous call to Insert on this container.  If the item has already been removed by
        /// any means, this method will throw an ArgumentException.
        /// 
        /// This method will invalidate the provided index.  If you re-insert the removed object, you must use
        /// the NEW index to remove it again.
        /// 
        /// This method is an O(log(n)) operation.
        /// </summary>
        public TValue Remove(NativeHeapIndex index)
        {
            unsafe
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);

                AssertValidIndex(index);
#endif
                int indexToRemove = Data->Table[index.TableIndex].HeapIndex;

                HeapNode<TValue> toRemove = Data->Heap[indexToRemove];

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                Data->Table[toRemove.TableIndex].Version++;
#endif

                HeapNode<TValue> lastNode = Data->Heap[--Data->Count];

                //First we move the node to remove to the end of the heap
                UnsafeUtility.WriteArrayElement(Data->Heap, Data->Count, toRemove);

                if (indexToRemove != 0)
                {
                    int parentIndex = (indexToRemove - 1) / 2;
                    HeapNode<TValue> parentNode = Data->Heap[parentIndex];
                    if (Data->Comparator.Compare(lastNode.Item, parentNode.Item) < 0)
                    {
                        InsertAndBubbleUp(lastNode, indexToRemove);
                        return toRemove.Item;
                    }
                }

                //If we couldn't bubble up, bubbling down instead
                InsertAndBubbleDown(lastNode, indexToRemove);

                return toRemove.Item;
            }
        }

        private unsafe void InsertAndBubbleDown(HeapNode<TValue> node, int insertIndex)
        {
            while (true)
            {
                int indexL = insertIndex * 2 + 1;
                int indexR = insertIndex * 2 + 2;

                //If the left index is off the end, we are finished
                if (indexL >= Data->Count)
                {
                    break;
                }

                if (indexR >= Data->Count || Data->Comparator.Compare(Data->Heap[indexL].Item, Data->Heap[indexR].Item) <= 0)
                {
                    //left is smaller (or the only child)
                    HeapNode<TValue> leftNode = Data->Heap[indexL];

                    if (Data->Comparator.Compare(node.Item, leftNode.Item) <= 0)
                    {
                        //Last is smaller or equal to left, we are done
                        break;
                    }

                    Data->Heap[insertIndex] = leftNode;
                    Data->Table[leftNode.TableIndex].HeapIndex = insertIndex;
                    insertIndex = indexL;
                }
                else
                {
                    //right is smaller
                    HeapNode<TValue> rightNode = Data->Heap[indexR];

                    if (Data->Comparator.Compare(node.Item, rightNode.Item) <= 0)
                    {
                        //Last is smaller than or equal to right, we are done
                        break;
                    }

                    Data->Heap[insertIndex] = rightNode;
                    Data->Table[rightNode.TableIndex].HeapIndex = insertIndex;
                    insertIndex = indexR;
                }
            }

            Data->Heap[insertIndex] = node;
            Data->Table[node.TableIndex].HeapIndex = insertIndex;
        }

        private unsafe void InsertAndBubbleUp(HeapNode<TValue> node, int insertIndex)
        {
            while (insertIndex != 0)
            {
                int parentIndex = (insertIndex - 1) / 2;
                HeapNode<TValue> parentNode = Data->Heap[parentIndex];

                //If parent is actually less or equal to us, we are ok and can break out
                if (Data->Comparator.Compare(parentNode.Item, node.Item) <= 0)
                {
                    break;
                }

                //We need to swap parent down
                Data->Heap[insertIndex] = parentNode;
                //Update table to point to new heap index
                Data->Table[parentNode.TableIndex].HeapIndex = insertIndex;

                //Restart loop trying to insert at parent index
                insertIndex = parentIndex;
            }

            Data->Heap[insertIndex] = node;
            Data->Table[node.TableIndex].HeapIndex = insertIndex;
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private void AssertValidIndex(NativeHeapIndex index)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            bool isValid = true;
            int errorCode = 0;
            IsValidIndexInternal(index, ref isValid, ref errorCode);
            if (isValid)
            {
                return;
            }

            switch (errorCode)
            {
                case VALIDATION_ERROR_WRONG_INSTANCE:
                    throw new ArgumentException("The provided ItemHandle was not valid for this NativeHeap. It was taken from a different instance.");
                case VALIDATION_ERROR_INVALID:
                    throw new ArgumentException("The provided ItemHandle was not valid for this NativeHeap.");
                case VALIDATION_ERROR_REMOVED:
                    throw new ArgumentException("The provided ItemHandle was not valid for this NativeHeap. The item it pointed to might have already been removed.");
            }
#endif
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private unsafe void IsValidIndexInternal(NativeHeapIndex index, ref bool result, ref int errorCode)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);

            if (index.StructureId != _id)
            {
                errorCode = VALIDATION_ERROR_WRONG_INSTANCE;
                result = false;
                return;
            }

            if (index.TableIndex >= Data->Capacity)
            {
                errorCode = VALIDATION_ERROR_INVALID;
                result = false;
                return;
            }

            TableValue tableValue = Data->Table[index.TableIndex];
            if (tableValue.Version != index.Version)
            {
                errorCode = VALIDATION_ERROR_REMOVED;
                result = false;
            }
#endif
        }
    }

    internal unsafe class NativeHeapDebugView<TValue, TComparer> where TValue : unmanaged where TComparer : unmanaged, IComparer<TValue>
    {
        public int Count => _heap.Count;
        public int Capacity => _heap.Capacity;
        public TComparer Comparator => _heap.Comparator;
        public TValue[] Items
        {
            get
            {
                TValue[] items = new TValue[_heap.Count];
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = _heap.Data->Heap[i].Item;
                }

                return items;
            }
        }

        private NativeHeap<TValue, TComparer> _heap;


        public NativeHeapDebugView(NativeHeap<TValue, TComparer> heap)
        {
            _heap = heap;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TableValue
    {
        public int HeapIndex;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        public int Version;
#endif
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct HeapData<TValue, TComparer> where TValue : unmanaged
    {
        public int Count;
        public int Capacity;
        public HeapNode<TValue>* Heap;
        public TableValue* Table;
        public TComparer Comparator;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HeapNode<TValue> where TValue : unmanaged
    {
        public TValue Item;
        public int TableIndex;
    }
}
