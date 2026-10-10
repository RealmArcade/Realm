namespace Realm.Shared.Distribution;

public class SeederCircuitState
{
	private int _consecutiveFailures;
	private DateTime _openUntilUtc = DateTime.MinValue;

	public int ConsecutiveFailures => _consecutiveFailures;
	public DateTime OpenUntilUtc => _openUntilUtc;

	public bool IsOpen(DateTime nowUtc)
	{
		return _consecutiveFailures >= 3 && nowUtc < _openUntilUtc;
	}

	public void RecordSuccess()
	{
		System.Threading.Interlocked.Exchange(ref _consecutiveFailures, 0);
		_openUntilUtc = DateTime.MinValue;
	}

	public void RecordFailure(DateTime nowUtc)
	{
		int failures = System.Threading.Interlocked.Increment(ref _consecutiveFailures);
		if (failures >= 3)
		{
			int exponent = Math.Min(failures - 3, 5);
			int backoffSeconds = Math.Min(60, (int)Math.Pow(2, exponent) * 5);
			_openUntilUtc = nowUtc.AddSeconds(backoffSeconds);
		}
	}

	public void Reset()
	{
		System.Threading.Interlocked.Exchange(ref _consecutiveFailures, 0);
		_openUntilUtc = DateTime.MinValue;
	}
}