using Realm.Shared.Metadata;

namespace Realm.Tools.Cli;

public static class CliAssetAgreement
{
	public static bool Accepted { get; set; }

	public static void EnsureAccepted()
	{
		if (Accepted) return;

		Console.WriteLine($"{RealmMetadataHelper.AssetAgreementWarning} Y/N");
		string? response = Console.ReadLine()?.Trim();
		if (string.Equals(response, "Y", StringComparison.OrdinalIgnoreCase))
		{
			Accepted = true;
		}
		else
		{
			Console.WriteLine("Task cancelled.");
			Environment.Exit(0);
		}
	}
}
