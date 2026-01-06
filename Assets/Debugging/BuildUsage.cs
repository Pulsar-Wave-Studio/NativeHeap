using Amarcolina.NativeHeap;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;


// Make sure this script is used in a scene to avoid the NativeHeap being stripped from a build.
public class BuildUsage : MonoBehaviour
{
    private UsageJob _job;
    private JobHandle _handle;
    private bool _isJobScheduled;


    void Start()
    {
        _job = new UsageJob()
        {
            heap = new NativeHeap<int, Min>(Allocator.TempJob)
        };
        _handle = _job.Schedule();
        JobHandle.ScheduleBatchedJobs();

        _isJobScheduled = true;
    }

    private void Update()
    {
        if (!_isJobScheduled)
        {
            return;
        }

        _handle.Complete();
        _handle = default;

        Debug.Log($"Heap contents: {_job.heap.Pop()}");
        _job.heap.Dispose();
        _job = default;

        _isJobScheduled = false;
    }

    [BurstCompile(CompileSynchronously = true)]
    public struct UsageJob : IJob
    {
        public NativeHeap<int, Min> heap;

        public void Execute()
        {
            heap.Insert(42);
        }
    }
}
