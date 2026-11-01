using System;

namespace Realm.Client.Services.MapHistory;

public readonly record struct MapHistoryGranularityBucket(TimeSpan Granularity, TimeSpan MaximumAge);

public static class MapHistoryPolicy
{
	public const int AutosaveIntervalMinutes = 5;

	public const int MinimumDroppedVersionsPerCondense = 10;

	public static readonly MapHistoryGranularityBucket[] GranularityBuckets =
	{
		new(TimeSpan.FromMinutes(5), TimeSpan.FromHours(24)),
		new(TimeSpan.FromHours(1), TimeSpan.FromDays(3)),
		new(TimeSpan.FromDays(1), TimeSpan.FromDays(90)),
		new(TimeSpan.FromDays(7), TimeSpan.FromDays(365)),
		new(TimeSpan.FromDays(30), TimeSpan.FromDays(730)),
		new(TimeSpan.FromDays(365), TimeSpan.MaxValue)
	};

	public static TimeSpan GetGranularity(TimeSpan age)
	{
		foreach (MapHistoryGranularityBucket bucket in GranularityBuckets)
		{
			if (age <= bucket.MaximumAge)
			{
				return bucket.Granularity;
			}
		}

		return GranularityBuckets[^1].Granularity;
	}
}
