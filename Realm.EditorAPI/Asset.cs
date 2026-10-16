using System.Collections.Generic;

namespace Realm.EditorAPI;

/// <summary>
/// Represents an indexed asset in the project index or content addressable storage.
/// </summary>
public class Asset
{
	/// <summary>
	/// Gets or sets the file path of the asset.
	/// </summary>
	public string FilePath { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the file name including extension.
	/// </summary>
	public string FileName { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the file extension with leading dot.
	/// </summary>
	public string Extension { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the BLAKE3 hash identifier of the asset content.
	/// </summary>
	public string Blake3 { get; set; } = string.Empty;

	/// <summary>
	/// Gets or sets the categorized asset type.
	/// </summary>
	public AssetType AssetType { get; set; }

	/// <summary>
	/// Gets or sets the descriptive tags associated with this asset.
	/// </summary>
	public List<string> Tags { get; set; } = new();

	/// <summary>
	/// Initializes a new instance of the <see cref="Asset"/> class.
	/// </summary>
	public Asset()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="Asset"/> class with properties.
	/// </summary>
	/// <param name="filePath">File path of the asset.</param>
	/// <param name="fileName">File name of the asset.</param>
	/// <param name="extension">File extension of the asset.</param>
	/// <param name="blake3">BLAKE3 hash of the asset.</param>
	/// <param name="assetType">Asset type category.</param>
	/// <param name="tags">Descriptive tags for the asset.</param>
	public Asset(string filePath, string fileName, string extension, string blake3, AssetType assetType, List<string>? tags = null)
	{
		FilePath = filePath;
		FileName = fileName;
		Extension = extension;
		Blake3 = blake3;
		AssetType = assetType;
		Tags = tags ?? new List<string>();
	}
}
