namespace cochca.Models;

public class TurnCredentials
{
    public string Username { get; set; } = string.Empty;
    public string Credential { get; set; } = string.Empty;
    public string[] Urls { get; set; } = Array.Empty<string>();
    public long ExpiresAt { get; set; }
}
