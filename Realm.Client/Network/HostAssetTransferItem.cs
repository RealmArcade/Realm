namespace Realm.Client.Network;

public struct HostAssetTransferItem
{
	public string AssetKey;
	public string SourceFilePath;
	public string? Metadata;
	public long Size;
}