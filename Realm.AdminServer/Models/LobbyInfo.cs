namespace Realm.AdminServer.Models;

public class LobbyInfo
{
    public required string LobbyId { get; set; }
    public required string Map { get; set; }
    public required string HostIP { get; set; }
    public required int HostPort { get; set; }
    public required string NatType { get; set; }
    public required string PasswordHash { get; set; }
    public required int MaxPlayers { get; set; }
    public required int SlotsUsed { get; set; }
    public string? GameVersion { get; set; }
    public string? MapVersion { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public string? OriginServerUri { get; set; }
    public string? HostToken { get; set; }
    public int HostPingBaseline { get; set; }
    public string? LocalIP { get; set; }
    public long MapSizeBytes { get; set; }
    public bool IsGameInProgress { get; set; }
}
