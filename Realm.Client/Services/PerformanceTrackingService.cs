using System.Collections.Generic;
using System.Diagnostics;

namespace Realm.Client.Services;

public class PerformanceTrackingService
{
    public Stopwatch TrackerTickStopwatch { get; set; } = new Stopwatch();
    public Stopwatch TrackerIntervalStopwatch { get; set; } = new Stopwatch();
    public List<float> TrackerTickDurations { get; set; } = new List<float>();
    public List<float> TrackerApiDurations { get; set; } = new List<float>();
    public float TrackerLastTickDelay { get; set; }
}
