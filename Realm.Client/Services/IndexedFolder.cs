using System;

namespace Realm.Client.Services;

public class IndexedFolder
{
	public int Id { get; set; }
	public string DirectoryPath { get; set; } = string.Empty;
	public DateTime LastScannedUtc { get; set; }
}