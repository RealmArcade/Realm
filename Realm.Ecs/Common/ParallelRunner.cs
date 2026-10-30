using System;
using System.Threading.Tasks;

namespace Realm.Ecs.Common;

/// <summary>
///     Runs a fixed set of work items, serially below the threshold and as a fork-join above it. A caller that names the
///     world it is reading has the world marked as forked for the duration, so a structural write attempted by one of
///     the workers is reported as the defect it is rather than corrupting a buffer they are reading.
/// </summary>
public static class ParallelRunner
{
    public const int DefaultParallelThreshold = 64;

    public static void Execute(int count, Action<int> body, int threshold = DefaultParallelThreshold)
    {
        if (count <= 0)
            return;

        if (count < threshold)
        {
            for (int i = 0; i < count; i++)
            {
                body(i);
            }

            return;
        }

        Parallel.For(0, count, body);
    }

    public static void Execute(int count, Action<int> body, EcsWorld workerLeaseWorld, int threshold = DefaultParallelThreshold)
    {
        if (count <= 0)
            return;

        if (count < threshold)
        {
            for (int i = 0; i < count; i++)
            {
                body(i);
            }

            return;
        }

        workerLeaseWorld.EnterWorkerLease();
        try
        {
            Parallel.For(0, count, body);
        }
        finally
        {
            workerLeaseWorld.ExitWorkerLease();
        }
    }
}
