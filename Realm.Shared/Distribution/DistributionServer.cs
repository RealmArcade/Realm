using System.Net;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public partial class DistributionServer
	{
	private readonly ContentAddressableStorage _storage;
	private readonly string _seederId;
	private readonly int _capacityPercentage;
	private readonly TokenBucketThrottle? _throttle;
	private readonly string? _adminPublicKeyBase64;
	private readonly Func<string, string, bool>? _greenlightChecker;
	private HttpListener? _listener;
	private bool _isRunning;
	private CancellationTokenSource? _cancellationTokenSource;

	public string SeederId => _seederId;
	public int CapacityPercentage => _capacityPercentage;
	public ContentAddressableStorage Storage => _storage;
	public int BoundPort { get; private set; }

	public DistributionServer(
		ContentAddressableStorage storage,
		string seederId,
		int capacityPercentage = 100,
		TokenBucketThrottle? throttle = null,
		string? adminPublicKeyBase64 = null,
		Func<string, string, bool>? greenlightChecker = null)
	{
		_storage = storage;
		_seederId = seederId;
		_capacityPercentage = capacityPercentage;
		_throttle = throttle;
		_adminPublicKeyBase64 = adminPublicKeyBase64;
		_greenlightChecker = greenlightChecker;
	}

}
