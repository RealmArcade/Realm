namespace Realm.AdminServer.Models;

public record SeederRegisterRequest(
	string SeederId,
	string? ReportedIP,
	int Port,
	List<string> MapIds,
	int CapacityPercentage = 100,
	bool AcceptingUploads = true
);