
using System.Security.Cryptography;
using System.Text;

namespace Sentinel.Api.Authentication;

public sealed class AgentWorkerAuthenticator
{
    private readonly byte[] _expectedHash;

    public AgentWorkerAuthenticator(
        IConfiguration configuration)
    {
        var configured = configuration["AgentWorker:ApiKey"]
            ?? throw new InvalidOperationException(
                "Agent worker API key is missing.");

        byte[] key;

        try
        {
            key = Convert.FromBase64String(configured);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "Agent worker API key must be Base64.", ex);
        }

        if (key.Length < 32)
            throw new InvalidOperationException(
                "Agent worker API key is too short.");

        _expectedHash = SHA256.HashData(key);

        CryptographicOperations.ZeroMemory(key);
    }

    public bool IsValid(string? provided)
    {
        if (string.IsNullOrWhiteSpace(provided))
            return false;

        byte[] supplied;

        try
        {
            supplied = Convert.FromBase64String(provided);
        }
        catch (FormatException)
        {
            return false;
        }

        if (supplied.Length < 32)
            return false;

        var suppliedHash = SHA256.HashData(supplied);

        var matches =
            CryptographicOperations.FixedTimeEquals(
                suppliedHash, _expectedHash);

        CryptographicOperations.ZeroMemory(supplied);
        CryptographicOperations.ZeroMemory(suppliedHash);

        return matches;
    }
}
