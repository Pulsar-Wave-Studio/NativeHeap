# Native Heap

This repo contains a heap implementation for use with the Unity Burst compiler.

This implementation allows you to remove items from the center of the collection with the same cost as removing them in-order. When an item is added, a special `Index` is returned that can later be used to remove the item.

### Parameterization
The NativeHeap is generic like NativeArray<T> or NativeList<T>, allowing you to store structs of type T ín the collection. The second parameter is the `Comparator` and is responsible for determining the ordering of the items inside the heap.

Two comparators are included, the Min and Max comparator, which are able to construct a MinHeap or a MaxHeap for the built-in csharp primitives such as `int` and `float`. Writing your own comparator is as simple as implementing the IComparer<T> interface for the type you would like to insert.

### Example Usage
Basic example
```csharp
//Construct a new MinHeap for integers
var heap = new NativeHeap<int, Min>(Allocator.Temp);

//Insert some numbers into the heap
heap.Insert(5);
heap.Insert(3);
heap.Insert(10);

print(heap.Pop()); //3
print(heap.Pop()); //5
print(heap.Pop()); //10

//Always remember to dispose when you are finished!
heap.Dispose();
```

Custom comparator example
```csharp
//Define a custom comparator that orders floats by their distance to the 
//constant 100
public struct DistanceTo100 : IComparer<float> {
    public int Compare(float a, float b) {
        float distForA = Mathf.Abs(a - 100.0f);
        float distForB = Mathf.Abs(b - 100.0f);
        return distForA.CompareTo(distForB);
    }
}

var heap = new NativeHeap<float, DistanceTo100>(Allocator.Temp);
...
```

Using an `Index` to remove an item
```csharp
var heap = new NativeHeap<int, Min>(Allocator.Temp);

heap.Insert(5);
heap.Insert(3);
heap.Insert(10);

NativeHeapIndex indexOf7 = heap.Insert(7);

print(heap.Pop()); //3

//Remove the item 7 from the heap, even though it is
//not next up
heap.Remove(indexOf7);

print(heap.Pop()); //5
print(heap.Pop()); //10

heap.Dispose();
```

### License

This project is based on work by [Alex Marcolina (MIT License)](https://github.com/Amarcolina/NativeHeap).
Modifications and additional code © 2026 [Pulsar Wave Studio](https://github.com/Pulsar-Wave-Studio/NativeHeap), also licensed under the [MIT License](https://github.com/Pulsar-Wave-Studio/NativeHeap/blob/master/LICENSE).
