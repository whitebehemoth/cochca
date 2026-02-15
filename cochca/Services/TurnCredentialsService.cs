using System.Security.Cryptography;
using System.Text;
using cochca.Models;

namespace cochca.Services;

public class TurnCredentialsService
{
    private readonly string _turnPassword;
    private readonly string _turnDomain;
    private readonly int _credentialTtlSeconds = 3600; // 1 hour

    public TurnCredentialsService(IConfiguration configuration)
    {
        _turnPassword = configuration["TurnServer:Password"] ?? throw new InvalidOperationException("TurnServer:Password not configured");
        _turnDomain = configuration["TurnServer:Domain"] ?? "turn.example.com";
    }

    public TurnCredentials GenerateCredentials()
    {
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(_credentialTtlSeconds).ToUnixTimeSeconds();
        var username = $"{expiresAt}:cochca";
        
        // Generate HMAC-SHA1 credential (coturn standard with static-auth-secret)
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_turnPassword));
        var credentialBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(username));
        var credential = Convert.ToBase64String(credentialBytes);

        return new TurnCredentials
        {
            Username = username,
            Credential = credential,
            Urls = new[]
            {
                $"turn:{_turnDomain}:3478",
                $"turn:{_turnDomain}:3478?transport=tcp",
                $"turns:{_turnDomain}:5349?transport=tcp"
            },
            ExpiresAt = expiresAt
        };
    }
}
