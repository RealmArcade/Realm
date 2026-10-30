using System.Collections.Generic;

namespace Realm.Client.Services;

public record AssetIndexVerificationResult(
	int CheckedCount,
	int TotalAssetCount,
	string Strategy,
	IReadOnlyList<string> CheckedFilePaths,
	IReadOnlyList<AssetIndexVerificationFailure> Failures)
{
	public bool IsHealthy => Failures.Count == 0;
}
