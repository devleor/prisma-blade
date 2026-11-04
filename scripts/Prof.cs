using System.Collections.Generic;
using System.Diagnostics;

namespace Prisma;

/// <summary>
/// A tiny opt-in profiler for the stress check: adds up the wall time of named sections (sim step, each _Draw).
/// Off by default, and then it costs one bool check per section.
/// </summary>
public static class Prof
{
    public static bool On;
    public static readonly Dictionary<string, double> Ms = [];
    public static readonly Dictionary<string, int> Calls = [];
    static readonly Stopwatch clock = Stopwatch.StartNew();

    public static long Begin() => On ? clock.ElapsedTicks : 0;

    public static void End(string name, long start)
    {
        if (!On) return;
        double ms = (clock.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;
        Ms[name] = Ms.GetValueOrDefault(name) + ms;
        Calls[name] = Calls.GetValueOrDefault(name) + 1;
    }
}
