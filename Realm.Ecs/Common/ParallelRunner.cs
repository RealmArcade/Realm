using System;
using System.Threading.Tasks;

namespace Realm.Ecs.Common;

public static class ParallelRunner
{
    public const int DefaultParallelThreshold = 64;

    public static void Execute(int count, Action<int> body, int threshold = DefaultParallelThreshold)
    {
        if (count <= 0)
            return;

        if (count >= threshold)
        {
            Parallel.For(0, count, body);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                body(i);
            }
        }
    }
}
