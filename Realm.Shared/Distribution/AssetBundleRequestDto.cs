namespace Realm.Shared.Distribution;

/// <summary>
/// Data transfer object for requesting a batched bundle of assets by their Blake3 hashes.
/// </summary>
public class AssetBundleRequestDto
{
    /// <summary>
    /// Gets or sets the list of normalized Blake3 asset hashes requested in the bundle.
    /// </summary>
    public List<string> Hashes { get; set; } = new();
}