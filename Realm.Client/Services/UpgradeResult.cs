using System.Collections.Generic;

namespace Realm.Client.Services;

public class UpgradeResult
{
	public bool Success { get; set; }
	public string? ErrorMessage { get; set; }
	public string InitialVersion { get; set; } = string.Empty;
	public string FinalVersion { get; set; } = string.Empty;
	public List<MigrationResult> StepResults { get; set; } = new();
}