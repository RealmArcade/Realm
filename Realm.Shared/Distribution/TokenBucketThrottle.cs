namespace Realm.Shared.Distribution;

public class TokenBucketThrottle
{
	private readonly long _maximumCapacity;
	private readonly double _fillRatePerSecond;
	private double _availableTokens;
	private DateTime _lastRefillUtc;
	private readonly object _syncLock = new();

	public TokenBucketThrottle(long maximumCapacity, double fillRatePerSecond)
	{
		_maximumCapacity = maximumCapacity;
		_fillRatePerSecond = fillRatePerSecond;
		_availableTokens = maximumCapacity;
		_lastRefillUtc = DateTime.UtcNow;
	}

	/// <summary>
	///     Takes tokens, waiting for them to refill before returning. The wait happens outside the lock: a thread that
	///     slept while holding it would stall every other caller for the whole wait, and the bucket's refill clock would be
	///     frozen for exactly the time it was asked to wait.
	/// </summary>
	public async Task ConsumeAsync(long byteCount, CancellationToken cancellationToken = default)
	{
		if (byteCount <= 0 || _fillRatePerSecond <= 0)
		{
			return;
		}

		while (!cancellationToken.IsCancellationRequested)
		{
			int millisecondsToDelay = 0;
			lock (_syncLock)
			{
				Refill();
				if (_availableTokens >= byteCount)
				{
					_availableTokens -= byteCount;
					return;
				}

				double neededTokens = byteCount - _availableTokens;
				double secondsToWait = neededTokens / _fillRatePerSecond;
				millisecondsToDelay = Math.Max(1, (int)(secondsToWait * 1000));
			}

			if (millisecondsToDelay > 0)
			{
				await Task.Delay(millisecondsToDelay, cancellationToken);
			}
		}
	}

	private void Refill()
	{
		var nowUtc = DateTime.UtcNow;
		double elapsedSeconds = (nowUtc - _lastRefillUtc).TotalSeconds;
		_lastRefillUtc = nowUtc;
		_availableTokens = Math.Min(_maximumCapacity, _availableTokens + (elapsedSeconds * _fillRatePerSecond));
	}
}