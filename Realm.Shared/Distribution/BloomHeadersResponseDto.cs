namespace Realm.Shared.Distribution;

public class BloomHeadersResponseDto
{
	public string SeederId { get; set; } = string.Empty;
	public int BitCount { get; set; }
	public int HashCount { get; set; }
	public int ItemCount { get; set; }
	public string FilterDataBase64 { get; set; } = string.Empty;
}