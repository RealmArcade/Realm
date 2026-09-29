namespace Realm.AdminServer.Models;

public class UpdatePlayerUsernameRequest
{
    public string PlayerId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string? Token { get; set; }
}
