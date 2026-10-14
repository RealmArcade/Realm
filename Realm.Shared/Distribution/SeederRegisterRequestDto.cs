namespace Realm.Shared.Distribution;

public class SeederRegisterRequestDto
{
	public string SeederId { get; set; } = string.Empty;
	public string ReportedIP { get; set; } = string.Empty;
	public int Port { get; set; }
	public int CapacityPercentage { get; set; } = 100;
	public bool AcceptingUploads { get; set; } = true;
	public List<string> MapIds { get; set; } = new();
}