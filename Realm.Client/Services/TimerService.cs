using System;
using System.Collections.Generic;

namespace Realm.Client.Services
{
	public class TimerService
	{
		public int NextTimerHandle { get; set; }
		public Dictionary<int, (float Interval, float Remaining, bool Repeating, Action Callback)> ScheduledTimers { get; } = new();
	}
}
