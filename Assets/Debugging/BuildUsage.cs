using Amarcolina.NativeHeap;
using Unity.Collections;
using UnityEngine;


// Make sure this script is used in a scene to avoid the NativeHeap being stripped from a build.
public class BuildUsage : MonoBehaviour
{
    private NativeHeap<int, Min> _heap;


    void Awake()
    {
        _heap = new NativeHeap<int, Min>(Allocator.Persistent);
    }

    void OnDestroy()
    {
        _heap.Dispose();
        _heap = default;
    }
}
