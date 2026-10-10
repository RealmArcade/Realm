using System;
using System.Threading;

namespace Realm.Client.Network;

public class TokenBucket
{
	private readonly long _maxCapacity;
	private readonly double _fillRatePerSecond;
	private double _tokens;
	private DateTime _lastRefill;
	private readonly object _lock = new();

	public TokenBucket(long maxCapacity, double fillRatePerSecond)
	{
		_maxCapacity = maxCapacity;
		_fillRatePerSecond = fillRatePerSecond;
		_tokens = maxCapacity;
		_lastRefill = DateTime.UtcNow;
	}

	public void Consume(long amount)
	{
		while (true)
		{
			lock (_lock)
			{
				Refill();
				if (_tokens >= amount)
				{
					_tokens -= amount;
					return;
				}
				double needed = amount - _tokens;
				double sleepSecs = needed / _fillRatePerSecond;
				int sleepMs = Math.Max(1, (int)(sleepSecs * 1000));
				Thread.Sleep(sleepMs);
			}
		}
	}

	private void Refill()
	{
		var now = DateTime.UtcNow;
		double elapsed = (now - _lastRefill).TotalSeconds;
		_lastRefill = now;
		_tokens = Math.Min(_maxCapacity, _tokens + elapsed * _fillRatePerSecond);
	}
}