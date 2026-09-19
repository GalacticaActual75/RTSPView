using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RTSPView.Controller;

// Stores only hashes. All methods are called under the shared configuration gate.
public sealed class ConnectorPairing
{
    private sealed record Binding(string InstanceId, string TokenHash, string SessionVersion);
    private readonly string _path;
    private string? _codeHash;
    private DateTimeOffset _expires;
    private string? _sessionVersion;
    private Binding? _binding;
    private readonly TimeProvider _clock;
    public ConnectorPairing(string directory, TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _path = Path.Combine(directory, "scrypted-connector.json");
        if (File.Exists(_path))
        {
            try { _binding = JsonSerializer.Deserialize<Binding>(File.ReadAllText(_path)); }
            catch { _binding = null; }
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Matches(string? expected, string value) => expected is not null && value.Length <= 256 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(Hash(value)));
    public object Status(string sessionVersion) => new { paired = _binding?.SessionVersion == sessionVersion,
        instanceId = _binding?.SessionVersion == sessionVersion ? _binding.InstanceId : null };
    public object CreateCode(string sessionVersion)
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        _codeHash = Hash(code); _sessionVersion = sessionVersion; _expires = _clock.GetUtcNow().AddMinutes(5);
        return new { code, expiresAt = _expires };
    }
    public async Task<string?> PairAsync(string code, string instanceId, string sessionVersion)
    {
        if (!Guid.TryParse(instanceId, out _) || _sessionVersion != sessionVersion || _clock.GetUtcNow() >= _expires || !Matches(_codeHash, code)) return null;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var binding = new Binding(instanceId, Hash(token), sessionVersion);
        await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(binding));
        File.Move(_path + ".tmp", _path, true);
        _binding = binding; _codeHash = null;
        return token;
    }
    public bool Authorize(string token, string instanceId, string sessionVersion) =>
        _binding?.SessionVersion == sessionVersion && _binding.InstanceId == instanceId && Matches(_binding.TokenHash, token);
    public void Revoke()
    {
        File.Delete(_path); _binding = null; _codeHash = null;
    }
}
